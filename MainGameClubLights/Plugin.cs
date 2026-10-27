using Bindings = CanonPlugins.Shared.PluginInput;
using BepInEx;
using BepInEx.Configuration;
using CanonPlugins.Shared;
using System;
using System.IO;
using UnityEngine;

namespace MainGameClubLights
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    [BepInDependency("com.kks.maingameblankmapadd",          BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.kks.maingame.beatsyncseed",         BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.kks.maingame.transformgizmo",       BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.kks.maingame.uiinputcapture",       BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("vr",                                    BepInDependency.DependencyFlags.SoftDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid       = "com.kks.maingame.clublights";
        public const string PluginName = "MainGameClubLights";
        public const string Version    = "0.1.0";

        internal static Plugin Instance { get; private set; }

        private string             _pluginDir;
        private string             _settingsPath;
        private string             _legacySettingsPath;
        private string             _profilesDir;
        private string             _profileStatePath;
        private string             _currentProfileName;
        private ClubLightsSettings _settings;
        internal SimpleFileLogger   _log;

        // Config.Bind（ConfigManager に表示される）
        private ConfigEntry<bool>             _cfgEnabled;
        internal ConfigEntry<bool>            _cfgLogEnabled;
        private ConfigEntry<KeyboardShortcut> _cfgToggleKey;

        private HSceneProc _hSceneProc;
        private bool       _insideHScene;
        private float      _nextScanTime;
        private bool       _rendererDiagDone;
        private bool       _astralModeSuppressed;

        private void Awake()
        {
            Instance      = this;
            _pluginDir    = Path.GetDirectoryName(Info.Location);
            _log          = new SimpleFileLogger(Path.Combine(_pluginDir, PluginName + ".log"));

            InitializeProfileStorage();
            EnsureSettingsValid();

            BindConfig();
            SubscribeVideoEvents();

            _log.Info($"loaded pluginDir={_pluginDir} profile={CurrentProfileName}");
        }

        private void BindConfig()
        {
            _cfgEnabled = Config.Bind("General", "Enabled", true, "プラグイン有効/無効");

            _cfgLogEnabled = Config.Bind("General", "LogEnabled", false, "ログ出力 有効/無効");
            _log.Enabled = _cfgLogEnabled.Value;
            _cfgLogEnabled.SettingChanged += (_, __) => _log.Enabled = _cfgLogEnabled.Value;

            _cfgToggleKey = Config.Bind("General", "UiToggleKey",
                KeyboardShortcut.Empty, "UIウィンドウの表示/非表示キー");
        }

        // BlankMapAdd の追加マップ以外では一切発火させない。
        private bool _gateWasInRoom;

        private void Update()
        {
            if (!BlankRoomGate.Tick(ref _gateWasInRoom, OnBlankRoomLeft)) return;

            RegisterUnifiedTabIfAvailable();
            if (!_cfgEnabled.Value) return;

            if (Bindings.Down("MainGameClubLights.toggle", _cfgToggleKey.Value.ToString()))
                _settings.UiVisible = !_settings.UiVisible;

            UpdateInputCapture();

            ScanHScene();
            if (!_insideHScene) return;
            if (_astralModeSuppressed) return;

            UpdateLights();
            UpdateFolders();
            UpdatePoseTracking();
            UpdateNativeLightLoop();
            UpdateBeatSync();
            UpdateVRLightGrab();
            if (!_rendererDiagDone && _cfgLogEnabled.Value) TryLogCharacterRenderers();
        }

        /// <summary>
        /// 退室時の後始末。Update を止めるだけだと HScene 終了検知も止まり、
        /// 置いた照明が現実の部屋に残ってしまうので、ここで通常の終了処理を1回通す。
        /// </summary>
        private void OnBlankRoomLeft()
        {
            if (_insideHScene)
            {
                _insideHScene = false;
                OnHSceneEnd();
            }
            if (_settings != null) _settings.UiVisible = false;
            ReleaseInputCapture();
            _log?.Info("blank room left -> club lights cleared");
        }

        private void LateUpdate()
        {
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (!_insideHScene) return;
            SyncFreeGizmoPositions();
        }

        private void OnGUI()
        {
            if (!BlankRoomGate.IsInBlankRoom) return;
            // 総合UIがタブ表示する間は自前窓を出さない。
            if (IsHostedByUnifiedUi()) return;
            if (!_cfgEnabled.Value) return;
            if (!_settings.UiVisible) return;
            DrawUI();
        }

        private void OnDestroy()
        {
            ReleaseInputCapture();
            UnsubscribeVideoEvents();
            DestroyAllLights();
            SaveSettingsNow("destroy");
            _log.Info("destroyed");
        }

        private void OnApplicationQuit()
        {
            SaveSettingsNow("quit");
        }

        // ── HScene検出 ───────────────────────────────────────────────────────

        private void ScanHScene()
        {
            float now = Time.unscaledTime;
            if (now < _nextScanTime) return;
            _nextScanTime = now + 1f;

            if (_hSceneProc == null)
                _hSceneProc = FindObjectOfType<HSceneProc>();

            bool inside = _hSceneProc != null;
            if (inside == _insideHScene) return;

            _insideHScene = inside;
            if (_insideHScene)
                OnHSceneStart();
            else
                OnHSceneEnd();
        }

        private void OnHSceneStart()
        {
            _log.Info("HScene開始");
            CacheNativeLights();
            BuildLightObjects();
            ResetVideoLinkBeatState();
            _rendererDiagDone = !_cfgLogEnabled.Value; // 診断ログが無効ならRenderer探索を行わない
        }

        private void TryLogCharacterRenderers()
        {
            if (!_cfgLogEnabled.Value)
            {
                _rendererDiagDone = true;
                return;
            }
            try
            {
                var field = HarmonyLib.AccessTools.Field(typeof(HSceneProc), "lstFemale");
                var lst   = field?.GetValue(_hSceneProc) as System.Collections.Generic.List<ChaControl>;
                if (lst == null || lst.Count == 0) return; // まだ未準備、次フレームで再試行
                _rendererDiagDone = true;
                LogCharacterRenderers(lst[0]);
            }
            catch (Exception ex) { _log.Info($"[RendererDiag] 例外: {ex.Message}"); _rendererDiagDone = true; }
        }

        private void LogCharacterRenderers(ChaControl cha)
        {
            var renderers = cha.GetComponentsInChildren<Renderer>(includeInactive: true);
            _log.Info($"[RendererDiag] キャラクターRenderer数={renderers.Length}");

            // 代表的なRenderer（最大20件）だけログ出力
            int count = 0;
            foreach (var r in renderers)
            {
                if (count >= 20) break;
                string shaders = "";
                foreach (var mat in r.sharedMaterials)
                    if (mat != null) shaders += mat.shader?.name + ";";
                _log.Info($"[RendererDiag] name={r.gameObject.name} layer={r.gameObject.layer} shaders=[{shaders}]");
                count++;
            }

            // 作成したライトの cullingMask も確認
            foreach (var entry in _lightEntries)
                if (entry.Light != null)
                    _log.Info($"[RendererDiag] light id={entry.Settings.Id} cullingMask={entry.Light.cullingMask} renderMode={entry.Light.renderMode}");
        }

        private void OnHSceneEnd()
        {
            _log.Info("HScene終了");
            _femalePositionFrame = -1;
            _femalePositionChara = null;
            _femaleHeadRoot = null;
            _femaleHeadBone = null;
            DestroyAllLights();
            ClearNativeLightsOverride();
            _hSceneProc = null;
            _rendererDiagDone = false;
            ResetBeatState();
            ResetVideoLinkBeatState();
        }

        // ── 設定バリデーション ────────────────────────────────────────────────

        private void EnsureSettingsValid()
        {
            if (_settings == null)
                _settings = new ClubLightsSettings();
            if (_settings.Lights == null)
                _settings.Lights = new System.Collections.Generic.List<LightInstanceSettings>();
            if (_settings.Presets == null)
                _settings.Presets = new System.Collections.Generic.List<LightPreset>();
            if (_settings.VideoPresetMappings == null)
                _settings.VideoPresetMappings = new System.Collections.Generic.List<VideoPresetMapping>();
            if (_settings.LightPoseSnapshots == null)
                _settings.LightPoseSnapshots = new System.Collections.Generic.List<LightPoseSnapshot>();
            if (_settings.NativeLight == null)
                _settings.NativeLight = new NativeLightSettings();
            var nlt = _settings.NativeLight;
            if (nlt.IntensityLoop == null) nlt.IntensityLoop = new LoopSettings { MinValue = 0f, MaxValue = 1f, SpeedHz = 0.5f };
            if (nlt.Rainbow       == null) nlt.Rainbow       = new RainbowSettings();
            if (nlt.Strobe        == null) nlt.Strobe        = new StrobeSettings();
            if (nlt.Beat          == null) nlt.Beat          = new BeatPresetAssignment();

            foreach (var li in _settings.Lights)
            {
                if (string.IsNullOrEmpty(li.Id))
                    li.Id = GenerateId();
                if (li.Rainbow      == null) li.Rainbow      = new RainbowSettings();
                if (li.Strobe       == null) li.Strobe       = new StrobeSettings();
                if (li.Beat         == null) li.Beat         = new BeatPresetAssignment();
                if (li.IntensityLoop == null) li.IntensityLoop = new LoopSettings { MinValue = 0.5f, MaxValue = 1.0f, SpeedHz = 0.3f };
                if (li.RangeLoop     == null) li.RangeLoop     = new LoopSettings { MinValue = 1f,   MaxValue = 10f };
                if (li.SpotAngleLoop == null) li.SpotAngleLoop = new LoopSettings { MinValue = 10f,  MaxValue = 60f };
                if (li.Mirrorball    == null) li.Mirrorball    = new MirrorballCookieSettings();

                li.SpotAngle = Mathf.Clamp(li.SpotAngle, 1f, 360f);
                li.InnerSpotAngle = Mathf.Clamp(li.InnerSpotAngle, 0f, 360f);
                li.SpotAngleLoop.MinValue = Mathf.Clamp(li.SpotAngleLoop.MinValue, 1f, 360f);
                li.SpotAngleLoop.MaxValue = Mathf.Clamp(li.SpotAngleLoop.MaxValue, 1f, 360f);

                li.Mirrorball.Resolution = QuantizeCookieResolution(li.Mirrorball.Resolution);
                li.Mirrorball.DotCount   = Mathf.Clamp(li.Mirrorball.DotCount, 1, 4096);
                li.Mirrorball.DotSize    = Mathf.Clamp(li.Mirrorball.DotSize, 0.002f, 0.45f);
                li.Mirrorball.Scatter    = Mathf.Clamp01(li.Mirrorball.Scatter);
                li.Mirrorball.Softness   = Mathf.Clamp01(li.Mirrorball.Softness);
                li.Mirrorball.SpinSpeed  = Mathf.Clamp(li.Mirrorball.SpinSpeed, -3f, 3f);
                li.Mirrorball.UpdateHz   = Mathf.Clamp(li.Mirrorball.UpdateHz, 0.5f, 30f);
                li.Mirrorball.Twinkle    = Mathf.Clamp01(li.Mirrorball.Twinkle);

                // 旧JSON移行: RevolutionCenter未設定(0,0,0)の場合はWorldPosで初期化
                if (li.RevolutionCenterX == 0f && li.RevolutionCenterY == 0f && li.RevolutionCenterZ == 0f)
                {
                    li.RevolutionCenterX = li.WorldPosX;
                    li.RevolutionCenterY = li.WorldPosY;
                    li.RevolutionCenterZ = li.WorldPosZ;
                }
            }

            foreach (var preset in _settings.Presets)
            {
                if (preset == null) continue;
                if (string.IsNullOrEmpty(preset.Id))
                    preset.Id = GenerateId();
                if (preset.Settings == null)
                    preset.Settings = new LightInstanceSettings();

                var ps = preset.Settings;
                if (ps.Rainbow       == null) ps.Rainbow       = new RainbowSettings();
                if (ps.Strobe        == null) ps.Strobe        = new StrobeSettings();
                if (ps.Beat          == null) ps.Beat          = new BeatPresetAssignment();
                if (ps.IntensityLoop == null) ps.IntensityLoop = new LoopSettings { MinValue = 0.5f, MaxValue = 1.0f, SpeedHz = 0.3f };
                if (ps.RangeLoop     == null) ps.RangeLoop     = new LoopSettings { MinValue = 1f,   MaxValue = 10f };
                if (ps.SpotAngleLoop == null) ps.SpotAngleLoop = new LoopSettings { MinValue = 10f,  MaxValue = 60f };
                if (ps.Mirrorball    == null) ps.Mirrorball    = new MirrorballCookieSettings();
            }
        }

        internal bool ReloadSettingsFromDisk(string reason = "manual")
        {
            try
            {
                bool prevUiVisible = _settings?.UiVisible ?? false;
                _settings = SettingsStore.Load(_settingsPath);
                _log.Info($"[Settings] ロード直後 lights={_settings?.Lights?.Count ?? -1} presets={_settings?.Presets?.Count ?? -1} path={_settingsPath}");
                EnsureSettingsValid();
                _settings.UiVisible = prevUiVisible;

                _expandedLights.Clear();
                _sliderTextBuf.Clear();
                _textFieldWasFocused.Clear();
                _presetNameBuf.Clear();
                _applyPresetId = "";
                _videoMapPathBuf = "";
                _videoMapPresetId = "";
                SyncProfileUiBuffersFromCurrent();

                DestroyAllLights();
                ClearNativeLightsOverride();

                if (_insideHScene)
                {
                    CacheNativeLights();
                    BuildLightObjects();
                    if (_settings.NativeLight.OverrideEnabled)
                        ApplyNativeLightOverride();
                }

                ResetBeatState();
                ResetVideoLinkBeatState();
                _log.Info($"[Settings] 設定呼び出し完了 reason={reason} profile={CurrentProfileName} lights={_settings.Lights.Count} presets={_settings.Presets.Count} maps={_settings.VideoPresetMappings.Count}");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error($"[Settings] 設定呼び出し失敗 reason={reason} ex={ex.Message}");
                return false;
            }
        }

        internal void SaveSettingsNow(string reason = "")
        {
            try
            {
                if (string.IsNullOrEmpty(_settingsPath))
                    _settingsPath = GetProfilePath(CurrentProfileName);

                if (SaveSettingsToPath(_settingsPath, reason))
                    SaveLastProfileName(CurrentProfileName);
            }
            catch (Exception ex)
            {
                _log.Error($"[Settings] 保存失敗 reason={reason} ex={ex.Message}");
            }
        }

        internal static string GenerateId()
        {
            return System.Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        // ── 参照位置取得（カメラ / HMD）────────────────────────────────────

        internal static Vector3 GetReferencePosition()
        {
            Camera cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        internal static Quaternion GetReferenceRotation()
        {
            Camera cam = Camera.main;
            return cam != null ? cam.transform.rotation : Quaternion.identity;
        }
    }
}
