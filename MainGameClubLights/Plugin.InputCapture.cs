using MainGameUiInputCapture;
using UnityEngine;

namespace MainGameClubLights
{
    public sealed partial class Plugin
    {
        private const string InputOwnerKey  = Guid + ".input";
        private const string SourceWindow   = "window";
        private const string SourceSlider   = "slider";
        private const string SourceScroll   = "scroll";
        private const string SourceGizmo    = "gizmo-drag";

        private bool _windowDragging;
        private bool _sliderDragging;
        private bool _scrollDragging;
        private bool _gizmoDragging;
        private bool _gizmoGestureActive;

        // ── 毎フレーム呼び出し ───────────────────────────────────────────────

        private void UpdateInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable) return;

            bool standaloneVisible = _settings.UiVisible && !IsHostedByUnifiedUi();

            // マウスボタン離したらリセット
            if (!Input.GetMouseButton(0))
            {
                _windowDragging = false;
                _sliderDragging = false;
                _scrollDragging = false;
                _gizmoDragging  = false;
            }

            // UIが非表示になったら全解放
            if (!standaloneVisible)
            {
                _windowDragging = false;
                _sliderDragging = false;
                _scrollDragging = false;
            }

            // UI表示中はカーソルを常に解放。
            // 総合UIのタブに載っている間は standaloneVisible が常に false になるため、
            // それだけを条件にするとタブ表示中は一度もカーソルを解放できず、
            // ギズモを掴みに行けなくなる。ギズモ操作中も解放を維持する。
            bool gizmoVisible = false;
            if (_insideHScene && !_astralModeSuppressed) {
                foreach (var entry in _lightEntries)
                    if (entry.Gizmo != null && entry.Gizmo.IsVisible) { gizmoVisible = true; break; }
                if (!gizmoVisible) foreach (var entry in _folderEntries)
                    if (entry.Gizmo != null && entry.Gizmo.IsVisible) { gizmoVisible = true; break; }
            }
            bool needCursor = standaloneVisible || _settings.UiVisible || _gizmoDragging || gizmoVisible;
            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, needCursor);

            UiInputCaptureApi.Sync(InputOwnerKey, SourceWindow, standaloneVisible && _windowDragging);
            UiInputCaptureApi.Sync(InputOwnerKey, SourceSlider, standaloneVisible && _sliderDragging);
            UiInputCaptureApi.Sync(InputOwnerKey, SourceScroll, standaloneVisible && _scrollDragging);
            UiInputCaptureApi.Sync(InputOwnerKey, SourceGizmo,  _gizmoDragging);
        }

        private void ReleaseInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable) return;
            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, false);
            UiInputCaptureApi.EndOwner(InputOwnerKey);
            _windowDragging = false;
            _sliderDragging = false;
            _scrollDragging = false;
            _gizmoDragging  = false;
            _gizmoGestureActive = false;
        }

        // UI側から呼ぶ
        internal void SetWindowDragging(bool v) => _windowDragging = v;
        internal void SetSliderDragging(bool v) => _sliderDragging = v;
        internal void SetScrollDragging(bool v) => _scrollDragging = v;

        // ギズモから呼ぶ
        internal void OnGizmoDragStateChanged(bool dragging) {
            bool wasDragging = _gizmoGestureActive;
            _gizmoGestureActive = dragging;
            _gizmoDragging = dragging;
            if (dragging) {
                foreach (var entry in _lightEntries) if (entry.Gizmo != null && entry.Gizmo.IsDragging) {
                    if (entry.Settings.FollowCamera) SetLightFollowCamera(entry, false);
                    if (entry.Settings.RevolutionEnabled) SetRevolutionEnabled(entry, false);
                }
            } else if (wasDragging) SaveCurrentProfile(out _);
        }
    }
}
