using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MainGameCameraControl
{
    [DataContract]
    internal sealed class PluginSettings
    {
        [DataMember(Name = "UiVisible")]
        public bool UiVisible = true;

        [DataMember(Name = "DetailLogEnabled")]
        public bool DetailLogEnabled = false;

        [DataMember(Name = "VoiceFaceEventCameraTriggerEnabled")]
        public bool VoiceFaceEventCameraTriggerEnabled = true;

        [DataMember(Name = "VoiceFaceEventReturnEnabled")]
        public bool VoiceFaceEventReturnEnabled = true;

        [DataMember(Name = "VoiceFaceEventReturnStaySeconds")]
        public float VoiceFaceEventReturnStaySeconds = 4f;

        [DataMember(Name = "BoneReleaseByRightClick")]
        public bool BoneReleaseByRightClick = true;

        [DataMember(Name = "BoneReleaseLongPressSeconds")]
        public float BoneReleaseLongPressSeconds = 0.4f;

        [DataMember(Name = "SaveCameraPoseOverrides")]
        public bool SaveCameraPoseOverrides = false;

        // 保存タブのチェック状態。ここに残さないと起動ごとに既定へ戻り、
        // 前回OFFにしたはずの相対方向が黙って全プリセットに付く。
        [DataMember(Name = "SaveRelativeDirection")]
        public bool SaveRelativeDirection = true;

        [DataMember(Name = "SaveRollWithBone")]
        public bool SaveRollWithBone = false;

        // ── 相対方向の動作切替（3つとも切り分け用に独立して切れる） ──────────
        //
        // 既定値の考え方:
        //   焼いた構図をそのまま再現するのが本来の動作。それを曲げる処理は既定OFF。
        //   ヨーの軸取りだけは計算の誤りを正すものなので既定ON。

        /// <summary>手でカメラを動かした時、その位置で相対角度と距離を焼き直すか。既定OFF。</summary>
        [DataMember(Name = "RelRebakeOnManual")]
        public bool RelRebakeOnManual = false;

        /// <summary>計算位置が床下に出る時、仰角を起こして床の高さに乗せるか。既定OFF。</summary>
        [DataMember(Name = "RelFloorGuard")]
        public bool RelFloorGuard = false;

        /// <summary>腰の前方向が縦を向いた時、上方向の軸から体の向きを取るか。既定ON。</summary>
        [DataMember(Name = "RelYawUpAxisFallback")]
        public bool RelYawUpAxisFallback = true;

        [DataMember(Name = "PosePresetAutoApply")]
        public bool PosePresetAutoApply = false;

        [DataMember(Name = "DefaultFov")]
        public float DefaultFov = 35f;

        [DataMember(Name = "ApplyFov")]
        public bool ApplyFov = false;

        [DataMember(Name = "TransitionSeconds")]
        public float TransitionSeconds = 0.4f;

        [DataMember(Name = "AutoCameraCycleEnabled")]
        public bool AutoCameraCycleEnabled = false;

        [DataMember(Name = "AutoCameraStaySeconds")]
        public float AutoCameraStaySeconds = 8f;

        [DataMember(Name = "TransitionEasing")]
        public int TransitionEasing = 3;

        [DataMember(Name = "WindowX")]
        public float WindowX = 20f;

        [DataMember(Name = "WindowY")]
        public float WindowY = 100f;

        [DataMember(Name = "SaveWithBoneLink")]
        public bool SaveWithBoneLink = false;

        [DataMember(Name = "SelectedBoneTarget")]
        public int SelectedBoneTarget = 0;

        [DataMember(Name = "SelectedSaveMode")]
        public int SelectedSaveMode = 0;

        [DataMember(Name = "SaveBoneCameraPosition")]
        public bool SaveBoneCameraPosition = false;

        [DataMember(Name = "GizmoVisible")]
        public bool GizmoVisible = true;

        [DataMember(Name = "GizmoSize")]
        public float GizmoSize = 0.35f;

        [DataMember(Name = "BeatFovZoomEnabled")]
        public bool BeatFovZoomEnabled = false;

        [DataMember(Name = "BeatFovZoomMultiplier")]
        public float BeatFovZoomMultiplier = 1.15f;

        [DataMember(Name = "Presets")]
        public List<CameraPreset> Presets = new List<CameraPreset>();
    }

#pragma warning disable 0649
    [DataContract]
    internal sealed class CameraPreset
    {
        [DataMember(Name = "Name")]
        public string Name;

        [DataMember(Name = "TargetPosition")]
        public float[] TargetPosition;

        [DataMember(Name = "CameraDirection")]
        public float[] CameraDirection;

        [DataMember(Name = "Rotation")]
        public float[] Rotation;

        [DataMember(Name = "Fov")]
        public float Fov;

        [DataMember(Name = "QuickCycleEnabled")]
        public bool QuickCycleEnabled;

        [DataMember(Name = "UseBoneLink")]
        public bool UseBoneLink;

        [DataMember(Name = "BoneTarget")]
        public int BoneTarget;

        [DataMember(Name = "BoneName")]
        public string BoneName;

        [DataMember(Name = "LookAtWorldY")]
        public bool LookAtWorldY;

        [DataMember(Name = "LookAtOffsetLocal")]
        public float[] LookAtOffsetLocal;

        [DataMember(Name = "SaveCameraPosition")]
        public bool SaveCameraPosition;

        [DataMember(Name = "CameraOffsetLocal")]
        public float[] CameraOffsetLocal;

        [DataMember(Name = "CameraOffsetWorld")]
        public float[] CameraOffsetWorld;

        // カメラ位置を「ボーンの位置」基準＋transBase回転フレームで保存した相対オフセット。
        // ボーンの回転からは切り離してあるので、頭などがうなずいても着地点が振り回されない。
        // 存在すればこちらを最優先。無ければ従来の CameraOffsetLocal（bone.TransformPoint）にフォールバック。
        [DataMember(Name = "CameraOffsetBonePos")]
        public float[] CameraOffsetBonePos;

        [DataMember(Name = "RotationOffset")]
        public float[] RotationOffset;

        // ── 相対方向モード ──────────────────────────────────────────
        // 保存時の「基準ボーンから見たカメラの方向と距離」を焼き込み、
        // 呼び出し中は毎フレームその方向と距離を保つ。
        // 体位が変わってボーンが回れば、カメラも同じ相対角度を保って回り込む。
        [DataMember(Name = "UseRelativeDirection")]
        public bool UseRelativeDirection;

        /// <summary>基準ボーンのローカル空間で見たカメラ方向(単位ベクトル)。</summary>
        [DataMember(Name = "RelativeDirLocal")]
        public float[] RelativeDirLocal;

        /// <summary>基準点からカメラまでの距離。</summary>
        [DataMember(Name = "RelativeDistance")]
        public float RelativeDistance;

        /// <summary>
        /// 方向と距離の基準点。true=注視点(構図の中心)、false=旧仕様の腰ボーン位置。
        /// 旧仕様は体位で腰が下がるとカメラも同じ量だけ沈んで床下へ潜るため、
        /// 新規保存は必ず true。古いプリセットは焼き直すまで旧仕様のまま動かす。
        /// </summary>
        [DataMember(Name = "RelativeFromLookAt")]
        public bool RelativeFromLookAt;

        /// <summary>
        /// ボーンの傾きにカメラも一緒に傾くか。
        /// 既定はfalse(水平維持)。trueにするとキャラが寝た時に画面ごと倒れる。
        /// </summary>
        [DataMember(Name = "RollWithBone")]
        public bool RollWithBone;

        /// <summary>角度と距離の基準にするボーン。既定は腰。頭にすると頷きでカメラが振り回される。</summary>
        [DataMember(Name = "FrameBoneName")]
        public string FrameBoneName;

        [DataMember(Name = "Position")]
        public float[] LegacyPosition;

        [DataMember(Name = "LookAt")]
        public float[] LegacyLookAt;

        [DataMember(Name = "Distance")]
        public float LegacyDistance;

        [DataMember(Name = "UsePoseOverrides")]
        public bool UsePoseOverrides;

        [DataMember(Name = "PoseOverrides")]
        public CameraPoseOverride[] PoseOverrides;
    }

    [DataContract]
    internal sealed class CameraPoseOverride
    {
        // null は旧データ（共通設定を継承）。false はこの体位で明示的に相対方向OFF。
        [DataMember(Name = "UseRelativeDirection", EmitDefaultValue = false)]
        public bool? UseRelativeDirection;

        [DataMember(Name = "RelativeDirLocal")]
        public float[] RelativeDirLocal;

        [DataMember(Name = "RelativeDistance")]
        public float RelativeDistance;

        [DataMember(Name = "RelativeFromLookAt")]
        public bool RelativeFromLookAt;

        [DataMember(Name = "RollWithBone")]
        public bool RollWithBone;

        [DataMember(Name = "FrameBoneName")]
        public string FrameBoneName;

        [DataMember(Name = "Key")]
        public string Key;

        [DataMember(Name = "DisplayName")]
        public string DisplayName;

        [DataMember(Name = "UseBoneLink")]
        public bool UseBoneLink;

        [DataMember(Name = "BoneTarget")]
        public int BoneTarget;

        [DataMember(Name = "BoneName")]
        public string BoneName;

        [DataMember(Name = "SaveCameraPosition")]
        public bool SaveCameraPosition;

        [DataMember(Name = "TargetPosition")]
        public float[] TargetPosition;

        [DataMember(Name = "CameraDirection")]
        public float[] CameraDirection;

        [DataMember(Name = "Rotation")]
        public float[] Rotation;

        [DataMember(Name = "LookAtWorldY")]
        public bool LookAtWorldY;

        [DataMember(Name = "LookAtOffsetLocal")]
        public float[] LookAtOffsetLocal;

        [DataMember(Name = "CameraOffsetLocal")]
        public float[] CameraOffsetLocal;

        [DataMember(Name = "CameraOffsetWorld")]
        public float[] CameraOffsetWorld;

        // ボーン位置基準＋transBase回転フレームの相対オフセット（回転非依存）。CameraPreset 側と同じ意味。
        [DataMember(Name = "CameraOffsetBonePos")]
        public float[] CameraOffsetBonePos;

        [DataMember(Name = "Fov")]
        public float Fov;
    }
#pragma warning restore 0649
}
