using System;
using System.Collections;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using CanonPlugins.Shared;
using UnityEngine;

namespace MainGameCircularDisplacement
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        public const string Guid = "com.kks.maingame.circulardisplacement";
        public const string PluginName = "MainGameCircularDisplacement";
        public const string Version = "0.2.0";

        public static Plugin Instance { get; private set; }

        private enum Mode { Off, Radial, Ripple, Swirl, Ring }
        private sealed class OverlayDrawer : MonoBehaviour
        {
            internal Plugin Owner;
            internal Camera SourceCamera;
            // ポストプロセス適用後の最終画面(src)を歪んだメッシュで dst に描き直す。
            // 別カメラキャプチャ不要・深度/カリング/PostProcessing TempBuffer の影響を受けない。
            private void OnRenderImage(RenderTexture src, RenderTexture dst)
            {
                if (Owner == null) { Graphics.Blit(src, dst); return; }
                Owner.OnImage(src, dst, SourceCamera);
            }
        }

        private ConfigEntry<bool> _enabled, _onlyHScene, _useScreenSize, _preferMain;
        private ConfigEntry<string> _layer, _cameraFilter, _fullSceneExcludeLayer;
        private ConfigEntry<int> _capW, _capH, _cameraIndex, _gridX, _gridY, _waveCount;
        private ConfigEntry<Mode> _mode;
        private ConfigEntry<float> _opacity, _cx, _cy, _radius, _inner, _strength, _falloff, _timeScale, _pulse;
        private ConfigEntry<float> _waveAmp, _waveSpeed, _wavePhase, _swirlStrength, _rotSpeed, _ringRadius, _ringWidth, _ringStrength, _ringSpeed;

        private string _jsonPath;
        private string _logPath;
        private float _nextDiagTime;
        private float _nextDrawDiagTime;
        private int _drawCalls, _drawSkips;
        private bool _loading, _queued;

        // 診断用: 楔が「描画範囲/深度」か「メッシュUV/RT合成」かを切り分ける単色フルスクリーン塗りテスト。
        private ConfigEntry<bool> _diagSolidFill;
        private Texture2D _diagRedTex;
        private Texture2D _diagGridTex; // 診断用 固定チェッカーボード（src の代わりに描いて経路を切り分ける）
        private HSceneProc _hscene;
        private float _nextScan;
        private Camera _captureCam, _sourceCam;
        private float _nextCameraSelectionTime;
        private string _selectedCameraFilter;
        private int _selectedCameraIndex = -1;
        private bool _selectedPreferMain;
        private GameObject _captureRoot;
        private OverlayDrawer _drawer;
        private RenderTexture _rt;
        private RenderTexture _outRt; // 歪んだメッシュをいったん描く中間RT（自前の深度でシーン深度の干渉を回避）
        private Mesh _mesh;
        private Material _mat;        // Chara/部分モード用（アルファブレンド）
        private Material _matOpaque;  // 全画面モード用（不透明＝キャプチャ画像のアルファに依存しない）

        // ===== B方式: 専用正射影カメラで自前RTに歪みメッシュを描く（本体カメラの投影/深度に巻き込まれない）=====
        private RenderTexture _warpRt;
        private GameObject _warpCamGO;
        private Camera _warpCam;
        private GameObject _warpMeshGO;
        private Mesh _warpMesh;
        private Material _warpMat;
        private int _warpMeshGX = -1, _warpMeshGY = -1;
        private const int WarpLayer = 31; // 専用レイヤー（本体ジオメトリと混ざらない）
        private int _rtW, _rtH, _meshW, _meshH, _meshGX = -1, _meshGY = -1;

        // External runtime control for AstralMode and other plugins.
        // These values do not touch ConfigEntry, so ConfigurationManager/json save will not churn every frame.
        private bool _externalActive;
        private float _externalUntil;
        private Mode _externalMode = Mode.Radial;
        private float _externalIntensity;
        private float _externalOpacity = 1f;
        private float _externalStrength = -1f;
        private float _externalRadius = -1f;
        private float _externalInner = -1f;
        private float _externalCenterX = 0.5f;
        private float _externalCenterY = 0.5f;
        // When true, capture the whole scene (source camera mask minus the excluded UI layer)
        // instead of only the Chara layer. Driven by AstralMode for full-screen ripple.
        private bool _externalFullScreen;

        public static bool IsAvailable => Instance != null;

        public static void SetExternalWarp(float intensity, string mode = "Radial")
        {
            SetExternalWarp(intensity, mode, 1f, -1f, -1f, -1f, 0.5f, 0.5f, 0.35f);
        }

        public static void SetExternalWarp(
            float intensity,
            string mode,
            float opacity,
            float strength,
            float radius,
            float innerRadius,
            float centerX,
            float centerY,
            float holdSeconds = 0.35f)
        {
            if (Instance == null) return;
            Instance.ApplyExternalWarp(intensity, mode, opacity, strength, radius, innerRadius, centerX, centerY, holdSeconds, false);
        }

        // Full-screen capable overload. fullScreen=true captures the whole scene (minus UI layer)
        // so the displacement covers the entire screen, not just the Chara layer.
        public static void SetExternalWarp(
            float intensity,
            string mode,
            float opacity,
            float strength,
            float radius,
            float innerRadius,
            float centerX,
            float centerY,
            float holdSeconds,
            bool fullScreen)
        {
            if (Instance == null) return;
            Instance.ApplyExternalWarp(intensity, mode, opacity, strength, radius, innerRadius, centerX, centerY, holdSeconds, fullScreen);
        }

        public static void ClearExternalWarp()
        {
            if (Instance == null) return;
            Instance._externalActive = false;
            Instance._externalIntensity = 0f;
            Instance._externalFullScreen = false;
        }

        private void ApplyExternalWarp(
            float intensity,
            string mode,
            float opacity,
            float strength,
            float radius,
            float innerRadius,
            float centerX,
            float centerY,
            float holdSeconds,
            bool fullScreen)
        {
            intensity = Mathf.Clamp01(intensity);
            if (intensity <= 0.0001f)
            {
                _externalActive = false;
                _externalIntensity = 0f;
                _externalFullScreen = false;
                return;
            }

            _externalFullScreen = fullScreen;

            Mode parsed;
            if (!Enum.TryParse(mode ?? "Radial", true, out parsed) || parsed == Mode.Off)
                parsed = Mode.Radial;

            _externalActive = true;
            _externalUntil = Time.unscaledTime + Mathf.Max(0.02f, holdSeconds);
            _externalMode = parsed;
            _externalIntensity = intensity;
            _externalOpacity = Mathf.Clamp01(opacity);
            _externalStrength = strength;
            _externalRadius = radius;
            _externalInner = innerRadius;
            _externalCenterX = Mathf.Clamp01(centerX);
            _externalCenterY = Mathf.Clamp01(centerY);
        }

        private bool IsExternalActiveNow()
        {
            if (!_externalActive) return false;
            if (Time.unscaledTime <= _externalUntil && _externalIntensity > 0.0001f) return true;
            _externalActive = false;
            _externalIntensity = 0f;
            return false;
        }

        private void Awake()
        {
            Instance = this;
            string dir = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
            _jsonPath = Path.Combine(dir, PluginName + ".json");
            _logPath = Path.Combine(dir, PluginName + ".log");
            if (_runtimeLoggingEnabled) { try { File.WriteAllText(_logPath, "", System.Text.Encoding.UTF8); } catch { } }
            Bind();
            LoadJson();
            SaveJson();
            Logger.LogInfo(PluginName + " " + Version + " loaded.");
            // どのシェーダーが build に含まれているか実機で確認する（_mat 生成の信頼性診断）。
            Log("Awake v" + Version
                + " shader[Unlit/Transparent]=" + (Shader.Find("Unlit/Transparent") != null)
                + " shader[Sprites/Default]=" + (Shader.Find("Sprites/Default") != null)
                + " shader[UI/Default]=" + (Shader.Find("UI/Default") != null)
                + " shader[Unlit/Texture]=" + (Shader.Find("Unlit/Texture") != null)
                + " shader[GUI/Text Shader]=" + (Shader.Find("GUI/Text Shader") != null));
        }

        private void Log(string msg)
        {
            if (!_runtimeLoggingEnabled) return;
            try { File.AppendAllText(_logPath, "[" + System.DateTime.Now.ToString("HH:mm:ss.fff") + "] " + msg + "\n", System.Text.Encoding.UTF8); }
            catch { }
        }

        private void Bind()
        {
            _enabled = Config.Bind("00.General", "Enabled", true, "Enable displacement overlay.");
            _onlyHScene = Config.Bind("00.General", "OnlyHScene", true, "Run only during H scene.");
            _layer = Config.Bind("01.Capture", "CharaLayerName", "Chara", "Layer captured in Chara mode (default / external non-fullscreen).");
            _fullSceneExcludeLayer = Config.Bind("01.Capture", "FullSceneExcludeLayer", "UI", "Layer excluded when an external caller requests full-screen capture (everything except this layer is captured).");
            _useScreenSize = Config.Bind("01.Capture", "UseScreenSize", true, "Use screen size for capture texture.");
            _capW = Config.Bind("01.Capture", "CaptureWidth", 960, new ConfigDescription("Used when UseScreenSize=false.", new AcceptableValueRange<int>(16, 8192)));
            _capH = Config.Bind("01.Capture", "CaptureHeight", 540, new ConfigDescription("Used when UseScreenSize=false.", new AcceptableValueRange<int>(16, 8192)));
            _diagSolidFill = Config.Bind("99.Diag", "SolidFillTest", false, "診断用: 歪みメッシュの代わりに同じDraw経路で赤の全画面塗りを行う。楔になるなら描画範囲/深度問題、全画面なるならUV/RT問題。");
            _preferMain = Config.Bind("02.Camera", "PreferCameraMain", false, "Prefer Camera.main.");
            _cameraFilter = Config.Bind("02.Camera", "CameraNameFilter", "", "Optional camera name filter.");
            _cameraIndex = Config.Bind("02.Camera", "CameraFallbackIndex", 2, new ConfigDescription("Fallback camera index.", new AcceptableValueRange<int>(0, 64)));

            _mode = Config.Bind("10.Displacement", "Mode", Mode.Ripple, "Off / Radial / Ripple / Swirl / Ring.");
            _gridX = Config.Bind("10.Displacement", "GridX", 48, new ConfigDescription("Horizontal grid.", new AcceptableValueRange<int>(2, 256)));
            _gridY = Config.Bind("10.Displacement", "GridY", 27, new ConfigDescription("Vertical grid.", new AcceptableValueRange<int>(2, 256)));
            _opacity = Config.Bind("10.Displacement", "Opacity", 1f, new ConfigDescription("Overlay opacity.", new AcceptableValueRange<float>(0f, 1f)));
            _cx = Config.Bind("10.Displacement", "CenterX", 0.5f, new ConfigDescription("Circle center X.", new AcceptableValueRange<float>(0f, 1f)));
            _cy = Config.Bind("10.Displacement", "CenterY", 0.5f, new ConfigDescription("Circle center Y.", new AcceptableValueRange<float>(0f, 1f)));
            _radius = Config.Bind("10.Displacement", "Radius", 2f, new ConfigDescription("Outer radius.", new AcceptableValueRange<float>(0.001f, 2f)));
            _inner = Config.Bind("10.Displacement", "InnerRadius", 0f, new ConfigDescription("Stable inner radius.", new AcceptableValueRange<float>(0f, 2f)));
            _strength = Config.Bind("10.Displacement", "Strength", 0.003f, new ConfigDescription("UV strength.", new AcceptableValueRange<float>(-0.5f, 0.5f)));
            _falloff = Config.Bind("10.Displacement", "FalloffPower", 2f, new ConfigDescription("Falloff power.", new AcceptableValueRange<float>(0.001f, 16f)));
            _timeScale = Config.Bind("10.Displacement", "TimeScale", 1f, new ConfigDescription("Time multiplier.", new AcceptableValueRange<float>(-20f, 20f)));
            _pulse = Config.Bind("10.Displacement", "Pulse", 1f, new ConfigDescription("Strength multiplier.", new AcceptableValueRange<float>(0f, 10f)));

            _waveCount = Config.Bind("20.Ripple", "WaveCount", 4, new ConfigDescription("Wave count.", new AcceptableValueRange<int>(1, 64)));
            _waveAmp = Config.Bind("20.Ripple", "WaveAmplitude", 1f, new ConfigDescription("Wave amplitude.", new AcceptableValueRange<float>(-10f, 10f)));
            _waveSpeed = Config.Bind("20.Ripple", "WaveSpeed", 1.5f, new ConfigDescription("Wave speed.", new AcceptableValueRange<float>(-20f, 20f)));
            _wavePhase = Config.Bind("20.Ripple", "WavePhase", 0f, new ConfigDescription("Wave phase.", new AcceptableValueRange<float>(-100f, 100f)));
            _swirlStrength = Config.Bind("30.Swirl", "SwirlStrength", 1f, new ConfigDescription("Swirl strength.", new AcceptableValueRange<float>(-10f, 10f)));
            _rotSpeed = Config.Bind("30.Swirl", "RotationSpeed", 1f, new ConfigDescription("Swirl animation speed.", new AcceptableValueRange<float>(-20f, 20f)));
            _ringRadius = Config.Bind("40.Ring", "RingRadius", 0.3f, new ConfigDescription("Ring radius.", new AcceptableValueRange<float>(0f, 2f)));
            _ringWidth = Config.Bind("40.Ring", "RingWidth", 0.08f, new ConfigDescription("Ring width.", new AcceptableValueRange<float>(0.001f, 2f)));
            _ringStrength = Config.Bind("40.Ring", "RingStrength", 1f, new ConfigDescription("Ring strength.", new AcceptableValueRange<float>(-10f, 10f)));
            _ringSpeed = Config.Bind("40.Ring", "RingSpeed", 0f, new ConfigDescription("Ring animation speed.", new AcceptableValueRange<float>(-20f, 20f)));

            Config.SettingChanged += (_, __) => { if (!_loading) { RebuildMesh(); SaveJson(); } };
        }

        // BlankMapAdd の追加マップ以外では一切発火させない。
        private bool _gateWasInRoom;

        private void LateUpdate()
        {
            // 部屋を出たら通常の停止経路を通し、掛かっていた歪みを外す。
            if (!BlankRoomGate.Tick(ref _gateWasInRoom, StopRuntime)) return;

            bool external = IsExternalActiveNow();
            Mode mode = external ? _externalMode : _mode.Value;
            bool diag = _runtimeLoggingEnabled && external && Time.unscaledTime >= _nextDiagTime;
            if (diag) _nextDiagTime = Time.unscaledTime + 1f;

            if ((!_enabled.Value && !external) || mode == Mode.Off) { if (diag) Log("LateUpdate bail: enabled=" + _enabled.Value + " external=" + external + " mode=" + mode); StopRuntime(); return; }
            if (_onlyHScene.Value && !IsHSceneActive()) { if (diag) Log("LateUpdate bail: OnlyHScene and HScene inactive"); StopRuntime(); return; }
            Camera src = ResolveCamera();
            if (src == null) { if (diag) Log("LateUpdate bail: no camera resolved"); return; }
            EnsureRuntime(src);
            // 描画は OnRenderImage(OnImage) 側で行う。capture camera/_rt は不要なので起動しない。
        }

        private void OnDestroy()
        {
            SaveJson();
            StopRuntime();
            if (Instance == this) Instance = null;
        }

        private bool IsHSceneActive()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                if (_hscene == null || !_hscene) _hscene = FindObjectOfType<HSceneProc>();
            }
            return _hscene != null && _hscene && _hscene.isActiveAndEnabled && _hscene.gameObject.activeInHierarchy && _hscene.flags != null;
        }

        private Camera ResolveCamera()
        {
            string filter = _cameraFilter.Value ?? "";
            bool hasFilter = filter.Length > 0;
            bool preferMain = _preferMain.Value;
            int index = _cameraIndex.Value;
            if (_sourceCam != null && _sourceCam.enabled && _sourceCam.gameObject.activeInHierarchy
                && Time.unscaledTime < _nextCameraSelectionTime
                && _selectedPreferMain == preferMain && _selectedCameraIndex == index
                && string.Equals(_selectedCameraFilter, filter, StringComparison.Ordinal))
                return _sourceCam;

            _nextCameraSelectionTime = Time.unscaledTime + 0.1f;
            _selectedPreferMain = preferMain;
            _selectedCameraIndex = index;
            _selectedCameraFilter = filter;
            Camera main = preferMain ? Camera.main : null;
            if (main != null && main.enabled && (!hasFilter || main.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)) return main;
            Camera[] all = Camera.allCameras;
            if (all == null || all.Length == 0) return null;
            Array.Sort(all, (a, b) => (b != null ? b.depth : -9999f).CompareTo(a != null ? a.depth : -9999f));
            int seen = 0;
            foreach (Camera c in all)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || c == _captureCam) continue;
                if (hasFilter && c.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (seen++ == index) return c;
            }
            foreach (Camera c in all)
                if (c != null && c.enabled && c.gameObject.activeInHierarchy && c != _captureCam) return c;
            return null;
        }

        // B方式では本体カメラに OverlayDrawer を付けて OnRenderImage を発火させるだけ。
        // 旧来の capture camera / _rt / _outRt / pixel-mesh は使わない（生成もしない）。
        private void EnsureRuntime(Camera src)
        {
            if (_sourceCam != src)
            {
                if (_drawer != null) Destroy(_drawer);
                _drawer = src.gameObject.AddComponent<OverlayDrawer>();
                _drawer.Owner = this;
                _drawer.SourceCamera = src;
                _sourceCam = src;
            }
        }

        private IEnumerator CaptureEndOfFrame(Camera src)
        {
            yield return new WaitForEndOfFrame();
            _queued = false;
            bool external = IsExternalActiveNow();
            Mode mode = external ? _externalMode : _mode.Value;
            if ((!_enabled.Value && !external) || mode == Mode.Off || src == null || _captureCam == null || _rt == null) yield break;
            if (_onlyHScene.Value && !IsHSceneActive()) yield break;
            _captureCam.CopyFrom(src);
            _captureCam.targetTexture = _rt;
            if (external && _externalFullScreen)
            {
                // Whole scene minus UI: keep the source camera's skybox/background so empty areas
                // are filled, then drop only the excluded (UI) layer.
                int uiLayer = LayerMask.NameToLayer(_fullSceneExcludeLayer.Value ?? "UI");
                int exclude = uiLayer >= 0 ? (1 << uiLayer) : 0;
                _captureCam.cullingMask = src.cullingMask & ~exclude;
                _captureCam.clearFlags = src.clearFlags;
                _captureCam.backgroundColor = src.backgroundColor;
            }
            else
            {
                _captureCam.clearFlags = CameraClearFlags.SolidColor;
                _captureCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _captureCam.cullingMask = LayerMask.GetMask(_layer.Value ?? "Chara");
            }
            _captureCam.Render();
            _captureCam.targetTexture = null;
        }

        private void Draw(Camera cam)
        {
            bool external = IsExternalActiveNow();
            Mode mode = external ? _externalMode : _mode.Value;
            // 全画面モードは不透明マテリアル、それ以外はアルファ用マテリアルを使う。
            bool fullScreen = external && _externalFullScreen;
            Material m = fullScreen ? (_matOpaque ?? _mat) : _mat;
            if (_rt == null || m == null || _mesh == null || (!_enabled.Value && !external) || mode == Mode.Off) { _drawSkips++; return; }
            _drawCalls++;
            int w = cam != null && cam.pixelWidth > 0 ? cam.pixelWidth : Screen.width;
            int h = cam != null && cam.pixelHeight > 0 ? cam.pixelHeight : Screen.height;
            EnsureMesh(w, h);

            // --- A/B 診断ログ（1秒に1回） ---
            if (_runtimeLoggingEnabled && Time.unscaledTime >= _nextDrawDiagTime)
            {
                _nextDrawDiagTime = Time.unscaledTime + 1f;
                RenderTexture active = RenderTexture.active;
                Log("DRAW DIAG"
                    + " | cam=" + (cam != null ? cam.name : "null")
                    + " depth=" + (cam != null ? cam.depth : -999f)
                    + " pixelRect=" + (cam != null ? cam.pixelRect.ToString() : "?")
                    + " rect=" + (cam != null ? cam.rect.ToString() : "?")
                    + " target=" + (cam != null && cam.targetTexture != null ? cam.targetTexture.name + "(" + cam.targetTexture.width + "x" + cam.targetTexture.height + ")" : "null")
                    + " clearFlags=" + (cam != null ? cam.clearFlags.ToString() : "?")
                    + " cullingMask=" + (cam != null ? cam.cullingMask.ToString() : "?")
                    + " | RT.active=" + (active != null ? active.name + "(" + active.width + "x" + active.height + ")" : "null")
                    + " | Screen=" + Screen.width + "x" + Screen.height
                    + " camPixel=" + (cam != null ? cam.pixelWidth + "x" + cam.pixelHeight : "?")
                    + " rt=" + (_rt != null ? _rt.width + "x" + _rt.height : "null")
                    + " outRt=" + (_outRt != null ? _outRt.width + "x" + _outRt.height : "null")
                    + " loadPixelMatrix_w/h=" + w + "x" + h
                    + " external=" + external + " fullScreen=" + fullScreen + " solidFill=" + (_diagSolidFill != null && _diagSolidFill.Value));
            }

            // --- C 切り分けテスト: 歪みメッシュの代わりに、同じ on-screen DrawMeshNow 経路で赤の全画面塗り ---
            if (_diagSolidFill != null && _diagSolidFill.Value)
            {
                if (_diagRedTex == null)
                {
                    _diagRedTex = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                    _diagRedTex.SetPixel(0, 0, new Color(1f, 0f, 0f, 1f));
                    _diagRedTex.Apply();
                }
                // 深度を無視する Graphics.DrawTexture で全画面赤塗り（残像が全画面に出るのと同じ描画）。
                // これが全画面になれば DrawTexture 経由なら深度問題を回避できると確定する。
                GL.PushMatrix();
                GL.LoadPixelMatrix(0f, w, h, 0f);
                Graphics.DrawTexture(new Rect(0f, 0f, w, h), _diagRedTex, new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0,
                    new Color(1f, 1f, 1f, 1f));
                GL.PopMatrix();
                return;
            }

            UpdateUv();
            float opacity = external ? _externalOpacity * _externalIntensity : _opacity.Value;

            // 画面へ直接 DrawMeshNow するとメインカメラのシーン深度に負けて「楔状」にしか描けない
            // （確認済み: DrawMeshNow=楔 / DrawTexture=全画面）。
            // そこで歪んだメッシュをいったん自前RT(_outRt)へ描き（自分の深度をクリア＝シーン深度の干渉ゼロで全面に描ける）、
            // それを深度無視の Graphics.DrawTexture で画面へ合成する。全画面/Chara 共通。
            RenderTexture prev = RenderTexture.active;
            Graphics.SetRenderTarget(_outRt);
            GL.Clear(true, true, new Color(0f, 0f, 0f, 0f));
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, w, h, 0f);
            m.mainTexture = _rt;
            m.color = Color.white;
            m.SetPass(0);
            Graphics.DrawMeshNow(_mesh, Matrix4x4.identity);
            GL.PopMatrix();
            Graphics.SetRenderTarget(prev);

            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, w, h, 0f);
            Graphics.DrawTexture(new Rect(0f, 0f, w, h), _outRt, new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0,
                new Color(1f, 1f, 1f, Mathf.Clamp01(opacity)));
            GL.PopMatrix();
        }

        // OnRenderImage 経路（正規の画像エフェクト）。src=最終画面、dst=出力先。
        // 【B方式】本体カメラへ直接メッシュは描かない。専用の正射影カメラで自前RT(_warpRt)に歪みメッシュを描き、
        // その _warpRt を Graphics.Blit(_warpRt, dst) で全画面合成する。本体カメラの投影/深度に巻き込まれない。
        internal void OnImage(RenderTexture src, RenderTexture dst, Camera cam)
        {
            bool external = IsExternalActiveNow();
            Mode mode = external ? _externalMode : _mode.Value;
            bool active = (_enabled.Value || external) && mode != Mode.Off;
            if (active && !external && _onlyHScene.Value && !IsHSceneActive()) active = false;

            int w = src != null && src.width > 0 ? src.width : Screen.width;
            int h = src != null && src.height > 0 ? src.height : Screen.height;

            if (_runtimeLoggingEnabled && Time.unscaledTime >= _nextDrawDiagTime)
            {
                _nextDrawDiagTime = Time.unscaledTime + 1f;
                Log("ONIMAGE(B/ortho) cam=" + (cam != null ? cam.name : "null")
                    + " active=" + active + " mode=" + mode
                    + " src=" + (src != null ? src.width + "x" + src.height : "null")
                    + " dst=" + (dst != null ? dst.width + "x" + dst.height : "null")
                    + " Strength=" + _strength.Value.ToString("0.###")
                    + " Radius=" + _radius.Value.ToString("0.###")
                    + " WaveCount=" + _waveCount.Value
                    + " warpRt=" + (_warpRt != null) + " warpCam=" + (_warpCam != null) + " warpMesh=" + (_warpMesh != null)
                    + " renders=" + _drawCalls);
                _drawCalls = 0;
            }

            if (!active)
            {
                Graphics.Blit(src, dst);
                return;
            }

            EnsureWarpRig(w, h);
            if (_warpRt == null || _warpCam == null || _warpMat == null || _warpMesh == null)
            {
                Graphics.Blit(src, dst); // リグ未完成時はそのまま通す（黒画面防止）
                return;
            }

            // 歪みメッシュに src を貼り、UVを歪ませる。
            _warpMat.mainTexture = src;
            _warpMat.color = Color.white;
            UpdateWarpUv();

            // 専用正射影カメラで _warpRt に描画（本体カメラ無関係・真正面・深度無関係）。
            _warpCam.targetTexture = _warpRt;
            _warpCam.Render();
            _warpCam.targetTexture = null;

            // 最終画面へは Blit のみ。最終画面へのメッシュ直描きは一切しない。
            Graphics.Blit(_warpRt, dst);
            _drawCalls++;
        }

        // ===== B方式の構築・描画ヘルパ =====
        private void EnsureWarpRig(int w, int h)
        {
            if (_warpRt == null || _warpRt.width != w || _warpRt.height != h)
            {
                if (_warpRt != null) { _warpRt.Release(); Destroy(_warpRt); _warpRt = null; }
                _warpRt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                _warpRt.Create();
            }

            if (_warpMat == null)
            {
                // 不透明テクスチャ描画（src をそのまま貼る）。
                Shader s = Shader.Find("Unlit/Texture") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Sprites/Default");
                if (s != null) { _warpMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave }; Log("EnsureWarpRig: warp shader=" + s.name); }
            }

            EnsureWarpMesh();

            if (_warpCamGO == null)
            {
                // 本体ジオメトリと混ざらないよう、遠方かつ専用レイヤーにリグを置く。
                _warpCamGO = new GameObject(PluginName + "WarpCam") { hideFlags = HideFlags.DontSave };
                UnityEngine.Object.DontDestroyOnLoad(_warpCamGO);
                _warpCamGO.transform.position = new Vector3(0f, -100000f, 0f);
                _warpCamGO.transform.rotation = Quaternion.identity; // forward = +Z

                _warpCam = _warpCamGO.AddComponent<Camera>();
                _warpCam.enabled = false;            // 手動 Render のみ
                _warpCam.orthographic = true;
                _warpCam.orthographicSize = 0.5f;    // 縦 -0.5..+0.5
                _warpCam.aspect = 1f;                // 横 -0.5..+0.5（RTへフィット）
                _warpCam.nearClipPlane = 0.1f;
                _warpCam.farClipPlane = 10f;
                _warpCam.clearFlags = CameraClearFlags.SolidColor;
                _warpCam.backgroundColor = new Color(0f, 0f, 0f, 1f);
                _warpCam.cullingMask = 1 << WarpLayer;
                _warpCam.allowHDR = false;
                _warpCam.allowMSAA = false;
                _warpCam.useOcclusionCulling = false;

                _warpMeshGO = new GameObject(PluginName + "WarpQuad") { hideFlags = HideFlags.DontSave };
                _warpMeshGO.transform.SetParent(_warpCamGO.transform, false);
                _warpMeshGO.transform.localPosition = new Vector3(0f, 0f, 1f);
                _warpMeshGO.transform.localRotation = Quaternion.identity;
                _warpMeshGO.layer = WarpLayer;
                _warpMeshGO.AddComponent<MeshFilter>().sharedMesh = _warpMesh;
                var mr = _warpMeshGO.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _warpMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }
            else
            {
                _warpMeshGO.GetComponent<MeshFilter>().sharedMesh = _warpMesh;
                _warpMeshGO.GetComponent<MeshRenderer>().sharedMaterial = _warpMat;
            }
            _warpCam.aspect = 1f;
        }

        private void EnsureWarpMesh()
        {
            int gx = Mathf.Clamp(_gridX.Value, 2, 256), gy = Mathf.Clamp(_gridY.Value, 2, 256);
            if (_warpMesh != null && _warpMeshGX == gx && _warpMeshGY == gy) return;
            if (_warpMesh != null) Destroy(_warpMesh);
            _warpMeshGX = gx; _warpMeshGY = gy;
            int vx = gx + 1, vy = gy + 1;
            Vector3[] v = new Vector3[vx * vy];
            Vector2[] uv = new Vector2[vx * vy];
            int[] tri = new int[gx * gy * 12]; // 両面
            int n = 0;
            for (int y = 0; y <= gy; y++) for (int x = 0; x <= gx; x++)
            {
                float nx = x / (float)gx, ny = y / (float)gy;
                v[n] = new Vector3(nx - 0.5f, ny - 0.5f, 0f); // -0.5..+0.5
                uv[n++] = new Vector2(nx, ny);
            }
            int t = 0;
            for (int y = 0; y < gy; y++) for (int x = 0; x < gx; x++)
            {
                int i0 = y * vx + x, i1 = i0 + 1, i2 = i0 + vx, i3 = i2 + 1;
                tri[t++] = i0; tri[t++] = i2; tri[t++] = i1; tri[t++] = i1; tri[t++] = i2; tri[t++] = i3;
                tri[t++] = i0; tri[t++] = i1; tri[t++] = i2; tri[t++] = i1; tri[t++] = i3; tri[t++] = i2;
            }
            _warpMesh = new Mesh { name = PluginName + "WarpMesh", hideFlags = HideFlags.HideAndDontSave, vertices = v, uv = uv, triangles = tri };
            _warpMesh.RecalculateBounds();
        }

        private void UpdateWarpUv()
        {
            if (_warpMesh == null) return;
            Vector2[] uv = _warpMesh.uv;
            int n = 0;
            for (int y = 0; y <= _warpMeshGY; y++) for (int x = 0; x <= _warpMeshGX; x++)
                uv[n++] = Distort(new Vector2(x / (float)_warpMeshGX, y / (float)_warpMeshGY));
            _warpMesh.uv = uv;
        }

        private void CleanupWarpRig()
        {
            if (_warpMeshGO != null) { Destroy(_warpMeshGO); _warpMeshGO = null; }
            if (_warpCamGO != null) { Destroy(_warpCamGO); _warpCamGO = null; }
            _warpCam = null;
            if (_warpMesh != null) { Destroy(_warpMesh); _warpMesh = null; }
            if (_warpMat != null) { Destroy(_warpMat); _warpMat = null; }
            if (_warpRt != null) { _warpRt.Release(); Destroy(_warpRt); _warpRt = null; }
            _warpMeshGX = -1; _warpMeshGY = -1;
        }

        private void EnsureMaterial()
        {
            if (_mat == null)
            {
                // アルファブレンド用（Chara限定モード：背景透明・キャラだけ）。
                Shader s = Shader.Find("Unlit/Transparent")
                    ?? Shader.Find("Sprites/Default")
                    ?? Shader.Find("UI/Default")
                    ?? Shader.Find("GUI/Text Shader");
                if (s != null)
                {
                    _mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
                    Log("EnsureMaterial: alpha shader=" + s.name);
                }
                else
                {
                    Log("EnsureMaterial: NO ALPHA SHADER FOUND");
                }
            }

            if (_matOpaque == null)
            {
                // 不透明用（全画面モード：キャプチャ画像のアルファに関係なく rgb をそのまま描く）。
                Shader so = Shader.Find("Unlit/Texture")
                    ?? Shader.Find("Mobile/Diffuse")
                    ?? Shader.Find("Unlit/Transparent");
                if (so != null)
                {
                    _matOpaque = new Material(so) { hideFlags = HideFlags.HideAndDontSave };
                    Log("EnsureMaterial: opaque shader=" + so.name);
                }
                else
                {
                    Log("EnsureMaterial: NO OPAQUE SHADER FOUND");
                }
            }
        }

        private void EnsureMesh(int w, int h)
        {
            int gx = Mathf.Clamp(_gridX.Value, 2, 256), gy = Mathf.Clamp(_gridY.Value, 2, 256);
            if (_mesh != null && _meshW == w && _meshH == h && _meshGX == gx && _meshGY == gy) return;
            if (_mesh != null) Destroy(_mesh);
            _meshW = w; _meshH = h; _meshGX = gx; _meshGY = gy;
            int vx = gx + 1, vy = gy + 1;
            Vector3[] v = new Vector3[vx * vy];
            Vector2[] uv = new Vector2[vx * vy];
            Color32[] col = new Color32[vx * vy];
            // 両面化（表裏両方の三角形を持たせる）。GL.LoadPixelMatrix のY反転で裏面になり
            // Cull Back のシェーダー(Unlit/Texture等)で全面カリングされて消えるのを防ぐ。
            int[] tri = new int[gx * gy * 12];
            int n = 0;
            for (int y = 0; y <= gy; y++) for (int x = 0; x <= gx; x++) { float nx = x / (float)gx, ny = y / (float)gy; v[n] = new Vector3(w * nx, h * ny, 0f); col[n] = new Color32(255, 255, 255, 255); uv[n++] = new Vector2(nx, 1f - ny); }
            int t = 0;
            for (int y = 0; y < gy; y++) for (int x = 0; x < gx; x++)
            {
                int i0 = y * (gx + 1) + x, i1 = i0 + 1, i2 = i0 + gx + 1, i3 = i2 + 1;
                // front
                tri[t++] = i0; tri[t++] = i2; tri[t++] = i1; tri[t++] = i1; tri[t++] = i2; tri[t++] = i3;
                // back (reversed winding)
                tri[t++] = i0; tri[t++] = i1; tri[t++] = i2; tri[t++] = i1; tri[t++] = i3; tri[t++] = i2;
            }
            _mesh = new Mesh { name = PluginName + "Grid", hideFlags = HideFlags.HideAndDontSave, vertices = v, uv = uv, colors32 = col, triangles = tri };
            _mesh.RecalculateBounds();
        }

        private void RebuildMesh() { if (_mesh != null) { Destroy(_mesh); _mesh = null; } }

        private void UpdateUv()
        {
            Vector2[] uv = _mesh.uv;
            int n = 0;
            for (int y = 0; y <= _meshGY; y++) for (int x = 0; x <= _meshGX; x++) uv[n++] = Distort(new Vector2(x / (float)_meshGX, 1f - y / (float)_meshGY));
            _mesh.uv = uv;
        }

        private Vector2 Distort(Vector2 uv)
        {
            bool external = IsExternalActiveNow();
            Mode mode = external ? _externalMode : _mode.Value;
            float cx = external ? _externalCenterX : _cx.Value;
            float cy = external ? _externalCenterY : _cy.Value;
            float radius = Mathf.Max(0.0001f, external && _externalRadius > 0f ? _externalRadius : _radius.Value);
            float inner = Mathf.Clamp(external && _externalInner >= 0f ? _externalInner : _inner.Value, 0f, radius);
            float baseStrength = external && Math.Abs(_externalStrength) > 0.000001f ? _externalStrength : _strength.Value;
            float pulse = external ? _externalIntensity : _pulse.Value;

            Vector2 d = uv - new Vector2(cx, cy);
            float r = d.magnitude;
            if (r > radius) return uv;
            Vector2 dir = r > 0.000001f ? d / r : Vector2.right;
            float mask = Mathf.Pow(Mathf.Clamp01(Mathf.InverseLerp(radius, inner, r)), Mathf.Max(0.001f, _falloff.Value));
            float time = Time.time * _timeScale.Value;
            float strength = baseStrength * pulse;
            switch (mode)
            {
                case Mode.Radial: return uv + dir * strength * mask;
                case Mode.Ripple: return uv + dir * Mathf.Sin(r * Mathf.Max(1, _waveCount.Value) * Mathf.PI * 2f - time * _waveSpeed.Value + _wavePhase.Value) * _waveAmp.Value * strength * mask;
                case Mode.Swirl: return uv + new Vector2(-dir.y, dir.x) * _swirlStrength.Value * Mathf.Sin(time * _rotSpeed.Value) * strength * mask;
                case Mode.Ring:
                    float rc = _ringRadius.Value + (Math.Abs(_ringSpeed.Value) > 0.0001f ? Mathf.Sin(time * _ringSpeed.Value) * 0.05f : 0f);
                    float ring = Mathf.Exp(-Mathf.Pow((r - rc) / Mathf.Max(0.0001f, _ringWidth.Value), 2f) * 4f);
                    return uv + dir * _ringStrength.Value * strength * ring;
                default: return uv;
            }
        }

        private void StopRuntime()
        {
            _queued = false;
            if (_drawer != null) Destroy(_drawer); _drawer = null; _sourceCam = null;
            if (_captureCam != null) _captureCam.targetTexture = null;
            if (_captureRoot != null) Destroy(_captureRoot); _captureRoot = null; _captureCam = null;
            ReleaseRt();
            if (_mesh != null) Destroy(_mesh); _mesh = null;
            if (_mat != null) Destroy(_mat); _mat = null;
            if (_matOpaque != null) Destroy(_matOpaque); _matOpaque = null;
            _meshGX = -1; _meshGY = -1;
            CleanupWarpRig();
        }

        private Texture2D EnsureGridTex()
        {
            if (_diagGridTex != null) return _diagGridTex;
            const int size = 256, cell = 32;
            _diagGridTex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool c = (((x / cell) + (y / cell)) & 1) == 0;
                    Color col = c ? new Color(1f, 1f, 1f, 1f) : new Color(0.12f, 0.12f, 0.12f, 1f);
                    // 向き確認用に四隅を着色
                    if (x < cell && y < cell) col = new Color(1f, 0f, 0f, 1f);            // 左下=赤
                    else if (x >= size - cell && y < cell) col = new Color(0f, 1f, 0f, 1f); // 右下=緑
                    else if (x < cell && y >= size - cell) col = new Color(0f, 0f, 1f, 1f); // 左上=青
                    else if (x >= size - cell && y >= size - cell) col = new Color(1f, 1f, 0f, 1f); // 右上=黄
                    _diagGridTex.SetPixel(x, y, col);
                }
            _diagGridTex.Apply();
            return _diagGridTex;
        }

        private void EnsureOutRt(int w, int h)
        {
            if (_outRt != null && _outRt.width == w && _outRt.height == h) return;
            if (_outRt != null) { _outRt.Release(); Destroy(_outRt); _outRt = null; }
            _outRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            _outRt.Create();
        }

        private void ReleaseRt()
        {
            if (_outRt != null) { _outRt.Release(); Destroy(_outRt); _outRt = null; }
            if (_rt == null) return;
            _rt.Release(); Destroy(_rt); _rt = null; _rtW = 0; _rtH = 0;
        }

        private void SaveJson()
        {
            if (_loading) return;
            try
            {
                StringBuilder b = new StringBuilder(); b.AppendLine("{");
                J(b,"Enabled",_enabled.Value); J(b,"OnlyHScene",_onlyHScene.Value); J(b,"CharaLayerName",_layer.Value); J(b,"FullSceneExcludeLayer",_fullSceneExcludeLayer.Value); J(b,"UseScreenSize",_useScreenSize.Value); J(b,"CaptureWidth",_capW.Value); J(b,"CaptureHeight",_capH.Value); J(b,"PreferCameraMain",_preferMain.Value); J(b,"CameraNameFilter",_cameraFilter.Value); J(b,"CameraFallbackIndex",_cameraIndex.Value);
                J(b,"Mode",_mode.Value.ToString()); J(b,"GridX",_gridX.Value); J(b,"GridY",_gridY.Value); J(b,"Opacity",_opacity.Value); J(b,"CenterX",_cx.Value); J(b,"CenterY",_cy.Value); J(b,"Radius",_radius.Value); J(b,"InnerRadius",_inner.Value); J(b,"Strength",_strength.Value); J(b,"FalloffPower",_falloff.Value); J(b,"TimeScale",_timeScale.Value); J(b,"Pulse",_pulse.Value);
                J(b,"WaveCount",_waveCount.Value); J(b,"WaveAmplitude",_waveAmp.Value); J(b,"WaveSpeed",_waveSpeed.Value); J(b,"WavePhase",_wavePhase.Value); J(b,"SwirlStrength",_swirlStrength.Value); J(b,"RotationSpeed",_rotSpeed.Value); J(b,"RingRadius",_ringRadius.Value); J(b,"RingWidth",_ringWidth.Value); J(b,"RingStrength",_ringStrength.Value); J(b,"RingSpeed",_ringSpeed.Value, false);
                b.AppendLine("}"); File.WriteAllText(_jsonPath, b.ToString(), Encoding.UTF8);
            }
            catch (Exception e) { Logger.LogWarning("json save failed: " + e.Message); }
        }

        private void LoadJson()
        {
            if (!File.Exists(_jsonPath)) return;
            _loading = true;
            try { string s = File.ReadAllText(_jsonPath, Encoding.UTF8); foreach (string p in s.Trim('{','}','\r','\n',' ').Split(',')) { int i = p.IndexOf(':'); if (i <= 0) continue; Set(Unq(p.Substring(0,i).Trim()), Unq(p.Substring(i+1).Trim())); } }
            catch (Exception e) { Logger.LogWarning("json load failed: " + e.Message); }
            finally { _loading = false; }
        }

        private void Set(string k, string v)
        {
            switch(k)
            {
                case "Enabled": _enabled.Value=B(v,_enabled.Value); break; case "OnlyHScene": _onlyHScene.Value=B(v,_onlyHScene.Value); break; case "CharaLayerName": _layer.Value=v; break; case "FullSceneExcludeLayer": _fullSceneExcludeLayer.Value=v; break; case "UseScreenSize": _useScreenSize.Value=B(v,_useScreenSize.Value); break; case "CaptureWidth": _capW.Value=I(v,_capW.Value); break; case "CaptureHeight": _capH.Value=I(v,_capH.Value); break; case "PreferCameraMain": _preferMain.Value=B(v,_preferMain.Value); break; case "CameraNameFilter": _cameraFilter.Value=v; break; case "CameraFallbackIndex": _cameraIndex.Value=I(v,_cameraIndex.Value); break;
                case "Mode": _mode.Value=E(v,_mode.Value); break; case "GridX": _gridX.Value=I(v,_gridX.Value); break; case "GridY": _gridY.Value=I(v,_gridY.Value); break; case "Opacity": _opacity.Value=F(v,_opacity.Value); break; case "CenterX": _cx.Value=F(v,_cx.Value); break; case "CenterY": _cy.Value=F(v,_cy.Value); break; case "Radius": _radius.Value=F(v,_radius.Value); break; case "InnerRadius": _inner.Value=F(v,_inner.Value); break; case "Strength": _strength.Value=F(v,_strength.Value); break; case "FalloffPower": _falloff.Value=F(v,_falloff.Value); break; case "TimeScale": _timeScale.Value=F(v,_timeScale.Value); break; case "Pulse": _pulse.Value=F(v,_pulse.Value); break;
                case "WaveCount": _waveCount.Value=I(v,_waveCount.Value); break; case "WaveAmplitude": _waveAmp.Value=F(v,_waveAmp.Value); break; case "WaveSpeed": _waveSpeed.Value=F(v,_waveSpeed.Value); break; case "WavePhase": _wavePhase.Value=F(v,_wavePhase.Value); break; case "SwirlStrength": _swirlStrength.Value=F(v,_swirlStrength.Value); break; case "RotationSpeed": _rotSpeed.Value=F(v,_rotSpeed.Value); break; case "RingRadius": _ringRadius.Value=F(v,_ringRadius.Value); break; case "RingWidth": _ringWidth.Value=F(v,_ringWidth.Value); break; case "RingStrength": _ringStrength.Value=F(v,_ringStrength.Value); break; case "RingSpeed": _ringSpeed.Value=F(v,_ringSpeed.Value); break;
            }
        }

        private static void J(StringBuilder b,string k,string v,bool c=true){b.Append("  \"").Append(k).Append("\": \"").Append((v??"").Replace("\\","\\\\").Replace("\"","\\\"")).Append('"'); if(c)b.Append(','); b.AppendLine();}
        private static void J(StringBuilder b,string k,bool v,bool c=true){b.Append("  \"").Append(k).Append("\": ").Append(v?"true":"false"); if(c)b.Append(','); b.AppendLine();}
        private static void J(StringBuilder b,string k,int v,bool c=true){b.Append("  \"").Append(k).Append("\": ").Append(v); if(c)b.Append(','); b.AppendLine();}
        private static void J(StringBuilder b,string k,float v,bool c=true){b.Append("  \"").Append(k).Append("\": ").Append(v.ToString("0.######",System.Globalization.CultureInfo.InvariantCulture)); if(c)b.Append(','); b.AppendLine();}
        private static string Unq(string s){s=s.Trim(); if(s.StartsWith("\"")&&s.EndsWith("\""))s=s.Substring(1,s.Length-2); return s.Replace("\\\"","\"").Replace("\\\\","\\");}
        private static bool B(string s,bool d){return string.Equals(s,"true",StringComparison.OrdinalIgnoreCase)?true:string.Equals(s,"false",StringComparison.OrdinalIgnoreCase)?false:d;}
        private static int I(string s,int d){int v; return int.TryParse(s,out v)?v:d;}
        private static float F(string s,float d){float v; return float.TryParse(s,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out v)?v:d;}
        private static T E<T>(string s,T d) where T:struct {T v; return Enum.TryParse(s,true,out v)?v:d;}
    }
}
