using System;
using System.Collections.Generic;
using SimpleAfterimage;
class Program {
 static int count;
 static void Check(bool ok,string label) { count++; if(!ok) throw new Exception(label); }
 static bool Near(float a,float b)=>Math.Abs(a-b)<0.00001;
 static BpmAfterimageSettings Settings()=>new BpmAfterimageSettings { Points=new List<BpmAfterimagePoint>{new BpmAfterimagePoint(30,2,20,0.1f),new BpmAfterimagePoint(220,0.2f,50,0.4f)} };
 static void Main() {
  var s=Settings(); Check(BpmAfterimageProfile.Validate(s,out _),"valid");
  Check(Near(BpmAfterimageProfile.Evaluate(s,30,1).LifetimeSeconds,2),"30 endpoint");
  Check(Near(BpmAfterimageProfile.Evaluate(s,220,1).LifetimeSeconds,0.2f),"220 endpoint");
  var middle=BpmAfterimageProfile.Evaluate(s,125,1); Check(Near(middle.LifetimeSeconds,1.1f)&&Near(middle.IntervalMs,35)&&Near(middle.Alpha,0.25f),"all parameters interpolate");
  Check(Near(BpmAfterimageProfile.Evaluate(s,10,1).LifetimeSeconds,6),"below range extends lifetime");
  Check(Near(BpmAfterimageProfile.Evaluate(s,240,1).LifetimeSeconds,0.2f),"above range");
  foreach(int divisor in new[]{1,2,4}) {
   var slow=BpmAfterimageProfile.Evaluate(s,30,divisor);
   Check(Near(slow.LifetimeSeconds,2*divisor),"BPM30 half/quarter extends lifetime");
   Check(Near(slow.IntervalMs,20)&&Near(slow.Alpha,0.1f),"low BPM preserves authored interval/alpha");
   double spacing=BpmAfterimageProfile.CaptureIntervalMs(slow,120)/1000d;
   Check(spacing*120+0.000001>=slow.LifetimeSeconds,"slot capacity cannot truncate lifetime");
  }
  Check(Near(BpmAfterimageProfile.CaptureIntervalMs(new BpmAfterimagePoint(7.5f,4,1000f/60,0.0658f),120),1000f/30),"four seconds at 120 slots");
  foreach(int bpm in new[]{180,240}) foreach(int divisor in new[]{1,2,4}) {
   var p=BpmAfterimageProfile.Evaluate(s,bpm,divisor); Check(Near(p.Bpm,(float)bpm/divisor),"divide before clamp");
   Check(Near(p.LifetimeSeconds,BpmAfterimageProfile.Evaluate(s,(float)bpm/divisor,1).LifetimeSeconds),"divided interpolation");
  }
  s.Points.Insert(1,new BpmAfterimagePoint(100,0.8f,30,0.2f)); Check(BpmAfterimageProfile.Validate(s,out _),"middle add");
  Check(Near(BpmAfterimageProfile.Evaluate(s,100,1).LifetimeSeconds,0.8f),"middle exact");
  var copy=BpmAfterimageProfile.Deserialize(BpmAfterimageProfile.Serialize(s)); Check(copy.Enabled&&copy.Points.Count==3&&Near(copy.Points[1].IntervalMs,30),"settings save reload");
  copy.Enabled=false; var preset=BpmAfterimageProfile.Deserialize(BpmAfterimageProfile.Serialize(copy)); Check(!preset.Enabled&&Near(preset.Points[1].Alpha,0.2f),"preset round trip");
  foreach(float bad in new[]{float.NaN,float.PositiveInfinity,0f,11f}) { var invalid=Settings(); invalid.Points[0].LifetimeSeconds=bad; Check(!BpmAfterimageProfile.Validate(invalid,out _),"reject invalid lifetime"); }
  var dup=Settings(); dup.Points.Insert(1,new BpmAfterimagePoint(30,1,20,0.1f)); Check(!BpmAfterimageProfile.Validate(dup,out _),"duplicate bpm");
  foreach(int fps in new[]{30,60,120}) {
   var timed=new TimedAfterimageSlots(); timed.Ensure(2); timed.Mark(0,10,2);
   Check(Near(timed.Remaining(0,11),0.5f),"fraction");
   int expiredFrame=0; for(int frame=0;frame<=fps*3;frame++) if(timed.Remaining(0,10+(double)frame/fps)<=0) {expiredFrame=frame;break;}
   Check(Math.Abs((double)expiredFrame/fps-2)<1d/fps,"FPS independent lifetime");
   double next=0; int captures=0; for(int frame=0;frame<fps*10;frame++) if(TimedAfterimageSlots.Due(ref next,(double)frame/fps,0.1)) captures++;
   Check(Math.Abs(captures-100)<=1,"FPS independent emission");
   timed.Mark(1,10,1); Check(timed.IsTimed(1),"right eye independent metadata"); timed.Mark(1,0,0); Check(!timed.IsTimed(1),"external effect remains frame based");
   timed.Clear(); Check(!timed.IsTimed(0),"song/glow clear");
  }
  double resumed=0; Check(TimedAfterimageSlots.Due(ref resumed,0,0.1),"start"); Check(!TimedAfterimageSlots.Due(ref resumed,0.02,0.1),"no extra emission"); Check(TimedAfterimageSlots.Due(ref resumed,50,0.1)&&resumed>50,"stall no burst");
  Console.WriteLine("PASS "+count+" checks: BPM30..220, 1/2/4, persistence, validation, 30/60/120FPS, clear, external metadata, stall");
 }
}
