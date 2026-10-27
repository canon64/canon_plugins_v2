using UnityEngine;
using BepInEx.Configuration;
namespace AstralMode {
 public sealed partial class Plugin {
  private ConfigEntry<float> _skyX,_skyY,_skyZ,_skyDiameter,_skyFade,_skyRotX,_skyRotY,_skyRotZ;
  private Vector3 _skyOrigin;
  private Quaternion _skyHeading;
  private int _skyCloneLayer=-1;
  private float _lastSkyFade=-1;
  private ConfigEntry<float> _skyCenterOpacity;
  private float _lastSkyCenterOpacity=-1;
  private Light _skyCloneLight;
  private void SetupSkyDisplay() {
   const string c="76. Sky Display";
   _skyX=Config.Bind(c,"Position X",0f,new ConfigDescription("突入地点からの左右位置。負数は左",new AcceptableValueRange<float>(-500,500)));
   _skyY=Config.Bind(c,"Position Y",40f,new ConfigDescription("突入地点からの高さ。負数は下",new AcceptableValueRange<float>(-500,500)));
   _skyZ=Config.Bind(c,"Position Z",60f,new ConfigDescription("座った男の正面（ベンチ前方）への距離。負数は反対側",new AcceptableValueRange<float>(-500,500)));
   _skyDiameter=Config.Bind(c,"Diameter",240f,new ConfigDescription("円形表示の直径",new AcceptableValueRange<float>(1,1000)));
   _skyFade=Config.Bind(c,"Edge Fade Width",0.25f,new ConfigDescription("円の外周を透明にする幅",new AcceptableValueRange<float>(0.02f,0.95f)));
   _skyCenterOpacity=Config.Bind(c,"Center Opacity",100f,new ConfigDescription("中心の不透明度（％）。100で元の濃さ、50で半透明、0で非表示。外周はこの濃さから透明になります",new AcceptableValueRange<float>(0f,100f)));
   _skyRotX=Config.Bind(c,"Rotation X",0f,new ConfigDescription("突入地点を向く姿勢からの回転",new AcceptableValueRange<float>(-180,180)));
   _skyRotY=Config.Bind(c,"Rotation Y",0f,new ConfigDescription("回転Y",new AcceptableValueRange<float>(-180,180))); _skyRotZ=Config.Bind(c,"Rotation Z",0f,new ConfigDescription("回転Z",new AcceptableValueRange<float>(-180,180)));
   var layoutVersion=Config.Bind(c,"Layout Version",0,"配置設定の移行番号（内部管理）");
   if(layoutVersion.Value<1) {
    _skyX.Value=0; _skyY.Value=40; _skyZ.Value=60; _skyDiameter.Value=240;
    _skyRotX.Value=0; _skyRotY.Value=0; _skyRotZ.Value=0;
    layoutVersion.Value=1; Config.Save();
   }
  }
  private void CaptureSkyDisplayOrigin() {
   _skyOrigin=GetAstralCharaCenter();
   // The bench backrest is on local -Z; seated forward is local +Z.
   Quaternion hill=Quaternion.Euler(_cfgAstralHillRotX.Value,_cfgAstralHillRotY.Value,_cfgAstralHillRotZ.Value);
   Vector3 forward=hill*Quaternion.Euler(_cfgAstralBenchRotX.Value,_cfgAstralBenchRotY.Value,_cfgAstralBenchRotZ.Value)*Vector3.forward;
   forward.y=0; if(forward.sqrMagnitude<0.001f) forward=Vector3.forward;
   _skyHeading=Quaternion.LookRotation(forward.normalized,Vector3.up);
  }
  private void IsolateFrozenDisplay() {
   if(_realWorldSubCamera==null) return;
   int used=0;
   foreach(var renderer in Object.FindObjectsOfType<Renderer>()) used|=1<<renderer.gameObject.layer;
   _skyCloneLayer=-1;
   for(int i=31;i>=8;i--) if((used & (1<<i))==0 && string.IsNullOrEmpty(LayerMask.LayerToName(i))) { _skyCloneLayer=i; break; }
   _realWorldSubCamera.backgroundColor=Color.clear;
   _realWorldSubCamera.clearFlags=CameraClearFlags.SolidColor;
   _realWorldSubCamera.aspect=1f;
   if(_skyCloneLayer<0) { _realWorldSubCamera.cullingMask=0; SimpleFileLogger.Warn("Sky display: no unused layer; showing no actors"); return; }
   foreach(var clone in _frozenClones) if(clone?.Root!=null)
    foreach(var t in clone.Root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer=_skyCloneLayer;
   _realWorldSubCamera.cullingMask=1<<_skyCloneLayer;
   // Frozen actors use an isolated layer and are 100 units below the live room.
   // Illuminate that layer without widening existing room lights' masks.
   if(_skyCloneLight==null) {
    var lightObject=new GameObject("AstralMode_FrozenActorLight");
    lightObject.transform.SetParent(_realWorldSubCamera.transform,false);
    _skyCloneLight=lightObject.AddComponent<Light>();
    _skyCloneLight.type=LightType.Directional;
    _skyCloneLight.color=Color.white;
    _skyCloneLight.intensity=1f;
    _skyCloneLight.shadows=LightShadows.None;
    _skyCloneLight.renderMode=LightRenderMode.ForcePixel;
   }
   _skyCloneLight.cullingMask=1<<_skyCloneLayer;
   _skyCloneLight.enabled=true;
   if (Plugin.DiagnosticsEnabled) FrozenDisplayDiagnostics.Finish(_realWorldSubCamera, _frozenClones);
   SimpleFileLogger.Info($"Frozen actor lighting: layer={_skyCloneLayer}, mask=0x{_skyCloneLight.cullingMask:X8}, intensity={_skyCloneLight.intensity}");
  }
 }
}
