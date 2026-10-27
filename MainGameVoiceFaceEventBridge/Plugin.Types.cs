using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MainGameVoiceFaceEventBridge
{
    // Plugin.Types.cs
    //
    // 責務: プラグイン内部で使う型定義（DataContract / 値オブジェクト / 状態スナップショット）を集約する。
    //       ロジック・配線・データ定義は他ファイルに置く。
    internal sealed partial class Plugin
    {
        private sealed class VideoTrackGroup
        {
            public string CanonicalName;
            public string NormalizedName;
            public readonly List<string> FileNames = new List<string>();
        }

        private sealed class VideoTriggerHit
        {
            public int Index;
            public string Keyword;
        }

        private sealed class CameraPresetTriggerHit
        {
            public int LineIndex;
            public int MatchIndex;
            public string TriggerKeyword;
            public string PresetName;
        }

        [DataContract]
        private sealed class PoseKeywordScoreToken
        {
            [DataMember(Name = "keyword")]
            public string Keyword;

            [DataMember(Name = "score")]
            public int Score;
        }

        [DataContract]
        private sealed class PoseKeywordScoreRule
        {
            [DataMember(Name = "id")]
            public string RuleId;

            [DataMember(Name = "category")]
            public string Category;

            [DataMember(Name = "poseNames")]
            public string[] PoseNames;

            [DataMember(Name = "tokens")]
            public PoseKeywordScoreToken[] Tokens;

            [DataMember(Name = "priority")]
            public int Priority;

            [DataMember(Name = "enabled", EmitDefaultValue = false)]
            public bool? Enabled;
        }

        [DataContract]
        private sealed class PoseScoreRulesFile
        {
            [DataMember(Name = "version")]
            public int Version;

            [DataMember(Name = "enabled", EmitDefaultValue = false)]
            public bool? Enabled;

            [DataMember(Name = "simpleModeEnabled", EmitDefaultValue = false)]
            public bool? SimpleModeEnabled;

            [DataMember(Name = "simpleModeTriggerKeywords", EmitDefaultValue = false)]
            public string SimpleModeTriggerKeywords;

            [DataMember(Name = "rulesEnabled", EmitDefaultValue = false)]
            public bool? RulesEnabled;

            [DataMember(Name = "inferRulesEnabled", EmitDefaultValue = false)]
            public bool? InferRulesEnabled;

            [DataMember(Name = "scoreBase")]
            public int ScoreBase;

            [DataMember(Name = "adoptThreshold")]
            public int AdoptThreshold;

            [DataMember(Name = "forceThreshold")]
            public int ForceThreshold;

            [DataMember(Name = "rules")]
            public List<PoseKeywordScoreRule> Rules;

            [DataMember(Name = "inferRules")]
            public List<PoseCategoryInferRule> InferRules;

            [DataMember(Name = "categoryEnabled", EmitDefaultValue = false)]
            public Dictionary<string, bool> CategoryEnabled;

            [DataMember(Name = "categoryAliases", EmitDefaultValue = false)]
            public Dictionary<string, string[]> CategoryAliases;
        }

        [DataContract]
        private sealed class PoseCategoryInferRule
        {
            [DataMember(Name = "id")]
            public string RuleId;

            [DataMember(Name = "targetCategory")]
            public string TargetCategory;

            [DataMember(Name = "priority")]
            public int Priority;

            [DataMember(Name = "requiredAll")]
            public string[] RequiredAll;

            [DataMember(Name = "requiredAny")]
            public string[] RequiredAny;

            [DataMember(Name = "excludeAny")]
            public string[] ExcludeAny;

            [DataMember(Name = "enabled", EmitDefaultValue = false)]
            public bool? Enabled;
        }

        [DataContract]
        private sealed class BlankMapAddSettingsSnapshot
        {
            [DataMember(Name = "FolderPlayPath")]
            public string FolderPlayPath;

            [DataMember(Name = "HttpEnabled", EmitDefaultValue = false)]
            public bool? HttpEnabled;

            [DataMember(Name = "HttpPort", EmitDefaultValue = false)]
            public int? HttpPort;
        }

        private sealed class PoseScoreMatch
        {
            public string Category;
            public PoseKeywordScoreRule Rule;
            public List<PoseCategoryEntry> Candidates;
            public int Score;
            public int LongestMatch;
            public int Priority;
            public int MatchedTokenCount;
        }

        [DataContract]
        private sealed class PoseClassificationFile
        {
            [DataMember(Name = "categories")]
            public Dictionary<string, List<PoseClassificationItem>> Categories;
        }

        [DataContract]
        private sealed class PoseListFile
        {
            [DataMember(Name = "generatedAt")]
            public string GeneratedAt;

            [DataMember(Name = "source")]
            public string Source;

            [DataMember(Name = "poses")]
            public List<PoseListItem> Poses;
        }

        [DataContract]
        private sealed class PoseListItem
        {
            [DataMember(Name = "id")]
            public int Id;

            [DataMember(Name = "mode")]
            public string Mode;

            [DataMember(Name = "modeInt")]
            public int ModeInt;

            [DataMember(Name = "nameAnimation")]
            public string NameAnimation;
        }

        [DataContract]
        private sealed class PoseTranslatedListFile
        {
            [DataMember(Name = "poses")]
            public List<PoseTranslatedListItem> Poses;
        }

        [DataContract]
        private sealed class PoseTranslatedListItem
        {
            [DataMember(Name = "modeInt")]
            public int ModeInt;

            [DataMember(Name = "nameAnimation")]
            public string NameAnimation;

            [DataMember(Name = "nameAnimationOriginal")]
            public string NameAnimationOriginal;
        }

        [DataContract]
        private sealed class PoseClassificationItem
        {
            [DataMember(Name = "nameAnimation")]
            public string NameAnimation;

            [DataMember(Name = "modeInt")]
            public int ModeInt;
        }

        [DataContract]
        private sealed class FacePresetJsonFile
        {
            [DataMember(Name = "Presets")]
            public List<FacePresetJsonItem> Presets = new List<FacePresetJsonItem>();
        }

        [DataContract]
        private sealed class FacePresetJsonItem
        {
            [DataMember(Name = "Id")]
            public string Id;

            [DataMember(Name = "Name")]
            public string Name;

            [DataMember(Name = "Eyebrow")]
            public int Eyebrow;

            [DataMember(Name = "Eye")]
            public int Eye;

            [DataMember(Name = "Mouth")]
            public int Mouth;

            [DataMember(Name = "EyeMin")]
            public float EyeMin;

            [DataMember(Name = "EyeMax")]
            public float EyeMax = 1f;

            [DataMember(Name = "MouthMin")]
            public float MouthMin;

            [DataMember(Name = "MouthMax")]
            public float MouthMax = 1f;

            [DataMember(Name = "Tears")]
            public int Tears;

            [DataMember(Name = "Cheek")]
            public float Cheek;
        }

        private sealed class FaceSnapshot
        {
            public int Eyebrow;
            public int Eyes;
            public int Mouth;
            public float EyesOpenMin;
            public float EyesOpenMax;
            public float MouthOpenMin;
            public float MouthOpenMax;
            public float MouthFixedRate;
            public int Tears;
            public float Cheek;
        }

        private sealed class FacePresetProbeState
        {
            public int ProbeId;
            public ChaControl Target;
            public string RequestedName;
            public string RequestedId;
            public bool RequestedRandom;
            public string SelectedName;
            public string SelectedId;
            public int StartFrame;
            public int ExpireFrame;
            public float StartTime;
            public FaceSnapshot Baseline;
            public FaceSnapshot Last;
            public int ChangeLogCount;
            public int HookLogCount;
            public int LastHookFrame;
            public string LastHookKey;
        }

        private sealed class PoseCategoryEntry
        {
            public string NameAnimation;
            public int ModeInt;
        }

        private sealed class PoseContinuationState
        {
            public bool ShouldContinue;
            public bool ShouldRestoreAuto;
            public HFlag.EMode PreviousMode;
            public HFlag.EMode TargetMode;
            public int TargetId;
            public string TargetName;
            public string PreviousStateName;
            public bool TargetSonyu;
            public bool TargetHoushi;
            public bool WasAnal;
            public bool WasLoop;
            public bool WasStrongLoop;
            public float SpeedCalc;
            public int SpeedHoushi;
            public bool VoiceSpeedMotion;
            public bool WasAuto;
            public int Attempts;
        }

        private sealed class TimedClothesItem
        {
            public ClothesItem Item;
            public int MatchIndex;
            public string PartKeyword;
            public string ActionKeyword;
        }

        private sealed class TimedCoordItem
        {
            public string CoordName;
            public int MatchIndex;
            public string TriggerKeyword;
        }

        private sealed class TextLineSpan
        {
            public string Line;
            public int StartIndex;
        }
    }
}
