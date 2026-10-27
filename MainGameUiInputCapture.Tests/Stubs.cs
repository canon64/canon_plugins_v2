// Headless adapters only. The entire production runtime is compiled unchanged.
using System;
namespace UnityEngine {
 public class Coroutine {} public class WaitForEndOfFrame {}
 public class Object { public static object Found; public static T FindObjectOfType<T>() where T:class => Found as T; public static void Destroy(Object o) {} public static void DontDestroyOnLoad(Object o) {} }
 public class GameObject:Object { public GameObject(string n){} public bool activeSelf=true; public Transform transform=new Transform(); public void SetActive(bool v){activeSelf=v;} public T AddComponent<T>() where T:Component,new(){return new T{gameObject=this};} }
 public class Component:Object { public GameObject gameObject; }
 public class Transform:Object { public void SetParent(Transform t,bool b){} }
 public class RectTransform:Transform {public Vector2 anchorMin,anchorMax,offsetMin,offsetMax;}
 public struct Vector2 {public float x,y; public Vector2(float a,float b){x=a;y=b;} public static Vector2 zero=>new Vector2(); public static bool operator ==(Vector2 a,Vector2 b)=>a.x==b.x&&a.y==b.y; public static bool operator !=(Vector2 a,Vector2 b)=>!(a==b); public override bool Equals(object o)=>o is Vector2 v&&v==this; public override int GetHashCode()=>x.GetHashCode()^y.GetHashCode();}
 public struct Vector3 {public float x,y,z; public Vector3(float a,float b,float c=0){x=a;y=b;z=c;}}
 public struct Rect {public float x,y,width,height;public float yMax=>y+height;public Rect(float a,float b,float c,float d){x=a;y=b;width=c;height=d;}public bool Contains(Vector2 p)=>p.x>=x&&p.x<x+width&&p.y>=y&&p.y<yMax;public static bool operator ==(Rect a,Rect b)=>a.x==b.x&&a.y==b.y&&a.width==b.width&&a.height==b.height;public static bool operator !=(Rect a,Rect b)=>!(a==b); public override bool Equals(object o)=>o is Rect r&&r==this; public override int GetHashCode()=>x.GetHashCode();}
 public struct Color {public Color(float a,float b,float c,float d){}}
 public enum CursorLockMode{None,Locked,Confined} public static class Cursor {public static CursorLockMode lockState;public static bool visible;}
 public static class Input {public static bool[] Held=new bool[3],Down=new bool[3];public static Vector3 mousePosition;public static bool GetMouseButton(int i)=>Held[i];public static bool GetMouseButtonDown(int i)=>Down[i];}
 public static class Application {public static bool isFocused=true;}
 public static class Time {public static float unscaledTime;public static int frameCount;}
 public static class Screen {public static int width=1000,height=1000;}
 public enum RenderMode{ScreenSpaceOverlay} public class Canvas:Component {public RenderMode renderMode;public int sortingOrder;}
}
namespace UnityEngine.UI {public class Image:UnityEngine.Component {public UnityEngine.RectTransform rectTransform=new UnityEngine.RectTransform();public UnityEngine.Color color;public bool raycastTarget;}public class GraphicRaycaster:UnityEngine.Component{}}
public class BaseCameraControl_Ver2:UnityEngine.Object {public delegate bool NoCtrlFunc();public NoCtrlFunc NoCtrlCondition,KeyCondition;}
public class CameraControl_Ver2:BaseCameraControl_Ver2 {public bool isCursorLock=true;}
public class HFlag:UnityEngine.Object {public CameraControl_Ver2 ctrlCamera;}
public class GameCursor {public static bool isLock,isDraw;public static int Warps;public void SetCursorLock(bool v){if(!v&&isLock)Warps++;isLock=v;}public void UnLockCursor(){isLock=false;isDraw=true;}}
public static class Singleton<T> where T:new() {public static T Instance=new T();public static bool IsInstance()=>Instance!=null;}
public static class SingletonInitializer<T> where T:new() {public static T instance=new T();}
public class ActionScene {public bool isCursorLock;public void SetCursorLock(bool isLock){isCursorLock=isLock;}}
public static class GlobalMethod {public static void SetCameraMoveFlag(CameraControl_Ver2 c,bool _bPlay){c.NoCtrlCondition=()=>!_bPlay;}}
namespace MainGameUiInputCapture {internal class Plugin {public bool AutoUnstickEnabled=false,BlockDiagnosticsEnabled=false,LogStateOnTransition=false;public bool IsOwnerDebugEnabled(string s)=>false;public void LogAlways(string s){}public void LogWarn(string s){}public void LogDebugRaw(string s){}public void LogDebugForOwner(string a,string b){}}}
namespace MainGameBlankMapAdd.Application {internal interface ICoroutineHost {UnityEngine.Coroutine Start(System.Collections.IEnumerator routine);void Stop(UnityEngine.Coroutine routine);}}
