using System;
using UnityEngine;
namespace MainGameBlankMapAdd {
 public sealed partial class Plugin {
  static int checks,failures;
  static void Check(bool value,string name){checks++;Console.WriteLine((value?"PASS ":"FAIL ")+name);if(!value)failures++;}
  static Plugin Fresh(){Input.Held=new bool[3];Input.Down=new bool[3];ApplicationReset();GUIUtility.hotControl=0;GUI.Click=null;GUI.AcquireOnButton=0;GUI.NextScroll=null;GUI.Depth=GUILayout.Depth=0;GUI.Buttons.Clear();Screen.width=1920;Screen.height=1080;return new Plugin();}
  static void ApplicationReset(){UnityEngine.Application.isFocused=true;}
  void Draw(EventType type,Action<float> room=null){Event.current.type=type;GUI.Buttons.Clear();DrawEmbeddedPlaybackUi(new Rect(4,100,1000,506),room??(_=>{}));}
  void SeedDrag(){Draw(EventType.Layout);Input.Held[0]=Input.Down[0]=true;UpdateEmbeddedBarDrag(new Vector2(30,130));Input.Down[0]=false;}
  static void Gestures(){
   var p=Fresh();p.SeedDrag();Check(p._embeddedBarDragging,"press inside begins embedded gesture");
   p.UpdateEmbeddedBarDrag(new Vector2(1800,20));Check(p._embeddedBarDragging,"held drag outside keeps panel alive");
   Check(p.IsPlaybackBarDraggingNow(),"outside embedded gesture reaches fallback capture predicate");
   Input.Held[0]=false;p.UpdateEmbeddedBarDrag(new Vector2(1800,20));Check(!p._embeddedBarDragging,"release outside ends embedded gesture");
   p=Fresh();p.Draw(EventType.Layout);Input.Held[0]=Input.Down[0]=true;p.UpdateEmbeddedBarDrag(new Vector2(1800,20));Input.Down[0]=false;p.UpdateEmbeddedBarDrag(new Vector2(30,130));Check(!p._embeddedBarDragging,"drag originating outside is not adopted on entry");
   foreach(string reason in new[]{"collapse","focus","disable","ctrl-hide"}){
    p=Fresh();p.SeedDrag();p._embeddedHotControl=71;GUIUtility.hotControl=71;
    p._playbackSeekDragging=p._playbackVolumeDragging=p._playbackGainDragging=true;
    if(reason=="collapse"){p._playbackRoomControlsExpanded=false;p.UpdateEmbeddedBarDrag(new Vector2(30,130));}
    if(reason=="focus"){UnityEngine.Application.isFocused=false;p.OnApplicationFocus(false);}
    if(reason=="disable")p.OnDisable();
    if(reason=="ctrl-hide"){p._playbackBarCtrlHidden=true;p.HideUiBlocker();}
    Check(!p._embeddedBarDragging&&p._embeddedBarRect.width==0&&GUIUtility.hotControl==0,reason+" releases owned gesture and hotControl");
    if(reason!="collapse")Check(!p._playbackSeekDragging&&!p._playbackVolumeDragging&&!p._playbackGainDragging,reason+" cancels transport drags");
   }
   p=Fresh();p.SeedDrag();p._embeddedHotControl=71;GUIUtility.hotControl=99;p.HideUiBlocker();Check(GUIUtility.hotControl==99,"hide preserves other window hotControl");
   foreach(bool seek in new[]{true,false}){p=Fresh();if(seek)p._playbackSeekControl=41;else p._playbackSliderHotControl=41;GUIUtility.hotControl=41;p.HideUiBlocker();Check(GUIUtility.hotControl==0,(seek?"seek":"volume")+" owned control released on hide");}
   p=Fresh();p._playbackSeekControl=41;p._playbackSliderHotControl=42;GUIUtility.hotControl=99;p.HideUiBlocker();Check(GUIUtility.hotControl==99,"transport cleanup preserves unrelated control");
   p=Fresh();p._playbackVolumeDragging=p._playbackGainDragging=true;p.HideUiBlocker();p.HideUiBlocker();Check(p.volumeCommits==1&&p.gainCommits==1,"repeated hide commits pending volume/gain once");
   p=Fresh();p._vmdBarMode=true;p._playbackVolumeDragging=true;p.HideUiBlocker();Check(p.volumeCommits==0,"VMD hide does not persist normal-player volume");
   foreach(string slider in new[]{"seek","volume"}){p=Fresh();Input.mousePosition=new Vector3{x=1800,y=1000};p._playbackSeekDragging=slider=="seek";p._playbackVolumeDragging=slider=="volume";Check(p.IsPlaybackBarDraggingNow(),slider+" outside drag remains captured");UnityEngine.Application.isFocused=false;p.UpdateUiCaptureState();Check(!p.IsPlaybackBarDraggingNow()&&!p._uiResources.Idle,slider+" focus loss clears capture and idle ownership");}
  }
  static void Tabs(){
   var p=Fresh();int reads=0,room=0,tab=0;string[] catalog={"camera","Camera","vmd","VMD"};
   p._embeddedReadTabs=()=>{reads++;return catalog;};p._embeddedDrawTab=_=>tab++;
   p.Draw(EventType.Layout,_=>room++);GUI.Click="VMD";p.Draw(EventType.MouseUp,_=>room++);
   Check(p._embeddedSelectedTab==""&&p._embeddedPendingTab=="vmd"&&tab==0,"tab click deferred until Layout");
   p.Draw(EventType.Repaint);Check(reads==1&&p._embeddedSelectedTab=="","Repaint retains current layout tree");
   p.Draw(EventType.Layout);Check(p._embeddedSelectedTab=="vmd"&&tab==1,"next Layout selects registered callback");
   GUI.NextScroll=new Vector2(12,230);p.Draw(EventType.Repaint);GUI.Click="部屋";p.Draw(EventType.MouseUp);p.Draw(EventType.Layout);
   Check(p._embeddedScroll["vmd"].y==230&&p._embeddedScroll[""].y==0,"outer scroll independent per tab");
   p._embeddedPendingTab="vmd";p.Draw(EventType.Layout);Check(p._embeddedScroll["vmd"].y==230,"return to tab retains scroll");
   catalog=new[]{"camera","Camera"};p.Draw(EventType.Layout);Check(p._embeddedSelectedTab=="","unregistered selection falls back to room");
   p=Fresh();p.Draw(EventType.Layout,_=>room++);Check(p._embeddedSelectedTab==""&&GUI.Buttons.Count==1,"missing UnifiedUI leaves room usable");
   p=Fresh();GUI.AcquireOnButton=57;p.Draw(EventType.MouseDown);p.HideUiBlocker();Check(GUIUtility.hotControl==0,"tab-header owned control cleaned on hide");
   p=Fresh();p._embeddedReadTabs=()=>new[]{"fault","Fault"};p._embeddedPendingTab="fault";p._embeddedDrawTab=_=>{GUIUtility.hotControl=81;throw new InvalidOperationException("injected draw failure");};
   try{p.Draw(EventType.Layout);}catch(InvalidOperationException){}
   Check(GUILayout.Depth==0,"throwing tab balances GUILayout scopes");p.HideUiBlocker();Check(GUIUtility.hotControl==0,"throwing tab releases acquired control on hide");
   p=Fresh();try{p.Draw(EventType.Layout,_=>{GUIUtility.hotControl=82;throw new InvalidOperationException("injected room failure");});}catch(InvalidOperationException){}
   Check(GUI.Depth==0,"throwing room balances scroll scope");p.HideUiBlocker();Check(GUIUtility.hotControl==0,"throwing room releases acquired control on hide");
   p=Fresh();GUIUtility.hotControl=99;try{p.Draw(EventType.Layout,_=>{throw new InvalidOperationException("unrelated control active");});}catch(InvalidOperationException){}
   Check(GUIUtility.hotControl==99,"draw failure preserves control already owned by another window");
  }
  static void Layouts(){
   foreach(var size in new[]{(1920,1080),(1080,1920),(1280,720),(800,600)}){
    var p=Fresh();Screen.width=size.Item1;Screen.height=size.Item2;float w=Screen.width-8;
    p._embeddedReadTabs=()=>new[]{"1","Camera","2","Composer","3","HipHijack","4","Pose","5","SubCam","6","ClubLights","7","VMD","8","AutoHVoice"};
    float total=p.GetPlaybackBarHeightPx(),transport=PlaybackTransportLayout.MinimumHeight(w,false),top=total-transport;
    Event.current.type=EventType.Layout;p.DrawEmbeddedPlaybackUi(new Rect(8,Screen.height-total+4,w-8,top-8),_=>{});
    bool bounds=GUI.WindowRect.y>=0&&GUI.WindowRect.y+GUI.WindowRect.height<=Screen.height-transport;
    foreach(Rect r in GUI.Buttons)bounds&=r.x>=0&&r.y>=0&&r.x+r.width<=GUI.WindowRect.width&&r.y+r.height<=GUI.Viewport.y;
    Check(bounds&&GUI.Viewport.height>0&&GUI.Viewport.y+GUI.Viewport.height<=GUI.WindowRect.height,size+" wrapped tabs, viewport and transport do not overlap");
    p._playbackRoomControlsExpanded=false;p.UpdateEmbeddedBarDrag(Vector2.zero);Check(p._embeddedBarRect.width==0&&p.GetPlaybackBarHeightPx()<total,size+" collapse clears upper hit region and reduces height");
   }
  }
  public static int Main(){Gestures();Tabs();Layouts();Console.WriteLine("TOTAL "+checks+" FAILED "+failures);return failures==0?0:1;}
 }
}
