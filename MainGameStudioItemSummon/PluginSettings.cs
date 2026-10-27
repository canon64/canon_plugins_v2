namespace MainGameStudioItemSummon
{
    // DataContractJsonSerializer は属性なしの public フィールドをそのまま直列化する。
    // 操作（構築/再構築/HUD表示）は ConfigManager 側のボタン・トグルで行う。
    // ここは数値系パラメータのみ（StudioItemSummonSettings.json で変更可能）。
    public sealed class PluginSettings
    {
        // HUDで遡れるログ保持行数（スクロールバック長）。
        public int LogBufferLines = 300;

        // 台帳キャッシュのファイル名（プラグインフォルダ内）。
        public string CacheFileName = "StudioItemCatalog.json";

        // 台帳構築後、ログへ出力するアイテム名サンプル数。
        public int LogSampleCount = 30;
    }
}
