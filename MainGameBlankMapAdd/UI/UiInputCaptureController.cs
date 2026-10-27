using System;
using System.Collections;
using MainGameBlankMapAdd.Application;
using UnityEngine;

namespace MainGameBlankMapAdd.UI
{
    internal sealed class UiInputCaptureController : IDisposable
    {
        private const float ReleaseGraceSeconds = 0.15f;

        private readonly ICoroutineHost _coroutines;
        private readonly Func<bool> _isGizmoDragging;
        private readonly Func<bool> _isPlaybackDragging;
        private readonly Func<bool> _shouldKeepEditCursorUnlocked;
        private readonly Action<string> _log;
        private bool _active;
        private bool _hasPrevCtrlCameraCursorLock;
        private bool _prevCtrlCameraCursorLock;
        private bool _hasPrevCtrlCameraNoCtrl;
        private BaseCameraControl_Ver2.NoCtrlFunc _prevCtrlCameraNoCtrl;
        private bool _hasPrevCtrlCameraKeyCondition;
        private BaseCameraControl_Ver2.NoCtrlFunc _prevCtrlCameraKeyCondition;
        private bool _hasPrevGameCursorLock;
        private bool _prevGameCursorLock;
        private bool _hasPrevActionSceneCursorLock;
        private bool _prevActionSceneCursorLock;
        private HFlag _cachedHFlag;
        private CameraControl_Ver2 _cachedCtrlCamera;
        private CameraControl_Ver2 _capturedCtrlCamera;
        private ActionScene _capturedActionScene;
        private bool _gizmoDragHint;
        private Coroutine _endOfFrameRoutine;
        private float _holdUntil;

        internal UiInputCaptureController(
            ICoroutineHost coroutines,
            Func<bool> isGizmoDragging,
            Func<bool> isPlaybackDragging,
            Func<bool> shouldKeepEditCursorUnlocked,
            Action<string> log)
        {
            _coroutines = coroutines ?? throw new ArgumentNullException(nameof(coroutines));
            _isGizmoDragging = isGizmoDragging ?? throw new ArgumentNullException(nameof(isGizmoDragging));
            _isPlaybackDragging = isPlaybackDragging ?? throw new ArgumentNullException(nameof(isPlaybackDragging));
            _shouldKeepEditCursorUnlocked = shouldKeepEditCursorUnlocked ?? throw new ArgumentNullException(nameof(shouldKeepEditCursorUnlocked));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        internal void UpdateCapture()
        {
            if (!UnityEngine.Application.isFocused)
            {
                Stop("focus lost");
                return;
            }
            IsExternalCaptureActive(); // Resolve the shared API once.
            if (_sharedSync != null) {
                _sharedSync("MainGameBlankMapAdd.Local", "gizmo-drag", ShouldCapture(out _));
                _sharedIdle?.Invoke("MainGameBlankMapAdd.Local", _shouldKeepEditCursorUnlocked());
                return;
            }
            bool wantCapture = ShouldCapture(out string reason);
            float now = Time.unscaledTime;
            if (wantCapture)
            {
                _holdUntil = now + ReleaseGraceSeconds;
                if (!_active) Start(reason);
                TickCapture();
            }
            else if (_active && now < _holdUntil)
            {
                TickCapture();
            }
            else if (HasCapturedState() || NeedsRecovery())
            {
                Stop("ui interaction ended");
            }

        }

        /// <summary>カメラ側の退避値だけを捨てる。実際の書き戻しは行わない。</summary>
        private void ClearSavedCameraState()
        {
            _hasPrevCtrlCameraCursorLock = false;
            _hasPrevCtrlCameraNoCtrl = false;
            _hasPrevCtrlCameraKeyCondition = false;
        }

        // ── 他プラグインの掴み判定（参照を増やさないよう reflection で1回だけ解決）──

        private static System.Func<bool> _externalCaptureReader;
        private static Action<string, string, bool> _sharedSync;
        private static Func<string, bool, bool> _sharedIdle;
        private static bool _externalCaptureResolved;

        private static bool IsExternalCaptureActive()
        {
            if (!_externalCaptureResolved)
            {
                _externalCaptureResolved = true;
                try
                {
                    System.Reflection.Assembly[] asms = System.AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < asms.Length; i++)
                    {
                        System.Type t;
                        try { t = asms[i].GetType("MainGameUiInputCapture.UiInputCaptureApi", false); }
                        catch { continue; }
                        if (t == null) continue;

                        System.Reflection.MethodInfo m = t.GetMethod(
                            "IsAnyActive",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                            null, System.Type.EmptyTypes, null);
                        if (m == null || m.ReturnType != typeof(bool)) continue;

                        _externalCaptureReader =
                            (System.Func<bool>)System.Delegate.CreateDelegate(typeof(System.Func<bool>), m);
                        var sync = t.GetMethod("Sync", new[] { typeof(string), typeof(string), typeof(bool) });
                        var idle = t.GetMethod("SetIdleCursorUnlock", new[] { typeof(string), typeof(bool) });
                        if (sync != null) _sharedSync = (Action<string, string, bool>)Delegate.CreateDelegate(typeof(Action<string, string, bool>), sync);
                        if (idle != null) _sharedIdle = (Func<string, bool, bool>)Delegate.CreateDelegate(typeof(Func<string, bool, bool>), idle);
                        break;
                    }
                }
                catch { _externalCaptureReader = null; }
            }

            if (_externalCaptureReader == null) return false;
            try { return _externalCaptureReader(); }
            catch { return false; }
        }

