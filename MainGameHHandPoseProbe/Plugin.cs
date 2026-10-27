using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using MainGameUiInputCapture;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameHHandPoseProbe
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    [BepInDependency(MainGameUiInputCapture.Plugin.Guid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        private const string Guid = "canon.maingame.hscene.handposeprobe";
        private const string PluginName = "MainGameHHandPoseProbe";
        private const string Version = "0.1.0";
        private const int LeftHand = 0;
        private const int RightHand = 1;
        private const int WindowId = 0x48414E44;
        private const string InputOwnerKey = Guid + ".input";
        private const string InputSourceWindow = "window";
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static readonly FieldInfo FiLstFemale = typeof(HSceneProc).GetField("lstFemale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo FiLstMale = typeof(HSceneProc).GetField("lstMale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private ConfigEntry<bool> _cfgUiVisible;
        private Rect _windowRect = new Rect(40f, 40f, 520f, 500f);
        private Vector2 _scroll;
        private string _status = "未実行";
        private int _targetSex;
        private int _targetIndex;
        private int _leftPattern;
        private int _rightPattern;
        private int _lastTargetId;
        private string _leftPatternText = "0";
        private string _rightPatternText = "0";
        private bool _windowPointerActive;
        private bool _windowDragging;
        private string _pluginDir;
        private string _logFilePath;
        private bool _lastCaptureActiveLogged;
        private HSceneProc _cachedUiHSceneProc;
        private List<ChaControl> _cachedUiFemales;
        private List<ChaControl> _cachedUiMales;
        private float _nextUiTargetRefreshAt;

        private void Awake()
        {
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            _logFilePath = Path.Combine(_pluginDir, PluginName + ".log");
            Directory.CreateDirectory(_pluginDir);
            if (_runtimeLoggingEnabled) File.WriteAllText(_logFilePath, "[" + DateTime.Now.ToString("HH:mm:ss") + "] === " + PluginName + " " + Version + " started ===" + Environment.NewLine, Utf8NoBom);

            _cfgUiVisible = Config.Bind("UI", "UiVisible", false, "手ポーズ検証UIを表示する");
            _cfgUiVisible.SettingChanged += (_, __) => { };

            Config.Bind(
                "UI",
                "OpenWindow",
                false,
                new ConfigDescription(
                    "ConfigurationManager上のUI開閉ボタン",
                    null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 100,
                        HideDefaultButton = true,
                        CustomDrawer = DrawOpenWindowButton
                    }));

            Log("awake uiInputCapture=" + UiInputCaptureApi.IsAvailable);
        }

        private void OnGUI()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (!_cfgUiVisible.Value)
                return;

            HandleWindowPointer(Event.current);
            _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, PluginName);
        }

        private bool _gateWasInRoom;

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            // ただの return では駄目。掴んだ入力占有を返さないまま黙ると、
            // 部屋へ戻ってもカメラが動かせないまま残る。退室時は必ず返す。
            if (!BlankRoomGate.Tick(ref _gateWasInRoom, ReleaseInputCapture)) return;
            UpdateInputCapture();
        }

        private void OnDestroy()
        {
            ReleaseInputCapture();
        }

        private void DrawOpenWindowButton(ConfigEntryBase _)
        {
            string label = _cfgUiVisible.Value ? "UIを閉じる" : "UIを開く";
            if (GUILayout.Button(label))
            {
                _cfgUiVisible.Value = !_cfgUiVisible.Value;
                Log("ui visible=" + _cfgUiVisible.Value);
                if (!_cfgUiVisible.Value)
                    ReleaseInputCapture();
            }
        }

        private void DrawWindow(int id)
        {
            HandleWindowDrag(Event.current);

            if (_cachedUiFemales == null || _cachedUiMales == null || Time.unscaledTime >= _nextUiTargetRefreshAt)
            {
                _cachedUiHSceneProc = FindObjectOfType<HSceneProc>();
                _cachedUiFemales = ResolveList(_cachedUiHSceneProc, FiLstFemale, 1);
                _cachedUiMales = ResolveList(_cachedUiHSceneProc, FiLstMale, 0);
                _nextUiTargetRefreshAt = Time.unscaledTime + 0.25f;
            }
            HSceneProc proc = _cachedUiHSceneProc;
            List<ChaControl> females = _cachedUiFemales;
            List<ChaControl> males = _cachedUiMales;
            List<ChaControl> targets = _targetSex == 0 ? females : males;

            GUILayout.BeginVertical();
            GUILayout.Label("Hシーン: " + (proc != null ? "検出中" : "未検出"));
            GUILayout.Label("状態: " + _status);
            GUILayout.Label("女: " + females.Count + " / 男: " + males.Count);

            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_targetSex == 0, "女", GUI.skin.button, GUILayout.Width(60f)))
                _targetSex = 0;
            if (GUILayout.Toggle(_targetSex == 1, "男", GUI.skin.button, GUILayout.Width(60f)))
                _targetSex = 1;
            GUILayout.EndHorizontal();

            if (_targetIndex >= targets.Count)
                _targetIndex = Mathf.Max(0, targets.Count - 1);

            GUILayout.BeginHorizontal();
            GUI.enabled = targets.Count > 0;
            if (GUILayout.Button("前", GUILayout.Width(50f)))
                _targetIndex = WrapIndex(_targetIndex - 1, targets.Count);
            if (GUILayout.Button("次", GUILayout.Width(50f)))
                _targetIndex = WrapIndex(_targetIndex + 1, targets.Count);
            GUI.enabled = true;
            string targetLabel = targets.Count > 0 ? DescribeCha(targets[_targetIndex], _targetIndex) : "<対象なし>";
            GUILayout.Label("対象: " + targetLabel);
            GUILayout.EndHorizontal();

            ChaControl target = targets.Count > 0 ? targets[_targetIndex] : null;
            SyncPatternFieldsFromTargetIfNeeded(target);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(360f));
            DrawHandSection(target, LeftHand, "左手", ref _leftPattern);
            GUILayout.Space(12f);
            DrawHandSection(target, RightHand, "右手", ref _rightPattern);
            GUILayout.EndScrollView();

            if (GUILayout.Button("閉じる"))
            {
                _cfgUiVisible.Value = false;
                Log("ui visible=false");
                ReleaseInputCapture();
            }

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0f, 0f, 9999f, 22f));
        }

        private void DrawHandSection(ChaControl target, int hand, string label, ref int pattern)
        {
            GUILayout.Label(label);

            int maxCount = target != null ? Mathf.Max(0, target.GetShapeIndexCount()) : 0;
            bool enabled = target != null;
            int currentPattern = target != null ? target.GetShapeHandIndex(hand, 0) : 0;
            bool currentEnabled = target != null && target.GetEnableShapeHand(hand);

            GUILayout.Label("現在: index=" + currentPattern + " enabled=" + currentEnabled + " count=" + maxCount);

            GUILayout.BeginHorizontal();
            GUI.enabled = enabled && maxCount > 0;
            if (GUILayout.Button("-10", GUILayout.Width(60f)))
                SetPatternBuffer(hand, ref pattern, ClampPattern(pattern - 10, maxCount));
            if (GUILayout.Button("-1", GUILayout.Width(60f)))
                SetPatternBuffer(hand, ref pattern, ClampPattern(pattern - 1, maxCount));
            if (GUILayout.Button("+1", GUILayout.Width(60f)))
                SetPatternBuffer(hand, ref pattern, ClampPattern(pattern + 1, maxCount));
            if (GUILayout.Button("+10", GUILayout.Width(60f)))
                SetPatternBuffer(hand, ref pattern, ClampPattern(pattern + 10, maxCount));
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("指定 index", GUILayout.Width(80f));
            string currentText = hand == LeftHand ? _leftPatternText : _rightPatternText;
            GUI.SetNextControlName(hand == LeftHand ? "LeftHandIndex" : "RightHandIndex");
            string nextText = GUILayout.TextField(currentText, GUILayout.Width(80f));
            if (hand == LeftHand)
                _leftPatternText = nextText;
            else
                _rightPatternText = nextText;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.enabled = enabled;
            if (GUILayout.Button("適用", GUILayout.Width(100f)))
                ApplyHandFromText(target, hand, ref pattern);
            if (GUILayout.Button("0に戻す", GUILayout.Width(100f)))
                ApplyHand(target, hand, 0);
            if (GUILayout.Button("無効化", GUILayout.Width(100f)))
                DisableHand(target, hand);
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void SetPatternBuffer(int hand, ref int pattern, int value)
        {
            pattern = value;
            if (hand == LeftHand)
                _leftPatternText = value.ToString();
            else
                _rightPatternText = value.ToString();
        }

        private void ApplyHandFromText(ChaControl target, int hand, ref int pattern)
        {
            string text = hand == LeftHand ? _leftPatternText : _rightPatternText;
            if (!int.TryParse(text, out int parsed))
            {
                _status = (hand == LeftHand ? "左" : "右") + "手 index が数値ではありません: " + text;
                LogWarn(_status);
                return;
            }

            int maxCount = target != null ? Mathf.Max(0, target.GetShapeIndexCount()) : 0;
            pattern = ClampPattern(parsed, maxCount);
            ApplyHand(target, hand, pattern);
        }

        private void ApplyHand(ChaControl target, int hand, int pattern)
        {
            if (target == null)
            {
                _status = "対象がありません";
                return;
            }

            int maxCount = Mathf.Max(0, target.GetShapeIndexCount());
            pattern = ClampPattern(pattern, maxCount);

            try
            {
                target.SetShapeHandValue(hand, pattern, 0, 0f);
                target.SetEnableShapeHand(hand, pattern != 0);
                if (hand == LeftHand)
                {
                    _leftPattern = pattern;
                    _leftPatternText = pattern.ToString();
                }
                else
                {
                    _rightPattern = pattern;
                    _rightPatternText = pattern.ToString();
                }
                _status = DescribeCha(target, _targetIndex) + " " + (hand == LeftHand ? "左" : "右") + "手 index=" + pattern + " 適用";
                Log(_status);
            }
            catch (Exception ex)
            {
                _status = "適用失敗: " + ex.Message;
                LogWarn(_status);
            }
        }

        private void DisableHand(ChaControl target, int hand)
        {
            if (target == null)
            {
                _status = "対象がありません";
                return;
            }

            try
            {
                target.SetEnableShapeHand(hand, false);
                _status = DescribeCha(target, _targetIndex) + " " + (hand == LeftHand ? "左" : "右") + "手 無効化";
                Log(_status);
            }
            catch (Exception ex)
            {
                _status = "無効化失敗: " + ex.Message;
                LogWarn(_status);
            }
        }

        private void SyncPatternFieldsFromTargetIfNeeded(ChaControl target)
        {
            if (target == null)
            {
                _lastTargetId = 0;
                return;
            }

            int targetId = target.GetInstanceID();
            if (_lastTargetId == targetId)
                return;

            _lastTargetId = targetId;
            _leftPattern = target.GetShapeHandIndex(LeftHand, 0);
            _rightPattern = target.GetShapeHandIndex(RightHand, 0);
            _leftPatternText = _leftPattern.ToString();
            _rightPatternText = _rightPattern.ToString();
        }

        private static List<ChaControl> ResolveList(HSceneProc proc, FieldInfo field, int sex)
        {
            var result = new List<ChaControl>();
            var seen = new HashSet<int>();

            if (proc != null && field != null)
            {
                IList list = field.GetValue(proc) as IList;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        ChaControl cha = list[i] as ChaControl;
                        if (cha == null || cha.sex != sex)
                            continue;
                        if (seen.Add(cha.GetInstanceID()))
                            result.Add(cha);
                    }
                }
            }

            if (result.Count > 0)
                return result;

            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindHActors();
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (cha == null || cha.sex != sex)
                    continue;
                if (seen.Add(cha.GetInstanceID()))
                    result.Add(cha);
            }
            return result;
        }

        private static int WrapIndex(int index, int count)
        {
            if (count <= 0)
                return 0;
            if (index < 0)
                return count - 1;
            if (index >= count)
                return 0;
            return index;
        }

        private static int ClampPattern(int value, int maxCount)
        {
            if (maxCount <= 0)
                return 0;
            return Mathf.Clamp(value, 0, maxCount - 1);
        }

        private static string DescribeCha(ChaControl cha, int index)
        {
            if (cha == null)
                return "<null>";
            string fullName = string.Empty;
            try
            {
                fullName = cha.fileParam != null
                    ? cha.fileParam.fullname
                    : string.Empty;
            }
            catch
            {
                fullName = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(fullName))
                fullName = cha.name;

            return "#" + index + " " + fullName;
        }

        private void HandleWindowPointer(Event evt)
        {
            if (evt == null || !_cfgUiVisible.Value)
                return;

            bool insideWindow = _windowRect.Contains(evt.mousePosition);
            _windowPointerActive = insideWindow;
            if (insideWindow && (evt.isMouse || evt.type == EventType.ScrollWheel))
                evt.Use();
        }

        private void UpdateInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable)
                return;

            if (_cfgUiVisible == null || !_cfgUiVisible.Value)
            {
                ReleaseInputCapture();
                return;
            }

            ClampWindowRectToScreen();
            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, true);
            UiInputCaptureApi.SetInputBlockRects(InputOwnerKey, new[] { _windowRect });

            Vector3 mouse = Input.mousePosition;
            var guiMouse = new Vector2(mouse.x, Screen.height - mouse.y);
            _windowPointerActive = _windowRect.Contains(guiMouse);
            bool active = _windowPointerActive || _windowDragging || IsTextFieldFocused();
            UiInputCaptureApi.Sync(InputOwnerKey, InputSourceWindow, active);
            if (active && !_lastCaptureActiveLogged)
            {
                _lastCaptureActiveLogged = true;
                Log("input capture ON rect=" + _windowRect);
            }
            else if (!active && _lastCaptureActiveLogged)
            {
                _lastCaptureActiveLogged = false;
                Log("input capture idle rect=" + _windowRect);
            }
        }

        private void ReleaseInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable)
                return;

            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, false);
            UiInputCaptureApi.ClearInputBlockRects(InputOwnerKey);
            UiInputCaptureApi.EndOwner(InputOwnerKey);
            _windowPointerActive = false;
            _windowDragging = false;
            if (_lastCaptureActiveLogged)
            {
                _lastCaptureActiveLogged = false;
                Log("input capture OFF");
            }
        }

        private void ClampWindowRectToScreen()
        {
            float maxW = Mathf.Max(320f, Screen.width - 8f);
            float maxH = Mathf.Max(240f, Screen.height - 8f);
            _windowRect.width = Mathf.Clamp(_windowRect.width, 320f, maxW);
            _windowRect.height = Mathf.Clamp(_windowRect.height, 240f, maxH);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - _windowRect.width));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, Screen.height - _windowRect.height));
        }

        private void HandleWindowDrag(Event evt)
        {
            if (evt == null)
                return;

            Rect titleBar = new Rect(0f, 0f, _windowRect.width, 22f);
            if (evt.rawType == EventType.MouseDown && evt.button == 0 && titleBar.Contains(evt.mousePosition))
            {
                _windowDragging = true;
                return;
            }

            if (evt.rawType == EventType.MouseUp && evt.button == 0)
                _windowDragging = false;
        }

        private static bool IsTextFieldFocused()
        {
            string focused = GUI.GetNameOfFocusedControl();
            return string.Equals(focused, "LeftHandIndex", StringComparison.Ordinal)
                || string.Equals(focused, "RightHandIndex", StringComparison.Ordinal);
        }

        private void Log(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger.LogInfo(message);
            WriteLogLine(message);
        }

        private void LogWarn(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger.LogWarning(message);
            WriteLogLine("[warn] " + message);
        }

        private void WriteLogLine(string message)
        {
            if (string.IsNullOrEmpty(_logFilePath))
                return;

            try
            {
                File.AppendAllText(_logFilePath, "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message + Environment.NewLine, Utf8NoBom);
            }
            catch
            {
                // logging must not break the probe UI
            }
        }
    }
}
