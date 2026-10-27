using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using CanonPlugins.Shared;
using MainGameUnifiedUi;
using UnityEngine;

namespace MainGameKaleidoscope
{
    [BepInPlugin(Guid, "MainGameKaleidoscope", "1.0.0")]
    [BepInProcess("KoikatsuSunshine")]
    [BepInDependency("com.kks.maingame.unifiedui", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.kks.maingame.subcameradisplayprobe", BepInDependency.DependencyFlags.HardDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.maingame.kaleidoscope";
        public static Plugin Instance { get; private set; }
        private const string TabId = "kaleidoscope";
        private const string ShaderAssetPath = "assets/kaleidoscope.shader";
        private AssetBundle _bundle;
        private Shader _shader;
        private float _strength;
        private bool _manualActive;
        private bool _thoughtActive;
        private bool _wasInRoom;
        private float _clock;
        private float _saveAt;
        private bool _configDirty;

        private ConfigEntry<float> _fadeIn, _fadeOut, _rotationSpeed, _rotationOffset, _zoom;
        private ConfigEntry<float> _centerX, _centerY, _hueSpeed, _hueOffset, _saturation, _brightness;
        private ConfigEntry<float> _swirl, _wave, _waveSpeed, _radialRepeat, _chromatic, _strengthLimit;
        private ConfigEntry<float> _sourceZoom, _sourceCenterX, _sourceCenterY, _sourceRotationDegrees, _sourceQuality;
        private ConfigEntry<float> _baseRed, _baseGreen, _baseBlue, _vignetteStrength;
        private ConfigEntry<float> _vignetteInner, _vignetteOuter, _vignetteCenterX, _vignetteCenterY;
        private ConfigEntry<float> _vignetteRed, _vignetteGreen, _vignetteBlue;
        private ConfigEntry<int> _segments;
        private ConfigEntry<bool> _manualPersist;
        private ConfigEntry<bool> _sourceIncludeBackground;

        private void Awake()
        {
            Instance = this;
            Config.SaveOnConfigSet = false;
            BindSettings();
            InitializeAudioDucking();
            Config.SettingChanged += (sender, args) => MarkConfigDirty();
            string bundlePath = Path.Combine(Path.GetDirectoryName(Info.Location), "Resources", "kaleidoscope.bundle");
            try
            {
                _bundle = AssetBundle.LoadFromFile(bundlePath);
                _shader = _bundle?.LoadAsset<Shader>(ShaderAssetPath);
                if (_shader == null) Logger.LogError("Kaleidoscope shader unavailable: " + bundlePath);
            }
            catch (Exception ex) { Logger.LogError("Kaleidoscope shader load failed: " + ex); }
        }

        private void Update()
        {
            if (_configDirty && Time.unscaledTime >= _saveAt)
            {
                Config.Save();
                _configDirty = false;
            }
            if (UnifiedUiApi.IsAvailable && !UnifiedUiApi.IsHosting(TabId))
                UnifiedUiApi.RegisterTab(TabId, "万華鏡", DrawTab, 74);

            bool inRoom = BlankRoomGate.IsInBlankRoom;
            if (_wasInRoom && !inRoom && !_manualPersist.Value) _manualActive = false;
            _wasInRoom = inRoom;
            if (!inRoom)
            {
                _thoughtActive = false;
            }

            bool target = (_manualActive || (inRoom && _thoughtActive)) && _shader != null;
            float duration = target ? _fadeIn.Value : _fadeOut.Value;
            _strength = duration <= 0.001f ? (target ? 1f : 0f)
                : Mathf.MoveTowards(_strength, target ? 1f : 0f, Time.unscaledDeltaTime / duration);
            _clock += Time.unscaledDeltaTime;
            // Afterimage captures at end of frame. Set the actor mask before that
            // capture, including manual display and the fade-out interval.
            ChaControl calledActor = null;
            if (_strength > .0001f)
                MainGameSubCameraDisplayProbe.MainGameSubCameraDisplayProbeApi.TryGetKaleidoscopeSource(
                    out Camera _, out calledActor, out string _);
            SimpleAfterimage.Plugin.SetKaleidoscopeThoughtMask(_strength > .0001f, calledActor);
            UpdateScreenEffect(_strength > .0001f);
        }

        internal void SetThoughtActive(bool active)
        {
            if (_thoughtActive != active)
                Logger.LogInfo("Thought effect " + (active ? "started" : "ended")
                    + " room=" + BlankRoomGate.IsInBlankRoom + " shader=" + (_shader != null));
            _thoughtActive = active;
        }

        public bool IsManualActive() => _manualActive;

        public void ToggleManualActive() => _manualActive = !_manualActive;

        private void LateUpdate()
        {
            UpdateAudioDucking(BlankRoomGate.IsInBlankRoom && _thoughtActive);
        }

        private void OnDestroy()
        {
            SimpleAfterimage.Plugin.SetKaleidoscopeThoughtMask(false, null);
            UnifiedUiApi.UnregisterTab(TabId);
            DisposeScreenEffect();
            RestoreAudioDucking();
            if (_configDirty) Config.Save();
            if (_bundle != null) _bundle.Unload(false);
            if (Instance == this) Instance = null;
        }

        private void BindSettings()
        {
            _fadeIn = F("01.Transition", "FadeInSeconds", 2f, 0f, 8f, "万華鏡へのフェード秒数");
            _fadeOut = F("01.Transition", "FadeOutSeconds", 2f, 0f, 8f, "通常画面へ戻るフェード秒数");
            _strengthLimit = F("01.Transition", "Intensity", 1f, 0f, 1f, "万華鏡の最大混合率");
            _segments = Config.Bind("02.Geometry", "Segments", 8, new ConfigDescription("鏡面の分割数", new AcceptableValueRange<int>(2, 32)));
            _rotationSpeed = F("02.Geometry", "RotationSpeed", .12f, -3f, 3f, "回転速度 ラジアン毎秒");
            _rotationOffset = F("02.Geometry", "RotationOffset", 0f, -6.283f, 6.283f, "回転の開始角");
            _zoom = F("02.Geometry", "Zoom", 1f, .25f, 4f, "映像の拡大率");
            _centerX = F("02.Geometry", "CenterX", .5f, 0f, 1f, "中心位置 X");
            _centerY = F("02.Geometry", "CenterY", .5f, 0f, 1f, "中心位置 Y");
            _swirl = F("02.Geometry", "Swirl", 0f, -4f, 4f, "半径に応じたねじれ");
            _radialRepeat = F("02.Geometry", "RadialRepeat", 1f, 1f, 6f, "半径方向の繰り返し");
            _wave = F("02.Geometry", "WaveAmplitude", 0f, 0f, .25f, "波の歪み");
            _waveSpeed = F("02.Geometry", "WaveSpeed", 1f, 0f, 8f, "波の進行速度");
            _hueOffset = F("03.Color", "HueOffset", 0f, -1f, 1f, "色相のずれ");
            _hueSpeed = F("03.Color", "HueSpeed", .05f, -1f, 1f, "色相変化 毎秒");
            _saturation = F("03.Color", "Saturation", 1f, 0f, 3f, "彩度");
            _brightness = F("03.Color", "Brightness", 1f, 0f, 3f, "明るさ");
            _chromatic = F("03.Color", "ChromaticOffset", 0f, 0f, .03f, "色収差");
            _manualPersist = Config.Bind("04.Surfaces", "ManualAcrossRooms", false, "手動ONを部屋移動後も保持");
            _sourceZoom = F("04.Background", "SourceZoom", 1f, .25f, 4f, "万華鏡に取り込む素材の拡大度");
            _sourceCenterX = F("04.Background", "SourceCenterX", .5f, 0f, 1f, "素材中心 X");
            _sourceCenterY = F("04.Background", "SourceCenterY", .5f, 0f, 1f, "素材中心 Y");
            _sourceRotationDegrees = F("04.Background", "SourceRotationDegrees", 0f, -180f, 180f, "万華鏡に取り込む素材映像の回転角度（度）");
            _sourceIncludeBackground = Config.Bind("04.Background", "IncludeSubCameraBackground", false,
                "サブカメラの背景も万華鏡の素材に含める（OFF は女性像／呼び出しキャラのみ）");
            _sourceQuality = F("04.Background", "SourceQuality", .5f, .25f, 1f, "女性像を取り込む解像度比");
            _baseRed = F("04.Background", "BaseRed", .02f, 0f, 1f, "背景色の赤");
            _baseGreen = F("04.Background", "BaseGreen", .01f, 0f, 1f, "背景色の緑");
            _baseBlue = F("04.Background", "BaseBlue", .07f, 0f, 1f, "背景色の青");
            _vignetteStrength = F("04.Background", "VignetteStrength", .85f, 0f, 1f, "万華鏡表示中に画面周辺を暗くする強さ");
            _vignetteInner = F("04.Background", "VignetteInnerRadius", .25f, 0f, 1.2f, "ビネットが暗くなり始める半径");
            _vignetteOuter = F("04.Background", "VignetteOuterRadius", .75f, 0f, 1.5f, "ビネットが最大濃度になる半径");
            _vignetteCenterX = F("04.Background", "VignetteCenterX", .5f, 0f, 1f, "ビネット中心の横位置");
            _vignetteCenterY = F("04.Background", "VignetteCenterY", .5f, 0f, 1f, "ビネット中心の縦位置");
            _vignetteRed = F("04.Background", "VignetteRed", 0f, 0f, 1f, "ビネット色の赤");
            _vignetteGreen = F("04.Background", "VignetteGreen", 0f, 0f, 1f, "ビネット色の緑");
            _vignetteBlue = F("04.Background", "VignetteBlue", 0f, 0f, 1f, "ビネット色の青");
        }

        private ConfigEntry<float> F(string section, string key, float value, float min, float max, string description)
            => Config.Bind(section, key, value, new ConfigDescription(description, new AcceptableValueRange<float>(min, max)));

        private void DrawTab()
        {
            GUILayout.Label("サブカメラの女性／呼び出しキャラを画面の2D背景に配置。壁の材質は変更しません。");
            if (_shader == null) GUILayout.Label("シェーダーを読み込めません。Resources/kaleidoscope.bundle を確認してください。");
            if (GUILayout.Button(_manualActive ? "万華鏡をOFF" : "万華鏡をON", GUILayout.Height(32f)))
                ToggleManualActive();
            GUILayout.Label("思考トリガー: " + (_thoughtActive ? "ON" : "OFF") + "   描画濃度: " + _strength.ToString("0.00"));
            GUILayout.Label("素材: " + _screenStatus);
            GUILayout.Label("フェード");
            Slider("入る時間 (秒)", _fadeIn, 0f, 8f);
            Slider("戻る時間 (秒)", _fadeOut, 0f, 8f);
            Slider("最大濃度", _strengthLimit, 0f, 1f);
            GUILayout.Label("ビネット");
            Slider("濃度", _vignetteStrength, 0f, 1f);
            Slider("暗くなり始める半径", _vignetteInner, 0f, 1.2f);
            Slider("最大濃度の半径", _vignetteOuter, 0f, 1.5f);
            Slider("中心 X", _vignetteCenterX, 0f, 1f);
            Slider("中心 Y", _vignetteCenterY, 0f, 1f);
            Slider("色 赤", _vignetteRed, 0f, 1f);
            Slider("色 緑", _vignetteGreen, 0f, 1f);
            Slider("色 青", _vignetteBlue, 0f, 1f);
            GUILayout.Label("形状と動き");
            IntSlider("鏡面の数", _segments, 2, 32);
            Slider("回転速度", _rotationSpeed, -3f, 3f);
            Slider("回転開始角", _rotationOffset, -6.283f, 6.283f);
            Slider("拡大率", _zoom, .25f, 4f);
            Slider("中心 X", _centerX, 0f, 1f);
            Slider("中心 Y", _centerY, 0f, 1f);
            Slider("ねじれ", _swirl, -4f, 4f);
            Slider("半径方向の反復", _radialRepeat, 1f, 6f);
            Slider("波の歪み", _wave, 0f, .25f);
            Slider("波の速度", _waveSpeed, 0f, 8f);
            GUILayout.Label("色");
            Slider("色相", _hueOffset, -1f, 1f);
            Slider("色相の速度", _hueSpeed, -1f, 1f);
            Slider("彩度", _saturation, 0f, 3f);
            Slider("明るさ", _brightness, 0f, 3f);
            Slider("色収差", _chromatic, 0f, .03f);
            GUILayout.Label("背景素材");
            Toggle("サブカメラの背景も取り込む", _sourceIncludeBackground);
            Slider("素材拡大度", _sourceZoom, .25f, 4f);
            Slider("素材中心 X", _sourceCenterX, 0f, 1f);
            Slider("素材中心 Y", _sourceCenterY, 0f, 1f);
            Slider("素材回転角度 (度)", _sourceRotationDegrees, -180f, 180f);
            Slider("取込解像度比", _sourceQuality, .25f, 1f);
            Slider("背景色 赤", _baseRed, 0f, 1f);
            Slider("背景色 緑", _baseGreen, 0f, 1f);
            Slider("背景色 青", _baseBlue, 0f, 1f);
            Toggle("手動ONを部屋移動後も保持", _manualPersist);
            DrawAudioSettings();
        }

        private void Toggle(string label, ConfigEntry<bool> entry)
        {
            bool next = GUILayout.Toggle(entry.Value, label);
            if (next != entry.Value) { entry.Value = next; MarkConfigDirty(); }
        }

        private void Slider(string label, ConfigEntry<float> entry, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(165f));
            float next = GUILayout.HorizontalSlider(entry.Value, min, max, GUILayout.Width(300f));
            GUILayout.Label(entry.Value.ToString("0.000"), GUILayout.Width(70f));
            GUILayout.EndHorizontal();
            if (Mathf.Abs(next - entry.Value) > .0001f) { entry.Value = next; MarkConfigDirty(); }
        }

        private void IntSlider(string label, ConfigEntry<int> entry, int min, int max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(165f));
            int next = Mathf.RoundToInt(GUILayout.HorizontalSlider(entry.Value, min, max, GUILayout.Width(300f)));
            GUILayout.Label(entry.Value.ToString(), GUILayout.Width(70f));
            GUILayout.EndHorizontal();
            if (next != entry.Value) { entry.Value = next; MarkConfigDirty(); }
        }

        private void MarkConfigDirty()
        {
            _configDirty = true;
            _saveAt = Time.unscaledTime + .5f;
        }
    }
}
