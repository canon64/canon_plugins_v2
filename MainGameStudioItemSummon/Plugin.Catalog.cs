using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using StudioInfo = Studio.Info;

namespace MainGameStudioItemSummon
{
    public sealed partial class Plugin
    {
        private readonly List<CatalogEntry> _catalog = new List<CatalogEntry>();
        private bool _catalogBuilding;
        private string _currentSignature;

        internal bool CatalogReady { get; private set; }
        internal IReadOnlyList<CatalogEntry> Catalog => _catalog;

        // HUD 実況用ライブ状態。
        internal string BuildStatus = "idle";
        internal long BuildSteps;
        internal float BuildElapsed;
        internal int BuildLiveItems;

        private string CachePath => Path.Combine(_pluginDir, _settings.CacheFileName);

        // キー起点の唯一の入口。キャッシュが有効なら即読み込み、無ければフル構築。
        // 自動では一切走らない（タイトル画面等で勝手に動かない）。
        private void EnsureCatalog(string trigger, bool force)
        {
            if (_catalogBuilding)
            {
                LogInfo($"ignored ({trigger}): already building");
                return;
            }
            if (CatalogReady && !force)
            {
                LogInfo($"already ready items={_catalog.Count} ({trigger})");
                return;
            }
            if (!force && TryLoadCacheNow(trigger))
                return;

            _catalogBuilding = true;
            BuildStatus = "building";
            BuildSteps = 0;
            BuildElapsed = 0f;
            BuildLiveItems = 0;
            LogInfo($"build start ({trigger}) force={force}");
            StartCoroutine(BuildCatalogCoroutine());
        }

        private bool TryLoadCacheNow(string trigger)
        {
            List<string> bundles = EnumerateInfoBundles();
            if (bundles == null || bundles.Count == 0)
            {
                LogInfo($"({trigger}) studio/info not ready yet (bundles=0) -> will full build");
                return false;
            }

            _currentSignature = CatalogStore.ComputeSignature(bundles);

            if (!CatalogStore.TryLoad(CachePath, out var cache, out string err))
            {
                LogInfo($"({trigger}) no usable cache ({err}) -> full build (~2min once)");
                return false;
            }
            if (cache.Signature != _currentSignature)
            {
                LogInfo($"({trigger}) cache stale (mods changed) -> full build. cached={cache.Signature} now={_currentSignature}");
                return false;
            }

            _catalog.Clear();
            _catalog.AddRange(cache.Items);
            CatalogReady = true;
            BuildStatus = "ready (cache)";
            BuildLiveItems = _catalog.Count;
            LogInfo($"loaded from cache items={_catalog.Count} (instant)");
            return true;
        }

        private List<string> EnumerateInfoBundles()
        {
            try
            {
                return CommonLib.GetAssetBundleNameListFromPath("studio/info/", subdirCheck: true);
            }
            catch (Exception ex)
            {
                LogWarn($"bundle enumerate failed: {ex.Message}");
                return null;
            }
        }

        // B2: 本編で Studio.Info を起こし、純正ローダーを1回フルで走らせる。
        // Sideloader が純正ローダーをフックしているので mod アイテムも合流して載る。
        // ローダー本体には手を入れない（取りこぼし防止）。成功後にキャッシュ保存。
        private IEnumerator BuildCatalogCoroutine()
        {
            StudioInfo info = ResolveStudioInfo(createIfMissing: true);
            if (info == null)
            {
                LogError("failed to obtain Studio.Info instance");
                BuildStatus = "error";
                _catalogBuilding = false;
                yield break;
            }

            List<string> bundles = EnumerateInfoBundles();
            LogInfo($"studio/info bundles={(bundles != null ? bundles.Count : 0)}");
            _currentSignature = CatalogStore.ComputeSignature(bundles ?? new List<string>());

            if (!info.isLoadList)
            {
                LogInfo("running native loader (full, mod merge via Sideloader) ...");
                float t0 = Time.realtimeSinceStartup;

                IEnumerator loader;
                try
                {
                    loader = info.LoadExcelDataCoroutine();
                }
                catch (Exception ex)
                {
                    LogError($"failed to start loader: {ex}");
                    BuildStatus = "error";
                    _catalogBuilding = false;
                    yield break;
                }

                float lastBeat = t0;
                while (true)
                {
                    object current;
                    bool moved;
                    try
                    {
                        moved = loader.MoveNext();
                        current = moved ? loader.Current : null;
                    }
                    catch (Exception ex)
                    {
                        LogError($"loader threw: {ex}");
                        BuildStatus = "error";
                        _catalogBuilding = false;
                        yield break;
                    }

                    if (!moved)
                        break;

                    BuildSteps++;
                    BuildElapsed = Time.realtimeSinceStartup - t0;
                    BuildLiveItems = info.dicItemLoadInfo?.Count ?? 0;
                    BuildStatus = $"building {BuildElapsed:F0}s";

                    float now = Time.realtimeSinceStartup;
                    if (now - lastBeat >= 2f)
                    {
                        lastBeat = now;
                        LogInfo($"loader running... steps={BuildSteps} elapsed={BuildElapsed:F1}s items={BuildLiveItems}");
                    }
                    yield return current;
                }

                LogInfo($"loader done in {Time.realtimeSinceStartup - t0:F1}s steps={BuildSteps}");
            }
            else
            {
                LogInfo("Studio.Info already loaded, reusing dictionaries");
            }

            bool ok = false;
            try
            {
                FlattenCatalog(info);
                CatalogReady = true;
                BuildStatus = "ready (built)";
                BuildLiveItems = _catalog.Count;
                ok = true;
            }
            catch (Exception ex)
            {
                LogError($"flatten failed: {ex}");
                BuildStatus = "error";
            }
            finally
            {
                _catalogBuilding = false;
            }

            if (ok)
                SaveCache();
        }

