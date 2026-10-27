using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using CanonPlugins.Shared;
using UnityEngine;
using UnityEngine.XR;
using VRGIN.Core;

namespace MainGameVrAfterimageLab
{
    // VR残像の「左右に正しい出し方」を実機で探すための実験台。
    // 方式・パラメータをすべてConfig化し、毎フレーム読み直すので
    // ConfigManagerで値を変えた瞬間に次の描画から反映される（再ビルド・再起動不要）。
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.maingame.vrafterimagelab";
        public const string PluginName = "MainGameVrAfterimageLab";
        public const string Version = "0.1.0";

        public static Plugin Instance { get; private set; }

        // Runtime
        private Camera _captureCamera;
        private Camera _lastSourceCamera;
        private GameObject _cameraRoot;
        private OverlayDrawer _overlayDrawer;
        private RenderTexture[] _slotsL;   // 左目（Monoでは共通バッファとして使う）
        private RenderTexture[] _slotsR;   // 右目（StereoPerEye時のみ使う）
        private int[] _life;
        private int _writeIndex;
        private int _frameCounter;
        private int _characterMask;
        private int _rtWidth;
        private int _rtHeight;
        private bool _runtimeReady;

        private HSceneProc _hSceneProc;
        private float _nextHSceneScan;
        // 診断ログは「初めて見た状態」だけ出す（毎フレーム/毎秒のスパムを避ける）。
        private readonly HashSet<string> _loggedCaptureSig = new HashSet<string>();

        private void Awake()
        {
            Instance = this;
            string dir = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
            SetupConfig();
            SimpleFileLogger.Init(Path.Combine(dir, PluginName + ".log"), _cfgVerboseLog.Value);
            SimpleFileLogger.Info("=== Awake v" + Version + " ===");
            ApplyConfig();
            Logger.LogInfo(PluginName + " " + Version + " loaded.");
        }

        // BlankMapAdd の追加マップ以外では一切発火させない。
        private bool _gateWasInRoom;

        private void LateUpdate()
        {
            // 部屋を出たら通常の停止経路を通し、残像バッファを解放する。
            if (!BlankRoomGate.Tick(ref _gateWasInRoom, StopRuntime)) return;

            if (!IsEffectivelyEnabled() || !IsActiveScene())
            {
                StopRuntime();
                return;
            }

            if (!_runtimeReady || _slotsL == null)
                ApplyConfig();
            if (_slotsL == null)
                return;

            // VR解像度（目テクスチャ）変化を検知してバッファを作り直す
            if (_cfgUseScreenSize.Value)
            {
                bool vr = VR.Active && XRSettings.eyeTextureWidth > 0;
                int w = vr ? XRSettings.eyeTextureWidth : Screen.width;
                int h = vr ? XRSettings.eyeTextureHeight : Screen.height;
                if (w != _rtWidth || h != _rtHeight) ApplyConfig();
            }

            Camera src = ResolveCamera();
            SyncOverlayDrawer(src);

            // 寿命を1フレーム分減らす
            for (int i = 0; i < _life.Length; i++)
                if (_life[i] > 0) _life[i]--;

            _frameCounter++;
            int interval = Mathf.Max(1, _cfgCaptureInterval.Value);
            if (src != null && (interval <= 1 || (_frameCounter % interval) == 0))
                StartCoroutine(CaptureEndOfFrame(src));
        }

        private void OnDestroy()
        {
            if (_overlayDrawer != null) Destroy(_overlayDrawer);
            if (_cameraRoot != null) Destroy(_cameraRoot);
            ReleaseSlots();
            if (Instance == this) Instance = null;
            SimpleFileLogger.Info("OnDestroy");
        }

        private bool IsEffectivelyEnabled() => _cfgEnabled != null && _cfgEnabled.Value;

        private bool IsActiveScene()
        {
            if (_cfgOnlyHScene == null || !_cfgOnlyHScene.Value) return true;
            return IsHSceneActive();
        }

        private bool IsHSceneActive()
        {
            if (Time.unscaledTime >= _nextHSceneScan)
            {
                _nextHSceneScan = Time.unscaledTime + 0.5f;
                if (_hSceneProc == null || !_hSceneProc)
                    _hSceneProc = FindObjectOfType<HSceneProc>();
            }
            return _hSceneProc != null && _hSceneProc && _hSceneProc.isActiveAndEnabled
                && _hSceneProc.gameObject.activeInHierarchy && _hSceneProc.flags != null;
        }

        private Camera ResolveCamera()
        {
            if (VR.Active && VR.Camera != null && VR.Camera.SteamCam != null)
            {
                Camera vc = ((Component)VR.Camera.SteamCam).GetComponent<Camera>();
                if (vc != null) return vc;
            }

            string filter = _cfgCameraNameFilter.Value ?? "";
            bool hasFilter = filter.Length > 0;
            if (_cfgPreferMain.Value && Camera.main != null && Camera.main.enabled)
            {
                if (!hasFilter || Camera.main.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    return Camera.main;
            }

            Camera[] all = Camera.allCameras;
            if (all == null || all.Length == 0) return null;
            List<Camera> list = new List<Camera>(all.Length);
            foreach (Camera c in all)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                if (c == _captureCamera) continue;
                list.Add(c);
            }
            if (list.Count == 0) return null;
            list.Sort((a, b) => b.depth.CompareTo(a.depth));
            if (hasFilter)
                foreach (Camera c in list)
                    if (c.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) return c;
            int idx = Mathf.Clamp(_cfgCameraFallbackIndex.Value, 0, list.Count - 1);
            return list[idx];
        }

        private void SyncOverlayDrawer(Camera src)
        {
            if (src == null)
            {
                if (_overlayDrawer != null) { Destroy(_overlayDrawer); _overlayDrawer = null; }
                _lastSourceCamera = null;
                return;
            }
            if (_lastSourceCamera != src)
            {
                if (_overlayDrawer != null) Destroy(_overlayDrawer);
                _overlayDrawer = src.gameObject.AddComponent<OverlayDrawer>();
                _overlayDrawer.Owner = this;
                _lastSourceCamera = src;
            }
        }

        private void StopRuntime()
        {
            if (_overlayDrawer != null) { Destroy(_overlayDrawer); _overlayDrawer = null; }
            _lastSourceCamera = null;
            if (_slotsL != null || _slotsR != null) ReleaseSlots();
            if (_cameraRoot != null) { Destroy(_cameraRoot); _cameraRoot = null; _captureCamera = null; }
            _runtimeReady = false;
            _frameCounter = 0;
        }
    }
}
