using MainGameUnifiedUi;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        private const string TestTabId = "vrdildo-test";

        private void RegisterTestTabIfAvailable()
        {
            if (UnifiedUiApi.IsAvailable && !UnifiedUiApi.IsHosting(TestTabId))
                UnifiedUiApi.RegisterTab(TestTabId, "ディルド", DrawTestTab, 80);
        }
    }
}