        internal void NotifyGizmoDragState(bool dragging)
        {
            _gizmoDragHint = dragging;
            UpdateCapture();
        }

        internal void Stop(string reason)
        {
            if (_sharedSync != null) {
                _sharedSync("MainGameBlankMapAdd.Local", "gizmo-drag", false);
                _sharedIdle?.Invoke("MainGameBlankMapAdd.Local", false);
                _gizmoDragHint = false;
                StopEndOfFrameRoutine();
                return;
            }
            if (!HasCapturedState() && !NeedsRecovery()) return;

            CameraControl_Ver2 ctrl = _capturedCtrlCamera ?? ResolveCtrlCamera();

            // MainGameUiInputCapture が今まさに掴んでいる最中は、カメラのスイッチに触らない。
            // ここと向こうは同じ NoCtrlCondition / KeyCondition を書く二重の口で、
            // 掴んだ直後に片方が解除側へ倒すと、UIやギズモを操作している最中に
            // カメラがマウスを持って行く。掴んでいる側が勝つ。
            if (ctrl != null && IsExternalCaptureActive())
            {
                ClearSavedCameraState();
                ctrl = null;
            }

            if (ctrl != null)
            {
                if (_hasPrevCtrlCameraCursorLock) ctrl.isCursorLock = _prevCtrlCameraCursorLock;
                if (IsNoCtrlFunc(ctrl.NoCtrlCondition, AlwaysTrue))
                    ctrl.NoCtrlCondition = _hasPrevCtrlCameraNoCtrl ? _prevCtrlCameraNoCtrl : AlwaysFalse;
                if (IsNoCtrlFunc(ctrl.KeyCondition, AlwaysFalse))
                    ctrl.KeyCondition = _hasPrevCtrlCameraKeyCondition ? _prevCtrlCameraKeyCondition : AlwaysTrue;
            }

            GameCursor gameCursor = Singleton<GameCursor>.Instance;
            if (gameCursor != null && _hasPrevGameCursorLock)
            {
                gameCursor.UnLockCursor();
                GameCursor.isDraw = true;
            }

            ActionScene actionScene = _capturedActionScene;
            if (actionScene != null && _hasPrevActionSceneCursorLock)
                actionScene.SetCursorLock(_prevActionSceneCursorLock);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            ClearCapturedState();
            StopEndOfFrameRoutine();
            _log($"ui capture OFF ({reason})");
        }

        private bool ShouldCapture(out string reason)
        {
            bool gizmoDragging = _gizmoDragHint || _isGizmoDragging();
            bool playbackDragging = _isPlaybackDragging();
            if (gizmoDragging && playbackDragging) { reason = "gizmo+playback-drag"; return true; }
            if (gizmoDragging) { reason = "gizmo-drag"; return true; }
            if (playbackDragging) { reason = "playback-drag"; return true; }
            reason = string.Empty;
            return false;
        }

        private bool HasCapturedState()
        {
            return _active
                || _hasPrevCtrlCameraCursorLock
                || _hasPrevCtrlCameraNoCtrl
                || _hasPrevCtrlCameraKeyCondition
                || _hasPrevGameCursorLock
                || _hasPrevActionSceneCursorLock;
        }

        private void Start(string reason)
        {
            _active = true;
            ResetPreviousStateFlags();
            CaptureCurrentState();
            EnsureEndOfFrameRoutine();
            _log($"ui capture ON ({reason})");
        }

