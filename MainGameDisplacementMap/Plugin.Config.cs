using System;
using System.IO;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameDisplacementMap
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgVerboseLog;
        private ConfigEntry<bool> _cfgDiagnosticOverlay;
        private ConfigEntry<bool> _cfgDiagnosticRTPreview;
        private ConfigEntry<bool> _cfgDiagnosticOnGUI;
        private ConfigEntry<bool> _cfgCaptureForceColor;
        private ConfigEntry<bool> _cfgCaptureFullLayers;
        private ConfigEntry<int> _cfgDiagnosticCameraIndex;
        private ConfigEntry<bool> _cfgFullScreen;   // 画面全体に波紋を適用するか（これが重要）
        private ConfigEntry<bool> _cfgReuseSourceFrame;

        private ConfigEntry<bool> _cfgUseScreenSize;
        private ConfigEntry<int> _cfgCapW;
        private ConfigEntry<int> _cfgCapH;
        private ConfigEntry<string> _cfgCharaLayer;
        private ConfigEntry<string> _cfgFullSceneExcludeLayer;

        private ConfigEntry<bool> _cfgPreferMain;
        private ConfigEntry<string> _cfgCameraFilter;
        private ConfigEntry<int> _cfgCameraIndex;

        private ConfigEntry<float> _cfgOpacity;
        private ConfigEntry<float> _cfgStrength;
        private ConfigEntry<float> _cfgCenterX;
        private ConfigEntry<float> _cfgCenterY;
        private ConfigEntry<float> _cfgRadius;
        private ConfigEntry<float> _cfgInner;
        private ConfigEntry<float> _cfgFalloff;
        private ConfigEntry<float> _cfgTimeScale;
        private ConfigEntry<float> _cfgPulse;

        // Map specific (Phase 2 will use these heavily)
        private ConfigEntry<int> _cfgMapResolution;
        private ConfigEntry<int> _cfgMapWaveCount;
        private ConfigEntry<float> _cfgMapFrequency;
        private ConfigEntry<float> _cfgMapPropagationSpeed;
        private ConfigEntry<float> _cfgMapDamping;
        private ConfigEntry<float> _cfgMapNoise;
        private ConfigEntry<float> _cfgMapScrollX;
        private ConfigEntry<float> _cfgMapScrollY;

        private ConfigEntry<int> _cfgGridX;
        private ConfigEntry<int> _cfgGridY;

        private string _jsonPath;
        private string _logPath;
        private bool _loadingJson;

        private SimpleFileLogger _fileLog;

        private void BindConfig()
        {
            const string g = "00.一般";
            const string c = "01.キャプチャ";
            const string cam = "02.カメラ";
            const string d = "10.ディスプレイスメント";
            const string m = "20.マップ生成";
            const string grid = "30.グリッド";

            _cfgEnabled = Config.Bind(g, "有効", true, "プラグイン全体の有効/無効 (最優先)");
            _cfgVerboseLog = Config.Bind(g, "詳細ログ", false, "診断ログをファイルに出力");
            _cfgDiagnosticOverlay = Config.Bind(g, "診断表示モード", false, "描画テスト用。trueの間は通常のディスプレイスメントの代わりに画面全体に半透明の色を強制表示して、描画が最終画面に出ているかを確認する");
            _cfgDiagnosticRTPreview = Config.Bind(g, "診断RTプレビュー", false, "RT内容確認用。trueの間はメッシュ変位を使わず、_rtをそのまま画面全体に描画してキャプチャ内容とアスペクトを確認する");
            _cfgDiagnosticOnGUI = Config.Bind(g, "診断OnGUI表示", false, "OnGUI経路確認用。trueの間は左上にRTのプレビューを表示して、プラグインの描画がGUI最終経路に出るかを確認する");
            _cfgCaptureForceColor = Config.Bind(g, "キャプチャ前強制色塗り", false, "キャプチャ診断用。Render前にRTをマゼンタで塗る。OnGUIで色が見えれば表示経路は正常");
            _cfgCaptureFullLayers = Config.Bind(g, "キャプチャ時全レイヤー", false, "キャプチャ診断用。full時のみcullingMaskを-1にして全レイヤー描画を試す");
            _cfgDiagnosticCameraIndex = Config.Bind(g, "診断カメラ選択Index", 0, new ConfigDescription("", new AcceptableValueRange<int>(0, 32)));
            _cfgFullScreen = Config.Bind(c, "全画面キャプチャ", true, "背景も含めて画面全体に波紋を適用（推奨）");
            _cfgReuseSourceFrame = Config.Bind(c, "描画済みフレーム再利用", true, "条件が合うカメラでは描画済みの画面をコピーし、全画面の再描画を省く");

            _cfgUseScreenSize = Config.Bind(c, "画面解像度を使用", true, "画面解像度を使用");
            _cfgCapW = Config.Bind(c, "キャプチャ幅", 960, new ConfigDescription("", new AcceptableValueRange<int>(64, 4096)));
            _cfgCapH = Config.Bind(c, "キャプチャ高さ", 540, new ConfigDescription("", new AcceptableValueRange<int>(64, 4096)));
            _cfgCharaLayer = Config.Bind(c, "キャラレイヤー名", "Chara", "Chara専用モード時のレイヤー");
            _cfgFullSceneExcludeLayer = Config.Bind(c, "全画面除外レイヤー", "UI", "全画面モードで除外するレイヤー");

            _cfgPreferMain = Config.Bind(cam, "Camera.main優先", true, "Camera.main 優先");
            _cfgCameraFilter = Config.Bind(cam, "カメラ名フィルタ", "", "カメラ名フィルタ");
            _cfgCameraIndex = Config.Bind(cam, "カメラ候補フォールバック", 0, new ConfigDescription("", new AcceptableValueRange<int>(0, 32)));

            _cfgOpacity = Config.Bind(d, "不透明度", 1f, new ConfigDescription("オーバーレイ不透明度", new AcceptableValueRange<float>(0f, 1f)));
            _cfgStrength = Config.Bind(d, "強度", 0.35f, new ConfigDescription("変位強度（Hシーンで目立つようにさらに上げた）", new AcceptableValueRange<float>(-0.5f, 0.5f)));
            _cfgCenterX = Config.Bind(d, "中心X", 0.5f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            _cfgCenterY = Config.Bind(d, "中心Y", 0.5f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            _cfgRadius = Config.Bind(d, "半径", 1.1f, new ConfigDescription("", new AcceptableValueRange<float>(0.01f, 3f)));
            _cfgInner = Config.Bind(d, "内側半径", 0f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 3f)));
            _cfgFalloff = Config.Bind(d, "減衰", 1.8f, new ConfigDescription("", new AcceptableValueRange<float>(0.1f, 16f)));
            _cfgTimeScale = Config.Bind(d, "時間スケール", 1.0f, new ConfigDescription("", new AcceptableValueRange<float>(-8f, 8f)));
            _cfgPulse = Config.Bind(d, "パルス", 1f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 10f)));

            _cfgMapResolution = Config.Bind(m, "マップ解像度", 192, new ConfigDescription("ディスプレイスメントマップ解像度", new AcceptableValueRange<int>(32, 512)));
            _cfgMapWaveCount = Config.Bind(m, "波の数", 3, new ConfigDescription("", new AcceptableValueRange<int>(1, 8)));
            _cfgMapFrequency = Config.Bind(m, "基本周波数", 6.5f, new ConfigDescription("", new AcceptableValueRange<float>(0.5f, 32f)));
            _cfgMapPropagationSpeed = Config.Bind(m, "伝播速度", 1.6f, new ConfigDescription("", new AcceptableValueRange<float>(-10f, 10f)));
            _cfgMapDamping = Config.Bind(m, "減衰率", 1.4f, new ConfigDescription("", new AcceptableValueRange<float>(0.1f, 8f)));
            _cfgMapNoise = Config.Bind(m, "ノイズ量", 0.15f, new ConfigDescription("", new AcceptableValueRange<float>(0f, 1f)));
            _cfgMapScrollX = Config.Bind(m, "スクロール速度X", 0f, new ConfigDescription("", new AcceptableValueRange<float>(-2f, 2f)));
            _cfgMapScrollY = Config.Bind(m, "スクロール速度Y", 0.2f, new ConfigDescription("", new AcceptableValueRange<float>(-2f, 2f)));

            _cfgGridX = Config.Bind(grid, "グリッド横", 64, new ConfigDescription("メッシュ横分割数", new AcceptableValueRange<int>(4, 256)));
            _cfgGridY = Config.Bind(grid, "グリッド縦", 36, new ConfigDescription("メッシュ縦分割数", new AcceptableValueRange<int>(4, 256)));

            Config.SettingChanged += (_, __) =>
            {
                if (!_loadingJson)
                {
                    if (_fileLog != null) _fileLog.Enabled = _cfgVerboseLog.Value;
                    SaveJson();
                    if (_cfgEnabled != null && !_cfgEnabled.Value)
                    {
                        StopRuntime();
                    }
                }
            };
        }

        private void ApplyConfig()
        {
            if (_cfgEnabled != null && !_cfgEnabled.Value)
            {
                StopRuntime();
                return;
            }
            // 画面サイズなどは LateUpdate 側で動的対応
        }

        private void SaveJson()
        {
            if (_loadingJson || string.IsNullOrEmpty(_jsonPath)) return;
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("{");
                J(sb, "有効", _cfgEnabled.Value);
                J(sb, "詳細ログ", _cfgVerboseLog.Value);
                J(sb, "診断表示モード", _cfgDiagnosticOverlay.Value);
                J(sb, "診断RTプレビュー", _cfgDiagnosticRTPreview.Value);
                J(sb, "診断OnGUI表示", _cfgDiagnosticOnGUI.Value);
                J(sb, "キャプチャ前強制色塗り", _cfgCaptureForceColor.Value);
                J(sb, "キャプチャ時全レイヤー", _cfgCaptureFullLayers.Value);
                J(sb, "診断カメラ選択Index", _cfgDiagnosticCameraIndex.Value);
                J(sb, "全画面キャプチャ", _cfgFullScreen.Value);
                J(sb, "描画済みフレーム再利用", _cfgReuseSourceFrame.Value);
                J(sb, "画面解像度を使用", _cfgUseScreenSize.Value);
                J(sb, "キャプチャ幅", _cfgCapW.Value);
                J(sb, "キャプチャ高さ", _cfgCapH.Value);
                J(sb, "キャラレイヤー名", _cfgCharaLayer.Value);
                J(sb, "全画面除外レイヤー", _cfgFullSceneExcludeLayer.Value);
                J(sb, "Camera.main優先", _cfgPreferMain.Value);
                J(sb, "カメラ名フィルタ", _cfgCameraFilter.Value);
                J(sb, "カメラ候補フォールバック", _cfgCameraIndex.Value);
                J(sb, "不透明度", _cfgOpacity.Value);
                J(sb, "強度", _cfgStrength.Value);
                J(sb, "中心X", _cfgCenterX.Value);
                J(sb, "中心Y", _cfgCenterY.Value);
                J(sb, "半径", _cfgRadius.Value);
                J(sb, "内側半径", _cfgInner.Value);
                J(sb, "減衰", _cfgFalloff.Value);
                J(sb, "時間スケール", _cfgTimeScale.Value);
                J(sb, "パルス", _cfgPulse.Value);
                J(sb, "マップ解像度", _cfgMapResolution.Value);
                J(sb, "波の数", _cfgMapWaveCount.Value);
                J(sb, "基本周波数", _cfgMapFrequency.Value);
                J(sb, "伝播速度", _cfgMapPropagationSpeed.Value);
                J(sb, "減衰率", _cfgMapDamping.Value);
                J(sb, "ノイズ量", _cfgMapNoise.Value);
                J(sb, "スクロール速度X", _cfgMapScrollX.Value);
                J(sb, "スクロール速度Y", _cfgMapScrollY.Value);
                J(sb, "グリッド横", _cfgGridX.Value);
                J(sb, "グリッド縦", _cfgGridY.Value, false);
                sb.AppendLine("}");
                File.WriteAllText(_jsonPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception e)
            {
                Logger.LogWarning("json save failed: " + e.Message);
            }
        }

        private void LoadJson()
        {
            if (!File.Exists(_jsonPath)) return;
            _loadingJson = true;
            try
            {
                string s = File.ReadAllText(_jsonPath, Encoding.UTF8);
                foreach (string p in s.Trim('{', '}', '\r', '\n', ' ').Split(','))
                {
                    int i = p.IndexOf(':');
                    if (i <= 0) continue;
                    string k = Unq(p.Substring(0, i).Trim());
                    string v = Unq(p.Substring(i + 1).Trim());
                    Set(k, v);
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning("json load failed: " + e.Message);
            }
            finally
            {
                _loadingJson = false;
            }
        }

        private void Set(string k, string v)
        {
            switch (k)
            {
                case "有効": _cfgEnabled.Value = B(v, _cfgEnabled.Value); break;
                case "詳細ログ": _cfgVerboseLog.Value = B(v, _cfgVerboseLog.Value); break;
                case "診断表示モード": _cfgDiagnosticOverlay.Value = B(v, _cfgDiagnosticOverlay.Value); break;
                case "診断RTプレビュー": _cfgDiagnosticRTPreview.Value = B(v, _cfgDiagnosticRTPreview.Value); break;
                case "診断OnGUI表示": _cfgDiagnosticOnGUI.Value = B(v, _cfgDiagnosticOnGUI.Value); break;
                case "キャプチャ前強制色塗り": _cfgCaptureForceColor.Value = B(v, _cfgCaptureForceColor.Value); break;
                case "キャプチャ時全レイヤー": _cfgCaptureFullLayers.Value = B(v, _cfgCaptureFullLayers.Value); break;
                case "診断カメラ選択Index": _cfgDiagnosticCameraIndex.Value = I(v, _cfgDiagnosticCameraIndex.Value); break;
                case "全画面キャプチャ": _cfgFullScreen.Value = B(v, _cfgFullScreen.Value); break;
                case "描画済みフレーム再利用": _cfgReuseSourceFrame.Value = B(v, _cfgReuseSourceFrame.Value); break;
                case "画面解像度を使用": _cfgUseScreenSize.Value = B(v, _cfgUseScreenSize.Value); break;
                case "キャプチャ幅": _cfgCapW.Value = I(v, _cfgCapW.Value); break;
                case "キャプチャ高さ": _cfgCapH.Value = I(v, _cfgCapH.Value); break;
                case "キャラレイヤー名": _cfgCharaLayer.Value = v; break;
                case "全画面除外レイヤー": _cfgFullSceneExcludeLayer.Value = v; break;
                case "Camera.main優先": _cfgPreferMain.Value = B(v, _cfgPreferMain.Value); break;
                case "カメラ名フィルタ": _cfgCameraFilter.Value = v; break;
                case "カメラ候補フォールバック": _cfgCameraIndex.Value = I(v, _cfgCameraIndex.Value); break;
                case "不透明度": _cfgOpacity.Value = F(v, _cfgOpacity.Value); break;
                case "強度": _cfgStrength.Value = F(v, _cfgStrength.Value); break;
                case "中心X": _cfgCenterX.Value = F(v, _cfgCenterX.Value); break;
                case "中心Y": _cfgCenterY.Value = F(v, _cfgCenterY.Value); break;
                case "半径": _cfgRadius.Value = F(v, _cfgRadius.Value); break;
                case "内側半径": _cfgInner.Value = F(v, _cfgInner.Value); break;
                case "減衰": _cfgFalloff.Value = F(v, _cfgFalloff.Value); break;
                case "時間スケール": _cfgTimeScale.Value = F(v, _cfgTimeScale.Value); break;
                case "パルス": _cfgPulse.Value = F(v, _cfgPulse.Value); break;
                case "マップ解像度": _cfgMapResolution.Value = I(v, _cfgMapResolution.Value); break;
                case "波の数": _cfgMapWaveCount.Value = I(v, _cfgMapWaveCount.Value); break;
                case "基本周波数": _cfgMapFrequency.Value = F(v, _cfgMapFrequency.Value); break;
                case "伝播速度": _cfgMapPropagationSpeed.Value = F(v, _cfgMapPropagationSpeed.Value); break;
                case "減衰率": _cfgMapDamping.Value = F(v, _cfgMapDamping.Value); break;
                case "ノイズ量": _cfgMapNoise.Value = F(v, _cfgMapNoise.Value); break;
                case "スクロール速度X": _cfgMapScrollX.Value = F(v, _cfgMapScrollX.Value); break;
                case "スクロール速度Y": _cfgMapScrollY.Value = F(v, _cfgMapScrollY.Value); break;
                case "グリッド横": _cfgGridX.Value = I(v, _cfgGridX.Value); break;
                case "グリッド縦": _cfgGridY.Value = I(v, _cfgGridY.Value); break;
            }
        }

        private static void J(StringBuilder b, string k, bool v, bool comma = true) { b.Append("  \"").Append(k).Append("\": ").Append(v ? "true" : "false"); if (comma) b.Append(','); b.AppendLine(); }
        private static void J(StringBuilder b, string k, int v, bool comma = true) { b.Append("  \"").Append(k).Append("\": ").Append(v); if (comma) b.Append(','); b.AppendLine(); }
        private static void J(StringBuilder b, string k, float v, bool comma = true) { b.Append("  \"").Append(k).Append("\": ").Append(v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)); if (comma) b.Append(','); b.AppendLine(); }
        private static void J(StringBuilder b, string k, string v, bool comma = true) { b.Append("  \"").Append(k).Append("\": \"").Append((v ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"'); if (comma) b.Append(','); b.AppendLine(); }

        private static string Unq(string s) { s = s.Trim(); if (s.StartsWith("\"") && s.EndsWith("\"")) s = s.Substring(1, s.Length - 2); return s.Replace("\\\"", "\"").Replace("\\\\", "\\"); }
        private static bool B(string s, bool d) { return string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) ? true : string.Equals(s, "false", StringComparison.OrdinalIgnoreCase) ? false : d; }
        private static int I(string s, int d) { return int.TryParse(s, out int v) ? v : d; }
        private static float F(string s, float d) { return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : d; }
    }
}
