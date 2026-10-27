using System;
using HarmonyLib;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        private void InstallVoiceHook()
        {
            try
            {
                var target = AccessTools.Method(typeof(HVoiceCtrl), "IsBreathPtnConditions");
                if (target == null)
                {
                    Logger.LogWarning("Strong voice unavailable: native breath condition method missing");
                    return;
                }
                _voiceHarmony = new Harmony(Guid);
                _voiceHarmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(Plugin), nameof(VoiceConditionPrefix)),
                    finalizer: new HarmonyMethod(typeof(Plugin), nameof(VoiceConditionFinalizer)));
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Strong voice hook failed: " + ex);
            }
        }

        private static void VoiceConditionPrefix(HVoiceCtrl __instance, out bool __state)
        {
            __state = false;
            var plugin = Instance;
            if (plugin == null || !plugin._aibuStrongVoice || plugin._scene == null ||
                plugin._scene.voice != __instance || plugin._scene.flags == null ||
                plugin._scene.flags.voice == null || plugin._scene.flags.mode != HFlag.EMode.aibu ||
                plugin._scene.flags.voice.speedMotion)
                return;
            plugin._scene.flags.voice.speedMotion = true;
            __state = true;
            if (!plugin._voiceHookObserved)
            {
                plugin._voiceHookObserved = true;
                plugin.LogDiagnostic("VR Vibe native strong voice condition applied");
            }
        }

        private static Exception VoiceConditionFinalizer(Exception __exception, bool __state)
        {
            var plugin = Instance;
            if (__state && plugin != null && plugin._scene != null && plugin._scene.flags != null &&
                plugin._scene.flags.voice != null)
                plugin._scene.flags.voice.speedMotion = false;
            return __exception;
        }
    }
}
