using System;

namespace MainGameUnifiedUi
{
    /// <summary>
    /// 総合UI（タブホスト）への公開API。
    /// 各プラグインは本DLLを soft-dependency 参照し、自分の窓の中身を
    /// <see cref="RegisterTab"/> でタブとして登録する。総合UI未ロード時は
    /// <see cref="IsAvailable"/> が false になるので、従来の自前窓へフォールバックする。
    /// </summary>
    public static class UnifiedUiApi
    {
        /// <summary>総合UIプラグインがロード済みか。</summary>
        public static bool IsAvailable => Plugin.Instance != null;

        /// <summary>埋込ホスト用の ID/表示名ペア。登録が変わるまで同じ配列を返す。変更禁止。</summary>
        public static string[] GetEmbeddedTabs() => Plugin.Instance?.GetEmbeddedTabs() ?? Array.Empty<string>();

        /// <summary>ホストのGUILayout領域へタブ内容だけを描画。別窓の選択・表示には影響しない。</summary>
        public static void DrawEmbeddedTab(string id) => Plugin.Instance?.DrawEmbeddedTab(id);

        /// <summary>
        /// 描画コールバックをタブとして登録（同一IDは上書き更新）。
        /// draw は総合UIの窓内（GUILayout コンテキスト）から毎フレーム呼ばれる。
        /// order 昇順でタブが並ぶ。
        /// </summary>
        public static void RegisterTab(string id, string label, Action draw, int order = 0)
        {
            Plugin.Instance?.RegisterTab(id, label, draw, order);
        }

        /// <summary>登録済みタブを解除する。</summary>
        public static void UnregisterTab(string id)
        {
            Plugin.Instance?.UnregisterTab(id);
        }

        /// <summary>
        /// 指定IDが総合UIに登録済みか。参加プラグインは true の間、自前の窓を描かない。
        /// </summary>
        public static bool IsHosting(string id)
        {
            return Plugin.Instance != null && Plugin.Instance.IsHosting(id);
        }

        /// <summary>総合UIを開いて指定タブを選択する。登録済みなら true。</summary>
        public static bool ShowTab(string id)
        {
            return Plugin.Instance != null && Plugin.Instance.ShowTab(id);
        }

        /// <summary>総合UIの窓が表示中か。</summary>
        public static bool IsVisible => Plugin.Instance != null && Plugin.Instance.IsVisible;

        /// <summary>総合UIの窓の表示/非表示を設定する。</summary>
        public static void SetVisible(bool visible)
        {
            Plugin.Instance?.SetVisible(visible);
        }
    }
}
