using MainGameUnifiedUi;

namespace MainGameHScenePoseInjector
{
    public sealed partial class Plugin
    {
        private const string UnifiedTabId = "hscene-pose";
        private const string UnifiedTabLabel = "Pose";
        private const int UnifiedTabOrder = 20;

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
            if (!IsInHScene())
            {
                UnityEngine.GUILayout.Label("Hシーン中のみ使用できます");
                return;
            }

            EnsureRectFromSettings();
            DrawWindowContents(WindowId, false);
        }
    }
}
