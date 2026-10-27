using System.Runtime.Serialization;

namespace MainGameVoiceFaceEventBridge
{
    [DataContract]
    internal sealed class PluginSettings
    {
        internal const float MaxFemalePlaybackVolume = 5f;
        [DataMember] public bool Enabled = true;
        [DataMember] public bool VerboseLog = false;
        [DataMember] public bool ReceiveTimingEnabled = false;
        [DataMember] public bool EnableCtrlRReload = true;
        [DataMember] public string ReloadKey = "R + LeftControl";
        [DataMember] public string StateDumpKey = "F8 + LeftControl + LeftShift";

        [DataMember] public bool EnablePipeServer = true;
        [DataMember] public string PipeName = "kks_voice_face_events";
        [DataMember] public int MaxQueuedCommands = 64;
        [DataMember] public bool AcceptJsonCommand = true;
        [DataMember] public bool AcceptPlainAudioPath = true;
        [DataMember] public bool AcceptPlainAssetName = false;
        [DataMember] public bool StopGameVoiceBeforeExternalPlay = true;
        [DataMember] public bool BlockGameVoiceWhileExternalPlaying = true;
        [DataMember] public float ExternalPlayPreBlockSeconds = 0.25f;

        [DataMember] public bool IgnoreCommandOutsideHScene = true;
        [DataMember] public int TargetMainIndex = 0;

        [DataMember] public string DefaultAssetBundle = "abdata\\sound\\data\\pcm\\c13\\h\\01.unity3d";
        [DataMember] public string DefaultAssetName = "h_ko_13_03_001";
        [DataMember] public float DefaultPitch = 1.0f;
        [DataMember] public float DefaultFadeTime = 0.0f;
        [DataMember] public int DefaultEyeNeck = 0;
        [DataMember] public int DefaultVoiceKind = 0;
        [DataMember] public int DefaultAction = 0;
        [DataMember] public bool DefaultInterruptCurrent = true;
        [DataMember] public bool DeleteAudioAfterPlayback = false;
        [DataMember] public float PlaybackVolume = 1.0f;
        [DataMember] public float FemalePlaybackVolume = 1.0f;

        internal float ResolveFemalePlaybackVolume()
        {
            float value = FemalePlaybackVolume < 0f ? PlaybackVolume : FemalePlaybackVolume;
            return System.Math.Max(0f, System.Math.Min(MaxFemalePlaybackVolume, value));
        }
        [DataMember] public float ExternalPlaybackPitch = 1.0f;

        [DataMember] public string TopKeywords = "上着,ジャケット,トップス";
        [DataMember] public string BottomKeywords = "スカート,ホットパンツ,ミニスカ,ボトムス,ズボン,パンツ";
        [DataMember] public string BraKeywords = "ブラ";
        [DataMember] public string ShortsKeywords = "パンティー,パンティ";
        [DataMember] public string GlovesKeywords = "グローブ,手袋";
        [DataMember] public string PanthoseKeywords = "ガーターベルト,パンスト,ガーター";
        [DataMember] public string SocksKeywords = "ストッキング,ニーハイ,靴下";
        [DataMember] public string ShoesKeywords = "ハイヒール,スニーカー,サンダル,ヒール,靴";
        [DataMember] public string GlassesKeywords = "メガネ,眼鏡,めがね";
        [DataMember] public string RemoveKeywords = "脱ぐね,脱いじゃう";
        [DataMember] public string ShiftKeywords = "ずらすね,半脱ぎにするね";
        [DataMember] public string ShiftSecondKeywords = "下げるね";
        [DataMember] public string PutOnKeywords = "着るね,付けるね";
        [DataMember] public string RemoveAllKeywords = "全裸になるね,全部脱ぐね,全部脱いじゃう";
        [DataMember] public string PutOnAllKeywords = "全部着るね";
        [DataMember] public string CoordPattern = "に着替えるね";
        [DataMember] public string CameraTriggerKeywords = "カメラにして,カメラ切り替えて,視点にして,視点切り替えて,アングルにして,で見せて";
        [DataMember] public bool EnableVideoPlaybackByResponseText = true;
        [DataMember] public string VideoPlaybackTriggerKeywords = "流す";
        [DataMember] public string AstralModeTriggerKeywords = "アストラル";
        [DataMember] public string RealWorldTriggerKeywords = "現実";
        [DataMember] public string BlankMapAddSettingsRelativePath = "..\\MainGameBlankMapAdd\\MapAddSettings.json";
        [DataMember] public string VideoPlayEndpointPath = "/videoroom/play";
        [DataMember] public int BlankMapAddHttpPort = 55982;
        [DataMember] public string VideoFileExtensions = ".mp4,.wmv,.avi,.mkv,.mov,.m4v,.webm";
        [DataMember] public bool SequenceSubtitleEnabled = true;
        [DataMember] public string SequenceSubtitleHost = "127.0.0.1";
        [DataMember] public int SequenceSubtitlePort = 18766;
        [DataMember] public string SequenceSubtitleEndpointPath = "/subtitle-event";
        [DataMember] public string SequenceSubtitleToken = "";
        [DataMember] public string SequenceSubtitleBackend = "Overlay";
        [DataMember] public string SequenceSubtitleDisplayMode = "StackFemale";
        [DataMember] public string SequenceSubtitleSendMode = "PerLine";
        [DataMember] public float SequenceSubtitleHoldPaddingSeconds = 0.2f;
        [DataMember] public bool SequenceSubtitleProgressPrefixEnabled = true;

