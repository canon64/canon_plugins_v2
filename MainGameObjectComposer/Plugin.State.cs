using System;
using System.Collections.Generic;
using HarmonyLib;
using RootMotion.FinalIK;
using UnityEngine;

namespace MainGameObjectComposer
{
    [Serializable]
    internal sealed class ComposerSettings
    {
        public bool UiVisible = true;
        public bool StateVisible = true;

        public KeyCode ToggleUiKey = KeyCode.None;
        public KeyCode ToggleStateKey = KeyCode.None;

        public float MainWindowX = 20f;
        public float MainWindowY = 20f;
        public float MainWindowW = 760f;
        public float MainWindowH = 820f;

        public float StateWindowX = 800f;
        public float StateWindowY = 20f;
        public float StateWindowW = 520f;
        public float StateWindowH = 620f;

        public bool AutoSaveOnMutation = true;
        public bool AutoLoadLayoutOnStart = true;
        public bool AutoSpawnOnHSceneReady = true;
        public bool VerboseLog = false;
        public bool EnableSelectedGizmo = true;

        public int MaxUndoSteps = 128;
        public float DiskFlattenYScale = 0.1f;
        public float PositionNudgeStep = 0.02f;
        public float RotationNudgeStep = 3f;
        public float ScaleNudgeStep = 0.02f;

        public PrimitiveType DefaultPrimitive = PrimitiveType.Sphere;
        public Vector3 DefaultScale = new Vector3(0.1f, 0.1f, 0.1f);
        public Vector3 DefaultChildOffset = new Vector3(0.5f, 0f, 0f);
        public Vector3 DefaultAutoRotateAxis = Vector3.up;
        public float DefaultAutoRotateSpeedDegPerSec = 45f;
        public bool DefaultAutoRotateLocalSpace = true;

        public Vector3 DefaultAngleAxis = Vector3.up;
        public float DefaultAngleAmplitudeDeg = 15f;
        public float DefaultAngleSpeedHz = 1f;
        public bool DefaultAngleLocalSpace = true;

        public Vector3 DefaultPistonAxis = Vector3.forward;
        public float DefaultPistonAmplitude = 0.1f;
        public float DefaultPistonSpeedHz = 1f;
        public bool DefaultPistonLocalSpace = true;

        // カメラ前方からの距離 (m) — 新規作成・「目の前へ移動」で使用
        public float DefaultSpawnDistance = 0.5f;

        // 回転オブジェクトの初期値
        public float DefaultOrbitRadiusX = 0.3f;
        public float DefaultOrbitRadiusZ = 0.3f;
        public float DefaultTubeRadius = 0.01f;
        public float DefaultOrbitSpeedHz = 0.5f;

        // ピストンドライバの初期値
        public float DefaultPistonRodRadius = 0.01f;

        // アングルドライバの初期値
        public float DefaultAngleFanRadius = 0.3f;

        // ギズモのサイズ倍率（TransformGizmo 側で 0.2〜4.0 にクランプされる）
        public float GizmoSizeMultiplier = 0.3f;

        // デバッグログ（右クリック検出など）
        public bool DebugLogEnabled = false;

        // 体位/モーション変化でプリセットを自動切替する
        public bool AutoSwitchPresetOnPositionChange = false;
    }

    [Serializable]
    internal sealed class ObjectLayoutFile
    {
        public string format = "ObjectLayoutV2";
        // JsonUtility が List<internal class> をシリアライズから外す不具合を回避するため、
        // 永続化は ManagedObjectData を個別 JSON 化した List<string> として持つ。
        public List<string> objectsJson = new List<string>();
        public string selectedId;

        public string parentCandidateKind;
        public string parentCandidateRefId;

        // legacy compatibility
        public string parentCandidateId;
    }

    [Serializable]
    internal sealed class ManagedObjectData
    {
        public string id;
        public string name;
        public PrimitiveType primitive = PrimitiveType.Sphere;

        public string parentKind = Plugin.ParentKindRoot;
        public string parentRefId;

        // legacy compatibility (managed parent id)
        public string parentId;

        public Vector3 localPosition = Vector3.zero;
        public Vector3 localEulerAngles = Vector3.zero;
        public Vector3 localScale = Vector3.one;

        public bool autoRotate;
        public Vector3 autoRotateAxis = Vector3.up;
        public float autoRotateSpeedDegPerSec = 45f;
        public bool autoRotateLocalSpace = true;

        // 動きモード: 0=なし, 1=回転(autoRotate), 2=アングル, 3=ピストン
        // 1オブジェクトに1モードのみ（排他）
        public int motionMode;

        // Angle (motionMode=2)
        public Vector3 angleAxis = Vector3.up;
        public float angleAmplitudeDeg = 15f;
        public float angleSpeedHz = 1f;
        public float anglePhaseTurns;
        public bool angleLocalSpace = true;

        // Piston (motionMode=3)
        public Vector3 pistonAxis = Vector3.forward;
        public float pistonAmplitude = 0.1f;
        public float pistonSpeedHz = 1f;
        public float pistonPhaseTurns;
        public bool pistonLocalSpace = true;

