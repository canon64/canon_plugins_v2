using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;
using VRGIN.Core;

namespace SimpleAfterimage
{
    public sealed partial class Plugin
    {
        // プリセットデータ
        private class PresetData
        {
            public int    FadeFrames      = 30;
            public int    MaxSlots        = 30;
            public int    CaptureInterval = 1;
            public float  TintR           = 1f;
            public float  TintG           = 1f;
            public float  TintB           = 1f;
            public float  TintA           = 1f;
            public float  AlphaScale      = 1f;
            public string FadeCurve       = "Linear";
            public bool   FrontOfCharacter = true;
            public float  GlowThreshold   = 1f;
            public float  GlowStrength    = 1f;
            public float  GlowBlurPercent = 0f;
        }

        private void SetupConfig()
        {
            const string cat1 = "01.一般";
            const string cat2 = "02.キャプチャ";
            const string cat3 = "03.オーバーレイ";
            const string cat4 = "04.元カメラ";
            const string cat5 = "05.プリセット";

            _cfgEnabled         = Config.Bind(cat1, "有効",             true,  "機能の有効/無効");
            _cfgVerboseLog      = Config.Bind(cat1, "詳細ログ",         false, "詳細ログを出力する");
            _cfgFadeFrames      = Config.Bind(cat2, "残像寿命フレーム", 120,    new ConfigDescription("残像が消えるまでのフレーム数", new AcceptableValueRange<int>(1, 300)));
            _cfgMaxSlots        = Config.Bind(cat2, "同時残像数",       120,    new ConfigDescription("同時に保持する残像スロット数", new AcceptableValueRange<int>(1, 300)));
            _cfgCaptureInterval = Config.Bind(cat2, "キャプチャ間隔",   1,     new ConfigDescription("何フレームごとにキャプチャするか(1=毎フレーム)", new AcceptableValueRange<int>(1, 60)));
            _cfgUseScreenSize   = Config.Bind(cat2, "画面解像度を使う", true,  "キャプチャサイズに画面解像度を使う");
            _cfgCaptureWidth    = Config.Bind(cat2, "キャプチャ幅",     0,     new ConfigDescription("UseScreenSize=false時のキャプチャ幅", new AcceptableValueRange<int>(0, 8192)));
            _cfgCaptureHeight   = Config.Bind(cat2, "キャプチャ高さ",   0,     new ConfigDescription("UseScreenSize=false時のキャプチャ高さ", new AcceptableValueRange<int>(0, 8192)));
            _cfgCharaLayer      = Config.Bind(cat2, "キャラレイヤー名", "Chara", "キャプチャ対象のレイヤー名(通常/Charaモード)");
            _cfgFullSceneExcludeLayer = Config.Bind(cat2, "全画面除外レイヤー名", "UI", "外部から全画面キャプチャ要求時に除外するレイヤー名(これ以外を全部キャプチャ)");
            _cfgTintR           = Config.Bind(cat3, "色R",             0.5f,    new ConfigDescription("残像色 R (0..1)", new AcceptableValueRange<float>(0f, 1f)));
            _cfgTintG           = Config.Bind(cat3, "色G",             0.5f,    new ConfigDescription("残像色 G (0..1)", new AcceptableValueRange<float>(0f, 1f)));
            _cfgTintB           = Config.Bind(cat3, "色B",             0.5f,    new ConfigDescription("残像色 B (0..1)", new AcceptableValueRange<float>(0f, 1f)));
            _cfgTintA           = Config.Bind(cat3, "色A",             1f,    new ConfigDescription("残像色 A (0..1)", new AcceptableValueRange<float>(0f, 1f)));
            _cfgAlphaScale      = Config.Bind(cat3, "残像アルファ倍率", 0.03f,   new ConfigDescription("残像の全体濃度スケール(0..1)", new AcceptableValueRange<float>(0f, 1f)));
            _cfgFadeCurve       = Config.Bind(cat3, "フェードカーブ",   "Linear", new ConfigDescription("Linear=線形 / EaseIn=最初ゆっくり後半急 / EaseOut=最初急後半ゆっくり / Square=三乗", new AcceptableValueList<string>("Linear", "EaseIn", "EaseOut", "Square")));
            _cfgFrontOfCharacter = Config.Bind(cat3, "キャラ前面に表示", true, "true=キャラの前面 / false=キャラの背面");
            _cfgGlowThreshold   = Config.Bind(cat3, "グロー閾値",       1f, new ConfigDescription("グロー抽出の閾値(0..5)", new AcceptableValueRange<float>(0f, 5f)));
            _cfgGlowStrength    = Config.Bind(cat3, "グロー強さ",       1f, new ConfigDescription("グロー強度(0..10)", new AcceptableValueRange<float>(0f, 10f)));
            _cfgGlowBlurPercent = Config.Bind(cat3, "グローぼかし%",    0f, new ConfigDescription("グローぼかし量(0..100)。0で無効", new AcceptableValueRange<float>(0f, 100f)));
            _cfgPreferCameraMain    = Config.Bind(cat4, "Camera.main優先",       true, "Camera.mainを優先する");
            _cfgCameraNameFilter    = Config.Bind(cat4, "カメラ名フィルタ",       "",   "カメラ名の部分一致フィルタ(空なら無効)");
            _cfgCameraFallbackIndex = Config.Bind(cat4, "カメラ候補フォールバック", 0,  new ConfigDescription("候補カメラのフォールバックインデックス", new AcceptableValueRange<int>(0, 64)));
            _cfgPresetName         = Config.Bind(cat5, "プリセット名", "default", "保存・読込するプリセット名");
            _cfgPresetAction       = Config.Bind(cat5, "プリセット操作", "なし", new ConfigDescription("保存/読込を選んで実行", new AcceptableValueList<string>("なし", "保存", "読込")));
            _cfgBeatSyncFadeEnabled = Config.Bind(cat5, "速さ同期有効", false, "BeatSyncの速さスライダーに残像寿命フレームを同期する");
            _cfgBeatSyncFadeMin    = Config.Bind(cat5, "速さ最小時FadeFrames", 120,  new ConfigDescription("速さ0の時の残像寿命フレーム", new AcceptableValueRange<int>(1, 300)));
            _cfgBeatSyncFadeMax    = Config.Bind(cat5, "速さ最大時FadeFrames", 60, new ConfigDescription("速さ1の時の残像寿命フレーム", new AcceptableValueRange<int>(1, 300)));

            Config.SettingChanged += (_, e) =>
            {
                if (e.ChangedSetting == _cfgPresetAction && _cfgPresetAction.Value != "なし")
                    _pendingAction = _cfgPresetAction.Value;
                else
                    ApplyConfig();
                SaveConfigJson();
            };
        }

