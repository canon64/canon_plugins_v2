using System;
using UnityEngine;
namespace MainGameHScenePoseInjector {
 public sealed partial class Plugin {
  private sealed class EffectSuspension {
   internal System.Collections.Generic.Dictionary<int,EffectTarget> Targets;
   internal int? Tears; internal int? Hoho;
   internal AutoCycleSettings Auto;
   internal int Selected; internal bool[] Enabled=new bool[EffectChannelCount]; internal int[] Manual=new int[EffectChannelCount],Index=new int[EffectChannelCount]; internal float[] Remaining=new float[EffectChannelCount];
  }
  public static object SuspendEffectsForAstral() {
   var p=Instance; if(p==null) return null;
   var s=new EffectSuspension {Tears=p._desiredTears,Hoho=p._desiredHohoAka,Auto=p.Auto.SuspensionCopy(),Targets=new System.Collections.Generic.Dictionary<int,EffectTarget>(p._effectTargets),Selected=p._selectedEffectCharacter};
   for(int i=0;i<EffectChannelCount;i++) {
    var ch=p.ChannelSettings(p.Auto,i); s.Enabled[i]=ch.Enabled; s.Manual[i]=ch.ManualValue;
    s.Index[i]=p._effectClocks[i].Index; s.Remaining[i]=Mathf.Max(0,p._effectClocks[i].NextAt-Time.unscaledTime);
    ch.Enabled=false;
   }
   p._desiredHohoAka=null; p._desiredTears=null;
   p._effectTargets.Clear(); p._selectedEffectCharacter=0; return s;
  }
  public static void RestoreEffectsFromAstral(object snapshot) {
   var p=Instance; var s=snapshot as EffectSuspension; if(p==null || s==null) return;
   p._settings.AutoCycle=s.Auto; p._desiredTears=s.Tears; p._desiredHohoAka=s.Hoho;
   p._effectTargets.Clear();
   foreach(var pair in s.Targets) if(pair.Value.Cha!=null) p._effectTargets[pair.Key]=pair.Value;
   p._selectedEffectCharacter=s.Selected;
   for(int i=0;i<EffectChannelCount;i++) {
    var ch=p.ChannelSettings(p.Auto,i); ch.Enabled=s.Enabled[i]; ch.ManualValue=s.Manual[i];
    p._effectClocks[i].Index=s.Index[i]; p._effectClocks[i].NextAt=Time.unscaledTime+s.Remaining[i];
   }
  }
  public static bool SetCharacterEffect(ChaControl cha, int channel, int value) {
   var p=Instance; if(p==null || cha==null || channel<0 || channel>=EffectChannelCount) return false;
   p.RefreshEffectScene();
   value=Mathf.Clamp(value,0,p.ChannelMax(channel));
   p.StateFor(cha).Desired[channel]=value; p.StateFor(cha).ManualOverride[channel]=true;
   if(p.Auto!=null && p._selectedEffectCharacter==cha.GetInstanceID()) { p.ChannelSettings(p.Auto,channel).Enabled=false; p.ChannelSettings(p.Auto,channel).ManualValue=value; p.SaveSettings(); }
   p.WriteEffect(cha,channel,value); return true;
  }
  public static int GetCharacterEffect(ChaControl cha,int channel) => Instance!=null && cha!=null && channel>=0 && channel<EffectChannelCount ? Instance.ReadEffect(cha,channel) : -1;
 }
}