        [DataMember] public bool ScenarioTextEnabled = false;
        [DataMember] public bool ScenarioManualSendRequested = false;
        [DataMember] public bool ScenarioAutoSendEnabled = false;
        [DataMember] public float ScenarioAutoSendIntervalSeconds = 60.0f;
        // true: current face is kept by default (face=-1 on PlayVoice).
        // false: use explicit/random face selection.
        [DataMember] public bool KeepCurrentFaceByDefault = false;

        // If >= 0, fixed face. If < 0, choose from RandomFaceCandidates.
        [DataMember] public int DefaultFace = -1;
        [DataMember] public int[] RandomFaceCandidates = new[] { 0 };

        [DataMember] public bool EnableFacePresetApply = true;
        [DataMember] public string FacePresetJsonRelativePath = "..\\..\\StudioFacePresetTool\\StudioFacePresets.json";

        [DataMember] public bool EnableSubCameraPresetForward = false;
        [DataMember] public bool EnableMorphBridgeTriggers = true;
        [DataMember] public string MorphBridgeTransformKeywords = "変身するね";
        [DataMember] public string MorphBridgeResetKeywords = "元に戻るね";
        [DataMember] public float MorphBridgeTransitionSeconds = 5.0f;
        // 合言葉とカードの対応。"合言葉=カードPNGのフルパス" を1行1件。
        // 合言葉が発話に出たら、その場でそのカードへ変身する（変身キーワードは不要）。
        [DataMember] public string[] MorphBridgeCards = new string[0];

        private const string DefaultPipeName = "kks_voice_face_events";
        private const string LegacyDiagnosticPipeName = "kks_voice_face_events_diag_0423";

