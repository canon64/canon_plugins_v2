using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MainGameHScenePoseInjector
{
    internal sealed class PoseEntry
    {
        public int Id;
        public string Bundle;
        public string Asset;
        public string Name;
        public string State;
        public bool UseFemale;
        public bool UseMale;
    }

    internal sealed class PatternEntry
    {
        public int Id;
        public string Name;
    }

    public sealed partial class Plugin
    {
        private List<PoseEntry> _poseList;
        private bool _poseListLoadAttempted;
        private List<PatternEntry> _eyebrowList;
        private List<PatternEntry> _eyesList;
        private List<PatternEntry> _mouthList;
        private bool _patternListLoadAttempted;

        internal List<PatternEntry> GetEyebrowList() { EnsurePatternsLoaded(); return _eyebrowList ?? new List<PatternEntry>(); }
        internal List<PatternEntry> GetEyesList() { EnsurePatternsLoaded(); return _eyesList ?? new List<PatternEntry>(); }
        internal List<PatternEntry> GetMouthList() { EnsurePatternsLoaded(); return _mouthList ?? new List<PatternEntry>(); }

        private void EnsurePatternsLoaded()
        {
            if (_patternListLoadAttempted) return;
            _patternListLoadAttempted = true;
            LoadPatternListsFromExcel();
            LogInfo("pattern lists loaded eb=" + (_eyebrowList?.Count ?? 0)
                + " eyes=" + (_eyesList?.Count ?? 0)
                + " mouth=" + (_mouthList?.Count ?? 0));
        }

        private void LoadPatternListsFromExcel()
        {
            Dictionary<int, PatternEntry> ebTable = new Dictionary<int, PatternEntry>();
            Dictionary<int, PatternEntry> eTable = new Dictionary<int, PatternEntry>();
            Dictionary<int, PatternEntry> mTable = new Dictionary<int, PatternEntry>();

            try
            {
                List<string> bundleNames = CommonLib.GetAssetBundleNameListFromPath("custom/customscenelist/", subdirCheck: true);
                bundleNames.Sort();
                foreach (string file in bundleNames)
                {
                    UnityEngine.Object[] allAssets;
                    try
                    {
                        allAssets = AssetBundleManager.LoadAllAsset(file, typeof(ExcelData)).GetAllAssets<ExcelData>();
                    }
                    catch (Exception ex)
                    {
                        LogWarn("excel load failed (patterns) bundle=" + file + " err=" + ex.Message);
                        continue;
                    }
                    if (allAssets == null) continue;
                    foreach (UnityEngine.Object asset in allAssets)
                    {
                        ExcelData excelData = asset as ExcelData;
                        if (excelData == null) continue;
                        Dictionary<int, PatternEntry> target = null;
                        if (excelData.name == "cus_eb_ptn") target = ebTable;
                        else if (excelData.name == "cus_e_ptn") target = eTable;
                        else if (excelData.name == "cus_m_ptn") target = mTable;
                        else continue;

                        foreach (ExcelData.Param p in excelData.list)
                        {
                            List<string> row = p.list;
                            if (row == null || row.Count < 1) continue;
                            int id;
                            if (!int.TryParse(SafeGet(row, 0), out id)) continue;
                            string name = SafeGet(row, 1);
                            target[id] = new PatternEntry { Id = id, Name = string.IsNullOrWhiteSpace(name) ? ("ptn_" + id) : name };
                        }
                    }
                    try { AssetBundleManager.UnloadAssetBundle(file, isUnloadForceRefCount: true); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogWarn("pattern list load exception: " + ex.Message);
            }

            _eyebrowList = new List<PatternEntry>(ebTable.Values);
            _eyebrowList.Sort((a, b) => a.Id.CompareTo(b.Id));
            _eyesList = new List<PatternEntry>(eTable.Values);
            _eyesList.Sort((a, b) => a.Id.CompareTo(b.Id));
            _mouthList = new List<PatternEntry>(mTable.Values);
            _mouthList.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        internal List<PoseEntry> GetPoseList()
        {
            if (_poseList != null)
                return _poseList;

            if (_poseListLoadAttempted)
                return new List<PoseEntry>();

            _poseListLoadAttempted = true;
            _poseList = LoadPoseListFromExcel();
            EnsurePoseCategoryDictionary(_poseList);
            LogInfo("pose list loaded count=" + (_poseList != null ? _poseList.Count : 0));
            return _poseList ?? new List<PoseEntry>();
        }

        private List<PoseEntry> LoadPoseListFromExcel()
        {
            List<PoseEntry> result = new List<PoseEntry>();
            try
            {
                List<string> bundleNames = CommonLib.GetAssetBundleNameListFromPath("custom/customscenelist/", subdirCheck: true);
                bundleNames.Sort();
                foreach (string file in bundleNames)
                {
                    UnityEngine.Object[] allAssets;
                    try
                    {
                        allAssets = AssetBundleManager.LoadAllAsset(file, typeof(ExcelData)).GetAllAssets<ExcelData>();
                    }
                    catch (Exception ex)
                    {
                        LogWarn("excel load failed bundle=" + file + " err=" + ex.Message);
                        continue;
                    }
                    if (allAssets == null) continue;
                    foreach (UnityEngine.Object asset in allAssets)
                    {
                        ExcelData excelData = asset as ExcelData;
                        if (excelData == null || excelData.name != "cus_pose")
                            continue;
                        foreach (ExcelData.Param p in excelData.list.Skip(1))
                        {
                            List<string> row = p.list;
                            if (row == null || row.Count < 5) continue;
                            int id;
                            if (!int.TryParse(SafeGet(row, 0), out id)) continue;
                            string bundle = SafeGet(row, 1);
                            string assetName = SafeGet(row, 2);
                            string name = SafeGet(row, 3);
                            string state = SafeGet(row, 4);
                            bool useMale = true;
                            bool useFemale = true;
                            if (row.Count >= 6)
                                bool.TryParse(SafeGet(row, 5), out useMale);
                            if (row.Count >= 7)
                                bool.TryParse(SafeGet(row, 6), out useFemale);

                            if (string.IsNullOrEmpty(bundle) || string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(state))
                                continue;
                            if (!useFemale)
                                continue;

                            result.Add(new PoseEntry
                            {
                                Id = id,
                                Bundle = bundle,
                                Asset = assetName,
                                Name = string.IsNullOrWhiteSpace(name) ? ("pose_" + id) : name,
                                State = state,
                                UseMale = useMale,
                                UseFemale = useFemale,
                            });
                        }
                    }
                    try { AssetBundleManager.UnloadAssetBundle(file, isUnloadForceRefCount: true); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogWarn("pose list load exception: " + ex.Message);
            }

            return result
                .GroupBy(e => e.Id)
                .Select(g => g.First())
                .OrderBy(e => e.Id)
                .ToList();
        }

        private static string SafeGet(List<string> list, int idx)
        {
            if (list == null || idx < 0 || idx >= list.Count)
                return string.Empty;
            return list[idx] ?? string.Empty;
        }
    }
}
