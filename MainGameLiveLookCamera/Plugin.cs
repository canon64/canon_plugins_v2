using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using MainGameUiInputCapture;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameLiveLookCamera
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInDependency("com.kks.maingame.uiinputcapture", BepInDependency.DependencyFlags.SoftDependency)]
    internal sealed class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.kks.maingame.livelookcamera";
        public const string PluginName = "MainGameLiveLookCamera";
        public const string Version = "0.1.0";

        private const string InputOwnerKey = GUID + ".input";
        private const string InputSourceWindow = "window";
        private const string InputSourceDrag = "drag";
        private const float TitleBarHeight = 24f;
        private const float WindowWidth = 380f;
        private const float WindowHeight = 520f;
        private const int SaveModeNormal = 0;
        private const int SaveModeBone = 1;

        private static readonly string[] SaveModeLabels = { "通常", "ボーン" };
        private static readonly string[] BoneTargetLabels = { "頭", "胸", "腰" };
        private static readonly string[][] BoneTargetNameCandidates =
        {
            new[] { "cf_j_head", "cf_J_Head" },
            new[] { "cf_j_spine03", "cf_J_Spine03", "cf_j_spine02", "cf_J_Spine02" },
            new[] { "cf_j_waist01", "cf_J_Waist01", "cf_j_waist02", "cf_J_Waist02" }
        };

        internal static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger { get; private set; }
        internal static string PluginDir { get; private set; }
        internal static PluginSettings Settings { get; private set; }

        private readonly List<LiveLookPreset> _presets = new List<LiveLookPreset>();
        private ConfigEntry<bool> _cfgUiVisible;
        private ConfigEntry<bool> _cfgApplyFov;
        private ConfigEntry<float> _cfgDefaultFov;
        private ConfigEntry<bool> _cfgDetailLogEnabled;

        private HFlag _cachedHFlag;
        private CameraControl_Ver2 _ctrlCamera;
        private float _nextControllerResolveAt;
        private bool _hasCameraController;
        private Rect _windowRect;
        private Vector2 _scroll;
        private bool _windowPointerActive;
        private bool _windowDragging;
        private Vector2 _dragPointerOffset;
        private readonly List<Rect> _inputBlockRects = new List<Rect>(1) { new Rect() };
        private string _presetNameBuffer = string.Empty;
        private string _activePresetName = string.Empty;
        private string _statusMessage = string.Empty;
        private int _selectedSaveMode;
        private int _selectedBoneTarget;
        private float _liveFov;

        private bool _boneLookActive;
        private int _activeBoneTarget = -1;
        private string _activeBoneName = string.Empty;
        private Vector3 _activeLookAtOffsetLocal = Vector3.zero;
        private float _activeBoneFov;
        private float _nextBoneLookRetryAt;
        private bool _runtimeLoggingEnabled = false;

        private ChaControl _cachedFemale;
        private ChaControl _boneCacheOwner;
        private Transform _boneCacheRoot;
        private Transform[] _cachedBoneCache;
        private ChaControl _activeBoneCacheOwner;
        private Transform _activeBoneCacheRoot;
        private Transform _activeBoneCacheTransform;
        private int _activeBoneCacheTarget = -1;
        private string _activeBoneCacheName;
        private static FieldInfo _lstFemaleField;

        private string _settingsPath;
        private string _logPath;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object FileLogLock = new object();

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            PluginDir = Path.GetDirectoryName(Info.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            _settingsPath = Path.Combine(PluginDir, "LiveLookCameraSettings.json");
            _logPath = Path.Combine(PluginDir, "MainGameLiveLookCamera.log");
            ResetFileLog();

            Settings = SettingsStore.LoadOrCreate(_settingsPath);
            _cfgUiVisible = Config.Bind("UI", "UiVisible", Settings.UiVisible, "Show MainGameLiveLookCamera window.");
            _cfgApplyFov = Config.Bind("Camera", "ApplyFov", Settings.ApplyFov, "Apply preset FOV.");
            _cfgDefaultFov = Config.Bind("Camera", "DefaultFov", Settings.DefaultFov, "Fallback FOV.");
            _cfgDetailLogEnabled = Config.Bind("Logging", "DetailLogEnabled", Settings.DetailLogEnabled, "Enable detailed logs.");

            // ウィンドウ位置は保存・復元しない。毎回左上から開始する。
            _windowRect = new Rect(20f, 20f, WindowWidth, WindowHeight);
            _selectedSaveMode = Mathf.Clamp(Settings.SelectedSaveMode, 0, SaveModeLabels.Length - 1);
            _selectedBoneTarget = Mathf.Clamp(Settings.SelectedBoneTarget, 0, BoneTargetLabels.Length - 1);
            _presetNameBuffer = Settings.PresetName ?? string.Empty;
            if (Settings.Presets != null)
                _presets.AddRange(Settings.Presets);
            _liveFov = Mathf.Clamp(Settings.DefaultFov, 5f, 120f);

            Camera.onPreCull += OnCameraPreCull;
            LogInfo(PluginName + " " + Version + " initialized");
            LogInfo("pluginDir=" + PluginDir);
            LogInfo("settingsPath=" + _settingsPath);
        }

        private void OnDestroy()
        {
            Camera.onPreCull -= OnCameraPreCull;
            ReleaseInputCapture();
            SaveSettings();
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            RefreshCameraControllerCache();
            UpdateWindowDrag();
            UpdateInputCapture();
        }

        private void LateUpdate()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            RefreshCameraControllerCache();
            ApplyBoneLookDirect();
            if (_boneLookActive && _cfgApplyFov.Value && _activeBoneFov > 0f)
                ApplyFovOnly(_activeBoneFov);
        }

        private void OnCameraPreCull(Camera camera)
        {
            if (!_boneLookActive || camera == null || _ctrlCamera == null)
                return;
            if (_ctrlCamera.thisCmaera != null && !ReferenceEquals(camera, _ctrlCamera.thisCmaera))
                return;

            ApplyBoneLookDirect();
            if (_cfgApplyFov.Value && _activeBoneFov > 0f)
                camera.fieldOfView = Mathf.Clamp(_activeBoneFov, 5f, 120f);
        }

        private void OnGUI()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (!_cfgUiVisible.Value)
                return;

            _windowRect.width = WindowWidth;
            _windowRect.height = WindowHeight;
            _windowRect = GUI.Window(981624, _windowRect, DrawWindow, PluginName);
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();
            _scroll = GUILayout.BeginScrollView(_scroll);

            GUILayout.Label("状態");
            GUILayout.Label(_hasCameraController ? "CameraControl_Ver2: OK" : "CameraControl_Ver2: 未取得");
            GUILayout.Label("追従: " + (_boneLookActive ? "ボーン " + BoneLabel(_activeBoneTarget) : "なし"));
            if (!string.IsNullOrEmpty(_statusMessage))
                GUILayout.Label(_statusMessage);

            GUILayout.Space(8f);
            GUILayout.Label("保存モード");
            int nextMode = GUILayout.SelectionGrid(_selectedSaveMode, SaveModeLabels, SaveModeLabels.Length);
            if (nextMode != _selectedSaveMode)
            {
                _selectedSaveMode = nextMode;
                SaveSettings();
            }

            if (_selectedSaveMode == SaveModeBone || _boneLookActive)
                DrawBoneLiveControls();

            GUILayout.Space(8f);
            DrawFovControls();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("保存名", GUILayout.Width(48f));
            string nextName = GUILayout.TextField(_presetNameBuffer ?? string.Empty);
            if (nextName != _presetNameBuffer)
            {
                _presetNameBuffer = nextName;
                SaveSettings();
            }
            GUILayout.EndHorizontal();

            bool canSave = !string.IsNullOrWhiteSpace(_presetNameBuffer) && _hasCameraController;
            GUI.enabled = canSave;
            if (GUILayout.Button(FindPreset(_presetNameBuffer) == null ? "保存" : "上書き保存"))
                SaveCurrentPreset(_presetNameBuffer.Trim());
            GUI.enabled = true;

            GUILayout.Space(8f);
            if (_boneLookActive && GUILayout.Button("ボーン追従解除"))
                ClearBoneLook("解除");

            GUILayout.Space(8f);
            GUILayout.Label("プリセット");
            if (_presets.Count == 0)
            {
                GUILayout.Label("保存済みなし");
            }
            else
            {
                for (int i = 0; i < _presets.Count; i++)
                    DrawPresetRow(i);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            // GUI.DragWindow は使わない。画面外までマウスを追ってウィンドウが飛ぶため、
            // タイトルバーの自前ドラッグ（UpdateWindowDrag）で動かす。
        }

        // 頭/胸/腰 のボーン選択。現在のボーンは赤。
        // ボーン追従中は「ライブ状態だけ」切り替える（保存しない）。
        // 非アクティブ時は新規ボーン保存の対象選択（設定のみ保存）。
        // ボーン追従中は注視点XYZスライダーも出す（_activeLookAtOffsetLocal を直接編集・保存しない）。
        private void DrawBoneLiveControls()
        {
            GUILayout.Label("対象ボーン");
            int current = _boneLookActive ? _activeBoneTarget : _selectedBoneTarget;
            GUILayout.BeginHorizontal();
            for (int t = 0; t < BoneTargetLabels.Length; t++)
            {
                Color prev = GUI.backgroundColor;
                if (t == current)
                    GUI.backgroundColor = Color.red;
                if (GUILayout.Button(BoneTargetLabels[t]))
                {
                    if (_boneLookActive)
                    {
                        SwitchBoneLive(t);
                    }
                    else if (t != _selectedBoneTarget)
                    {
                        _selectedBoneTarget = t;
                        SaveSettings();
                    }
                }
                GUI.backgroundColor = prev;
            }
            GUILayout.EndHorizontal();

            if (_boneLookActive)
            {
                GUILayout.Label("注視点オフセット (保存しない)");
                _activeLookAtOffsetLocal.x = DrawAxisSlider("X", _activeLookAtOffsetLocal.x);
                _activeLookAtOffsetLocal.y = DrawAxisSlider("Y", _activeLookAtOffsetLocal.y);
                _activeLookAtOffsetLocal.z = DrawAxisSlider("Z", _activeLookAtOffsetLocal.z);
            }
        }

        private static float DrawAxisSlider(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(16f));
            float next = GUILayout.HorizontalSlider(value, -2f, 2f);
            string text = GUILayout.TextField(value.ToString("F2", CultureInfo.InvariantCulture), GUILayout.Width(56f));
            GUILayout.EndHorizontal();

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float typed))
                next = typed;
            return Mathf.Clamp(next, -2f, 2f);
        }

        private void DrawFovControls()
        {
            GUILayout.BeginHorizontal();
            bool nextApply = GUILayout.Toggle(_cfgApplyFov.Value, "FOV適用", GUILayout.Width(80f));
            if (nextApply != _cfgApplyFov.Value)
            {
                _cfgApplyFov.Value = nextApply;
                SaveSettings();
            }
            GUILayout.Label("FOV", GUILayout.Width(32f));
            float source = ResolveLiveFov();
            float next = GUILayout.HorizontalSlider(source, 5f, 120f);
            string text = GUILayout.TextField(source.ToString("F2", CultureInfo.InvariantCulture), GUILayout.Width(56f));
            GUILayout.EndHorizontal();

            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float typed))
                next = typed;

            next = Mathf.Clamp(next, 5f, 120f);
            if (Mathf.Abs(next - source) > 0.001f)
            {
                _liveFov = next;
                if (_boneLookActive)
                    _activeBoneFov = next;
                if (_cfgApplyFov.Value)
                    ApplyFovOnly(next);
            }
        }

        private void DrawPresetRow(int index)
        {
            LiveLookPreset preset = _presets[index];
            GUILayout.BeginHorizontal();
            string marker = string.Equals(_activePresetName, preset.Name ?? string.Empty, StringComparison.Ordinal) ? ">" : " ";
            if (GUILayout.Button(marker + " " + DescribePreset(preset), GUILayout.ExpandWidth(true)))
                LoadPreset(preset);
            if (GUILayout.Button("↻", GUILayout.Width(28f)))
                OverwritePreset(preset);
            if (GUILayout.Button("x", GUILayout.Width(24f)))
            {
                _presets.RemoveAt(index);
                if (string.Equals(_activePresetName, preset.Name ?? string.Empty, StringComparison.Ordinal))
                    _activePresetName = string.Empty;
                SaveSettings();
            }
            GUILayout.EndHorizontal();
        }

        private void SaveCurrentPreset(string name)
        {
            LiveLookPreset preset = _selectedSaveMode == SaveModeBone
                ? BuildBonePreset(name)
                : BuildNormalPreset(name);

            if (preset == null)
            {
                SetStatus("保存失敗");
                return;
            }

            LiveLookPreset existing = FindPreset(name);
            if (existing == null)
                _presets.Add(preset);
            else
                CopyPreset(preset, existing);

            _activePresetName = name;
            SaveSettings();
            SetStatus("保存: " + name);
        }

        private void OverwritePreset(LiveLookPreset preset)
        {
            if (preset == null)
                return;

            LiveLookPreset next = preset.UseBoneLook
                ? BuildBonePreset(preset.Name ?? string.Empty)
                : BuildNormalPreset(preset.Name ?? string.Empty);
            if (next == null)
            {
                SetStatus("上書き失敗");
                return;
            }

            CopyPreset(next, preset);
            _activePresetName = preset.Name ?? string.Empty;
            SaveSettings();
            SetStatus("上書き: " + _activePresetName);
        }

        private LiveLookPreset BuildNormalPreset(string name)
        {
            if (_ctrlCamera == null)
                return null;

            BaseCameraControl_Ver2.CameraData data = CaptureVisibleCameraData(GetCurrentLookAtWorld());
            return new LiveLookPreset
            {
                Name = name,
                UseBoneLook = false,
                TargetPosition = ToArray(data.Pos),
                CameraDirection = ToArray(data.Dir),
                Rotation = ToArray(data.Rot),
                Fov = data.Fov
            };
        }

        private LiveLookPreset BuildBonePreset(string name)
        {
            if (_ctrlCamera == null)
                return null;

            ChaControl female = ResolveMainFemale();
            Transform bone = female != null ? ResolveBoneTarget(GetFemaleBoneCache(female), _selectedBoneTarget, null) : null;
            if (bone == null)
            {
                SetStatus("対象ボーンが見つからない: " + BoneLabel(_selectedBoneTarget));
                return null;
            }

            Vector3 lookAtWorld = GetCurrentLookAtWorld();
            return new LiveLookPreset
            {
                Name = name,
                UseBoneLook = true,
                BoneTarget = _selectedBoneTarget,
                BoneName = bone.name,
                LookAtOffsetLocal = ToArray(bone.InverseTransformPoint(lookAtWorld)),
                Fov = ResolveLiveFov()
            };
        }

        private void LoadPreset(LiveLookPreset preset)
        {
            if (preset == null)
                return;
            RefreshCameraControllerCache();
            if (_ctrlCamera == null)
            {
                SetStatus("CameraControl_Ver2 が見つからない");
                return;
            }

            _activePresetName = preset.Name ?? string.Empty;
            _presetNameBuffer = _activePresetName;

            if (preset.UseBoneLook)
            {
                ActivateBonePreset(preset);
            }
            else
            {
                ClearBoneLook(null);
                BaseCameraControl_Ver2.CameraData data = _ctrlCamera.GetCameraData();
                data.Pos = ToVector3(preset.TargetPosition, data.Pos);
                data.Dir = ToVector3(preset.CameraDirection, data.Dir);
                data.Rot = ToVector3(preset.Rotation, data.Rot);
                data.Fov = Mathf.Clamp(preset.Fov <= 0f ? ResolveLiveFov() : preset.Fov, 5f, 120f);
                _ctrlCamera.SetCameraData(data);
                if (_cfgApplyFov.Value)
                    ApplyFovOnly(data.Fov);
                SetStatus("呼び出し: " + _activePresetName);
            }

            SaveSettings();
        }

        private void ActivateBonePreset(LiveLookPreset preset)
        {
            _nextBoneLookRetryAt = 0f;
            _selectedSaveMode = SaveModeBone;
            _selectedBoneTarget = Mathf.Clamp(preset.BoneTarget, 0, BoneTargetLabels.Length - 1);
            _activeBoneTarget = _selectedBoneTarget;
            _activeBoneName = preset.BoneName ?? string.Empty;
            _activeLookAtOffsetLocal = ToVector3(preset.LookAtOffsetLocal, Vector3.zero);
            _activeBoneFov = Mathf.Clamp(preset.Fov <= 0f ? ResolveLiveFov() : preset.Fov, 5f, 120f);
            _boneLookActive = true;
            if (_cfgApplyFov.Value)
                ApplyFovOnly(_activeBoneFov);
            ApplyBoneLookDirect();
            SetStatus("ボーン呼び出し: " + (preset.Name ?? string.Empty));
        }

        // ボーン追従中に対象ボーンを乗り換える。今見ているワールド注視点を保ったまま
        // 新ボーン基準の相対オフセットへ変換するだけ。position も rotation も即座には動かさない
        // ＝ジャンプしない。保存もしない（保存は保存ボタン/↻のみ）。
        private void SwitchBoneLive(int newBoneTarget)
        {
            if (!_boneLookActive)
                return;

            newBoneTarget = Mathf.Clamp(newBoneTarget, 0, BoneTargetLabels.Length - 1);
            RefreshCameraControllerCache();
            ChaControl female = ResolveMainFemale();
            Transform newBone = female != null ? ResolveBoneTarget(GetFemaleBoneCache(female), newBoneTarget, null) : null;
            if (newBone == null)
            {
                InvalidateFemaleCache();
                SetStatus("対象ボーンが見つからない: " + BoneLabel(newBoneTarget));
                return;
            }

            Vector3 lookAtWorld = GetCurrentLookAtWorld();
            _activeLookAtOffsetLocal = newBone.InverseTransformPoint(lookAtWorld);
            _activeBoneTarget = newBoneTarget;
            _activeBoneName = newBone.name;
            _selectedBoneTarget = newBoneTarget;
            _nextBoneLookRetryAt = 0f;
            ApplyBoneLookDirect();
            SetStatus("ボーン切替: " + BoneLabel(newBoneTarget));
        }

        private void ApplyBoneLookDirect()
        {
            if (!_boneLookActive || _ctrlCamera == null || _ctrlCamera.thisCmaera == null)
                return;
            if (Time.unscaledTime < _nextBoneLookRetryAt)
                return;

            ChaControl female = ResolveMainFemale();
            Transform bone = ResolveActiveBone(female);
            if (bone == null)
            {
                InvalidateFemaleCache();
                _nextBoneLookRetryAt = Time.unscaledTime + 0.25f;
                return;
            }
            _nextBoneLookRetryAt = 0f;

            Vector3 lookAtWorld = bone.TransformPoint(_activeLookAtOffsetLocal);
            Transform cameraTransform = _ctrlCamera.thisCmaera.transform;
            Vector3 forward = lookAtWorld - cameraTransform.position;
            if (forward.sqrMagnitude <= 0.000001f)
                return;

            cameraTransform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        private void ClearBoneLook(string status)
        {
            _nextBoneLookRetryAt = 0f;
            _boneLookActive = false;
            _activeBoneTarget = -1;
            _activeBoneName = string.Empty;
            _activeLookAtOffsetLocal = Vector3.zero;
            _activeBoneFov = 0f;
            if (!string.IsNullOrEmpty(status))
                SetStatus(status);
        }

        private BaseCameraControl_Ver2.CameraData CaptureVisibleCameraData(Vector3 lookAtWorld)
        {
            BaseCameraControl_Ver2.CameraData data = _ctrlCamera.GetCameraData();
            Transform cameraTransform = _ctrlCamera.thisCmaera != null ? _ctrlCamera.thisCmaera.transform : _ctrlCamera.transform;
            Quaternion cameraRotation = cameraTransform.rotation;

            if (_ctrlCamera.transBase != null)
            {
                data.Pos = _ctrlCamera.transBase.InverseTransformPoint(lookAtWorld);
                data.Rot = NormalizeEuler((Quaternion.Inverse(_ctrlCamera.transBase.rotation) * cameraRotation).eulerAngles);
            }
            else
            {
                data.Pos = lookAtWorld;
                data.Rot = NormalizeEuler(cameraRotation.eulerAngles);
            }

            data.Dir = Quaternion.Inverse(cameraRotation) * (cameraTransform.position - lookAtWorld);
            data.Fov = ResolveLiveFov();
            return data;
        }

        private Vector3 GetCurrentLookAtWorld()
        {
            if (_boneLookActive)
            {
                ChaControl female = ResolveMainFemale();
                Transform bone = ResolveActiveBone(female);
                if (bone != null)
                    return bone.TransformPoint(_activeLookAtOffsetLocal);
            }

            if (_ctrlCamera == null)
                return Vector3.zero;

            BaseCameraControl_Ver2.CameraData data = _ctrlCamera.GetCameraData();
            return _ctrlCamera.transBase != null ? _ctrlCamera.transBase.TransformPoint(data.Pos) : data.Pos;
        }

        private void ApplyFovOnly(float fov)
        {
            if (_ctrlCamera == null)
                return;

            float clamped = Mathf.Clamp(fov <= 0f ? _cfgDefaultFov.Value : fov, 5f, 120f);
            _liveFov = clamped;
            _ctrlCamera.CameraInitFov = clamped;
            _ctrlCamera.CameraFov = clamped;
            if (_ctrlCamera.thisCmaera != null)
                _ctrlCamera.thisCmaera.fieldOfView = clamped;
        }

        private float ResolveLiveFov()
        {
            if (_ctrlCamera != null && _ctrlCamera.thisCmaera != null)
                return Mathf.Clamp(_ctrlCamera.thisCmaera.fieldOfView, 5f, 120f);
            return Mathf.Clamp(_liveFov <= 0f ? _cfgDefaultFov.Value : _liveFov, 5f, 120f);
        }

        private void RefreshCameraControllerCache()
        {
            CameraControl_Ver2 ctrl = ResolveCtrlCamera();
            if (ctrl == null)
            {
                _hasCameraController = false;
                _ctrlCamera = null;
                return;
            }

            if (_ctrlCamera != ctrl)
            {
                _ctrlCamera = ctrl;
                _hasCameraController = true;
                _liveFov = ResolveLiveFov();
                SetStatus("CameraControl_Ver2 を取得");
                return;
            }

            _hasCameraController = true;
        }

        private CameraControl_Ver2 ResolveCtrlCamera()
        {
            if (_cachedHFlag != null && _cachedHFlag.ctrlCamera != null)
                return _cachedHFlag.ctrlCamera;

            // Update と LateUpdate の双方から呼ばれる。HFlag が存在しない間も
            // 毎回シーン全体を検索せず、切り替えを拾える間隔で再探索する。
            if (Time.unscaledTime < _nextControllerResolveAt)
                return _ctrlCamera != null ? _ctrlCamera : null;
            _nextControllerResolveAt = Time.unscaledTime + 0.5f;

            if (_cachedHFlag == null)
                _cachedHFlag = UnityEngine.Object.FindObjectOfType<HFlag>();
            if (_cachedHFlag != null && _cachedHFlag.ctrlCamera != null)
                return _cachedHFlag.ctrlCamera;
            return UnityEngine.Object.FindObjectOfType<CameraControl_Ver2>();
        }

        private ChaControl ResolveMainFemale()
        {
            if (_cachedFemale != null)
                return _cachedFemale;

            HSceneProc proc = UnityEngine.Object.FindObjectOfType<HSceneProc>();
            if (proc != null)
            {
                if (_lstFemaleField == null)
                    _lstFemaleField = typeof(HSceneProc).GetField("lstFemale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                IList list = _lstFemaleField != null ? _lstFemaleField.GetValue(proc) as IList : null;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        ChaControl cha = list[i] as ChaControl;
                        if (cha != null)
                        {
                            _cachedFemale = cha;
                            return _cachedFemale;
                        }
                    }
                }
            }

            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindHActors();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].sex == 1)
                {
                    _cachedFemale = all[i];
                    return _cachedFemale;
                }
            }

            return null;
        }

        private Transform[] GetFemaleBoneCache(ChaControl female)
        {
            if (female == null)
                return null;
            Transform root = female.animBody != null ? female.animBody.transform : female.transform;
            if (_cachedBoneCache != null && _boneCacheOwner == female && _boneCacheRoot == root)
                return _cachedBoneCache;

            _cachedBoneCache = root != null ? root.GetComponentsInChildren<Transform>(true) : null;
            _boneCacheOwner = female;
            _boneCacheRoot = root;
            return _cachedBoneCache;
        }

        private Transform ResolveActiveBone(ChaControl female)
        {
            if (female == null) return null;
            Transform root = female.animBody != null ? female.animBody.transform : female.transform;
            if (_activeBoneCacheOwner == female && _activeBoneCacheRoot == root &&
                _activeBoneCacheTarget == _activeBoneTarget &&
                string.Equals(_activeBoneCacheName, _activeBoneName, StringComparison.Ordinal) &&
                _activeBoneCacheTransform != null)
                return _activeBoneCacheTransform;

            _activeBoneCacheOwner = female;
            _activeBoneCacheRoot = root;
            _activeBoneCacheTarget = _activeBoneTarget;
            _activeBoneCacheName = _activeBoneName;
            _activeBoneCacheTransform = ResolveBoneTarget(GetFemaleBoneCache(female), _activeBoneTarget, _activeBoneName);
            return _activeBoneCacheTransform;
        }

        private void InvalidateFemaleCache()
        {
            _cachedFemale = null;
            _boneCacheOwner = null;
            _boneCacheRoot = null;
            _cachedBoneCache = null;
            _activeBoneCacheOwner = null;
            _activeBoneCacheRoot = null;
            _activeBoneCacheTransform = null;
        }

        private static Transform ResolveBoneTarget(Transform[] boneCache, int boneTarget, string preferredName)
        {
            if (boneCache == null)
                return null;

            if (!string.IsNullOrWhiteSpace(preferredName))
            {
                Transform exact = FindBoneByName(boneCache, preferredName);
                if (exact != null)
                    return exact;
            }

            string[] candidates = BoneTargetNameCandidates[Mathf.Clamp(boneTarget, 0, BoneTargetNameCandidates.Length - 1)];
            for (int i = 0; i < candidates.Length; i++)
            {
                Transform found = FindBoneByName(boneCache, candidates[i]);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static Transform FindBoneByName(Transform[] boneCache, string name)
        {
            if (boneCache == null || string.IsNullOrWhiteSpace(name))
                return null;
            for (int i = 0; i < boneCache.Length; i++)
            {
                Transform current = boneCache[i];
                if (current != null && string.Equals(current.name, name, StringComparison.Ordinal))
                    return current;
            }
            return null;
        }

        private LiveLookPreset FindPreset(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            for (int i = 0; i < _presets.Count; i++)
            {
                if (string.Equals(_presets[i].Name ?? string.Empty, name.Trim(), StringComparison.Ordinal))
                    return _presets[i];
            }
            return null;
        }

        private static void CopyPreset(LiveLookPreset src, LiveLookPreset dst)
        {
            dst.Name = src.Name;
            dst.UseBoneLook = src.UseBoneLook;
            dst.TargetPosition = CloneArray(src.TargetPosition);
            dst.CameraDirection = CloneArray(src.CameraDirection);
            dst.Rotation = CloneArray(src.Rotation);
            dst.Fov = src.Fov;
            dst.BoneTarget = src.BoneTarget;
            dst.BoneName = src.BoneName;
            dst.LookAtOffsetLocal = CloneArray(src.LookAtOffsetLocal);
        }

        private static string DescribePreset(LiveLookPreset preset)
        {
            if (preset == null)
                return "(null)";
            return (preset.Name ?? "(no name)") + " [" + (preset.UseBoneLook ? "ボーン/" + BoneLabel(preset.BoneTarget) : "通常") + "]";
        }

        private static string BoneLabel(int target)
        {
            return target >= 0 && target < BoneTargetLabels.Length ? BoneTargetLabels[target] : "?";
        }

        private void SaveSettings()
        {
            if (Settings == null)
                return;

            Settings.UiVisible = _cfgUiVisible.Value;
            Settings.ApplyFov = _cfgApplyFov.Value;
            Settings.DefaultFov = Mathf.Clamp(_cfgDefaultFov.Value, 5f, 120f);
            Settings.DetailLogEnabled = _cfgDetailLogEnabled.Value;
            // ウィンドウ位置は保存しない（毎回左上から開始）。
            Settings.SelectedSaveMode = _selectedSaveMode;
            Settings.SelectedBoneTarget = _selectedBoneTarget;
            Settings.PresetName = _presetNameBuffer ?? string.Empty;
            Settings.Presets = new List<LiveLookPreset>(_presets);
            SettingsStore.Save(_settingsPath, Settings);
        }

        // タイトルバーの自前ドラッグ。GUI.DragWindow と違い、マウス位置から直接 rect を動かす。
        // ドラッグ中はキャプチャーを維持する（UpdateInputCapture）ので、カーソルが画面中央へ
        // 強制ロックされて飛ぶことがなく、ウィンドウが画面外へワープしない。
        private void UpdateWindowDrag()
        {
            if (!_cfgUiVisible.Value)
            {
                _windowPointerActive = false;
                _windowDragging = false;
                return;
            }

            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            _windowPointerActive = _windowRect.Contains(guiMouse);

            Rect titleBar = new Rect(_windowRect.x, _windowRect.y, WindowWidth, TitleBarHeight);
            if (!_windowDragging)
            {
                if (Input.GetMouseButtonDown(0) && titleBar.Contains(guiMouse))
                {
                    _windowDragging = true;
                    _dragPointerOffset = guiMouse - new Vector2(_windowRect.x, _windowRect.y);
                }
                return;
            }

            if (!Input.GetMouseButton(0))
            {
                _windowDragging = false;
                return;
            }

            _windowRect.x = guiMouse.x - _dragPointerOffset.x;
            _windowRect.y = guiMouse.y - _dragPointerOffset.y;
        }

        private void UpdateInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable)
                return;

            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, _cfgUiVisible.Value);

            // ウィンドウ矩形を登録し、マウスが矩形内か否かの判定・キャプチャー切替を
            // キャプチャープラグイン側に毎フレーム正確にやらせる（外に出たら自動でオフ）。
            if (_cfgUiVisible.Value)
            {
                _inputBlockRects[0] = _windowRect;
                UiInputCaptureApi.SetInputBlockRects(InputOwnerKey, _inputBlockRects);
            }
            else
            {
                UiInputCaptureApi.ClearInputBlockRects(InputOwnerKey);
            }

            // ドラッグ中だけはマウスが一瞬矩形を外れてもキャプチャーを保持し、
            // カーソルの中央ロック（＝ウィンドウ飛び）を防ぐ。
            UiInputCaptureApi.Sync(InputOwnerKey, InputSourceDrag, _windowDragging);
        }

        private void ReleaseInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable)
                return;

            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, false);
            UiInputCaptureApi.EndOwner(InputOwnerKey);
            _windowPointerActive = false;
        }

        private void SetStatus(string message)
        {
            _statusMessage = message ?? string.Empty;
            LogInfo("camera: " + _statusMessage);
        }

        private void LogInfo(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger?.LogInfo(message);
            WriteFileLog("INFO", message);
        }

        private void LogDebug(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (_cfgDetailLogEnabled == null || !_cfgDetailLogEnabled.Value)
                return;
            Logger?.LogDebug(message);
            WriteFileLog("DEBUG", message);
        }

        private void ResetFileLog()
        {
            if (!_runtimeLoggingEnabled) return;
            try
            {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllText(_logPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [INFO] === session start ===" + Environment.NewLine, Utf8NoBom);
            }
            catch
            {
            }
        }

        private void WriteFileLog(string level, string message)
        {
            if (!_runtimeLoggingEnabled) return;
            try
            {
                lock (FileLogLock)
                {
                    File.AppendAllText(
                        _logPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + level + "] " + (message ?? string.Empty) + Environment.NewLine,
                        Utf8NoBom);
                }
            }
            catch
            {
            }
        }

        private static float[] ToArray(Vector3 value)
        {
            return new[] { value.x, value.y, value.z };
        }

        private static Vector3 ToVector3(float[] values, Vector3 fallback)
        {
            if (values == null || values.Length < 3)
                return fallback;
            return new Vector3(values[0], values[1], values[2]);
        }

        private static float[] CloneArray(float[] values)
        {
            if (values == null)
                return null;
            float[] copy = new float[values.Length];
            Array.Copy(values, copy, values.Length);
            return copy;
        }

        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            return new Vector3(NormalizeAngle(euler.x), NormalizeAngle(euler.y), NormalizeAngle(euler.z));
        }

        private static float NormalizeAngle(float angle)
        {
            while (angle > 180f) angle -= 360f;
            while (angle < -180f) angle += 360f;
            return angle;
        }
    }
}