        // 回転オブジェクト（ドーナツ／楕円軌道ドライバ）
        // true のとき、このオブジェクトは手続き生成のトーラスメッシュを持ち
        // 直下の子の localPosition を XZ 平面の楕円軌道に強制配置する
        public bool isRotationObject;
        public float orbitRadiusX = 0.3f;
        public float orbitRadiusZ = 0.3f;
        public float tubeRadius = 0.01f;
        public float orbitSpeedHz = 0.5f;
        // On a rotation-object child, blend the parent orbit only while Vibe movement is slow.
        public bool vibeSlowParentEnabled;
        // Keep this object's local +Z orbit axis on Vibe's entry -> deep direction.
        public bool vibeInsertionAlignEnabled;
        public float vibeSlowParentWeight = 0.3f;
        public float vibeSlowRiseSeconds = 0.4f;
        public float vibeSlowFallSeconds = 0.35f;

        // 回転オブジェクトの子に与える個別位相 (0..1 turns)
        public float orbitPhaseTurns;

        // 表示/非表示トグル（MeshRenderer.enabled）
        public bool visible = true;

        // アニメ同期: HScene の現在アニメ正規化時間を位相源にする
        public bool animSync = false;
        // アニメ1周に対する公転倍率（1.0 = アニメ1周で1公転、2.0 = 2公転、0.5 = 半公転）
        public float animSpeedMultiplier = 1.0f;
        // 位相の連続性を保つためのオフセット。非同期モードでの速度変更時に内部で更新される。
        public float phaseContinuityOffsetTurns = 0f;
        // アニメ同期時のユーザー位相シフト (0〜1 turns)。完全同期に対して見た目を回転させる。
        public float animSyncPhaseShift = 0f;

        // 回転オブジェクト用: 子の向きを軌道接線方向に追従させるデフォルト
        // （新規に子化されたオブジェクトの orientToTangent 初期値）
        public bool orientChildrenToTangent = true;

        // 回転オブジェクトの子: このオブジェクト個別に接線追従するか
        public bool orientToTangent = true;

        // ピストンドライバ（ストロークレール、子が直線往復）
        public bool isPistonObject;
        public float pistonRodRadius = 0.01f;

        // アングルドライバ（扇形、子が扇内でswing）
        public bool isAngleObject;
        public float angleFanRadius = 0.3f;

        // ドライバの子に与える個別位相 (0..1 turns)  ♻ isRotationObject の orbitPhaseTurns を流用
        // orbitPhaseTurns は上記にすでに定義済み
    }

    internal sealed class RuntimeObjectRef
    {
        public ManagedObjectData Data;
        // Wrapper: 空オブジェクト (scale=1 固定)。位置/回転はここ。親子関係もここ同士で繋ぐ。
        public GameObject GameObject;
        // Visual: Wrapper の子。mesh とユーザー scale を持つ。他のオブジェクトには伝播しない。
        public GameObject Visual;
        public GameObject CachedRendererVisual;
        public MeshRenderer CachedVisualRenderer;

        // Piston/Angle の基準姿勢（motionMode 2/3 で使用）
        public Vector3 BaseLocalPosition;
        public Quaternion BaseLocalRotation = Quaternion.identity;
        public bool HasBaseLocalPose;

        // 回転オブジェクト用: 直近のメッシュ生成パラメータ。変化時のみメッシュ再生成。
        public float CachedRx = -1f;
        public float CachedRz = -1f;
        public float CachedTube = -1f;
        public Mesh GeneratedMesh;
        public Material GeneratedMaterial;
    }

    internal sealed class ExternalParentTarget
    {
        public string Key;
        public string Label;
        public string Category;
        public Transform Transform;
    }

    internal sealed class RuntimeState
    {
        public HSceneProc HSceneProc;
        public HFlag Flags;
        public ChaControl MainFemale;
        public Animator MainFemaleAnimBody;
        public FullBodyBipedIK MainFemaleFbbik;
        public GameObject Root;
        public bool ReadyLogged;

        public void Clear(bool keepLogState = false)
        {
            HSceneProc = null;
            Flags = null;
            MainFemale = null;
            MainFemaleAnimBody = null;
            MainFemaleFbbik = null;
            Root = null;
            if (!keepLogState)
            {
                ReadyLogged = false;
            }
        }
    }

    internal static class RuntimeReflection
    {
        internal static readonly System.Reflection.FieldInfo FiHSceneLstFemale =
            AccessTools.Field(typeof(HSceneProc), "lstFemale");

        internal static readonly System.Reflection.FieldInfo FiHSceneNowHpointData =
            AccessTools.Field(typeof(HSceneProc), "nowHpointData");

        internal static readonly System.Reflection.FieldInfo FiHSceneNowHpointDataPos =
            AccessTools.Field(typeof(HSceneProc), "nowHpointDataPos");

        internal static readonly System.Reflection.FieldInfo FiHSceneLstAnimInfo =
            AccessTools.Field(typeof(HSceneProc), "lstAnimInfo");

        internal static readonly System.Reflection.FieldInfo FiHSceneLstUseAnimInfo =
            AccessTools.Field(typeof(HSceneProc), "lstUseAnimInfo");
    }
}
