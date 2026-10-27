using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameGlow
{
    public sealed partial class Plugin
    {
        private void SetupConfig()
        {
            const string cat1 = "01. General";
            const string cat2 = "02. Capture";
            const string cat3 = "03. Glow";
            const string cat4 = "04. Overlay";
            const string cat5 = "05. Source Camera";

            _cfgEnabled = Bind(
                cat1, "Enabled", true,
                "グロープラグイン有効/無効",
                order: 100);

            _cfgVerboseLog = Bind(
                cat1, "Verbose Log", false,
                "詳細ログ出力",
                order: 90);

            _cfgUseScreenSize = Bind(
                cat2, "Use Screen Size", true,
                "キャプチャ解像度に画面サイズを使う",
                order: 100);

            _cfgCaptureWidth = Bind(
                cat2,
                "Capture Width",
                1280,
                new ConfigDescription(
                    "未使用。幅は Capture Height と画面のアスペクトから自動算出します",
                    new AcceptableValueRange<int>(16, 8192),
                    BuildConfigManagerAttributes(order: 90)
                ),
                order: 90
            );

            _cfgCaptureHeight = Bind(
                cat2,
                "Capture Height",
                720,
                new ConfigDescription(
                    "Use Screen Size=false のときの高さ。幅は画面のアスペクトに合わせて自動で決まります",
                    new AcceptableValueRange<int>(16, 8192),
                    BuildConfigManagerAttributes(order: 80)
                ),
                order: 80
            );

            _cfgCharaLayer = Bind(
                cat2, "Character Layer Name", "Chara",
                "発光キャプチャ対象レイヤー名",
                order: 70);

            _cfgGlowThreshold = Bind(
                cat3,
                "Glow Threshold",
                0.5f,
                new ConfigDescription(
                    "Bloom抽出閾値",
                    new AcceptableValueRange<float>(0f, 5f),
                    BuildConfigManagerAttributes(order: 100)
                ),
                order: 100
            );

            _cfgGlowStrength = Bind(
                cat3,
                "Glow Strength",
                3f,
                new ConfigDescription(
                    "Bloom強度",
                    new AcceptableValueRange<float>(0f, 10f),
                    BuildConfigManagerAttributes(order: 90)
                ),
                order: 90
            );

            _cfgGlowBlurPercent = Bind(
                cat3,
                "Glow Blur Percent",
                30f,
                new ConfigDescription(
                    "ぼかし量。0でほぼ無効、100で強い拡散",
                    new AcceptableValueRange<float>(0f, 100f),
                    BuildConfigManagerAttributes(order: 80)
                ),
                order: 80
            );

            _cfgTintR = Bind(
                cat4,
                "Tint R",
                0.5f,
                new ConfigDescription(
                    "発光色R",
                    new AcceptableValueRange<float>(0f, 1f),
                    BuildConfigManagerAttributes(order: 100)
                ),
                order: 100
            );

            _cfgTintG = Bind(
                cat4,
                "Tint G",
                0.5f,
                new ConfigDescription(
                    "発光色G",
                    new AcceptableValueRange<float>(0f, 1f),
                    BuildConfigManagerAttributes(order: 90)
                ),
                order: 90
            );

            _cfgTintB = Bind(
                cat4,
                "Tint B",
                0.5f,
                new ConfigDescription(
                    "発光色B",
                    new AcceptableValueRange<float>(0f, 1f),
                    BuildConfigManagerAttributes(order: 80)
                ),
                order: 80
            );

            _cfgTintA = Bind(
                cat4,
                "Tint A",
                1f,
                new ConfigDescription(
                    "色アルファ",
                    new AcceptableValueRange<float>(0f, 1f),
                    BuildConfigManagerAttributes(order: 70)
                ),
                order: 70
            );

            _cfgOverlayAlpha = Bind(
                cat4,
                "Overlay Alpha",
                1f,
                new ConfigDescription(
                    "最終合成アルファ",
                    new AcceptableValueRange<float>(0f, 1f),
                    BuildConfigManagerAttributes(order: 60)
                ),
                order: 60
            );

            _cfgPreferCameraMain = Bind(
                cat5, "Prefer Camera.main", true,
                "Camera.main を優先",
                order: 100);

            _cfgCameraNameFilter = Bind(
                cat5, "Camera Name Filter", "",
                "部分一致するカメラ名を優先",
                order: 90);

            _cfgCameraFallbackIndex = Bind(
                cat5,
                "Camera Fallback Index",
                0,
                new ConfigDescription(
                    "候補カメラのフォールバック番号",
                    new AcceptableValueRange<int>(0, 64),
                    BuildConfigManagerAttributes(order: 80)
                ),
                order: 80
            );

            Config.SettingChanged += (_, __) =>
            {
                ApplyConfig();
                if (!_suppressJsonWrite)
                    SaveSettingsJsonFromConfig();
            };
        }

        private ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description, int order = 0)
        {
            return Config.Bind(
                section,
                key,
                defaultValue,
                new ConfigDescription(description, null, BuildConfigManagerAttributes(order)));
        }

        private ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description, int order = 0)
        {
            List<object> tags = new List<object>();
            if (description.Tags != null)
                tags.AddRange(description.Tags);

            object attrs = BuildConfigManagerAttributes(order);
            if (attrs != null)
                tags.Add(attrs);

            return Config.Bind(
                section,
                key,
                defaultValue,
                new ConfigDescription(description.Description, description.AcceptableValues, tags.ToArray()));
        }

        private object BuildConfigManagerAttributes(int order = 0)
        {
            try
            {
                Type attrType = Type.GetType("ConfigurationManager.ConfigurationManagerAttributes, ConfigurationManager");
                if (attrType == null)
                    return null;

                object obj = Activator.CreateInstance(attrType);

                PropertyInfo orderProp = attrType.GetProperty("Order");
                if (orderProp != null && orderProp.CanWrite)
                    orderProp.SetValue(obj, order, null);

                return obj;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>画面の横÷縦。極端な値でも破綻しないよう範囲を絞る。</summary>
        private static float CurrentScreenAspect()
        {
            int w = Mathf.Max(1, Screen.width);
            int h = Mathf.Max(1, Screen.height);
            return Mathf.Clamp((float)w / h, 0.2f, 5f);
        }

        private void ApplyConfig()
        {
            string charaLayerName = _cfgCharaLayer.Value ?? "Chara";
            _characterMask = LayerMask.GetMask(charaLayerName);
            // 名前が実在しないとマスクが0になり、キャプチャカメラは何も撮らない（＝真っ黒で無音の失敗）。
            LogPlugin(
                _characterMask == 0 ? "WARN" : "INFO",
                "chara layer: name=" + charaLayerName
                    + " index=" + LayerMask.NameToLayer(charaLayerName)
                    + " mask=" + _characterMask);

            int newW;
            int newH;

            if (_cfgUseScreenSize.Value)
            {
                newW = Screen.width;
                newH = Screen.height;
            }
            else
            {
                // 高さを品質のつまみとして使い、幅は画面のアスペクトから決める。
                // 幅を固定値にすると、縦画面やウルトラワイドで撮った絵が潰れ、
                // それを画面いっぱいへ引き伸ばすため光の幅がキャラの輪郭とずれる。
                newH = Mathf.Max(16, _cfgCaptureHeight.Value);
                newW = Mathf.RoundToInt(newH * CurrentScreenAspect());
            }

            newW = Mathf.Max(16, newW);
            newH = Mathf.Max(16, newH);

            bool needRebuild =
                _captureRt == null ||
                _rtWidth != newW ||
                _rtHeight != newH;

            if (needRebuild)
            {
                ReleaseCaptureRt();
                _captureRt = CreateRT(newW, newH);
                _rtWidth = newW;
                _rtHeight = newH;

                if (_cfgVerboseLog.Value)
                    Logger.LogInfo($"capture RT rebuilt: {newW}x{newH}");
            }

            if (_cameraRoot == null)
                SetupCaptureCamera();

            bool pipelineReady = _captureBloom != null && _capturePostProcessLayer != null && _capturePostProcessVolume != null;
            ApplyCaptureGlowSettings(pipelineReady);
        }
    }
}
