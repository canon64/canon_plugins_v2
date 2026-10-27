using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.XR;
using VRGIN.Core;

namespace MainGameVrAfterimageLab
{
    public sealed partial class Plugin
    {
        // 当たり探しの主役。実機で見比べて選ぶ。
        internal enum EyeMode { Mono, StereoPerEye }
        internal enum CompositeHook { PostRender, RenderImage }
        internal enum CaptureScope { CharaOnly, FullScreen }

        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgOnlyHScene;
        private ConfigEntry<bool> _cfgVerboseLog;

        private ConfigEntry<EyeMode> _cfgEyeMode;
        private ConfigEntry<CompositeHook> _cfgCompositeHook;
        private ConfigEntry<CaptureScope> _cfgScope;
        private ConfigEntry<string> _cfgCharaLayer;
        private ConfigEntry<string> _cfgExcludeLayer;

        private ConfigEntry<int> _cfgFadeFrames;
        private ConfigEntry<int> _cfgMaxSlots;
        private ConfigEntry<int> _cfgCaptureInterval;
        private ConfigEntry<float> _cfgAlphaScale;
        private ConfigEntry<float> _cfgTintR;
        private ConfigEntry<float> _cfgTintG;
        private ConfigEntry<float> _cfgTintB;
        private ConfigEntry<float> _cfgTintA;
        private ConfigEntry<bool> _cfgHideNewest;

        private ConfigEntry<bool> _cfgUseScreenSize;
        private ConfigEntry<int> _cfgCaptureWidth;
        private ConfigEntry<int> _cfgCaptureHeight;
        private ConfigEntry<bool> _cfgPreferMain;
        private ConfigEntry<string> _cfgCameraNameFilter;
        private ConfigEntry<int> _cfgCameraFallbackIndex;

        private void SetupConfig()
        {
            const string c0 = "00.General";
            const string c1 = "10.Method";
            const string c2 = "20.Trail";
            const string c3 = "30.Camera";

            _cfgEnabled = Config.Bind(c0, "Enabled", true, "全体ON/OFF");
            _cfgOnlyHScene = Config.Bind(c0, "OnlyHScene", false, "Hシーン中だけ動かす。falseで常時(VRで試しやすい)");
            _cfgVerboseLog = Config.Bind(c0, "VerboseLog", false, "詳細ログ");

            _cfgEyeMode = Config.Bind(c1, "EyeMode", EyeMode.StereoPerEye,
                "Mono=片目の絵を両目へ同じ貼り(今のSimpleと同じ=ベースライン) / StereoPerEye=左右の目の視点で別々に撮り各目へ");
            _cfgCompositeHook = Config.Bind(c1, "CompositeHook", CompositeHook.PostRender,
                "残像を重ねるフック。PostRender / RenderImage。実機で効く方を選ぶ");
            _cfgScope = Config.Bind(c1, "CaptureScope", CaptureScope.CharaOnly,
                "CharaOnly=キャラだけ / FullScreen=除外レイヤー以外の全画面");
            _cfgCharaLayer = Config.Bind(c1, "CharaLayer", "Chara", "CharaOnly時のキャプチャ対象レイヤー名");
            _cfgExcludeLayer = Config.Bind(c1, "ExcludeLayer", "UI", "FullScreen時に除外するレイヤー名");

            _cfgFadeFrames = Config.Bind(c2, "FadeFrames", 60, new ConfigDescription("残像寿命フレーム", new AcceptableValueRange<int>(1, 300)));
            _cfgMaxSlots = Config.Bind(c2, "MaxSlots", 60, new ConfigDescription("残像枚数", new AcceptableValueRange<int>(1, 300)));
            _cfgCaptureInterval = Config.Bind(c2, "CaptureInterval", 1, new ConfigDescription("撮影間隔(1=毎フレーム)", new AcceptableValueRange<int>(1, 60)));
            _cfgAlphaScale = Config.Bind(c2, "AlphaScale", 0.3f, new ConfigDescription("濃さ(0..1)", new AcceptableValueRange<float>(0f, 1f)));
            _cfgTintR = Config.Bind(c2, "TintR", 1f, new ConfigDescription("色R", new AcceptableValueRange<float>(0f, 1f)));
            _cfgTintG = Config.Bind(c2, "TintG", 1f, new ConfigDescription("色G", new AcceptableValueRange<float>(0f, 1f)));
            _cfgTintB = Config.Bind(c2, "TintB", 1f, new ConfigDescription("色B", new AcceptableValueRange<float>(0f, 1f)));
            _cfgTintA = Config.Bind(c2, "TintA", 1f, new ConfigDescription("色A(全体不透明度)", new AcceptableValueRange<float>(0f, 1f)));
            _cfgHideNewest = Config.Bind(c2, "HideNewestFrame", true, "最新フレーム(今の絵)を残像から除外する");

            _cfgUseScreenSize = Config.Bind(c3, "UseScreenSize", true, "解像度に画面/VR目テクスチャを使う");
            _cfgCaptureWidth = Config.Bind(c3, "CaptureWidth", 0, new ConfigDescription("UseScreenSize=false時の幅", new AcceptableValueRange<int>(0, 8192)));
            _cfgCaptureHeight = Config.Bind(c3, "CaptureHeight", 0, new ConfigDescription("UseScreenSize=false時の高さ", new AcceptableValueRange<int>(0, 8192)));
            _cfgPreferMain = Config.Bind(c3, "PreferCameraMain", true, "非VR時にCamera.mainを優先");
            _cfgCameraNameFilter = Config.Bind(c3, "CameraNameFilter", "", "カメラ名フィルタ(部分一致)");
            _cfgCameraFallbackIndex = Config.Bind(c3, "CameraFallbackIndex", 0, new ConfigDescription("候補カメラのフォールバックindex", new AcceptableValueRange<int>(0, 64)));

            // ConfigManagerで値を変えたら、その場でバッファ要否を見直す（リアルタイム反映）。
            Config.SettingChanged += (_, __) =>
            {
                SimpleFileLogger.Enabled = _cfgVerboseLog.Value;
                ApplyConfig();
            };
        }

