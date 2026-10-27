using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
namespace SimpleAfterimage {
    [DataContract] public sealed class BpmAfterimagePoint {
        [DataMember] public float Bpm;
        [DataMember] public float LifetimeSeconds;
        [DataMember] public float IntervalMs;
        [DataMember] public float Alpha;
        public BpmAfterimagePoint() { }
        public BpmAfterimagePoint(float bpm, float life, float interval, float alpha) { Bpm=bpm; LifetimeSeconds=life; IntervalMs=interval; Alpha=alpha; }
    }
    [DataContract] public sealed class BpmAfterimageSettings {
        [DataMember] public bool Enabled = true;
        [DataMember] public List<BpmAfterimagePoint> Points = new List<BpmAfterimagePoint>();
    }
    internal static class BpmAfterimageProfile {
        internal static string Serialize(BpmAfterimageSettings settings) {
            using(var stream=new MemoryStream()) { new DataContractJsonSerializer(typeof(BpmAfterimageSettings)).WriteObject(stream,settings); return Encoding.UTF8.GetString(stream.ToArray()); }
        }
        internal static BpmAfterimageSettings Deserialize(string json) {
            using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(json))) return (BpmAfterimageSettings)new DataContractJsonSerializer(typeof(BpmAfterimageSettings)).ReadObject(stream);
        }
        internal static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        internal static bool Validate(BpmAfterimageSettings settings, out string error) {
            error = "";
            if (settings == null || settings.Points == null || settings.Points.Count < 2) { error="BPM30・220の基準点が必要です"; return false; }
            var sorted = new List<BpmAfterimagePoint>(settings.Points);
            if (sorted.Exists(p => p == null)) { error="基準点が不正です"; return false; }
            sorted.Sort((a,b)=>a.Bpm.CompareTo(b.Bpm));
            for (int i=0;i<sorted.Count;i++) {
                var p=sorted[i];
                if (!Finite(p.Bpm) || p.Bpm<30 || p.Bpm>220 || (i>0 && p.Bpm<=sorted[i-1].Bpm)) { error="BPMは30〜220、重複不可です"; return false; }
                if (!Finite(p.LifetimeSeconds) || p.LifetimeSeconds<0.01f || p.LifetimeSeconds>10 || !Finite(p.IntervalMs) || p.IntervalMs<1 || p.IntervalMs>5000 || !Finite(p.Alpha) || p.Alpha<0 || p.Alpha>1) { error="寿命0.01〜10秒、間隔1〜5000ms、濃さ0〜1で入力してください"; return false; }
            }
            if (sorted[0].Bpm!=30 || sorted[sorted.Count-1].Bpm!=220) { error="両端のBPM30・220を残してください"; return false; }
            settings.Points=sorted; return true;
        }
        internal static BpmAfterimagePoint Evaluate(BpmAfterimageSettings settings, float songBpm, int divisor) {
            float bpm = songBpm / Math.Max(1, divisor);
            var points=settings.Points;
            if (bpm<=points[0].Bpm) {
                var result=Copy(points[0],bpm);
                // Below the lowest anchor, keep the same lifetime in beats.
                if (Finite(bpm) && bpm>0) result.LifetimeSeconds *= points[0].Bpm/bpm;
                return result;
            }
            for(int i=1;i<points.Count;i++) if(bpm<=points[i].Bpm) {
                var a=points[i-1]; var b=points[i]; float t=(bpm-a.Bpm)/(b.Bpm-a.Bpm);
                return new BpmAfterimagePoint(bpm,a.LifetimeSeconds+(b.LifetimeSeconds-a.LifetimeSeconds)*t,a.IntervalMs+(b.IntervalMs-a.IntervalMs)*t,a.Alpha+(b.Alpha-a.Alpha)*t);
            }
            return Copy(points[points.Count-1],bpm);
        }
        internal static float CaptureIntervalMs(BpmAfterimagePoint point, int slots) =>
            Math.Max(point.IntervalMs, point.LifetimeSeconds*1000f/Math.Max(1,slots));
        private static BpmAfterimagePoint Copy(BpmAfterimagePoint p,float bpm) => new BpmAfterimagePoint(bpm,p.LifetimeSeconds,p.IntervalMs,p.Alpha);
    }
    // Per-slot real-time metadata; frame-based external effects retain their existing counters.
    internal sealed class TimedAfterimageSlots {
        private double[] _expires=Array.Empty<double>(), _duration=Array.Empty<double>();
        internal void Ensure(int count) { if (_expires.Length==count) return; _expires=new double[count]; _duration=new double[count]; }
        internal void Mark(int slot,double now,double duration) { _duration[slot]=duration; _expires[slot]=now+duration; }
        internal void Clear() { Array.Clear(_duration,0,_duration.Length); }
        internal bool IsTimed(int slot) => _duration[slot]>0;
        internal float Remaining(int slot,double now) => (float)Math.Max(0,Math.Min(1,(_expires[slot]-now)/_duration[slot]));
        internal static bool Due(ref double next,double now,double interval) {
            if(now+0.000001<next) return false;
            // Keep the phase after a short frame delay; skip missed captures after a stall.
            next=next<=0 || now-next>=interval ? now+interval : next+interval;
            return true;
        }
    }
}
