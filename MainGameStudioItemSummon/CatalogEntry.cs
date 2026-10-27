using System.Collections.Generic;

namespace MainGameStudioItemSummon
{
    // 平坦化したアイテム1件。名前で引いて住所(bundle/file/manifest)を得る。
    // DataContractJsonSerializer でそのままキャッシュ保存するため public フィールド。
    public sealed class CatalogEntry
    {
        public int Group;
        public int Category;
        public int No;
        public string Name;
        public string GroupName;
        public string CategoryName;
        public string BundlePath;
        public string FileName;
        public string Manifest;
    }

    // ディスクキャッシュの中身。Signature が一致すれば再構築不要。
    public sealed class CatalogCacheFile
    {
        public string Signature;
        public List<CatalogEntry> Items = new List<CatalogEntry>();
    }
}
