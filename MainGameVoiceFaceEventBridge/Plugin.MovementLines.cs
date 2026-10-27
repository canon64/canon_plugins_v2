using System;
using System.Reflection;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed partial class Plugin
    {
        private static MethodInfo _composerMovementLine;

        private bool ScheduleComposerMovementLines(ExternalVoiceFaceCommand command,
            Func<int, string, Action, float> schedule)
        {
            int count = command.lineTexts == null ? 0 : command.lineTexts.Length;
            if (count == 0 || command.lineSourceIndices == null || command.lineSourceIndices.Length != count
                || command.lineMovementIntensities == null || command.lineMovementIntensities.Length != count
                || command.lineSourceTexts == null || command.lineSourceTexts.Length != count) return false;

            string[] targets = SplitKeywords(_cfgMotionTargetKeywords != null ? _cfgMotionTargetKeywords.Value : "");
            var levels = new string[_cfgMotionLevelKeywords.Length][];
            for (int i = 0; i < levels.Length; i++)
                levels[i] = SplitKeywords(_cfgMotionLevelKeywords[i] != null ? _cfgMotionLevelKeywords[i].Value : "");
            int offset = 0;
            for (int i = 0; i < count; i++)
            {
                int sourceLine = command.lineSourceIndices[i];
                int score = command.lineMovementIntensities[i];
                string session = command.sessionId ?? "";
                int speechLevel = -1;
                foreach (MotionIntensityHit hit in MotionIntensityTextMatcher.FindHits(command.lineSourceTexts[i], targets, levels))
                    speechLevel = hit.Level;
                int capturedLevel = speechLevel;
                if (sourceLine >= 0)
                {
                    Log("[movement] received session=" + session + " sourceLine=" + (sourceLine + 1)
                        + " score=" + score + " speechLevel=" + capturedLevel);
                    schedule(offset, "composer-movement-line", () =>
                    {
                        if (!TryForwardMovementLine(session, sourceLine, score, capturedLevel))
                            LogWarn("[movement] Composer unavailable session=" + session + " sourceLine=" + (sourceLine + 1));
                    });
                }
                offset += (command.lineTexts[i] ?? "").Length + 1;
            }
            return true;
        }

        private static bool TryForwardMovementLine(string session, int sourceLine, int score, int speechLevel)
        {
            try
            {
                if (_composerMovementLine == null)
                {
                    Type type = Type.GetType("MainGameObjectComposer.Plugin, MainGameObjectComposer", false)
                        ?? HarmonyLib.AccessTools.TypeByName("MainGameObjectComposer.Plugin");
                    if (type == null) return false;
                    _composerMovementLine = type.GetMethod("TryApplyMovementLine", BindingFlags.Public | BindingFlags.Static,
                        null, new[] { typeof(string), typeof(int), typeof(int), typeof(int) }, null);
                }
                return _composerMovementLine != null
                    && (bool)_composerMovementLine.Invoke(null, new object[] { session, sourceLine, score, speechLevel });
            }
            catch { return false; }
        }
    }
}