        private void ApplyConfig()
        {
            if (_cfgEnabled != null && (!CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom || !IsEffectivelyEnabled() || !IsHSceneActive()))
            {
                StopAfterimageRuntime();
                return;
            }

            _characterMask = LayerMask.GetMask(_cfgCharaLayer.Value ?? "Chara");

            Camera currentCamera = ResolveCamera();
            bool vrStereoRenderImagePath = ShouldUseVrStereoRenderImage(currentCamera);
            if (vrStereoRenderImagePath)
            {
                ReleaseCaptureCameraRuntime();
                _afterimageRuntimeReady = true;
                return;
            }

            int newSlots = Mathf.Clamp(_cfgMaxSlots.Value, 1, 300);
            bool useVRSize = VR.Active && UnityEngine.XR.XRSettings.eyeTextureWidth > 0;
            int newW = _cfgUseScreenSize.Value || _cfgCaptureWidth.Value <= 0
                ? (useVRSize ? UnityEngine.XR.XRSettings.eyeTextureWidth  : Screen.width)
                : _cfgCaptureWidth.Value;
            int newH = _cfgUseScreenSize.Value || _cfgCaptureHeight.Value <= 0
                ? (useVRSize ? UnityEngine.XR.XRSettings.eyeTextureHeight : Screen.height)
                : _cfgCaptureHeight.Value;
            newW = Mathf.Max(16, newW);
            newH = Mathf.Max(16, newH);

            bool needRebuild = _slots == null
                || _slots.Length != newSlots
                || _rtWidth != newW
                || _rtHeight != newH;

            if (needRebuild)
            {
                ReleaseSlots();
                ReleaseVrStereoSlots();
                _slots     = new RenderTexture[newSlots];
                _life      = new int[newSlots];
                _drawSlots = new RenderTexture[newSlots];
                _drawAlpha = new float[newSlots];
                _rtWidth    = newW;
                _rtHeight   = newH;
                _writeIndex = 0;
                _frameCounter = 0;
                if (_cfgVerboseLog.Value)
                    Logger.LogInfo($"slots rebuilt: {newW}x{newH} slots={newSlots}");
            }

            if (_cameraRoot == null)
                SetupCaptureCamera();

            _afterimageRuntimeReady = _slots != null && _captureCamera != null;

            bool pipelineReady = _captureBloom != null && _capturePostProcessLayer != null && _capturePostProcessVolume != null;
            ApplyCaptureGlowSettings(pipelineReady);
        }

