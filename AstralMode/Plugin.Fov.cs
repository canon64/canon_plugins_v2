using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        // ============================================================
        // FOV演出
        //   originalFov: 復元専用ターゲット（ワープ開始瞬間の cam.fieldOfView）
        //   baseFov    : 倍率計算用ベース（FovBaseMode により originalFov か FovBaseValue）
        //
        //   Warp: target = Lerp(originalFov, FOV Warp Target)
        //   Hold: target = FOV Warp Target
        //   Fade: target = Lerp(FOV Warp Target, originalFov)
        //   Restore: target = originalFov（baseFov ではない）
        //
        //   全部 [FovMin, FovMax] でクランプ。
        // ============================================================

        private Camera _fovTargetCamera;
        private int _fovTargetCameraId;
        private float _fovOriginalValue;
        private float _fovBaseValue;
        private bool _fovCaptured;
        private float _lastFovTarget;
        private string _lastFovTargetSource;
        private int _fovDriveLogStep = -1;
        private int _fovFadeLogStep = -1;
        private int _fovOverrideLogFrame = -1;
        private const int FovDriveLogSteps = 10;
        // target>120 の最初の1回だけ allCameras 一覧をダンプする
        private bool _fovOver120Dumped;

        // Camera.onPreCull 経由の描画直前再代入用
        private bool _fovPreCullHookAttached;
        private bool _fovPreCullLoggedOver120;
        private bool _fovPreCullLoggedHold;
        private bool _fovPreCullLoggedFade;

        private static readonly Harmony _fovHarmony = new Harmony(Guid + ".fov.override");
        private static bool _fovOverridePatched;
        private static MethodInfo _fovOverridePatchedMethod;

        // MainGameCameraControl.Plugin の外部FOV API を動的 lookup。
        // Fast = SetCameraData を呼ばない軽量経路（毎フレ向け）
        // 既存 = SetCameraData も呼ぶ完全経路（Capture/Restore など低頻度向け、Fast 未存在時の fallback）
        private bool _mgccApiLookupDone;
        private MethodInfo _mgccApplyFastMethod;
        private MethodInfo _mgccApplyMethod;

        private void EnsureMainGameCameraControlApi()
        {
            if (_mgccApiLookupDone) return;
            _mgccApiLookupDone = true;

            Type t = FindType("MainGameCameraControl.Plugin", "MainGameCameraControl");
            if (t == null)
            {
                WarnExternalOnce("MGCC.Type.Missing", "MainGameCameraControl.Plugin type not found");
                return;
            }

            _mgccApplyFastMethod = t.GetMethod("TryApplyExternalFovFast",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[] { typeof(float), typeof(string) },
                null);

            _mgccApplyMethod = t.GetMethod("TryApplyExternalFov",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new Type[] { typeof(float), typeof(string) },
                null);

            if (_mgccApplyFastMethod == null)
                WarnExternalOnce("MGCC.FastMethod.Missing", "MainGameCameraControl.Plugin.TryApplyExternalFovFast(float,string) not found");
            if (_mgccApplyMethod == null)
                WarnExternalOnce("MGCC.Method.Missing", "MainGameCameraControl.Plugin.TryApplyExternalFov(float,string) not found");

            SimpleFileLogger.Info($"FOV_MGCC_API ready fast={(_mgccApplyFastMethod != null)} full={(_mgccApplyMethod != null)}");
        }

        // FOV書き込み統一関数。
        // 探索順: Fast → 既存 → fallback 直接書込
        private void WriteFovThroughCameraControl(float target, string reasonPhase, Camera fallbackCam)
        {
            EnsureMainGameCameraControlApi();

            // 1. Fast 経路（毎フレ向け軽量、SetCameraData 不要）
            if (_mgccApplyFastMethod != null)
            {
                try
                {
                    object result = _mgccApplyFastMethod.Invoke(null, new object[] { target, "AstralMode:" + reasonPhase });
                    if (result is bool ok && ok) return;
                }
                catch (Exception ex)
                {
                    WarnExternalOnce("MGCC.FastMethod.InvokeError", "TryApplyExternalFovFast invoke failed: " + ex.Message);
                }
            }

            // 2. 既存 TryApplyExternalFov（SetCameraData あり）
            if (_mgccApplyMethod != null)
            {
                try
                {
                    object result = _mgccApplyMethod.Invoke(null, new object[] { target, "AstralMode:" + reasonPhase });
                    if (result is bool ok && ok) return;
                }
                catch (Exception ex)
                {
                    WarnExternalOnce("MGCC.Method.InvokeError", "TryApplyExternalFov invoke failed: " + ex.Message);
                }
            }

            // 3. 直接 Camera.fieldOfView 書込み（最終 fallback）
            if (fallbackCam != null)
            {
                try { fallbackCam.fieldOfView = target; } catch { }
            }
        }

        // ---------------- カメラ特定 ----------------
        // 優先順位:
        //   1. _hSceneProc.ctrlCamera（H シーン専用カメラ）
        //   2. Camera.main
        //   3. allCameras から targetTexture==null かつ enabled かつ depth最大
        private Camera ResolveTargetCamera(out string source)
        {
            try
            {
                if (_hFlag != null)
                {
                    var ctrl = _hFlag.ctrlCamera;
                    if (ctrl != null)
                    {
                        Camera cam = ctrl.GetComponent<Camera>();
                        if (cam == null) cam = ctrl.GetComponentInChildren<Camera>();
                        if (cam != null && cam.gameObject != null)
                        {
                            source = "HFlag.ctrlCamera";
                            return cam;
                        }
                    }
                }
            }
            catch { }

            Camera main = Camera.main;
            if (main != null)
            {
                source = "Camera.main";
                return main;
            }

            Camera best = null;
            float bestDepth = float.NegativeInfinity;
            foreach (Camera c in Camera.allCameras)
            {
                if (c == null || !c.enabled) continue;
                if (c.targetTexture != null) continue;
                if (c.depth > bestDepth)
                {
                    best = c;
                    bestDepth = c.depth;
                }
            }
            source = best != null ? "AllCameras.maxDepth" : "null";
            return best;
        }

        private string DescribeCamera(Camera c)
        {
            if (c == null) return "null";
            try
            {
                string tag = "?";
                try { tag = c.gameObject != null ? c.gameObject.tag : "?"; } catch { }
                return $"{(c.gameObject != null ? c.gameObject.name : "?")}(id={c.GetInstanceID()}, tag={tag}, depth={c.depth:F1}, target={(c.targetTexture == null ? "Screen" : "RT")}, enabled={c.enabled})";
            }
            catch { return "?"; }
        }

        private void LogActiveCameras(string phase)
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                foreach (Camera c in Camera.allCameras)
                {
                    if (c == null) continue;
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(DescribeCamera(c));
                }
                SimpleFileLogger.Info($"FOV_INIT_CAMS phase={phase} active=[{sb}]");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"LogActiveCameras failed phase={phase}: {ex.Message}");
            }
        }

        // ---------------- 捕獲 / 復元 ----------------
        internal void CaptureFovsForWarp(string phase)
        {
            try
            {
                string source;
                Camera cam = ResolveTargetCamera(out source);
                if (cam == null)
                {
                    SimpleFileLogger.Warn($"FOV_INIT phase={phase} cam=null source={source}");
                    _fovCaptured = false;
                    return;
                }

                _fovTargetCamera = cam;
                _fovTargetCameraId = cam.GetInstanceID();
                _fovOriginalValue = cam.fieldOfView;

                FovBaseMode mode = _cfgFovBaseMode != null ? _cfgFovBaseMode.Value : FovBaseMode.CurrentFov;
                _fovBaseValue = mode == FovBaseMode.CurrentFov
                    ? _fovOriginalValue
                    : (_cfgFovBaseValue != null ? _cfgFovBaseValue.Value : _fovOriginalValue);

                _fovCaptured = true;
                _lastFovTarget = _fovOriginalValue;
                _fovDriveLogStep = -1;
                _fovFadeLogStep = -1;
                _fovOver120Dumped = false;
                _fovPreCullLoggedOver120 = false;
                _fovPreCullLoggedHold = false;
                _fovPreCullLoggedFade = false;

                SimpleFileLogger.Info(
                    $"FOV_INIT phase={phase} cam={DescribeCamera(cam)} source={source} baseMode={mode} originalFov={_fovOriginalValue:F2} baseFov={_fovBaseValue:F2}");
                LogActiveCameras(phase);

                EnsureCameraControlForceFovPatched();
                EnsureFovPreCullHook();
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"CaptureFovsForWarp failed phase={phase}: {ex.Message}");
                _fovCaptured = false;
            }
        }

        internal void RestoreOriginalFov(string phase)
        {
            if (!_fovCaptured) return;
            try
            {
                Camera cam = _fovTargetCamera;
                if (cam == null)
                {
                    SimpleFileLogger.Warn($"FOV_RESTORE phase={phase} cam=null");
                    _fovCaptured = false;
                    return;
                }

                float before = cam.fieldOfView;
                WriteFovThroughCameraControl(_fovOriginalValue, "restore:" + phase, cam);
                _lastFovTarget = _fovOriginalValue;

                SimpleFileLogger.Info($"FOV_RESTORE phase={phase} cam={DescribeCamera(cam)} from={before:F2} to=originalFov={_fovOriginalValue:F2}");

                _fovCaptured = false;
                _fovTargetCamera = null;
                _fovTargetCameraId = 0;
                DetachFovPreCullHook();
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"RestoreOriginalFov failed phase={phase}: {ex.Message}");
            }
        }

        // ---------------- 計算 ----------------
        private float ClampFov(float v)
        {
            float min = _cfgFovMin != null ? _cfgFovMin.Value : 1f;
            float max = _cfgFovMax != null ? _cfgFovMax.Value : 179f;
            if (max < min) { float t = min; min = max; max = t; }
            return Mathf.Clamp(v, min, max);
        }

        private float GetFovWarpTarget()
        {
            float target = _cfgFovWarpTarget != null ? _cfgFovWarpTarget.Value : 179f;
            return ClampFov(target);
        }

        private float ComputeWarpFov(float t01, out float windowT, out float mult, out float startTarget, out float endTarget)
        {
            EasingType ease = _cfgFovEasing != null ? _cfgFovEasing.Value : EasingType.Linear;
            float startT = _cfgFovStartT != null ? _cfgFovStartT.Value : 0f;
            float endT = _cfgFovEndT != null ? _cfgFovEndT.Value : 1f;

            // ToWhite: 元FOVから絶対ターゲットFOVへ向かう。
            // FOV Start T / End T で「いつ始まり、いつ終わるか」を決める。
            windowT = Easing.Window(ease, t01, startT, endT);
            startTarget = _fovOriginalValue;
            endTarget = GetFovWarpTarget();

            float fov = Mathf.Lerp(startTarget, endTarget, windowT);
            mult = Mathf.Approximately(_fovOriginalValue, 0f) ? 0f : (fov / _fovOriginalValue);
            return ClampFov(fov);
        }

        private float ComputeHoldFov(out float mult)
        {
            float target = GetFovWarpTarget();
            mult = Mathf.Approximately(_fovOriginalValue, 0f) ? 0f : (target / _fovOriginalValue);
            return target;
        }

        private float ComputeFadeFov(float fadeT, out float windowT, out float mult, out float startTarget, out float endTarget)
        {
            EasingType ease = _cfgFovFadeEasing != null ? _cfgFovFadeEasing.Value : EasingType.Linear;
            float startT = _cfgFovFadeStartT != null ? _cfgFovFadeStartT.Value : 0f;
            float endT = _cfgFovFadeEndT != null ? _cfgFovFadeEndT.Value : 1f;

            // FromWhite: 絶対ターゲットFOVから元FOVへ戻る。
            // FOV Fade Start T / End T で「いつ戻り始め、いつ戻り切るか」を決める。
            windowT = Easing.Window(ease, fadeT, startT, endT);
            startTarget = GetFovWarpTarget();
            endTarget = _fovOriginalValue;

            float fov = Mathf.Lerp(startTarget, endTarget, windowT);
            mult = Mathf.Approximately(_fovOriginalValue, 0f) ? 0f : (fov / _fovOriginalValue);
            return ClampFov(fov);
        }

        // ---------------- 適用 ----------------
        private void ApplyFov(float target, string phase, float windowT, float mult, float startTarget, float endTarget, float tForLog, ref int logStep)
        {
            if (!_fovCaptured) return;
            Camera cam = _fovTargetCamera;
            if (cam == null) return;

            float before = cam.fieldOfView;
            WriteFovThroughCameraControl(target, phase, cam);
            _lastFovTarget = target;
            _lastFovTargetSource = phase;

            int step = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(tForLog) * FovDriveLogSteps), 0, FovDriveLogSteps);
            if (step != logStep)
            {
                logStep = step;
                float after = cam.fieldOfView;
                Camera mainCam = Camera.main;
                SimpleFileLogger.Info(
                    $"FOV_DRIVE phase={phase} t={tForLog:F3} windowT={windowT:F3} mult={mult:F3} startTarget={startTarget:F2} endTarget={endTarget:F2} beforeFov={before:F2} target={target:F2} afterFov={after:F2} targetCam={DescribeCamera(cam)} mainCam={DescribeCamera(mainCam)}");
            }

            // target が 120 を超えた最初の 1 回だけ allCameras 一覧ダンプ
            if (!_fovOver120Dumped && target > 120.001f)
            {
                _fovOver120Dumped = true;
                LogActiveCameras("over-120-first phase=" + phase + " target=" + target.ToString("0.##"));
            }
        }

        // ---------------- Drive 公開 ----------------
        internal void DriveFovMixer(float t01)
        {
            if (_cfgEnableFovWarp != null && !_cfgEnableFovWarp.Value) return;
            if (!_fovCaptured) return;

            float windowT, mult, startTarget, endTarget;
            float fov = ComputeWarpFov(t01, out windowT, out mult, out startTarget, out endTarget);
            ApplyFov(fov, "warp", windowT, mult, startTarget, endTarget, t01, ref _fovDriveLogStep);
        }

        internal void DriveFovHold()
        {
            if (_cfgEnableFovWarp != null && !_cfgEnableFovWarp.Value) return;
            if (!_fovCaptured) return;

            float mult;
            float fov = ComputeHoldFov(out mult);
            int dummyStep = -1;
            ApplyFov(fov, "hold", 1f, mult, fov, fov, 0f, ref dummyStep);
        }

        internal void DriveFovFade(float fadeT)
        {
            if (_cfgEnableFovWarp != null && !_cfgEnableFovWarp.Value) return;
            if (!_fovCaptured) return;

            float windowT, mult, startTarget, endTarget;
            float fov = ComputeFadeFov(fadeT, out windowT, out mult, out startTarget, out endTarget);
            ApplyFov(fov, "fade", windowT, mult, startTarget, endTarget, fadeT, ref _fovFadeLogStep);
        }

        // ---------------- Force Override（CameraControl_Ver2.LateUpdate Postfix） ----------------
        // ゲーム本体の CameraControl が毎フレ fieldOfView を書き戻すケースに対応。
        // 対象タイプは実行時に対象カメラから component を取って動的特定。
        private void EnsureCameraControlForceFovPatched()
        {
            if (_fovOverridePatched) return;
            if (_fovTargetCamera == null) return;

            try
            {
                Component ctrl = _fovTargetCamera.GetComponent("CameraControl_Ver2") as Component;
                if (ctrl == null)
                {
                    SimpleFileLogger.Info("FOV_OVERRIDE_PATCH skipped: CameraControl_Ver2 not found on target camera");
                    return;
                }
                Type type = ctrl.GetType();
                MethodInfo target = type.GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (target == null)
                {
                    SimpleFileLogger.Warn("FOV_OVERRIDE_PATCH skipped: LateUpdate not found on " + type.FullName);
                    return;
                }
                MethodInfo postfix = AccessTools.Method(typeof(Plugin), nameof(CameraControl_LateUpdate_Postfix_ForceFov));
                if (postfix == null)
                {
                    SimpleFileLogger.Warn("FOV_OVERRIDE_PATCH skipped: postfix method not found");
                    return;
                }
                _fovHarmony.Patch(target, postfix: new HarmonyMethod(postfix));
                _fovOverridePatchedMethod = target;
                _fovOverridePatched = true;
                SimpleFileLogger.Info($"FOV_OVERRIDE_PATCH applied: {type.FullName}.LateUpdate");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn("FOV_OVERRIDE_PATCH failed: " + ex.Message);
            }
        }

        private static void CameraControl_LateUpdate_Postfix_ForceFov()
        {
            Plugin inst = Instance;
            if (inst == null) return;
            inst.ReapplyLastFovIfActive();
        }

        // ---- Camera.onPreCull 経由の描画直前再代入 ----
        // LateUpdate より後・描画直前に発火する。
        // ここで _lastFovTarget を対象カメラに再代入することで、
        // LateUpdate 後に第三者が fieldOfView を 120 に書き戻しても
        // 描画には我々の値が確実に使われる。
        private void EnsureFovPreCullHook()
        {
            if (_fovPreCullHookAttached) return;
            Camera.onPreCull += OnFovCameraPreCull;
            _fovPreCullHookAttached = true;
            SimpleFileLogger.Info("FOV_PRECULL hook attached");
        }

        internal void DetachFovPreCullHook()
        {
            if (!_fovPreCullHookAttached) return;
            try { Camera.onPreCull -= OnFovCameraPreCull; } catch { }
            _fovPreCullHookAttached = false;
            SimpleFileLogger.Info("FOV_PRECULL hook detached");
        }

        private void OnFovCameraPreCull(Camera cam)
        {
            if (!_fovCaptured) return;
            if (cam == null) return;
            if (cam.GetInstanceID() != _fovTargetCameraId) return;
            if (_cfgEnableFovWarp != null && !_cfgEnableFovWarp.Value) return;

            float before = cam.fieldOfView;
            cam.fieldOfView = _lastFovTarget;
            float after = cam.fieldOfView;

            // 1サイクル1回ずつの選択ログ
            if (!_fovPreCullLoggedOver120 && _lastFovTarget > 120.001f)
            {
                _fovPreCullLoggedOver120 = true;
                SimpleFileLogger.Info(
                    $"FOV_PRECULL phase=over-120-first state={_state} before={before:F2} target={_lastFovTarget:F2} after={after:F2} camId={_fovTargetCameraId}");
            }
            if (!_fovPreCullLoggedHold && (_state == AstralState.WhiteHold || _state == AstralState.ReturnHold))
            {
                _fovPreCullLoggedHold = true;
                SimpleFileLogger.Info(
                    $"FOV_PRECULL phase=hold-first state={_state} before={before:F2} target={_lastFovTarget:F2} after={after:F2} camId={_fovTargetCameraId}");
            }
            if (!_fovPreCullLoggedFade && (_state == AstralState.FadeIn || _state == AstralState.ReturnFadeOut))
            {
                _fovPreCullLoggedFade = true;
                SimpleFileLogger.Info(
                    $"FOV_PRECULL phase=fade-first state={_state} before={before:F2} target={_lastFovTarget:F2} after={after:F2} camId={_fovTargetCameraId}");
            }
        }

        internal void ReapplyLastFovIfActive()
        {
            if (!_fovCaptured) return;
            if (_cfgEnableFovWarp != null && !_cfgEnableFovWarp.Value) return;
            if (_cfgFovForceOverride != null && !_cfgFovForceOverride.Value) return;

            AstralState s = _state;
            if (s != AstralState.Warping
                && s != AstralState.WhiteHold
                && s != AstralState.FadeIn
                && s != AstralState.ReturnWarping
                && s != AstralState.ReturnHold
                && s != AstralState.ReturnFadeOut) return;

            Camera cam = _fovTargetCamera;
            if (cam == null) return;

            float before = cam.fieldOfView;
            if (Mathf.Approximately(before, _lastFovTarget)) return;

            WriteFovThroughCameraControl(_lastFovTarget, "force-override", cam);

            int frame = Time.frameCount;
            if (frame - _fovOverrideLogFrame >= 10)
            {
                _fovOverrideLogFrame = frame;
                SimpleFileLogger.Info($"FOV_OVERRIDE phase={s} beforePostfix={before:F2} reapplied={_lastFovTarget:F2}");
            }
        }
    }
}
