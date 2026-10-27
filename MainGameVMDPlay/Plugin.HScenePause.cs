using System.Reflection;
using HarmonyLib;

namespace MainGameVMDPlay {
 public sealed partial class Plugin {
  private bool PauseMainH => !IsIndependentTarget && _vmdOwnsCharacter && !FaceOnlyRequested && !_endingPose;
  private static MethodInfo _abortImpactCue;
  private void InitMainHPause() {
   _poseHarmony.Patch(AccessTools.Method(typeof(HSceneProc), "Update"), prefix:new HarmonyMethod(typeof(Plugin),nameof(BeforeMainHUpdate)));
   _poseHarmony.Patch(AccessTools.Method(typeof(HSceneProc), "LateUpdate"), prefix:new HarmonyMethod(typeof(Plugin),nameof(BeforeMainHLateUpdate)));
   var cue = AccessTools.TypeByName("MainGameVoiceFaceEventBridge.ExternalVoicePlayer");
   if(cue != null) {
    _abortImpactCue=AccessTools.Method(cue,"AbortImpactCue");
    var request=AccessTools.Method(cue,"RequestImpactCue");
    var tick=AccessTools.Method(cue,"TickImpactCue");
    if(request != null) _poseHarmony.Patch(request,prefix:new HarmonyMethod(typeof(Plugin),nameof(BeforeMainImpactRequest)));
    if(tick != null && _abortImpactCue != null) _poseHarmony.Patch(tick,prefix:new HarmonyMethod(typeof(Plugin),nameof(BeforeMainImpactTick)));
   }
  }
  private static bool BeforeMainHUpdate(HSceneProc __instance) {
   var p=Instance;
   if(p == null || !p.PauseMainH || p._hSceneProc != __instance) return true;
   var flags=__instance.flags;
   // Release before the original routine processes exit, movement, or a requested pose.
   if(flags.isHSceneEnd || flags.selectAnimationListInfo != null || flags.click == HFlag.ClickKind.pointmove) {
    p.ReleaseCharacter(flags.isHSceneEnd ? VmdEndReason.SceneExit : VmdEndReason.PoseChange);
    return true;
   }
   flags.click=HFlag.ClickKind.none;
   return false;
  }
  private static bool BeforeMainHLateUpdate(HSceneProc __instance) => Instance == null || !Instance.PauseMainH || Instance._hSceneProc != __instance;
  private static bool BeforeMainImpactRequest(ref bool __result) {
   if(Instance == null || !Instance.PauseMainH) return true;
   __result=false; return false;
  }
  private static bool BeforeMainImpactTick(object __instance) {
   if(Instance == null || !Instance.PauseMainH) return true;
   _abortImpactCue.Invoke(__instance,null); return false;
  }
 }
}