        // ---- プリセット公開API ----

        public bool SavePreset(string name)
        {
            _presets[name] = new PresetData
            {
                FadeFrames       = _cfgFadeFrames.Value,
                MaxSlots         = _cfgMaxSlots.Value,
                CaptureInterval  = _cfgCaptureInterval.Value,
                TintR            = _cfgTintR.Value,
                TintG            = _cfgTintG.Value,
                TintB            = _cfgTintB.Value,
                TintA            = _cfgTintA.Value,
                AlphaScale       = _cfgAlphaScale.Value,
                FadeCurve        = _cfgFadeCurve.Value,
                FrontOfCharacter = _cfgFrontOfCharacter.Value,
                GlowThreshold    = _cfgGlowThreshold.Value,
                GlowStrength     = _cfgGlowStrength.Value,
                GlowBlurPercent  = _cfgGlowBlurPercent.Value,
            };
            SavePresetsFile();
            SaveVmdPreset(name);
            return true;
        }

        public bool LoadPreset(string name)
        {
            if (!_presets.TryGetValue(name, out PresetData p)) return false;
            _cfgMaxSlots.Value = Mathf.Clamp(p.MaxSlots, 1, 300);
            _cfgCaptureInterval.Value = Mathf.Clamp(p.CaptureInterval, 1, 60);
            _cfgTintR.Value = Mathf.Clamp01(p.TintR); _cfgTintG.Value = Mathf.Clamp01(p.TintG); _cfgTintB.Value = Mathf.Clamp01(p.TintB); _cfgTintA.Value = Mathf.Clamp01(p.TintA);
            _cfgAlphaScale.Value = Mathf.Clamp01(p.AlphaScale);
            _cfgFadeCurve.Value = p.FadeCurve;
            _cfgFrontOfCharacter.Value = p.FrontOfCharacter;
            LoadVmdPreset(name);
            _cfgFadeFrames.Value = Mathf.Clamp(p.FadeFrames, 1, 300);
            _cfgGlowThreshold.Value = Mathf.Clamp(p.GlowThreshold, 0f, 5f);
            _cfgGlowStrength.Value = Mathf.Clamp(p.GlowStrength, 0f, 10f);
            _cfgGlowBlurPercent.Value = Mathf.Clamp(p.GlowBlurPercent, 0f, 100f);
            return true;
        }

        public string[] GetPresetNames()
        {
            var names = new string[_presets.Count];
            _presets.Keys.CopyTo(names, 0);
            return names;
        }

        // ---- config.json 保存・読込 ----

