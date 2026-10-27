using Bindings = CanonPlugins.Shared.PluginInput;
using System.Collections.Generic;
using MainGameUiInputCapture;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameTransformGizmo
{
    public enum GizmoMode
    {
        Move = 0,
        Rotate = 1,
        Scale = 2,
    }

    public enum GizmoAxisSpace
    {
        Local = 0,
        World = 1
    }

    /// <summary>
    /// Video room root editor gizmo.
    /// - Center sphere click: cycle Move/Rotate/Scale
    /// - Center sphere right-click: toggle Local/World axis space
    /// - Drag axis handle: edit transform by current mode
    /// </summary>
    public sealed class TransformGizmo : MonoBehaviour
    {
        public const float DefaultSizeMultiplier = 1f;
        public const float MinSizeMultiplier = 0.2f;
        public const float MaxSizeMultiplier = 4f;

        private struct TransformSnapshot
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        public bool IsVisible { get; private set; }
        private bool _visibilityInitialized;
        public bool IsDragging => _dragging;
        public bool FollowActive { get; private set; }
        public int VRGrabState { get; private set; } // 0=none, 1=in-range, 2=grabbing
        public float SizeMultiplier { get; private set; } = DefaultSizeMultiplier;
        public GizmoMode Mode { get; private set; } = GizmoMode.Move;

        // 追従中(FollowActive)の中央球・左クリックの挙動を選ぶ。
        // false（既定）: 軸空間トグル（CameraControl/SubCamera 等の従来挙動）
        // true        : モード循環(Move/Rotate/Scale)。BodyIKギズモ用。
        public bool CycleModeOnFollowClick { get; set; } = false;
        public bool CenterDragOnXZPlane { get; set; } = false;

        public void SetMode(GizmoMode mode)
        {
            if (Mode == mode) return;
            Mode = mode;
            ModeChanged?.Invoke(Mode);
            _hoveredAxis = -1;
            _hoveredCenter = false;
            UpdateModeVisibility();
            UpdateVisualState();
        }
        public GizmoAxisSpace AxisSpace { get; private set; } = GizmoAxisSpace.World;
        public event System.Action<bool> DragStateChanged;
        public event System.Action<GizmoMode> ModeChanged;
        public event System.Action<GizmoAxisSpace> AxisSpaceChanged;

        private GameObject _moveRoot;
        private GameObject _rotateRoot;
        private GameObject _scaleRoot;
        private GameObject _centerSphere;

        private readonly GameObject[] _moveAxisRoot = new GameObject[3];
        private readonly Texture2D[] _moveAxisTex = new Texture2D[3];
        private readonly Material[] _moveAxisMat = new Material[3];

        private readonly LineRenderer[] _rotateAxisLine = new LineRenderer[3];
        private readonly Material[] _rotateAxisMat = new Material[3];

        private readonly GameObject[] _scaleAxisRoot = new GameObject[4];
        private readonly Texture2D[] _scaleAxisTex = new Texture2D[4];
        private readonly Material[] _scaleAxisMat = new Material[4];

        private Texture2D _centerTex;
        private Material _centerMat;

        private const int GizmoLayer = 31;

        // オーバーレイ用カメラはギズモ全体で1台だけ共有する。
        // インスタンスごとに持つと、ライトの数だけカメラが生まれ、
        // さらに各自が同じ本カメラの cullingMask を外したり戻したりして取り合うため、
        // 「本体のほかに、物陰で消えたり別サイズで出たりする幻影」がもう1つ描かれていた。
        private static Camera _overlayCam;
        private static Camera _lastMainCam;
        private static readonly HashSet<TransformGizmo> LiveGizmos = new HashSet<TransformGizmo>();
        private static int _maskStripFrame = -1;
        private static OverlayProjectionSync _overlaySync;
        private static float _nextCensusTime;
        private static string _lastCensus = string.Empty;

        private bool _hoveredCenter;
        private int _hoveredAxis = -1;
        private bool _dragging;
        private int _dragAxis = -1;
        private bool _centerDragAttempt;
        private float _centerDragAttemptStartTime;
        private Vector2 _prevMouseForScreenDrag;

        private float _dragStartT;
        private Vector3 _posAtDragStart;
        private Vector3 _scaleAtDragStart;
        private Quaternion _rotAtDragStart;
        private Vector3 _dragAxisWorld;
        private Vector3 _dragRotateAxis;
        private Vector3 _rotateVecAtDragStart;
        private TransformSnapshot _dragStartSnapshot;
        private readonly List<TransformSnapshot> _undoHistory = new List<TransformSnapshot>();
        private readonly List<TransformSnapshot> _redoHistory = new List<TransformSnapshot>();

        private static readonly Color ColX = new Color(1f, 0.25f, 0.25f);
        private static readonly Color ColY = new Color(0.25f, 1f, 0.25f);
        private static readonly Color ColZ = new Color(0.25f, 0.5f, 1f);
        private static readonly Color ColHL = Color.yellow;
        private static readonly Color ColCenter = new Color(0.86f, 0.86f, 0.86f);
        private static readonly Color ColCenterRotate = new Color(0.82f, 0.9f, 1f);
        private static readonly Color ColCenterScale = new Color(1f, 0.9f, 0.82f);
        private static readonly Color ColUniform = new Color(0.92f, 0.92f, 0.92f);

        private const float MoveLen = 0.6f;
        private const float MoveShaftR = 0.03f;
        private const float MoveHeadR = 0.07f;
        private const float MoveHeadH = 0.12f;

        private const float ScaleLen = 0.65f;
        private const float ScaleShaftR = 0.025f;
        private const float ScaleCubeSize = 0.11f;
        private const float UniformScaleLen = 0.8f;
        private const float ScaleDragFactor = 1f;
        private const float MinScale = 0.05f;
        private const float MaxScale = 200f;
        private const int UniformScaleAxis = 3;

        private const float RotateRadius = 0.75f;
        private const float RotateLineWidth = 0.03f;
        private const int RotateSegments = 72;

        private const float CenterSphereSize = 0.14f;
        private const float AxisHoverPx = 30f;
        private const float RotateHoverPx = 20f;
        private const float CenterHoverPx = 24f;
        private const int MaxHistoryCount = 128;
        private const int ScreenSpaceAxis = -2;
        private const float CenterDragThresholdSec = 0.15f;

        private void Awake()
        {
            _moveRoot = new GameObject("GizmoMove");
            _moveRoot.transform.SetParent(transform, false);
            _rotateRoot = new GameObject("GizmoRotate");
            _rotateRoot.transform.SetParent(transform, false);
            _scaleRoot = new GameObject("GizmoScale");
            _scaleRoot.transform.SetParent(transform, false);

            Quaternion[] axisRots =
            {
                Quaternion.Euler(0f, 0f, -90f), // X
                Quaternion.identity,            // Y
                Quaternion.Euler(90f, 0f, 0f), // Z
            };

            for (int axis = 0; axis < 3; axis++)
            {
                (_moveAxisRoot[axis], _moveAxisTex[axis], _moveAxisMat[axis]) =
                    BuildMoveHandle(_moveRoot.transform, "Move" + AxisName(axis), AxisColor(axis), axisRots[axis]);

                (_scaleAxisRoot[axis], _scaleAxisTex[axis], _scaleAxisMat[axis]) =
                    BuildScaleHandle(_scaleRoot.transform, "Scale" + AxisName(axis), AxisColor(axis), axisRots[axis]);
            }

            (_scaleAxisRoot[UniformScaleAxis], _scaleAxisTex[UniformScaleAxis], _scaleAxisMat[UniformScaleAxis]) =
                BuildScaleUniformHandle(_scaleRoot.transform, "ScaleXYZ", ColUniform);

            for (int axis = 0; axis < 3; axis++)
            {
                (_rotateAxisLine[axis], _rotateAxisMat[axis]) =
                    BuildRotateRing(_rotateRoot.transform, "Rotate" + AxisName(axis), AxisColor(axis), AxisDirLocal(axis));
            }

            (_centerSphere, _centerTex, _centerMat) = BuildCenterSphere("GizmoModeCenter");
            _centerSphere.transform.SetParent(transform, false);
            ApplySizeMultiplier();

            SetLayerRecursive(_moveRoot, GizmoLayer);
            SetLayerRecursive(_rotateRoot, GizmoLayer);
            SetLayerRecursive(_scaleRoot, GizmoLayer);
            SetLayerRecursive(_centerSphere, GizmoLayer);

            SetVisible(false);
            LiveGizmos.Add(this);
        }

        private static string AxisName(int axis)
        {
            if (axis == 0) return "X";
            if (axis == 1) return "Y";
            return "Z";
        }

        private static Color AxisColor(int axis)
        {
            if (axis == 0) return ColX;
            if (axis == 1) return ColY;
            return ColZ;
        }

        private (GameObject root, Texture2D tex, Material mat) BuildMoveHandle(
            Transform parent,
            string name,
            Color color,
            Quaternion rot)
        {
            Texture2D tex = CreateColorTexture(color);
            Material mat = CreateTextureMaterial(tex);

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localRotation = rot;

            AddCylinder(root.transform, name + "_shaft", mat,
                new Vector3(0f, MoveLen * 0.5f, 0f),
                new Vector3(MoveShaftR * 2f, MoveLen * 0.5f, MoveShaftR * 2f));

            AddCylinder(root.transform, name + "_head", mat,
                new Vector3(0f, MoveLen + MoveHeadH * 0.5f, 0f),
                new Vector3(MoveHeadR * 2f, MoveHeadH * 0.5f, MoveHeadR * 2f));

            return (root, tex, mat);
        }

        private (GameObject root, Texture2D tex, Material mat) BuildScaleHandle(
            Transform parent,
            string name,
            Color color,
            Quaternion rot)
        {
            Texture2D tex = CreateColorTexture(color);
            Material mat = CreateTextureMaterial(tex);

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localRotation = rot;

            AddCylinder(root.transform, name + "_shaft", mat,
                new Vector3(0f, ScaleLen * 0.5f, 0f),
                new Vector3(ScaleShaftR * 2f, ScaleLen * 0.5f, ScaleShaftR * 2f));

            AddCube(root.transform, name + "_tip", mat,
                new Vector3(0f, ScaleLen + ScaleCubeSize * 0.5f, 0f),
                new Vector3(ScaleCubeSize, ScaleCubeSize, ScaleCubeSize));

            return (root, tex, mat);
        }

        private (GameObject root, Texture2D tex, Material mat) BuildScaleUniformHandle(
            Transform parent,
            string name,
            Color color)
        {
            Texture2D tex = CreateColorTexture(color);
            Material mat = CreateTextureMaterial(tex);

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(1f, 1f, 1f).normalized);

            AddCylinder(root.transform, name + "_shaft", mat,
                new Vector3(0f, UniformScaleLen * 0.5f, 0f),
                new Vector3(ScaleShaftR * 2f, UniformScaleLen * 0.5f, ScaleShaftR * 2f));

            AddCube(root.transform, name + "_tip", mat,
                new Vector3(0f, UniformScaleLen + ScaleCubeSize * 0.5f, 0f),
                new Vector3(ScaleCubeSize, ScaleCubeSize, ScaleCubeSize));

            return (root, tex, mat);
        }

        private (LineRenderer line, Material mat) BuildRotateRing(
            Transform parent,
            string name,
            Color color,
            Vector3 axisDir)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, axisDir);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = RotateSegments;
            line.startWidth = RotateLineWidth;
            line.endWidth = RotateLineWidth;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            Material mat = CreateColorMaterial(color);
            line.sharedMaterial = mat;
            line.startColor = color;
            line.endColor = color;

            for (int i = 0; i < RotateSegments; i++)
            {
                float t = i / (float)RotateSegments;
                float rad = t * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(Mathf.Cos(rad) * RotateRadius, Mathf.Sin(rad) * RotateRadius, 0f));
            }

            return (line, mat);
        }

        private (GameObject sphere, Texture2D tex, Material mat) BuildCenterSphere(string name)
        {
            Texture2D tex = CreateColorTexture(ColCenter);
            Material mat = CreateTextureMaterial(tex);

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = name;
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localScale = Vector3.one * CenterSphereSize;

            var mr = sphere.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;

            return (sphere, tex, mat);
        }

        private static Texture2D CreateColorTexture(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }

        private static Material CreateTextureMaterial(Texture2D tex)
        {
            var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            LogShader("CreateTextureMaterial", shader);
            var mat = new Material(shader);
            if (mat.HasProperty("_MainTex")) mat.mainTexture = tex;
            return mat;
        }

        private static Material CreateColorMaterial(Color color)
        {
            // Sprites/Default は3D/LineRenderer で正常描画できないため Unlit/Color を優先する
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            LogShader("CreateColorMaterial", shader);
            var mat = new Material(shader);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            return mat;
        }

        private static void LogShader(string context, UnityEngine.Shader shader)
        {
            if (Plugin.Instance != null)
                Plugin.Instance.LogInfo($"[Shader] {context}: {(shader != null ? shader.name : "NULL")}");
        }

        private static void AddCylinder(Transform parent, string name, Material mat, Vector3 localPos, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }

        private static void AddCube(Transform parent, string name, Material mat, Vector3 localPos, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = mat;
        }

        public void SetVisible(bool visible)
        {
            if (_visibilityInitialized && IsVisible == visible && !_dragging) return;
            _visibilityInitialized = true;
            IsVisible = visible;
            UpdateModeVisibility();

            if (!visible)
            {
                _hoveredCenter = false;
                _hoveredAxis = -1;
                _centerDragAttempt = false;
                if (_dragging)
                    DragStateChanged?.Invoke(false);
                _dragging = false;
                _dragAxis = -1;
                UpdateVisualState();
                if (_overlayCam != null && !AnyGizmoVisible())
                    _overlayCam.enabled = false;
            }
        }

        public void SetSizeMultiplier(float sizeMultiplier)
        {
            float clamped = Mathf.Clamp(sizeMultiplier, MinSizeMultiplier, MaxSizeMultiplier);
            if (Mathf.Abs(SizeMultiplier - clamped) <= 0.0001f)
                return;

            SizeMultiplier = clamped;
            ApplySizeMultiplier();
        }

        private void UpdateModeVisibility()
        {
            bool visible = IsVisible;

            if (_moveRoot != null) _moveRoot.SetActive(visible && Mode == GizmoMode.Move);
            if (_rotateRoot != null) _rotateRoot.SetActive(visible && Mode == GizmoMode.Rotate);
            if (_scaleRoot != null) _scaleRoot.SetActive(visible && Mode == GizmoMode.Scale);
            if (_centerSphere != null) _centerSphere.SetActive(visible);
        }

        private void ApplySizeMultiplier()
        {
            Vector3 scale = Vector3.one * SizeMultiplier;
            if (_moveRoot != null)
                _moveRoot.transform.localScale = scale;
            if (_rotateRoot != null)
                _rotateRoot.transform.localScale = scale;
            if (_scaleRoot != null)
                _scaleRoot.transform.localScale = scale;
            if (_centerSphere != null)
                _centerSphere.transform.localScale = Vector3.one * (CenterSphereSize * SizeMultiplier);

            for (int i = 0; i < _rotateAxisLine.Length; i++)
            {
                if (_rotateAxisLine[i] == null)
                    continue;
                float width = RotateLineWidth * SizeMultiplier;
                _rotateAxisLine[i].startWidth = width;
                _rotateAxisLine[i].endWidth = width;
            }
        }

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (!IsVisible) return;
            UpdateOverlayCam();

            var cam = Camera.main;
            if (cam == null) return;

            // IMGUI上の操作を背後の3Dギズモがraw Inputで拾わないようにする。
            // 既に開始済みのドラッグはマウス解放まで継続し、取り残しを防ぐ。
            if (!_dragging && UiInputCaptureApi.IsAvailable && UiInputCaptureApi.IsMouseInputBlocked())
            {
                bool visualChanged = _hoveredCenter || _hoveredAxis >= 0;
                _hoveredCenter = false;
                _hoveredAxis = -1;
                _centerDragAttempt = false;
                if (visualChanged)
                    UpdateVisualState();
                return;
            }

            UpdateAxisSpaceVisual();

            Vector3 pos = transform.position;
            Vector2 mouse = Input.mousePosition;

            if (!_dragging)
            {
                if (HandleUndoRedoShortcuts())
                    return;

                bool newHoveredCenter = IsCenterHovered(cam, mouse);
                int newHoveredAxis = newHoveredCenter ? -1 : DetectHoveredAxis(cam, mouse);

                if (newHoveredCenter != _hoveredCenter || newHoveredAxis != _hoveredAxis)
                {
                    _hoveredCenter = newHoveredCenter;
                    _hoveredAxis = newHoveredAxis;
                    UpdateVisualState();
                }

                if (_hoveredCenter && Bindings.Down("MainGameTransformGizmo.axisSpace", "Mouse1"))
                {
                    ToggleAxisSpace();
                    return;
                }

                if (_hoveredCenter && Bindings.Down("MainGameTransformGizmo.drag", "Mouse0") && FollowActive && !CycleModeOnFollowClick)
                {
                    ToggleAxisSpace();
                    return;
                }

                // 中央球ドラッグ待機中
                if (_centerDragAttempt)
                {
                    if (!Bindings.Held("MainGameTransformGizmo.drag", "Mouse0"))
                    {
                        // 離した → クリック扱い
                        _centerDragAttempt = false;
                        if (FollowActive && !CycleModeOnFollowClick)
                            ToggleAxisSpace();
                        else
                            CycleMode();
                        return;
                    }

                    if (Time.unscaledTime - _centerDragAttemptStartTime >= CenterDragThresholdSec)
                    {
                        // 押し続けた → スクリーンスペースドラッグ開始
                        _centerDragAttempt = false;
                        BeginScreenSpaceDrag(pos, mouse);
                    }

                    return;
                }

                if (!Bindings.Down("MainGameTransformGizmo.drag", "Mouse0")) return;

                if (_hoveredCenter)
                {
                    // すぐにモード切替せず、ドラッグか判断するまで待機
                    _centerDragAttempt = true;
                    _centerDragAttemptStartTime = Time.unscaledTime;
                    return;
                }

                if (_hoveredAxis >= 0)
                {
                    BeginDrag(cam, pos, mouse, _hoveredAxis);
                }

                return;
            }

            if (Bindings.Held("MainGameTransformGizmo.drag", "Mouse0"))
            {
                UpdateDrag(cam, mouse);
                return;
            }

            // Ensure the release-frame cursor position is reflected once before ending drag.
            // Without this, the gizmo may snap slightly to the previous frame's pose on mouse-up.
            UpdateDrag(cam, mouse);
            _dragging = false;
            _dragAxis = -1;
            CommitDragHistoryIfChanged();
            DragStateChanged?.Invoke(false);
        }

        public bool Undo()
        {
            if (_undoHistory.Count <= 0) return false;

            TransformSnapshot current = CaptureSnapshot();
            TransformSnapshot target = PopUndo();
            PushRedo(current);
            ApplySnapshot(target);
            return true;
        }

        public bool Redo()
        {
            if (_redoHistory.Count <= 0) return false;

            TransformSnapshot current = CaptureSnapshot();
            TransformSnapshot target = PopRedo();
            PushUndo(current);
            ApplySnapshot(target);
            return true;
        }

        public void ClearHistory()
        {
            _undoHistory.Clear();
            _redoHistory.Clear();
        }

        private void CycleMode()
        {
            Mode = (GizmoMode)(((int)Mode + 1) % 3);
            ModeChanged?.Invoke(Mode);
            _hoveredAxis = -1;
            _hoveredCenter = false;
            UpdateModeVisibility();
            UpdateVisualState();
        }

        public void SetFollowActive(bool active)
        {
            if (FollowActive == active) return;
            FollowActive = active;
            UpdateVisualState();
        }

        public void SetVRGrabState(int state)
        {
            int clamped = state < 0 ? 0 : state > 2 ? 2 : state;
            if (VRGrabState == clamped) return;
            VRGrabState = clamped;
            UpdateVisualState();
        }

        private Color? _centerColorOverride;
        public void SetCenterColor(Color color)
        {
            color.a = 1f;
            if (_centerColorOverride.HasValue && _centerColorOverride.Value == color) return;
            _centerColorOverride = color;
            UpdateVisualState();
        }

        public void SetAxisSpace(GizmoAxisSpace axisSpace)
        {
            if (AxisSpace == axisSpace)
                return;

            AxisSpace = axisSpace;
            AxisSpaceChanged?.Invoke(AxisSpace);
            _hoveredAxis = -1;
            _hoveredCenter = false;
            UpdateAxisSpaceVisual();
            UpdateVisualState();
        }

        private void ToggleAxisSpace()
        {
            SetAxisSpace(AxisSpace == GizmoAxisSpace.Local ? GizmoAxisSpace.World : GizmoAxisSpace.Local);
        }

        private int DetectHoveredAxis(Camera cam, Vector2 mouse)
        {
            if (Mode == GizmoMode.Move)
            {
                return DetectLinearHover(cam, mouse, useScaleTip: false, AxisHoverPx * SizeMultiplier);
            }

            if (Mode == GizmoMode.Scale)
            {
                return DetectLinearHover(cam, mouse, useScaleTip: true, AxisHoverPx * SizeMultiplier);
            }

            return DetectRotateHover(cam, mouse, RotateHoverPx);
        }

        private int DetectLinearHover(Camera cam, Vector2 mouse, bool useScaleTip, float maxPx)
        {
            int axis = -1;
            float best = maxPx;
            int count = useScaleTip ? _scaleAxisRoot.Length : 3;
            for (int i = 0; i < count; i++)
            {
                Vector3 tip = useScaleTip ? GetScaleTipWorld(i) : GetMoveTipWorld(i);
                float d = ScreenDist(cam, tip, mouse);
                if (d < best)
                {
                    best = d;
                    axis = i;
                }
            }

            return axis;
        }

        private int DetectRotateHover(Camera cam, Vector2 mouse, float maxPx)
        {
            int axis = -1;
            float best = maxPx;
            for (int i = 0; i < 3; i++)
            {
                float d = ScreenDistToRing(cam, _rotateAxisLine[i], mouse);
                if (d < best)
                {
                    best = d;
                    axis = i;
                }
            }

            return axis;
        }

        private bool IsCenterHovered(Camera cam, Vector2 mouse)
        {
            Vector3 center = _centerSphere != null ? _centerSphere.transform.position : transform.position;
            return ScreenDist(cam, center, mouse) <= CenterHoverPx;
        }

        private void BeginScreenSpaceDrag(Vector3 center, Vector2 mouse)
        {
            _dragging = true;
            _dragAxis = ScreenSpaceAxis;
            _posAtDragStart = center;
            _prevMouseForScreenDrag = mouse;
            _dragStartSnapshot = CaptureSnapshot();
            DragStateChanged?.Invoke(true);
        }

        private void BeginDrag(Camera cam, Vector3 center, Vector2 mouse, int axis)
        {
            _dragging = true;
            _dragAxis = axis;
            _posAtDragStart = center;
            _dragAxisWorld = GetAxisDirWorld(axis);
            _dragStartSnapshot = CaptureSnapshot();

            if (Mode == GizmoMode.Move)
            {
                _dragStartT = GetRayAxisT(cam, _dragAxisWorld, center, mouse);
            }
            else if (Mode == GizmoMode.Scale)
            {
                _scaleAtDragStart = transform.localScale;
                _dragStartT = GetRayAxisT(cam, _dragAxisWorld, center, mouse);
            }
            else
            {
                _rotAtDragStart = transform.rotation;
                _dragRotateAxis = _dragAxisWorld;
                if (!TryGetRotationVector(cam, center, _dragRotateAxis, mouse, out _rotateVecAtDragStart))
                    _rotateVecAtDragStart = BuildFallbackRotateVector(cam, _dragRotateAxis);
            }

            DragStateChanged?.Invoke(true);
        }

        private void CommitDragHistoryIfChanged()
        {
            TransformSnapshot end = CaptureSnapshot();
            if (!AreSnapshotsDifferent(_dragStartSnapshot, end))
                return;

            PushUndo(_dragStartSnapshot);
            _redoHistory.Clear();
        }

        private void UpdateDrag(Camera cam, Vector2 mouse)
        {
            if (_dragAxis == ScreenSpaceAxis)
            {
                Vector2 delta = mouse - _prevMouseForScreenDrag;
                _prevMouseForScreenDrag = mouse;
                float dist = Vector3.Distance(cam.transform.position, transform.position);
                float pixelToUnit = dist * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2f / Screen.height;
                if (CenterDragOnXZPlane)
                {
                    Vector3 right = Vector3.ProjectOnPlane(cam.transform.right, Vector3.up);
                    if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
                    right.Normalize();
                    Vector3 forward = Vector3.Cross(right, Vector3.up);
                    Vector3 next = transform.position + (right * delta.x + forward * delta.y) * pixelToUnit;
                    next.y = _posAtDragStart.y;
                    transform.position = next;
                    return;
                }
                transform.position += cam.transform.right * delta.x * pixelToUnit
                                    + cam.transform.up * delta.y * pixelToUnit;
                return;
            }

            if (_dragAxis < 0) return;

            if (Mode == GizmoMode.Move)
            {
                float t = GetRayAxisT(cam, _dragAxisWorld, _posAtDragStart, mouse);
                transform.position = _posAtDragStart + _dragAxisWorld * (t - _dragStartT);
                return;
            }

            if (Mode == GizmoMode.Scale)
            {
                float t = GetRayAxisT(cam, _dragAxisWorld, _posAtDragStart, mouse);
                float delta = (t - _dragStartT) * ScaleDragFactor;

                Vector3 s = _scaleAtDragStart;
                if (_dragAxis == 0) s.x = Mathf.Clamp(_scaleAtDragStart.x + delta, MinScale, MaxScale);
                if (_dragAxis == 1) s.y = Mathf.Clamp(_scaleAtDragStart.y + delta, MinScale, MaxScale);
                if (_dragAxis == 2) s.z = Mathf.Clamp(_scaleAtDragStart.z + delta, MinScale, MaxScale);
                if (_dragAxis == UniformScaleAxis)
                {
                    float baseLen = Mathf.Max(MinScale, Mathf.Max(_scaleAtDragStart.x, Mathf.Max(_scaleAtDragStart.y, _scaleAtDragStart.z)));
                    float factor = Mathf.Max(0.01f, 1f + (delta / baseLen));
                    s.x = Mathf.Clamp(_scaleAtDragStart.x * factor, MinScale, MaxScale);
                    s.y = Mathf.Clamp(_scaleAtDragStart.y * factor, MinScale, MaxScale);
                    s.z = Mathf.Clamp(_scaleAtDragStart.z * factor, MinScale, MaxScale);
                }
                transform.localScale = s;
                return;
            }

            if (!TryGetRotationVector(cam, _posAtDragStart, _dragRotateAxis, mouse, out var currentVec))
                return;

            float angle = Vector3.SignedAngle(_rotateVecAtDragStart, currentVec, _dragRotateAxis);
            transform.rotation = Quaternion.AngleAxis(angle, _dragRotateAxis) * _rotAtDragStart;
        }

        private void UpdateVisualState()
        {
            for (int i = 0; i < 3; i++)
            {
                Color moveColor = (Mode == GizmoMode.Move && _hoveredAxis == i) ? ColHL : AxisColor(i);
                SetPx(_moveAxisTex[i], moveColor);

                Color rotateColor = (Mode == GizmoMode.Rotate && _hoveredAxis == i) ? ColHL : AxisColor(i);
                SetMatColor(_rotateAxisMat[i], rotateColor);
                if (_rotateAxisLine[i] != null)
                {
                    _rotateAxisLine[i].startColor = rotateColor;
                    _rotateAxisLine[i].endColor = rotateColor;
                }
            }

            for (int i = 0; i < _scaleAxisTex.Length; i++)
            {
                Color baseColor = i == UniformScaleAxis ? ColUniform : AxisColor(i);
                Color scaleColor = (Mode == GizmoMode.Scale && _hoveredAxis == i) ? ColHL : baseColor;
                SetPx(_scaleAxisTex[i], scaleColor);
            }

            Color centerBase = ColCenter;
            if (Mode == GizmoMode.Rotate) centerBase = ColCenterRotate;
            if (Mode == GizmoMode.Scale) centerBase = ColCenterScale;
            centerBase = ApplyAxisSpaceTint(centerBase);
            if (FollowActive) centerBase = Color.Lerp(centerBase, new Color(0.2f, 0.8f, 1f), 0.6f);
            if (VRGrabState == 1) centerBase = Color.Lerp(centerBase, Color.white, 0.7f);
            if (VRGrabState == 2) centerBase = Color.Lerp(centerBase, Color.yellow, 0.8f);
            Color centerColor = _hoveredCenter ? ColHL : centerBase;
            if (_centerColorOverride.HasValue) centerColor = _centerColorOverride.Value;
            SetPx(_centerTex, centerColor);
        }

        private Color ApplyAxisSpaceTint(Color baseColor)
        {
            if (AxisSpace == GizmoAxisSpace.World)
            {
                return Color.Lerp(baseColor, new Color(1f, 0.72f, 0.18f), 0.45f);
            }

            return baseColor;
        }

        private static void SetPx(Texture2D tex, Color c)
        {
            if (tex == null) return;
            tex.SetPixel(0, 0, c);
            tex.Apply();
        }

        private static void SetMatColor(Material mat, Color c)
        {
            if (mat == null) return;
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", c);
        }

        private static Vector3 AxisDirLocal(int axis)
        {
            if (axis == 0) return Vector3.right;
            if (axis == 1) return Vector3.up;
            return Vector3.forward;
        }

        private Vector3 GetAxisDirWorld(int axis)
        {
            Vector3 axisLocal = axis == UniformScaleAxis
                ? new Vector3(1f, 1f, 1f).normalized
                : AxisDirLocal(axis);

            if (AxisSpace == GizmoAxisSpace.World)
                return axisLocal;

            return transform.TransformDirection(axisLocal).normalized;
        }

        private void UpdateAxisSpaceVisual()
        {
            Quaternion basisRotation = AxisSpace == GizmoAxisSpace.Local
                ? transform.rotation
                : Quaternion.identity;

            if (_moveRoot != null)
                _moveRoot.transform.rotation = basisRotation;
            if (_rotateRoot != null)
                _rotateRoot.transform.rotation = basisRotation;
            if (_scaleRoot != null)
                _scaleRoot.transform.rotation = basisRotation;
        }

        private Vector3 GetMoveTipWorld(int axis)
        {
            Transform root = _moveAxisRoot[axis] != null ? _moveAxisRoot[axis].transform : null;
            if (root == null)
                return transform.position + GetAxisDirWorld(axis) * ((MoveLen + MoveHeadH) * SizeMultiplier);

            return root.TransformPoint(new Vector3(0f, MoveLen + MoveHeadH, 0f));
        }

        private Vector3 GetScaleTipWorld(int axis)
        {
            Transform root = _scaleAxisRoot[axis] != null ? _scaleAxisRoot[axis].transform : null;
            if (root == null)
            {
                float len = axis == UniformScaleAxis ? UniformScaleLen : ScaleLen;
                return transform.position + GetAxisDirWorld(axis) * ((len + ScaleCubeSize * 0.5f) * SizeMultiplier);
            }

            float tipLen = axis == UniformScaleAxis ? UniformScaleLen : ScaleLen;
            return root.TransformPoint(new Vector3(0f, tipLen + ScaleCubeSize * 0.5f, 0f));
        }

        private static float ScreenDist(Camera cam, Vector3 worldPos, Vector2 mouse)
        {
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z < 0f) return float.MaxValue;
            return Vector2.Distance(new Vector2(sp.x, sp.y), mouse);
        }

        private static float ScreenDistToRing(Camera cam, LineRenderer ring, Vector2 mouse)
        {
            if (ring == null || ring.positionCount <= 1)
                return float.MaxValue;

            float best = float.MaxValue;
            bool hasPrev = false;
            Vector2 prev = Vector2.zero;
            int count = ring.positionCount;

            for (int i = 0; i <= count; i++)
            {
                int idx = i % count;
                Vector3 lp = ring.GetPosition(idx);
                Vector3 wp = ring.useWorldSpace ? lp : ring.transform.TransformPoint(lp);
                Vector3 sp = cam.WorldToScreenPoint(wp);
                if (sp.z < 0f)
                {
                    hasPrev = false;
                    continue;
                }

                Vector2 cur = new Vector2(sp.x, sp.y);
                if (hasPrev)
                {
                    float d = DistPointSegment(mouse, prev, cur);
                    if (d < best) best = d;
                }

                prev = cur;
                hasPrev = true;
            }

            return best;
        }

        private static float DistPointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            if (len2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            Vector2 proj = a + ab * t;
            return Vector2.Distance(p, proj);
        }

        private static float GetRayAxisT(Camera cam, Vector3 axisDir, Vector3 axisOrigin, Vector2 screenPos)
        {
            Ray ray = cam.ScreenPointToRay(screenPos);
            float b = Vector3.Dot(axisDir, ray.direction);
            float denom = 1f - b * b;
            if (Mathf.Abs(denom) < 1e-6f) return 0f;
            float c = Vector3.Dot(axisDir, axisOrigin - ray.origin);
            float f = Vector3.Dot(ray.direction, axisOrigin - ray.origin);
            return (b * f - c) / denom;
        }

        private static bool TryGetRotationVector(
            Camera cam,
            Vector3 center,
            Vector3 axisDir,
            Vector2 screenPos,
            out Vector3 dirOnPlane)
        {
            dirOnPlane = Vector3.zero;
            if (!TryRayPlaneIntersection(cam, screenPos, center, axisDir, out var hit))
                return false;

            Vector3 v = Vector3.ProjectOnPlane(hit - center, axisDir);
            if (v.sqrMagnitude < 1e-6f) return false;
            dirOnPlane = v.normalized;
            return true;
        }

        private static bool TryRayPlaneIntersection(
            Camera cam,
            Vector2 screenPos,
            Vector3 planePoint,
            Vector3 planeNormal,
            out Vector3 hitPoint)
        {
            hitPoint = planePoint;
            Ray ray = cam.ScreenPointToRay(screenPos);
            float denom = Vector3.Dot(planeNormal, ray.direction);
            if (Mathf.Abs(denom) < 1e-6f) return false;

            float t = Vector3.Dot(planePoint - ray.origin, planeNormal) / denom;
            if (t < 0f) return false;

            hitPoint = ray.origin + ray.direction * t;
            return true;
        }

        private static Vector3 BuildFallbackRotateVector(Camera cam, Vector3 axisDir)
        {
            Vector3 v = Vector3.ProjectOnPlane(cam.transform.up, axisDir);
            if (v.sqrMagnitude < 1e-6f)
                v = Vector3.ProjectOnPlane(cam.transform.right, axisDir);
            if (v.sqrMagnitude < 1e-6f)
                v = Vector3.Cross(axisDir, Vector3.up);
            if (v.sqrMagnitude < 1e-6f)
                v = Vector3.Cross(axisDir, Vector3.right);
            return v.normalized;
        }

        private bool HandleUndoRedoShortcuts()
        {


            if (Bindings.Down("MainGameTransformGizmo.undo", "Z + Ctrl"))
            {
                Undo();
                return true;
            }

            if (Bindings.Down("MainGameTransformGizmo.redo", "Y + Ctrl"))
            {
                Redo();
                return true;
            }

            return false;
        }

        private TransformSnapshot CaptureSnapshot()
        {
            return new TransformSnapshot
            {
                Position = transform.position,
                Rotation = transform.rotation,
                Scale = transform.localScale
            };
        }

        private void ApplySnapshot(TransformSnapshot snapshot)
        {
            transform.position = snapshot.Position;
            transform.rotation = snapshot.Rotation;
            transform.localScale = snapshot.Scale;
        }

        private static bool AreSnapshotsDifferent(TransformSnapshot a, TransformSnapshot b)
        {
            return (a.Position - b.Position).sqrMagnitude > 1e-10f
                || Quaternion.Angle(a.Rotation, b.Rotation) > 0.001f
                || (a.Scale - b.Scale).sqrMagnitude > 1e-10f;
        }

        private void PushUndo(TransformSnapshot snapshot)
        {
            if (_undoHistory.Count >= MaxHistoryCount)
                _undoHistory.RemoveAt(0);
            _undoHistory.Add(snapshot);
        }

        private void PushRedo(TransformSnapshot snapshot)
        {
            if (_redoHistory.Count >= MaxHistoryCount)
                _redoHistory.RemoveAt(0);
            _redoHistory.Add(snapshot);
        }

        private TransformSnapshot PopUndo()
        {
            int idx = _undoHistory.Count - 1;
            TransformSnapshot snapshot = _undoHistory[idx];
            _undoHistory.RemoveAt(idx);
            return snapshot;
        }

        private TransformSnapshot PopRedo()
        {
            int idx = _redoHistory.Count - 1;
            TransformSnapshot snapshot = _redoHistory[idx];
            _redoHistory.RemoveAt(idx);
            return snapshot;
        }

        /// <summary>
        /// 生きているギズモを全部書き出す。推測せずに「今なにが描かれているか」を確定させるための口。
        /// 内容が変わった時だけ、最短1秒間隔で1行。
        /// </summary>
        private static void LogCensus(Camera mainCam)
        {
            if (!GizmoLog.Enabled) return;
            if (Time.unscaledTime < _nextCensusTime) return;
            _nextCensusTime = Time.unscaledTime + 1f;

            var sb = new System.Text.StringBuilder();
            sb.Append("gizmos=").Append(LiveGizmos.Count);
            sb.Append(" overlay=").Append(_overlayCam != null && _overlayCam.enabled ? "on" : "off");
            sb.Append(" mainCam=").Append(mainCam != null ? mainCam.name : "null");
            sb.Append(" fov=").Append(mainCam != null ? mainCam.fieldOfView.ToString("0.#") : "-");
            sb.Append(" camPos=").Append(mainCam != null ? mainCam.transform.position.ToString("0.0") : "-");

            foreach (TransformGizmo g in LiveGizmos)
            {
                if (g == null) { sb.Append(" | <destroyed>"); continue; }
                sb.Append(" | ").Append(g.name)
                  .Append(" vis=").Append(g.IsVisible)
                  .Append(" pos=").Append(g.transform.position.ToString("0.0"))
                  .Append(" size=").Append(g.SizeMultiplier.ToString("0.##"))
                  .Append(" active=").Append(g.gameObject.activeInHierarchy);
                if (mainCam != null)
                    sb.Append(" dist=").Append(Vector3.Distance(mainCam.transform.position, g.transform.position).ToString("0.0"));
            }

            string line = sb.ToString();
            if (string.Equals(line, _lastCensus, System.StringComparison.Ordinal)) return;
            _lastCensus = line;
            GizmoLog.Write(line);
        }

        private static bool AnyGizmoVisible()
        {
            foreach (TransformGizmo g in LiveGizmos)
            {
                if (g != null && g.IsVisible) return true;
            }
            return false;
        }

        private void UpdateOverlayCam()
        {
            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            if (_overlayCam == null)
            {
                var go = new GameObject("TGizmoOverlayCam");
                DontDestroyOnLoad(go);
                _overlayCam = go.AddComponent<Camera>();
                _overlayCam.clearFlags = CameraClearFlags.Depth;
                _overlayCam.cullingMask = 1 << GizmoLayer;
                _overlayCam.renderingPath = RenderingPath.Forward;
                _overlayCam.enabled = false;
            }

            // ギズモ層を描いていいのはオーバーレイカメラだけ。
            // 「本カメラが切り替わった時に1回だけ外す」では足りない。
            // 表示に使われるカメラは途中で増えたり入れ替わったりするため、
            // 外し損ねたカメラがもう1つのギズモ（幻影）を描いてしまう。
            // 毎フレーム、オーバーレイ以外の全カメラから確実に落とす。
            if (_maskStripFrame != Time.frameCount)
            {
                _maskStripFrame = Time.frameCount;
                Camera[] cams = Camera.allCameras;
                for (int i = 0; i < cams.Length; i++)
                {
                    Camera c = cams[i];
                    if (c == null || c == _overlayCam) continue;
                    c.cullingMask &= ~(1 << GizmoLayer);
                }
            }
            _lastMainCam = mainCam;

            _overlayCam.transform.position = mainCam.transform.position;
            _overlayCam.transform.rotation = mainCam.transform.rotation;
            if (!mainCam.stereoEnabled)
                _overlayCam.fieldOfView = mainCam.fieldOfView;
            _overlayCam.nearClipPlane = mainCam.nearClipPlane;
            _overlayCam.farClipPlane = mainCam.farClipPlane;

            // ここでの写しだけでは足りない。
            // 本カメラの画角は onPreCull で他プラグイン(CameraControl等)に
            // 描画直前で書き換えられるため、Update 時点の値を写すと
            // 画角がズレたままギズモだけ拡大されて描かれる。
            // 実際の描画直前に、本カメラの投影行列をそのまま写す。
            if (_overlaySync == null)
                _overlaySync = _overlayCam.gameObject.AddComponent<OverlayProjectionSync>();
            _overlaySync.Source = mainCam;
            _overlayCam.depth = mainCam.depth + 100;
            _overlayCam.enabled = AnyGizmoVisible();

            LogCensus(mainCam);
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursive(child.gameObject, layer);
        }

        private void OnDestroy()
        {
            DragStateChanged?.Invoke(false);

            LiveGizmos.Remove(this);

            // 後始末は最後の1個が消えるときだけ。
            // 各インスタンスがやると、まだ生きている仲間の足元で
            // 本カメラにギズモ層が戻され、幻影が復活する。
            if (LiveGizmos.Count == 0)
            {
                // 落とした層は全カメラへ返す。返さないと、
                // このプラグインを外した後もギズモ層が見えないままになる。
                Camera[] cams = Camera.allCameras;
                for (int i = 0; i < cams.Length; i++)
                {
                    if (cams[i] != null && cams[i] != _overlayCam)
                        cams[i].cullingMask |= (1 << GizmoLayer);
                }
                _lastMainCam = null;
                _maskStripFrame = -1;
                if (_overlayCam != null)
                {
                    Destroy(_overlayCam.gameObject);
                    _overlayCam = null;
                }
            }

            // コンポーネント破棄時に子GameObjectも明示的に削除
            // （Destroy(component)はGOを破棄しないため残骸が残る）
            if (_moveRoot    != null) Destroy(_moveRoot);
            if (_rotateRoot  != null) Destroy(_rotateRoot);
            if (_scaleRoot   != null) Destroy(_scaleRoot);
            if (_centerSphere != null) Destroy(_centerSphere);

            for (int i = 0; i < 3; i++)
            {
                if (_moveAxisTex[i] != null) Destroy(_moveAxisTex[i]);
                if (_moveAxisMat[i] != null) Destroy(_moveAxisMat[i]);
                if (_rotateAxisMat[i] != null) Destroy(_rotateAxisMat[i]);
            }

            for (int i = 0; i < _scaleAxisTex.Length; i++)
            {
                if (_scaleAxisTex[i] != null) Destroy(_scaleAxisTex[i]);
                if (_scaleAxisMat[i] != null) Destroy(_scaleAxisMat[i]);
            }

            if (_centerTex != null) Destroy(_centerTex);
            if (_centerMat != null) Destroy(_centerMat);
        }
    }

    /// <summary>
    /// オーバーレイカメラの投影を、実際の描画直前に本カメラへ合わせる。
    /// 本カメラの画角は onPreCull で書き換わることがあり、
    /// Update 時点の値を写すとギズモの大きさと位置がズレる。
    /// </summary>
    internal sealed class OverlayProjectionSync : MonoBehaviour
    {
        internal Camera Source;
        private Camera _self;

        private void Awake()
        {
            _self = GetComponent<Camera>();
        }

        private void OnPreCull()
        {
            if (_self == null || Source == null) return;
            _self.worldToCameraMatrix = Source.worldToCameraMatrix;
            _self.projectionMatrix = Source.projectionMatrix;
        }
    }
}
