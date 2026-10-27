using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameCharacterAfterimage
{
    public sealed partial class Plugin
    {
        private const string UiLanguageJapanese = "ja";

        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgVerboseLog;

        private ConfigEntry<int> _cfgFadeFrames;
        private ConfigEntry<int> _cfgMaxAfterimageSlots;
        private ConfigEntry<int> _cfgCaptureIntervalFrames;
        private ConfigEntry<bool> _cfgUseScreenSize;
        private ConfigEntry<int> _cfgCaptureWidth;
        private ConfigEntry<int> _cfgCaptureHeight;

        private ConfigEntry<string> _cfgCharacterLayerNames;
        private ConfigEntry<int> _cfgMiddleCameraDepthOffsetMilli;
        private ConfigEntry<int> _cfgTopCharacterCameraDepthOffsetMilli;

        private ConfigEntry<string> _cfgOverlayShaderName;
        private ConfigEntry<float> _cfgOverlayTintR;
        private ConfigEntry<float> _cfgOverlayTintG;
        private ConfigEntry<float> _cfgOverlayTintB;
        private ConfigEntry<float> _cfgOverlayTintA;
        private ConfigEntry<float> _cfgAfterimageAlphaScale;
        private ConfigEntry<bool> _cfgOverlayInFrontOfCharacter;
        private ConfigEntry<float> _cfgFrontOverlayTargetTotalAlpha;
        private ConfigEntry<bool> _cfgDrawNewestLast;

        private ConfigEntry<bool> _cfgPreferCameraMain;
        private ConfigEntry<string> _cfgSourceCameraNameContains;
        private ConfigEntry<int> _cfgSourceCameraFallbackIndex;

        private ConfigEntry<float> _cfgStatusLogIntervalSec;

        private void SetupConfigBindings()
        {
            _settings.UiLanguage = UiLanguageJapanese;
            string layerCsv = ToLayerCsv(_settings.CharacterLayerNames);
            string catGeneral = Category("01.一般", "01.General");
            _cfgEnabled = Config.Bind(catGeneral, KeyLabel("有効", "Enabled"), _settings.Enabled, L("機能の有効/無効", "Enable or disable feature"));
            _cfgVerboseLog = Config.Bind(catGeneral, KeyLabel("詳細ログ", "VerboseLog"), _settings.VerboseLog, L("詳細ログを出力する", "Enable verbose logging"));

            string catCapture = Category("02.キャプチャ", "02.Capture");
            _cfgFadeFrames = Config.Bind(catCapture, KeyLabel("残像寿命フレーム", "FadeFrames"), _settings.FadeFrames, L("残像が消えるまでのフレーム数", "Frames until afterimage fades out"));
            _cfgMaxAfterimageSlots = Config.Bind(catCapture, KeyLabel("同時残像数", "MaxAfterimageSlots"), _settings.MaxAfterimageSlots, L("同時に保持する残像スロット数", "Max simultaneous afterimage slots"));
            _cfgCaptureIntervalFrames = Config.Bind(catCapture, KeyLabel("キャプチャ間隔フレーム", "CaptureIntervalFrames"), _settings.CaptureIntervalFrames, L("何フレームごとにキャプチャするか(1=毎フレーム)", "Capture interval in frames (1=every frame)"));
            _cfgUseScreenSize = Config.Bind(catCapture, KeyLabel("画面解像度を使う", "UseScreenSize"), _settings.UseScreenSize, L("キャプチャサイズに画面解像度を使う", "Use screen resolution as capture size"));
            _cfgCaptureWidth = Config.Bind(catCapture, KeyLabel("キャプチャ幅", "CaptureWidth"), _settings.CaptureWidth, L("UseScreenSize=false時のキャプチャ幅", "Capture width when UseScreenSize=false"));
            _cfgCaptureHeight = Config.Bind(catCapture, KeyLabel("キャプチャ高さ", "CaptureHeight"), _settings.CaptureHeight, L("UseScreenSize=false時のキャプチャ高さ", "Capture height when UseScreenSize=false"));

            string catLayer = Category("03.レイヤー", "03.Layer");
            _cfgCharacterLayerNames = Config.Bind(catLayer, KeyLabel("キャラレイヤーCSV", "CharacterLayerNamesCsv"), layerCsv, L("キャラとして扱うレイヤー名(カンマ区切り) 例: Chara", "Layer names treated as character (CSV) e.g. Chara"));
            _cfgMiddleCameraDepthOffsetMilli = Config.Bind(catLayer, KeyLabel("中間カメラ深度オフセット", "MiddleCameraDepthOffsetMilli"), _settings.MiddleCameraDepthOffsetMilli, L("中間カメラのdepthオフセット(1/1000)", "Middle camera depth offset (1/1000)"));
            _cfgTopCharacterCameraDepthOffsetMilli = Config.Bind(catLayer, KeyLabel("前面カメラ深度オフセット", "TopCharacterCameraDepthOffsetMilli"), _settings.TopCharacterCameraDepthOffsetMilli, L("最終キャラカメラのdepthオフセット(1/1000)", "Top character camera depth offset (1/1000)"));

            string catOverlay = Category("04.オーバーレイ", "04.Overlay");
            _cfgOverlayShaderName = Config.Bind(catOverlay, KeyLabel("シェーダー名", "OverlayShaderName"), _settings.OverlayShaderName, L("残像描画に使うシェーダー名", "Shader name used for afterimage drawing"));
            _cfgOverlayTintR = Config.Bind(catOverlay, KeyLabel("色R", "OverlayTintR"), _settings.OverlayTintR, L("残像色 R (0..1)", "Afterimage tint R (0..1)"));
            _cfgOverlayTintG = Config.Bind(catOverlay, KeyLabel("色G", "OverlayTintG"), _settings.OverlayTintG, L("残像色 G (0..1)", "Afterimage tint G (0..1)"));
            _cfgOverlayTintB = Config.Bind(catOverlay, KeyLabel("色B", "OverlayTintB"), _settings.OverlayTintB, L("残像色 B (0..1)", "Afterimage tint B (0..1)"));
            _cfgOverlayTintA = Config.Bind(catOverlay, KeyLabel("色A", "OverlayTintA"), _settings.OverlayTintA, L("残像色 A (0..1)", "Afterimage tint A (0..1)"));
            _cfgAfterimageAlphaScale = Config.Bind(catOverlay, KeyLabel("残像アルファ倍率", "AfterimageAlphaScale"), _settings.AfterimageAlphaScale, L("残像の全体濃度スケール(0..1)", "Global afterimage alpha scale (0..1)"));
            _cfgOverlayInFrontOfCharacter = Config.Bind(catOverlay, KeyLabel("前面表示", "OverlayInFrontOfCharacter"), _settings.OverlayInFrontOfCharacter, L("true: 残像をキャラ前面 / false: キャラ背面", "true: in front of character / false: behind character"));
            _cfgFrontOverlayTargetTotalAlpha = Config.Bind(catOverlay, KeyLabel("前面時の目標合計アルファ", "FrontOverlayTargetTotalAlpha"), _settings.FrontOverlayTargetTotalAlpha, L("前面表示時の目標合計アルファ(自動補正の強さ)", "Target total alpha for front overlay (auto compensation strength)"));
            _cfgDrawNewestLast = Config.Bind(catOverlay, KeyLabel("最新描画優先(互換)", "DrawNewestLast"), _settings.DrawNewestLast, L("互換項目(通常は変更不要)", "Compatibility option (normally keep default)"));

            string catSource = Category("05.元カメラ", "05.SourceCamera");
            _cfgPreferCameraMain = Config.Bind(catSource, KeyLabel("Camera.main優先", "PreferCameraMain"), _settings.PreferCameraMain, L("Camera.mainを優先する", "Prefer Camera.main"));
            _cfgSourceCameraNameContains = Config.Bind(catSource, KeyLabel("カメラ名フィルタ", "SourceCameraNameContains"), _settings.SourceCameraNameContains, L("カメラ名の部分一致フィルタ(空なら無効)", "Partial camera-name filter (empty=disabled)"));
            _cfgSourceCameraFallbackIndex = Config.Bind(catSource, KeyLabel("カメラ候補フォールバック", "SourceCameraFallbackIndex"), _settings.SourceCameraFallbackIndex, L("候補カメラのフォールバックインデックス", "Fallback camera index from candidates"));

            string catDiag = Category("06.診断", "06.Diagnostics");
            _cfgStatusLogIntervalSec = Config.Bind(catDiag, KeyLabel("状態ログ間隔秒", "StatusLogIntervalSec"), _settings.StatusLogIntervalSec, L("状態ログの出力間隔(秒) 0で無効", "Status log interval in seconds (0=disabled)"));

            HookSettingChanged(_cfgEnabled);
            HookSettingChanged(_cfgVerboseLog);
            HookSettingChanged(_cfgFadeFrames);
            HookSettingChanged(_cfgMaxAfterimageSlots);
            HookSettingChanged(_cfgCaptureIntervalFrames);
            HookSettingChanged(_cfgUseScreenSize);
            HookSettingChanged(_cfgCaptureWidth);
            HookSettingChanged(_cfgCaptureHeight);
            HookSettingChanged(_cfgCharacterLayerNames);
            HookSettingChanged(_cfgMiddleCameraDepthOffsetMilli);
            HookSettingChanged(_cfgTopCharacterCameraDepthOffsetMilli);
            HookSettingChanged(_cfgOverlayShaderName);
            HookSettingChanged(_cfgOverlayTintR);
            HookSettingChanged(_cfgOverlayTintG);
            HookSettingChanged(_cfgOverlayTintB);
            HookSettingChanged(_cfgOverlayTintA);
            HookSettingChanged(_cfgAfterimageAlphaScale);
            HookSettingChanged(_cfgOverlayInFrontOfCharacter);
            HookSettingChanged(_cfgFrontOverlayTargetTotalAlpha);
            HookSettingChanged(_cfgDrawNewestLast);
            HookSettingChanged(_cfgPreferCameraMain);
            HookSettingChanged(_cfgSourceCameraNameContains);
            HookSettingChanged(_cfgSourceCameraFallbackIndex);
            HookSettingChanged(_cfgStatusLogIntervalSec);

            ApplyConfigToSettings("startup");
        }

        private void HookSettingChanged<T>(ConfigEntry<T> entry)
        {
            entry.SettingChanged += OnAnyConfigChanged;
        }

        private void OnAnyConfigChanged(object sender, EventArgs args)
        {
            ApplyConfigToSettings("config changed");
            SaveSettings(createBackup: false);
            if (_pipeline != null)
            {
                _pipeline.UpdateSettings(_settings);
            }
            _nextCameraResolveTime = 0f;
        }

        private void ApplyConfigToSettings(string reason)
        {
            if (_settings == null)
            {
                _settings = new PluginSettings();
            }

            _settings.UiLanguage = UiLanguageJapanese;
            _settings.Enabled = _cfgEnabled.Value;
            _settings.VerboseLog = _cfgVerboseLog.Value;

            _settings.FadeFrames = _cfgFadeFrames.Value;
            _settings.MaxAfterimageSlots = _cfgMaxAfterimageSlots.Value;
            _settings.CaptureIntervalFrames = _cfgCaptureIntervalFrames.Value;
            _settings.UseScreenSize = _cfgUseScreenSize.Value;
            _settings.CaptureWidth = _cfgCaptureWidth.Value;
            _settings.CaptureHeight = _cfgCaptureHeight.Value;

            _settings.CharacterLayerNames = ParseLayerNamesCsv(_cfgCharacterLayerNames.Value);
            _settings.MiddleCameraDepthOffsetMilli = _cfgMiddleCameraDepthOffsetMilli.Value;
            _settings.TopCharacterCameraDepthOffsetMilli = _cfgTopCharacterCameraDepthOffsetMilli.Value;

            _settings.OverlayShaderName = _cfgOverlayShaderName.Value;
            _settings.OverlayTintR = _cfgOverlayTintR.Value;
            _settings.OverlayTintG = _cfgOverlayTintG.Value;
            _settings.OverlayTintB = _cfgOverlayTintB.Value;
            _settings.OverlayTintA = _cfgOverlayTintA.Value;
            _settings.AfterimageAlphaScale = _cfgAfterimageAlphaScale.Value;
            _settings.OverlayInFrontOfCharacter = _cfgOverlayInFrontOfCharacter.Value;
            _settings.FrontOverlayTargetTotalAlpha = _cfgFrontOverlayTargetTotalAlpha.Value;
            _settings.DrawNewestLast = _cfgDrawNewestLast.Value;

            _settings.PreferCameraMain = _cfgPreferCameraMain.Value;
            _settings.SourceCameraNameContains = _cfgSourceCameraNameContains.Value ?? string.Empty;
            _settings.SourceCameraFallbackIndex = _cfgSourceCameraFallbackIndex.Value;
            _settings.StatusLogIntervalSec = _cfgStatusLogIntervalSec.Value;

            ClampSettings();
            LogDebug("config applied: " + reason);
        }

        private ConfigDescription CreateButtonDescription(
            string japaneseDescription,
            string englishDescription,
            Action<ConfigEntryBase> drawer,
            int order)
        {
            return new ConfigDescription(
                L(japaneseDescription, englishDescription),
                null,
                new ConfigurationManager.ConfigurationManagerAttributes
                {
                    Order = order,
                    HideDefaultButton = true,
                    HideSettingName = true,
                    CustomDrawer = drawer
                });
        }

        private string L(string japanese, string english)
        {
            return japanese;
        }

        private string Category(string japanese, string english)
        {
            return L(japanese, english);
        }

        private string KeyLabel(string japanese, string english)
        {
            return L(japanese, english);
        }

        private static string NormalizeUiLanguage(string value)
        {
            return UiLanguageJapanese;
        }

        private static string ToLayerCsv(string[] layers)
        {
            if (layers == null || layers.Length == 0)
            {
                return "Chara";
            }

            var sb = new StringBuilder(64);
            for (int i = 0; i < layers.Length; i++)
            {
                string value = layers[i];
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }
                if (sb.Length > 0)
                {
                    sb.Append(',');
                }
                sb.Append(value.Trim());
            }
            if (sb.Length == 0)
            {
                return "Chara";
            }
            return sb.ToString();
        }

        private static string[] ParseLayerNamesCsv(string csv)
        {
            if (string.IsNullOrWhiteSpace(csv))
            {
                return new[] { "Chara" };
            }

            string[] raw = csv.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<string>(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                string value = raw[i].Trim();
                if (value.Length == 0)
                {
                    continue;
                }
                bool exists = false;
                for (int j = 0; j < list.Count; j++)
                {
                    if (string.Equals(list[j], value, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                {
                    list.Add(value);
                }
            }

            if (list.Count == 0)
            {
                list.Add("Chara");
            }
            return list.ToArray();
        }
    }
}
