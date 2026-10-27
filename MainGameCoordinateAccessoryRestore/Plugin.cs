using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MainGameCoordinateAccessoryRestore
{
    [BepInPlugin(Guid, nameof(MainGameCoordinateAccessoryRestore), "1.0.0")]
    [BepInProcess("KoikatsuSunshine")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "canon.maingame.coordinateaccessoryrestore";
        private static Plugin _instance;
        private Harmony _harmony;
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<float> _restoreSeconds;
        private const float RestoreRetryIntervalSeconds = 0.1f;
        private struct PendingRestore
        {
            internal float Until;
            internal float NextApply;
        }

        private readonly Dictionary<ChaControl, PendingRestore> _pending = new Dictionary<ChaControl, PendingRestore>();
        private readonly List<ChaControl> _snapshot = new List<ChaControl>();

        private void Awake()
        {
            _instance = this;
            _enabled = Config.Bind("General", "Enabled", true, "衣装変更後に全アクセサリーを表示する");
            _restoreSeconds = Config.Bind("General", "RestoreSeconds", 1f,
                new ConfigDescription("衣装変更後の再適用期間（秒）。遅れて読み込まれるアクセサリーにも適用する。",
                    new AcceptableValueRange<float>(0.1f, 5f)));
            _harmony = new Harmony(Guid);
            var postfix = new HarmonyMethod(typeof(Plugin), nameof(CoordinateChanged)) { priority = Priority.Last };
            _harmony.Patch(AccessTools.Method(typeof(ChaControl), "ChangeCoordinateType",
                new[] { typeof(ChaFileDefine.CoordinateType), typeof(bool) }), postfix: postfix);
            _harmony.Patch(AccessTools.Method(typeof(ChaControl), "SetNowCoordinate",
                new[] { typeof(ChaFileCoordinate) }), postfix: postfix);
            Logger.LogInfo("Ready: restore all accessory visibility after coordinate changes (1.0.0).");
        }

        private static void CoordinateChanged(ChaControl __instance, bool __result)
        {
            var plugin = _instance;
            if (!__result || plugin == null || !plugin._enabled.Value || __instance == null) return;
            // Defer until LateUpdate: the caller still needs to Reload the new coordinate.
            float now = Time.unscaledTime;
            plugin._pending[__instance] = new PendingRestore
            {
                Until = now + plugin._restoreSeconds.Value,
                NextApply = now
            };
        }

        private void LateUpdate()
        {
            if (!_enabled.Value) { _pending.Clear(); return; }
            if (_pending.Count == 0) return;
            _snapshot.Clear();
            _snapshot.AddRange(_pending.Keys);
            float now = Time.unscaledTime;
            foreach (var cha in _snapshot)
            {
                if (cha == null) { _pending.Remove(cha); continue; }
                PendingRestore pending = _pending[cha];
                if (now < pending.NextApply && now < pending.Until) continue;
                try
                {
                    // Use the game API so MoreAccessories' installed patch handles extra slots.
                    cha.SetAccessoryStateAll(true);
                    if (now >= pending.Until)
                    {
                        _pending.Remove(cha);
                    }
                    else
                    {
                        pending.NextApply = now + RestoreRetryIntervalSeconds;
                        _pending[cha] = pending;
                    }
                }
                catch (Exception ex)
                {
                    _pending.Remove(cha);
                    Logger.LogError("Accessory restore failed: " + ex);
                }
            }
            _snapshot.Clear();
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _pending.Clear();
            if (_instance == this) _instance = null;
        }
    }
}
