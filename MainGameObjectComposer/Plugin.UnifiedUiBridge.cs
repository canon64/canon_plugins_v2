using MainGameUnifiedUi;

namespace MainGameObjectComposer
{
    // 総合UI（MainGameUnifiedUi）への「出展ブース」。
    // 総合UIがロードされていれば、自分の窓の中身をタブとして登録し、
    // 総合UIが表示している間は自前窓を畳む（OnGUI 側で IsHostedByUnifiedUi を見る）。
    // 総合UI未ロード時は何もせず、従来どおり自前窓で動く。
    public sealed partial class Plugin
    {
        private const string UnifiedTabId = "objectcomposer";
        private const string UnifiedTabLabel = "Composer";
        private const int UnifiedTabOrder = 10;

        private bool IsHostedByUnifiedUi()
        {
            return UnifiedUiApi.IsAvailable && UnifiedUiApi.IsHosting(UnifiedTabId);
        }

        // タブが消えた場合だけ登録し直す。
        private void RegisterUnifiedTabIfAvailable()
        {
            if (UnifiedUiApi.IsAvailable && !UnifiedUiApi.IsHosting(UnifiedTabId))
                UnifiedUiApi.RegisterTab(UnifiedTabId, UnifiedTabLabel, DrawUnifiedContent, UnifiedTabOrder);
        }

        // 総合UIのタブ本体。自前窓と同じ中身を、総合UIのスクロール枠の中に描く。
        private void DrawUnifiedContent()
        {
            DrawMainBody();
        }
    }
}
