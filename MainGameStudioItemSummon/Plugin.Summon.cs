using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameStudioItemSummon
{
    public sealed partial class Plugin
    {
        private readonly List<GameObject> _summoned = new List<GameObject>();

        // 名前で台帳を引き、ゲーム共通ローダーで実体化してカメラ前に出す。
        // CommonLib.LoadAsset は Sideloader が横取りする＝zipmod解決＋シェーダ修復込み。
        internal void SummonByName(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                LogWarn("summon: item name is empty");
                return;
            }

            // 台帳が未ロードならキャッシュから即読み込み（Build押下不要）。
            if (!CatalogReady && !TryLoadCacheNow("summon-autoload"))
            {
                LogWarn("summon: catalog not ready and no cache. press 'Build / Load' first.");
                return;
            }

            CatalogEntry entry = FindByName(query);
            if (entry == null)
            {
                LogWarn($"summon: no item matches \"{query}\" (items={_catalog.Count})");
                return;
            }

            try
            {
                GameObject go = CommonLib.LoadAsset<GameObject>(entry.BundlePath, entry.FileName, clone: true, entry.Manifest);
                if (go == null)
                {
                    LogError($"summon: LoadAsset returned null name=\"{entry.Name}\" bundle={entry.BundlePath} file={entry.FileName} manifest={entry.Manifest}");
                    return;
                }

                PlaceInFrontOfCamera(go);
                RemoveColliders(go);
                go.name = $"Summoned_{entry.Name}";
                _summoned.Add(go);

                LogInfo($"summoned \"{entry.Name}\" no={entry.No} bundle={entry.BundlePath} pos={go.transform.position} total={_summoned.Count}");
            }
            catch (Exception ex)
            {
                LogError($"summon failed name=\"{entry.Name}\": {ex}");
            }
        }

        private CatalogEntry FindByName(string query)
        {
            // 完全一致（大小無視）優先。
            for (int i = 0; i < _catalog.Count; i++)
            {
                if (string.Equals(_catalog[i].Name, query, StringComparison.OrdinalIgnoreCase))
                    return _catalog[i];
            }
            // 部分一致。
            for (int i = 0; i < _catalog.Count; i++)
            {
                string n = _catalog[i].Name;
                if (!string.IsNullOrEmpty(n) && n.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    return _catalog[i];
            }
            return null;
        }

        private static void PlaceInFrontOfCamera(GameObject go)
        {
            Camera cam = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();
            if (cam != null)
            {
                go.transform.position = cam.transform.position + cam.transform.forward * 2f;
                go.transform.rotation = Quaternion.identity;
            }
            else
            {
                go.transform.position = Vector3.zero;
            }
        }

        private static void RemoveColliders(GameObject go)
        {
            var cols = go.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    Destroy(cols[i]);
            }
        }

        internal void ClearSummoned()
        {
            int n = 0;
            for (int i = 0; i < _summoned.Count; i++)
            {
                if (_summoned[i] != null) { Destroy(_summoned[i]); n++; }
            }
            _summoned.Clear();
            LogInfo($"cleared summoned items={n}");
        }
    }
}
