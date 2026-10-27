using System;
using HarmonyLib;
namespace MainGameVMDPlay {
 public sealed partial class Plugin {
  private static Type _moreOutfitsControllerType;
  private static System.Reflection.MethodInfo _moreOutfitsGetName;
  private string GetActorCoordinateName(ChaControl cha, int slot) {
   // Additional slot names live in MoreOutfits extended data, not coordinateName.
   // Use the same controller API as VoiceFaceEventBridge.
   if(slot >= Enum.GetValues(typeof(ChaFileDefine.CoordinateType)).Length) {
    if(_moreOutfitsControllerType == null) {
     _moreOutfitsControllerType = AccessTools.TypeByName("KK_Plugins.MoreOutfits.MoreOutfitsController");
     _moreOutfitsGetName = _moreOutfitsControllerType?.GetMethod("GetCoodinateName", new[] { typeof(int) });
    }
    var controller = _moreOutfitsControllerType == null ? null : cha.GetComponent(_moreOutfitsControllerType);
    if(controller != null && _moreOutfitsGetName != null) {
     var savedName = _moreOutfitsGetName.Invoke(controller, new object[] { slot }) as string;
     if(!string.IsNullOrWhiteSpace(savedName)) return savedName;
    }
   }
   var coordinateName = cha.chaFile.coordinate[slot]?.coordinateName;
   return string.IsNullOrWhiteSpace(coordinateName) ? CoordinateLabel(slot) : coordinateName;
  }
  private static Func<ChaControl,int,int,bool> _setPoseEffect;
  private static bool SetPoseEffect(ChaControl cha,int channel,int value) {
   if(_setPoseEffect==null) {
    var t=AccessTools.TypeByName("MainGameHScenePoseInjector.Plugin");
    var m=t?.GetMethod("SetCharacterEffect");
    if(m!=null) _setPoseEffect=(Func<ChaControl,int,int,bool>)Delegate.CreateDelegate(typeof(Func<ChaControl,int,int,bool>),m);
   }
   return _setPoseEffect!=null && _setPoseEffect(cha,channel,value);
  }
  private static string CoordinateLabel(int slot) {
   var name=Enum.GetName(typeof(ChaFileDefine.CoordinateType),slot);
   switch(name) {
    case "Plain": return "私服";
    case "Swim": return "水着";
    case "Bathing": return "入浴";
    case "Pajamas": return "寝巻き";
   }
   return name ?? "名称未設定（"+(slot+1)+"）";
  }
 }
}
