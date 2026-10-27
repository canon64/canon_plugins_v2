using BepInEx.Configuration;
using System;
using UnityEngine;

namespace MainGamePortraitCapture
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgDevelopmentHotkeyEnabled;
        private ConfigEntry<KeyboardShortcut> _cfgCaptureKey;
        private ConfigEntry<int> _cfgTargetFemaleIndex;
        private ConfigEntry<bool> _cfgLookAtCameraDuringCapture;
        private ConfigEntry<int> _cfgEyeLookPatternDuringCapture;
        private ConfigEntry<bool> _cfgTurnHeadToCameraDuringCapture;
        private ConfigEntry<int> _cfgNeckLookPatternDuringCapture;
        private ConfigEntry<bool> _cfgPreserveHeadTiltDuringCapture;
        private ConfigEntry<float> _cfgHeadLookMinPitch;
        private ConfigEntry<float> _cfgHeadLookMaxPitch;
        private ConfigEntry<float> _cfgHeadLookMinYaw;
        private ConfigEntry<float> _cfgHeadLookMaxYaw;
        private ConfigEntry<float> _cfgHeadLookMinRoll;
        private ConfigEntry<float> _cfgHeadLookMaxRoll;
        private ConfigEntry<bool> _cfgHeadLookFollowPitchEnabled;
        private ConfigEntry<bool> _cfgHeadLookFollowYawEnabled;
        private ConfigEntry<bool> _cfgHeadLookFollowRollEnabled;
        private ConfigEntry<bool> _cfgHeadLookPitchDeadZoneEnabled;
        private ConfigEntry<float> _cfgHeadLookPitchDeadZoneMin;
        private ConfigEntry<float> _cfgHeadLookPitchDeadZoneMax;
        private ConfigEntry<bool> _cfgHeadLookYawDeadZoneEnabled;
        private ConfigEntry<float> _cfgHeadLookYawDeadZoneMin;
        private ConfigEntry<float> _cfgHeadLookYawDeadZoneMax;
        private ConfigEntry<bool> _cfgHeadLookRollDeadZoneEnabled;
        private ConfigEntry<float> _cfgHeadLookRollDeadZoneMin;
        private ConfigEntry<float> _cfgHeadLookRollDeadZoneMax;
        private ConfigEntry<bool> _cfgAutoRotateLyingPose;
        private ConfigEntry<float> _cfgMoveDuration;
        private ConfigEntry<float> _cfgHoldBeforeCapture;
        private ConfigEntry<float> _cfgReturnDuration;
        private ConfigEntry<bool> _cfgReturnCamera;
        private ConfigEntry<bool> _cfgForceKeepFov;
        private ConfigEntry<int> _cfgCaptureWidth;
        private ConfigEntry<int> _cfgCaptureHeight;
        private ConfigEntry<string> _cfgOutputDirectory;
        private ConfigEntry<float> _cfgMinYaw;
        private ConfigEntry<float> _cfgMaxYaw;
        private ConfigEntry<float> _cfgHeadDirectionPriority;
        private ConfigEntry<float> _cfgHipDirectionPriority;
        private ConfigEntry<float> _cfgDirectionPriorityRandomness;
        private ConfigEntry<float> _cfgMinPitch;
        private ConfigEntry<float> _cfgMaxPitch;
        private ConfigEntry<float> _cfgMinRoll;
        private ConfigEntry<float> _cfgMaxRoll;
        private ConfigEntry<float> _cfgMinFov;
        private ConfigEntry<float> _cfgMaxFov;
        private ConfigEntry<float> _cfgMinDistance;
        private ConfigEntry<float> _cfgMaxDistance;
        private ConfigEntry<float> _cfgViewportMargin;
        private ConfigEntry<float> _cfgHeadUpperMargin;
        private ConfigEntry<float> _cfgCrotchLowerMargin;
        private ConfigEntry<int> _cfgRandomAttempts;
        private ConfigEntry<bool> _cfgDetailLogEnabled;

        private void SetupConfig()
        {
            KeyCode key;
            if (!Enum.TryParse(_settings.CaptureKey, true, out key)) key = KeyCode.None;
            _cfgEnabled = Config.Bind("00. General", "Enabled", _settings.Enabled, "このプラグインの撮影機能を有効にします。");
            _cfgDevelopmentHotkeyEnabled = Config.Bind("00. General", "Development Hotkey Enabled", _settings.DevelopmentHotkeyEnabled, "開発中の手動撮影用ホットキーを有効にします。");
            _cfgCaptureKey = Config.Bind("00. General", "Capture Key", new KeyboardShortcut(key), "手動で撮影を実行するキーです。");
            _cfgTargetFemaleIndex = Config.Bind("00. General", "Target Female Index", _settings.TargetFemaleIndex, new ConfigDescription("撮影対象の女性番号です。0が1人目、1が2人目です。", new AcceptableValueRange<int>(0, 9)));
            _cfgLookAtCameraDuringCapture = Config.Bind("00. General", "Look At Camera During Capture", _settings.LookAtCameraDuringCapture, "撮影中だけ対象女性の目線を撮影カメラへ向け、終了後に元へ戻します。");
            _cfgEyeLookPatternDuringCapture = Config.Bind("00. General", "Eye Look Pattern During Capture", _settings.EyeLookPatternDuringCapture, new ConfigDescription("撮影時に使用する視線パターン番号です。視線の向きが合わない場合は0～5を切り替えて試せます。", new AcceptableValueRange<int>(0, 5)));
            _cfgTurnHeadToCameraDuringCapture = Config.Bind("00. General", "Turn Head To Camera During Capture", _settings.TurnHeadToCameraDuringCapture, "撮影中だけ対象女性の頭を撮影カメラへ向け、終了後に元へ戻します。");
            _cfgNeckLookPatternDuringCapture = Config.Bind("00. General", "Neck Look Pattern During Capture", _settings.NeckLookPatternDuringCapture, new ConfigDescription("撮影時に使用する首向きパターン番号です。首が動かない場合は0～4を切り替えて試せます。", new AcceptableValueRange<int>(0, 4)));
            _cfgPreserveHeadTiltDuringCapture = Config.Bind("00. General", "Preserve Head Tilt During Capture", _settings.PreserveHeadTiltDuringCapture, "頭をカメラへ向ける時も、元のポーズに含まれる頭の左右への傾きを維持します。");
            _cfgHeadLookMinPitch = Config.Bind("00. General", "Head Look Min Pitch", _settings.HeadLookMinPitch, new ConfigDescription("撮影ごとに頭をカメラ方向から上下へずらす角度の最小値です。", new AcceptableValueRange<float>(-45f, 45f)));
            _cfgHeadLookMaxPitch = Config.Bind("00. General", "Head Look Max Pitch", _settings.HeadLookMaxPitch, new ConfigDescription("撮影ごとに頭をカメラ方向から上下へずらす角度の最大値です。", new AcceptableValueRange<float>(-45f, 45f)));
            _cfgHeadLookMinYaw = Config.Bind("00. General", "Head Look Min Yaw", _settings.HeadLookMinYaw, new ConfigDescription("撮影ごとに頭をカメラ方向から左右へずらす角度の最小値です。", new AcceptableValueRange<float>(-60f, 60f)));
            _cfgHeadLookMaxYaw = Config.Bind("00. General", "Head Look Max Yaw", _settings.HeadLookMaxYaw, new ConfigDescription("撮影ごとに頭をカメラ方向から左右へずらす角度の最大値です。", new AcceptableValueRange<float>(-60f, 60f)));
            _cfgHeadLookMinRoll = Config.Bind("00. General", "Head Look Min Roll", _settings.HeadLookMinRoll, new ConfigDescription("撮影ごとに頭を左右へ傾ける追加角度の最小値です。", new AcceptableValueRange<float>(-45f, 45f)));
            _cfgHeadLookMaxRoll = Config.Bind("00. General", "Head Look Max Roll", _settings.HeadLookMaxRoll, new ConfigDescription("撮影ごとに頭を左右へ傾ける追加角度の最大値です。", new AcceptableValueRange<float>(-45f, 45f)));
            _cfgHeadLookFollowPitchEnabled = Config.Bind("00. General", "Head Look Follow Pitch Enabled", _settings.HeadLookFollowPitchEnabled, "頭の上下角度を撮影カメラへ追従させます。無効時は元の上下角度を維持します。");
            _cfgHeadLookFollowYawEnabled = Config.Bind("00. General", "Head Look Follow Yaw Enabled", _settings.HeadLookFollowYawEnabled, "頭の左右角度を撮影カメラへ追従させます。無効時は元の左右角度を維持します。");
            _cfgHeadLookFollowRollEnabled = Config.Bind("00. General", "Head Look Follow Roll Enabled", _settings.HeadLookFollowRollEnabled, "頭の左右傾斜へランダムRollを加えます。無効時は元の左右傾斜を維持します。");
            _cfgHeadLookPitchDeadZoneEnabled = Config.Bind("00. General", "Head Look Pitch Dead Zone Enabled", _settings.HeadLookPitchDeadZoneEnabled, "頭のPitch抽選で使用しない角度範囲を有効にします。");
            _cfgHeadLookPitchDeadZoneMin = Config.Bind("00. General", "Head Look Pitch Dead Zone Min", _settings.HeadLookPitchDeadZoneMin, new ConfigDescription("頭のPitch抽選で使用しない範囲の最小値です。", new AcceptableValueRange<float>(-45f, 45f)));
            _cfgHeadLookPitchDeadZoneMax = Config.Bind("00. General", "Head Look Pitch Dead Zone Max", _settings.HeadLookPitchDeadZoneMax, new ConfigDescription("頭のPitch抽選で使用しない範囲の最大値です。", new AcceptableValueRange<float>(-45f, 45f)));
            _cfgHeadLookYawDeadZoneEnabled = Config.Bind("00. General", "Head Look Yaw Dead Zone Enabled", _settings.HeadLookYawDeadZoneEnabled, "頭のYaw抽選で使用しない角度範囲を有効にします。");
            _cfgHeadLookYawDeadZoneMin = Config.Bind("00. General", "Head Look Yaw Dead Zone Min", _settings.HeadLookYawDeadZoneMin, new ConfigDescription("頭のYaw抽選で使用しない範囲の最小値です。", new AcceptableValueRange<float>(-60f, 60f)));
            _cfgHeadLookYawDeadZoneMax = Config.Bind("00. General", "Head Look Yaw Dead Zone Max", _settings.HeadLookYawDeadZoneMax, new ConfigDescription("頭のYaw抽選で使用しない範囲の最大値です。", new AcceptableValueRange<float>(-60f, 60f)));
            _cfgHeadLookRollDeadZoneEnabled = Config.Bind("00. General", "Head Look Roll Dead Zone Enabled", _settings.HeadLookRollDeadZoneEnabled, "頭のRoll抽選で使用しない角度範囲を有効にします。");
            _cfgHeadLookRollDeadZoneMin = Config.Bind("00. General", "Head Look Roll Dead Zone Min", _settings.HeadLookRollDeadZoneMin, new ConfigDescription("頭のRoll抽選で使用しない範囲の最小値です。", new AcceptableValueRange<float>(-45f, 45f)));
            _cfgHeadLookRollDeadZoneMax = Config.Bind("00. General", "Head Look Roll Dead Zone Max", _settings.HeadLookRollDeadZoneMax, new ConfigDescription("頭のRoll抽選で使用しない範囲の最大値です。", new AcceptableValueRange<float>(-45f, 45f)));
            _cfgAutoRotateLyingPose = Config.Bind("00. General", "Auto Rotate Lying Pose", _settings.AutoRotateLyingPose, "寝姿では腰から頭の方向が画像の縦方向になるよう、カメラを自動回転します。");
            _cfgMoveDuration = Config.Bind("10. Timing", "Move Duration", _settings.MoveDuration, new ConfigDescription("現在位置から撮影位置へカメラを移動する秒数です。0で瞬間移動します。", new AcceptableValueRange<float>(0f, 5f)));
            _cfgHoldBeforeCapture = Config.Bind("10. Timing", "Hold Before Capture", _settings.HoldBeforeCapture, new ConfigDescription("撮影位置へ移動してから画像を保存するまで待つ秒数です。", new AcceptableValueRange<float>(0f, 2f)));
            _cfgReturnDuration = Config.Bind("10. Timing", "Return Duration", _settings.ReturnDuration, new ConfigDescription("撮影後に元のカメラ位置へ戻る秒数です。0で瞬時に戻ります。", new AcceptableValueRange<float>(0f, 5f)));
            _cfgReturnCamera = Config.Bind("10. Timing", "Return Camera After Capture", _settings.ReturnCameraAfterCapture, new ConfigDescription("撮影後に元のカメラ位置へ戻します。offにすると撮影した構図のまま留まります。"));
            _cfgForceKeepFov = Config.Bind("10. Timing", "Force Keep Capture Fov", _settings.ForceKeepCaptureFov, new ConfigDescription("撮影後の画角を毎フレーム強制的に維持します。他プラグインが必要があってFOVを操作している場合、それを妨げるため既定はoffです。"));
            _cfgCaptureWidth = Config.Bind("20. Output", "Capture Width", _settings.CaptureWidth, new ConfigDescription("保存するPNG画像の横幅（ピクセル）です。", new AcceptableValueRange<int>(320, 7680)));
            _cfgCaptureHeight = Config.Bind("20. Output", "Capture Height", _settings.CaptureHeight, new ConfigDescription("保存するPNG画像の高さ（ピクセル）です。", new AcceptableValueRange<int>(240, 4320)));
            _cfgOutputDirectory = Config.Bind("20. Output", "Output Directory", _settings.OutputDirectory, "画像の保存先です。相対パスはこのプラグインのフォルダを基準にします。");
            _cfgMinYaw = Config.Bind("30. Random", "Min Yaw", _settings.MinYaw, "身体の正面を0度とした、左右方向のランダム範囲の最小値です。");
            _cfgMaxYaw = Config.Bind("30. Random", "Max Yaw", _settings.MaxYaw, "身体の正面を0度とした、左右方向のランダム範囲の最大値です。");
            _cfgHeadDirectionPriority = Config.Bind("30. Random", "Head Direction Priority", _settings.HeadDirectionPriority, new ConfigDescription("カメラ基準方向を作るときの頭・顔方向の優先度です。大きいほど顔が向く側へ寄ります。", new AcceptableValueRange<float>(0f, 1f)));
            _cfgHipDirectionPriority = Config.Bind("30. Random", "Hip Direction Priority", _settings.HipDirectionPriority, new ConfigDescription("カメラ基準方向を作るときの腰方向の優先度です。大きいほど腰の正面側へ寄ります。", new AcceptableValueRange<float>(0f, 1f)));
            _cfgDirectionPriorityRandomness = Config.Bind("30. Random", "Direction Priority Randomness", _settings.DirectionPriorityRandomness, new ConfigDescription("撮影候補ごとに頭と腰の優先度を揺らす割合です。0で固定、1で最大限変化します。", new AcceptableValueRange<float>(0f, 1f)));
            _cfgMinPitch = Config.Bind("30. Random", "Min Pitch", _settings.MinPitch, "身体の正面を0度とした、上下方向のランダム範囲の最小値です。負の値ほど下側から撮ります。");
            _cfgMaxPitch = Config.Bind("30. Random", "Max Pitch", _settings.MaxPitch, "身体の正面を0度とした、上下方向のランダム範囲の最大値です。正の値ほど上側から撮ります。");
            _cfgMinRoll = Config.Bind("30. Random", "Min Roll", _settings.MinRoll, "画像全体を斜めに傾ける角度の最小値です。0で傾けません。");
            _cfgMaxRoll = Config.Bind("30. Random", "Max Roll", _settings.MaxRoll, "画像全体を斜めに傾ける角度の最大値です。最小値から最大値の間でランダムに選びます。");
            _cfgMinFov = Config.Bind("30. Random", "Min FOV", _settings.MinFov, "ランダムに選ぶ画角の最小値です。小さいほど望遠になります。");
            _cfgMaxFov = Config.Bind("30. Random", "Max FOV", _settings.MaxFov, "ランダムに選ぶ画角の最大値です。大きいほど広角になります。");
            _cfgMinDistance = Config.Bind("30. Random", "Min Distance", _settings.MinDistance, "撮影中心からカメラまでの最短距離です。");
            _cfgMaxDistance = Config.Bind("30. Random", "Max Distance", _settings.MaxDistance, "撮影中心からカメラまでの最長距離です。");
            _cfgViewportMargin = Config.Bind("30. Random", "Viewport Margin", _settings.ViewportMargin, new ConfigDescription("頭・股間・両肩を画面端から離す余白です。0.08は各辺8％です。", new AcceptableValueRange<float>(0.02f, 0.3f)));
            _cfgHeadUpperMargin = Config.Bind("30. Random", "Head Upper Margin", _settings.HeadUpperMargin, new ConfigDescription("頭の位置から身体の上方向へ追加する余白です。腰から頭までの長さに対する比率です。", new AcceptableValueRange<float>(0f, 0.5f)));
            _cfgCrotchLowerMargin = Config.Bind("30. Random", "Crotch Lower Margin", _settings.CrotchLowerMargin, new ConfigDescription("股間位置から身体の下方向へ追加する余白。腰から頭までの長さに対する比率。", new AcceptableValueRange<float>(0f, 0.5f)));
            _cfgRandomAttempts = Config.Bind("30. Random", "Random Attempts", _settings.RandomAttempts, new ConfigDescription("条件を満たす構図を探すために、ランダムな候補を試す回数です。", new AcceptableValueRange<int>(1, 50)));
            _cfgDetailLogEnabled = Config.Bind("90. Logging", "Detail Log Enabled", _settings.DetailLogEnabled, "骨の検出結果や身体座標など、問題調査用の詳しいログを出します。");
            Config.SettingChanged += OnConfigChanged;
            SaveConfigToJson();
        }

        private void OnConfigChanged(object sender, EventArgs args)
        {
            SaveConfigToJson();
        }

        private void SaveConfigToJson()
        {
            _settings.Enabled = _cfgEnabled.Value;
            _settings.DevelopmentHotkeyEnabled = _cfgDevelopmentHotkeyEnabled.Value;
            _settings.CaptureKey = _cfgCaptureKey.Value.MainKey.ToString();
            _settings.TargetFemaleIndex = _cfgTargetFemaleIndex.Value;
            _settings.LookAtCameraDuringCapture = _cfgLookAtCameraDuringCapture.Value;
            _settings.EyeLookPatternDuringCapture = _cfgEyeLookPatternDuringCapture.Value;
            _settings.TurnHeadToCameraDuringCapture = _cfgTurnHeadToCameraDuringCapture.Value;
            _settings.NeckLookPatternDuringCapture = _cfgNeckLookPatternDuringCapture.Value;
            _settings.PreserveHeadTiltDuringCapture = _cfgPreserveHeadTiltDuringCapture.Value;
            _settings.HeadLookMinPitch = Round2(_cfgHeadLookMinPitch.Value);
            _settings.HeadLookMaxPitch = Round2(_cfgHeadLookMaxPitch.Value);
            _settings.HeadLookMinYaw = Round2(_cfgHeadLookMinYaw.Value);
            _settings.HeadLookMaxYaw = Round2(_cfgHeadLookMaxYaw.Value);
            _settings.HeadLookMinRoll = Round2(_cfgHeadLookMinRoll.Value);
            _settings.HeadLookMaxRoll = Round2(_cfgHeadLookMaxRoll.Value);
            _settings.HeadLookFollowPitchEnabled = _cfgHeadLookFollowPitchEnabled.Value;
            _settings.HeadLookFollowYawEnabled = _cfgHeadLookFollowYawEnabled.Value;
            _settings.HeadLookFollowRollEnabled = _cfgHeadLookFollowRollEnabled.Value;
            _settings.HeadLookPitchDeadZoneEnabled = _cfgHeadLookPitchDeadZoneEnabled.Value;
            _settings.HeadLookPitchDeadZoneMin = Round2(_cfgHeadLookPitchDeadZoneMin.Value);
            _settings.HeadLookPitchDeadZoneMax = Round2(_cfgHeadLookPitchDeadZoneMax.Value);
            _settings.HeadLookYawDeadZoneEnabled = _cfgHeadLookYawDeadZoneEnabled.Value;
            _settings.HeadLookYawDeadZoneMin = Round2(_cfgHeadLookYawDeadZoneMin.Value);
            _settings.HeadLookYawDeadZoneMax = Round2(_cfgHeadLookYawDeadZoneMax.Value);
            _settings.HeadLookRollDeadZoneEnabled = _cfgHeadLookRollDeadZoneEnabled.Value;
            _settings.HeadLookRollDeadZoneMin = Round2(_cfgHeadLookRollDeadZoneMin.Value);
            _settings.HeadLookRollDeadZoneMax = Round2(_cfgHeadLookRollDeadZoneMax.Value);
            _settings.AutoRotateLyingPose = _cfgAutoRotateLyingPose.Value;
            _settings.MoveDuration = Round2(_cfgMoveDuration.Value);
            _settings.HoldBeforeCapture = Round2(_cfgHoldBeforeCapture.Value);
            _settings.ReturnDuration = Round2(_cfgReturnDuration.Value);
            _settings.ReturnCameraAfterCapture = _cfgReturnCamera.Value;
            _settings.ForceKeepCaptureFov = _cfgForceKeepFov.Value;
            _settings.CaptureWidth = _cfgCaptureWidth.Value;
            _settings.CaptureHeight = _cfgCaptureHeight.Value;
            _settings.OutputDirectory = _cfgOutputDirectory.Value ?? "captures";
            _settings.MinYaw = Round2(_cfgMinYaw.Value); _settings.MaxYaw = Round2(_cfgMaxYaw.Value);
            _settings.HeadDirectionPriority = Round2(_cfgHeadDirectionPriority.Value);
            _settings.HipDirectionPriority = Round2(_cfgHipDirectionPriority.Value);
            _settings.DirectionPriorityRandomness = Round2(_cfgDirectionPriorityRandomness.Value);
            _settings.MinPitch = Round2(_cfgMinPitch.Value); _settings.MaxPitch = Round2(_cfgMaxPitch.Value);
            _settings.MinRoll = Round2(_cfgMinRoll.Value); _settings.MaxRoll = Round2(_cfgMaxRoll.Value);
            _settings.MinFov = Round2(_cfgMinFov.Value); _settings.MaxFov = Round2(_cfgMaxFov.Value);
            _settings.MinDistance = Round2(_cfgMinDistance.Value); _settings.MaxDistance = Round2(_cfgMaxDistance.Value);
            _settings.ViewportMargin = Round2(_cfgViewportMargin.Value);
            _settings.HeadUpperMargin = Round2(_cfgHeadUpperMargin.Value);
            _settings.CrotchLowerMargin = Round2(_cfgCrotchLowerMargin.Value);
            _settings.RandomAttempts = _cfgRandomAttempts.Value;
            _settings.DetailLogEnabled = _cfgDetailLogEnabled.Value;
            try { SettingsStore.Save(_settingsPath, _settings); }
            catch (Exception ex) { LogError("settings save failed: " + ex); }
        }

        private static float Round2(float value)
        {
            return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }
    }
}
