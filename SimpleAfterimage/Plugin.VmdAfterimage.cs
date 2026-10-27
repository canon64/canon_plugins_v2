using System;
using System.IO;
using System.Text;
using UnityEngine;
namespace SimpleAfterimage {
    public sealed partial class Plugin {
        private BpmAfterimageSettings _vmdSettings;
        private BpmAfterimagePoint _vmdApplied;
        private string _vmdReason="VMD再生待ち", _vmdSong;
        private int _vmdBpm, _vmdDivisor=1;
        private double _nextVmdCapture;
        private readonly TimedAfterimageSlots _timedSlots=new TimedAfterimageSlots();
        private string VmdSettingsPath => Path.Combine(Path.GetDirectoryName(_configJsonPath),"VmdAfterimageSettings.json");
        private BpmAfterimageSettings DefaultVmdSettings() {
            float life=Mathf.Clamp(_cfgFadeFrames.Value/60f,0.01f,10);
            float interval=Mathf.Clamp(_cfgCaptureInterval.Value*1000f/60f,1,5000);
            return new BpmAfterimageSettings { Points=new System.Collections.Generic.List<BpmAfterimagePoint> {
                new BpmAfterimagePoint(30,life,interval,_cfgAlphaScale.Value),
                new BpmAfterimagePoint(220,Mathf.Max(0.01f,life*30f/220f),interval,_cfgAlphaScale.Value) } };
        }
        private void LoadVmdSettings() {
            try {
                var settings=File.Exists(VmdSettingsPath) ? BpmAfterimageProfile.Deserialize(File.ReadAllText(VmdSettingsPath,Encoding.UTF8)) : DefaultVmdSettings();
                if(!BpmAfterimageProfile.Validate(settings,out string error)) throw new InvalidDataException(error);
                _vmdSettings=settings;
                if(!File.Exists(VmdSettingsPath)) SaveVmdSettings();
            } catch(Exception ex) { _vmdSettings=DefaultVmdSettings(); _tabMessage="VMD設定読込失敗: "+ex.Message; Logger.LogWarning(_tabMessage); }
            ResetVmdDraft();
        }
        private void SaveVmdSettings() => File.WriteAllText(VmdSettingsPath,BpmAfterimageProfile.Serialize(_vmdSettings),Encoding.UTF8);
        private string VmdPresetPath(string name) => Path.Combine(Path.GetDirectoryName(_configJsonPath),"vmd-preset-"+Convert.ToBase64String(Encoding.UTF8.GetBytes(name)).TrimEnd('=').Replace('/','_').Replace('+','-')+".json");
        private void SaveVmdPreset(string name) => File.WriteAllText(VmdPresetPath(name),BpmAfterimageProfile.Serialize(_vmdSettings),Encoding.UTF8);
        private void LoadVmdPreset(string name) {
            if(!File.Exists(VmdPresetPath(name))) return;
            var settings=BpmAfterimageProfile.Deserialize(File.ReadAllText(VmdPresetPath(name),Encoding.UTF8));
            if(!BpmAfterimageProfile.Validate(settings,out string error)) throw new InvalidDataException(error);
            _vmdSettings=settings; SaveVmdSettings(); ResetVmdDraft();
        }
        private void RefreshVmdAfterimage() {
            bool was=_vmdApplied!=null;
            float previousInterval=_vmdApplied!=null ? _vmdApplied.IntervalMs : 0;
            _vmdApplied=null; _vmdBpm=0; _vmdDivisor=1;
            if(_vmdSettings==null || !_vmdSettings.Enabled) { _vmdReason="VMDのBPM連動がOFF"; _nextVmdCapture=0; return; }
            bool valid=MainGameRhythmSync.Plugin.TryGetVmdAfterimageRhythm(out string song,out _vmdBpm,out _vmdDivisor,out _vmdReason);
            if(!string.Equals(song,_vmdSong,StringComparison.OrdinalIgnoreCase)) { _vmdSong=song; ClearAfterimageSlots(); _nextVmdCapture=0; }
            if(valid && !IsExternalAfterimageActiveNow()) _vmdApplied=BpmAfterimageProfile.Evaluate(_vmdSettings,_vmdBpm,_vmdDivisor);
            if(was!=(_vmdApplied!=null) || (_vmdApplied!=null && !Mathf.Approximately(previousInterval,_vmdApplied.IntervalMs))) { _nextVmdCapture=0; _vrRightBuffer.NextVmdCapture=0; }
        }
        private bool CaptureDue(ref double next,int frame,int interval) {
            if(_vmdApplied==null || IsWarpAfterimageActive() || IsExternalAfterimageActiveNow()) return interval<=1 || frame%interval==0;
            return TimedAfterimageSlots.Due(ref next,Time.unscaledTime,BpmAfterimageProfile.CaptureIntervalMs(_vmdApplied,_cfgMaxSlots.Value)/1000d);
        }
        private void MarkTimedNormal(TimedAfterimageSlots timed,int count,int slot) {
            timed.Ensure(count);
            timed.Mark(slot,Time.unscaledTime,_vmdApplied!=null ? _vmdApplied.LifetimeSeconds : 0);
        }
        private void AgeSlot(TimedAfterimageSlots timed,int[] life,int i) {
            if(life[i]<=0) return;
            if(timed.IsTimed(i)) { if(timed.Remaining(i,Time.unscaledTime)<=0) life[i]=0; }
            else life[i]--;
        }
    }
}
