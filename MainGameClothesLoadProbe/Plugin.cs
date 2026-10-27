using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MainGameClothesLoadProbe
{
    [BepInPlugin(Guid, "MainGameClothesLoadProbe", "1.0.0")]
    [BepInProcess("KoikatsuSunshine")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "canon.maingame.clothesloadprobe";
        private static Plugin _instance;
        private Harmony _harmony;
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<float> _traceSeconds;
        private ConfigEntry<float> _methodThresholdMs;
        private ConfigEntry<float> _frameThresholdMs;
        private ConfigEntry<KeyboardShortcut> _manualKey;
        private readonly List<string> _pending = new List<string>(256);
        private string _path;
        private long _traceUntil;
        private long _lastFrame;
        private int _session;
        private int _frame;

        private sealed class Sample
        {
            internal long Start;
            internal int Gc0, Gc1, Gc2;
            internal long Memory;
            internal int Frame, Session;
            internal string Detail;
        }

        private void Awake()
        {
            _instance = this;
            _enabled = Config.Bind("Probe", "Enabled", false, "診断を有効にする。通常はOFF");
            _traceSeconds = Config.Bind("Probe", "TraceSeconds", 15f, "切替後にフレームを記録する秒数");
            _methodThresholdMs = Config.Bind("Probe", "MethodThresholdMs", 0.5f, "メソッド記録の最短時間。0で全件");
            _frameThresholdMs = Config.Bind("Probe", "FrameThresholdMs", 25f, "通常時に記録する長いフレームの閾値");
            _manualKey = Config.Bind("Probe", "ManualTraceKey", new KeyboardShortcut(KeyCode.F10), "15秒の手動診断を開始");
            if (!_enabled.Value) return;
            var directory = Path.Combine(Paths.BepInExRootPath, "clothes-load-probe");
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "trace-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
            File.WriteAllText(_path, "utc,session,frame,event,method,elapsed_ms,gc0,gc1,gc2,memory_delta_bytes,detail\r\n", new UTF8Encoding(false));
            _harmony = new Harmony(Guid);
            var prefix = new HarmonyMethod(typeof(Plugin), nameof(Begin));
            var postfix = new HarmonyMethod(typeof(Plugin), nameof(End));
            var finalizer = new HarmonyMethod(typeof(Plugin), nameof(FinishException));
            string[] names = {
                "ChangeCoordinateTypeAndReload", "ChangeCoordinateType", "SetNowCoordinate",
                "Reload", "ReloadNoAsync", "ChangeClothes", "ChangeClothesNoAsync",
                "ChangeAccessory", "ChangeAccessoryNoAsync", "LoadCharaFbxData", "LoadCharaFbxDataNoAsync",
                "ChangeClothesTopNoAsync", "ChangeClothesBotNoAsync", "ChangeClothesBraNoAsync",
                "ChangeClothesShortsNoAsync", "ChangeClothesGlovesNoAsync",
                "ChangeClothesPanstNoAsync", "ChangeClothesSocksNoAsync",
                "ChangeClothesShoesNoAsync",
                "ChangeCustomClothes", "InitBaseCustomTextureClothes", "LoadAlphaMaskTexture",
                "UpdateClothesStateAll", "CreateBodyTexture", "CreateFaceTexture"
            };
            int patched = 0;
            foreach (var method in typeof(ChaControl).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (Array.IndexOf(names, method.Name) < 0 || method.IsGenericMethod) continue;
                try { _harmony.Patch(method, prefix: prefix, postfix: postfix, finalizer: finalizer); patched++; }
                catch (Exception ex) { Logger.LogWarning("Patch skipped " + method + ": " + ex.Message); }
            }
            // GC.Collect may be called by the game after clothing generation. Its duration
            // is included in ReloadNoAsync; the collection counters identify that case.
            Logger.LogInfo("Clothes probe ready; patches=" + patched + " log=" + _path);
            Queue("start", "probe", 0, 0, 0, 0, 0, "patches=" + patched);
            Flush();
        }

        private static bool IsCoordinateStart(MethodBase method)
        {
            return method.Name == "ChangeCoordinateTypeAndReload";
        }

        private static void Begin(ChaControl __instance, MethodBase __originalMethod, object[] __args, out Sample __state)
        {
            __state = null;
            var p = _instance;
            if (p == null || !p._enabled.Value) return;
            if (IsCoordinateStart(__originalMethod))
            {
                p._session++;
                p._traceUntil = Stopwatch.GetTimestamp() + (long)(Math.Max(1f, p._traceSeconds.Value) * Stopwatch.Frequency);
            }
            if (Stopwatch.GetTimestamp() > p._traceUntil) return;
            string detail = "char=" + (__instance == null ? 0 : __instance.GetInstanceID());
            if (__originalMethod.Name == "ChangeCoordinateTypeAndReload" && __args != null && __args.Length > 0)
                detail += " target=" + __args[0];
            if (__originalMethod.Name == "ChangeClothesNoAsync" && __args != null && __args.Length > 1)
                detail += " kind=" + __args[0] + " id=" + __args[1];
            if (__originalMethod.Name == "ChangeAccessoryNoAsync" && __args != null && __args.Length > 2)
                detail += " slot=" + __args[0] + " type=" + __args[1] + " id=" + __args[2];
            if (__originalMethod.Name == "LoadCharaFbxData" && __args != null && __args.Length > 3)
                detail += " category=" + __args[2] + " id=" + __args[3];
            if (__originalMethod.Name == "LoadCharaFbxDataNoAsync" && __args != null && __args.Length > 4)
                detail += " category=" + __args[3] + " id=" + __args[4];
            __state = new Sample {
                Start = Stopwatch.GetTimestamp(), Frame = p._frame, Session = p._session,
                Gc0 = GC.CollectionCount(0), Gc1 = GC.CollectionCount(1), Gc2 = GC.CollectionCount(2),
                Memory = GC.GetTotalMemory(false), Detail = detail
            };
        }

        private static void End(MethodBase __originalMethod, Sample __state)
        {
            var p = _instance;
            if (p == null || __state == null) return;
            double ms = (Stopwatch.GetTimestamp() - __state.Start) * 1000.0 / Stopwatch.Frequency;
            if (ms < p._methodThresholdMs.Value && !IsCoordinateStart(__originalMethod)) return;
            p.Queue("method", __originalMethod.Name + "(" + __originalMethod.GetParameters().Length + ")", ms,
                GC.CollectionCount(0) - __state.Gc0, GC.CollectionCount(1) - __state.Gc1,
                GC.CollectionCount(2) - __state.Gc2, GC.GetTotalMemory(false) - __state.Memory,
                __state.Detail, __state.Session, __state.Frame);
        }

        private static Exception FinishException(Exception __exception, MethodBase __originalMethod, Sample __state)
        {
            var p = _instance;
            if (__exception != null && p != null)
                p.Queue("exception", __originalMethod.Name, 0, 0, 0, 0, 0, __exception.GetType().Name + ": " + __exception.Message);
            return __exception;
        }

        private void Update()
        {
            if (!_enabled.Value) return;
            _frame = Time.frameCount;
            long now = Stopwatch.GetTimestamp();
            if (_lastFrame != 0 && _enabled.Value)
            {
                double ms = (now - _lastFrame) * 1000.0 / Stopwatch.Frequency;
                if (now <= _traceUntil || ms >= _frameThresholdMs.Value)
                    Queue("frame", "Update-to-Update", ms, 0, 0, 0, 0,
                        now <= _traceUntil ? "trace" : "long-frame");
            }
            _lastFrame = now;
            if (_enabled.Value && Bindings.Down("MainGameClothesLoadProbe.trace", _manualKey.Value.ToString()))
            {
                _session++;
                _traceUntil = now + (long)(Math.Max(1f, _traceSeconds.Value) * Stopwatch.Frequency);
                Queue("manual", "F10", 0, 0, 0, 0, 0, "trace-start");
            }
            // Disk writes during the measured window would create their own frame spikes.
            if (now > _traceUntil && _pending.Count > 0 && (_frame % 30 == 0 || _pending.Count > 5000)) Flush();
        }

        private void Queue(string eventName, string method, double ms, int gc0, int gc1, int gc2,
            long memoryDelta, string detail, int session = -1, int frame = -1)
        {
            if (_pending.Count >= 10000) return;
            _pending.Add(string.Join(",", new[] {
                DateTime.UtcNow.ToString("O"), (session < 0 ? _session : session).ToString(),
                (frame < 0 ? _frame : frame).ToString(), eventName, method,
                ms.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
                gc0.ToString(), gc1.ToString(), gc2.ToString(), memoryDelta.ToString(),
                "\"" + (detail ?? "").Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\""
            }));
        }

        private void Flush()
        {
            if (_pending.Count == 0) return;
            try { File.AppendAllLines(_path, _pending, new UTF8Encoding(false)); _pending.Clear(); }
            catch (Exception ex) { Logger.LogWarning("Trace write failed: " + ex.Message); _pending.Clear(); }
        }

        private void OnDestroy()
        {
            Flush();
            _harmony?.UnpatchSelf();
            if (_instance == this) _instance = null;
        }
    }
}
