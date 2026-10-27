using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MainGameUiInputCapture
{
    internal sealed class UiInputCaptureRuntime
    {
        // 掴みっぱなし防止の時効。owner 側の Update が止まっても、
        // この秒数だけ更新が来なければ掴みを自動で返す。
        // 生きている owner は毎フレーム Sync/Tick を打つので、通常運用では絶対に触れない値。
        private const float StaleReleaseSeconds = 1.0f;

        private readonly Plugin _plugin;
        private readonly HashSet<string> _activeTokens = new HashSet<string>(System.StringComparer.Ordinal);
        // Owner/source は通常固定文字列。毎フレームの Sync で同じ token を作り直さない。
        private readonly Dictionary<string, Dictionary<string, string>> _tokenCache =
            new Dictionary<string, Dictionary<string, string>>(System.StringComparer.Ordinal);
        private int _tokenCacheCount;
        // token / owner ごとの最終更新時刻。時効判定にだけ使う。
        private readonly Dictionary<string, float> _tokenTouchTime =
            new Dictionary<string, float>(System.StringComparer.Ordinal);
        private readonly Dictionary<string, float> _inputBlockTouchTime =
            new Dictionary<string, float>(System.StringComparer.Ordinal);
        private readonly List<string> _scratchExpired = new List<string>();
        private readonly HashSet<string> _idleUnlockOwners = new HashSet<string>(System.StringComparer.Ordinal);
        private readonly List<string> _scratchTokens = new List<string>();

        // uGUI クリック貫通防止ブロッカー。owner ごとに IMGUI 矩形群を保持し、
        // 透明 Image (Canvas+GraphicRaycaster) で覆って uGUI クリックを吸収する。
        private readonly Dictionary<string, List<Rect>> _blockRectsByOwner =
            new Dictionary<string, List<Rect>>(System.StringComparer.Ordinal);
        private readonly Dictionary<string, List<Rect>> _inputBlockRectsByOwner =
            new Dictionary<string, List<Rect>>(System.StringComparer.Ordinal);
        private readonly HashSet<string> _windowScopedOwnersWarned =
            new HashSet<string>(System.StringComparer.Ordinal);
        private GameObject _blockerRoot;
        private readonly List<Image> _blockerImages = new List<Image>();
        private bool _blockerDirty = true;
        private int _lastBlockerScreenWidth = -1;
        private int _lastBlockerScreenHeight = -1;
        private float _nextBlockerSafetyRefreshTime;

        private bool _captureApplied;
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
        private ActionScene _capturedActionScene;

        // 誰も掴んでいないのにカメラが止まったままの状態が、この秒数続いたら強制解除する。
        // ゲーム本体が遷移中に一時的に止めるぶんは1秒未満なので巻き込まない。
        private const float AutoUnstickSeconds = 3.0f;
        private float _cameraBlockedSince = -1f;
        private float _nextAutoUnstickCheckTime;

        private float _nextBlockDiagTime;
        private string _lastBlockDiag = string.Empty;

        // UI の上でボタンを押した瞬間に掛ける掛け金。
        // 押している間はポインタが矩形の外へ出てもカメラを渡さない。
        // これが無いと、スライダーを掴んだまま少し外へ動かした瞬間に
        // 矩形判定が外れ、その場でカメラがマウスを持って行く。
        private bool _pointerLatched;
        private string _pointerLatchOwner = string.Empty;

        private HFlag _cachedHFlag;
        private float _nextHFlagSearchTime;
        private int _lastCameraMaintenanceFrame = -1;
        private CameraControl_Ver2 _cachedCtrlCamera;
        private CameraControl_Ver2 _capturedCtrlCamera;

        internal UiInputCaptureRuntime(Plugin plugin)
        {
            _plugin = plugin;
        }

        internal bool IsAnyActive => _activeTokens.Count > 0;

        internal void Sync(string ownerKey, string sourceKey, bool active)
        {
            if (!TryNormalizeKey(ownerKey, out string owner)
                || !TryNormalizeKey(sourceKey, out string source))
                return;

            string token = BuildToken(owner, source);
            if (active && IsWindowSource(source) && HasInputBlockRects(owner) && !IsMouseInsideOwnerInputBlock(owner))
            {
                bool removed = _activeTokens.Remove(token);
                _tokenTouchTime.Remove(token);
                if (_windowScopedOwnersWarned.Add(owner))
                {
                    _plugin.LogWarn("Window capture scoped to registered input rects. owner=" + owner
                        + " source=" + source
                        + " active=true ignored while pointer is outside rects");
                }

                if (removed && _activeTokens.Count == 0)
                    Restore("sync-window-outside:" + token);
                return;
            }

            if (active)
            {
                bool added = _activeTokens.Add(token);
                _tokenTouchTime[token] = Time.unscaledTime;
                if (added)
                    _plugin.LogDebugForOwner(owner, $"capture token add owner={owner} source={source} active={_activeTokens.Count}");
                ApplyCapture(_captureApplied ? null : "sync:" + token);
            }
            else
            {
                bool removed = _activeTokens.Remove(token);
                _tokenTouchTime.Remove(token);
                if (removed)
                    _plugin.LogDebugForOwner(owner, $"capture token remove owner={owner} source={source} active={_activeTokens.Count}");
                if (_activeTokens.Count == 0)
                    Restore(_captureApplied || HasSavedState() ? "sync-end:" + token : null);
            }
        }

        internal bool Begin(string ownerKey, string sourceKey)
        {
            if (!TryNormalizeKey(ownerKey, out string owner)
                || !TryNormalizeKey(sourceKey, out string source))
            {
                _plugin.LogWarn("Begin rejected: owner/source empty");
                return false;
            }

            string token = BuildToken(owner, source);
            bool added = _activeTokens.Add(token);
            _tokenTouchTime[token] = Time.unscaledTime;
            if (added)
            {
                _plugin.LogDebugForOwner(owner, $"capture token add owner={owner} source={source} active={_activeTokens.Count}");
            }

            ApplyCapture("begin:" + token);
            return added;
        }

        internal bool Tick(string ownerKey, string sourceKey)
        {
            if (!TryNormalizeKey(ownerKey, out string owner)
                || !TryNormalizeKey(sourceKey, out string source))
            {
                return false;
            }

            string token = BuildToken(owner, source);
            if (!_activeTokens.Contains(token))
            {
                return false;
            }

            _tokenTouchTime[token] = Time.unscaledTime;
            ApplyCapture(_captureApplied ? null : "tick:" + token);
            return true;
        }

        internal bool End(string ownerKey, string sourceKey)
        {
            if (!TryNormalizeKey(ownerKey, out string owner)
                || !TryNormalizeKey(sourceKey, out string source))
            {
                return false;
            }

            string token = BuildToken(owner, source);
            bool removed = _activeTokens.Remove(token);
            _tokenTouchTime.Remove(token);
            if (removed)
            {
                _plugin.LogDebugForOwner(owner, $"capture token remove owner={owner} source={source} active={_activeTokens.Count}");
            }

            if (_activeTokens.Count == 0)
            {
                Restore("end:" + token);
            }

            return removed;
        }

        internal int EndOwner(string ownerKey)
        {
            if (!TryNormalizeKey(ownerKey, out string owner))
            {
                return 0;
            }

            string prefix = owner + "::";
            _scratchTokens.Clear();
            foreach (string token in _activeTokens)
            {
                if (token.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    _scratchTokens.Add(token);
                }
            }

            int removed = 0;
            for (int i = 0; i < _scratchTokens.Count; i++)
            {
                _tokenTouchTime.Remove(_scratchTokens[i]);
                if (_activeTokens.Remove(_scratchTokens[i]))
                    removed++;
            }

            bool idleRemoved = _idleUnlockOwners.Remove(owner);
            if (removed > 0 || idleRemoved)
            {
                _plugin.LogDebugForOwner(owner, $"capture owner clear owner={owner} removed={removed} idleRemoved={idleRemoved}");
            }

            if (_blockRectsByOwner.Remove(owner))
            {
                _blockerDirty = true;
                UpdateBlocker();
            }
            _inputBlockRectsByOwner.Remove(owner);
            _inputBlockTouchTime.Remove(owner);

            if (_activeTokens.Count == 0)
            {
                Restore("end-owner:" + owner);
            }

            return removed;
        }

        internal bool SetIdleCursorUnlock(string ownerKey, bool enabled)
        {
            if (!TryNormalizeKey(ownerKey, out string owner))
            {
                return false;
            }

            bool changed = enabled
                ? _idleUnlockOwners.Add(owner)
                : _idleUnlockOwners.Remove(owner);

            if (changed)
            {
                _plugin.LogDebugForOwner(owner, $"idle unlock {(enabled ? "ON" : "OFF")} owner={owner} total={_idleUnlockOwners.Count}");
            }

            if (!enabled
                && _idleUnlockOwners.Count == 0
                && _activeTokens.Count == 0)
            {
                Restore("idle-off:" + owner);
            }

            if (enabled)
            {
                ApplyIdleCursorUnlock();
            }

            return changed;
        }

        /// <summary>
        /// owner の uGUI ブロック矩形群を置き換える。矩形は IMGUI 座標（左上原点）。
        /// null/空 を渡すと owner のブロックを解除する。
        /// </summary>
        internal void SetBlockRects(string ownerKey, IList<Rect> rects)
        {
            if (!TryNormalizeKey(ownerKey, out string owner))
            {
                return;
            }

            if (rects == null || rects.Count == 0)
            {
                if (_blockRectsByOwner.Remove(owner))
                {
                    _blockerDirty = true;
                    UpdateBlocker();
                }
                return;
            }

            if (!_blockRectsByOwner.TryGetValue(owner, out List<Rect> list))
            {
                list = new List<Rect>(rects.Count);
                _blockRectsByOwner[owner] = list;
            }
            else if (list.Count == rects.Count)
            {
                bool same = true;
                for (int i = 0; i < rects.Count; i++)
                {
                    if (list[i] == rects[i]) continue;
                    same = false;
                    break;
                }
                if (same) return;
            }
            list.Clear();
            for (int i = 0; i < rects.Count; i++)
            {
                list.Add(rects[i]);
            }
            _blockerDirty = true;
            UpdateBlocker();
        }

        /// <summary>
        /// owner の入力ブロック矩形群を置き換える。矩形は IMGUI 座標（左上原点）。
        /// uGUI クリック吸収用ブロッカーも同じ矩形で更新する。
        /// </summary>
        internal void SetInputBlockRects(string ownerKey, IList<Rect> rects)
        {
            if (!TryNormalizeKey(ownerKey, out string owner))
            {
                return;
            }

            if (rects == null || rects.Count == 0)
            {
                _inputBlockRectsByOwner.Remove(owner);
                _inputBlockTouchTime.Remove(owner);
                _windowScopedOwnersWarned.Remove(owner);
                SetBlockRects(owner, null);
                return;
            }

            _inputBlockTouchTime[owner] = Time.unscaledTime;

            if (!_inputBlockRectsByOwner.TryGetValue(owner, out List<Rect> list))
            {
                list = new List<Rect>(rects.Count);
                _inputBlockRectsByOwner[owner] = list;
            }
            list.Clear();
            for (int i = 0; i < rects.Count; i++)
            {
                list.Add(rects[i]);
            }

            SetBlockRects(owner, rects);
        }

        private void EnsureBlocker()
        {
            if (_blockerRoot != null)
            {
                return;
            }

            _blockerRoot = new GameObject("UiInputCaptureBlocker");
            Object.DontDestroyOnLoad(_blockerRoot);

            Canvas canvas = _blockerRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 29999;

            _blockerRoot.AddComponent<GraphicRaycaster>();
        }

        private Image CreateBlockerImage()
        {
            var go = new GameObject("BlockerImage");
            go.transform.SetParent(_blockerRoot.transform, false);
            var image = go.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
            return image;
        }

        /// <summary>
        /// 全 owner の矩形を集約し、透明 Image プールに割り当てて配置する。
        /// 矩形ゼロならブロッカー全体を非アクティブにする。
        /// </summary>
        private void UpdateBlocker()
        {
            int screenWidth = Screen.width;
            int screenHeight = Screen.height;
            float now = Time.unscaledTime;
            if (!_blockerDirty
                && screenWidth == _lastBlockerScreenWidth
                && screenHeight == _lastBlockerScreenHeight
                && now < _nextBlockerSafetyRefreshTime
                && (_blockerRoot != null || _blockRectsByOwner.Count == 0))
                return;

            _blockerDirty = false;
            _lastBlockerScreenWidth = screenWidth;
            _lastBlockerScreenHeight = screenHeight;
            _nextBlockerSafetyRefreshTime = now + 1f;
            int needed = 0;
            foreach (KeyValuePair<string, List<Rect>> kv in _blockRectsByOwner)
            {
                if (kv.Value != null)
                {
                    needed += kv.Value.Count;
                }
            }

            if (needed == 0)
            {
                if (_blockerRoot != null && _blockerRoot.activeSelf)
                {
                    _blockerRoot.SetActive(false);
                }
                return;
            }

            EnsureBlocker();
            if (_blockerRoot == null)
            {
                return;
            }

            while (_blockerImages.Count < needed)
            {
                _blockerImages.Add(CreateBlockerImage());
            }

            float screenW = Screen.width;
            float screenH = Screen.height;
            int idx = 0;
            foreach (KeyValuePair<string, List<Rect>> kv in _blockRectsByOwner)
            {
                List<Rect> list = kv.Value;
                if (list == null)
                {
                    continue;
                }
                for (int i = 0; i < list.Count; i++)
                {
                    Rect r = list[i];
                    Image img = _blockerImages[idx++];
                    RectTransform rt = img.rectTransform;

                    // IMGUI座標(左上原点) → ScreenSpaceOverlay(左下原点) に変換
                    float uiX = r.x;
                    float uiY = screenH - r.yMax;
                    Vector2 anchorMin = new Vector2(uiX / screenW, uiY / screenH);
                    Vector2 anchorMax = new Vector2((uiX + r.width) / screenW, (uiY + r.height) / screenH);
                    if (rt.anchorMin != anchorMin) rt.anchorMin = anchorMin;
                    if (rt.anchorMax != anchorMax) rt.anchorMax = anchorMax;
                    if (rt.offsetMin != Vector2.zero) rt.offsetMin = Vector2.zero;
                    if (rt.offsetMax != Vector2.zero) rt.offsetMax = Vector2.zero;

                    if (!img.gameObject.activeSelf)
                    {
                        img.gameObject.SetActive(true);
                    }
                }
            }

            for (int i = idx; i < _blockerImages.Count; i++)
            {
                if (_blockerImages[i].gameObject.activeSelf)
                {
                    _blockerImages[i].gameObject.SetActive(false);
                }
            }

            if (!_blockerRoot.activeSelf)
            {
                _blockerRoot.SetActive(true);
            }
        }

        internal bool IsOwnerActive(string ownerKey)
        {
            if (!TryNormalizeKey(ownerKey, out string owner))
            {
                return false;
            }

            string prefix = owner + "::";
            foreach (string token in _activeTokens)
            {
                if (token.StartsWith(prefix, System.StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private bool HasInputBlockRects(string owner)
        {
            return _inputBlockRectsByOwner.TryGetValue(owner, out List<Rect> rects)
                && rects != null
                && rects.Count > 0;
        }

        private bool IsMouseInsideOwnerInputBlock(string owner)
        {
            return _inputBlockRectsByOwner.TryGetValue(owner, out List<Rect> rects)
                && ContainsGuiMouse(rects, GetGuiMousePosition());
        }

        private static bool IsWindowSource(string source)
        {
            return string.Equals(source, "window", System.StringComparison.Ordinal)
                || string.Equals(source, "ui-window", System.StringComparison.Ordinal);
        }

        internal bool IsMouseInputBlocked()
        {
            return TryGetInputBlockOwnerUnderMouse(out _)
                || (_pointerLatched && (Input.GetMouseButton(0) || Input.GetMouseButton(1)));
        }

        internal bool BlockCameraPointerInput => IsMouseInputBlocked() || _activeTokens.Count > 0;

        internal bool PreventGameCursorWarp => BlockCameraPointerInput
            || ShouldReleaseIdleCursor;

        private bool ShouldReleaseIdleCursor => _idleUnlockOwners.Count > 0
            && NoMouseButtonsHeld;
        private static bool NoMouseButtonsHeld => !Input.GetMouseButton(0)
            && !Input.GetMouseButton(1) && !Input.GetMouseButton(2);

        internal bool IsMouseInputBlockedForOwner(string ownerKey)
        {
            if (!TryNormalizeKey(ownerKey, out string owner))
            {
                return IsMouseInputBlocked();
            }

            if (_inputBlockRectsByOwner.Count == 0)
            {
                return false;
            }

            Vector2 guiMouse = GetGuiMousePosition();
            foreach (KeyValuePair<string, List<Rect>> kv in _inputBlockRectsByOwner)
            {
                if (string.Equals(kv.Key, owner, System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (ContainsGuiMouse(kv.Value, guiMouse))
                {
                    return true;
                }
            }

            return false;
        }

        internal string GetStateSummary()
        {
            return "activeTokens=" + _activeTokens.Count
                + ", idleUnlockOwners=" + _idleUnlockOwners.Count
                + ", inputBlockOwners=" + _inputBlockRectsByOwner.Count
                + ", captureApplied=" + _captureApplied
                + ", hasSavedState=" + HasSavedState();
        }

        internal void FrameUpdate()
        {
            if (!Application.isFocused) {
                if (_captureApplied || HasSavedState() || _activeTokens.Count > 0 || _pointerLatched)
                    ReleaseAll("unfocused");
                return;
            }
            // 掴んだまま音信不通になった owner の分を先に返す。
            // これを入れないと、owner の Update が止まった瞬間の掴みが永久に残り、
            // カメラ操作が二度と戻らなくなる。
            ExpireStaleOwners();

            // 「今カメラが止まっているか、止めているのは誰か」を毎フレーム見張る。
            // FrameUpdate は途中で return する枝があるので、必ず先頭で呼ぶ。
            if (_lastCameraMaintenanceFrame != Time.frameCount)
            {
                _lastCameraMaintenanceFrame = Time.frameCount;
                DiagnoseCameraBlock();
                AutoUnstickCamera();
            }

            // ブロッカーはキャプチャ状態と独立。解像度/ウィンドウ移動追従のため毎フレーム再配置。
            UpdateBlocker();

            // 掛け金の開閉。押した瞬間に掛け、離した瞬間に外す。
            if (_pointerLatched && !Input.GetMouseButton(0) && !Input.GetMouseButton(1))
            {
                _pointerLatched = false;
                _pointerLatchOwner = string.Empty;
            }

            if (TryGetInputBlockOwnerUnderMouse(out string blockOwner))
            {
                if (!_pointerLatched && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)))
                {
                    _pointerLatched = true;
                    _pointerLatchOwner = blockOwner;
                }
                ApplyCapture("input-block:" + blockOwner);
                return;
            }

            // 矩形の外へ出ても、掴んだままならカメラは渡さない。
            if (_pointerLatched)
            {
                ApplyCapture("pointer-latch:" + _pointerLatchOwner);
                return;
            }

            if (_activeTokens.Count > 0)
            {
                ApplyCapture("frame-active");
                return;
            }

            if (_captureApplied || HasSavedState())
            {
                Restore("frame-inactive");
            }

            if (_idleUnlockOwners.Count > 0)
            {
                ApplyIdleCursorUnlock();
            }
        }

        internal void ReleaseAll(string reason)
        {
            _pointerLatched = false;
            _pointerLatchOwner = string.Empty;
            _activeTokens.Clear();
            _idleUnlockOwners.Clear();
            _inputBlockRectsByOwner.Clear();
            _tokenTouchTime.Clear();
            _inputBlockTouchTime.Clear();
            Restore(reason);
            // 状態に関わらずカーソルを必ず解放（Restore が早期 return した場合の保険）
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // ブロッカー破棄（Image は root の子なので root 破棄で一緒に消える）
            if (Singleton<GameCursor>.IsInstance()) Singleton<GameCursor>.Instance.UnLockCursor();
            _blockRectsByOwner.Clear();
            _blockerDirty = true;
            _blockerImages.Clear();
            if (_blockerRoot != null)
            {
                Object.Destroy(_blockerRoot);
                _blockerRoot = null;
            }
        }

        /// <summary>
        /// 一定時間更新の来ない掴み（token / 入力ブロック矩形）を捨てる。
        ///
        /// owner 側は毎フレーム Sync/Tick/SetInputBlockRects を打ち直す作りになっている。
        /// ところが多くの owner は Update の先頭で「自作の部屋の外なら return」しており、
        /// 部屋の外へ出た瞬間や H シーンの出入りでマップが一瞬消えると、
        /// 掴みを返す処理まで到達しないまま黙り込む。
        /// その取り残しをここで回収する。owner の作りに依存しない最後の砦。
        /// </summary>
        private void ExpireStaleOwners()
        {
            float now = Time.unscaledTime;

            if (_tokenTouchTime.Count > 0)
            {
                _scratchExpired.Clear();
                foreach (KeyValuePair<string, float> kv in _tokenTouchTime)
                {
                    if (now - kv.Value > StaleReleaseSeconds)
                        _scratchExpired.Add(kv.Key);
                }

                for (int i = 0; i < _scratchExpired.Count; i++)
                {
                    string token = _scratchExpired[i];
                    _tokenTouchTime.Remove(token);
                    if (_activeTokens.Remove(token) && TryExtractOwnerFromToken(token, out string owner))
                    {
                        _plugin.LogAlways("capture token expired (owner stopped updating): " + token);
                        _idleUnlockOwners.Remove(owner);
                    }
                }
            }

            if (_inputBlockTouchTime.Count > 0)
            {
                _scratchExpired.Clear();
                foreach (KeyValuePair<string, float> kv in _inputBlockTouchTime)
                {
                    if (now - kv.Value > StaleReleaseSeconds)
                        _scratchExpired.Add(kv.Key);
                }

                for (int i = 0; i < _scratchExpired.Count; i++)
                {
                    string owner = _scratchExpired[i];
                    _inputBlockTouchTime.Remove(owner);
                    if (_inputBlockRectsByOwner.Remove(owner))
                    {
                        _plugin.LogAlways("input block rects expired (owner stopped updating): " + owner);
                        _windowScopedOwnersWarned.Remove(owner);
                        SetBlockRects(owner, null);
                    }
                }
            }
        }

        private void ApplyCapture(string reason)
        {
            if (!Application.isFocused) return;
            CameraControl_Ver2 ctrl = ResolveCtrlCamera();
            if (ctrl != null)
            {
                if (_capturedCtrlCamera != ctrl) {
                    if (_capturedCtrlCamera != null) {
                        if (_hasPrevCtrlCameraCursorLock) _capturedCtrlCamera.isCursorLock = _prevCtrlCameraCursorLock;
                        if (IsNoCtrlFunc(_capturedCtrlCamera.NoCtrlCondition, AlwaysTrue))
                            _capturedCtrlCamera.NoCtrlCondition = _hasPrevCtrlCameraNoCtrl ? _prevCtrlCameraNoCtrl : AlwaysFalse;
                        if (IsNoCtrlFunc(_capturedCtrlCamera.KeyCondition, AlwaysFalse))
                            _capturedCtrlCamera.KeyCondition = _hasPrevCtrlCameraKeyCondition ? _prevCtrlCameraKeyCondition : AlwaysTrue;
                    }
                    _hasPrevCtrlCameraCursorLock = false;
                    _hasPrevCtrlCameraNoCtrl = false;
                    _hasPrevCtrlCameraKeyCondition = false;
                    _capturedCtrlCamera = ctrl;
                }
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
                // 独自のラムダ (() => !_bPlay) で丸ごと差し替える。
                // 先に呼んでおかないと我々の目印 (AlwaysTrue) が消され、
                // 「掴みっぱなしを検出して解除する」保険が永久に空振りする。
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
                // A scene replacement must not inherit another scene's lock setting.
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

                actionScene.SetCursorLock(false);
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (!_captureApplied)
            {
                _captureApplied = true;
                if (ShouldLogCaptureTransition(reason))
                {
                    _plugin.LogDebugRaw("capture ON (" + reason + ")");
                    if (_plugin.LogStateOnTransition)
                        _plugin.LogDebugRaw("capture state: " + GetStateSummary());
                }
            }
        }

        private void Restore(string reason)
        {
            // Ending one token must not release another token or a UI-owned drag.
            if (BlockCameraPointerInput) return;
            if (!_captureApplied && !HasSavedState())
            {
                if (_idleUnlockOwners.Count > 0)
                {
                    ApplyIdleCursorUnlock();
                }
                return;
            }

            // Never apply the previous scene's delegates to a newly created camera.
            CameraControl_Ver2 ctrl = _capturedCtrlCamera;
            bool cameraRestored = ctrl == null;
            if (ctrl != null)
            {
                if (_hasPrevCtrlCameraCursorLock)
                    ctrl.isCursorLock = _prevCtrlCameraCursorLock;

                // Only undo our own write. A game menu or another plugin may have
                // installed a new restriction; never turn that into click-through.
                if (IsNoCtrlFunc(ctrl.NoCtrlCondition, AlwaysTrue))
                    ctrl.NoCtrlCondition = _hasPrevCtrlCameraNoCtrl ? _prevCtrlCameraNoCtrl : AlwaysFalse;
                if (IsNoCtrlFunc(ctrl.KeyCondition, AlwaysFalse))
                    ctrl.KeyCondition = _hasPrevCtrlCameraKeyCondition ? _prevCtrlCameraKeyCondition : AlwaysTrue;

                cameraRestored = true;
            }

            GameCursor gameCursor = Singleton<GameCursor>.Instance;
            if (gameCursor != null && _hasPrevGameCursorLock)
            {
                gameCursor.UnLockCursor();
                GameCursor.isDraw = true;
            }

            ActionScene actionScene = _capturedActionScene;
            if (actionScene != null && _hasPrevActionSceneCursorLock)
            {
                actionScene.SetCursorLock(_prevActionSceneCursorLock);
            }

            // Camera permission to lock is not Unity's screen-centre lock state.
            // A fresh native drag can acquire its own lock after capture ends.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _captureApplied = false;
            // カメラを取れなかったときに保存値まで捨てると、
            // 「操作禁止のまま・戻す情報も無い」状態で固定される。
            // 掴んだ情報は残し、次フレーム以降にやり直す。
            if (cameraRestored)
            {
                _capturedCtrlCamera = null;
                _hasPrevCtrlCameraCursorLock = false;
                _hasPrevCtrlCameraNoCtrl = false;
                _hasPrevCtrlCameraKeyCondition = false;
            }
            _hasPrevGameCursorLock = false;
            _hasPrevActionSceneCursorLock = false;
            _capturedActionScene = null;

            if (ShouldLogCaptureTransition(reason))
            {
                _plugin.LogDebugRaw("capture OFF (" + reason + ")");
                if (_plugin.LogStateOnTransition)
                    _plugin.LogDebugRaw("capture state: " + GetStateSummary());
            }

            if (_idleUnlockOwners.Count > 0)
            {
                ApplyIdleCursorUnlock();
            }
        }

        private void ApplyIdleCursorUnlock()
        {
            GameCursor gameCursor = Singleton<GameCursor>.Instance;
            if (gameCursor != null)
            {
                // Unlike SetCursorLock(false), this never warps to the old lock position.
                if (ShouldReleaseIdleCursor) gameCursor.UnLockCursor();
                GameCursor.isDraw = true;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private bool TryGetInputBlockOwnerUnderMouse(out string owner)
        {
            owner = string.Empty;
            if (_inputBlockRectsByOwner.Count == 0)
            {
                return false;
            }

            Vector2 guiMouse = GetGuiMousePosition();
            foreach (KeyValuePair<string, List<Rect>> kv in _inputBlockRectsByOwner)
            {
                if (ContainsGuiMouse(kv.Value, guiMouse))
                {
                    owner = kv.Key;
                    return true;
                }
            }

            return false;
        }

        private static Vector2 GetGuiMousePosition()
        {
            Vector3 mouse = Input.mousePosition;
            return new Vector2(mouse.x, Screen.height - mouse.y);
        }

        private static bool ContainsGuiMouse(List<Rect> rects, Vector2 guiMouse)
        {
            if (rects == null)
            {
                return false;
            }

            for (int i = 0; i < rects.Count; i++)
            {
                Rect r = rects[i];
                if (r.width <= 0f || r.height <= 0f)
                {
                    continue;
                }

                if (r.Contains(guiMouse))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>NoCtrlCondition が「今カメラ操作を禁止している」かを安全に評価する。</summary>
        private static bool EvaluatesToNoControl(BaseCameraControl_Ver2.NoCtrlFunc func)
        {
            if (func == null) return false;
            try { return func(); }
            catch { return true; } // 壊れた delegate を握っている。禁止扱いにして解除させる。
        }

        /// <summary>KeyCondition が「今キー操作を禁止している」かを安全に評価する。</summary>
        private static bool EvaluatesToNoKeyControl(BaseCameraControl_Ver2.NoCtrlFunc func)
        {
            if (func == null) return false;
            try { return !func(); }
            catch { return true; }
        }

        private CameraControl_Ver2 ResolveCtrlCamera()
        {
            // H シーンを出入りすると HFlag ごと作り直される。
            // 掴んだ当時のカメラを握り続けると、戻す先を間違えて掴みが残る。
            if (_cachedHFlag == null && Time.unscaledTime >= _nextHFlagSearchTime)
            {
                _nextHFlagSearchTime = Time.unscaledTime + 0.5f;
                _cachedHFlag = Object.FindObjectOfType<HFlag>();
            }

            if (_cachedHFlag != null)
            {
                CameraControl_Ver2 live = _cachedHFlag.ctrlCamera;
                if (live != null)
                    _cachedCtrlCamera = live;
            }

            return _cachedCtrlCamera != null ? _cachedCtrlCamera : null;
        }

        private bool HasSavedState()
        {
            return _hasPrevCtrlCameraCursorLock
                || _hasPrevCtrlCameraNoCtrl
                || _hasPrevCtrlCameraKeyCondition
                || _hasPrevGameCursorLock
                || _hasPrevActionSceneCursorLock;
        }

        private static bool TryNormalizeKey(string raw, out string normalized)
        {
            normalized = raw == null ? string.Empty : raw.Trim();
            return normalized.Length > 0;
        }

        private string BuildToken(string owner, string source)
        {
            if (_tokenCache.TryGetValue(owner, out var sources)
                && sources.TryGetValue(source, out var token))
                return token;

            // 動的な owner/source を使う呼び出し元があってもキャッシュを無制限に増やさない。
            if (_tokenCacheCount >= 256)
            {
                _tokenCache.Clear();
                _tokenCacheCount = 0;
            }
            if (!_tokenCache.TryGetValue(owner, out sources))
                _tokenCache[owner] = sources = new Dictionary<string, string>(System.StringComparer.Ordinal);
            token = owner + "::" + source;
            sources[source] = token;
            _tokenCacheCount++;
            return token;
        }

        private static bool AlwaysTrue()
        {
            return true;
        }

        private static bool AlwaysFalse()
        {
            return false;
        }

        private static bool IsNoCtrlFunc(BaseCameraControl_Ver2.NoCtrlFunc current, BaseCameraControl_Ver2.NoCtrlFunc expected)
        {
            if (current == null || expected == null) return false;
            return current.Method == expected.Method && current.Target == expected.Target;
        }

        private bool ShouldLogCaptureTransition(string reason)
        {
            if (TryExtractOwnerFromReason(reason, out string owner))
                return _plugin.IsOwnerDebugEnabled(owner);

            if (HasAnyDebugEnabledActiveOwner())
                return true;

            if (HasAnyDebugEnabledIdleUnlockOwner())
                return true;

            return false;
        }

        private bool HasAnyDebugEnabledActiveOwner()
        {
            foreach (string token in _activeTokens)
            {
                if (!TryExtractOwnerFromToken(token, out string owner))
                    continue;

                if (_plugin.IsOwnerDebugEnabled(owner))
                    return true;
            }

            return false;
        }

        private bool HasAnyDebugEnabledIdleUnlockOwner()
        {
            foreach (string owner in _idleUnlockOwners)
            {
                if (_plugin.IsOwnerDebugEnabled(owner))
                    return true;
            }

            return false;
        }

        private static bool TryExtractOwnerFromReason(string reason, out string owner)
        {
            owner = string.Empty;
            if (string.IsNullOrEmpty(reason))
                return false;

            int idx = reason.IndexOf(':');
            if (idx < 0 || idx + 1 >= reason.Length)
                return false;

            string kind = reason.Substring(0, idx);
            string payload = reason.Substring(idx + 1);
            if (!TryNormalizeKey(payload, out string normalizedPayload))
                return false;

            switch (kind)
            {
                case "sync":
                case "sync-end":
                case "begin":
                case "tick":
                case "end":
                    return TryExtractOwnerFromToken(normalizedPayload, out owner);

                case "end-owner":
                case "idle-off":
                    owner = normalizedPayload;
                    return true;

                default:
                    return TryExtractOwnerFromToken(normalizedPayload, out owner);
            }
        }

        /// <summary>
        /// カメラ操作が今この瞬間に止められているか、止めているのは誰かを名指しで記録する。
        /// 状態が変わった時だけ1行出す。推測せずに犯人を特定するための唯一の口。
        /// </summary>
        private void DiagnoseCameraBlock()
        {
            if (!_plugin.BlockDiagnosticsEnabled) return;

            float now = Time.unscaledTime;
            if (now < _nextBlockDiagTime) return;
            _nextBlockDiagTime = now + 0.5f;

            CameraControl_Ver2 ctrl = ResolveCtrlCamera();
            if (ctrl == null) return;

            bool blocked = EvaluatesToNoControl(ctrl.NoCtrlCondition);
            bool keyBlocked = EvaluatesToNoKeyControl(ctrl.KeyCondition);

            string line = "camera mouse=" + (blocked ? "BLOCKED" : "free")
                + " key=" + (keyBlocked ? "BLOCKED" : "free")
                + " noCtrl=" + DescribeFunc(ctrl.NoCtrlCondition)
                + " keyCond=" + DescribeFunc(ctrl.KeyCondition)
                + " cursorLock=" + ctrl.isCursorLock
                + " captureApplied=" + _captureApplied
                + " tokens=" + DescribeSet(_activeTokens)
                + " inputBlockOwners=" + DescribeKeys(_inputBlockRectsByOwner)
                + " idleUnlock=" + DescribeSet(_idleUnlockOwners);

            if (string.Equals(line, _lastBlockDiag, System.StringComparison.Ordinal)) return;
            _lastBlockDiag = line;
            _plugin.LogAlways(line);
        }

        /// <summary>
        /// 誰も掴んでいないのにカメラ操作が禁止されたまま固まっていたら解除する。
        ///
        /// 掴みの取り残しは自分の管轄内なら時効で回収できるが、
        /// カメラを止める口は BlankMapAdd 側にも、ゲーム本体側にもある。
        /// どこの誰が止めたかに関わらず「操作不能のまま放置しない」を保証するための最後の砦。
        /// </summary>
        private void AutoUnstickCamera()
        {
            float now = Time.unscaledTime;
            if (now < _nextAutoUnstickCheckTime) return;
            _nextAutoUnstickCheckTime = now + 0.1f;

            if (!_plugin.AutoUnstickEnabled)
            {
                _cameraBlockedSince = -1f;
                return;
            }

            // 自分が掴んでいる最中は当然止まっている。対象外。
            if (_captureApplied || _activeTokens.Count > 0 || HasSavedState())
            {
                _cameraBlockedSince = -1f;
                return;
            }

            CameraControl_Ver2 ctrl = ResolveCtrlCamera();
            if (ctrl == null)
            {
                _cameraBlockedSince = -1f;
                return;
            }

            if (!IsNoCtrlFunc(ctrl.NoCtrlCondition, AlwaysTrue) && !IsNoCtrlFunc(ctrl.KeyCondition, AlwaysFalse))
            {
                _cameraBlockedSince = -1f;
                return;
            }

            if (_cameraBlockedSince < 0f)
            {
                _cameraBlockedSince = now;
                return;
            }

            if (now - _cameraBlockedSince < AutoUnstickSeconds)
                return;

            _cameraBlockedSince = -1f;
            // Automatic cleanup is not the user's explicit panic override.
            // Restore only our orphaned sentinel; retain a new menu restriction
            // on the other input channel.
            if (IsNoCtrlFunc(ctrl.NoCtrlCondition, AlwaysTrue))
                ctrl.NoCtrlCondition = AlwaysFalse;
            if (IsNoCtrlFunc(ctrl.KeyCondition, AlwaysFalse))
                ctrl.KeyCondition = AlwaysTrue;
        }

        private static string DescribeFunc(BaseCameraControl_Ver2.NoCtrlFunc func)
        {
            if (func == null) return "null";
            try
            {
                System.Reflection.MethodInfo m = func.Method;
                string type = m.DeclaringType != null ? m.DeclaringType.FullName : "?";
                string target = func.Target != null ? func.Target.GetType().FullName : "static";
                return type + "." + m.Name + "(target=" + target + ")";
            }
            catch { return "<broken>"; }
        }

        private static string DescribeSet(HashSet<string> set)
        {
            if (set == null || set.Count == 0) return "[]";
            return "[" + string.Join(",", new List<string>(set).ToArray()) + "]";
        }

        private static string DescribeKeys(Dictionary<string, List<Rect>> map)
        {
            if (map == null || map.Count == 0) return "[]";
            List<string> keys = new List<string>(map.Keys);
            return "[" + string.Join(",", keys.ToArray()) + "]";
        }

        /// <summary>
        /// カメラ操作を無条件で取り戻す最終手段。緊急キーから呼ぶ。
        /// これで戻るならこの系統が犯人、戻らないなら別のプラグインが握っている。
        /// </summary>
        internal void ForceCameraControlBack(string reason)
        {
            CameraControl_Ver2 ctrl = ResolveCtrlCamera();
            if (ctrl == null)
            {
                _plugin.LogAlways("force camera control back (" + reason + "): controller not found");
                return;
            }

            string before = DescribeFunc(ctrl.NoCtrlCondition) + " / " + DescribeFunc(ctrl.KeyCondition);
            GlobalMethod.SetCameraMoveFlag(ctrl, _bPlay: true);
            ctrl.NoCtrlCondition = AlwaysFalse;
            ctrl.KeyCondition = AlwaysTrue;
            _plugin.LogAlways("force camera control back (" + reason + "): was " + before);
        }

        private static bool TryExtractOwnerFromToken(string token, out string owner)
        {
            owner = string.Empty;
            if (string.IsNullOrEmpty(token))
                return false;

            int sep = token.IndexOf("::", System.StringComparison.Ordinal);
            if (sep <= 0)
                return false;

            return TryNormalizeKey(token.Substring(0, sep), out owner);
        }
    }
}
