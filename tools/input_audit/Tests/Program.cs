using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MainGameUiInputCapture;
using UnityEngine;
class Program
{
    static int checks;
    static HashSet<KeyCode> held=new HashSet<KeyCode>(), down=new HashSet<KeyCode>(), up=new HashSet<KeyCode>();
    static float wheel;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    static void Advance(){Time.frameCount+=3;held.Clear();down.Clear();up.Clear();wheel=0;}
    static void Main()
    {
        string dir=Path.Combine(Path.GetTempPath(),"canon-input-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try
        {
            BepInEx.Paths.BepInExRootPath=dir; BepInEx.Paths.PluginPath=dir;
            InputBindingsApi.KeyHeld=k=>held.Contains(k); InputBindingsApi.KeyDown=k=>down.Contains(k); InputBindingsApi.KeyUp=k=>up.Contains(k); InputBindingsApi.ReadAxis=n=>wheel;
            InputBindingsApi.Initialize(dir);
            Check(InputBindingsApi.TryParse("ctrl + mouse2","mouse",out var parsed,out _) && parsed[0].Main=="Mouse2","case-insensitive mouse chord");
            Check(InputBindingsApi.TryParse("Mouse0 + Mouse1","mouse",out var mouseChord,out _) && mouseChord[0].Modifiers.Contains("Mouse1"),"mouse button chord supported");
            Check(!InputBindingsApi.TryParse("Wheel +","axis",out _,out _),"reject empty modifier");
            Check(!InputBindingsApi.TryParse("Ctrl","axis",out _,out _),"axis requires an axis");
            Check(InputBindingsApi.TryParse("None","vr",out var empty,out _) && empty.Count==0,"None disables VR");
            Check(InputBindingsApi.TryParse("Not set","key",out var unset,out _) && unset.Count==0,"BepInEx unset value disables binding");
            Check(InputBindingsApi.TryParse("32","vr",out var numericVr,out _) && numericVr[0].Main=="32","numeric VR button preserved");
            down.Add(KeyCode.R);held.Add(KeyCode.LeftControl);
            Check(InputBindingsApi.Down("MainGameBlankMapAdd.reload","R + Ctrl"),"left Ctrl satisfies Ctrl chord");
            held.Clear();held.Add(KeyCode.RightControl);
            Check(InputBindingsApi.Down("MainGameBlankMapAdd.reload","R + Ctrl"),"right Ctrl satisfies Ctrl chord");
            held.Clear();Check(!InputBindingsApi.Down("MainGameBlankMapAdd.reload","R + Ctrl"),"missing modifier does not fire");
            Advance();down.Add(KeyCode.S);held.Add(KeyCode.LeftAlt);Check(!InputBindingsApi.Down("MainGameCoordinateCardSwap.male","S"),"BepInEx exact shortcut rejects extra Alt");
            Advance();down.Add(KeyCode.Alpha5);held.Add(KeyCode.LeftShift);Check(!InputBindingsApi.Down("MainGameVMDPlay.liquid","Alpha5"),"Shift+5 does not also toggle liquid");
            Advance(); Check(InputBindingsApi.Set("MainGameBlankMapAdd.reload","F11 + LeftShift"),"override persists");Advance();
            down.Add(KeyCode.R);held.Add(KeyCode.LeftControl);Check(!InputBindingsApi.Down("MainGameBlankMapAdd.reload","R + Ctrl"),"old key stops firing");
            down.Add(KeyCode.F11);held.Add(KeyCode.LeftShift);Check(InputBindingsApi.Down("MainGameBlankMapAdd.reload","R + Ctrl"),"new key fires");
            Check(!InputBindingsApi.Set("MainGameBlankMapAdd.reload","NoSuchKey"),"invalid key rejected");
            InputBindingsApi.Reload();Advance();down.Add(KeyCode.F11);held.Add(KeyCode.LeftShift);Check(InputBindingsApi.Down("MainGameBlankMapAdd.reload","R + Ctrl"),"reload retains override");
            Check(InputBindingsApi.Set("MainGameBlankMapAdd.reload",null,true),"restore original setting");Advance();down.Add(KeyCode.R);held.Add(KeyCode.LeftControl);Check(InputBindingsApi.Down("MainGameBlankMapAdd.reload","R + Ctrl"),"restored original fires");
            Advance();wheel=.1f;held.Add(KeyCode.RightShift);Check(InputBindingsApi.Axis("MainGameBlankMapAdd.pitch","Wheel + Shift")==.1f,"wheel modifier axis");
            Advance();held.Add(KeyCode.Mouse2);Check(InputBindingsApi.MainHeld("MainGameCameraControl.release","Mouse2 + Ctrl"),"drag stays held after modifier release");Check(!InputBindingsApi.Held("MainGameCameraControl.release","Mouse2 + Ctrl"),"full chord requires modifier");
            Check(InputBindingsApi.AllowsTextSubmit("MainGameSubtitleCore.send","Return | KeypadEnter"),"default IME submit fallback");
            Check(InputBindingsApi.Set("MainGameSubtitleCore.send","F10"),"remap subtitle send");Advance();Check(!InputBindingsApi.AllowsTextSubmit("MainGameSubtitleCore.send","Return | KeypadEnter"),"old IME Enter disabled after remap");
            Check(InputBindingsApi.Set("MainGameSubtitleCore.send","Return + Ctrl"),"modified Enter binding");Advance();Check(!InputBindingsApi.AllowsTextSubmit("MainGameSubtitleCore.send","Return | KeypadEnter"),"IME fallback requires modifier");held.Add(KeyCode.LeftControl);Check(InputBindingsApi.AllowsTextSubmit("MainGameSubtitleCore.send","Return | KeypadEnter"),"IME fallback accepts modifier");
            var vr=new VrInput();vr.Held.Add(VrButton.Trigger);vr.Down.Add(VrButton.Axis0);
            Check(InputBindingsApi.Vr("MainGirlHipHijack.vrQuick",vr,1,"Axis0 + Trigger"),"VR chord down");
            vr.Held.Clear();Check(!InputBindingsApi.Vr("MainGirlHipHijack.vrQuick",vr,1,"Axis0 + Trigger"),"VR chord missing held button");
            Check(InputBindingsApi.Set("MainGirlHipHijack.grab","A"),"VR button override");Advance();vr.Held.Add(VrButton.A);Check(InputBindingsApi.Vr("MainGirlHipHijack.grab",vr,0,"Grip"),"VR new button held");vr.Held.Remove(VrButton.A);vr.Up.Add(VrButton.A);Check(InputBindingsApi.Vr("MainGirlHipHijack.grab",vr,2,"Grip"),"VR release detection");
            Check(InputBindingsApi.Set("MainGirlHipHijack.grab","None"),"disable a VR grab");Advance();Check(InputBindingsApi.Vr("MainGirlHipHijack.grab",vr,2,"Grip"),"disabling a binding releases active grab");
            InputBindingsApi.SimulatedEditorActive=true;down.Add(KeyCode.R);held.Add(KeyCode.LeftControl);Check(!InputBindingsApi.Down("MainGameBlankMapAdd.reload","R + Ctrl"),"editor suppresses action firing");Check(!InputBindingsApi.Vr("MainGirlHipHijack.grab",vr,0,"Grip"),"editor suppresses VR firing");InputBindingsApi.SimulatedEditorActive=false;
            Check(InputBindingsApi.Set("MainGameVMDPlay.liquid","LeftShift"),"liquid shortcut can be rebound to Shift");Advance();held.Add(KeyCode.LeftShift);down.Add(KeyCode.LeftShift);Check(InputBindingsApi.Down("MainGameVMDPlay.liquid","Alpha5"),"explicit Shift liquid binding works");
            Check(InputBindingsApi.Stick("MainGameSubCameraDisplayProbe.fovStick",new Vector2(.2f,.8f))==.8f,"stick Y default");Check(InputBindingsApi.Set("MainGameSubCameraDisplayProbe.fovStick","-StickX"),"stick remapping saves");Advance();Check(InputBindingsApi.Stick("MainGameSubCameraDisplayProbe.fovStick",new Vector2(.2f,.8f))==-.2f,"inverted X stick mapping");
            File.WriteAllText(Path.Combine(dir,"InputBindings.json"),"broken");InputBindingsApi.Reload();Check(InputBindingsApi.Error.Length>0,"corrupt config reported");
            Check(InputBindingsApi.Definitions.Select(x=>x.Id).Distinct().Count()==InputBindingsApi.Definitions.Count,"unique operation ids");
            Console.WriteLine(checks+" checks passed");
        }
        finally{Directory.Delete(dir,true);}
    }
    public enum VrButton {Grip=2,A=7,Axis0=32,Trigger=33}
    public class VrInput
    {
        public HashSet<VrButton> Held=new HashSet<VrButton>(),Down=new HashSet<VrButton>(),Up=new HashSet<VrButton>();
        public bool GetPress(VrButton b)=>Held.Contains(b);public bool GetPressDown(VrButton b)=>Down.Contains(b);public bool GetPressUp(VrButton b)=>Up.Contains(b);
    }
}
