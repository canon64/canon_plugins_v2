using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace MainGameVMDPlay {
 public sealed partial class Plugin {
  private sealed class AstralSnapshot : PlaybackSuspension {
   internal HSceneProc Scene;
   internal ChaControl Actor; internal bool ActorActive,Owns,Independent,Reload,ControllerEnabled;
   internal VMDPlaySettings Settings; internal string PresetPath,PresetName,ListName; internal bool CameraRequested;
   internal bool EyesCamera,FaceCamera,PlacementSaved; internal Vector3 SavedPosition,SavedRotation;
   internal string Path,PendingCard,PendingPreset; internal PluginState OriginalState; internal GameObject Body;
   internal FaceState[] Faces; internal Transform EyesTarget,NeckTarget;
   internal Behaviour Controller; internal ChaFileStatus Status; internal byte[] Coordinate; internal Version CoordinateVersion;
   internal byte[] Clothes,Siru; internal bool[] Accessories; internal byte Tears;
   internal Transform[] Bones; internal Vector3[] Positions,Scales; internal Quaternion[] Rotations;
   internal Vector3 StagePosition; internal Quaternion StageRotation;
   internal Dictionary<string,int> Skin=new Dictionary<string,int>();
  }
  private AstralSnapshot _astral;
  private long _astralSerial;
  private bool _astralInternal;
  private string _lastActorCardRequest;
  private bool AstralBlocked => _astral!=null && !_astralInternal;
  public static ChaControl GetSuspendedHActor() => Instance?._astral!=null && !Instance._astral.Independent ? Instance._trialChara : null;
  public static bool IsAstralSuspended() => Instance?._astral!=null;
  public static long SuspendForAstral() {
   var p=Instance; if(p==null) return 0;
   try {return p.CaptureAstral();} catch {p.AbortAstral(); throw;}
  }
  public static bool ReleaseHForAstral(long token) {
   var p=Instance; var s=p?._astral; if(s==null || s.Token!=token) return false;
   if(!s.Independent && s.Owns) { p._astralInternal=true; try { p.ReleaseCharacter(); s.Reload=true; return true; } finally { p._astralInternal=false; } }
   return false;
  }
  public static IEnumerator RestoreFromAstral(long token) { return Instance==null ? EmptyAstral() : Instance.RestoreAstral(token); }
  private static IEnumerator EmptyAstral() { yield break; }
  public static void ResumeFromAstral(long token) {
   var p=Instance; var s=p?._astral; if(s==null || s.Token!=token) return;
   if(!s.Commit(token,p._target.SceneGeneration,p._target.Generation)) {p.FailAstral("対象が変わったため再開を取り消しました"); return;}
   p._astral=null;
   p._actorCameraRequested=s.CameraRequested; p.TickActorCamera();
   if(s.WasPlaying && p._vmdOwnsCharacter) p.PlaybackPlayAll();
   else if(s.Owns && p._vmdOwnsCharacter) p.SetState(PluginState.Paused);
   else p.SetState(s.OriginalState);
  }
  public static void DiscardAstral(long token) { var p=Instance; if(p?._astral!=null && p._astral.Token==token) p.AbortAstral(); }
  private void AbortAstral() {
   var s=_astral; _astral=null; s?.Discard();
   if(s?.Controller!=null) s.Controller.enabled=s.ControllerEnabled;
   if(s?.Actor!=null) s.Actor.gameObject.SetActive(s.ActorActive);
   if(s!=null) ReleaseCharacter();
  }
  private long CaptureAstral() {
   if (!_available || !_cfgEnabled.Value || !_insideHScene) return 0;
   if(_astral!=null) return _astral.Token;
   var s=new AstralSnapshot {Token=++_astralSerial,Scene=_hSceneProc,SceneGeneration=_target.SceneGeneration,TargetGeneration=_target.Generation,
    OriginalState=_state,Actor=_actorReady?_actorSession?.Actor:null,Owns=_vmdOwnsCharacter,Independent=IsIndependentTarget,
    WasPlaying=_state==PluginState.Playing || _loadRoutine!=null || _queuedActorPreset!=null,Reload=_loadRoutine!=null || _musicLoading,
    Path=_selectedVmdForPicker ?? _settings.LastVmdAbsPath,
    PlacementSaved=_actorPlacementSaved.Value,SavedPosition=_actorSavedPosition.Value,SavedRotation=_actorSavedRotation.Value,Settings=_settings.SuspensionCopy(),PresetPath=_activePresetPath,PresetName=_presetName,ListName=_runningMyListName,CameraRequested=_actorCameraRequested,EyesCamera=_actorEyesCamera.Value,FaceCamera=_actorFaceCamera.Value,
    PendingCard=_actorRoutine!=null?_lastActorCardRequest:null,PendingPreset=_queuedActorPreset};
   s.CapturePlaylist(_myListQueue,_myListIndex,_myListRunning);
   s.Time=_seekRequested||_seekDragging ? _seekDragValue : TryMediaTime(out float cursor)?cursor:_currentAnimTime;
   if(s.Actor!=null) s.ActorActive=s.Actor.gameObject.activeSelf;
   _astral=s; _controlTransition.Invalidate(); _target.Generation++; s.TargetGeneration=_target.Generation;
   _modelReloadAt=-1; _modelResumeTime=-1; _seekRequested=false; _seekDragging=false;
   VmdBridgeSoundPause(); _cameraSequence?.PauseSequences();
   _videoClockHeld=false; _mediaClockFrame=-1;
   if(_controller!=null) VmdBridgePause(_controller);
   if(s.Reload || _actorRoutine!=null) {
    _astralInternal=true; try {
     ReleaseCharacter();
     if(_actorRoutine!=null) DisposeIndependentActor();
    } finally { _astralInternal=false; }
    s.TargetGeneration=_target.Generation;
   }
   s.Controller=_controller as Behaviour;
   if(s.Controller!=null) {s.ControllerEnabled=s.Controller.enabled; s.Controller.enabled=false;}
   if(s.Actor!=null) {
    var cha=s.Actor; s.Body=cha.objBodyBone; s.ActorActive=cha.gameObject.activeSelf;
    s.Status=new ChaFileStatus(); s.Status.Copy(cha.fileStatus);
    foreach(var property in typeof(ChaFileStatus).GetProperties()) if(property.CanRead && property.CanWrite && property.PropertyType.IsArray && property.GetValue(s.Status,null) is Array array) property.SetValue(s.Status,array.Clone(),null);
    s.Faces=new[]{new FaceState(cha.eyebrowCtrl),new FaceState(cha.eyesCtrl),new FaceState(cha.mouthCtrl)};
    s.EyesTarget=cha.eyeLookCtrl?.target; s.NeckTarget=cha.neckLookCtrl?.target;
    s.Clothes=(byte[])cha.fileStatus.clothesState.Clone(); s.Siru=(byte[])cha.fileStatus.siruLv.Clone(); s.Accessories=(bool[])cha.fileStatus.showAccessory.Clone(); s.Tears=cha.tearsLv;
    s.Coordinate=cha.nowCoordinate.SaveBytes(); s.CoordinateVersion=cha.nowCoordinate.loadVersion;
    s.Bones=cha.GetComponentsInChildren<Transform>(true); s.Positions=new Vector3[s.Bones.Length]; s.Rotations=new Quaternion[s.Bones.Length]; s.Scales=new Vector3[s.Bones.Length];
    for(int i=0;i<s.Bones.Length;i++) {s.Positions[i]=s.Bones[i].localPosition; s.Rotations[i]=s.Bones[i].localRotation; s.Scales[i]=s.Bones[i].localScale;}
    if(_actorStage!=null) {s.StagePosition=_actorStage.transform.position; s.StageRotation=_actorStage.transform.rotation;}
    ResolveActorSkin(); var skin=_actorSkinType==null?null:cha.GetComponent(_actorSkinType);
    if(skin!=null) for(int i=0;i<ActorModProperties.Length;i++) if(_actorSkinProperties[i]?.CanRead==true) s.Skin[ActorModProperties[i]]=Convert.ToInt32(_actorSkinProperties[i].GetValue(skin,null));
    cha.gameObject.SetActive(false);
   }
   SetState(PluginState.Paused); _presetMessage="アストラル中：現実のVMDを凍結中";
   return s.Token;
  }
  private IEnumerator RestoreAstral(long token) {
   var s=_astral;
   if(s==null || s.Token!=token) yield break;
   if(s.Scene==null || !ReferenceEquals(s.Scene,_hSceneProc) || !s.Matches(token,_target.SceneGeneration,_target.Generation) || (s.Actor!=null && (!ReferenceEquals(s.Actor,_actorSession?.Actor) || s.Body!=s.Actor.objBodyBone)) || !_cfgEnabled.Value) {AbortAstral(); yield break;}
   _settings=s.Settings; _activePresetPath=s.PresetPath; _presetName=s.PresetName; _runningMyListName=s.ListName;
   _actorPlacementSaved.Value=s.PlacementSaved; _actorSavedPosition.Value=s.SavedPosition; _actorSavedRotation.Value=s.SavedRotation;
   _actorCameraRequested=s.CameraRequested; _actorEyesCamera.Value=s.EyesCamera; _actorFaceCamera.Value=s.FaceCamera;
   Exception failure=null;
   try {
    if(s.Actor!=null) {
     var cha=s.Actor;
     cha.fileStatus.Copy(s.Status);
     cha.fileStatus.clothesState=(byte[])s.Clothes.Clone(); cha.fileStatus.siruLv=(byte[])s.Siru.Clone(); cha.fileStatus.showAccessory=(bool[])s.Accessories.Clone();
     cha.nowCoordinate.LoadBytes(s.Coordinate,s.CoordinateVersion);
     if(_actorStage!=null) {_actorStage.transform.position=s.StagePosition; _actorStage.transform.rotation=s.StageRotation;}
     for(int i=0;i<s.Bones.Length;i++) if(s.Bones[i]!=null) {s.Bones[i].localPosition=s.Positions[i]; s.Bones[i].localRotation=s.Rotations[i]; s.Bones[i].localScale=s.Scales[i];}
     cha.gameObject.SetActive(s.ActorActive);
     cha.ChangeHohoAkaRate(s.Status.hohoAkaRate); cha.tearsLv=s.Tears;
     cha.ChangeEyebrowPtn(s.Status.eyebrowPtn,false); cha.ChangeEyesPtn(s.Status.eyesPtn,false); cha.ChangeMouthPtn(s.Status.mouthPtn,false);
     cha.ChangeEyesBlinkFlag(s.Status.eyesBlink); cha.ChangeMouthFixed(s.Status.mouthFixed);
     cha.ChangeLookEyesPtn(s.Status.eyesLookPtn); cha.ChangeLookNeckPtn(s.Status.neckLookPtn);
     if(cha.eyeLookCtrl!=null) cha.eyeLookCtrl.target=s.EyesTarget;
     if(cha.neckLookCtrl!=null) cha.neckLookCtrl.target=s.NeckTarget;
     foreach(var face in s.Faces) face.Restore();
     for(int i=0;i<s.Clothes.Length;i++) cha.SetClothesState(i,s.Clothes[i]);
     for(int i=0;i<s.Accessories.Length;i++) cha.SetAccessoryState(i,s.Accessories[i]);
     for(int i=0;i<s.Siru.Length;i++) cha.SetSiruFlags((ChaFileDefine.SiruParts)i,s.Siru[i]);
     ActorUpdateSiru.Invoke(cha,new object[]{true}); ActorUpdateVisible.Invoke(cha,null);
     var skin=_actorSkinType==null?null:cha.GetComponent(_actorSkinType);
     if(skin!=null) foreach(var pair in s.Skin) _actorSkinType.GetProperty(pair.Key)?.SetValue(skin,pair.Value,null);
    }
    if(s.Controller!=null) s.Controller.enabled=s.ControllerEnabled;
   } catch(Exception ex) {failure=ex;}
   if(failure!=null) {FailAstral(failure.Message); yield break;}
   if(s.PendingCard!=null) {
    _astralInternal=true; try {RequestActorCard(s.PendingCard);} finally {_astralInternal=false;}
    float until=Time.realtimeSinceStartup+180;
    while(_astral==s && _actorRoutine!=null && Time.realtimeSinceStartup<until) yield return null;
    if(_astral!=s) yield break;
    if(!_actorReady) {FailAstral("呼出キャラの読込が完了しませんでした"); yield break;}
    // The same explicit request is resumed, never a new/default character.
    s.TargetGeneration=_target.Generation;
    _actorCameraRequested=s.CameraRequested;
   }
   if(s.Reload && s.Owns || s.PendingPreset!=null) {
    _astralInternal=true;
    try {
     if(s.PendingPreset!=null) ApplyPreset(s.PendingPreset); else PlaybackLoadAndPlay(s.Path,false);
    } finally {_astralInternal=false;}
    float until=Time.realtimeSinceStartup+180;
    while(_astral==s && (_loadRoutine!=null || _musicLoading) && Time.realtimeSinceStartup<until) yield return null;
    if(_astral!=s) yield break;
    if(!_vmdOwnsCharacter || _loadRoutine!=null || _musicLoading) {FailAstral("VMDの復元読込に失敗しました"); yield break;}
   }
   if(_astral!=s) yield break;
   _myListQueue=s.Queue; _myListIndex=s.Index; _myListRunning=s.ListRunning;
   if(s.Owns || s.PendingPreset!=null) {
    SetState(PluginState.Paused); _videoDeadline=Time.realtimeSinceStartup+10; PlaybackSeekTo(s.Time);
    float until=Time.realtimeSinceStartup+15;
    while(_videoSeeks.Pending && Time.realtimeSinceStartup<until) {TickVideoPlayback(); yield return null;}
    if(_videoSeeks.Pending || !string.IsNullOrEmpty(_musicError)) {FailAstral("動画の復元シークが完了しませんでした"); yield break;}
    VmdBridgeSoundPause();
   }
   s.TargetGeneration=_target.Generation;
   s.MarkRestored(token,_target.SceneGeneration,_target.Generation); _currentAnimTime=s.Time; _presetMessage="現実の復元完了：帰還待ち"; InvalidateBarCatalog();
  }
  private void FailAstral(string reason) {AbortAstral(); _presetMessage="アストラル復元失敗: "+reason; LogError(_presetMessage);}
 }
}
