using System;
using MainGameUiInputCapture;
using UnityEngine;
class Program {
 sealed class Host:MainGameBlankMapAdd.Application.ICoroutineHost {public Coroutine Start(System.Collections.IEnumerator routine)=>new Coroutine();public void Stop(Coroutine routine){}}
 static void TestFallback(){
  New(out var c);c.NoCtrlCondition=()=>true;c.KeyCondition=()=>false;
  var f=new MainGameBlankMapAdd.UI.UiInputCaptureController(new Host(),()=>false,()=>false,()=>false,_=>{});
  f.NotifyGizmoDragState(true);f.Stop("test");Check(c.NoCtrlCondition()&&!c.KeyCondition(),"fallback preserves existing menu restrictions");
  New(out c);f=new MainGameBlankMapAdd.UI.UiInputCaptureController(new Host(),()=>false,()=>false,()=>false,_=>{});f.NotifyGizmoDragState(true);BaseCameraControl_Ver2.NoCtrlFunc restriction=()=>true;c.NoCtrlCondition=restriction;f.Stop("test");Check(ReferenceEquals(c.NoCtrlCondition,restriction),"fallback preserves new restriction");
  New(out c);f=new MainGameBlankMapAdd.UI.UiInputCaptureController(new Host(),()=>false,()=>false,()=>false,_=>{});var scene=SingletonInitializer<ActionScene>.instance;scene.isCursorLock=true;f.NotifyGizmoDragState(true);var nextScene=new ActionScene();SingletonInitializer<ActionScene>.instance=nextScene;var next=new CameraControl_Ver2{NoCtrlCondition=()=>true,KeyCondition=()=>false};((HFlag)UnityEngine.Object.Found).ctrlCamera=next;f.Stop("test");Check(!c.NoCtrlCondition()&&next.NoCtrlCondition()&&scene.isCursorLock&&!nextScene.isCursorLock,"fallback releases saved instances without writing into replacement");
  New(out c);f=new MainGameBlankMapAdd.UI.UiInputCaptureController(new Host(),()=>false,()=>false,()=>false,_=>{});f.NotifyGizmoDragState(true);var old=c;next=new CameraControl_Ver2{NoCtrlCondition=()=>true,KeyCondition=()=>false};((HFlag)UnityEngine.Object.Found).ctrlCamera=next;f.UpdateCapture();Check(!old.NoCtrlCondition(),"fallback active replacement restores old camera");f.Stop("test");Check(next.NoCtrlCondition()&&!next.KeyCondition(),"fallback replacement preserves own restrictions");
  New(out c);f=new MainGameBlankMapAdd.UI.UiInputCaptureController(new Host(),()=>false,()=>false,()=>false,_=>{});f.NotifyGizmoDragState(true);Application.isFocused=false;f.UpdateCapture();Check(!c.NoCtrlCondition()&&!GameCursor.isLock&&GameCursor.Warps==0,"fallback focus loss releases without warp");
  New(out c);c.NoCtrlCondition=()=>true;f=new MainGameBlankMapAdd.UI.UiInputCaptureController(new Host(),()=>false,()=>false,()=>false,_=>{});f.UpdateCapture();Check(c.NoCtrlCondition(),"fallback does not rescue unrelated menu restriction");
 }
 static int checks;
 static void Check(bool b,string name){if(!b)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
 static UiInputCaptureRuntime New(out CameraControl_Ver2 camera){
  Input.Held=new bool[3];Input.Down=new bool[3];Input.mousePosition=new Vector3(900,900);
  Application.isFocused=true;Time.unscaledTime=0;Time.frameCount=0;GameCursor.isLock=false;GameCursor.Warps=0;Cursor.lockState=CursorLockMode.None;
  SingletonInitializer<ActionScene>.instance=new ActionScene();
  camera=new CameraControl_Ver2{NoCtrlCondition=()=>false,KeyCondition=()=>true};UnityEngine.Object.Found=new HFlag{ctrlCamera=camera};return new UiInputCaptureRuntime(new Plugin());
 }
 static void Main(){
  var r=New(out var c);GameCursor.isLock=true;Input.Held[1]=true;
  r.Sync("camera","bone-look-height-drag",true);Check(!GameCursor.isLock&&GameCursor.Warps==0,"capture never warps old position");
  Input.Held[1]=false;r.Sync("camera","bone-look-height-drag",false);
  Check(Cursor.lockState==CursorLockMode.None&&!GameCursor.isLock,"release does not replay native/Unity lock");Check(c.isCursorLock&&!c.NoCtrlCondition(),"camera permissions restored without locking pointer");
  r=New(out c);r.Begin("A","drag");r.Begin("B","drag");r.End("A","drag");Check(c.NoCtrlCondition()&&r.BlockCameraPointerInput,"overlap ending A leaves B captured");r.End("B","drag");Check(!c.NoCtrlCondition(),"last owner restores");
  r=New(out c);Input.mousePosition=new Vector3(20,980);r.SetInputBlockRects("bar",new[]{new Rect(0,0,100,100)});r.FrameUpdate();r.Sync("other","window",false);Check(c.NoCtrlCondition(),"unrelated inactive owner cannot release UI hover");
  Input.Held[0]=true;Input.Down[0]=true;r.FrameUpdate();Input.Down[0]=false;Input.mousePosition=new Vector3(900,900);r.FrameUpdate();Check(r.IsMouseInputBlocked()&&c.NoCtrlCondition(),"UI drag remains owned outside rectangle");Input.Held[0]=false;r.FrameUpdate();Check(!r.IsMouseInputBlocked()&&!c.NoCtrlCondition(),"release outside frees input");
  r=New(out c);r.Begin("gizmo","drag");Time.unscaledTime=2;r.FrameUpdate();Check(!r.IsAnyActive&&!c.NoCtrlCondition(),"expired owner recovers");
  r=New(out c);r.Begin("camera","drag");Application.isFocused=false;r.FrameUpdate();Check(!r.IsAnyActive&&!GameCursor.isLock&&Cursor.lockState==CursorLockMode.None,"focus loss releases both lock mechanisms");
  r=New(out c);r.Begin("camera","drag");var old=c;var next=new CameraControl_Ver2{NoCtrlCondition=()=>true,KeyCondition=()=>false};((HFlag)UnityEngine.Object.Found).ctrlCamera=next;r.End("camera","drag");Check(!old.NoCtrlCondition()&&next.NoCtrlCondition(),"release never writes old state onto replacement camera");
  r=New(out c);c.NoCtrlCondition=()=>true;c.KeyCondition=()=>false;r.Begin("UI","drag");r.End("UI","drag");Check(c.NoCtrlCondition()&&!c.KeyCondition(),"pre-existing game-menu restriction is preserved");
  r=New(out c);r.Begin("UI","drag");BaseCameraControl_Ver2.NoCtrlFunc menu=()=>true;c.NoCtrlCondition=menu;r.End("UI","drag");Check(ReferenceEquals(c.NoCtrlCondition,menu),"restriction installed during capture is preserved");
  r=New(out c);Input.Held[1]=true;r.Begin("camera","bone-look-height-drag");r.End("camera","bone-look-height-drag");Check(!GameCursor.isLock&&Cursor.lockState==CursorLockMode.None,"Shift released before right button never replays lock");
  r=New(out c);r.Begin("camera","drag");old=c;next=new CameraControl_Ver2{NoCtrlCondition=()=>false,KeyCondition=()=>true};((HFlag)UnityEngine.Object.Found).ctrlCamera=next;r.Tick("camera","drag");Check(!old.NoCtrlCondition()&&next.NoCtrlCondition(),"camera replacement during active capture restores old instance");r.End("camera","drag");Check(!next.NoCtrlCondition(),"replacement restores its own saved state");
  r=New(out c);var oldScene=SingletonInitializer<ActionScene>.instance;oldScene.isCursorLock=true;r.Begin("UI","drag");var nextScene=new ActionScene();SingletonInitializer<ActionScene>.instance=nextScene;r.End("UI","drag");Check(oldScene.isCursorLock&&!nextScene.isCursorLock,"release cannot transfer previous scene lock to new scene");
  r=New(out c);oldScene=SingletonInitializer<ActionScene>.instance;oldScene.isCursorLock=true;r.Begin("UI","drag");nextScene=new ActionScene();SingletonInitializer<ActionScene>.instance=nextScene;r.Tick("UI","drag");Check(oldScene.isCursorLock&&!nextScene.isCursorLock,"active capture restores departing scene only");r.End("UI","drag");Check(!nextScene.isCursorLock,"replacement scene restores its own lock preference");
  New(out c);r=new UiInputCaptureRuntime(new Plugin{AutoUnstickEnabled=true});r.Begin("UI","drag");var sentinel=c.NoCtrlCondition;r.End("UI","drag");c.NoCtrlCondition=sentinel;BaseCameraControl_Ver2.NoCtrlFunc keyMenu=()=>false;c.KeyCondition=keyMenu;r.FrameUpdate();Time.unscaledTime=4;Time.frameCount++;r.FrameUpdate();Check(!c.NoCtrlCondition()&&ReferenceEquals(c.KeyCondition,keyMenu),"automatic orphan recovery preserves unrelated keyboard restriction");
  TestFallback();Console.WriteLine("TOTAL "+checks);
 }
}
