using Bindings = CanonPlugins.Shared.PluginInput;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace StudioCharaStateLogger
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("CharaStudio")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "canon.studio.charastatelogger";
        public const string PluginName = "StudioCharaStateLogger";
        public const string Version = "1.0.0";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object FileLock = new object();

        private static Plugin _instance;
        private static BepInEx.Logging.ManualLogSource _bepLog;

        private string _pluginDir = string.Empty;
        private string _logPath = string.Empty;

        private ConfigEntry<bool> _enabled;
        private ConfigEntry<KeyboardShortcut> _dumpKey;
        private ConfigEntry<bool> _verboseMaterials;
        private ConfigEntry<bool> _logOnDelta;
        private ConfigEntry<float> _pollInterval;

        private Harmony _harmony;
        private float _nextPollTime;
        private readonly Dictionary<int, CharaLastState> _lastStates = new Dictionary<int, CharaLastState>();

        private sealed class CharaLastState
        {
            public float SkinTuya;
            public float HohoAka;
            public byte Tears;
            public byte[] Siru = new byte[5];
            public float SkinGlossBase;
            public int SweatLevel;  // from Additional Skin Effects mod (KK_SkinEffects)
        }

        private void Awake()
        {
            _instance = this;
            _bepLog = Logger;

            _pluginDir = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            _logPath = Path.Combine(_pluginDir, PluginName + ".log");
            BindConfig();
            if (!_enabled.Value) return;
            InitLogFileIfNeeded();

            WriteLog("=== " + PluginName + " " + Version + " started ===");
            WriteLog("pluginDir=" + _pluginDir);
            WriteLog("logPath=" + _logPath);
            WriteLog("Studio mode: will log on UI changes (skin/siru/cheek/tears) + poll deltas + manual key");

            _harmony = new Harmony(Guid);
            ApplyPatches();

            WriteLog("Harmony patches applied");
        }

        private void BindConfig()
        {
            _enabled = Config.Bind("General", "Enabled", false, "診断ログの有効/無効。有効化してパッチも使う場合はStudioを再起動");
            _dumpKey = Config.Bind("General", "Dump Hotkey", new KeyboardShortcut(KeyCode.F10), "押すと現在シーン中の全キャラの状態をフルダンプ");
            _verboseMaterials = Config.Bind("Log", "Verbose Materials", false, "true=マテリアルの全対象プロパティ(_Sweat/_Wet/_Specular等)を毎回全部吐く");
            _logOnDelta = Config.Bind("Log", "Log On Value Delta", false, "ポーリングで skinTuya / hoho / tears / siru / glossBase に変化を検知したら自動ダンプ");
            _pollInterval = Config.Bind("Log", "Poll Interval Sec", 0.25f, "変化検知ポーリング間隔(秒)。0.1〜1.0推奨");
        }

        private void Update()
        {
            if (!_enabled.Value) return;

            // Manual force dump on hotkey
            if (Bindings.Down("StudioCharaStateLogger.dump", _dumpKey.Value.ToString()))
            {
                WriteLog("=== MANUAL DUMP TRIGGERED (hotkey) ===");
                DumpAllCharas("manual-hotkey");
            }

            // Poll for any external/UI direct changes (catches skin slider etc even if no method patch fired)
            if (_logOnDelta.Value && Time.unscaledTime >= _nextPollTime)
            {
                _nextPollTime = Time.unscaledTime + Mathf.Clamp(_pollInterval.Value, 0.05f, 2f);
                PollAndDumpDeltas();
            }
        }

        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
            WriteLog("destroyed");
        }

        private void ApplyPatches()
        {
            // Patch ChaControl common state writers (these are called from UI and other paths)
            TryPatch(typeof(ChaControl), "SetSiruFlags", nameof(AfterSetSiruFlags));
            TryPatch(typeof(ChaControl), "ChangeHohoAkaRate", nameof(AfterChangeHohoAkaRate));
            TryPatch(typeof(ChaControl), "ChangeSettingSkinGlossPower", nameof(AfterChangeSkinGlossPower));

            // Patch Studio MPCharCtrl UI handlers (the actual sliders/buttons user clicks)
            // Note: nested private class in decomp, use TypeByName
            var stateInfoType = AccessTools.TypeByName("Studio.MPCharCtrl+StateInfo");
            if (stateInfoType != null)
            {
                TryPatchMethod(stateInfoType, "OnValueChangedSkin", nameof(AfterUIValueChanged));
                TryPatchMethod(stateInfoType, "OnValueChangedCheek", nameof(AfterUIValueChanged));
                TryPatchMethod(stateInfoType, "OnValueChangedNipple", nameof(AfterUIValueChanged));
                TryPatchMethod(stateInfoType, "OnClickLiquid", nameof(AfterUIClickLiquid));
                TryPatchMethod(stateInfoType, "OnClickTears", nameof(AfterUIClickTears));
            }
            else
            {
                WriteLog("WARN: Studio.MPCharCtrl+StateInfo type not found (UI patches for sliders may be limited, use hotkey or delta poll)");
            }
        }

        private void TryPatch(Type targetType, string methodName, string postfixName)
        {
            try
            {
                var original = AccessTools.Method(targetType, methodName);
                if (original == null)
                {
                    WriteLog("patch skip: " + targetType.Name + "." + methodName + " not found");
                    return;
                }
                var postfix = AccessTools.Method(typeof(Plugin), postfixName);
                _harmony.Patch(original, postfix: new HarmonyMethod(postfix));
                WriteLog("patched: " + targetType.Name + "." + methodName + " -> " + postfixName);
            }
            catch (Exception ex)
            {
                WriteLog("patch error " + targetType.Name + "." + methodName + ": " + ex.Message);
            }
        }

        private void TryPatchMethod(Type targetType, string methodName, string postfixName)
        {
            try
            {
                var original = AccessTools.Method(targetType, methodName);
                if (original == null)
                {
                    WriteLog("patch skip (nested): " + targetType.Name + "." + methodName + " not found");
                    return;
                }
                var postfix = AccessTools.Method(typeof(Plugin), postfixName);
                _harmony.Patch(original, postfix: new HarmonyMethod(postfix));
                WriteLog("patched (UI): " + targetType.Name + "." + methodName);
            }
            catch (Exception ex)
            {
                WriteLog("patch error (UI) " + targetType.Name + "." + methodName + ": " + ex.Message);
            }
        }

        // === Patch targets ===

        private static void AfterSetSiruFlags(ChaControl __instance, ChaFileDefine.SiruParts parts, byte state)
        {
            if (_instance == null || !_instance._enabled.Value || __instance == null) return;
            _instance.WriteLog($"UI/STATE: SetSiruFlags parts={parts} value={state} cha={__instance.name}");
            _instance.DumpAllCharas("after-SetSiruFlags:" + parts);
        }

        private static void AfterChangeHohoAkaRate(ChaControl __instance, float value)
        {
            if (_instance == null || !_instance._enabled.Value || __instance == null) return;
            _instance.WriteLog($"UI/STATE: ChangeHohoAkaRate value={value:F3} cha={__instance.name}");
            _instance.DumpAllCharas("after-ChangeHohoAkaRate");
        }

        private static void AfterChangeSkinGlossPower(ChaControl __instance)
        {
            if (_instance == null || !_instance._enabled.Value || __instance == null) return;
            float tuya = (__instance.fileStatus != null) ? __instance.fileStatus.skinTuyaRate : -1f;
            _instance.WriteLog($"UI/STATE: ChangeSettingSkinGlossPower skinTuyaRate={tuya:F3} cha={__instance.name}");
            _instance.DumpAllCharas("after-ChangeSkinGlossPower");
        }

        private static void AfterUIValueChanged(object __instance)
        {
            if (_instance == null || !_instance._enabled.Value) return;
            _instance.WriteLog("UI: slider value changed (skin/cheek/nipple)");
            _instance.DumpAllCharas("studio-ui-slider");
        }

        private static void AfterUIClickLiquid(object __instance, ChaFileDefine.SiruParts _parts, byte _state)
        {
            if (_instance == null || !_instance._enabled.Value) return;
            _instance.WriteLog($"UI: liquid (siru) click parts={_parts} state={_state}");
            _instance.DumpAllCharas("studio-ui-liquid:" + _parts);
        }

        private static void AfterUIClickTears(object __instance, byte _state)
        {
            if (_instance == null || !_instance._enabled.Value) return;
            _instance.WriteLog($"UI: tears click state={_state}");
            _instance.DumpAllCharas("studio-ui-tears");
        }

        // === Core dump logic ===

        private void PollAndDumpDeltas()
        {
            try
            {
                var charas = FindObjectsOfType<ChaControl>();
                foreach (var cha in charas)
                {
                    if (cha == null || !cha.gameObject.activeInHierarchy || cha.fileStatus == null) continue;

                    int id = cha.GetInstanceID();
                    if (!_lastStates.TryGetValue(id, out var last))
                    {
                        last = new CharaLastState();
                        _lastStates[id] = last;
                        // initial capture only, no dump spam
                        CaptureLast(cha, last);
                        continue;
                    }

                    bool changed = false;
                    string what = "";

                    float curTuya = cha.fileStatus.skinTuyaRate;
                    if (Mathf.Abs(curTuya - last.SkinTuya) > 0.0001f) { changed = true; what += "skinTuya "; }

                    float curHoho = cha.fileStatus.hohoAkaRate;
                    if (Mathf.Abs(curHoho - last.HohoAka) > 0.0001f) { changed = true; what += "hohoAka "; }

                    byte curTears = cha.fileStatus.tearsLv;
                    if (curTears != last.Tears) { changed = true; what += "tears "; }

                    var curSiru = cha.fileStatus.siruLv;
                    if (curSiru != null && last.Siru != null)
                    {
                        for (int i = 0; i < Math.Min(curSiru.Length, last.Siru.Length); i++)
                            if (curSiru[i] != last.Siru[i]) { changed = true; what += "siru[" + i + "] "; }
                    }

                    float baseGloss = GetBaseSkinGlossPower(cha);
                    if (Mathf.Abs(baseGloss - last.SkinGlossBase) > 0.0001f) { changed = true; what += "baseGloss "; }

                    int curSweat = GetModSweatLevel(cha);
                    if (curSweat != last.SweatLevel) { changed = true; what += "modSweat "; }

                    if (changed)
                    {
                        WriteLog($"DELTA DETECTED: {what.Trim()} cha={cha.name}");
                        DumpCharaState(cha, "poll-delta:" + what.Trim());
                        CaptureLast(cha, last);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog("Poll error: " + ex.Message);
            }
        }

        private void CaptureLast(ChaControl cha, CharaLastState last)
        {
            if (cha?.fileStatus == null) return;
            last.SkinTuya = cha.fileStatus.skinTuyaRate;
            last.HohoAka = cha.fileStatus.hohoAkaRate;
            last.Tears = cha.fileStatus.tearsLv;
            if (cha.fileStatus.siruLv != null)
                last.Siru = (byte[])cha.fileStatus.siruLv.Clone();
            last.SkinGlossBase = GetBaseSkinGlossPower(cha);
            last.SweatLevel = GetModSweatLevel(cha);
        }

        private float GetBaseSkinGlossPower(ChaControl cha)
        {
            try
            {
                // chaFile is usually protected/internal; use reflection
                var prop = AccessTools.Property(typeof(ChaControl), "chaFile");
                var chaFile = prop?.GetValue(cha) as ChaFileControl;
                if (chaFile != null && chaFile.custom != null && chaFile.custom.body != null)
                    return chaFile.custom.body.skinGlossPower;
            }
            catch { }
            return 0f;
        }

        private int GetModSweatLevel(ChaControl cha)
        {
            try
            {
                var type = AccessTools.TypeByName("KK_SkinEffects.SkinEffectsController");
                if (type == null) return -1;
                var comp = cha.GetComponent(type);
                if (comp == null) return -1;
                var prop = AccessTools.Property(type, "SweatLevel");
                if (prop == null) return -1;
                return (int)prop.GetValue(comp);
            }
            catch
            {
                return -1;
            }
        }

        private void DumpAllCharas(string reason)
        {
            try
            {
                var charas = FindObjectsOfType<ChaControl>();
                int count = 0;
                foreach (var cha in charas)
                {
                    if (cha != null && cha.gameObject.activeInHierarchy)
                    {
                        DumpCharaState(cha, reason);
                        count++;
                    }
                }
                if (count == 0)
                    WriteLog("DumpAll: no active ChaControl found (reason=" + reason + ")");
            }
            catch (Exception ex)
            {
                WriteLog("DumpAll error: " + ex.Message);
            }
        }

        private void DumpCharaState(ChaControl cha, string reason)
        {
            if (cha == null || cha.fileStatus == null) return;

            WriteLog("=== CHARA STATE DUMP reason=" + reason + " cha=" + cha.name + " sex=" + cha.sex + " ===");

            // === fileStatus key fields (the "汗/汁/頬/涙" state) ===
            var fs = cha.fileStatus;
            WriteLog("  fileStatus.skinTuyaRate=" + fs.skinTuyaRate.ToString("0.###"));
            WriteLog("  fileStatus.hohoAkaRate=" + fs.hohoAkaRate.ToString("0.###"));
            WriteLog("  fileStatus.tearsLv=" + fs.tearsLv);

            string siruStr = "null";
            if (fs.siruLv != null)
            {
                var parts = Enum.GetValues(typeof(ChaFileDefine.SiruParts));
                var sb = new StringBuilder();
                foreach (ChaFileDefine.SiruParts p in parts)
                {
                    int idx = (int)p;
                    byte val = (idx >= 0 && idx < fs.siruLv.Length) ? fs.siruLv[idx] : (byte)0;
                    sb.Append(p).Append("=").Append(val).Append(" ");
                }
                siruStr = sb.ToString().Trim();
            }
            WriteLog("  fileStatus.siruLv=[" + siruStr + "]");

            // base gloss from custom (the maker "肌のツヤ")
            float baseGloss = GetBaseSkinGlossPower(cha);
            WriteLog("  custom.body.skinGlossPower(base)=" + baseGloss.ToString("0.###"));

            // modded Additional skin effects > Sweat (KK_SkinEffects mod)
            int modSweat = GetModSweatLevel(cha);
            if (modSweat >= 0)
                WriteLog("  KK_SkinEffects.SweatLevel=" + modSweat);

            // clothes quick
            if (fs.clothesState != null)
            {
                WriteLog("  clothesState[0..]=" + string.Join(",", fs.clothesState));
            }

            // === material probe (the actual rendered sweat/wet/gloss) ===
            DumpMaterials(cha, reason);

            WriteLog("=== END DUMP cha=" + cha.name + " ===");
        }

        private void DumpMaterials(ChaControl cha, string phase)
        {
            try
            {
                Renderer[] renderers = cha.GetComponentsInChildren<Renderer>(true);
                int emitted = 0;

                string[] floatProps = new[]
                {
                    "_SpecularPower", "_SpecularPowerNail",
                    "_Glossiness", "_GlossPower", "_Wet", "_wet",
                    "_Sweat", "_sweat",
                    "_liquidface", "_liquidftop", "_liquidfbot", "_liquidbtop", "_liquidbbot",
                    "_overcolor2"
                };

                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;
                    Material[] mats = null;
                    try { mats = renderer.materials; } catch { continue; }
                    if (mats == null) continue;

                    foreach (var mat in mats)
                    {
                        if (mat == null) continue;

                        var propsOut = new List<string>();
                        foreach (var prop in floatProps)
                        {
                            if (!mat.HasProperty(prop)) continue;
                            try
                            {
                                float v = mat.GetFloat(prop);
                                // in non-verbose, only emit if interesting (>0 or specific)
                                if (!_verboseMaterials.Value)
                                {
                                    if (Mathf.Abs(v) < 0.0001f) continue;
                                    if (!prop.Contains("Specular") && !prop.Contains("Sweat") && !prop.Contains("Wet") && !prop.Contains("liquid") && !prop.Contains("overcolor")) continue;
                                }
                                propsOut.Add(prop + "=" + v.ToString("0.###"));
                            }
                            catch { }
                        }

                        if (propsOut.Count == 0) continue;

                        emitted++;
                        WriteLog("  MAT renderer=" + renderer.name + " mat=" + mat.name + " shader=" + (mat.shader != null ? mat.shader.name : "null") + " " + string.Join(" ", propsOut));

                        // always log main tex name for skin/body/face mats to see overlay effects (e.g. wet from mod)
                        if (renderer.name.ToLower().Contains("body") || renderer.name.ToLower().Contains("skin") || renderer.name.ToLower().Contains("face"))
                        {
                            try
                            {
                                if (mat.HasProperty("_MainTex"))
                                {
                                    Texture t = mat.GetTexture("_MainTex");
                                    WriteLog("    SKIN_TEX renderer=" + renderer.name + " _MainTex=" + (t != null ? t.name : "null"));
                                }
                            }
                            catch { }
                        }
                    }
                }

                if (emitted == 0 && _verboseMaterials.Value)
                    WriteLog("  MAT: no interesting float props found on " + renderers.Length + " renderers");
            }
            catch (Exception ex)
            {
                WriteLog("  MAT probe failed: " + ex.Message);
            }
        }

        // === logging helpers (standalone file + bepinex) ===

        private void WriteLog(string message)
        {
            if (_enabled == null || !_enabled.Value) return;
            try
            {
                _bepLog?.LogInfo(message);
            }
            catch { }

            if (string.IsNullOrEmpty(_logPath)) return;

            try
            {
                lock (FileLock)
                {
                    if (!File.Exists(_logPath))
                        File.WriteAllText(_logPath, "=== " + PluginName + " log start " + DateTime.Now + " ===" + Environment.NewLine, Utf8NoBom);
                    File.AppendAllText(_logPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message + Environment.NewLine, Utf8NoBom);
                }
            }
            catch { }
        }

        private void InitLogFileIfNeeded()
        {
            if (string.IsNullOrEmpty(_logPath)) return;
            try
            {
                lock (FileLock)
                {
                    if (!File.Exists(_logPath))
                        File.WriteAllText(_logPath, "=== " + PluginName + " log start " + DateTime.Now + " ===" + Environment.NewLine, Utf8NoBom);
                }
            }
            catch { }
        }
    }
}
