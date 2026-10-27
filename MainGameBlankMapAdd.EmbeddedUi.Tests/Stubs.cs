// Headless boundary adapters: no Unity rendering or real camera is simulated.
#pragma warning disable CS0649 // Unused adapter fields intentionally keep their default values.
using System;
using System.Collections.Generic;
namespace UnityEngine {
 public struct Vector2 {public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} public static Vector2 zero=>default;}
 public struct Vector3 {public float x,y,z;}
 public struct Rect {public float x,y,width,height; public Rect(float x,float y,float w,float h){this.x=x;this.y=y;width=w;height=h;} public static Rect zero=>default;public bool Contains(Vector2 p)=>p.x>=x&&p.y>=y&&p.x<x+width&&p.y<y+height;}
 public struct Color {public Color(float r,float g,float b){}}
 public class RectOffset {public RectOffset(int a,int b,int c,int d){}}
 public static class Mathf {public static float Min(float a,float b)=>Math.Min(a,b);public static float Max(float a,float b)=>Math.Max(a,b);public static float Clamp(float a,float b,float c)=>Math.Clamp(a,b,c);}
 public static class Screen {public static int width=1920,height=1080;}
 public static class Application {public static bool isFocused=true;}
 public static class Time {public static float unscaledTime;}
 public static class Input {public static bool[] Held=new bool[3],Down=new bool[3];public static Vector3 mousePosition;public static bool GetMouseButton(int i)=>Held[i];public static bool GetMouseButtonDown(int i)=>Down[i];}
 public enum EventType {Layout,Repaint,MouseDown,MouseDrag,MouseUp}
 public class Event {public static Event current=new Event();public EventType type;}
 public static class GUIUtility {public static int hotControl;}
 public class GUIContent {public string text; public GUIContent(string t,string tip=null){text=t;}public static GUIContent none=new GUIContent("");}
 public class GUIStyle {public RectOffset padding;public GUIStyle(){}public GUIStyle(GUIStyle other){}public Vector2 CalcSize(GUIContent c)=>new Vector2(c.text.Length*8,20);}
 public class GUISkin {public GUIStyle box=new GUIStyle(),button=new GUIStyle();}
 public static class GUI {
  public static GUISkin skin=new GUISkin();public static Color backgroundColor;
  public static string Click;public static int AcquireOnButton;public static int Depth;public static Rect WindowRect,Viewport;public static Vector2? NextScroll;
  public static List<Rect> Buttons=new List<Rect>();
  public static Rect Window(int id,Rect r,Action<int> draw,GUIContent content,GUIStyle style){WindowRect=r;draw(id);return r;}
  public static bool Button(Rect r,GUIContent c){Buttons.Add(r);if(AcquireOnButton!=0){GUIUtility.hotControl=AcquireOnButton;AcquireOnButton=0;}if(Click!=c.text)return false;Click=null;return true;}
  public static Vector2 BeginScrollView(Rect r,Vector2 p,Rect content){Viewport=r;Depth++;var result=NextScroll??p;NextScroll=null;return result;}
  public static void EndScrollView(){Depth--;}
 }
 public class GUILayoutOption {}
 public static class GUILayout {
  public static int Depth;
  public static void BeginArea(Rect r){GUI.Viewport=r;Depth++;}public static void EndArea(){Depth--;}
  public static Vector2 BeginScrollView(Vector2 p,bool a,bool b,params GUILayoutOption[] o){Depth++;var r=GUI.NextScroll??p;GUI.NextScroll=null;return r;}
  public static void EndScrollView(){Depth--;}public static void BeginVertical(params GUILayoutOption[] o){Depth++;}public static void EndVertical(){Depth--;}
  public static GUILayoutOption Width(float v)=>new GUILayoutOption();public static GUILayoutOption Height(float v)=>new GUILayoutOption();public static GUILayoutOption MinWidth(float v)=>new GUILayoutOption();
 }
}
namespace BepInEx.Bootstrap {public class Info{public object Instance;} public static class Chainloader {public static Dictionary<string,Info> PluginInfos=new Dictionary<string,Info>();}}
namespace MainGameBlankMapAdd.Application {public class CoroutineHost{public CoroutineHost(object p){}}}
namespace MainGameBlankMapAdd.UI {
 public class UiInputCaptureController {
  public int Stops,Updates; public UiInputCaptureController(object host,Func<bool> a,Func<bool>b,Func<bool>c,Action<string>d){}
  public void Stop(string s){Stops++;}public void UpdateCapture(){Updates++;}public void UpdateEditCursorUnlockLoop(){}public void NotifyGizmoDragState(bool b){}
 }
}
namespace MainGameBlankMapAdd {
 public sealed partial class Plugin {
  private class Settings {public float PlaybackBarHeight=100,PlaybackBarMarginX=4;public bool EnablePlaybackBar=true,SyncVoiceSourcesToVideoRoom;}
  private class Resources {public bool UsesSharedInputCapture=true,Idle; public int Hidden;public void HideBlocker(){Hidden++;}public void SyncPlaybackCursor(bool v){Idle=v;}}
  private class Gizmo {public bool IsDragging,IsVisible;}
  private Settings _settings=new Settings();private Resources _uiResources=new Resources();private Gizmo _gizmo;private bool _editMode;
  private object _videoRoomRoot=new object(),_mainVideoPlayer=new object();
  private bool _playbackRoomControlsExpanded=true,_playbackBarCtrlHidden,_playbackBarHiddenByUser;
  private bool _playbackSeekDragging,_playbackVolumeDragging,_playbackGainDragging,_vmdBarMode,_actorCoordinatesOpen;
  private UnityEngine.Rect _actorCoordinatesList,_actorCoordinatesAnchor;
  private int _playbackSeekControl,_playbackSliderHotControl,volumeCommits,gainCommits;
  private void CommitPlaybackBarVolume(){volumeCommits++;}private void CommitPlaybackBarVideoGain(){gainCommits++;}
  private void SyncActiveVoiceSourcesToVideoRoom(bool b){}private void LogInfo(string s){}
  private float GetPlaybackBarMinHeightPx()=>PlaybackTransportLayout.MinimumHeight(UnityEngine.Screen.width-2*_settings.PlaybackBarMarginX,_playbackRoomControlsExpanded);
 }
}