        private void CaptureCurrentState()
        {
            CameraControl_Ver2 ctrl = ResolveCtrlCamera();
            _capturedCtrlCamera = ctrl;
            if (ctrl != null)
            {
                _prevCtrlCameraCursorLock = ctrl.isCursorLock;
                _hasPrevCtrlCameraCursorLock = true;
                _prevCtrlCameraNoCtrl = ctrl.NoCtrlCondition;
                _hasPrevCtrlCameraNoCtrl = true;
                _prevCtrlCameraKeyCondition = ctrl.KeyCondition;
                _hasPrevCtrlCameraKeyCondition = true;
            }

            GameCursor gameCursor = Singleton<GameCursor>.Instance;
            if (gameCursor != null)
            {
                _prevGameCursorLock = GameCursor.isLock;
                _hasPrevGameCursorLock = true;
            }

            ActionScene actionScene = SingletonInitializer<ActionScene>.instance;
            _capturedActionScene = actionScene;
            if (actionScene != null)
            {
                _prevActionSceneCursorLock = actionScene.isCursorLock;
                _hasPrevActionSceneCursorLock = true;
            }
        }

        private void TickCapture()
        {
            if (!UnityEngine.Application.isFocused) return;
            CameraControl_Ver2 ctrl = ResolveCtrlCamera();
            if (_capturedCtrlCamera != ctrl)
            {
                if (_capturedCtrlCamera != null)
                {
                    if (_hasPrevCtrlCameraCursorLock) _capturedCtrlCamera.isCursorLock = _prevCtrlCameraCursorLock;
                    if (IsNoCtrlFunc(_capturedCtrlCamera.NoCtrlCondition, AlwaysTrue))
                        _capturedCtrlCamera.NoCtrlCondition = _hasPrevCtrlCameraNoCtrl ? _prevCtrlCameraNoCtrl : AlwaysFalse;
                    if (IsNoCtrlFunc(_capturedCtrlCamera.KeyCondition, AlwaysFalse))
                        _capturedCtrlCamera.KeyCondition = _hasPrevCtrlCameraKeyCondition ? _prevCtrlCameraKeyCondition : AlwaysTrue;
                }
                ClearSavedCameraState();
                _capturedCtrlCamera = ctrl;
            }
            if (ctrl != null)
            {
                if (!_hasPrevCtrlCameraCursorLock)
                {
                    _prevCtrlCameraCursorLock = ctrl.isCursorLock;
                    _hasPrevCtrlCameraCursorLock = true;
                }
                if (!_hasPrevCtrlCameraNoCtrl)
                {
                    _prevCtrlCameraNoCtrl = ctrl.NoCtrlCondition;
                    _hasPrevCtrlCameraNoCtrl = true;
                }
                if (!_hasPrevCtrlCameraKeyCondition)
                {
                    _prevCtrlCameraKeyCondition = ctrl.KeyCondition;
                    _hasPrevCtrlCameraKeyCondition = true;
                }
                // 順序が重要。GlobalMethod.SetCameraMoveFlag は NoCtrlCondition を
                // 独自ラムダ (() => !_bPlay) で丸ごと差し替えるため、後から呼ぶと
                // 我々の目印 (AlwaysTrue) が消え、NeedsRecovery の自己修復が永久に空振りする。
                GlobalMethod.SetCameraMoveFlag(ctrl, _bPlay: false);
                ctrl.isCursorLock = false;
                ctrl.NoCtrlCondition = AlwaysTrue;
                ctrl.KeyCondition = AlwaysFalse;
            }

            GameCursor gameCursor = Singleton<GameCursor>.Instance;
            if (gameCursor != null)
            {
                if (!_hasPrevGameCursorLock)
                {
                    _prevGameCursorLock = GameCursor.isLock;
                    _hasPrevGameCursorLock = true;
                }
                gameCursor.UnLockCursor();
                GameCursor.isDraw = true;
            }

            ActionScene actionScene = SingletonInitializer<ActionScene>.instance;
            if (_capturedActionScene != actionScene)
            {
                if (_capturedActionScene != null && _hasPrevActionSceneCursorLock)
                    _capturedActionScene.SetCursorLock(_prevActionSceneCursorLock);
                _capturedActionScene = actionScene;
                _hasPrevActionSceneCursorLock = false;
            }
            if (actionScene != null)
            {
                if (!_hasPrevActionSceneCursorLock)
                {
                    _prevActionSceneCursorLock = actionScene.isCursorLock;
                    _hasPrevActionSceneCursorLock = true;
                }
                actionScene.SetCursorLock(isLock: false);
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private bool NeedsRecovery()
        {
            // 「カメラが止まっている」だけを根拠に救助してはいけない。
            // KeyCondition == AlwaysFalse は MainGameUiInputCapture が
            // 「今このカメラを止めている」と書く目印そのもので、
            // それを救助対象と見なすと、UIやギズモを操作している最中に
            // 毎フレーム横から解除し続けることになる（綱引き）。
            //
            // 掴みっぱなしの救助は MainGameUiInputCapture 側の
            // AutoUnstickCamera が一手に引き受ける（誰も掴んでいない状態が
            // 数秒続いた時だけ解除する）。二重に持たない。
            if (IsExternalCaptureApiPresent()) return false;

            CameraControl_Ver2 ctrl = ResolveCtrlCamera();
            // Only recover restrictions installed by this controller.
            return ctrl != null
                && (IsNoCtrlFunc(ctrl.NoCtrlCondition, AlwaysTrue)
                    || IsNoCtrlFunc(ctrl.KeyCondition, AlwaysFalse));
        }

        /// <summary>MainGameUiInputCapture が読み込まれているか。いるなら救助はそちらに任せる。</summary>
        private static bool IsExternalCaptureApiPresent()
        {
            IsExternalCaptureActive();
            return _externalCaptureReader != null;
        }

        internal void UpdateEditCursorUnlockLoop()
        {
            if (_sharedSync != null) return;
            if (_shouldKeepEditCursorUnlocked() && !_active) EnsureEndOfFrameRoutine();
            else if (!_active) StopEndOfFrameRoutine();
        }

        private IEnumerator EndOfFrameLoop()
        {
            while (_active || _shouldKeepEditCursorUnlocked())
            {
                yield return new WaitForEndOfFrame();
                if (_active) TickCapture();
                else if (_shouldKeepEditCursorUnlocked()) TickEditCursorUnlockOnly();
            }
            _endOfFrameRoutine = null;
        }

        private static void TickEditCursorUnlockOnly()
        {
            GameCursor gameCursor = Singleton<GameCursor>.Instance;
            if (gameCursor != null) GameCursor.isDraw = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void EnsureEndOfFrameRoutine()
        {
            if (_endOfFrameRoutine == null) _endOfFrameRoutine = _coroutines.Start(EndOfFrameLoop());
        }

        private void StopEndOfFrameRoutine()
        {
            if (_endOfFrameRoutine == null) return;
            _coroutines.Stop(_endOfFrameRoutine);
            _endOfFrameRoutine = null;
        }

        private CameraControl_Ver2 ResolveCtrlCamera()
        {
            if (_cachedHFlag == null) _cachedHFlag = UnityEngine.Object.FindObjectOfType<HFlag>();
            if (_cachedHFlag != null)
            {
                CameraControl_Ver2 live = _cachedHFlag.ctrlCamera;
                if (live != null) _cachedCtrlCamera = live;
            }
            return _cachedCtrlCamera != null ? _cachedCtrlCamera : null;
        }

        private void ClearCapturedState()
        {
            _active = false;
            _capturedCtrlCamera = null;
            _capturedActionScene = null;
            ResetPreviousStateFlags();
            _gizmoDragHint = false;
        }

        private void ResetPreviousStateFlags()
        {
            _hasPrevCtrlCameraCursorLock = false;
            _hasPrevCtrlCameraNoCtrl = false;
            _hasPrevCtrlCameraKeyCondition = false;
            _hasPrevGameCursorLock = false;
            _hasPrevActionSceneCursorLock = false;
        }

        /// <summary>今カメラ操作が禁止されているかを安全に評価する。</summary>
        private static bool EvaluatesToNoControl(BaseCameraControl_Ver2.NoCtrlFunc func)
        {
            if (func == null) return false;
            try { return func(); }
            catch { return true; }
        }

        /// <summary>今キー操作が禁止されているかを安全に評価する。</summary>
        private static bool EvaluatesToNoKeyControl(BaseCameraControl_Ver2.NoCtrlFunc func)
        {
            if (func == null) return false;
            try { return !func(); }
            catch { return true; }
        }

        private static bool AlwaysTrue() => true;
        private static bool AlwaysFalse() => false;

        private static bool IsNoCtrlFunc(BaseCameraControl_Ver2.NoCtrlFunc current, BaseCameraControl_Ver2.NoCtrlFunc expected)
        {
            return current != null && expected != null
                && current.Method == expected.Method
                && current.Target == expected.Target;
        }

        public void Dispose()
        {
            Stop("controller disposed");
            StopEndOfFrameRoutine();
            _cachedCtrlCamera = null;
            _cachedHFlag = null;
        }
    }
}
