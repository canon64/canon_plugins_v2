using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using MainGameUnifiedUi;
using UnityEngine;
namespace SimpleAfterimage {
    public sealed partial class Plugin {
        private bool _showBasic, _showVmd=true, _showAdvanced;
        private string _tabMessage="";
        private BpmAfterimageSettings _vmdDraft;
        private readonly Dictionary<string,string> _numberDrafts=new Dictionary<string,string>();
        private void RegisterAfterimageTab() {
            if(UnifiedUiApi.IsAvailable && !UnifiedUiApi.IsHosting("simple-afterimage")) UnifiedUiApi.RegisterTab("simple-afterimage","残像",DrawAfterimageTab,82);
        }
        private void ResetVmdDraft() { _vmdDraft=BpmAfterimageProfile.Deserialize(BpmAfterimageProfile.Serialize(_vmdSettings)); _numberDrafts.Clear(); }
        private float Number(string id,float value,float width=95) {
            if(!_numberDrafts.TryGetValue(id,out string text)) text=value.ToString("0.####",CultureInfo.InvariantCulture);
            string next=GUILayout.TextField(text,GUILayout.Width(width)); _numberDrafts[id]=next;
            if(float.TryParse(next,NumberStyles.Float,CultureInfo.InvariantCulture,out float parsed) && BpmAfterimageProfile.Finite(parsed)) return parsed;
            return float.NaN;
        }
        private void IntSetting(string label,ConfigEntry<int> entry,int min,int max,string help) {
            GUILayout.BeginHorizontal(); GUILayout.Label(new GUIContent(label,help),GUILayout.Width(210));
            float value=Number("cfg:"+entry.Definition.Key,entry.Value);
            if(GUILayout.Button("適用",GUILayout.Width(55)) && BpmAfterimageProfile.Finite(value)) entry.Value=Mathf.Clamp(Mathf.RoundToInt(value),min,max);
            GUILayout.Label($"{min}〜{max} / 現在 {entry.Value}"); GUILayout.EndHorizontal();
        }
        private void FloatSetting(string label,ConfigEntry<float> entry,float min,float max) {
            GUILayout.BeginHorizontal(); GUILayout.Label(label,GUILayout.Width(210));
            float value=Number("cfg:"+entry.Definition.Key,entry.Value);
            if(GUILayout.Button("適用",GUILayout.Width(55)) && BpmAfterimageProfile.Finite(value)) entry.Value=Mathf.Clamp(value,min,max);
            GUILayout.Label($"{min}〜{max} / 現在 {entry.Value:0.####}"); GUILayout.EndHorizontal();
        }
        private void DrawAfterimageTab() {
            bool enabled=GUILayout.Toggle(_cfgEnabled.Value,"残像 ON"); if(enabled!=_cfgEnabled.Value) _cfgEnabled.Value=enabled;
            _showBasic=GUILayout.Toggle(_showBasic,(_showBasic?"▼":"▶")+" 通常の残像",GUI.skin.button,GUILayout.Width(220));
            if(_showBasic) {
                IntSetting("寿命（フレーム）",_cfgFadeFrames,1,300,"60FPSなら120フレームは約2秒。VMD連動中はBPM別の秒設定を使用。");
                IntSetting("発生間隔（フレーム）",_cfgCaptureInterval,1,60,"1=毎フレーム、2=2フレームごと。");
                IntSetting("同時残像数",_cfgMaxSlots,1,300,"上限を超えると最も古い残像を置き換えます。増やすほどGPUメモリを使用。");
                FloatSetting("濃さ",_cfgAlphaScale,0,1);
                FloatSetting("色 R",_cfgTintR,0,1); FloatSetting("色 G",_cfgTintG,0,1); FloatSetting("色 B",_cfgTintB,0,1); FloatSetting("色の不透明度",_cfgTintA,0,1);
                bool front=GUILayout.Toggle(_cfgFrontOfCharacter.Value,"キャラの前面に表示"); if(front!=_cfgFrontOfCharacter.Value) _cfgFrontOfCharacter.Value=front;
                string[] curves={"Linear","EaseIn","EaseOut","Square"};
                int curve=Array.IndexOf(curves,_cfgFadeCurve.Value);
                int chosen=GUILayout.SelectionGrid(Math.Max(0,curve),new[]{"均等に消える","後半に速く消える","前半に速く消える","三乗で消える"},4);
                if(chosen!=curve) _cfgFadeCurve.Value=curves[chosen];
                FloatSetting("グロー閾値",_cfgGlowThreshold,0,5); FloatSetting("グロー強さ",_cfgGlowStrength,0,10); FloatSetting("グローぼかし（%）",_cfgGlowBlurPercent,0,100);
            }
            _showVmd=GUILayout.Toggle(_showVmd,(_showVmd?"▼":"▶")+" VMDのBPM別設定",GUI.skin.button,GUILayout.Width(220));
            if(_showVmd && _vmdSettings!=null) {
                bool sync=GUILayout.Toggle(_vmdSettings.Enabled,"VMDのBPMに連動");
                if(sync!=_vmdSettings.Enabled) { _vmdSettings.Enabled=sync; _vmdDraft.Enabled=sync; SaveVmdSettings(); }
                GUILayout.Label("状態: "+_vmdReason+(_vmdApplied==null?" / 通常設定を使用":""));
                if(_vmdApplied!=null) {
                    GUILayout.Label($"曲BPM {_vmdBpm} × {(_vmdDivisor==1?"通常": "1/"+_vmdDivisor)} → 計算BPM {_vmdApplied.Bpm:0.##}（範囲30〜220）");
                    GUILayout.Label($"適用: 寿命 {_vmdApplied.LifetimeSeconds:0.###}秒 / 間隔 {BpmAfterimageProfile.CaptureIntervalMs(_vmdApplied,_cfgMaxSlots.Value):0.##}ms / 濃さ {_vmdApplied.Alpha:0.####}");
                    if(_vmdApplied.LifetimeSeconds*1000/_vmdApplied.IntervalMs>_cfgMaxSlots.Value) GUILayout.Label("寿命を保つため、同時残像数に合わせて発生間隔を自動調整中。");
                }
                GUILayout.Label("基準点の間は補間。30未満の寿命は30÷実効BPM倍に延長。220超は220の設定。実際の発生は最大で毎描画フレーム1回。");
                GUILayout.Label("例: 曲BPM180 → 強180・中90・弱45。寿命0.01〜10秒 / 間隔1〜5000ms / 濃さ0〜1。");
                GUILayout.BeginHorizontal();
                foreach(string title in new[]{"基準BPM","寿命（秒）","間隔（ms）","濃さ"}) GUILayout.Label(title,GUILayout.Width(110));
                GUILayout.EndHorizontal();
                int remove=-1;
                for(int i=0;i<_vmdDraft.Points.Count;i++) {
                    var p=_vmdDraft.Points[i]; bool endpoint=i==0 || i==_vmdDraft.Points.Count-1;
                    GUILayout.BeginHorizontal();
                    if(endpoint) GUILayout.Label(p.Bpm.ToString("0"),GUILayout.Width(110)); else p.Bpm=Number("bpm"+i,p.Bpm,110);
                    p.LifetimeSeconds=Number("life"+i,p.LifetimeSeconds,110); p.IntervalMs=Number("interval"+i,p.IntervalMs,110); p.Alpha=Number("alpha"+i,p.Alpha,110);
                    if(!endpoint && GUILayout.Button("削除",GUILayout.Width(55))) remove=i;
                    GUILayout.EndHorizontal();
                }
                if(remove>=0) { _vmdDraft.Points.RemoveAt(remove); _numberDrafts.Clear(); }
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("基準点を追加",GUILayout.Width(140))) {
                    if(BpmAfterimageProfile.Validate(_vmdDraft,out string validation)) {
                        int gap=1; for(int i=2;i<_vmdDraft.Points.Count;i++) if(_vmdDraft.Points[i].Bpm-_vmdDraft.Points[i-1].Bpm>_vmdDraft.Points[gap].Bpm-_vmdDraft.Points[gap-1].Bpm) gap=i;
                        float bpm=(_vmdDraft.Points[gap-1].Bpm+_vmdDraft.Points[gap].Bpm)/2;
                        _vmdDraft.Points.Insert(gap,BpmAfterimageProfile.Evaluate(_vmdDraft,bpm,1)); _numberDrafts.Clear();
                    } else _tabMessage=validation;
                }
                if(GUILayout.Button("BPM設定を適用・保存",GUILayout.Width(190))) {
                    if(BpmAfterimageProfile.Validate(_vmdDraft,out string validation)) {
                        _vmdSettings=BpmAfterimageProfile.Deserialize(BpmAfterimageProfile.Serialize(_vmdDraft)); _vmdSettings.Enabled=sync;
                        SaveVmdSettings(); ResetVmdDraft(); _tabMessage="BPM別設定を保存しました";
                    } else _tabMessage=validation;
                }
                if(GUILayout.Button("保存値を再読込",GUILayout.Width(150))) LoadVmdSettings();
                GUILayout.EndHorizontal();
            }
            _showAdvanced=GUILayout.Toggle(_showAdvanced,(_showAdvanced?"▼":"▶")+" 既存の詳細設定・プリセット",GUI.skin.button,GUILayout.Width(260));
            if(_showAdvanced) {
                bool sync=GUILayout.Toggle(_cfgBeatSyncFadeEnabled.Value,"通常Hの速さに寿命を同期"); if(sync!=_cfgBeatSyncFadeEnabled.Value) _cfgBeatSyncFadeEnabled.Value=sync;
                IntSetting("速さ最小時の寿命（フレーム）",_cfgBeatSyncFadeMin,1,300,""); IntSetting("速さ最大時の寿命（フレーム）",_cfgBeatSyncFadeMax,1,300,"");
                bool screen=GUILayout.Toggle(_cfgUseScreenSize.Value,"画面解像度でキャプチャ"); if(screen!=_cfgUseScreenSize.Value) _cfgUseScreenSize.Value=screen;
                IntSetting("キャプチャ幅",_cfgCaptureWidth,0,8192,"0は画面サイズ"); IntSetting("キャプチャ高さ",_cfgCaptureHeight,0,8192,"0は画面サイズ");
                GUILayout.BeginHorizontal(); GUILayout.Label("プリセット名",GUILayout.Width(110));
                string name=GUILayout.TextField(_cfgPresetName.Value,GUILayout.Width(240)); if(name!=_cfgPresetName.Value) _cfgPresetName.Value=name;
                if(GUILayout.Button("保存",GUILayout.Width(70))) { if(!string.IsNullOrWhiteSpace(name)) { SavePreset(name.Trim()); _tabMessage="プリセット保存"; } }
                if(GUILayout.Button("読込",GUILayout.Width(70))) { _tabMessage=LoadPreset(name.Trim())?"プリセット読込":"プリセットがありません"; _numberDrafts.Clear(); }
                GUILayout.EndHorizontal();
                foreach(string preset in GetPresetNames()) if(GUILayout.Button(preset,GUILayout.Width(300))) { LoadPreset(preset); _cfgPresetName.Value=preset; _numberDrafts.Clear(); }
                if(GUILayout.Button("通常設定をファイルから再読込",GUILayout.Width(260))) { LoadConfigJson(); ApplyConfig(); _numberDrafts.Clear(); }
            }
            if(!string.IsNullOrEmpty(_tabMessage)) GUILayout.Label(_tabMessage);
        }
    }
}
