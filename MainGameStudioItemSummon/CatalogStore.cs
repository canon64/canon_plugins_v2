using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGameStudioItemSummon
{
    internal static class CatalogStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static bool TryLoad(string path, out CatalogCacheFile cache, out string error)
        {
            cache = null;
            error = null;
            try
            {
                if (!File.Exists(path))
                {
                    error = "no cache file";
                    return false;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                var serializer = new DataContractJsonSerializer(typeof(CatalogCacheFile));
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    cache = serializer.ReadObject(ms) as CatalogCacheFile;
                }

                if (cache == null || cache.Items == null)
                {
                    error = "cache parse failed";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static void Save(string path, CatalogCacheFile cache)
        {
            var serializer = new DataContractJsonSerializer(typeof(CatalogCacheFile));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, cache ?? new CatalogCacheFile());
                File.WriteAllText(path, Encoding.UTF8.GetString(ms.ToArray()), Utf8NoBom);
            }
        }

        // studio/info に積まれたバンドル名の集合から署名を作る。
        // mod を足し引きすると一覧が変わるので署名がズレ、キャッシュ無効を検出できる。
        internal static string ComputeSignature(IList<string> bundleNames)
        {
            var names = new List<string>(bundleNames ?? new List<string>());
            names.Sort(StringComparer.OrdinalIgnoreCase);

            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            for (int i = 0; i < names.Count; i++)
            {
                string s = names[i] ?? string.Empty;
                for (int j = 0; j < s.Length; j++)
                {
                    hash ^= s[j];
                    hash *= prime;
                }
                hash ^= 10; // 区切り(改行)
                hash *= prime;
            }
            return $"v1:{names.Count}:{hash:x16}";
        }
    }
}
