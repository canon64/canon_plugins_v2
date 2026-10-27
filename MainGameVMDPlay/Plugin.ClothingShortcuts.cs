using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
namespace MainGameVMDPlay {
 public sealed partial class Plugin {
  private ConfigEntry<int> _clothingTarget;
  private Harmony _clothingHarmony;
  private float _clothingRetryAt;
  private static readonly string[] ClothesKeys={"Top","Bottom","Bra","Pants","Gloves","Pantyhose","Socks","Shoes","Wear","Naked","Shift","Coordinate","CoordinateB"};
  private readonly FieldInfo[] _clothesFields=new FieldInfo[13];
  private static readonly FieldInfo HFemales=AccessTools.Field(typeof(HSceneProc),"lstFemale");
  private void InitClothingShortcuts() {
   _clothingTarget=Config.Bind("Clothing shortcuts","Target",1,new ConfigDescription("0=呼び出しキャラ 1=メインキャラ 2=両方",new AcceptableValueRange<int>(0,2)));
   EnsureClothingShortcuts();
  }
  private void EnsureClothingShortcuts() {
   if(_clothingHarmony!=null || UnityEngine.Time.unscaledTime < _clothingRetryAt) return;
   _clothingRetryAt=UnityEngine.Time.unscaledTime+3;
   if(!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("KKS_GirlsClothesChange",out var info) || info.Instance==null) return;
   var t=info.Instance.GetType();
   var update=t==null?null:AccessTools.Method(t,"Update");
   if(update==null) return;
   for(int i=0;i<13;i++) { _clothesFields[i]=AccessTools.Field(t,ClothesKeys[i]); if(_clothesFields[i]==null) { LogWarn("Clothing shortcut contract mismatch"); return; } }
   _clothingHarmony=new Harmony(Guid+".clothing");
   _clothingHarmony.Patch(update,prefix:new HarmonyMethod(typeof(Plugin),nameof(ClothesUpdatePrefix)));
   Logger.LogInfo("[Clothing] GirlsClothesChange target routing installed");
  }
  public static int GetClothingTarget() => Instance?._clothingTarget?.Value ?? 1;
  public static void SetClothingTarget(int value) { if(Instance?._clothingTarget!=null) Instance._clothingTarget.Value=Mathf.Clamp(value,0,2); }
  private static bool ClothesUpdatePrefix(object __instance) {
   var p=Instance;
   if(p==null || !p._insideHScene || p._hSceneProc==null || !p._cfgEnabled.Value) return true;
   if(GUIUtility.keyboardControl!=0) return false;
   try {
    for(int i=0;i<13;i++) {
     var key=p._clothesFields[i].GetValue(__instance) as ConfigEntry<KeyboardShortcut>;
     if(key==null || !Bindings.Down("MainGameVMDPlay.clothes." + ClothesKeys[i], key.Value.ToString())) continue;
     var actor=p.ReadyIndependentActor();
     var targets=ClothingTargets.Capture(p._clothingTarget.Value,HFemales.GetValue(p._hSceneProc) as List<ChaControl>,p.AstralBlocked?null:actor);
     foreach(var cha in targets) {
      if(cha==null) continue;
      if(i<7) cha.SetClothesStateNext(i);
      else if(i==7) cha.SetClothesState(8,(byte)(cha.fileStatus.clothesState[8]==0?1:0));
      else if(i==8) cha.SetClothesStateAll(0);
      else if(i==9) cha.SetClothesStateAll(3);
      else if(i==10) { cha.SetClothesStateAll(1); cha.SetClothesState(6,0); }
      else {
       int count=cha.chaFile.coordinate.Length; if(count==0) continue;
       int slot=(cha.fileStatus.coordinateType+(i==11?-1:1)+count)%count;
       if(ReferenceEquals(cha,actor)) p.ApplyActorCoordinate(null,slot,cha);
       else cha.ChangeCoordinateTypeAndReload((ChaFileDefine.CoordinateType)slot);
      }
     }
    }
   } catch(Exception ex) { p.LogError("衣装ショートカット: "+ex.Message); }
   return false;
  }
 }
}

