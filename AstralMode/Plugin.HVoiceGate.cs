using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private const float EntryClimaxVoiceGraceSeconds = 1.25f;

        private float _nextAstralVoiceGateLogTime;
        private float _entryClimaxVoiceGraceUntil;

        private static bool HVoiceCtrl_VoiceProc_Prefix(ref bool __result, int _main)
        {
            Plugin inst = Instance;
            if (inst == null || !inst.ShouldUseAstralVoiceGate())
                return true;

            HFlag flags = _hFlag;
            if (flags == null || flags.voice == null || flags.voice.playVoices == null
                || _main < 0 || _main >= flags.voice.playVoices.Length)
            {
                __result = false;
                return false;
            }

            int value = flags.voice.playVoices[_main];
            if (IsAstralAllowedClimaxVoice(value))
            {
                inst.LogAstralVoiceGate($"allow main={_main} value={value} finish={flags.finish}");
                return true;
            }

            if (value != -1)
            {
                flags.voice.playVoices[_main] = -1;
                inst.LogAstralVoiceGate($"drop main={_main} value={value} finish={flags.finish}");
            }

            __result = false;
            return false;
        }

        private bool ShouldUseAstralVoiceGate()
        {
            if (_cfgSuppressVoiceInAstral == null || !_cfgSuppressVoiceInAstral.Value)
                return false;

            if (_state == AstralState.Astral)
                return true;

            return IsEntryClimaxVoiceGraceActive()
                && (_state == AstralState.Warping
                    || _state == AstralState.WhiteHold
                    || _state == AstralState.FadeIn);
        }

        private void BeginEntryClimaxVoiceGrace(string source)
        {
            _entryClimaxVoiceGraceUntil = Time.unscaledTime + EntryClimaxVoiceGraceSeconds;
            SimpleFileLogger.Info($"AstralVoiceGate: entry climax voice grace start source={source} seconds={EntryClimaxVoiceGraceSeconds:F2}");
        }

        private bool IsEntryClimaxVoiceGraceActive()
        {
            return _entryClimaxVoiceGraceUntil > Time.unscaledTime;
        }

        private static bool IsAstralAllowedClimaxVoice(int playVoiceValue)
        {
            if (playVoiceValue < 0) return false;

            int mode = playVoiceValue / 100;
            int key = playVoiceValue % 100;

            // 3=HSonyu, 8=H3PSonyu. Female initiative variants add +38 to the key.
            if (mode != 3 && mode != 8) return false;

            switch (key)
            {
                case 18: // finish lead-in: orgW/orgS
                case 19: // finish lead-in: sameW/sameS
                case 25: // climax start: orgW/sameW
                case 26: // climax start: orgS/sameS
                case 56: // +38 variant of 18
                case 57: // +38 variant of 19
                case 63: // +38 variant of 25
                case 64: // +38 variant of 26
                    return true;
                default:
                    return false;
            }
        }

        private void LogAstralVoiceGate(string message)
        {
            if (Time.unscaledTime < _nextAstralVoiceGateLogTime) return;
            _nextAstralVoiceGateLogTime = Time.unscaledTime + 0.5f;
            SimpleFileLogger.Info("AstralVoiceGate: " + message);
        }
    }
}
