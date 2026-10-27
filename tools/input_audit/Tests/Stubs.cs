using System;
using System.Text.Json;
namespace UnityEngine
{
    public enum KeyCode { None=0, Return=13, Escape=27, A=97, B=98, D=100, G=103, K=107, R=114, S=115, V=118, Y=121, Z=122, Alpha5=53, Semicolon=59, Colon=58, Equals=61, RightBracket=93, KeypadEnter=271, F1=282, F6=287, F7=288, F8=289, F9=290, F10=291, F11=292, F12=293, RightShift=303, LeftShift=304, RightControl=305, LeftControl=306, RightAlt=307, LeftAlt=308, Mouse0=323, Mouse1=324, Mouse2=325, Mouse3=326 }
    public static class Input { public static bool GetKey(KeyCode k)=>false; public static bool GetKeyDown(KeyCode k)=>false; public static bool GetKeyUp(KeyCode k)=>false; public static float GetAxisRaw(string n)=>0; }
    public static class Time { public static int frameCount=10; }
    public static class Mathf { public static float Abs(float x)=>Math.Abs(x); }
    public struct Vector2 { public float x,y; public Vector2(float x,float y){this.x=x;this.y=y;} }
    public static class JsonUtility
    {
        static JsonSerializerOptions Options=new JsonSerializerOptions {IncludeFields=true};
        public static T FromJson<T>(string s)=>JsonSerializer.Deserialize<T>(s,Options);
        public static string ToJson(object o,bool pretty)=>JsonSerializer.Serialize(o,o.GetType(),Options);
    }
}
namespace BepInEx { public static class Paths { public static string PluginPath=""; public static string BepInExRootPath=""; } }
namespace MainGameUiInputCapture
{
    public static partial class InputBindingsApi
    {
        private static int _suppressUntilFrame=-1;
        internal static bool SimulatedEditorActive;
        internal static bool InputSuppressed=>SimulatedEditorActive || UnityEngine.Time.frameCount<=_suppressUntilFrame;
    }
}
