using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay {
 public sealed partial class Plugin {
  private ConfigEntry<bool> _neckTraceRequested;
  private Harmony _neckTraceHarmony;
  private readonly List<string> _neckTraceRows = new List<string>();
  private float _neckTraceDeadline;
  private int _neckTraceSequence;
  private Transform _neckTraceRoot, _neckTraceHead, _neckTraceNeck;
  private static readonly FieldInfo NeckFix = AccessTools.Field(typeof(NeckObjectVer2), "fixAngle");
  private static readonly FieldInfo NeckBackup = AccessTools.Field(typeof(NeckObjectVer2), "fixAngleBackup");
  internal void TraceActorLookBefore() => NeckTrace("before:ActorLook");
  internal void TraceActorLookAfter() => NeckTrace("after:ActorLook");
  private void InitNeckDiagnostics() {
   _neckTraceRequested = Config.Bind("Neck diagnostics", "Capture10Seconds", false, "ONで呼出キャラの首を10秒記録。自動でOFF。キャラ呼出前にもONにできます。");
   _neckTraceRequested.Value = false;
  }
  private void TickNeckDiagnostics() {
   if (_neckTraceRequested == null) return;
   if (_neckTraceDeadline > 0 && (!_neckTraceRequested.Value || !_cfgEnabled.Value || Time.realtimeSinceStartup >= _neckTraceDeadline)) FinishNeckDiagnostics();
   if (_neckTraceDeadline == 0 && _neckTraceRequested.Value) {
    _neckTraceDeadline = Time.realtimeSinceStartup + 10;
    _neckTraceRows.Clear(); _neckTraceSequence = 0;
    _neckTraceHarmony = new Harmony("com.kks.maingame.vmdplay.neckdiagnostic");
    foreach (var method in new MethodBase[] {
     AccessTools.Method(typeof(NeckLookControllerVer2), "LateUpdate"),
     AccessTools.Method(typeof(ChaControl), "ChangeLookNeckPtn"),
     AccessTools.Method(typeof(ChaControl), "LateUpdate"),
     AccessTools.Method(AccessTools.TypeByName("KKVMDPlayPlugin.VMDAnimationController"), "ApplyBoneTransformToOriginal") }) {
     if(method == null) continue;
     var info = Harmony.GetPatchInfo(method);
     _neckTraceRows.Add("PATCH " + method.DeclaringType.FullName + "." + method.Name + " owners=" + (info == null ? "" : string.Join(",", info.Owners)));
     _neckTraceHarmony.Patch(method, new HarmonyMethod(typeof(Plugin), nameof(NeckTraceBefore)), new HarmonyMethod(typeof(Plugin), nameof(NeckTraceAfter)));
    }
   }
   NeckTrace("Update");
  }
  private static bool NeckTraceMatches(object instance) {
   var p=Instance; return p != null && p._trialChara != null &&
    (ReferenceEquals(instance,p._trialChara) || ReferenceEquals(instance,p._trialChara.neckLookCtrl) || (p.IsIndependentTarget && ReferenceEquals(instance,p._controller)));
  }
  private static void NeckTraceBefore(object __instance, MethodBase __originalMethod) {
   if(NeckTraceMatches(__instance)) {
    Instance.NeckTrace("before:"+__originalMethod.Name);
    if(__originalMethod.Name == "ChangeLookNeckPtn" && Instance._neckTraceRows.Count < 24000)
     Instance._neckTraceRows.Add("CALLER " + Environment.StackTrace);
   }
  }
  private static void NeckTraceAfter(object __instance, MethodBase __originalMethod) {
   if(NeckTraceMatches(__instance)) Instance.NeckTrace("after:"+__originalMethod.Name);
  }
  private void NeckTrace(string phase) {
   if(_neckTraceDeadline == 0 || _neckTraceRows.Count >= 24000) return;
   try { CaptureNeckTrace(phase); }
   catch(Exception ex) { if(_neckTraceRows.Count < 24000) _neckTraceRows.Add("CAPTURE_ERROR " + ex.Message); }
  }
  private void CaptureNeckTrace(string phase) {
   var a=_trialChara;
   if(a == null) return;
   var root=a.objBodyBone == null ? null : a.objBodyBone.transform;
   if(root != _neckTraceRoot) {
    _neckTraceRoot=root; _neckTraceHead=null; _neckTraceNeck=null;
    if(root != null) foreach(var t in root.GetComponentsInChildren<Transform>(true)) {
     if(t.name == "cf_j_head") _neckTraceHead=t;
     if(t.name == "cf_j_neck") _neckTraceNeck=t;
    }
   }
   var n=a.neckLookCtrl; var calc=n == null ? null : n.neckLookScript;
   string bones="";
   if(calc != null && calc.aBones != null) foreach(var b in calc.aBones)
    bones += " fix="+NeckFix?.GetValue(b)+" backup="+NeckBackup?.GetValue(b);
   _neckTraceRows.Add($"{Time.frameCount}|{++_neckTraceSequence}|{phase}|actor={a.GetInstanceID()}|generation={_target.Generation}|ready={_actorReady}|vmd={_vmdOwnsCharacter}|face={_actorFaceCamera.Value}|eyes={_actorEyesCamera.Value}|neckEnabled={n?.enabled}|pattern={n?.ptnNo}|status={a.GetLookNeckPtn()}|target={(n?.target == null ? "null" : n.target.name+":"+n.target.GetInstanceID())}|calc={calc?.isEnabled}|skip={calc?.skipCalc}|anim={a.animBody?.enabled}|state={(a.animBody == null ? 0 : a.animBody.GetCurrentAnimatorStateInfo(0).fullPathHash)}|root={root?.rotation.ToString("F4")}|neck={_neckTraceNeck?.localRotation.ToString("F4")}|head={_neckTraceHead?.localRotation.ToString("F4")}"+bones);
  }
  private void FinishNeckDiagnostics() {
   if(_neckTraceDeadline == 0) return;
   _neckTraceDeadline=0; _neckTraceHarmony?.UnpatchSelf(); _neckTraceHarmony=null;
   _neckTraceRequested.Value=false;
   try {
    var path=Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "neck_trace_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".log");
    File.WriteAllLines(path,_neckTraceRows); LogInfo("[NeckDiagnostic] "+path);
   } catch(Exception ex) { LogError("[NeckDiagnostic] "+ex.Message); }
   _neckTraceRows.Clear(); _neckTraceRoot=null; _neckTraceHead=null; _neckTraceNeck=null;
  }
 }
}
