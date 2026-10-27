using MainGameUnifiedUi;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private const string UnifiedTabId = "subcamera";
        private const string UnifiedTabLabel = "SubCam";
        private const int UnifiedTabOrder = 30;

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
            DrawWindowBody(saveWindowLayout: false);
        }
    }
}
