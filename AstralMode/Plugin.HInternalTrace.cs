using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private readonly Dictionary<string, string> _savedHInternalState = new Dictionary<string, string>();

        private static void PatchHInternalTrace(Harmony harmony)
        {
            TryPatchPluginMethod(harmony, nameof(CaptureRealStateForAstral), postfixName: nameof(CaptureRealStateForAstral_HInternal_Postfix));
            TryPatchPluginMethod(harmony, nameof(RestoreRealStateFromAstral), prefixName: nameof(RestoreRealStateFromAstral_HInternal_Prefix), postfixName: nameof(RestoreRealStateFromAstral_HInternal_Postfix));
            TryPatchPluginMethod(harmony, "RestoreSavedPose", postfixName: nameof(RestoreSavedPose_HInternal_Postfix));
            TryPatchPluginMethod(harmony, "LogCurrentAnimatorMotion", prefixName: nameof(LogCurrentAnimatorMotion_HInternal_Prefix));
            TryPatchPluginMethod(harmony, nameof(ClearSavedRealState), postfixName: nameof(ClearSavedRealState_HInternal_Postfix));
        }

        private static void TryPatchPluginMethod(Harmony harmony, string targetName, string prefixName = null, string postfixName = null)
        {
            try
            {
                MethodInfo target = AccessTools.Method(typeof(Plugin), targetName);
                if (target == null)
                {
                    SimpleFileLogger.Warn($"H_INTERNAL_TRACE patch failed: target not found {targetName}");
                    return;
                }

                HarmonyMethod prefix = null;
                HarmonyMethod postfix = null;

                if (!string.IsNullOrWhiteSpace(prefixName))
                {
                    MethodInfo p = AccessTools.Method(typeof(Plugin), prefixName);
                    if (p == null)
                    {
                        SimpleFileLogger.Warn($"H_INTERNAL_TRACE patch failed: prefix not found {prefixName}");
                        return;
                    }
                    prefix = new HarmonyMethod(p);
                }

                if (!string.IsNullOrWhiteSpace(postfixName))
                {
                    MethodInfo p = AccessTools.Method(typeof(Plugin), postfixName);
                    if (p == null)
                    {
                        SimpleFileLogger.Warn($"H_INTERNAL_TRACE patch failed: postfix not found {postfixName}");
                        return;
                    }
                    postfix = new HarmonyMethod(p);
                }

                harmony.Patch(target, prefix: prefix, postfix: postfix);
                SimpleFileLogger.Info($"Patched H_INTERNAL_TRACE: Plugin.{targetName}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"H_INTERNAL_TRACE patch failed for Plugin.{targetName}: {ex.Message}");
            }
        }

        private static void CaptureRealStateForAstral_HInternal_Postfix(Plugin __instance)
        {
            if (__instance == null) return;
            __instance.CaptureHInternalSnapshot("after-capture");
        }

        private static void RestoreRealStateFromAstral_HInternal_Prefix(Plugin __instance)
        {
            if (__instance == null) return;
            __instance.LogHInternalStateDiff("before-restore");
        }

        private static void RestoreRealStateFromAstral_HInternal_Postfix(Plugin __instance)
        {
            if (__instance == null) return;
            __instance.LogHInternalStateDiff("after-restore");
        }

        private static void RestoreSavedPose_HInternal_Postfix(Plugin __instance)
        {
            if (__instance == null) return;
            __instance.LogHInternalStateDiff("after-RestoreSavedPose");
        }

        private static void LogCurrentAnimatorMotion_HInternal_Prefix(Plugin __instance, string phase)
        {
            if (__instance == null) return;
            __instance.LogHInternalStateDiff("anim-probe:" + (phase ?? "null"));
        }

        private static void ClearSavedRealState_HInternal_Postfix(Plugin __instance)
        {
            if (__instance == null) return;
            __instance._savedHInternalState.Clear();
            SimpleFileLogger.Warn("H_INTERNAL_SNAPSHOT cleared");
        }

        private void CaptureHInternalSnapshot(string phase)
        {
            try
            {
                _savedHInternalState.Clear();

                Dictionary<string, string> map = CaptureHInternalStateMap();
                foreach (var pair in map)
                    _savedHInternalState[pair.Key] = pair.Value;

                SimpleFileLogger.Warn($"H_INTERNAL_SNAPSHOT phase={phase} savedCount={_savedHInternalState.Count}");
                LogHInternalStateSummary("saved:" + phase, map);
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"H_INTERNAL_SNAPSHOT failed phase={phase} error={ex.Message}");
            }
        }

        private Dictionary<string, string> CaptureHInternalStateMap()
        {
            var map = new Dictionary<string, string>();

            CaptureSimpleFields(map, "HFlag", _hFlag);
            CaptureSimpleFields(map, "HSceneProc", _hSceneProc);
            CaptureAnimationInfo(map, "HFlag.nowAnimationInfo", _hFlag != null ? _hFlag.nowAnimationInfo : null);
            CaptureAnimationInfo(map, "HFlag.selectAnimationListInfo", _hFlag != null ? _hFlag.selectAnimationListInfo : null);

            try
            {
                object count = _hFlag != null ? _hFlag.count : null;
                CaptureSimpleFields(map, "HFlag.count", count);
            }
            catch (Exception ex)
            {
                map["HFlag.count.<error>"] = ex.Message;
            }

            return map;
        }

        private static void CaptureAnimationInfo(Dictionary<string, string> map, string key, HSceneProc.AnimationListInfo info)
        {
            if (map == null) return;

            if (info == null)
            {
                map[key] = "null";
                return;
            }

            map[key + ".id"] = info.id.ToString(CultureInfo.InvariantCulture);
            map[key + ".mode"] = info.mode.ToString();
            map[key + ".nameAnimation"] = info.nameAnimation ?? "null";
        }

        private static void CaptureSimpleFields(Dictionary<string, string> map, string ownerName, object obj)
        {
            if (map == null) return;

            if (obj == null)
            {
                map[ownerName + ".<null>"] = "null";
                return;
            }

            try
            {
                FieldInfo[] fields = obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                foreach (FieldInfo f in fields)
                {
                    if (f == null || !IsSimpleLoggableType(f.FieldType)) continue;

                    string key = ownerName + "." + f.Name;
                    try
                    {
                        object value = f.GetValue(obj);
                        map[key] = FormatSimpleValue(value);
                    }
                    catch (Exception ex)
                    {
                        map[key] = "ERROR:" + ex.Message;
                    }
                }
            }
            catch (Exception ex)
            {
                map[ownerName + ".<error>"] = ex.Message;
            }
        }

        private static bool IsSimpleLoggableType(Type t)
        {
            if (t == null) return false;
            if (t.IsEnum) return true;

            return t == typeof(bool)
                || t == typeof(byte)
                || t == typeof(sbyte)
                || t == typeof(short)
                || t == typeof(ushort)
                || t == typeof(int)
                || t == typeof(uint)
                || t == typeof(long)
                || t == typeof(ulong)
                || t == typeof(float)
                || t == typeof(double)
                || t == typeof(string);
        }

        private static string FormatSimpleValue(object value)
        {
            if (value == null) return "null";
            if (value is float f) return f.ToString("0.###", CultureInfo.InvariantCulture);
            if (value is double d) return d.ToString("0.###", CultureInfo.InvariantCulture);
            return value.ToString();
        }

        private void LogHInternalStateDiff(string phase)
        {
            try
            {
                if (_savedHInternalState == null || _savedHInternalState.Count == 0)
                {
                    SimpleFileLogger.Warn($"H_INTERNAL_DIFF phase={phase} skipped=no-saved-map");
                    return;
                }

                Dictionary<string, string> now = CaptureHInternalStateMap();
                int diffCount = 0;
                int logged = 0;
                const int maxLogs = 160;

                foreach (string key in _savedHInternalState.Keys.Union(now.Keys).OrderBy(x => x))
                {
                    string saved = _savedHInternalState.TryGetValue(key, out string sv) ? sv : "<missing>";
                    string current = now.TryGetValue(key, out string cv) ? cv : "<missing>";

                    if (saved == current) continue;

                    diffCount++;

                    if (logged < maxLogs)
                    {
                        logged++;
                        SimpleFileLogger.Warn($"H_INTERNAL_DIFF phase={phase} key={key} saved={saved} current={current}");
                    }
                }

                if (diffCount > logged)
                    SimpleFileLogger.Warn($"H_INTERNAL_DIFF phase={phase} truncated logged={logged} total={diffCount}");

                SimpleFileLogger.Warn($"H_INTERNAL_DIFF_DONE phase={phase} diffCount={diffCount} savedCount={_savedHInternalState.Count} currentCount={now.Count}");
                LogHInternalStateSummary("current:" + phase, now);
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"H_INTERNAL_DIFF failed phase={phase} error={ex}");
            }
        }

        private static void LogHInternalStateSummary(string phase, Dictionary<string, string> map)
        {
            try
            {
                if (map == null)
                {
                    SimpleFileLogger.Warn($"H_INTERNAL_SUMMARY phase={phase} map=null");
                    return;
                }

                string nowId = GetMap(map, "HFlag.nowAnimationInfo.id");
                string nowMode = GetMap(map, "HFlag.nowAnimationInfo.mode");
                string nowName = GetMap(map, "HFlag.nowAnimationInfo.nameAnimation");

                string selId = GetMap(map, "HFlag.selectAnimationListInfo.id");
                string selMode = GetMap(map, "HFlag.selectAnimationListInfo.mode");
                string selName = GetMap(map, "HFlag.selectAnimationListInfo.nameAnimation");

                string gaugeF = GetMap(map, "HFlag.gaugeFemale");
                string gaugeM = GetMap(map, "HFlag.gaugeMale");

                string sonyuSame = GetMap(map, "HFlag.count.sonyuSame");
                string sonyuOrg = GetMap(map, "HFlag.count.sonyuOrg");
                string houshiInside = GetMap(map, "HFlag.count.houshiInside");

                SimpleFileLogger.Warn(
                    $"H_INTERNAL_SUMMARY phase={phase}"
                    + $" now=({nowId},{nowMode},{nowName})"
                    + $" select=({selId},{selMode},{selName})"
                    + $" gaugeF={gaugeF} gaugeM={gaugeM}"
                    + $" count(sonyuSame={sonyuSame},sonyuOrg={sonyuOrg},houshiInside={houshiInside})"
                    + $" mapCount={map.Count}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"H_INTERNAL_SUMMARY failed phase={phase} error={ex.Message}");
            }
        }

        private static string GetMap(Dictionary<string, string> map, string key)
        {
            if (map == null || key == null) return "?";
            return map.TryGetValue(key, out string value) ? value : "?";
        }
    }
}
