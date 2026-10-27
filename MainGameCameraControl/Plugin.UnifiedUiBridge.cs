using MainGameUnifiedUi;

namespace MainGameCameraControl
{
    // 総合UI（MainGameUnifiedUi）への出展ブース。総合UI未ロード時は従来どおり自前窓。
    internal sealed partial class Plugin
    {
        private const string UnifiedTabId = "cameracontrol";
        private const string UnifiedTabLabel = "Camera";
        private const int UnifiedTabOrder = 0;

        private bool IsHostedByUnifiedUi()
        {
            return UnifiedUiApi.IsAvailable && UnifiedUiApi.IsHosting(UnifiedTabId);
        }

        private void RegisterUnifiedTabIfAvailable()
        {
            if (UnifiedUiApi.IsAvailable && !UnifiedUiApi.IsHosting(UnifiedTabId))
                UnifiedUiApi.RegisterTab(UnifiedTabId, UnifiedTabLabel, DrawUnifiedContent, UnifiedTabOrder);
        }

        private void DrawUnifiedContent()
        {
            bool uiChanged = DrawCameraControlBody();
            ApplyPostDrawMutations(uiChanged);
        }
    }
}