        internal void Normalize()
        {
            PipeName = NormalizePipeName(PipeName);

            if (TargetMainIndex < 0)
            {
                TargetMainIndex = 0;
            }

            if (MaxQueuedCommands < 1)
            {
                MaxQueuedCommands = 1;
            }

            if (MaxQueuedCommands > 1024)
            {
                MaxQueuedCommands = 1024;
            }

            if (ExternalPlayPreBlockSeconds < 0f)
            {
                ExternalPlayPreBlockSeconds = 0f;
            }

            if (ExternalPlayPreBlockSeconds > 2f)
            {
                ExternalPlayPreBlockSeconds = 2f;
            }

            if (DefaultPitch < 0f)
            {
                DefaultPitch = 0f;
            }

            if (DefaultPitch > 3f)
            {
                DefaultPitch = 3f;
            }

            if (DefaultFadeTime < 0f)
            {
                DefaultFadeTime = 0f;
            }

            if (PlaybackVolume < 0f)
            {
                PlaybackVolume = 0f;
            }

            if (PlaybackVolume > 1f)
            {
                PlaybackVolume = 1f;
            }

            if (FemalePlaybackVolume < -1f)
            {
                FemalePlaybackVolume = -1f;
            }

            if (FemalePlaybackVolume > MaxFemalePlaybackVolume)
            {
                FemalePlaybackVolume = MaxFemalePlaybackVolume;
            }

            if (ExternalPlaybackPitch < 0.1f)
            {
                ExternalPlaybackPitch = 0.1f;
            }

            if (ExternalPlaybackPitch > 3f)
            {
                ExternalPlaybackPitch = 3f;
            }

            if (RandomFaceCandidates == null)
            {
                RandomFaceCandidates = new int[0];
            }

            if (string.IsNullOrWhiteSpace(ReloadKey))
            {
                ReloadKey = "R + LeftControl";
            }

            if (string.IsNullOrWhiteSpace(StateDumpKey))
            {
                StateDumpKey = "F8 + LeftControl + LeftShift";
            }

            if (string.IsNullOrWhiteSpace(TopKeywords)) TopKeywords = "上着,ジャケット,トップス";
            if (string.IsNullOrWhiteSpace(BottomKeywords)) BottomKeywords = "スカート,ホットパンツ,ミニスカ,ボトムス,ズボン,パンツ";
            if (string.IsNullOrWhiteSpace(BraKeywords)) BraKeywords = "ブラ";
            if (string.IsNullOrWhiteSpace(ShortsKeywords)) ShortsKeywords = "パンティー,パンティ";
            if (string.IsNullOrWhiteSpace(GlovesKeywords)) GlovesKeywords = "グローブ,手袋";
            if (string.IsNullOrWhiteSpace(PanthoseKeywords)) PanthoseKeywords = "ガーターベルト,パンスト,ガーター";
            if (string.IsNullOrWhiteSpace(SocksKeywords)) SocksKeywords = "ストッキング,ニーハイ,靴下";
            if (string.IsNullOrWhiteSpace(ShoesKeywords)) ShoesKeywords = "ハイヒール,スニーカー,サンダル,ヒール,靴";
            if (string.IsNullOrWhiteSpace(GlassesKeywords)) GlassesKeywords = "メガネ,眼鏡,めがね";
            if (string.IsNullOrWhiteSpace(RemoveKeywords)) RemoveKeywords = "脱ぐね,脱いじゃう";
            if (string.IsNullOrWhiteSpace(ShiftKeywords)) ShiftKeywords = "ずらすね,半脱ぎにするね";
            if (string.IsNullOrWhiteSpace(ShiftSecondKeywords)) ShiftSecondKeywords = "下げるね";
            if (string.IsNullOrWhiteSpace(PutOnKeywords)) PutOnKeywords = "着るね,付けるね";
            if (string.IsNullOrWhiteSpace(RemoveAllKeywords)) RemoveAllKeywords = "全裸になるね,全部脱ぐね,全部脱いじゃう";
            if (string.IsNullOrWhiteSpace(PutOnAllKeywords)) PutOnAllKeywords = "全部着るね";
            if (string.IsNullOrWhiteSpace(CoordPattern)) CoordPattern = "に着替えるね";
            if (string.IsNullOrWhiteSpace(CameraTriggerKeywords)) CameraTriggerKeywords = "カメラにして,カメラ切り替えて,視点にして,視点切り替えて,アングルにして,で見せて";
            if (string.IsNullOrWhiteSpace(VideoPlaybackTriggerKeywords)) VideoPlaybackTriggerKeywords = "流す";
            if (string.IsNullOrWhiteSpace(AstralModeTriggerKeywords)) AstralModeTriggerKeywords = "アストラル";
            if (string.IsNullOrWhiteSpace(RealWorldTriggerKeywords)) RealWorldTriggerKeywords = "現実";
            if (string.IsNullOrWhiteSpace(BlankMapAddSettingsRelativePath)) BlankMapAddSettingsRelativePath = "..\\MainGameBlankMapAdd\\MapAddSettings.json";
            if (string.IsNullOrWhiteSpace(VideoPlayEndpointPath)) VideoPlayEndpointPath = "/videoroom/play";
            if (!VideoPlayEndpointPath.StartsWith("/", System.StringComparison.Ordinal)) VideoPlayEndpointPath = "/" + VideoPlayEndpointPath;
            if (BlankMapAddHttpPort <= 0 || BlankMapAddHttpPort > 65535) BlankMapAddHttpPort = 55982;
            if (string.IsNullOrWhiteSpace(VideoFileExtensions)) VideoFileExtensions = ".mp4,.wmv,.avi,.mkv,.mov,.m4v,.webm";
            if (string.IsNullOrWhiteSpace(SequenceSubtitleHost)) SequenceSubtitleHost = "127.0.0.1";
            SequenceSubtitleHost = SequenceSubtitleHost.Trim();
            if (SequenceSubtitlePort <= 0 || SequenceSubtitlePort > 65535) SequenceSubtitlePort = 18766;
            if (string.IsNullOrWhiteSpace(SequenceSubtitleEndpointPath)) SequenceSubtitleEndpointPath = "/subtitle-event";
            SequenceSubtitleEndpointPath = SequenceSubtitleEndpointPath.Trim();
            if (!SequenceSubtitleEndpointPath.StartsWith("/", System.StringComparison.Ordinal)) SequenceSubtitleEndpointPath = "/" + SequenceSubtitleEndpointPath;
            if (SequenceSubtitleToken == null) SequenceSubtitleToken = "";
            if (!string.Equals(SequenceSubtitleBackend, "Auto", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(SequenceSubtitleBackend, "InformationUI", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(SequenceSubtitleBackend, "Overlay", System.StringComparison.OrdinalIgnoreCase))
            {
                SequenceSubtitleBackend = "Overlay";
            }
            if (string.IsNullOrWhiteSpace(SequenceSubtitleDisplayMode)) SequenceSubtitleDisplayMode = "StackFemale";
            SequenceSubtitleDisplayMode = SequenceSubtitleDisplayMode.Trim();
            if (string.IsNullOrWhiteSpace(SequenceSubtitleSendMode)) SequenceSubtitleSendMode = "PerLine";
            SequenceSubtitleSendMode = SequenceSubtitleSendMode.Trim();
            if (!string.Equals(SequenceSubtitleSendMode, "PerLine", System.StringComparison.OrdinalIgnoreCase)
                && !string.Equals(SequenceSubtitleSendMode, "FullTextOnce", System.StringComparison.OrdinalIgnoreCase))
            {
                SequenceSubtitleSendMode = "PerLine";
            }
            else if (string.Equals(SequenceSubtitleSendMode, "FullTextOnce", System.StringComparison.OrdinalIgnoreCase))
            {
                SequenceSubtitleSendMode = "FullTextOnce";
            }
            else
            {
                SequenceSubtitleSendMode = "PerLine";
            }
            if (SequenceSubtitleHoldPaddingSeconds < 0f) SequenceSubtitleHoldPaddingSeconds = 0f;
            if (SequenceSubtitleHoldPaddingSeconds > 5f) SequenceSubtitleHoldPaddingSeconds = 5f;
            if (string.IsNullOrWhiteSpace(FacePresetJsonRelativePath)) FacePresetJsonRelativePath = "..\\..\\StudioFacePresetTool\\StudioFacePresets.json";
            if (string.IsNullOrWhiteSpace(MorphBridgeTransformKeywords)) MorphBridgeTransformKeywords = "変身するね";
            if (string.IsNullOrWhiteSpace(MorphBridgeResetKeywords)) MorphBridgeResetKeywords = "元に戻るね";
            if (MorphBridgeTransitionSeconds < 0f) MorphBridgeTransitionSeconds = 0f;
            if (MorphBridgeTransitionSeconds > 60f) MorphBridgeTransitionSeconds = 60f;
            if (ScenarioAutoSendIntervalSeconds < 2f) ScenarioAutoSendIntervalSeconds = 2f;
            if (ScenarioAutoSendIntervalSeconds > 300f) ScenarioAutoSendIntervalSeconds = 300f;
            ExternalPlayPreBlockSeconds = Round2(ExternalPlayPreBlockSeconds);
            DefaultPitch = Round2(DefaultPitch);
            DefaultFadeTime = Round2(DefaultFadeTime);
            PlaybackVolume = Round2(PlaybackVolume);
            FemalePlaybackVolume = Round2(FemalePlaybackVolume);
            ExternalPlaybackPitch = Round2(ExternalPlaybackPitch);
            SequenceSubtitleHoldPaddingSeconds = Round2(SequenceSubtitleHoldPaddingSeconds);
            MorphBridgeTransitionSeconds = Round2(MorphBridgeTransitionSeconds);
            ScenarioAutoSendIntervalSeconds = Round2(ScenarioAutoSendIntervalSeconds);
        }

        private static float Round2(float value)
        {
            return (float)System.Math.Round(value, 2, System.MidpointRounding.AwayFromZero);
        }

        private static string NormalizePipeName(string pipeName)
        {
            string value = string.IsNullOrWhiteSpace(pipeName) ? DefaultPipeName : pipeName.Trim();
            const string prefix = @"\\.\pipe\";
            if (value.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(prefix.Length).Trim();
            }

            if (string.Equals(value, LegacyDiagnosticPipeName, System.StringComparison.OrdinalIgnoreCase))
            {
                return DefaultPipeName;
            }

            return string.IsNullOrWhiteSpace(value) ? DefaultPipeName : value;
        }
    }
}
