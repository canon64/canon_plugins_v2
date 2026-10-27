using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MainGameClubLights
{
    [Serializable]
    public sealed class RainbowSettings
    {
        public bool  Enabled    = false;
        public float CycleSpeed = 0.2f;   // Hue/sec
        [OptionalField] public bool  VideoLink      = false; // true: CycleSpeed = BPM駆動
        [OptionalField] public bool  BeatFollow     = false; // true: CycleSpeed = Lerp(Min,Max,zoneSpeed01)
        [OptionalField] public float MinCycleSpeed  = 0f;
        [OptionalField] public float MaxCycleSpeed  = 2f;
    }

    [Serializable]
    public sealed class StrobeSettings
    {
        public bool  Enabled     = false;
        public float FrequencyHz = 4f;
        public float DutyRatio   = 0.5f;  // ON比率
        [OptionalField] public bool  VideoLink      = false; // true: FrequencyHz = BPM駆動
        [OptionalField] public bool  BeatFollow     = false; // true: FrequencyHz = Lerp(Min,Max,zoneSpeed01)
        [OptionalField] public float MinFrequencyHz = 0f;
        [OptionalField] public float MaxFrequencyHz = 20f;
        // DutyRatio連動
        [OptionalField] public bool  DutyBeatFollow = false; // true: DutyRatio = Lerp(MinDuty,MaxDuty,zoneSpeed01)
        [OptionalField] public bool  DutyVideoLink  = false; // true: DutyRatio = Lerp(MinDuty,MaxDuty, BPMサイン波)
        [OptionalField] public float MinDutyRatio   = 0.1f;
        [OptionalField] public float MaxDutyRatio   = 0.9f;
    }

    [Serializable]
    public sealed class LoopSettings
    {
        public bool  Enabled  = false;
        public bool  VideoLink = false;
        public float MinValue = 0f;
        public float MaxValue = 1f;
        public float SpeedHz  = 0.5f; // cycles per second
        [OptionalField] public bool BeatFollow = false; // true: 値 = Lerp(Min,Max,zoneSpeed01)、sine/BPMなし
    }

    [Serializable]
    public sealed class MirrorballCookieSettings
    {
        public bool  Enabled    = false;
        public int   Resolution = 256;
        public int   DotCount   = 220;
        public float DotSize    = 0.03f; // UV空間(0-1)基準
        public float Scatter    = 0.65f; // 0=格子整列, 1=ランダム
        public float Softness   = 0.45f; // ドット縁のぼかし
        public bool  Animate    = true;
        public float SpinSpeed  = 0.12f; // 回転速度(回/秒)
        public float UpdateHz   = 8f;    // 再生成頻度
        public float Twinkle    = 0.20f; // 輝度ゆらぎ
    }

    [Serializable]
    public sealed class BeatPresetAssignment
    {
        public string LowPresetId  = "";
        public string MidPresetId  = "";
        public string HighPresetId = "";
    }

    [Serializable]
    public sealed class LightInstanceSettings
    {
        public string Id      = "";
        public string Name    = "Light";
        public bool   Enabled = true;

        // 所属フォルダ（空＝未所属＝従来通りワールド座標で単体）
        [OptionalField] public string FolderId = "";

        // カメラ追従モード (true) / 自由配置 (false / ギズモで移動)
        public bool FollowCamera = false;

        // FollowCamera=true 時のカメラからのオフセット
        public float OffsetX = 0f;
        public float OffsetY = 0f;
        public float OffsetZ = 0f;

        // FollowCamera=false 時のワールド座標（ギズモ移動後に保存）
        public float WorldPosX = 0f;
        public float WorldPosY = 1f;
        public float WorldPosZ = 2f;

        public float Intensity      = 2f;
        public float Range          = 10f;
        public float SpotAngle      = 179f;
        public float InnerSpotAngle = 0f;  // 0=ぼかし最大, SpotAngleと同値=境界シャープ

        public float ColorR = 1f;
        public float ColorG = 1f;
        public float ColorB = 1f;

        // HMD周囲を回転（FollowCamera=true 時のみ有効）
        public bool  ShowMarker    = true;
        public bool  ShowArrow     = true;
        public bool  ShowGizmo     = true;
        public float MarkerSize    = 0.08f;
        public float ArrowScale    = 1f;
        public float GizmoSize     = 1f;

        // 自由配置時の回転（FollowCamera=false かつ LookAtFemale=false 時に有効）
        public float RotX = 0f;
        public float RotY = 0f;
        public float RotZ = 0f;

        // 照射方向
        public bool  LookAtFemale  = false;   // true=常に女を向く / false=カメラを向く（FollowCamera時）
        public float LookAtOffsetX = 0f;
        public float LookAtOffsetY = 0f;
        public float LookAtOffsetZ = 0f;

        // 公転（中心を周回）
        public bool  RevolutionEnabled  = false;
        public float RevolutionRadius   = 2f;
        public float RevolutionSpeed    = 45f; // deg/sec
        // 公転中心（公転ONにした瞬間の位置を保存。WorldPosとは別管理）
        public float RevolutionCenterX  = 0f;
        public float RevolutionCenterY  = 1f;
        public float RevolutionCenterZ  = 2f;

        // 自転（Y軸スピン）
        public bool  RotationEnabled = false;
        public float RotationSpeed   = 90f;  // deg/sec

        [NonSerialized, IgnoreDataMember] public float RainbowHue;        // runtime
        [NonSerialized, IgnoreDataMember] public float RevolutionAngleDeg; // runtime
        [NonSerialized, IgnoreDataMember] public float RotationAngleDeg;   // runtime
        [NonSerialized, IgnoreDataMember] public bool SpotAnglePinnedByUser; // runtime

        public RainbowSettings      Rainbow      = new RainbowSettings();
        public StrobeSettings       Strobe       = new StrobeSettings();
        public BeatPresetAssignment Beat         = new BeatPresetAssignment();
        public LoopSettings         IntensityLoop = new LoopSettings { MinValue = 0.5f, MaxValue = 1.0f, SpeedHz = 0.3f };
        public LoopSettings         RangeLoop     = new LoopSettings { MinValue = 1f,  MaxValue = 10f };
        public LoopSettings         SpotAngleLoop = new LoopSettings { MinValue = 10f, MaxValue = 60f };
        public MirrorballCookieSettings Mirrorball = new MirrorballCookieSettings();
    }

    [Serializable]
    public sealed class LightPreset
    {
        public string               Id       = "";
        public string               Name     = "Preset";
        public LightInstanceSettings Settings = new LightInstanceSettings();
    }

    [Serializable]
    public sealed class VideoPresetMapping
    {
        public string VideoPath = "";
        public string PresetId  = "";
    }

    [Serializable]
    public sealed class NativeLightSettings
    {
        public bool  OverrideEnabled = false;
        public float IntensityScale  = 1f;
        public BeatPresetAssignment Beat = new BeatPresetAssignment();
        [OptionalField] public LoopSettings     IntensityLoop = new LoopSettings { MinValue = 0f, MaxValue = 1f, SpeedHz = 0.5f };
        [OptionalField] public RainbowSettings  Rainbow       = new RainbowSettings();
        [OptionalField] public StrobeSettings   Strobe        = new StrobeSettings();
    }

    // フォルダ（ライトをまとめる親）。複数自由に作成可。掴むと所属ライトが一括で動く。
    [Serializable]
    public sealed class LightFolder
    {
        public string Id         = "";
        public string Name       = "Folder";
        public float  PosX       = 0f;
        public float  PosY       = 0f;
        public float  PosZ       = 0f;
        public float  RotX       = 0f;
        public float  RotY       = 0f;
        public float  RotZ       = 0f;
        public bool   ShowHandle = true;                 // マーカー球の表示
        [OptionalField] public bool ShowHandleGizmo = true; // ギズモの表示（マーカーと個別）
        public float  HandleSize = 0.08f;  // マーカー球サイズ（既定小さめ）
        [OptionalField] public float GizmoSize = 0.5f;  // ギズモ倍率（既定小さめ）
    }

    // 体位ごとの「丸ごとスナップショット」（全フォルダ＋全ライト全設定）
    [Serializable]
    public sealed class LightPoseSnapshot
    {
        public string Key         = "";
        public string DisplayName = "";
        public List<LightFolder>           Folders = new List<LightFolder>();
        public List<LightInstanceSettings> Lights  = new List<LightInstanceSettings>();
    }

    [Serializable]
    public sealed class ClubLightsSettings
    {
        public List<LightInstanceSettings> Lights              = new List<LightInstanceSettings>();
        public List<LightPreset>           Presets             = new List<LightPreset>();
        public List<VideoPresetMapping>    VideoPresetMappings = new List<VideoPresetMapping>();
        public NativeLightSettings         NativeLight         = new NativeLightSettings();

        public float BeatLowThreshold  = 0.4f;
        public float BeatHighThreshold = 0.75f;

        // フォルダ（複数自由に作成可。所属ライトを一括移動）
        [OptionalField] public List<LightFolder> Folders = new List<LightFolder>();

        // 体位ごと丸ごとスナップショット
        [OptionalField] public List<LightPoseSnapshot> LightPoseSnapshots = new List<LightPoseSnapshot>();
        [OptionalField] public bool PosePresetAutoApply = false;

        public bool  UiVisible = false;
        public float UiX       = 20f;
        public float UiY       = 20f;
    }
}
