using MainGameBlankMapAdd.UI;
using UnityEngine;
using MainGameUiInputCapture;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private UiInputCaptureController _uiInputCapture;

        private void OnDisable()
        {
            HideUiBlocker();
            _uiResources?.SyncPlaybackCursor(false);
            StopUiCapture("disabled");
        }

        private void OnApplicationFocus(bool focused)
        {
            if (focused) return;
            HideUiBlocker();
            _uiResources?.SyncPlaybackCursor(false);
            StopUiCapture("focus-lost");
        }

        private void InitializeUiInputCapture()
        {
            _uiInputCapture = new UiInputCaptureController(
                new Application.CoroutineHost(this),
                () => _gizmo != null && _gizmo.IsDragging,
                () => IsPlaybackBarDraggingNow(),
                () => _editMode && _gizmo != null && _gizmo.IsVisible,
                LogInfo);
        }

        private void LateUpdate()
        {
            if (_videoRoomRoot != null && _settings != null && _settings.SyncVoiceSourcesToVideoRoom)
                SyncActiveVoiceSourcesToVideoRoom(false);
            UpdateUiCaptureState();
            _uiInputCapture?.UpdateEditCursorUnlockLoop();
        }

        private void UpdateUiCaptureState()
        {
            if (!UnityEngine.Application.isFocused)
            {
                HideUiBlocker();
                _uiResources?.SyncPlaybackCursor(false);
                StopUiCapture("focus-lost");
                return;
            }
            UpdateEmbeddedBarDrag(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y));
            // Use one owner for bar capture and release. Keep the pointer free so it can
            // reach the bottom trigger again; this does not block input outside the UI.
            _uiResources?.SyncPlaybackCursor(_settings != null && _settings.EnablePlaybackBar
                && _videoRoomRoot != null && !_playbackBarCtrlHidden);
            _uiInputCapture?.UpdateCapture();
            UiInputCaptureApi.Sync("MainGameBlankMapAdd.PlaybackBar", "embedded-drag",
                IsPlaybackBarReady() && _embeddedBarDragging && Input.GetMouseButton(0));
        }

        internal void NotifyGizmoDragState(bool dragging)
        {
            _uiInputCapture?.NotifyGizmoDragState(dragging);
        }

        private void StopUiCapture(string reason)
        {
            UiInputCaptureApi.Sync("MainGameBlankMapAdd.PlaybackBar", "embedded-drag", false);
            _uiInputCapture?.Stop(reason);
        }

        private bool IsPlaybackBarReady()
        {
            if (!UnityEngine.Application.isFocused) return false;
            if (_playbackBarCtrlHidden) return false;
            if (_settings == null || !_settings.EnablePlaybackBar) return false;
            if (_videoRoomRoot == null) return false;
            return !_playbackBarHiddenByUser;
        }

        private bool IsPlaybackBarDraggingNow()
        {
            if (!IsPlaybackBarReady()) return false;
            // Keep the native camera input captured before a click/scroll arrives in the popup.
            if (_actorCoordinatesOpen && (_actorCoordinatesList.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y))
                || _actorCoordinatesAnchor.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y)))) return true;
            if (_roomOverlayDropdownOpen && _roomOverlayListRect.Contains(new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y))) return true;
            if (_embeddedBarDragging || _playbackSeekDragging || _playbackVolumeDragging || _playbackGainDragging) return true;
            if (!Input.GetMouseButton(0)) return false;

            float barHeight = GetPlaybackBarHeightPx();
            float marginX = Mathf.Max(0f, _settings.PlaybackBarMarginX);
            float barWidth = Mathf.Max(120f, Screen.width - marginX * 2f);
            var barRect = new Rect(marginX, Screen.height - barHeight, barWidth, barHeight);
            var mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return barRect.Contains(mouseGui);
        }
    }
}
