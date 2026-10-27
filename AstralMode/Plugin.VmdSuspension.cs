using System;
using System.Collections;
using HarmonyLib;
namespace AstralMode {
 public sealed partial class Plugin {
  private void OnDestroy() {
   DiscardVmdAstral(); ResumeMainGameSubCameraFromAstral("plugin-destroy"); DisposeRealWorldSubCamera();
  }
  private long _vmdAstralToken;
  private object _poseEffectsSnapshot;
  private static object CallVmdAstral(string name, params object[] args) {
   var t=AccessTools.TypeByName("MainGameVMDPlay.Plugin");
   var method=t==null?null:AccessTools.Method(t,name);
   return method?.Invoke(null,args);
  }
  private static object CallPoseAstral(string name, params object[] args) {
   var t=AccessTools.TypeByName("MainGameHScenePoseInjector.Plugin");
   return t==null?null:AccessTools.Method(t,name)?.Invoke(null,args);
  }
  private bool SuspendVmdForAstral() {
   try {
    _poseEffectsSnapshot=CallPoseAstral("SuspendEffectsForAstral");
    _vmdAstralToken=(long)(CallVmdAstral("SuspendForAstral") ?? 0L); return true;
   } catch(Exception ex) {
    CallPoseAstral("RestoreEffectsFromAstral",_poseEffectsSnapshot); _poseEffectsSnapshot=null;
    SimpleFileLogger.Warn("Astral entry cancelled: snapshot failed: "+ex); return false;
   }
  }
  private void ReleaseVmdAfterBake() {
   if(_vmdAstralToken==0) return;
   var dancer=CallVmdAstral("GetSuspendedHActor") as ChaControl;
   var speeds=new System.Collections.Generic.Dictionary<ChaControl,float>();
   foreach(var item in _realCharaStates) if(item.Cha!=null && item.Cha!=dancer) speeds[item.Cha]=item.AnimSpeed;
   if ((bool)(CallVmdAstral("ReleaseHForAstral",_vmdAstralToken) ?? false)) {
    CaptureRealStateForAstral();
    foreach(var item in _realCharaStates) if(item.Cha!=null && speeds.TryGetValue(item.Cha,out float speed)) item.AnimSpeed=speed;
   }
  }
  private IEnumerator RestoreVmdForAstral() {
   if(_vmdAstralToken!=0 && CallVmdAstral("RestoreFromAstral",_vmdAstralToken) is IEnumerator work) {
    // Unwind the iterator on error so a failed optional integration cannot strand the fade.
    while(true) {
     bool more=false; object next=null; Exception error=null;
     try {more=work.MoveNext(); if(more) next=work.Current;} catch(Exception ex) {error=ex;}
     if(error!=null) {SimpleFileLogger.Warn("VMD restore failed: "+error); CallVmdAstral("DiscardAstral",_vmdAstralToken); break;}
     if(!more) break; yield return next;
    }
   }
  }
  private void ResumeVmdAfterFade() {
   CallPoseAstral("RestoreEffectsFromAstral",_poseEffectsSnapshot); _poseEffectsSnapshot=null;
   if(_vmdAstralToken!=0) CallVmdAstral("ResumeFromAstral",_vmdAstralToken);
   _vmdAstralToken=0;
  }
  private void DiscardVmdAstral() {
   if(_vmdAstralToken!=0) CallVmdAstral("DiscardAstral",_vmdAstralToken);
   _vmdAstralToken=0;
   CallPoseAstral("RestoreEffectsFromAstral",_poseEffectsSnapshot); _poseEffectsSnapshot=null;
  }
 }
}