        private void SaveCache()
        {
            try
            {
                var cache = new CatalogCacheFile
                {
                    Signature = _currentSignature,
                    Items = new List<CatalogEntry>(_catalog),
                };
                CatalogStore.Save(CachePath, cache);
                LogInfo($"cache saved items={_catalog.Count} path={CachePath}");
            }
            catch (Exception ex)
            {
                LogError($"cache save failed: {ex}");
            }
        }

        private StudioInfo ResolveStudioInfo(bool createIfMissing)
        {
            try
            {
                if (Singleton<StudioInfo>.IsInstance())
                    return Singleton<StudioInfo>.Instance;
            }
            catch (Exception ex)
            {
                LogWarn($"Singleton<Info>.IsInstance check failed: {ex.Message}");
            }

            if (!createIfMissing)
                return null;

            var go = new GameObject("StudioItemSummonInfo");
            UnityEngine.Object.DontDestroyOnLoad(go);
            StudioInfo info = go.AddComponent<StudioInfo>();
            LogInfo("created Studio.Info instance for catalog");
            return info;
        }

        private void FlattenCatalog(StudioInfo info)
        {
            _catalog.Clear();

            var groups = info.dicItemGroupCategory;
            var items = info.dicItemLoadInfo;
            if (items == null)
            {
                LogWarn("dicItemLoadInfo is null");
                return;
            }

            foreach (var gkv in items)
            {
                int group = gkv.Key;
                string groupName = $"group{group}";
                StudioInfo.GroupInfo gi = null;
                if (groups != null && groups.TryGetValue(group, out gi) && gi != null && !string.IsNullOrEmpty(gi.name))
                    groupName = gi.name;

                if (gkv.Value == null)
                    continue;

                foreach (var ckv in gkv.Value)
                {
                    int cat = ckv.Key;
                    string catName = $"cat{cat}";
                    if (gi != null && gi.dicCategory != null && gi.dicCategory.TryGetValue(cat, out var cn) && !string.IsNullOrEmpty(cn))
                        catName = cn;

                    if (ckv.Value == null)
                        continue;

                    foreach (var nkv in ckv.Value)
                    {
                        StudioInfo.ItemLoadInfo li = nkv.Value;
                        if (li == null)
                            continue;

                        _catalog.Add(new CatalogEntry
                        {
                            Group = group,
                            Category = cat,
                            No = nkv.Key,
                            Name = li.name,
                            GroupName = groupName,
                            CategoryName = catName,
                            BundlePath = li.bundlePath,
                            FileName = li.fileName,
                            Manifest = li.manifest,
                        });
                    }
                }
            }

            LogInfo($"catalog built items={_catalog.Count} groups={items.Count}");

            int sample = Mathf.Max(0, _settings.LogSampleCount);
            int shown = Mathf.Min(sample, _catalog.Count);
            for (int i = 0; i < shown; i++)
            {
                var e = _catalog[i];
                LogInfo($"[item] no={e.No} name=\"{e.Name}\" group=\"{e.GroupName}\" cat=\"{e.CategoryName}\" bundle={e.BundlePath} file={e.FileName}");
            }
        }
    }
}
