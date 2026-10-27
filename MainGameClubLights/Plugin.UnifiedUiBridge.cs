using MainGameUnifiedUi;

namespace MainGameClubLights
{
    // 総合UI（MainGameUnifiedUi）への出展ブース。総合UI未ロード時は従来どおり自前窓。
    public sealed partial class Plugin
    {
        private const string UnifiedTabId = "clublights";
        private const string UnifiedTabLabel = "ClubLights";
        private const int UnifiedTabOrder = 40;

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
            DrawClubLightsBody();
        }
    }
}
