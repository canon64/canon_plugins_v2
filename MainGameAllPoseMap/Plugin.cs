using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ActionGame;
using BepInEx;
using H;
using HarmonyLib;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameAllPoseMap
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        internal const string GUID = "com.kks.maingame.allposemap";
        internal const string PluginName = "MainGameAllPoseMap";
        internal const string Version = "0.1.0";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly FieldInfo HSceneMapField = AccessTools.Field(typeof(HSceneProc), "map");
        private static readonly FieldInfo HSceneUseCategoriesField = AccessTools.Field(typeof(HSceneProc), "useCategorys");
        private static readonly FieldInfo HSceneCloseHPointDataField = AccessTools.Field(typeof(HSceneProc), "closeHpointData");
        private static readonly FieldInfo HSceneLstAnimInfoField = AccessTools.Field(typeof(HSceneProc), "lstAnimInfo");
        private static readonly FieldInfo HSceneLstUseAnimInfoField = AccessTools.Field(typeof(HSceneProc), "lstUseAnimInfo");
        private static readonly FieldInfo HPointTargetsField = AccessTools.Field(typeof(HPointData), "_targets");
        private static readonly FieldInfo HPointGroupsField = AccessTools.Field(typeof(HPointData), "_groups");
        private static readonly FieldInfo HPointOffsetPosField = AccessTools.Field(typeof(HPointData), "_offsetPos");
        private static readonly FieldInfo HPointOffsetAngleField = AccessTools.Field(typeof(HPointData), "_offsetAngle");
        private static readonly FieldInfo HPointExperienceField = AccessTools.Field(typeof(HPointData), "_experience");

        internal static Plugin Instance { get; private set; }

        private readonly object _logLock = new object();
        private readonly Dictionary<int, GameObject> _virtualPointRoots = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, List<HPointData>> _virtualPoints = new Dictionary<int, List<HPointData>>();
        private readonly Dictionary<int, string> _virtualPointSignatures = new Dictionary<int, string>();

        private Harmony _harmony;
        private PluginSettings _settings;
        private string _pluginDir;
        private string _logPath;

        private void Awake()
        {
            Instance = this;
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
            Directory.CreateDirectory(_pluginDir);

            _logPath = Path.Combine(_pluginDir, "MainGameAllPoseMap.log");
            if (_runtimeLoggingEnabled) File.WriteAllText(
                _logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === {PluginName} {Version} start ==={Environment.NewLine}",
                Utf8NoBom);

            _settings = SettingsStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
            _harmony = new Harmony(GUID);
            _harmony.PatchAll(typeof(Plugin));

            LogInfo(
                $"settings loaded enabled={_settings.Enabled} targetMapNo={_settings.TargetMapNo} " +
                $"allPose={_settings.EnableAllPoseInFreeH}");
        }

        private void OnDestroy()
        {
            DestroyAllVirtualPoints();
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch (Exception ex)
            {
                LogWarn($"unpatch failed: {ex.Message}");
            }
            Instance = null;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneProc), "GetCloseCategory")]
        private static void HSceneProc_GetCloseCategory_Postfix(HSceneProc __instance)
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            try
            {
                Instance?.ApplyAllPoseCategoryAccess(__instance);
            }
            catch (Exception ex)
            {
                Instance?.LogError($"GetCloseCategory postfix failed: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneProc), "CreateListAnimationFileName")]
        private static void HSceneProc_CreateListAnimationFileName_Postfix(HSceneProc __instance)
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            try
            {
                Instance?.RebuildUseAnimationListForAllPose(__instance);
            }
            catch (Exception ex)
            {
                Instance?.LogError($"CreateListAnimationFileName postfix failed: {ex}");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(HSceneProc), "LoadSpecialMapStartPosition")]
        private static bool HSceneProc_LoadSpecialMapStartPosition_Prefix(
            HSceneProc __instance,
            ref int _no,
            ref string _objName)
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return true;
            try
            {
                Plugin inst = Instance;
                if (inst == null || !inst.ShouldApplyToCurrentScene(__instance) || !inst._settings.DisableSpecialMapJump)
                    return true;

                _no = -1;
                _objName = inst._settings.VirtualPointAnchorName;
                if (inst._settings.VerboseLog)
                    inst.LogInfo("special map jump blocked on all-pose map");
                return false;
            }
            catch (Exception ex)
            {
                Instance?.LogError($"LoadSpecialMapStartPosition prefix failed: {ex}");
                return true;
            }
        }

        private void ApplyAllPoseCategoryAccess(HSceneProc proc)
        {
            if (!ShouldApplyToCurrentScene(proc))
                return;

            HashSet<int> categories = CollectConfiguredCategories(proc);
            if (categories.Count == 0)
                return;

            var useCategorys = HSceneUseCategoriesField?.GetValue(proc) as List<int>;
            if (useCategorys != null)
            {
                useCategorys.Clear();
                useCategorys.AddRange(categories.OrderBy(c => c));
            }

            if (_settings.EnableVirtualPoints)
            {
                EnsureVirtualPoints(proc, categories.OrderBy(c => c).ToList());
                var closePoints = HSceneCloseHPointDataField?.GetValue(proc) as List<HPointData>;
                if (closePoints != null)
                {
                    if (!_settings.KeepOriginalClosePoints)
                        closePoints.Clear();

                    if (_virtualPoints.TryGetValue(proc.GetInstanceID(), out var points) && points != null)
                    {
                        for (int i = 0; i < points.Count; i++)
                        {
                            HPointData point = points[i];
                            if (point != null && !closePoints.Contains(point))
                                closePoints.Add(point);
                        }
                    }
                }
            }
        }

        private void RebuildUseAnimationListForAllPose(HSceneProc proc)
        {
            if (!ShouldApplyToCurrentScene(proc))
                return;
            if (!_settings.BypassFreeHProgressLocks)
                return;

            var src = HSceneLstAnimInfoField?.GetValue(proc) as List<HSceneProc.AnimationListInfo>[];
            var dst = HSceneLstUseAnimInfoField?.GetValue(proc) as List<HSceneProc.AnimationListInfo>[];
            if (src == null || dst == null)
                return;

            HashSet<int> categories = CollectConfiguredCategories(proc);
            if (categories.Count == 0)
                return;

            for (int mode = 0; mode < src.Length; mode++)
            {
                var list = new List<HSceneProc.AnimationListInfo>();
                List<HSceneProc.AnimationListInfo> srcList = src[mode];
                if (srcList != null)
                {
                    for (int i = 0; i < srcList.Count; i++)
                    {
                        HSceneProc.AnimationListInfo anim = srcList[i];
                        if (anim == null || anim.lstCategory == null || anim.lstCategory.Count == 0)
                            continue;

                        bool include = false;
                        for (int c = 0; c < anim.lstCategory.Count; c++)
                        {
                            if (categories.Contains(anim.lstCategory[c].category))
                            {
                                include = true;
                                break;
                            }
                        }
                        if (include)
                            list.Add(anim);
                    }
                }
                dst[mode] = list;
            }
        }

        private bool ShouldApplyToCurrentScene(HSceneProc proc)
        {
            if (_settings == null || !_settings.Enabled || !_settings.EnableAllPoseInFreeH)
                return false;
            if (proc == null || proc.flags == null || !proc.flags.isFreeH)
                return false;

            ActionMap map = HSceneMapField?.GetValue(proc) as ActionMap;
            if (map == null)
                return false;
            return map.no == _settings.TargetMapNo;
        }

        private HashSet<int> CollectConfiguredCategories(HSceneProc proc)
        {
            HashSet<int> overrideSet = ParseCategoryCsv(_settings.CategoriesOverrideCsv);
            if (overrideSet.Count > 0)
                return overrideSet;

            var result = new HashSet<int>();
            var all = HSceneLstAnimInfoField?.GetValue(proc) as List<HSceneProc.AnimationListInfo>[];
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    List<HSceneProc.AnimationListInfo> modeList = all[i];
                    if (modeList == null)
                        continue;
                    for (int j = 0; j < modeList.Count; j++)
                    {
                        HSceneProc.AnimationListInfo info = modeList[j];
                        if (info == null || info.lstCategory == null)
                            continue;
                        for (int k = 0; k < info.lstCategory.Count; k++)
                            result.Add(info.lstCategory[k].category);
                    }
                }
            }
            return result;
        }

        private static HashSet<int> ParseCategoryCsv(string csv)
        {
            var set = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(csv))
                return set;

            string[] tokens = csv.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (int.TryParse(tokens[i], out int category))
                    set.Add(category);
            }
            return set;
        }

        private void EnsureVirtualPoints(HSceneProc proc, IReadOnlyList<int> categories)
        {
            ActionMap map = HSceneMapField?.GetValue(proc) as ActionMap;
            if (map == null || map.mapRoot == null)
                return;

            int procId = proc.GetInstanceID();
            string anchorName;
            Transform anchor = ResolveAnchorTransform(map, out anchorName);
            if (anchor == null)
                anchor = map.mapRoot.transform;
            if (string.IsNullOrWhiteSpace(anchorName))
                anchorName = _settings.VirtualPointAnchorName;

            string signature = BuildVirtualPointSignature(categories, anchorName, map.no);
            if (_virtualPointSignatures.TryGetValue(procId, out string currentSignature) &&
                string.Equals(currentSignature, signature, StringComparison.Ordinal))
            {
                if (_virtualPointRoots.TryGetValue(procId, out var existingRoot) && existingRoot != null)
                    return;
            }

            DestroyVirtualPoints(procId);

            var root = new GameObject("__AllPoseVirtualPoints");
            root.transform.SetParent(map.mapRoot.transform, worldPositionStays: true);
            _virtualPointRoots[procId] = root;
            _virtualPointSignatures[procId] = signature;

            int perRing = Mathf.Max(1, _settings.VirtualPointsPerRing);
            float baseRadius = Mathf.Max(0.1f, _settings.VirtualPointRadius);
            float ringStep = Mathf.Max(0f, _settings.VirtualPointRingStep);
            float yBase = _settings.VirtualPointHeightOffset;
            float yStep = _settings.VirtualPointVerticalStep;

            var created = new List<HPointData>(categories.Count);
            for (int i = 0; i < categories.Count; i++)
            {
                int ring = i / perRing;
                int slot = i % perRing;
                float angle = 360f * slot / perRing;
                float radians = angle * Mathf.Deg2Rad;
                float radius = baseRadius + ring * ringStep;
                float y = yBase + ring * yStep;

                var go = new GameObject(anchorName);
                go.transform.SetParent(root.transform, worldPositionStays: false);
                go.transform.position = anchor.position + new Vector3(Mathf.Cos(radians) * radius, y, Mathf.Sin(radians) * radius);
                go.transform.rotation = Quaternion.Euler(0f, angle, 0f);

                HPointData point = go.AddComponent<HPointData>();
                point.category = new[] { categories[i] };
                HPointTargetsField?.SetValue(point, Array.Empty<string>());
                HPointGroupsField?.SetValue(point, Array.Empty<string>());
                HPointOffsetPosField?.SetValue(point, Vector3.zero);
                HPointOffsetAngleField?.SetValue(point, Vector3.zero);
                HPointExperienceField?.SetValue(point, 0);
                point.BackUpPosition();
                created.Add(point);
            }

            _virtualPoints[procId] = created;
            if (_settings.VerboseLog)
                LogInfo($"virtual points rebuilt mapNo={map.no} categories={categories.Count} anchor={anchorName}");
        }

        private Transform ResolveAnchorTransform(ActionMap map, out string anchorName)
        {
            anchorName = _settings.VirtualPointAnchorName;
            Transform group = map.mapObjectGroup;
            if (group == null)
                return map.mapRoot != null ? map.mapRoot.transform : null;

            Transform anchor = FindRecursive(group, _settings.VirtualPointAnchorName);
            if (anchor != null)
            {
                anchorName = anchor.name;
                return anchor;
            }

            if (group.childCount > 0)
            {
                Transform fallback = group.GetChild(0);
                anchorName = fallback.name;
                return fallback;
            }

            return map.mapRoot != null ? map.mapRoot.transform : null;
        }

        private static Transform FindRecursive(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;
            if (root.name == name)
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                Transform found = FindRecursive(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static string BuildVirtualPointSignature(IReadOnlyList<int> categories, string anchorName, int mapNo)
        {
            var sb = new StringBuilder();
            sb.Append(mapNo).Append('|').Append(anchorName ?? string.Empty).Append('|');
            for (int i = 0; i < categories.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(categories[i]);
            }
            return sb.ToString();
        }

        private void DestroyVirtualPoints(int procId)
        {
            if (_virtualPointRoots.TryGetValue(procId, out GameObject root))
            {
                if (root != null)
                    Destroy(root);
                _virtualPointRoots.Remove(procId);
            }
            _virtualPoints.Remove(procId);
            _virtualPointSignatures.Remove(procId);
        }

        private void DestroyAllVirtualPoints()
        {
            int[] keys = _virtualPointRoots.Keys.ToArray();
            for (int i = 0; i < keys.Length; i++)
                DestroyVirtualPoints(keys[i]);
        }

        private void LogInfo(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger.LogInfo(message);
            AppendLog("INFO", message);
        }

        private void LogWarn(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger.LogWarning(message);
            AppendLog("WARN", message);
        }

        private void LogError(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger.LogError(message);
            AppendLog("ERROR", message);
        }

        private void AppendLog(string level, string message)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}";
                lock (_logLock)
                {
                    File.AppendAllText(_logPath, line, Utf8NoBom);
                }
            }
            catch
            {
            }
        }
    }
}
