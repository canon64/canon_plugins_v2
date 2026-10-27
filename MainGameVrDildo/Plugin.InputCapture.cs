using MainGameUiInputCapture;
using UnityEngine;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        private const string SettingsInputOwner = Guid + ".settings";
        private const string SettingsDragSource = "imgui-drag";
        private readonly Rect[] _settingsInputRects = new Rect[1];
        private int _settingsHotControl;

        private void SyncStandaloneInputCapture()
        {
            if (!Application.isFocused || !_showSettings)
            {
                ClearStandaloneInputCapture();
                return;
            }
            _settingsInputRects[0] = _window;
            UiInputCaptureApi.SetInputBlockRects(SettingsInputOwner, _settingsInputRects);
            UiInputCaptureApi.SetIdleCursorUnlock(SettingsInputOwner, true);
            UiInputCaptureApi.Sync(SettingsInputOwner, SettingsDragSource,
                _settingsHotControl != 0 && GUIUtility.hotControl == _settingsHotControl && Input.GetMouseButton(0));
        }

        private void ClearStandaloneInputCapture()
        {
            UiInputCaptureApi.Sync(SettingsInputOwner, SettingsDragSource, false);
            UiInputCaptureApi.ClearInputBlockRects(SettingsInputOwner);
            UiInputCaptureApi.SetIdleCursorUnlock(SettingsInputOwner, false);
            if (_settingsHotControl != 0 && GUIUtility.hotControl == _settingsHotControl)
                GUIUtility.hotControl = 0;
            _settingsHotControl = 0;
        }

        private void OnDisable()
        {
            ClearStandaloneInputCapture();
            DetachDesktopGizmo();
        }
    }
}