        private void ApplyConfig()
        {
            if (!IsEffectivelyEnabled() || !CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom || !IsActiveScene())
            {
                StopRuntime();
                return;
            }

            _characterMask = LayerMask.GetMask(_cfgCharaLayer.Value ?? "Chara");

            int slots = Mathf.Clamp(_cfgMaxSlots.Value, 1, 300);
            bool vr = VR.Active && XRSettings.eyeTextureWidth > 0;
            int w = _cfgUseScreenSize.Value
                ? (vr ? XRSettings.eyeTextureWidth : Screen.width)
                : Mathf.Max(16, _cfgCaptureWidth.Value);
            int h = _cfgUseScreenSize.Value
                ? (vr ? XRSettings.eyeTextureHeight : Screen.height)
                : Mathf.Max(16, _cfgCaptureHeight.Value);
            w = Mathf.Max(16, w);
            h = Mathf.Max(16, h);

            bool stereoBuffers = _cfgEyeMode.Value == EyeMode.StereoPerEye;
            bool rebuild = _slotsL == null || _slotsL.Length != slots || _rtWidth != w || _rtHeight != h
                || (stereoBuffers ? _slotsR == null : _slotsR != null);
            if (rebuild)
            {
                ReleaseSlots();
                _slotsL = new RenderTexture[slots];
                _slotsR = stereoBuffers ? new RenderTexture[slots] : null;
                _life = new int[slots];
                for (int i = 0; i < slots; i++)
                {
                    _slotsL[i] = CreateRT(w, h);
                    if (stereoBuffers) _slotsR[i] = CreateRT(w, h);
                }
                _rtWidth = w;
                _rtHeight = h;
                _writeIndex = 0;
                _frameCounter = 0;
                SimpleFileLogger.Info("slots rebuilt " + w + "x" + h + " x" + slots + " vr=" + vr);
            }

            if (_cameraRoot == null) SetupCaptureCamera();
            _runtimeReady = _slotsL != null && _captureCamera != null;
        }
    }
}
