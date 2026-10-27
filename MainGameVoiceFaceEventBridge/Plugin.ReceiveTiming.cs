using System.IO;
using MainGameVoiceFaceEventBridge.Diagnostics;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed partial class Plugin
    {
        private ReceiveTimingRecorder _receiveTiming;

        private void InitializeReceiveTiming()
        {
            string directory = Path.Combine(PluginDir, "receive-timing");
            string buildId = typeof(Plugin).Module.ModuleVersionId.ToString();
            _receiveTiming = new ReceiveTimingRecorder(batch => ReceiveTimingRecorder.WriteAsync(batch, directory, buildId));
        }

        private void HandleTimedCommand(ExternalVoiceFaceCommand command)
        {
            int index = command?.items != null && command.items.Length > 0 && command.items[0] != null
                ? command.items[0].index : 0;
            using (_receiveTiming.BeginCommand(command?.type, command?.sessionId, index, Time.frameCount,
                _externalVoicePlayer != null && _externalVoicePlayer.IsPlaying))
            {
                try { HandleCommand(command); }
                finally { _receiveTiming.SetVoiceAfter(_externalVoicePlayer != null && _externalVoicePlayer.IsPlaying); }
            }
        }

        private void BeginExternalVoiceGuard(HSceneProc proc, ChaControl female, int main)
        {
            using (_receiveTiming.Measure(ReceiveTimingStage.Guard)) BeginExternalVoiceGuardCore(proc, female, main);
        }

        private void HandleResponseTextCommand(ExternalVoiceFaceCommand command)
        {
            using (_receiveTiming.Measure(ReceiveTimingStage.Text)) HandleResponseTextCommandCore(command);
        }

        private bool IsAstralModeActive()
        {
            using (_receiveTiming.Measure(ReceiveTimingStage.Astral)) return IsAstralModeActiveCore();
        }
    }
}
