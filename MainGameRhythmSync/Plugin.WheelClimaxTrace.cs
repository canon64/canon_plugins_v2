using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace MainGameRhythmSync
{
    // Records the exact H-scene state around a downward speed-wheel event.
    // It does not change gauge, speed, input, or finish decisions.
    [HarmonyPatch]
    internal static class WheelClimaxTrace
    {
        private static readonly FieldInfo FlagsField = AccessTools.Field(typeof(HActionBase), "flags");
        private static readonly FieldInfo SpriteField = AccessTools.Field(typeof(HActionBase), "sprite");
        private static readonly FieldInfo IsAutoField = AccessTools.Field(typeof(HSonyu), "isAuto");
        private static readonly ConditionalWeakTable<HSonyu, TraceState> States = new ConditionalWeakTable<HSonyu, TraceState>();

        private sealed class TraceState
        {
            internal int FramesLeft;
            internal HFlag.FinishKind PreviousFinish;
            internal string Before;
        }

        [HarmonyPatch(typeof(HSonyu), "LoopProc", new[] { typeof(bool) })]
        [HarmonyPrefix]
        private static void BeforeLoop(HSonyu __instance)
        {
            if (Plugin.Instance == null || !Plugin.Instance.LoggingEnabled || FlagsField == null || SpriteField == null || IsAutoField == null) return;
            var flags = FlagsField.GetValue(__instance) as HFlag;
            var sprite = SpriteField.GetValue(__instance) as HSprite;
            if (flags == null || sprite == null) return;
            var state = States.GetOrCreateValue(__instance);
            bool isAuto = (bool)IsAutoField.GetValue(__instance);
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            bool onPad = sprite.IsCursorOnPad();
            if (wheel < 0f && flags.gaugeFemale >= 65f && flags.gaugeMale >= 65f)
                state.FramesLeft = 6;
            if (state.FramesLeft <= 0) return;
            state.PreviousFinish = flags.finish;
            state.Before = Describe("before", flags, isAuto, onPad, wheel, sprite.IsSonyuAutoFinish());
        }

        [HarmonyPatch(typeof(HSonyu), "LoopProc", new[] { typeof(bool) })]
        [HarmonyPostfix]
        private static void AfterLoop(HSonyu __instance)
        {
            if (Plugin.Instance == null || !Plugin.Instance.LoggingEnabled) return;
            if (!States.TryGetValue(__instance, out var state) || state.FramesLeft <= 0) return;
            var flags = FlagsField.GetValue(__instance) as HFlag;
            if (flags != null)
            {
                bool isAuto = (bool)IsAutoField.GetValue(__instance);
                Plugin.Instance?.WriteWheelTrace(state.Before + " | " +
                    Describe("after", flags, isAuto, false, 0f, false) +
                    " finishChanged=" + (flags.finish != state.PreviousFinish));
            }
            state.FramesLeft--;
        }

        private static string Describe(string label, HFlag flags, bool isAuto, bool onPad, float wheel, bool autoFinish)
        {
            return label + " frame=" + Time.frameCount + " mode=" + flags.mode +
                " wheel=" + wheel.ToString("0.###") + " isAuto=" + isAuto +
                " onPad=" + onPad + " autoFinish=" + autoFinish +
                " gaugeF=" + flags.gaugeFemale.ToString("0.###") +
                " gaugeM=" + flags.gaugeMale.ToString("0.###") +
                " speedCalc=" + flags.speedCalc.ToString("0.###") +
                " click=" + flags.click + " finish=" + flags.finish;
        }
    }

    public sealed partial class Plugin
    {
        internal void WriteWheelTrace(string line)
        {
            if (!LoggingEnabled) return;
            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Info.Location), "WheelClimaxTrace.log");
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss.fff") + " " + line + Environment.NewLine);
            }
            catch (Exception ex) { Logger.LogWarning("wheel climax trace failed: " + ex.Message); }
        }
    }
}
