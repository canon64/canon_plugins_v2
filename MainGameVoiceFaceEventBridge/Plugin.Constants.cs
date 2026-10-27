using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    // Plugin.Constants.cs
    //
    // 責務: コマンドキーワードや既定値、組み込みの体位スコア/推定ルール一覧、
    //       セクション名などの定数・静的データを集約する。型定義は Plugin.Types.cs。
    internal sealed partial class Plugin
    {
        private static readonly string[] NormalMissionaryInterlockKeywords = { "密着", "しがみ" };
        private static readonly string[] NormalMissionaryInterlockPoseNames = { "Missionary Interlock" };
        private static readonly string[] StandingWallPreferredPoseNames =
        {
            "壁対面片足上げ",
            "Wall Standing Split",
            "Wall Standings Split"
        };
        private const string PoseScoreRulesFileName = "pose_score_rules.json";
        private const int PoseScoreRulesCurrentVersion = 5;
        private const string DefaultPoseSimpleModeTriggerKeywords = "になるね,の体位になる";
        private const int DefaultPoseScoreBase = 20;
        private const int DefaultPoseAdoptThreshold = 35;
        private const int DefaultPoseForceThreshold = 60;
        private const int DefaultBlankMapAddHttpPort = 55982;
        private static readonly string[] DefaultVideoExtensions =
        {
            ".mp4", ".wmv", ".avi", ".mkv", ".mov", ".m4v", ".webm"
        };



        private static readonly PoseKeywordScoreRule[] PoseKeywordScoreRules =
        {
            // 正常位系
            CreatePoseScoreRule(
                "normal_missionary_interlock",
                "正常位系",
                220,
                new[] { "Missionary Interlock" },
                CreatePoseScoreToken("密着", 40),
                CreatePoseScoreToken("しがみ", 40)),
            CreatePoseScoreRule(
                "normal_breeding_press",
                "正常位系",
                210,
                new[] { "種付けプレス" },
                CreatePoseScoreToken("種付け", 40),
                CreatePoseScoreToken("孕ませ", 40)),
            CreatePoseScoreRule(
                "normal_kaikyaku",
                "正常位系",
                120,
                new[] { "開脚正常位" },
                CreatePoseScoreToken("開脚", 25),
                CreatePoseScoreToken("開", 10),
                CreatePoseScoreToken("脚", 10),
                CreatePoseScoreToken("足", 10)),
            CreatePoseScoreRule(
                "normal_manguri",
                "正常位系",
                160,
                new[] { "マングリ正常位" },
                CreatePoseScoreToken("マングリ", 40)),
            CreatePoseScoreRule(
                "normal_table",
                "正常位系",
                115,
                new[] { "卓球台正常位" },
                CreatePoseScoreToken("台", 25)),
            CreatePoseScoreRule(
                "normal_knee_hold",
                "正常位系",
                116,
                new[] { "膝抱え正常位" },
                CreatePoseScoreToken("膝", 10),
                CreatePoseScoreToken("抱え", 10)),
            CreatePoseScoreRule(
                "normal_leg_open_wide",
                "正常位系",
                117,
                new[] { "大股開き正常位" },
                CreatePoseScoreToken("股", 25),
                CreatePoseScoreToken("開き", 25),
                CreatePoseScoreToken("開", 10)),
            CreatePoseScoreRule(
                "normal_hip_hold",
                "正常位系",
                116,
                new[] { "腰抱え正常位" },
                CreatePoseScoreToken("腰", 10),
                CreatePoseScoreToken("抱え", 10)),
            CreatePoseScoreRule(
                "normal_bridge",
                "正常位系",
                116,
                new[] { "ブリッジ正常位" },
                CreatePoseScoreToken("ブリッジ", 25)),
            CreatePoseScoreRule(
                "normal_beachball",
                "正常位系",
                116,
                new[] { "ビーチボール正常位" },
                CreatePoseScoreToken("ビーチ", 25),
                CreatePoseScoreToken("ボール", 10)),

            // 後背位系
            CreatePoseScoreRule(
                "back_arm_pull",
                "後背位系",
                170,
                new[] { "腕引っ張り後背位", "Doggystyle Hair-Pull Forced", "Doggy Standing Arm-Grab" },
                CreatePoseScoreToken("腕", 10),
                CreatePoseScoreToken("手", 10),
                CreatePoseScoreToken("引っ張", 40)),
            CreatePoseScoreRule(
                "back_push_against",
                "後背位系",
                175,
                new[] { "押し付けバック" },
                CreatePoseScoreToken("壁", 25),
                CreatePoseScoreToken("押し付け", 40)),
            CreatePoseScoreRule(
                "back_fence",
                "後背位系",
                174,
                new[] { "フェンス後背位" },
                CreatePoseScoreToken("フェンス", 40)),
            CreatePoseScoreRule(
                "back_banana",
                "後背位系",
                118,
                new[] { "バナナボート後背位" },
                CreatePoseScoreToken("バナナ", 25),
                CreatePoseScoreToken("ボート", 10)),
            CreatePoseScoreRule(
                "back_net",
                "後背位系",
                118,
                new[] { "ネット後背位" },
                CreatePoseScoreToken("ネット", 25)),
            CreatePoseScoreRule(
                "back_prone",
                "後背位系",
                118,
                new[] { "伏せ後背位" },
                CreatePoseScoreToken("伏せ", 25)),
            CreatePoseScoreRule(
                "back_float_ring",
                "後背位系",
                118,
                new[] { "浮き輪後背位" },
                CreatePoseScoreToken("浮き輪", 25),
                CreatePoseScoreToken("浮き", 10),
                CreatePoseScoreToken("輪", 10)),
            CreatePoseScoreRule(
                "back_leg_hold",
                "後背位系",
                118,
                new[] { "足抱え後背位" },
                CreatePoseScoreToken("足", 10),
                CreatePoseScoreToken("抱え", 10)),

            // 騎乗位系
            CreatePoseScoreRule(
                "cowgirl_hug",
                "騎乗位系",
                180,
                new[] { "Cowgirl Hug" },
                CreatePoseScoreToken("密着", 40)),
            CreatePoseScoreRule(
                "cowgirl_sofa",
                "騎乗位系",
                118,
                new[] { "ソファ騎乗位" },
                CreatePoseScoreToken("ソファ", 25)),
            CreatePoseScoreRule(
                "cowgirl_hand_hold",
                "騎乗位系",
                118,
                new[] { "手つなぎ騎乗位", "Hand-Holding Cowgirl 2", "Hand-Holding Cowgirl 3" },
                CreatePoseScoreToken("手", 10),
                CreatePoseScoreToken("つなぎ", 25)),
            CreatePoseScoreRule(
                "cowgirl_nipple",
                "騎乗位系",
                118,
                new[] { "乳首責め騎乗位", "Cowgirl Nipple Torture 2" },
                CreatePoseScoreToken("乳首", 40),
                CreatePoseScoreToken("責め", 25)),
            CreatePoseScoreRule(
                "cowgirl_banana",
                "騎乗位系",
                118,
                new[] { "バナナボート騎乗位" },
                CreatePoseScoreToken("バナナ", 25),
                CreatePoseScoreToken("ボート", 10)),

            // 背面騎乗位系
            CreatePoseScoreRule(
                "reverse_cowgirl_wall_hand",
                "背面騎乗位系",
                150,
                new[] { "壁手つき背面騎乗位", "Reverse Cowgirl", "Reverse Cowgirl 3", "Reverse Cowgirl 4", "Reverse Cowgirl 5" },
                CreatePoseScoreToken("壁", 40),
                CreatePoseScoreToken("手つき", 25),
                CreatePoseScoreToken("手", 10),
                CreatePoseScoreToken("つき", 10),
                CreatePoseScoreToken("背中", 25),
                CreatePoseScoreToken("後ろ", 25),
                CreatePoseScoreToken("跨", 25)),

            // 側位(測位)系
            CreatePoseScoreRule(
                "side_desk",
                "測位系",
                118,
                new[] { "机側位" },
                CreatePoseScoreToken("机", 25)),
            CreatePoseScoreRule(
                "side_back",
                "測位系",
                118,
                new[] { "背面側位" },
                CreatePoseScoreToken("背面", 25),
                CreatePoseScoreToken("後ろ", 25)),
            CreatePoseScoreRule(
                "side_princess",
                "測位系",
                180,
                new[] { "お姫様抱っこ側位", "Princess Hug Side Position 2" },
                CreatePoseScoreToken("お姫様", 40),
                CreatePoseScoreToken("抱っこ", 25)),

            // 座位系
            CreatePoseScoreRule(
                "sitting_floor_face",
                "座位系",
                118,
                new[] { "床対面座位" },
                CreatePoseScoreToken("床", 25),
                CreatePoseScoreToken("対面", 25)),
            CreatePoseScoreRule(
                "sitting_foot_hook_face",
                "座位系",
                118,
                new[] { "足掛け対面座位" },
                CreatePoseScoreToken("足掛け", 25),
                CreatePoseScoreToken("足", 10),
                CreatePoseScoreToken("掛け", 10),
                CreatePoseScoreToken("対面", 25)),
            CreatePoseScoreRule(
                "sitting_floor_back",
                "座位系",
                118,
                new[] { "床背面座位" },
                CreatePoseScoreToken("床", 25),
                CreatePoseScoreToken("背面", 25)),
            CreatePoseScoreRule(
                "sitting_seiza_back",
                "座位系",
                118,
                new[] { "正座背面座位" },
                CreatePoseScoreToken("正座", 25),
                CreatePoseScoreToken("背面", 25)),
            CreatePoseScoreRule(
                "sitting_knee_up_back",
                "座位系",
                118,
                new[] { "膝立て背面座位" },
                CreatePoseScoreToken("膝立て", 25),
                CreatePoseScoreToken("膝", 10),
                CreatePoseScoreToken("立て", 10),
                CreatePoseScoreToken("背面", 25)),

            // 立位系 / 立後背位系
            CreatePoseScoreRule(
                "standing_wall_priority",
                "立位系",
                170,
                new[] { "壁対面片足上げ", "Wall Standing Split", "Wall Standings Split" },
                CreatePoseScoreToken("壁", 40)),
            CreatePoseScoreRule(
                "standing_table",
                "立位系",
                118,
                new[] { "卓球台立位" },
                CreatePoseScoreToken("台", 25)),
            CreatePoseScoreRule(
                "standing_back_default",
                "立後背位系",
                140,
                new[] { "立ちバック", "Standing Doggystyle", "Standing From Behind 2", "Doggy Standing Arm-Grab", "Wall Kneeling Doggystyle" },
                CreatePoseScoreToken("立ち", 10),
                CreatePoseScoreToken("立位", 10),
                CreatePoseScoreToken("後ろ", 25),
                CreatePoseScoreToken("バック", 25),
                CreatePoseScoreToken("背後", 25)),

            // オナニー系
            CreatePoseScoreRule(
                "masturbation_plain",
                "オナニー系",
                180,
                new[] { "角オナニー", "椅子オナニー", "立ちオナニー", "トイレオナニー", "シャワーオナニー", "床オナニー", "ネットオナニー", "ディルドオナニー", "ローターオナニー" },
                CreatePoseScoreToken("オナニー", 40),
                CreatePoseScoreToken("自慰", 40),
                CreatePoseScoreToken("ひとり", 20),
                CreatePoseScoreToken("一人", 20),
                CreatePoseScoreToken("セルフ", 20),
                CreatePoseScoreToken("自分で", 20)),
            CreatePoseScoreRule(
                "masturbation_device_rotor",
                "オナニー系",
                220,
                new[] { "ローターオナニー" },
                CreatePoseScoreToken("ローター", 40)),
            CreatePoseScoreRule(
                "masturbation_device_dildo",
                "オナニー系",
                220,
                new[] { "ディルドオナニー" },
                CreatePoseScoreToken("ディルド", 40)),
            CreatePoseScoreRule(
                "masturbation_location_shower",
                "オナニー系",
                210,
                new[] { "シャワーオナニー" },
                CreatePoseScoreToken("シャワー", 40)),
            CreatePoseScoreRule(
                "masturbation_location_toilet",
                "オナニー系",
                210,
                new[] { "トイレオナニー" },
                CreatePoseScoreToken("トイレ", 40)),
            CreatePoseScoreRule(
                "masturbation_location_stand",
                "オナニー系",
                200,
                new[] { "立ちオナニー" },
                CreatePoseScoreToken("立ち", 20),
                CreatePoseScoreToken("立って", 20)),
            CreatePoseScoreRule(
                "masturbation_location_chair",
                "オナニー系",
                200,
                new[] { "椅子オナニー" },
                CreatePoseScoreToken("椅子", 30)),
            CreatePoseScoreRule(
                "masturbation_location_floor",
                "オナニー系",
                200,
                new[] { "床オナニー" },
                CreatePoseScoreToken("床", 30)),
            CreatePoseScoreRule(
                "masturbation_location_corner",
                "オナニー系",
                200,
                new[] { "角オナニー" },
                CreatePoseScoreToken("角", 30)),
            CreatePoseScoreRule(
                "masturbation_location_net",
                "オナニー系",
                200,
                new[] { "ネットオナニー" },
                CreatePoseScoreToken("ネット", 30))
        };

        private static readonly string[] PoseInferCommonExclude = { "しない", "じゃない", "やめる", "止める", "やらない" };
        private static readonly string[] PoseInferCowgirlExclude = { "しない", "じゃない", "やめる", "止める", "やらない", "背面", "逆騎乗", "後ろ向き" };
        private static readonly string[] PoseInferMasturbationExclude = { "しない", "じゃない", "やめる", "止める", "やらない", "奉仕", "ご奉仕", "フェラ", "手コキ", "パイズリ", "挿れ", "突い", "ピストン", "中に" };
        private static readonly PoseCategoryInferRule[] PoseCategoryInferenceRules =
        {
            // 正常位系
            CreatePoseInferRule(
                "infer_normal_supine_leg_open",
                "正常位系",
                160,
                new[] { "仰向け", "脚" },
                new[] { "開", "広げ" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_normal_supine_foot_open",
                "正常位系",
                160,
                new[] { "仰向け", "足" },
                new[] { "開", "広げ" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_normal_plain_word",
                "正常位系",
                150,
                new string[0],
                new[] { "正常位", "missionary", "Missionary" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_normal_hug_interlock",
                "正常位系",
                155,
                new[] { "密着" },
                new[] { "しがみ", "抱きつ", "抱き合" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_normal_seed_press",
                "正常位系",
                170,
                new string[0],
                new[] { "種付け", "孕ませ" },
                PoseInferCommonExclude),

            // 後背位系
            CreatePoseInferRule(
                "infer_back_plain_word",
                "後背位系",
                155,
                new string[0],
                new[] { "後背位", "バック", "後ろから", "背後", "doggy", "Doggystyle", "from behind", "From Behind" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_back_arm_pull",
                "後背位系",
                170,
                new[] { "引っ張" },
                new[] { "腕", "手" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_back_wall_push",
                "後背位系",
                175,
                new[] { "壁" },
                new[] { "押し付け", "押しつけ", "押し当て", "押し当てる" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_back_fours",
                "後背位系",
                145,
                new string[0],
                new[] { "四つん這い", "四つんばい", "伏せ", "うつ伏せ" },
                PoseInferCommonExclude),

            // 騎乗位系
            CreatePoseInferRule(
                "infer_cowgirl_plain_word",
                "騎乗位系",
                155,
                new string[0],
                new[] { "騎乗位", "騎乗" },
                PoseInferCowgirlExclude),
            CreatePoseInferRule(
                "infer_cowgirl_mount_motion",
                "騎乗位系",
                150,
                new string[0],
                new[] { "またが", "跨", "上に乗", "乗って", "乗る" },
                PoseInferCowgirlExclude),
            CreatePoseInferRule(
                "infer_cowgirl_hug_motion",
                "騎乗位系",
                165,
                new[] { "密着" },
                new[] { "またが", "跨", "上に乗", "乗って", "乗る" },
                PoseInferCowgirlExclude),

            // 背面騎乗位系
            CreatePoseInferRule(
                "infer_reverse_cowgirl_plain_word",
                "背面騎乗位系",
                165,
                new string[0],
                new[] { "背面騎乗位", "背面騎乗", "逆騎乗位", "逆騎乗" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_reverse_cowgirl_direction_mount",
                "背面騎乗位系",
                160,
                new string[0],
                new[] { "後ろ向き", "背中を向け", "背中向け", "後ろ向いて", "後ろ", "背中" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_reverse_cowgirl_mount_word",
                "背面騎乗位系",
                150,
                new[] { "後ろ" },
                new[] { "またが", "跨", "乗って", "乗る" },
                PoseInferCommonExclude),

            // 測位系
            CreatePoseInferRule(
                "infer_side_plain_word",
                "測位系",
                150,
                new string[0],
                new[] { "側位", "横向き", "横で", "横から" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_side_princess",
                "測位系",
                170,
                new string[0],
                new[] { "お姫様", "抱っこ", "姫抱っこ" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_side_lie_down",
                "測位系",
                140,
                new[] { "横" },
                new[] { "寝", "絡め" },
                PoseInferCommonExclude),

            // 座位系
            CreatePoseInferRule(
                "infer_sitting_plain_word",
                "座位系",
                150,
                new string[0],
                new[] { "座位", "座って", "座る", "座ったまま", "椅子" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_sitting_face",
                "座位系",
                155,
                new[] { "対面" },
                new[] { "座位", "座って", "椅子", "床", "向かい合" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_sitting_back",
                "座位系",
                155,
                new string[0],
                new[] { "背面座位", "正座", "膝立て", "床背面", "正座背面", "膝立て背面" },
                PoseInferCommonExclude),

            // 立位系
            CreatePoseInferRule(
                "infer_standing_plain_word",
                "立位系",
                150,
                new string[0],
                new[] { "立位", "立って", "立ったまま", "駅弁" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_standing_wall",
                "立位系",
                165,
                new[] { "壁" },
                new[] { "立位", "立って", "片足", "立ったまま" },
                PoseInferCommonExclude),

            // 立後背位系
            CreatePoseInferRule(
                "infer_standing_back_plain_word",
                "立後背位系",
                170,
                new string[0],
                new[] { "立ちバック", "立位バック", "Standing Doggystyle", "standing doggy", "standing from behind" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_standing_back_combo",
                "立後背位系",
                165,
                new[] { "後ろ" },
                new[] { "立って", "立位", "立ち", "背後", "バック" },
                PoseInferCommonExclude),
            CreatePoseInferRule(
                "infer_standing_back_wall",
                "立後背位系",
                175,
                new[] { "壁" },
                new[] { "後ろ", "背後", "バック", "押し付け" },
                PoseInferCommonExclude),

            // オナニー系
            CreatePoseInferRule(
                "infer_masturbation_plain_word",
                "オナニー系",
                190,
                new string[0],
                new[] { "オナニー", "自慰", "ひとりで", "一人で", "セルフ", "自分で", "masturbation", "Masturbation" },
                PoseInferMasturbationExclude),
            CreatePoseInferRule(
                "infer_masturbation_device",
                "オナニー系",
                200,
                new string[0],
                new[] { "ローター", "ディルド" },
                PoseInferMasturbationExclude),
            CreatePoseInferRule(
                "infer_masturbation_location",
                "オナニー系",
                180,
                new[] { "オナニー" },
                new[] { "角", "椅子", "立ち", "立って", "トイレ", "シャワー", "床", "ネット" },
                PoseInferMasturbationExclude)
        };

        private readonly System.Random _random = new System.Random();
        private readonly object _facePresetCacheLock = new object();
        private readonly Dictionary<string, float> _blockLogCooldownByKey = new Dictionary<string, float>();
        private string _facePresetCachePath = string.Empty;
        private DateTime _facePresetCacheWriteTimeUtc = DateTime.MinValue;
        private long _facePresetCacheLength = -1;
        private List<FacePresetJsonItem> _facePresetCache = new List<FacePresetJsonItem>();
        private FacePresetProbeState _facePresetProbe;
        private int _facePresetProbeSequence;

        private readonly Dictionary<string, List<PoseCategoryEntry>> _poseEntriesByCategory =
            new Dictionary<string, List<PoseCategoryEntry>>(StringComparer.Ordinal);
        private Coroutine _responseTextCoroutine;
        private int _poseScoreBase = DefaultPoseScoreBase;
        private int _poseAdoptThreshold = DefaultPoseAdoptThreshold;
        private int _poseForceThreshold = DefaultPoseForceThreshold;
        private List<PoseKeywordScoreRule> _poseKeywordScoreRules = new List<PoseKeywordScoreRule>();
        private List<PoseCategoryInferRule> _poseCategoryInferRules = new List<PoseCategoryInferRule>();
        private Dictionary<string, string[]> _categoryAliases = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private bool _poseChangeEnabled = true;
        private bool _poseSimpleModeEnabled = false;
        private string _poseSimpleModeTriggerKeywords = DefaultPoseSimpleModeTriggerKeywords;
        private bool _poseRulesEnabled = true;
        private bool _poseInferRulesEnabled = true;
        private bool _poseCategoriesExpanded = false;
        private bool _poseRulesExpanded = false;
        private bool _poseInferRulesExpanded = false;
        private readonly Dictionary<string, bool> _poseCategoryEnabled = new Dictionary<string, bool>(StringComparer.Ordinal);
        private string _poseScoreRulesFilePath;
        private static Type _configurationManagerType;
        private static MethodInfo _configurationManagerBuildSettingListMethod;

        private const string PoseSonyuClassifiedFileName = "pose_sonyu_classified.json";
        private const string PoseHoushiClassifiedFileName = "pose_houshi_classified.json";
        private const string PoseMasturbationClassifiedFileName = "pose_masturbation_classified.json";
        private const string PoseListFileName = "pose_list.json";
        private const string PoseListTranslatedFileName = "pose_list_ja_with_original.json";
        private const string DefaultFacePresetJsonRelativePath = "..\\..\\StudioFacePresetTool\\StudioFacePresets.json";
        private static readonly string[] SonyuCategoryNames =
        {
            "正常位系",
            "騎乗位系",
            "背面騎乗位系",
            "後背位系",
            "座位系",
            "測位系",
            "立位系",
            "立後背位系"
        };
        private static readonly string[] HoushiCategoryNames =
        {
            "フェラ系",
            "手コキ系",
            "パイズリ系",
            "クンニ系",
            "69系",
            "足コキ系",
            "顔面騎乗系",
            "キス・愛撫系"
        };
        private static readonly string[] MasturbationCategoryNames =
        {
            "オナニー系"
        };
        private const string PoseControlSectionName = "体位制御";
        private const string PoseControlCategoriesSectionName = "体位制御.カテゴリ";
        private const string PoseControlRulesSectionName = "体位制御.ルール";
        private const string PoseControlInferRulesSectionName = "体位制御.推定";
    }
}
