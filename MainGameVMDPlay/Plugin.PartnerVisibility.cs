using System;
using System.Collections.Generic;
using HarmonyLib;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private readonly Dictionary<ChaControl, bool> _hiddenPartners = new Dictionary<ChaControl, bool>();
        private static readonly System.Reflection.MethodInfo PartnerUpdateVisible = AccessTools.Method(typeof(ChaControl), "UpdateVisible");

        private void HideDancePartners()
        {
            if (IsIndependentTarget || FaceOnlyRequested || _hSceneProc == null) return;
            foreach (string name in new[] { "male", "male1" }) {
                var partner = AccessTools.Field(typeof(HSceneProc), name)?.GetValue(_hSceneProc) as ChaControl;
                if (partner == null || partner == _trialChara || _hiddenPartners.ContainsKey(partner)) continue;
                _hiddenPartners.Add(partner, partner.visibleAll);
                partner.visibleAll = false;
                PartnerUpdateVisible.Invoke(partner, null);
            }
        }

        private static void BeforePartnerVisibility(ChaControl __instance)
        {
            var p = Instance;
            if (p != null && p._hiddenPartners.ContainsKey(__instance)) __instance.visibleAll = false;
        }

        private void RestoreDancePartners()
        {
            if (_hiddenPartners.Count == 0) return;
            var saved = new List<KeyValuePair<ChaControl, bool>>(_hiddenPartners);
            _hiddenPartners.Clear(); // Remove the override before applying the original visibility.
            foreach (var entry in saved) {
                if (entry.Key == null) continue;
                try {
                    entry.Key.visibleAll = entry.Value;
                    PartnerUpdateVisible.Invoke(entry.Key, null);
                } catch (Exception ex) { LogError("[Release] partner visibility: " + ex); }
            }
        }
    }
}
