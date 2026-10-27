using Bindings = CanonPlugins.Shared.PluginInput;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CanonPlugins.Shared;
using HarmonyLib;
using MainGameUiInputCapture;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace MainGameFaceSweatControl
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInDependency(MainGameUiInputCapture.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        public const string Guid = "canon.maingame.facesweatcontrol";
        public const string PluginName = "MainGameFaceSweatControl";
        public const string Version = "1.0.0";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly MethodInfo UpdateSiruMethod = AccessTools.Method(typeof(ChaControl), "UpdateSiru", new[] { typeof(bool) });
        private static readonly FieldInfo CustomMatFaceField = FindFieldQuietly(typeof(ChaControl), "customMatFace");
        private static readonly int OverColor2Id = ResolveChaShaderId("_overcolor2");
        private static readonly int[] LiquidShaderIds = ResolveLiquidShaderIds();

        private static Plugin _instance;
        private static ManualLogSource _log;
        private const string InputOwnerKey = "canon.maingame.facesweatcontrol";
        private const string InputSourceWindow = "window";

        private readonly object _fileLock = new object();
        private readonly List<ChaControl> _targets = new List<ChaControl>();
        private readonly Rect[] _inputBlockRects = new Rect[1];

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<KeyboardShortcut> _toggleUiKey;
        private ConfigEntry<bool> _openUiButton;
        private ConfigEntry<bool> _applyFemale;
        private ConfigEntry<bool> _applyMale;
        private ConfigEntry<int> _defaultFaceSweatLevel;
        private ConfigEntry<float> _defaultCheekRate;
        private ConfigEntry<bool> _autoApplyOnSliderChange;
        private ConfigEntry<bool> _verboseLog;

        private string _pluginDir = string.Empty;
        private string _logPath = string.Empty;
        private Rect _windowRect = new Rect(120f, 120f, 360f, 310f);
        private Vector2 _scroll;
        private bool _showUi;
        private bool _windowPointerActive;
        private bool _inputCaptureHeld;
        private int _selectedIndex;
        private int _faceSweatLevel;
        private float _cheekRate;
        private float _nextRefreshAt;

        private void Awake()
        {
            _instance = this;
            _log = Logger;
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            _logPath = Path.Combine(_pluginDir, PluginName + ".log");
            InitLogFile();
            BindConfig();
            _faceSweatLevel = Mathf.Clamp(_defaultFaceSweatLevel.Value, 0, 2);
            _cheekRate = Mathf.Clamp01(_defaultCheekRate.Value);
            WriteInfo("started version=" + Version);
        }

        // BlankMapAdd の追加マップ以外では一切発火させない。
        private bool _gateWasInRoom;

        private void Update()
        {
            // 汗の値は本体が毎フレーム書き戻すので、上書きをやめるだけで元に戻る。
            // 残るのは自前UIと入力占有だけなので、それだけ畳む。
            if (!BlankRoomGate.Tick(ref _gateWasInRoom, OnBlankRoomLeft)) return;

            if (!_enabled.Value) return;

            if (Bindings.Down("MainGameFaceSweatControl.toggle", _toggleUiKey.Value.ToString()))
            {
                _showUi = !_showUi;
                if (_showUi) RefreshTargets("toggle-key");
            }

            if (_openUiButton.Value)
            {
                _openUiButton.Value = false;
                _showUi = true;
                RefreshTargets("config-button");
            }

            if (_showUi && Time.unscaledTime >= _nextRefreshAt)
            {
                _nextRefreshAt = Time.unscaledTime + 1f;
                RefreshTargets("periodic");
            }

            UpdateInputCapture();
        }

        private void OnGUI()
        {
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (!_enabled.Value || !_showUi)
            {
                ReleaseInputCapture();
                return;
            }

            Vector2 mouse = Event.current != null ? Event.current.mousePosition : Vector2.zero;
            _windowPointerActive = _windowRect.Contains(mouse);
            _windowRect = GUILayout.Window(GetInstanceID(), _windowRect, DrawWindow, PluginName);
            UpdateInputCapture();
        }

        private void OnDestroy()
        {
            ReleaseInputCapture();
        }

        public static bool ApplyToCharacter(ChaControl cha, int faceSweatLevel, float cheekRate, string source)
        {
            if (_instance == null || cha == null) return false;
            return _instance.ApplyFaceState(cha, faceSweatLevel, cheekRate, source);
        }

        public static bool ClearCharacter(ChaControl cha, string source)
        {
            return ApplyToCharacter(cha, 0, 0f, source);
        }

        private void BindConfig()
        {
            _enabled = Bind("General", "Enabled", true, "プラグイン全体の有効/無効", 100);
            _toggleUiKey = Bind("General", "Toggle UI Key", new KeyboardShortcut(KeyCode.F9), "ゲーム内UIを開閉するキー", 90);
            _openUiButton = BindButton("General", "Open UI", "Config Managerからゲーム内UIを開く", 80, OpenUiFromConfigManager);
            _applyFemale = Bind("Target", "Apply Female", true, "女性キャラを対象にする", 70);
            _applyMale = Bind("Target", "Apply Male", false, "男性キャラを対象にする", 60);
            _defaultFaceSweatLevel = Bind("Control", "Default Face Sweat Level", 0, "UI初期値。0-2", 50, new AcceptableValueRange<int>(0, 2));
            _defaultCheekRate = Bind("Control", "Default Cheek Rate", 0f, "UI初期値。0-1", 40, new AcceptableValueRange<float>(0f, 1f));
            _autoApplyOnSliderChange = Bind("Control", "Auto Apply On Slider Change", false, "UIスライダー変更時に即時適用する", 30);
            _verboseLog = Bind("Debug", "Verbose Log", false, "適用内容をログに出す", 20);
        }

        private ConfigEntry<T> Bind<T>(string section, string key, T value, string description, int order, AcceptableValueBase acceptable = null)
        {
            return Config.Bind(section, key, value, new ConfigDescription(description, acceptable, BuildConfigAttributes(order, false, null)));
        }

        private ConfigEntry<bool> BindButton(string section, string key, string description, int order, Action action)
        {
            return Config.Bind(section, key, false, new ConfigDescription(description, null, BuildConfigAttributes(order, false, action)));
        }

        private void OpenUiFromConfigManager()
        {
            _showUi = true;
            RefreshTargets("config-action");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
                RefreshTargets("ui-refresh");
            if (GUILayout.Button("Close", GUILayout.Width(90f)))
                _showUi = false;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Target");
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(92f));
            if (_targets.Count == 0)
            {
                GUILayout.Label("No ChaControl targets");
            }
            else
            {
                for (int i = 0; i < _targets.Count; i++)
                {
                    ChaControl cha = _targets[i];
                    string label = BuildTargetLabel(i, cha);
                    bool selected = i == _selectedIndex;
                    bool next = GUILayout.Toggle(selected, label, "Button");
                    if (next && !selected)
                    {
                        _selectedIndex = i;
                        ReadCurrentFromTarget();
                    }
                }
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6f);
            int prevSweat = _faceSweatLevel;
            float prevCheek = _cheekRate;

            GUILayout.Label("Face Sweat Level: " + _faceSweatLevel);
            _faceSweatLevel = Mathf.RoundToInt(GUILayout.HorizontalSlider(_faceSweatLevel, 0f, 2f));

            GUILayout.Label("Cheek Rate: " + _cheekRate.ToString("0.000"));
            _cheekRate = GUILayout.HorizontalSlider(_cheekRate, 0f, 1f);

            if (_autoApplyOnSliderChange.Value && (prevSweat != _faceSweatLevel || Mathf.Abs(prevCheek - _cheekRate) > 0.0005f))
                ApplySelected("ui-auto");

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply"))
                ApplySelected("ui-apply");
            if (GUILayout.Button("Clear"))
            {
                _faceSweatLevel = 0;
                _cheekRate = 0f;
                ApplySelected("ui-clear");
            }
            if (GUILayout.Button("Read"))
                ReadCurrentFromTarget();
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.Label("Selected: " + BuildSelectedStatus());

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void OnBlankRoomLeft()
        {
            _showUi = false;
            ReleaseInputCapture();
        }

        private void UpdateInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable) return;

            bool active = _enabled.Value && _showUi;
            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, active);

            if (active)
            {
                _inputBlockRects[0] = _windowRect;
                UiInputCaptureApi.SetInputBlockRects(InputOwnerKey, _inputBlockRects);
                UiInputCaptureApi.Sync(InputOwnerKey, InputSourceWindow, _windowPointerActive);
                _inputCaptureHeld = true;
            }
            else
            {
                ReleaseInputCapture();
            }
        }

        private void ReleaseInputCapture()
        {
            if (!_inputCaptureHeld || !UiInputCaptureApi.IsAvailable) return;
            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, false);
            UiInputCaptureApi.ClearInputBlockRects(InputOwnerKey);
            UiInputCaptureApi.EndOwner(InputOwnerKey);
            _inputCaptureHeld = false;
        }

        private void RefreshTargets(string source)
        {
            ChaControl selected = GetSelectedTarget();
            _targets.Clear();

            ChaControl[] all;
            try
            {
                all = FindObjectsOfType<ChaControl>();
            }
            catch (Exception ex)
            {
                WriteWarn("refresh failed source=" + source + " error=" + ex.Message);
                return;
            }

            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (!IsTarget(cha)) continue;
                _targets.Add(cha);
            }

            _selectedIndex = selected != null ? _targets.IndexOf(selected) : -1;

        }

        private bool IsTarget(ChaControl cha)
        {
            if (cha == null || cha.fileStatus == null) return false;

            int sex = -1;
            try { sex = cha.sex; } catch { }
            if (sex == 0 && !_applyMale.Value) return false;
            if (sex == 1 && !_applyFemale.Value) return false;

            return cha.gameObject != null && cha.gameObject.activeInHierarchy;
        }

        private void ApplySelected(string source)
        {
            ChaControl cha = GetSelectedTarget();
            if (cha == null)
            {
                WriteWarn("apply skipped source=" + source + " reason=no_target");
                return;
            }

            ApplyFaceState(cha, _faceSweatLevel, _cheekRate, source);
        }

        private bool ApplyFaceState(ChaControl cha, int faceSweatLevel, float cheekRate, string source)
        {
            if (cha == null || cha.fileStatus == null) return false;

            int sweat = Mathf.Clamp(faceSweatLevel, 0, 2);
            float cheek = Mathf.Clamp01(cheekRate);

            try
            {
                SetFaceSweat(cha, sweat);
                SetCheek(cha, cheek);
                if (_verboseLog.Value)
                {
                    WriteInfo(
                        "apply source=" + source
                        + " cha=" + cha.name
                        + " sweat=" + sweat
                        + " cheek=" + cheek.ToString("0.###"));
                }
                return true;
            }
            catch (Exception ex)
            {
                WriteWarn("apply failed source=" + source + " cha=" + cha.name + " error=" + ex);
                return false;
            }
        }

        private static void SetFaceSweat(ChaControl cha, int level)
        {
            byte value = (byte)Mathf.Clamp(level, 0, 2);
            float normalized = Mathf.Clamp01(value / 2f);
            cha.SetSiruFlags(ChaFileDefine.SiruParts.SiruKao, value);

            if (cha.fileStatus != null && cha.fileStatus.siruLv != null)
            {
                int idx = (int)ChaFileDefine.SiruParts.SiruKao;
                if (idx >= 0 && idx < cha.fileStatus.siruLv.Length)
                    cha.fileStatus.siruLv[idx] = value;

                cha.fileStatus.skinTuyaRate = normalized;
                cha.ChangeSettingSkinGlossPower();
            }

            if (UpdateSiruMethod != null)
                UpdateSiruMethod.Invoke(cha, new object[] { true });

            SetLiquidShaderLevel(cha, "_liquidface", value);
        }

        private static void SetCheek(ChaControl cha, float value)
        {
            float cheek = Mathf.Clamp01(value);
            cha.fileStatus.hohoAkaRate = cheek;
            cha.ChangeHohoAkaRate(cheek);

            float targetAlpha = cheek;
            Material customFace = CustomMatFaceField != null ? CustomMatFaceField.GetValue(cha) as Material : null;
            if (customFace != null && OverColor2Id != 0 && customFace.HasProperty(OverColor2Id))
            {
                Color color = customFace.GetColor(OverColor2Id);
                color.a = targetAlpha;
                customFace.SetColor(OverColor2Id, color);
            }

            Renderer[] renderers = cha.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null) continue;

                Material[] materials = renderer.materials;
                for (int m = 0; m < materials.Length; m++)
                {
                    Material mat = materials[m];
                    if (mat == null || OverColor2Id == 0 || !mat.HasProperty(OverColor2Id)) continue;

                    Color color = mat.GetColor(OverColor2Id);
                    color.a = targetAlpha;
                    mat.SetColor(OverColor2Id, color);
                }
            }
        }

        private static void SetLiquidShaderLevel(ChaControl cha, string facePropName, byte level)
        {
            if (cha == null) return;

            int faceId = ResolveChaShaderId(facePropName);
            float value = level;
            Renderer[] renderers = cha.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null) continue;

                Material[] materials = renderer.materials;
                for (int m = 0; m < materials.Length; m++)
                {
                    Material mat = materials[m];
                    if (mat == null) continue;

                    if (faceId != 0 && mat.HasProperty(faceId))
                        mat.SetFloat(faceId, value);

                    if (level == 0)
                    {
                        for (int i = 0; i < LiquidShaderIds.Length; i++)
                        {
                            int id = LiquidShaderIds[i];
                            if (id != 0 && mat.HasProperty(id))
                                mat.SetFloat(id, 0f);
                        }
                    }
                }
            }
        }

        private void ReadCurrentFromTarget()
        {
            ChaControl cha = GetSelectedTarget();
            if (cha == null || cha.fileStatus == null) return;

            try
            {
                _cheekRate = Mathf.Clamp01(cha.fileStatus.hohoAkaRate);
                int idx = (int)ChaFileDefine.SiruParts.SiruKao;
                if (cha.fileStatus.siruLv != null && idx >= 0 && idx < cha.fileStatus.siruLv.Length)
                    _faceSweatLevel = Mathf.Clamp(cha.fileStatus.siruLv[idx], 0, 2);
            }
            catch (Exception ex)
            {
                WriteWarn("read failed cha=" + cha.name + " error=" + ex.Message);
            }
        }

        private ChaControl GetSelectedTarget()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _targets.Count) return null;
            return _targets[_selectedIndex];
        }

        private string BuildTargetLabel(int index, ChaControl cha)
        {
            if (cha == null) return index + ": null";
            string sex = "sex?";
            try { sex = cha.sex == 0 ? "M" : cha.sex == 1 ? "F" : cha.sex.ToString(); } catch { }
            return index + ": [" + sex + "] " + cha.name;
        }

        private string BuildSelectedStatus()
        {
            ChaControl cha = GetSelectedTarget();
            if (cha == null) return "none";

            string siru = "?";
            try
            {
                int idx = (int)ChaFileDefine.SiruParts.SiruKao;
                if (cha.fileStatus != null && cha.fileStatus.siruLv != null && idx >= 0 && idx < cha.fileStatus.siruLv.Length)
                    siru = cha.fileStatus.siruLv[idx].ToString();
            }
            catch { }

            string cheek = "?";
            try { cheek = cha.fileStatus != null ? cha.fileStatus.hohoAkaRate.ToString("0.###") : "?"; } catch { }
            return cha.name + " sweat=" + siru + " cheek=" + cheek;
        }

        private static object BuildConfigAttributes(int order, bool advanced, Action action)
        {
            var attrs = new ConfigurationManager.ConfigurationManagerAttributes
            {
                Order = order,
                ReadOnly = action != null,
                HideDefaultButton = action != null
            };

            if (action != null)
            {
                attrs.CustomDrawer = entry =>
                {
                    if (GUILayout.Button(entry.Definition.Key))
                        action();
                };
            }

            return attrs;
        }

        private static int[] ResolveLiquidShaderIds()
        {
            var ids = new List<int>();
            AddChaShaderId(ids, "_liquidface");
            AddChaShaderId(ids, "_liquidftop");
            AddChaShaderId(ids, "_liquidfbot");
            AddChaShaderId(ids, "_liquidbtop");
            AddChaShaderId(ids, "_liquidbbot");
            return ids.ToArray();
        }

        private static void AddChaShaderId(List<int> ids, string fieldName)
        {
            int id = ResolveChaShaderId(fieldName);
            if (id != 0) ids.Add(id);
        }

        private static int ResolveChaShaderId(string fieldName)
        {
            try
            {
                FieldInfo field = FindFieldQuietly(typeof(ChaShader), fieldName);
                if (field == null || field.FieldType != typeof(int)) return 0;
                return (int)field.GetValue(null);
            }
            catch
            {
                return 0;
            }
        }

        private static FieldInfo FindFieldQuietly(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            for (Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, flags);
                if (field != null) return field;
            }
            return null;
        }

        private void InitLogFile()
        {
            if (!_runtimeLoggingEnabled) return;
            try
            {
                Directory.CreateDirectory(_pluginDir);
                File.WriteAllText(_logPath, "[" + DateTime.Now.ToString("HH:mm:ss") + "] === " + PluginName + " " + Version + " started ===" + Environment.NewLine, Utf8NoBom);
            }
            catch
            {
                // BepInEx log still works.
            }
        }

        private void WriteInfo(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            _log?.LogInfo(message);
            AppendLog("Info", message);
        }

        private void WriteWarn(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            _log?.LogWarning(message);
            AppendLog("Warn", message);
        }

        private void AppendLog(string level, string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (string.IsNullOrWhiteSpace(_logPath)) return;
            try
            {
                lock (_fileLock)
                {
                    File.AppendAllText(_logPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [" + level + "] " + message + Environment.NewLine, Utf8NoBom);
                }
            }
            catch
            {
                // ignore file output failure
            }
        }
    }
}