        private void SaveConfigJson()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine($"  \"Enabled\": {(_cfgEnabled.Value ? "true" : "false")},");
                sb.AppendLine($"  \"VerboseLog\": {(_cfgVerboseLog.Value ? "true" : "false")},");
                sb.AppendLine($"  \"FadeFrames\": {_cfgFadeFrames.Value},");
                sb.AppendLine($"  \"MaxSlots\": {_cfgMaxSlots.Value},");
                sb.AppendLine($"  \"CaptureInterval\": {_cfgCaptureInterval.Value},");
                sb.AppendLine($"  \"UseScreenSize\": {(_cfgUseScreenSize.Value ? "true" : "false")},");
                sb.AppendLine($"  \"CaptureWidth\": {_cfgCaptureWidth.Value},");
                sb.AppendLine($"  \"CaptureHeight\": {_cfgCaptureHeight.Value},");
                sb.AppendLine($"  \"CharaLayer\": \"{Esc(_cfgCharaLayer.Value)}\",");
                sb.AppendLine($"  \"FullSceneExcludeLayer\": \"{Esc(_cfgFullSceneExcludeLayer.Value)}\",");
                sb.AppendLine($"  \"TintR\": {_cfgTintR.Value:0.####},");
                sb.AppendLine($"  \"TintG\": {_cfgTintG.Value:0.####},");
                sb.AppendLine($"  \"TintB\": {_cfgTintB.Value:0.####},");
                sb.AppendLine($"  \"TintA\": {_cfgTintA.Value:0.####},");
                sb.AppendLine($"  \"AlphaScale\": {_cfgAlphaScale.Value:0.####},");
                sb.AppendLine($"  \"FadeCurve\": \"{Esc(_cfgFadeCurve.Value)}\",");
                sb.AppendLine($"  \"FrontOfCharacter\": {(_cfgFrontOfCharacter.Value ? "true" : "false")},");
                sb.AppendLine($"  \"GlowThreshold\": {_cfgGlowThreshold.Value:0.##},");
                sb.AppendLine($"  \"GlowStrength\": {_cfgGlowStrength.Value:0.##},");
                sb.AppendLine($"  \"GlowBlurPercent\": {_cfgGlowBlurPercent.Value:0.##},");
                sb.AppendLine($"  \"PreferCameraMain\": {(_cfgPreferCameraMain.Value ? "true" : "false")},");
                sb.AppendLine($"  \"CameraNameFilter\": \"{Esc(_cfgCameraNameFilter.Value)}\",");
                sb.AppendLine($"  \"CameraFallbackIndex\": {_cfgCameraFallbackIndex.Value},");
                sb.AppendLine($"  \"PresetName\": \"{Esc(_cfgPresetName.Value)}\",");
                sb.AppendLine($"  \"BeatSyncFadeEnabled\": {(_cfgBeatSyncFadeEnabled.Value ? "true" : "false")},");
                sb.AppendLine($"  \"BeatSyncFadeMin\": {_cfgBeatSyncFadeMin.Value},");
                sb.AppendLine($"  \"BeatSyncFadeMax\": {_cfgBeatSyncFadeMax.Value}");
                sb.AppendLine("}");
                File.WriteAllText(_configJsonPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("config json save failed: " + ex.Message);
            }
        }

