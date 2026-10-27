using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private enum WarpLayerPreset
        {
            Default,
            DisplacementThenAfterimage,
            AfterimageThenDisplacement,
            AfterimageOnly,
            DisplacementOnly,
            WhiteOnly,
            Manual
        }

        private enum AfterimageExistingMode
        {
            KeepAdding,
            StopAddingAndDecay,
            FadeExistingFaster,
            ClearImmediately
        }

        // FOV演出のベース値をどう決めるか。
        //   CurrentFov   : ワープ開始瞬間の cam.fieldOfView を baseFov に使う（環境依存）
        //   ConfigBaseFov: FovBaseValue を baseFov に使う（環境非依存・固定演出）
        // どちらのモードでも復元先は常に originalFov（ワープ開始瞬間の値）。
        internal enum FovBaseMode
        {
            CurrentFov,
            ConfigBaseFov
        }

        private enum DisplacementMode
        {
            Radial,
            Ripple,
            Swirl,
            Ring
        }

        // General
        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgVerboseLog;
        private ConfigEntry<int> _cfgOrgasmCountToTrigger;
        private ConfigEntry<KeyboardShortcut> _cfgAstralModeKey;
        private ConfigEntry<bool> _cfgSuppressVoiceInAstral;
        private ConfigEntry<float> _cfgAstralEntryHardStopSeconds;
        private ConfigEntry<bool> _cfgSuppressKsPlugTimePassesInAstral;

        // Warp Timing
        private ConfigEntry<float> _cfgWarpDuration;
        private ConfigEntry<float> _cfgWhiteHoldDuration;
        private ConfigEntry<float> _cfgFadeOutDuration;

        // Mixer / layer
        private ConfigEntry<WarpLayerPreset> _cfgLayerPreset;
        private ConfigEntry<int> _cfgDisplacementLayer;
        private ConfigEntry<int> _cfgAfterimageLayer;
        private ConfigEntry<int> _cfgBloomLayer;
        private ConfigEntry<int> _cfgWhiteCanvasLayer;

        // Effect enabled
        private ConfigEntry<bool> _cfgUseAfterimage;
        private ConfigEntry<bool> _cfgUseDisplacement;
        // VR起動時だけ参照する残像/波紋トグル（通常版とは独立。初期OFF）。
        private ConfigEntry<bool> _cfgVrUseAfterimage;
        private ConfigEntry<bool> _cfgVrUseDisplacement;
        private ConfigEntry<bool> _cfgUseBloom;
        private ConfigEntry<bool> _cfgUseWhiteCanvas;
        private ConfigEntry<bool> _cfgEnableFovWarp;
        private ConfigEntry<bool> _cfgFovForceOverride;

        // Effect Start/End within warp window (0.0~1.0)
        private ConfigEntry<float> _cfgBloomStartT;
        private ConfigEntry<float> _cfgBloomEndT;
        private ConfigEntry<float> _cfgSunShaftsStartT;
        private ConfigEntry<float> _cfgSunShaftsEndT;
        private ConfigEntry<float> _cfgRadialBlurStartT;
        private ConfigEntry<float> _cfgRadialBlurEndT;
        private ConfigEntry<float> _cfgAfterimageStartT;
        private ConfigEntry<float> _cfgAfterimageEndT;
        private ConfigEntry<float> _cfgWhiteCanvasStartT;
        private ConfigEntry<float> _cfgWhiteCanvasEndT;
        private ConfigEntry<float> _cfgFovStartT;
        private ConfigEntry<float> _cfgFovEndT;
        private ConfigEntry<float> _cfgFovFadeStartT;
        private ConfigEntry<float> _cfgFovFadeEndT;

        // Easing
        private ConfigEntry<EasingType> _cfgBloomEasing;
        private ConfigEntry<EasingType> _cfgSunShaftsEasing;
        private ConfigEntry<EasingType> _cfgRadialBlurEasing;
        private ConfigEntry<EasingType> _cfgAfterimageEasing;
        private ConfigEntry<EasingType> _cfgWhiteCanvasEasing;
        private ConfigEntry<EasingType> _cfgFadeOutEasing;
        private ConfigEntry<EasingType> _cfgFovEasing;
        private ConfigEntry<EasingType> _cfgFovFadeEasing;

        // FOV detailed params
        private ConfigEntry<float> _cfgFovStartMultiplier;
        private ConfigEntry<float> _cfgFovEndMultiplier;
        private ConfigEntry<float> _cfgFovFadeStartMultiplier;
        private ConfigEntry<float> _cfgFovFadeEndMultiplier;
        private ConfigEntry<float> _cfgFovHoldMultiplier;
        private ConfigEntry<FovBaseMode> _cfgFovBaseMode;
        private ConfigEntry<float> _cfgFovBaseValue;
        private ConfigEntry<float> _cfgFovMin;
        private ConfigEntry<float> _cfgFovMax;
        private ConfigEntry<float> _cfgFovWarpTarget;

        // Legacy/simple intensity entries used by current Warp code
        private ConfigEntry<float> _cfgBloomMaxIntensity;
        private ConfigEntry<float> _cfgBloomThreshold;
        private ConfigEntry<float> _cfgRadialBlurMaxStrength;
        private ConfigEntry<float> _cfgAfterimageMaxAlpha;

        // Afterimage detailed params
        private ConfigEntry<AfterimageExistingMode> _cfgAfterimageExistingMode;
        private ConfigEntry<float> _cfgAfterimageStartIntensity;
        private ConfigEntry<float> _cfgAfterimageEndIntensity;
        private ConfigEntry<float> _cfgAfterimageStartAlphaScale;
        private ConfigEntry<float> _cfgAfterimageEndAlphaScale;
        private ConfigEntry<int> _cfgAfterimageStartFadeFrames;
        private ConfigEntry<int> _cfgAfterimageEndFadeFrames;
        private ConfigEntry<int> _cfgAfterimageStartCaptureInterval;
        private ConfigEntry<int> _cfgAfterimageEndCaptureInterval;
        private ConfigEntry<float> _cfgAfterimageExistingDecayStartT;
        private ConfigEntry<float> _cfgAfterimageExistingDecayEndT;
        private ConfigEntry<float> _cfgAfterimageExistingStartAlpha;
        private ConfigEntry<float> _cfgAfterimageExistingEndAlpha;

        // Displacement detailed params
        private ConfigEntry<DisplacementMode> _cfgDisplacementMode;
        private ConfigEntry<float> _cfgDisplacementStartIntensity;
        private ConfigEntry<float> _cfgDisplacementEndIntensity;
        private ConfigEntry<float> _cfgDisplacementStartOpacity;
        private ConfigEntry<float> _cfgDisplacementEndOpacity;
        private ConfigEntry<float> _cfgDisplacementStartStrength;
        private ConfigEntry<float> _cfgDisplacementEndStrength;
        private ConfigEntry<float> _cfgDisplacementStartRadius;
        private ConfigEntry<float> _cfgDisplacementEndRadius;
        private ConfigEntry<float> _cfgDisplacementStartInnerRadius;
        private ConfigEntry<float> _cfgDisplacementEndInnerRadius;
        private ConfigEntry<float> _cfgDisplacementCenterX;
        private ConfigEntry<float> _cfgDisplacementCenterY;
        private ConfigEntry<float> _cfgDisplacementStartFalloff;
        private ConfigEntry<float> _cfgDisplacementEndFalloff;

        // Bloom detailed params
        private ConfigEntry<float> _cfgBloomStartIntensity;
        private ConfigEntry<float> _cfgBloomEndIntensity;
        private ConfigEntry<float> _cfgBloomStartThreshold;
        private ConfigEntry<float> _cfgBloomEndThreshold;

        // White canvas detailed params
        private ConfigEntry<float> _cfgWhiteCanvasStartAlpha;
        private ConfigEntry<float> _cfgWhiteCanvasEndAlpha;
        private ConfigEntry<float> _cfgWhiteCanvasStartR;
        private ConfigEntry<float> _cfgWhiteCanvasStartG;
        private ConfigEntry<float> _cfgWhiteCanvasStartB;
        private ConfigEntry<float> _cfgWhiteCanvasEndR;
        private ConfigEntry<float> _cfgWhiteCanvasEndG;
        private ConfigEntry<float> _cfgWhiteCanvasEndB;

        // Sub camera
        private ConfigEntry<float> _cfgSubCameraFocusSpeed;
        private ConfigEntry<float> _cfgSubCameraYawSpeed;
        private ConfigEntry<float> _cfgSubCameraPitchBase;
        private ConfigEntry<float> _cfgSubCameraPitchAmp;
        private ConfigEntry<float> _cfgSubCameraDistance;
        private ConfigEntry<float> _cfgSubCameraDistanceAmp;
        private ConfigEntry<float> _cfgSubCameraBobAmp;
        private ConfigEntry<float> _cfgSubCameraFovBase;
        private ConfigEntry<float> _cfgSubCameraFovAmp;
        private ConfigEntry<float> _cfgSubCameraFovMin;
        private ConfigEntry<float> _cfgSubCameraFovMax;

        // Astral skybox
        private ConfigEntry<bool> _cfgUseAstralSkybox;
        private ConfigEntry<string> _cfgAstralSkyboxBundlePath;
        private ConfigEntry<string> _cfgAstralSkyboxMaterialName;

        // Astral hill (ランタイム生成。AssetBundle は使わない)
        private ConfigEntry<bool> _cfgUseAstralHill;
        private ConfigEntry<float> _cfgAstralHillPosX;
        private ConfigEntry<float> _cfgAstralHillPosY;
        private ConfigEntry<float> _cfgAstralHillPosZ;
        private ConfigEntry<float> _cfgAstralHillRotX;
        private ConfigEntry<float> _cfgAstralHillRotY;
        private ConfigEntry<float> _cfgAstralHillRotZ;
        private ConfigEntry<float> _cfgAstralHillScale;
        private ConfigEntry<float> _cfgAstralHillRadius;
        private ConfigEntry<float> _cfgAstralHillPeak;
        private ConfigEntry<float> _cfgAstralSceneryFill;
        private ConfigEntry<int> _cfgAstralGroundTextureSize;
        private ConfigEntry<float> _cfgAstralGroundExtentScale;
        private ConfigEntry<UnityEngine.Color> _cfgAstralHorizonColor;
        private ConfigEntry<float> _cfgAstralClearingRadius;
        private ConfigEntry<bool> _cfgAstralGroundFadeOut;
        private ConfigEntry<bool> _cfgAstralGroundFadeUnlit;
        private ConfigEntry<float> _cfgAstralGroundUnlitBrightness;
        private ConfigEntry<bool> _cfgUseAstralFog;
        private ConfigEntry<float> _cfgAstralFogStart;
        private ConfigEntry<float> _cfgAstralFogEnd;
        private ConfigEntry<UnityEngine.Color> _cfgAstralGroundMossColor;
        private ConfigEntry<UnityEngine.Color> _cfgAstralGroundTurfColor;
        private ConfigEntry<UnityEngine.Color> _cfgAstralGroundDryColor;
        private ConfigEntry<UnityEngine.Color> _cfgAstralGroundEarthColor;

        // Astral grass
        private ConfigEntry<int> _cfgAstralGrassCount;
        private ConfigEntry<float> _cfgAstralGrassHeight;
        private ConfigEntry<UnityEngine.Color> _cfgAstralGrassBaseColor;
        private ConfigEntry<UnityEngine.Color> _cfgAstralGrassTipColor;

        // Astral furniture (ベンチ・テーブル・ベッド。位置/角度/サイズを個別に変更できる)
        private ConfigEntry<UnityEngine.Color> _cfgAstralWoodLightColor;
        private ConfigEntry<UnityEngine.Color> _cfgAstralWoodDarkColor;
        private ConfigEntry<UnityEngine.Color> _cfgAstralFabricColor;
        private ConfigEntry<UnityEngine.Color> _cfgAstralBlanketColor;

        private ConfigEntry<bool> _cfgUseAstralBench;
        private ConfigEntry<float> _cfgAstralBenchPosX;
        private ConfigEntry<float> _cfgAstralBenchPosY;
        private ConfigEntry<float> _cfgAstralBenchPosZ;
        private ConfigEntry<float> _cfgAstralBenchRotX;
        private ConfigEntry<float> _cfgAstralBenchRotY;
        private ConfigEntry<float> _cfgAstralBenchRotZ;
        private ConfigEntry<float> _cfgAstralBenchScale;

        private ConfigEntry<bool> _cfgUseAstralTable;
        private ConfigEntry<float> _cfgAstralTablePosX;
        private ConfigEntry<float> _cfgAstralTablePosY;
        private ConfigEntry<float> _cfgAstralTablePosZ;
        private ConfigEntry<float> _cfgAstralTableRotX;
        private ConfigEntry<float> _cfgAstralTableRotY;
        private ConfigEntry<float> _cfgAstralTableRotZ;
        private ConfigEntry<float> _cfgAstralTableScale;

        private ConfigEntry<bool> _cfgUseAstralBed;
        private ConfigEntry<float> _cfgAstralBedPosX;
        private ConfigEntry<float> _cfgAstralBedPosY;
        private ConfigEntry<float> _cfgAstralBedPosZ;
        private ConfigEntry<float> _cfgAstralBedRotX;
        private ConfigEntry<float> _cfgAstralBedRotY;
        private ConfigEntry<float> _cfgAstralBedRotZ;
        private ConfigEntry<float> _cfgAstralBedScale;

        // 挿入体位のときベッドをキャラの下へ移動する
        private ConfigEntry<bool> _cfgBedFollowOnInsertion;
        private ConfigEntry<float> _cfgBedFollowHeightOffset;
        private ConfigEntry<float> _cfgBedFollowYawOffset;
        private ConfigEntry<bool> _cfgBenchHideOffPose;

        // Astral light (アストラル中だけ灯す点光源。帰還で必ず消える)
        private ConfigEntry<bool> _cfgUseAstralLight;
        private ConfigEntry<float> _cfgAstralLightIntensity;
        private ConfigEntry<float> _cfgAstralLightRange;
        private ConfigEntry<float> _cfgAstralLightHeight;
        private ConfigEntry<UnityEngine.Color> _cfgAstralLightColor;

        // Astral coordinate
        private ConfigEntry<bool> _cfgEnableAstralCoordinate;
        private ConfigEntry<ChaFileDefine.CoordinateType> _cfgAstralCoordinateSlot;

        // Manual test
        private ConfigEntry<bool> _cfgManualScrubEnabled;
        private ConfigEntry<float> _cfgManualT;
        private ConfigEntry<bool> _cfgManualTriggerWarp;
        private ConfigEntry<bool> _cfgManualResetEffects;

        private void SetupConfig()
        {
            SetupSkyDisplay();
            const string cat1 = "00. General";
            const string cat2 = "01. Warp Timing";
            const string catLayer = "02. Layer / Order";
            const string catEnable = "03. Effect Enable";
            const string cat3 = "10. Effect Windows (0..1)";
            const string cat4 = "11. Easing";
            const string cat5 = "12. Legacy Intensity";
            const string catAfter = "20. Afterimage";
            const string catDisp = "30. Displacement";
            const string catBloom = "40. Bloom";
            const string catFov = "45. FOV";
            const string catSubCamera = "46. Sub Camera";
            const string catWhite = "50. White Canvas";
            const string catSkybox = "60. Astral Skybox";
            const string catHill = "70. Astral Hill";
            const string catFurniture = "72. Astral Furniture";
            const string catLight = "75. Astral Light";
            const string catCoord = "80. Astral Coordinate";
            const string catManual = "90. Manual Test";

            _cfgEnabled = Bind(cat1, "Enabled", true,
                "AstralMode 有効/無効", order: 100);

            _cfgVerboseLog = Bind(cat1, "Verbose Log", false,
                "詳細ログ出力", order: 90);

            _cfgOrgasmCountToTrigger = Bind(cat1, "Orgasm Count To Trigger",
                10,
                new ConfigDescription("同時絶頂の回数 (この回数到達でアストラルモード突入)",
                    new AcceptableValueRange<int>(1, 100)),
                order: 80);

            _cfgAstralModeKey = Bind(cat1, "Astral Mode Toggle Key",
                new KeyboardShortcut(UnityEngine.KeyCode.S),
                "アストラルモード突入/帰還キー",
                order: 70);

            _cfgSuppressVoiceInAstral = Bind(cat1, "Suppress Idle Talk In Astral", true,
                "アストラル界 滞在中、待機セリフ(会話)を止める。アイドルタイマーを毎フレームリセットして発火させない方式。"
                + "動作中の喘ぎ(voice)・呼吸(breath)は通常どおり鳴る。帰還時に自動で元通り。",
                order: 65);

            _cfgAstralEntryHardStopSeconds = BindRange(cat1, "Entry Hard Stop Voice Seconds", 2.0f, 0f, 10f,
                "突入開始から この秒数だけ、毎フレーム ゲーム音声(喘ぎ/絶頂ボイス)を強制Stopする。"
                + "突入直後に漏れ出すゲーム音声を確実に止めるため。0で無効。この間は呼吸も切れる。",
                order: 63);

            _cfgSuppressKsPlugTimePassesInAstral = Bind(cat1, "Suppress ksPlug Time Passes In Astral", true,
                "アストラル中だけ ksPlug の Time passes in Ecchi を一時的に無効化し、帰還時に元の値へ戻す。",
                order: 61);

            // Timing
            _cfgWarpDuration = BindRange(cat2, "Warp Duration", 4f, 0.1f, 10f,
                "ワープ突入演出の長さ(秒)", order: 100);

            _cfgWhiteHoldDuration = BindRange(cat2, "White Hold Duration", 0f, 0.0f, 5f,
                "白固定ホールド時間(秒) — 裏で重い処理を実行", order: 90);

            _cfgFadeOutDuration = BindRange(cat2, "Fade Out Duration", 4f, 0.1f, 10f,
                "白→アストラル界フェードイン時間(秒)", order: 80);

            // Layer / order. Detailed layer indices are advanced so ConfigManager starts uncluttered.
            _cfgLayerPreset = Bind(catLayer, "Layer Preset", WarpLayerPreset.Default,
                "演出レイヤー順プリセット。細かく試す場合は Manual + 各Layer値を使う。", order: 100);
            _cfgDisplacementLayer = BindRangeInt(catLayer, "Displacement Layer", 10, 0, 100,
                "Manual用。値が小さいほど奥。", order: 90, advanced: true);
            _cfgAfterimageLayer = BindRangeInt(catLayer, "Afterimage Layer", 20, 0, 100,
                "Manual用。値が小さいほど奥。", order: 80, advanced: true);
            _cfgBloomLayer = BindRangeInt(catLayer, "Bloom Layer", 30, 0, 100,
                "Manual用。値が小さいほど奥。", order: 70, advanced: true);
            _cfgWhiteCanvasLayer = BindRangeInt(catLayer, "WhiteCanvas Layer", 40, 0, 100,
                "Manual用。値が小さいほど奥。", order: 60, advanced: true);

            // Enable toggles remain visible.
            _cfgUseAfterimage = Bind(catEnable, "Use Afterimage", true,
                "SimpleAfterimage をワープ演出に使う", order: 100);
            _cfgUseDisplacement = Bind(catEnable, "Use Displacement", true,
                "MainGameCircularDisplacement をワープ演出に使う", order: 90);
            _cfgVrUseAfterimage = Bind(catEnable, "VR: Use Afterimage", false,
                "VRで起動した時だけ使う残像スイッチ。VR時は通常の Use Afterimage を無視しこの値を使う。"
                + "残像はVRでは立体に正しく出ないため既定OFF。突入演出・滞在中の両方に効く。", order: 95);
            _cfgVrUseDisplacement = Bind(catEnable, "VR: Use Displacement", false,
                "VRで起動した時だけ使う波紋スイッチ。VR時は通常の Use Displacement を無視しこの値を使う。"
                + "波紋は画面全体が歪んでVRで酔いやすいため既定OFF。突入演出・滞在中の両方に効く。", order: 85);
            _cfgUseBloom = Bind(catEnable, "Use Bloom", true,
                "AstralMode側 Bloom を使う", order: 80);
            _cfgUseWhiteCanvas = Bind(catEnable, "Use WhiteCanvas", true,
                "白Canvasフェードを使う", order: 70);

            // Effect Windows
            _cfgBloomStartT = BindRange(cat3, "Bloom Start T", 0.0f, 0f, 1f,
                "Bloom 開始時刻 (WarpDuration 中の割合)", order: 100, advanced: true);
            _cfgBloomEndT = BindRange(cat3, "Bloom End T", 1.0f, 0f, 1f,
                "Bloom 最大値到達時刻", order: 95, advanced: true);

            _cfgSunShaftsStartT = BindRange(cat3, "SunShafts Start T", 0.0f, 0f, 1f,
                "SunShafts 開始時刻", order: 90, advanced: true);
            _cfgSunShaftsEndT = BindRange(cat3, "SunShafts End T", 0.8f, 0f, 1f,
                "SunShafts 最大値到達時刻", order: 85, advanced: true);

            _cfgRadialBlurStartT = BindRange(cat3, "Displacement Start T", 0.2f, 0f, 1f,
                "ディスプレイスメント開始時刻", order: 80, advanced: true);
            _cfgRadialBlurEndT = BindRange(cat3, "Displacement End T", 1.0f, 0f, 1f,
                "ディスプレイスメント最大値到達時刻", order: 75, advanced: true);

            _cfgAfterimageStartT = BindRange(cat3, "Afterimage Start T", 0.0f, 0f, 1f,
                "残像開始時刻", order: 70, advanced: true);
            _cfgAfterimageEndT = BindRange(cat3, "Afterimage End T", 0.8f, 0f, 1f,
                "残像最大値到達時刻", order: 65, advanced: true);

            _cfgWhiteCanvasStartT = BindRange(cat3, "WhiteCanvas Start T", 0.75f, 0f, 1f,
                "白Canvas フェードイン開始時刻", order: 60, advanced: true);
            _cfgWhiteCanvasEndT = BindRange(cat3, "WhiteCanvas End T", 1.0f, 0f, 1f,
                "白Canvas 完全白到達時刻", order: 55, advanced: true);


            // Easing
            _cfgBloomEasing = Bind(cat4, "Bloom Easing", EasingType.EaseIn,
                "Bloom 強度の補間方式", order: 100, advanced: true);
            _cfgSunShaftsEasing = Bind(cat4, "SunShafts Easing", EasingType.EaseIn,
                "SunShafts の補間方式", order: 90, advanced: true);
            _cfgRadialBlurEasing = Bind(cat4, "Displacement Easing", EasingType.EaseInCubic,
                "ディスプレイスメントの補間方式", order: 80, advanced: true);
            _cfgAfterimageEasing = Bind(cat4, "Afterimage Easing", EasingType.Linear,
                "残像強度の補間方式", order: 70, advanced: true);
            _cfgWhiteCanvasEasing = Bind(cat4, "WhiteCanvas Easing", EasingType.EaseIn,
                "白Canvas alpha の補間方式", order: 60, advanced: true);
            _cfgFadeOutEasing = Bind(cat4, "FadeOut Easing", EasingType.EaseOut,
                "白→アストラル界 フェードアウトの補間方式", order: 50, advanced: true);

            // Legacy/simple intensity entries kept for current warp code compatibility.
            _cfgBloomMaxIntensity = BindRange(cat5, "Bloom Max Intensity", 8f, 0f, 30f,
                "Bloom の最大強度", order: 100, advanced: true);
            _cfgBloomThreshold = BindRange(cat5, "Bloom Threshold", 0.5f, 0f, 5f,
                "Bloom 抽出閾値", order: 95, advanced: true);
            _cfgRadialBlurMaxStrength = BindRange(cat5, "Displacement Max Strength", 0.3f, 0f, 1f,
                "ディスプレイスメント最大強度", order: 90, advanced: true);
            _cfgAfterimageMaxAlpha = BindRange(cat5, "Afterimage Max Alpha", 0.7f, 0f, 1f,
                "残像の最大不透明度", order: 85, advanced: true);

            _cfgFovWarpTarget = BindRange(catFov, "FOV Warp Target", 179f, 1f, 179f,
                "ワープ時の最終FOV。倍率ではなく絶対値。ToWhiteではここへ向かい、FromWhiteではここから元FOVへ戻る", order: 19);

            // Sub camera
            _cfgSubCameraFocusSpeed = BindRange(catSubCamera, "Focus Speed", 0.12f, 0.01f, 1.0f,
                "サブカメラ注視点が腰〜頭ラインを移動する速度", order: 100);
            _cfgSubCameraYawSpeed = BindRange(catSubCamera, "Yaw Speed", 10f, -90f, 90f,
                "サブカメラの水平回転速度", order: 95);
            _cfgSubCameraPitchBase = BindRange(catSubCamera, "Pitch Base", 8f, -45f, 45f,
                "サブカメラの基本ピッチ", order: 90);
            _cfgSubCameraPitchAmp = BindRange(catSubCamera, "Pitch Amp", 30f, 0f, 45f,
                "サブカメラのピッチ揺れ幅", order: 85);
            _cfgSubCameraDistance = BindRange(catSubCamera, "Distance", 2f, 0.3f, 10f,
                "サブカメラの注視点からの距離", order: 80);
            _cfgSubCameraDistanceAmp = BindRange(catSubCamera, "Distance Amp", 1f, 0f, 5f,
                "サブカメラ距離の揺れ幅", order: 75);
            _cfgSubCameraBobAmp = BindRange(catSubCamera, "Vertical Bob Amp", 0f, 0f, 5f,
                "サブカメラ上下揺れ幅", order: 70);
            _cfgSubCameraFovBase = BindRange(catSubCamera, "FOV Base", 10f, 10f, 120f,
                "サブカメラFOV基準。小さいほど望遠で近く見える", order: 65);
            _cfgSubCameraFovAmp = BindRange(catSubCamera, "FOV Amp", 20f, 0f, 60f,
                "サブカメラFOV揺れ幅", order: 60);
            _cfgSubCameraFovMin = BindRange(catSubCamera, "FOV Min", 24f, 5f, 120f,
                "サブカメラFOV下限", order: 55);
            _cfgSubCameraFovMax = BindRange(catSubCamera, "FOV Max", 70f, 5f, 160f,
                "サブカメラFOV上限", order: 50);

            // Display placement is configured in 76. Sky Display. Legacy config values are not applied.

            // Afterimage detail
            _cfgAfterimageExistingMode = Bind(catAfter, "Existing Trail Mode", AfterimageExistingMode.StopAddingAndDecay,
                "ワープ開始時点の既存残像の扱い。基本は StopAddingAndDecay。", order: 100);
            _cfgAfterimageStartIntensity = BindRange(catAfter, "Start Intensity", 0.0f, 0f, 1f,
                "残像開始強度", order: 90, advanced: true);
            _cfgAfterimageEndIntensity = BindRange(catAfter, "End Intensity", 1.0f, 0f, 1f,
                "残像終了強度", order: 85, advanced: true);
            _cfgAfterimageStartAlphaScale = BindRange(catAfter, "Start AlphaScale", 0.1f, 0f, 1f,
                "残像開始Alpha倍率", order: 80, advanced: true);
            _cfgAfterimageEndAlphaScale = BindRange(catAfter, "End AlphaScale", 1.0f, 0f, 1f,
                "残像終了Alpha倍率", order: 75, advanced: true);
            _cfgAfterimageStartFadeFrames = BindRangeInt(catAfter, "Start FadeFrames", 60, 1, 300,
                "残像開始寿命フレーム", order: 70, advanced: true);
            _cfgAfterimageEndFadeFrames = BindRangeInt(catAfter, "End FadeFrames", 120, 1, 300,
                "残像終了寿命フレーム", order: 65, advanced: true);
            _cfgAfterimageStartCaptureInterval = BindRangeInt(catAfter, "Start CaptureInterval", 2, 1, 60,
                "残像開始キャプチャ間隔", order: 60, advanced: true);
            _cfgAfterimageEndCaptureInterval = BindRangeInt(catAfter, "End CaptureInterval", 1, 1, 60,
                "残像終了キャプチャ間隔", order: 55, advanced: true);
            _cfgAfterimageExistingDecayStartT = BindRange(catAfter, "Existing Decay Start T", 0.0f, 0f, 1f,
                "既存残像を消し始める時刻", order: 50, advanced: true);
            _cfgAfterimageExistingDecayEndT = BindRange(catAfter, "Existing Decay End T", 0.7f, 0f, 1f,
                "既存残像がほぼ消える時刻", order: 45, advanced: true);
            _cfgAfterimageExistingStartAlpha = BindRange(catAfter, "Existing Start Alpha", 1.0f, 0f, 1f,
                "既存残像の開始Alpha倍率", order: 40, advanced: true);
            _cfgAfterimageExistingEndAlpha = BindRange(catAfter, "Existing End Alpha", 0.0f, 0f, 1f,
                "既存残像の終了Alpha倍率", order: 35, advanced: true);

            // Displacement detail
            _cfgDisplacementMode = Bind(catDisp, "Mode", DisplacementMode.Radial,
                "ディスプレイスメント種類", order: 100);
            _cfgDisplacementStartIntensity = BindRange(catDisp, "Start Intensity", 0.0f, 0f, 1f,
                "開始強度", order: 90, advanced: true);
            _cfgDisplacementEndIntensity = BindRange(catDisp, "End Intensity", 1.0f, 0f, 1f,
                "終了強度", order: 85, advanced: true);
            _cfgDisplacementStartOpacity = BindRange(catDisp, "Start Opacity", 0.0f, 0f, 1f,
                "開始不透明度", order: 80, advanced: true);
            _cfgDisplacementEndOpacity = BindRange(catDisp, "End Opacity", 1.0f, 0f, 1f,
                "終了不透明度", order: 75, advanced: true);
            _cfgDisplacementStartStrength = BindRange(catDisp, "Start Strength", 0.0f, -0.5f, 0.5f,
                "開始UV変位量", order: 70, advanced: true);
            _cfgDisplacementEndStrength = BindRange(catDisp, "End Strength", 0.06f, -0.5f, 0.5f,
                "終了UV変位量", order: 65, advanced: true);
            _cfgDisplacementStartRadius = BindRange(catDisp, "Start Radius", 0.3f, 0.001f, 2f,
                "開始半径", order: 60, advanced: true);
            _cfgDisplacementEndRadius = BindRange(catDisp, "End Radius", 0.8f, 0.001f, 2f,
                "終了半径", order: 55, advanced: true);
            _cfgDisplacementStartInnerRadius = BindRange(catDisp, "Start InnerRadius", 0.0f, 0f, 2f,
                "開始内側半径", order: 50, advanced: true);
            _cfgDisplacementEndInnerRadius = BindRange(catDisp, "End InnerRadius", 2f, 0f, 2f,
                "終了内側半径", order: 45, advanced: true);
            _cfgDisplacementCenterX = BindRange(catDisp, "Center X", 0.5f, 0f, 1f,
                "中心X", order: 40, advanced: true);
            _cfgDisplacementCenterY = BindRange(catDisp, "Center Y", 0.5f, 0f, 1f,
                "中心Y", order: 35, advanced: true);
            _cfgDisplacementStartFalloff = BindRange(catDisp, "Start Falloff", 2f, 0.001f, 16f,
                "開始フォールオフ", order: 30, advanced: true);
            _cfgDisplacementEndFalloff = BindRange(catDisp, "End Falloff", 2f, 0.001f, 16f,
                "終了フォールオフ", order: 25, advanced: true);

            // Bloom detail
            _cfgBloomStartIntensity = BindRange(catBloom, "Start Intensity", 0.0f, 0f, 30f,
                "開始Bloom強度", order: 100, advanced: true);
            _cfgBloomEndIntensity = BindRange(catBloom, "End Intensity", 8.0f, 0f, 30f,
                "終了Bloom強度", order: 95, advanced: true);
            _cfgBloomStartThreshold = BindRange(catBloom, "Start Threshold", 1.0f, 0f, 5f,
                "開始Bloom閾値", order: 90, advanced: true);
            _cfgBloomEndThreshold = BindRange(catBloom, "End Threshold", 0.5f, 0f, 5f,
                "終了Bloom閾値", order: 85, advanced: true);

            // FOV all-in-one
            _cfgEnableFovWarp = Bind(catFov, "EnableFovWarp", true,
                "ワープ中のFOV演出を使う", order: 200);
            _cfgFovForceOverride = Bind(catFov, "FOV Force Override", true,
                "CameraControl_Ver2 等の FOV 書き戻しを Harmony Postfix で上書き抑制する（MGCC API 成功時は実質不要）", order: 195);

            _cfgFovStartT = BindRange(catFov, "FOV StartT", 0.0f, 0f, 1f,
                "FOV Warp 開始時刻", order: 190);
            _cfgFovEndT = BindRange(catFov, "FOV EndT", 1.0f, 0f, 1f,
                "FOV Warp 終了時刻", order: 185);
            _cfgFovEasing = Bind(catFov, "FOV Easing", EasingType.EaseInOut,
                "FOV Warp 補間方式", order: 180);

            _cfgFovFadeStartT = BindRange(catFov, "FOV Fade StartT", 0.0f, 0f, 1f,
                "FOV Fade 開始時刻", order: 175);
            _cfgFovFadeEndT = BindRange(catFov, "FOV Fade EndT", 1.0f, 0f, 1f,
                "FOV Fade 終了時刻", order: 170);
            _cfgFovFadeEasing = Bind(catFov, "FOV Fade Easing", EasingType.EaseOut,
                "FOV Fade 補間方式", order: 165);

            // 倍率系・Base・MinMax は既存配置のまま
            _cfgFovStartMultiplier = BindRange(catFov, "FOV Start Multiplier", 1.0f, 0.1f, 5f,
                "Warp開始時の baseFov 倍率", order: 100, advanced: true);
            _cfgFovEndMultiplier = BindRange(catFov, "FOV End Multiplier", 2f, 0.1f, 5f,
                "Warp終了時の baseFov 倍率", order: 95, advanced: true);
            _cfgFovFadeStartMultiplier = BindRange(catFov, "FOV Fade Start Multiplier", 1.5f, 0.1f, 5f,
                "Fade開始時の baseFov 倍率", order: 90, advanced: true);
            _cfgFovFadeEndMultiplier = BindRange(catFov, "FOV Fade End Multiplier", 1.0f, 0.1f, 5f,
                "Fade終了時の originalFov 倍率（既定1.0でスナップなし）", order: 85, advanced: true);
            _cfgFovHoldMultiplier = BindRange(catFov, "FOV Hold Multiplier", 2f, 0.1f, 5f,
                "WhiteHold中の baseFov 倍率", order: 80, advanced: true);
            _cfgFovBaseMode = Bind(catFov, "FOV Base Mode", FovBaseMode.CurrentFov,
                "baseFov の取り方。CurrentFov=ワープ開始時のFOV、ConfigBaseFov=FOV Base Value固定", order: 75);
            _cfgFovBaseValue = BindRange(catFov, "FOV Base Value", 50f, 10f, 170f,
                "Base Mode=ConfigBaseFov 時の固定 baseFov 値", order: 70, advanced: true);
            _cfgFovMin = BindRange(catFov, "FOV Min", 10f, 1f, 179f,
                "最終FOVの下限クランプ", order: 65, advanced: true);
            _cfgFovMax = BindRange(catFov, "FOV Max", 179f, 1f, 179f,
                "最終FOVの上限クランプ", order: 60, advanced: true);

            // White canvas detail
            _cfgWhiteCanvasStartAlpha = BindRange(catWhite, "Start Alpha", 0.0f, 0f, 1f,
                "開始Alpha", order: 100, advanced: true);
            _cfgWhiteCanvasEndAlpha = BindRange(catWhite, "End Alpha", 1.0f, 0f, 1f,
                "終了Alpha", order: 95, advanced: true);
            _cfgWhiteCanvasStartR = BindRange(catWhite, "Start R", 1.0f, 0f, 1f,
                "開始色R", order: 90, advanced: true);
            _cfgWhiteCanvasStartG = BindRange(catWhite, "Start G", 1.0f, 0f, 1f,
                "開始色G", order: 85, advanced: true);
            _cfgWhiteCanvasStartB = BindRange(catWhite, "Start B", 1.0f, 0f, 1f,
                "開始色B", order: 80, advanced: true);
            _cfgWhiteCanvasEndR = BindRange(catWhite, "End R", 1.0f, 0f, 1f,
                "終了色R", order: 75, advanced: true);
            _cfgWhiteCanvasEndG = BindRange(catWhite, "End G", 1.0f, 0f, 1f,
                "終了色G", order: 70, advanced: true);
            _cfgWhiteCanvasEndB = BindRange(catWhite, "End B", 1.0f, 0f, 1f,
                "終了色B", order: 65, advanced: true);

            _cfgUseAstralSkybox = Bind(catSkybox, "Use Astral Skybox", true,
                "Use an AssetBundle skybox while AstralMode is active.", order: 100);
            _cfgAstralSkyboxBundlePath = Bind(catSkybox, "Bundle Path", "astral_skybox/astral_skybox_cool",
                "AssetBundle path relative to the AstralMode plugin folder, or an absolute path.", order: 90);
            _cfgAstralSkyboxMaterialName = Bind(catSkybox, "Material Name", "6sidedCosmicCoolCloud",
                "Skybox material name inside the AssetBundle. If empty or missing, the first material is used.", order: 80);

            _cfgUseAstralHill = Bind(catHill, "Use Astral Hill", true,
                "アストラル中に丘と草原を表示する。丘・草・地面模様はすべてその場で生成する（外部ファイル不要）。", order: 100);
            _cfgAstralHillPosX = BindRange(catHill, "Position X", 0f, -1000f, 1000f,
                "アストラル基準点からの丘Root Xオフセット。", order: 95);
            _cfgAstralHillPosY = BindRange(catHill, "Position Y", -5.45f, -1000f, 1000f,
                "アストラル基準点からの丘Root Yオフセット。0でキャラの足元に丘の頂上が来る。", order: 94);
            _cfgAstralHillPosZ = BindRange(catHill, "Position Z", 0f, -1000f, 1000f,
                "アストラル基準点からの丘Root Zオフセット。", order: 93);
            _cfgAstralHillRotX = BindRange(catHill, "Rotation X", 0f, -360f, 360f,
                "丘RootのX回転。", order: 92, advanced: true);
            _cfgAstralHillRotY = BindRange(catHill, "Rotation Y", 0f, -360f, 360f,
                "丘RootのY回転。", order: 91);
            _cfgAstralHillRotZ = BindRange(catHill, "Rotation Z", 0f, -360f, 360f,
                "丘RootのZ回転。", order: 90, advanced: true);
            _cfgAstralHillScale = BindRange(catHill, "Scale", 1f, 0.01f, 100f,
                "丘と草を含むRoot全体のスケール。", order: 89);

            _cfgAstralHillRadius = BindRange(catHill, "Hill Radius", 26f, 5f, 200f,
                "丘の裾までの半径(m)。草の広がりもこれに追従する。変更すると組み直す。", order: 88);
            _cfgAstralHillPeak = BindRange(catHill, "Hill Peak Height", 5f, 0f, 40f,
                "丘の頂上の高さ(m)。0で平地。変更すると組み直す。", order: 87);
            _cfgAstralGroundTextureSize = BindRangeInt(catHill, "Ground Texture Size", 1024, 256, 2048,
                "地面模様のテクスチャ解像度。上げるほど粒が細かくなるが生成時間とメモリを食う。", order: 86, advanced: true);
            _cfgAstralGroundExtentScale = BindRange(catHill, "Ground Extent Scale", 3.2f, 1.6f, 8f,
                "地面を丘の半径の何倍まで伸ばすか。小さいと地面が手前で終わって星空との境目が線に見える。", order: 85);
            _cfgAstralHorizonColor = Bind(catHill, "Horizon Color", new UnityEngine.Color(0.071f, 0.024f, 1.000f),
                "遠くの地面が沈んでいく色。**地平線のすぐ上の空の色に合わせると、地面と空の境目が消える。**"
                + "ここが空とかけ離れていると、沈めた地面と空がぶつかって逆に線がくっきり見える。", order: 84);

            _cfgAstralGroundFadeOut = Bind(catHill, "Ground Fade Out Edge", true,
                "地面の外周を、だんだん透明にして空へ溶かす。地面は1枚のままで濃さだけが変わるので段差は出ない。", order: 83);
            _cfgAstralGroundFadeUnlit = Bind(catHill, "Ground Fade Use Unlit", true,
                "【透明にならなかったとき用】地面の塗りを、半透明が確実に効く塗りへ切り替える。"
                + "借り物の塗りは薄める機能を削られていることがあり、その場合は透明にならず不透明のまま出る（霧と同じ現象）。"
                + "これを on にすると必ず透けるが、地面が光を受けなくなり一様な明るさになる。", order: 82);
            _cfgAstralGroundUnlitBrightness = BindRange(catHill, "Ground Unlit Brightness", 0.7f, 0.05f, 3f,
                "Ground Fade Use Unlit が on のときの地面の明るさ。草との明るさを揃えるために使う。", order: 81, advanced: true);

            _cfgAstralClearingRadius = BindRange(catHill, "Clearing Radius", 2.2f, 0f, 12f,
                "キャラの周りで草を短く・薄くする半径(m)。H中に草が視界を邪魔しないため。"
                + "中心は完全に空き、外へ向かって丈と密度が戻る。0で無効。", order: 83);

            _cfgUseAstralFog = Bind(catHill, "Use Astral Fog", true,
                "【既定オフ】アストラル中だけ霧を出す。ただし実機で確認したところ、"
                + "このゲームの描画は霧を無視する（設定は毎フレーム保たれているのに絵が変わらないことをログで確認済み）。"
                + "遠景を沈めたい場合は霧ではなく Horizon Color を空の色に合わせること。", order: 77);
            _cfgAstralFogStart = BindRange(catHill, "Fog Start Distance", 0f, 0f, 300f,
                "霧が掛かり始める距離(m)。ここより近くは霧の影響を受けない。"
                + "100m下の現実を覗くサブカメラは被写体のすぐ近くにいるため、この距離を離しておけば影響しない。", order: 76);
            _cfgAstralFogEnd = BindRange(catHill, "Fog End Distance", 321.953f, 1f, 500f,
                "霧で完全に見えなくなる距離(m)。地面の端より手前にすると端が見えなくなる。", order: 75);

            _cfgAstralGroundMossColor = Bind(catHill, "Ground Moss Color", new UnityEngine.Color(0.404f, 0.404f, 0.086f),
                "地面の色1: 湿った苔の濃緑。窪みに出る。", order: 85, advanced: true);
            _cfgAstralGroundTurfColor = Bind(catHill, "Ground Turf Color", new UnityEngine.Color(0.212f, 0.361f, 0.165f),
                "地面の色2: 普通の草地。地面の基調色。", order: 84);
            _cfgAstralGroundDryColor = Bind(catHill, "Ground Dry Color", new UnityEngine.Color(0.408f, 0.424f, 0.220f),
                "地面の色3: 枯れかけた黄緑。斑になって出る。この斑の上は草が薄くなる。", order: 83, advanced: true);
            _cfgAstralGroundEarthColor = Bind(catHill, "Ground Earth Color", new UnityEngine.Color(0.192f, 0.506f, 0.000f),
                "地面の色4: 地肌の土。ごく一部に出る。", order: 82, advanced: true);

            _cfgAstralSceneryFill = BindRange(catHill, "Scenery Self Lit", 0.35f, 0f, 1f,
                "丘・草・家具が自分の色でどれだけ光るか。アストラル界の光源は点光源1個だけで"
                + "マップ照明も落ちているため、0だと光の当たらない所が真っ黒に沈む。"
                + "環境光そのものは現実に漏れるので触らず、景色側を光らせて底上げする。", order: 82);

            _cfgAstralGrassCount = BindRangeInt(catHill, "Grass Count", 34958, 0, 120000,
                "草の本数。下草はこの1.2倍が別に生える。重ければ下げる。変更すると組み直す。", order: 81);
            _cfgAstralGrassHeight = BindRange(catHill, "Grass Height Scale", 1f, 0.1f, 5f,
                "草の丈の倍率。1.0で腰より下の草原。変更すると組み直す。", order: 80);
            _cfgAstralGrassBaseColor = Bind(catHill, "Grass Base Color", new UnityEngine.Color(0.102f, 0.227f, 0.094f),
                "草の根元の色。濃い緑。", order: 79);
            _cfgAstralGrassTipColor = Bind(catHill, "Grass Tip Color", new UnityEngine.Color(0.518f, 0.769f, 0.306f),
                "草の先端の色。明るい黄緑。根元からここへグラデーションする。", order: 78);

            // ---- 家具 ----
            _cfgAstralWoodLightColor = Bind(catFurniture, "Wood Light Color", new UnityEngine.Color(0.596f, 0.502f, 0.376f),
                "家具の明るい木の色（座面・天板・頭板）。白木寄りにも古材寄りにも振れる。", order: 100);
            _cfgAstralWoodDarkColor = Bind(catFurniture, "Wood Dark Color", new UnityEngine.Color(0.408f, 0.329f, 0.243f),
                "家具の濃い木の色（脚・枠・支柱）。", order: 99);
            _cfgAstralFabricColor = Bind(catFurniture, "Fabric Color", new UnityEngine.Color(0.839f, 0.831f, 0.792f),
                "ベッドのマットレスの色。", order: 98);
            _cfgAstralBlanketColor = Bind(catFurniture, "Blanket Color", new UnityEngine.Color(0.518f, 0.573f, 0.627f),
                "ベッドの掛け布団の色。", order: 97);

            _cfgUseAstralBench = Bind(catFurniture, "Use Bench", true,
                "ベンチを置く。アストラルの開始体位はベンチフェラ固定なので、座る実体がこれ。", order: 90);
            _cfgAstralBenchPosX = BindRange(catFurniture, "Bench Position X", 0f, -100f, 100f,
                "ベンチのXオフセット（丘の中心基準）。", order: 89);
            _cfgAstralBenchPosY = BindRange(catFurniture, "Bench Position Y", 0f, -20f, 20f,
                "ベンチの高さ。0で地面の上に自動で乗る。ここは地面からの持ち上げ量。", order: 88);
            _cfgAstralBenchPosZ = BindRange(catFurniture, "Bench Position Z", 0f, -100f, 100f,
                "ベンチのZオフセット（丘の中心基準）。", order: 87);
            _cfgAstralBenchRotX = BindRange(catFurniture, "Bench Rotation X", 0f, -360f, 360f,
                "ベンチのX回転。", order: 86, advanced: true);
            _cfgAstralBenchRotY = BindRange(catFurniture, "Bench Rotation Y", 89.57749f, -360f, 360f,
                "ベンチの向き。", order: 85);
            _cfgAstralBenchRotZ = BindRange(catFurniture, "Bench Rotation Z", 0f, -360f, 360f,
                "ベンチのZ回転。", order: 84, advanced: true);
            _cfgAstralBenchScale = BindRange(catFurniture, "Bench Scale", 1f, 0.1f, 10f,
                "ベンチの大きさ。足元の草を刈る範囲も一緒に変わる。", order: 83);

            _cfgUseAstralTable = Bind(catFurniture, "Use Table", true,
                "テーブルを置く。ベンチと寸法・材質・脚の意匠を揃えてある。", order: 80);
            _cfgAstralTablePosX = BindRange(catFurniture, "Table Position X", 1.5f, -100f, 100f,
                "テーブルのXオフセット（丘の中心基準）。", order: 79);
            _cfgAstralTablePosY = BindRange(catFurniture, "Table Position Y", 0f, -20f, 20f,
                "テーブルの高さ。0で地面の上に自動で乗る。", order: 78);
            _cfgAstralTablePosZ = BindRange(catFurniture, "Table Position Z", 0.08103289f, -100f, 100f,
                "テーブルのZオフセット。既定はベンチの正面。", order: 77);
            _cfgAstralTableRotX = BindRange(catFurniture, "Table Rotation X", 0f, -360f, 360f,
                "テーブルのX回転。", order: 76, advanced: true);
            _cfgAstralTableRotY = BindRange(catFurniture, "Table Rotation Y", 90f, -360f, 360f,
                "テーブルの向き。", order: 75);
            _cfgAstralTableRotZ = BindRange(catFurniture, "Table Rotation Z", 0f, -360f, 360f,
                "テーブルのZ回転。", order: 74, advanced: true);
            _cfgAstralTableScale = BindRange(catFurniture, "Table Scale", 0.81f, 0.1f, 10f,
                "テーブルの大きさ。", order: 73);

            _cfgUseAstralBed = Bind(catFurniture, "Use Bed", false,
                "ベッドを置く。既定は置かない（地面の上でよい、との判断）。", order: 70);
            _cfgAstralBedPosX = BindRange(catFurniture, "Bed Position X", -3.10f, -100f, 100f,
                "ベッドのXオフセット（丘の中心基準）。", order: 69);
            _cfgAstralBedPosY = BindRange(catFurniture, "Bed Position Y", 0f, -20f, 20f,
                "ベッドの高さ。0で地面の上に自動で乗る。", order: 68);
            _cfgAstralBedPosZ = BindRange(catFurniture, "Bed Position Z", 1.35f, -100f, 100f,
                "ベッドのZオフセット（丘の中心基準）。", order: 67);
            _cfgAstralBedRotX = BindRange(catFurniture, "Bed Rotation X", 0f, -360f, 360f,
                "ベッドのX回転。", order: 66, advanced: true);
            _cfgAstralBedRotY = BindRange(catFurniture, "Bed Rotation Y", 28f, -360f, 360f,
                "ベッドの向き。", order: 65);
            _cfgAstralBedRotZ = BindRange(catFurniture, "Bed Rotation Z", 0f, -360f, 360f,
                "ベッドのZ回転。", order: 64, advanced: true);
            _cfgAstralBedScale = BindRange(catFurniture, "Bed Scale", 1f, 0.1f, 10f,
                "ベッドの大きさ。", order: 63);

            _cfgBedFollowOnInsertion = Bind(catFurniture, "Bed Follow On Insertion", true,
                "挿入体位(sonyu系)のあいだ、ベッドをキャラの真下へ移動してマットレスの上面を足元に合わせる。"
                + "キャラは一切動かさない。体位を抜けると元の置き場所へ戻る。", order: 60);
            _cfgBedFollowHeightOffset = BindRange(catFurniture, "Bed Follow Height Offset", 0f, -2f, 2f,
                "追従中のベッドの高さ微調整。キャラが沈むならプラス、浮くならマイナス。", order: 59);
            _cfgBedFollowYawOffset = BindRange(catFurniture, "Bed Follow Yaw Offset", 0f, -360f, 360f,
                "追従中のベッドの向きの微調整。キャラの向きを基準にした角度差。", order: 58);
            _cfgBenchHideOffPose = Bind(catFurniture, "Bench Hide Off Pose", true,
                "許可していない体位ではベンチを隠す。座らない体位ではベンチが体を貫くだけで、"
                + "挿入体位ではベッドとも重なるため。どの体位で出すかは「73. Astral Bench Poses」の"
                + "チェックボックスで決める（既定はベンチフェラだけON）。これをオフにすると全体位で出しっぱなし。",
                order: 57);

            _cfgUseAstralLight = Bind(catLight, "Use Astral Light", true,
                "アストラル中だけ点光源を灯す。BlankMapAdd の DisableLights でマップ照明が落ちているため、"
                + "これが無いと丘も草も暗いまま。帰還時に必ず消えるので現実には残らない。", order: 100);
            _cfgAstralLightIntensity = BindRange(catLight, "Intensity", 1.2f, 0f, 8f,
                "点光源の強さ。0で消灯。星空の静けさを壊さない程度に留めるのが本来の意図。", order: 90);
            _cfgAstralLightRange = BindRange(catLight, "Range", 80f, 5f, 300f,
                "点光源が届く半径。100m下の「止まった現実」を照らさないよう、実際には自動で上限が掛かる。", order: 80);
            _cfgAstralLightHeight = BindRange(catLight, "Height", 10f, -50f, 100f,
                "アストラル基準点(キャラ中心)から見た点光源の高さ。丘は既定で45m下にあるので、下向きに届く距離を稼ぐ。",
                order: 70);
            _cfgAstralLightColor = Bind(catLight, "Color", new UnityEngine.Color(0.72f, 0.80f, 1f, 1f),
                "点光源の色。既定は青寄りの月明かり。", order: 60);

            _cfgEnableAstralCoordinate = Bind(catCoord, "Enable Astral Coordinate Change", true,
                "アストラル界突入時に女キャラを指定スロットのコーディネートへ切り替え、帰還時に元スロットへ戻す。男キャラは対象外。",
                order: 100);
            _cfgAstralCoordinateSlot = Bind(catCoord, "Astral Coordinate Slot", ChaFileDefine.CoordinateType.Plain,
                "アストラル中に女キャラへ着せるカード内コーディネートスロット。Plain/Swim/Pajamas/Bathing。",
                order: 90);

            // Manual test visible controls. ManualT is advanced because it is noisy during normal play.
            _cfgManualTriggerWarp = Bind(catManual, "Trigger Warp", false,
                "trueにすると手動ワープテスト用。実処理側で消費する。", order: 100);
            _cfgManualResetEffects = Bind(catManual, "Reset Effects", false,
                "trueにすると演出状態をリセット。実処理側で消費する。", order: 90);
            _cfgManualScrubEnabled = Bind(catManual, "Scrub Enabled", false,
                "ManualTで演出時刻を固定確認する", order: 80);
            _cfgManualT = BindRange(catManual, "Manual T", 0.0f, 0f, 1f,
                "Scrub用の演出時刻", order: 70, advanced: true);
        }

        private ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description, int order = 0, bool advanced = false)
        {
            return Config.Bind(section, key, defaultValue,
                new ConfigDescription(description, null, BuildConfigManagerAttributes(order, advanced)));
        }

        private ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, ConfigDescription description, int order = 0, bool advanced = false)
        {
            List<object> tags = new List<object>();
            if (description.Tags != null) tags.AddRange(description.Tags);
            object attrs = BuildConfigManagerAttributes(order, advanced);
            if (attrs != null) tags.Add(attrs);
            return Config.Bind(section, key, defaultValue,
                new ConfigDescription(description.Description, description.AcceptableValues, tags.ToArray()));
        }

        private ConfigEntry<float> BindRange(string section, string key, float def, float min, float max, string description, int order = 0, bool advanced = false)
        {
            return Bind(section, key, def,
                new ConfigDescription(description, new AcceptableValueRange<float>(min, max)),
                order, advanced);
        }

        private ConfigEntry<int> BindRangeInt(string section, string key, int def, int min, int max, string description, int order = 0, bool advanced = false)
        {
            return Bind(section, key, def,
                new ConfigDescription(description, new AcceptableValueRange<int>(min, max)),
                order, advanced);
        }

        private object BuildConfigManagerAttributes(int order = 0, bool advanced = false)
        {
            return new ConfigurationManager.ConfigurationManagerAttributes
            {
                // 範囲付きの設定を％ではなく実値で出す。
                // ConfigurationManager は ShowRangeAsPercent が true だと
                // 「範囲内の位置」を％ラベルで描いて return し、値のテキスト欄まで到達しない。
                // 回数やレイヤー番号が 9% のように出て、数値の直接入力もできなくなる。
                ShowRangeAsPercent = false,
                Order = order,
                IsAdvanced = advanced
            };
        }

        /// <summary>
        /// アストラルの突入/帰還キーが押されたか。設定 (Astral Mode Toggle Key) をそのまま見る。
        /// Config がまだ張られていない起動直後だけ、既定の S にフォールバックする。
        /// </summary>
        private bool IsAstralToggleDown()
        {
            if (_cfgAstralModeKey == null)
                return Bindings.Down("AstralMode.toggle", "S");

            return Bindings.Down("AstralMode.toggle", _cfgAstralModeKey.Value.ToString());
        }

        /// <summary>ログに出す押されたキーの名前。</summary>
        private string AstralToggleKeyName()
        {
            return _cfgAstralModeKey != null
                ? _cfgAstralModeKey.Value.ToString()
                : UnityEngine.KeyCode.S.ToString();
        }
    }
}