        private void LoadConfigJson()
        {
            if (string.IsNullOrEmpty(_configJsonPath) || !File.Exists(_configJsonPath))
            {
                // JSONがなければ現在のcfg初期値でJSONを作成する
                SaveConfigJson();
                return;
            }

            try
            {
                string json = File.ReadAllText(_configJsonPath, Encoding.UTF8).Trim();
                if (!json.StartsWith("{") || !json.EndsWith("}")) return;

                json = json.Substring(1, json.Length - 2).Trim();
                int pos = 0;

                while (pos < json.Length)
                {
                    string key = ReadJsonString(json, ref pos);
                    if (key == null) break;

                    SkipColon(json, ref pos);
                    SkipWs(json, ref pos);

                    if (pos >= json.Length) break;

                    string val;
                    if (json[pos] == '"')
                    {
                        val = ReadJsonString(json, ref pos);
                    }
                    else
                    {
                        int end = pos;
                        while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
                        val = json.Substring(pos, end - pos).Trim();
                        pos = end;
                    }

                    ApplyConfigJsonValue(key, val);
                    SkipComma(json, ref pos);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("config json load failed: " + ex.Message);
            }
        }

        private void ApplyConfigJsonValue(string key, string val)
        {
            switch (key)
            {
                case "Enabled":
                    _cfgEnabled.Value = val == "true";
                    break;
                case "VerboseLog":
                    _cfgVerboseLog.Value = val == "true";
                    break;
                case "FadeFrames":
                    if (int.TryParse(val, out int fadeFrames))
                        _cfgFadeFrames.Value = Mathf.Clamp(fadeFrames, 1, 300);
                    break;
                case "MaxSlots":
                    if (int.TryParse(val, out int maxSlots))
                        _cfgMaxSlots.Value = Mathf.Clamp(maxSlots, 1, 300);
                    break;
                case "CaptureInterval":
                    if (int.TryParse(val, out int captureInterval))
                        _cfgCaptureInterval.Value = Mathf.Clamp(captureInterval, 1, 60);
                    break;
                case "UseScreenSize":
                    _cfgUseScreenSize.Value = val == "true";
                    break;
                case "CaptureWidth":
                    if (int.TryParse(val, out int captureWidth))
                        _cfgCaptureWidth.Value = Mathf.Max(16, captureWidth);
                    break;
                case "CaptureHeight":
                    if (int.TryParse(val, out int captureHeight))
                        _cfgCaptureHeight.Value = Mathf.Max(16, captureHeight);
                    break;
                case "CharaLayer":
                    _cfgCharaLayer.Value = val ?? "Chara";
                    break;
                case "FullSceneExcludeLayer":
                    _cfgFullSceneExcludeLayer.Value = val ?? "UI";
                    break;
                case "TintR":
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tintR))
                        _cfgTintR.Value = Mathf.Clamp01(tintR);
                    break;
                case "TintG":
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tintG))
                        _cfgTintG.Value = Mathf.Clamp01(tintG);
                    break;
                case "TintB":
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tintB))
                        _cfgTintB.Value = Mathf.Clamp01(tintB);
                    break;
                case "TintA":
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tintA))
                        _cfgTintA.Value = Mathf.Clamp01(tintA);
                    break;
                case "AlphaScale":
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float alphaScale))
                        _cfgAlphaScale.Value = Mathf.Clamp01(alphaScale);
                    break;
                case "FadeCurve":
                    _cfgFadeCurve.Value = val ?? "Linear";
                    break;
                case "FrontOfCharacter":
                    _cfgFrontOfCharacter.Value = val == "true";
                    break;
                case "GlowThreshold":
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float glowThreshold))
                        _cfgGlowThreshold.Value = Mathf.Clamp(glowThreshold, 0f, 5f);
                    break;
                case "GlowStrength":
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float glowStrength))
                        _cfgGlowStrength.Value = Mathf.Clamp(glowStrength, 0f, 10f);
                    break;
                case "GlowBlurPercent":
                    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float glowBlurPercent))
                        _cfgGlowBlurPercent.Value = Mathf.Clamp(glowBlurPercent, 0f, 100f);
                    break;
                case "PreferCameraMain":
                    _cfgPreferCameraMain.Value = val == "true";
                    break;
                case "CameraNameFilter":
                    _cfgCameraNameFilter.Value = val ?? "";
                    break;
                case "CameraFallbackIndex":
                    if (int.TryParse(val, out int fallbackIndex))
                        _cfgCameraFallbackIndex.Value = Mathf.Max(0, fallbackIndex);
                    break;
                case "PresetName":
                    _cfgPresetName.Value = val ?? "default";
                    break;
                case "BeatSyncFadeEnabled":
                    _cfgBeatSyncFadeEnabled.Value = val == "true";
                    break;
                case "BeatSyncFadeMin":
                    if (int.TryParse(val, out int beatSyncMin))
                        _cfgBeatSyncFadeMin.Value = Mathf.Clamp(beatSyncMin, 1, 300);
                    break;
                case "BeatSyncFadeMax":
                    if (int.TryParse(val, out int beatSyncMax))
                        _cfgBeatSyncFadeMax.Value = Mathf.Clamp(beatSyncMax, 1, 300);
                    break;
            }
        }

        // ---- プリセットJSON ----

        private void LoadPresetsFile()
        {
            _presets = new Dictionary<string, PresetData>();
            if (!File.Exists(_presetsPath)) return;
            try
            {
                string json = File.ReadAllText(_presetsPath, Encoding.UTF8);
                json = json.Trim();
                if (!json.StartsWith("{") || !json.EndsWith("}")) return;
                json = json.Substring(1, json.Length - 2).Trim();

                int pos = 0;
                while (pos < json.Length)
                {
                    string name = ReadJsonString(json, ref pos);
                    if (name == null) break;
                    SkipColon(json, ref pos);
                    PresetData p = ReadPresetObject(json, ref pos);
                    if (p != null) _presets[name] = p;
                    SkipComma(json, ref pos);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("presets load failed: " + ex.Message);
                _presets = new Dictionary<string, PresetData>();
            }
        }

        private void SavePresetsFile()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                bool first = true;
                foreach (var kv in _presets)
                {
                    if (!first) sb.AppendLine(",");
                    first = false;
                    PresetData p = kv.Value;
                    sb.Append($"  \"{Esc(kv.Key)}\": {{");
                    sb.Append($"\"FadeFrames\":{p.FadeFrames},");
                    sb.Append($"\"MaxSlots\":{p.MaxSlots},");
                    sb.Append($"\"CaptureInterval\":{p.CaptureInterval},");
                    sb.Append($"\"TintR\":{p.TintR:0.####},");
                    sb.Append($"\"TintG\":{p.TintG:0.####},");
                    sb.Append($"\"TintB\":{p.TintB:0.####},");
                    sb.Append($"\"TintA\":{p.TintA:0.####},");
                    sb.Append($"\"AlphaScale\":{p.AlphaScale:0.####},");
                    sb.Append($"\"FadeCurve\":\"{Esc(p.FadeCurve)}\",");
                    sb.Append($"\"FrontOfCharacter\":{(p.FrontOfCharacter ? "true" : "false")},");
                    sb.Append($"\"GlowThreshold\":{p.GlowThreshold:0.##},");
                    sb.Append($"\"GlowStrength\":{p.GlowStrength:0.##},");
                    sb.Append($"\"GlowBlurPercent\":{p.GlowBlurPercent:0.##}");
                    sb.Append("}");
                }
                sb.AppendLine();
                sb.Append("}");
                File.WriteAllText(_presetsPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Logger.LogWarning("presets save failed: " + ex.Message);
            }
        }

        // ---- JSON ヘルパ ----

        private static string Esc(string s) => s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

        private static string ReadJsonString(string json, ref int pos)
        {
            SkipWs(json, ref pos);
            if (pos >= json.Length || json[pos] != '"') return null;
            pos++;
            var sb = new StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && pos < json.Length) { sb.Append(json[pos++]); continue; }
                sb.Append(c);
            }
            return null;
        }

        private static void SkipWs(string json, ref int pos) { while (pos < json.Length && json[pos] <= ' ') pos++; }
        private static void SkipColon(string json, ref int pos) { SkipWs(json, ref pos); if (pos < json.Length && json[pos] == ':') pos++; }
        private static void SkipComma(string json, ref int pos) { SkipWs(json, ref pos); if (pos < json.Length && json[pos] == ',') pos++; }

        private static PresetData ReadPresetObject(string json, ref int pos)
        {
            SkipWs(json, ref pos);
            if (pos >= json.Length || json[pos] != '{') return null;
            pos++;
            var p = new PresetData();
            while (pos < json.Length)
            {
                SkipWs(json, ref pos);
                if (pos < json.Length && json[pos] == '}') { pos++; break; }
                string key = ReadJsonString(json, ref pos);
                if (key == null) break;
                SkipColon(json, ref pos);
                SkipWs(json, ref pos);
                if (json[pos] == '"')
                {
                    string val = ReadJsonString(json, ref pos);
                    if (key == "FadeCurve") p.FadeCurve = val;
                }
                else
                {
                    int end = pos;
                    while (end < json.Length && json[end] != ',' && json[end] != '}') end++;
                    string val = json.Substring(pos, end - pos).Trim();
                    pos = end;
                    switch (key)
                    {
                        case "FadeFrames":       if (int.TryParse(val, out int fi))    p.FadeFrames = fi; break;
                        case "MaxSlots":         if (int.TryParse(val, out int ms))    p.MaxSlots = ms; break;
                        case "CaptureInterval":  if (int.TryParse(val, out int ci))    p.CaptureInterval = ci; break;
                        case "TintR":            if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tr)) p.TintR = tr; break;
                        case "TintG":            if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tg)) p.TintG = tg; break;
                        case "TintB":            if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float tb)) p.TintB = tb; break;
                        case "TintA":            if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ta)) p.TintA = ta; break;
                        case "AlphaScale":       if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float as_)) p.AlphaScale = as_; break;
                        case "FrontOfCharacter": p.FrontOfCharacter = val == "true"; break;
                        case "GlowThreshold":    if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float gth)) p.GlowThreshold = gth; break;
                        case "GlowStrength":     if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float gstr)) p.GlowStrength = gstr; break;
                        case "GlowBlurPercent":  if (float.TryParse(val, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float gblur)) p.GlowBlurPercent = gblur; break;
                    }
                }
                SkipComma(json, ref pos);
            }
            return p;
        }
    }
}
