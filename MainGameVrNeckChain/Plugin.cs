using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using ConfigurationManager;
using UnityEngine;
using Valve.VR;
using VRGIN.Controls;
using VRGIN.Core;

namespace MainGameVrNeckChain
{
    [BepInPlugin(Guid, "MainGameVrNeckChain", "0.1.0")]
    [BepInProcess("KoikatsuSunshine")]
    [BepInDependency("com.kks.maingame.unifiedui", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(MainGameTransformGizmo.Plugin.GUID, BepInDependency.DependencyFlags.HardDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.maingame.vrneckchain";
        private const string BundlePath = "studio/chains.unity3d";
        private const string AssetName = "chain_24_weighted";
        private const string CollarBundlePath = "chara/Augh/leash.unity3d";
        private const string CollarAssetName = "choker";
        private const int BoneCount = 24;
        private const float GrabRadius = 0.18f;
        private const float Gravity = 6f;
        private static readonly Vector3 NeckOffset = new Vector3(0f, -0.035f, 0.065f);
        private static readonly FieldInfo FemaleListField = typeof(HSceneProc).GetField(
            "lstFemale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private ConfigEntry<bool> _toggle;
        private ConfigEntry<float> _neckDown;
        private ConfigEntry<float> _neckForward;
        private ConfigEntry<float> _grabRadius;
        private ConfigEntry<float> _collarScale;
        private ConfigEntry<float> _collarScaleX;
        private ConfigEntry<float> _collarScaleY;
        private ConfigEntry<float> _collarScaleZ;
        private ConfigEntry<float> _collarOffsetX;
        private ConfigEntry<float> _collarOffsetY;
        private ConfigEntry<float> _collarOffsetZ;
        private ConfigEntry<float> _collarRotationX;
        private ConfigEntry<float> _collarRotationY;
        private ConfigEntry<float> _collarRotationZ;
        private ConfigEntry<float> _chainOffsetX;
        private ConfigEntry<float> _chainScale;
        private ConfigEntry<float> _chainScaleX;
        private ConfigEntry<float> _chainScaleY;
        private ConfigEntry<float> _chainScaleZ;
        private ConfigEntry<float> _ringOffsetX;
        private ConfigEntry<float> _ringOffsetY;
        private ConfigEntry<float> _ringOffsetZ;
        private ConfigEntry<float> _collarAttachRadius;
        private ConfigEntry<float> _collarDetachRadius;
        private ConfigEntry<float> _chainAttachRadius;
        private ConfigEntry<bool> _showColliderPreviews;
        private HSceneProc _scene;
        private ChaControl _female;
        private Transform _neck;
        private Transform _collarBone;
        private GameObject _collar;
        private GameObject _chain;
        private DynamicBone _dynamicBone;
        private readonly Transform[] _bones = new Transform[BoneCount];
        private readonly Vector3[] _points = new Vector3[BoneCount];
        private readonly Vector3[] _previous = new Vector3[BoneCount];
        private readonly Quaternion[] _restWorldRotations = new Quaternion[BoneCount];
        private readonly float[] _segments = new float[BoneCount - 1];
        private readonly Vector3[] _baseSegmentVectors = new Vector3[BoneCount - 1];
        private float _chainLength;
        private Vector3 _chainRootLocalOffset;
        private Quaternion _restRootRotation;
        private Controller _grabController;
        private Controller.Lock _grabLock = Controller.Lock.Invalid;
        private Controller _rootController;
        private Controller.Lock _rootLock = Controller.Lock.Invalid;
        private Controller _collarController;
        private Controller.Lock _collarLock = Controller.Lock.Invalid;
        private Vector3 _collarGrabOffset;
        private bool _collarAttached;
        private bool _rootAttached;
        private bool _collarSnapArmed = true;
        private bool _rootSnapArmed = true;
        private Vector3 _attachedRootLocal;
        private bool _customSimulation;
        private bool _rootAttachPending;
        private bool _collarAttachPending;
        private Vector3 _collarPreviousPosition;
        private float _nextSceneCheck;

        private void Awake()
        {
            _toggle = Config.Bind("Chain", "Show / remove chain", false,
                new ConfigDescription("通常H中の最初の女キャラの首に鎖を表示・削除します。",
                    null, new ConfigurationManagerAttributes
                    {
                        HideDefaultButton = true,
                        CustomDrawer = DrawToggleButton
                    }));
            _toggle.SettingChanged += (sender, args) =>
            {
                if (!_toggle.Value) return;
                _toggle.Value = false;
                if (_chain != null) ClearChain();
                else SpawnChain();
            };
            _neckDown = Config.Bind("Placement", "Neck down (m)", NeckOffset.y,
                "首ボーンから鎖の根元を下へずらす距離。負の値を指定します。");
            _neckForward = Config.Bind("Placement", "Neck forward (m)", NeckOffset.z,
                "首ボーンから鎖の根元を前へずらす距離。");
            _grabRadius = Config.Bind("VR grip", "Grab radius (m)", GrabRadius,
                new ConfigDescription("手を鎖の先端に近づけてグリップする判定距離。",
                    new AcceptableValueRange<float>(0.05f, 0.5f)));
            _collarScale = Config.Bind("Collar", "Scale", 1f,
                new ConfigDescription("Leash Choker の大きさ。", new AcceptableValueRange<float>(0.6f, 2f)));
            _collarScaleX = Config.Bind("Collar", "Scale X", 1f,
                new ConfigDescription("首輪の横幅倍率。", new AcceptableValueRange<float>(0.3f, 3f)));
            _collarScaleY = Config.Bind("Collar", "Scale Y", 1f,
                new ConfigDescription("首輪の高さ倍率。", new AcceptableValueRange<float>(0.3f, 3f)));
            _collarScaleZ = Config.Bind("Collar", "Scale Z", 1f,
                new ConfigDescription("首輪の前後幅倍率。", new AcceptableValueRange<float>(0.3f, 3f)));
            _collarOffsetX = Config.Bind("Collar", "Offset X (m)", 0f, "首輪の左右位置。");
            _collarOffsetY = Config.Bind("Collar", "Offset Y (m)", 0f, "首輪の上下位置。");
            _collarOffsetZ = Config.Bind("Collar", "Offset Z (m)", 0f, "首輪の前後位置。");
            _collarRotationX = Config.Bind("Collar", "Rotation X (deg)", 0f, "首輪のX軸回転。");
            _collarRotationY = Config.Bind("Collar", "Rotation Y (deg)", 0f, "首輪のY軸回転。");
            _collarRotationZ = Config.Bind("Collar", "Rotation Z (deg)", 0f, "首輪のZ軸回転。");
            _chainOffsetX = Config.Bind("Placement", "Chain side (m)", 0f, "鎖の接続位置の左右微調整。");
            _chainScale = Config.Bind("Chain", "Scale", 1f,
                new ConfigDescription("鎖全体の大きさと長さ。", new AcceptableValueRange<float>(0.5f, 2f)));
            _chainScaleX = Config.Bind("Chain", "Scale X", 1f,
                new ConfigDescription("鎖の横幅倍率。", new AcceptableValueRange<float>(0.3f, 3f)));
            _chainScaleY = Config.Bind("Chain", "Scale Y", 1f,
                new ConfigDescription("鎖の高さ倍率。", new AcceptableValueRange<float>(0.3f, 3f)));
            _chainScaleZ = Config.Bind("Chain", "Scale Z", 1f,
                new ConfigDescription("鎖の長さ方向の倍率。", new AcceptableValueRange<float>(0.3f, 3f)));
            _ringOffsetX = Config.Bind("Collar ring", "Offset X (m)", 0.055f, "鎖をつなぐ首輪内の左右位置。");
            _ringOffsetY = Config.Bind("Collar ring", "Offset Y (m)", -0.03f, "鎖をつなぐ首輪内の上下位置。");
            _ringOffsetZ = Config.Bind("Collar ring", "Offset Z (m)", 0f, "鎖をつなぐ首輪内の前後位置。");
            _collarAttachRadius = Config.Bind("Collar", "Attach radius (m)", 0.10f,
                new ConfigDescription("離した首輪が所定の首位置へ戻る距離。", new AcceptableValueRange<float>(0.02f, 0.5f)));
            _collarDetachRadius = Config.Bind("Collar", "Detach radius (m)", 0.20f,
                new ConfigDescription("首輪を掴んでこの距離より離すと外れる。Attach radius より大きくしてください。", new AcceptableValueRange<float>(0.03f, 1f)));
            _chainAttachRadius = Config.Bind("Chain", "Attach radius (m)", 0.08f,
                new ConfigDescription("鎖の接続側を首輪のリングへ近づける距離。", new AcceptableValueRange<float>(0.02f, 0.3f)));
            _showColliderPreviews = Config.Bind("Colliders", "Show previews", false,
                "鎖が参照する胴体・胸のコライダーを半透明で表示します。");
            Logger.LogInfo("VR neck chain ready; use ConfigurationManager > Chain > Show / remove chain");
        }

        private void DrawToggleButton(ConfigEntryBase entry)
        {
            if (GUILayout.Button(_chain == null ? "Show chain" : "Remove chain", GUILayout.MinWidth(150f)))
                ((ConfigEntry<bool>)entry).Value = true;
        }

        private void Update()
        {
            RegisterChainTab();
            if (Time.unscaledTime >= _nextSceneCheck)
            {
                _nextSceneCheck = Time.unscaledTime + 0.25f;
                ValidateScene();
            }
            if (_chain == null) return;
            UpdateGrip();
            UpdateDesktopGizmo();
            UpdateCollarGizmo();
        }

        private void LateUpdate()
        {
            if (_chain == null) return;
            if (_scene == null || !_scene.isActiveAndEnabled || _female == null ||
                _neck == null || _collarBone == null || _collar == null)
            {
                ClearChain();
                return;
            }

            UpdateCollarPlacement();
            UpdateCollarHold();
            UpdateChainScale();
            UpdateBodyColliders();
            UpdateColliderPreviews();
            UpdateRootAttachment();
            bool custom = !_rootAttached || _grabController != null || _desktopGizmoProxy != null;
            if (custom)
            {
                if (!_customSimulation) InitializeSimulation();
                if (_dynamicBone.enabled) _dynamicBone.enabled = false;
                SolveChain();
            }
            else
            {
                _chain.transform.position = _collar.transform.TransformPoint(_attachedRootLocal) -
                    _chain.transform.TransformVector(_chainRootLocalOffset);
                if (!_dynamicBone.enabled) _dynamicBone.enabled = true;
            }
            _customSimulation = custom;
            if (!_collarAttached && !_rootAttached && _collarController == null &&
                _rootController == null && _grabController == null &&
                _collarGizmoProxy == null && _rootGizmoProxy == null && _desktopGizmoProxy == null &&
                _collar.transform.position.y < _collarBone.position.y - 1.5f &&
                _points[0].y < _collarBone.position.y - 1.5f)
                ClearChain();
        }

        private void ValidateScene()
        {
            var active = FindObjectOfType<HSceneProc>();
            if (active == null || !active.isActiveAndEnabled)
            {
                if (_chain != null) ClearChain();
                _scene = null;
                return;
            }
            var activeFemale = FirstFemale(active);
            if (_chain != null && (active != _scene || activeFemale != _female)) ClearChain();
            _scene = active;
        }

        public bool IsSpawned() => _chain != null;

        public void ToggleSpawn()
        {
            if (_chain != null) ClearChain();
            else SpawnChain();
        }

        private void SpawnChain()
        {
            ValidateScene();
            if (_scene == null) { Logger.LogWarning("Chain requires an active normal H scene"); return; }
            _female = FirstFemale(_scene);
            _neck = FindBone(_female, "cf_j_neck");
            _collarBone = FindBone(_female, "a_n_neck");
            if (_neck == null || _collarBone == null)
            {
                Logger.LogWarning("Chain: female neck or accessory attachment bone not found");
                _female = null;
                return;
            }

            try
            {
                _collar = CommonLib.LoadAsset<GameObject>(CollarBundlePath, CollarAssetName, true, string.Empty);
                if (_collar == null) throw new InvalidOperationException("Leash Choker prefab could not be loaded");
                _collar.name = "MainGameVrNeckChain_Collar";
                _collar.transform.SetParent(_collarBone, false);
                _collarAttached = true;
                UpdateCollarPlacement();
                _collarPreviousPosition = _collar.transform.position;
                _chain = CommonLib.LoadAsset<GameObject>(BundlePath, AssetName, true, string.Empty);
                if (_chain == null) throw new InvalidOperationException("Chain Short Weighted prefab could not be loaded");
                _chain.name = "MainGameVrNeckChain";
                _chain.transform.localScale = ChainScaleVector();
                _chain.transform.position = NeckAnchor();
                // The prefab runs along local -Z. Point that axis down in world space.
                _chain.transform.rotation = Quaternion.FromToRotation(Vector3.back, Vector3.down);
                _dynamicBone = _chain.GetComponent<DynamicBone>();
                if (_dynamicBone == null) throw new InvalidOperationException("DynamicBone component not found");
                SetupBodyColliders();
                var all = _chain.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < BoneCount; i++)
                {
                    string boneName = "c_" + i.ToString("000");
                    foreach (var candidate in all)
                        if (candidate.name == boneName) { _bones[i] = candidate; break; }
                    if (_bones[i] == null) throw new InvalidOperationException("Missing chain bone " + boneName);
                    _restWorldRotations[i] = _bones[i].rotation;
                }
                _chainRootLocalOffset = _chain.transform.InverseTransformPoint(_bones[0].position);
                // Align the visible first chain bone with the collar ring, even when
                // the prefab root and c_000 have different local positions.
                _chain.transform.position += NeckAnchor() - _bones[0].position;
                _restRootRotation = _chain.transform.rotation;
                _chainLength = 0f;
                for (int i = 0; i < BoneCount - 1; i++)
                {
                    _segments[i] = Vector3.Distance(_bones[i].position, _bones[i + 1].position);
                    if (_segments[i] <= 0.001f) throw new InvalidOperationException("Invalid chain segment " + i);
                    _baseSegmentVectors[i] = _chain.transform.InverseTransformVector(
                        _bones[i + 1].position - _bones[i].position);
                    _chainLength += _segments[i];
                }
                _attachedRootLocal = _collar.transform.InverseTransformPoint(_bones[0].position);
                _rootAttached = true;
                _rootSnapArmed = false;
                InitializeSimulation();
                _dynamicBone.enabled = true;
                _customSimulation = false;
                Logger.LogInfo("Chain and collar spawned for " + _female.name + " length=" + _chainLength.ToString("F3") + " m");
            }
            catch (Exception ex)
            {
                Logger.LogError("Chain spawn failed: " + ex);
                ClearChain();
            }
        }

        private void UpdateGrip()
        {
            if (!VR.Active || VR.Mode == null) { ReleaseGrip(); ReleaseRootGrip(); ReleaseCollarGrip(); return; }
            if (_grabController != null && !Held(_grabController, _grabLock)) ReleaseGrip();
            if (_rootController != null && !Held(_rootController, _rootLock)) ReleaseRootGrip();
            if (_collarController != null && !Held(_collarController, _collarLock)) ReleaseCollarGrip();
            TryGrab(VR.Mode.Left);
            TryGrab(VR.Mode.Right);
        }

        private static bool Held(Controller controller, Controller.Lock focus)
        {
            return controller != null && controller.Input != null && focus != null && focus.IsValid &&
                Bindings.VrHeld("MainGameVrNeckChain.grab", controller.Input, "Grip");
        }

        private void TryGrab(Controller controller)
        {
            if (controller == null || controller.Input == null ||
                !Bindings.VrDown("MainGameVrNeckChain.grab", controller.Input, "Grip") ||
                controller == _grabController || controller == _rootController || controller == _collarController) return;
            float collarDistance = Vector3.Distance(controller.transform.position, _collar.transform.position);
            float rootDistance = _rootAttached ? float.MaxValue : Vector3.Distance(controller.transform.position, _bones[0].position);
            float tipDistance = Vector3.Distance(controller.transform.position, _bones[BoneCount - 1].position);
            float nearest = Mathf.Min(collarDistance, rootDistance, tipDistance);
            if (nearest > _grabRadius.Value) return;
            if (!controller.TryAcquireFocus(out Controller.Lock focus) || focus == null || !focus.IsValid)
            {
                Logger.LogWarning("Chain grip focus unavailable: " + controller.name);
                return;
            }
            // The collar ring overlaps the chain root; prefer the root when it is free.
            if (nearest == rootDistance)
            {
                _rootController = controller; _rootLock = focus;
            }
            else if (nearest == collarDistance)
            {
                _collarController = controller; _collarLock = focus;
                _collarGrabOffset = controller.transform.InverseTransformPoint(_collar.transform.position);
            }
            else
            {
                _grabController = controller; _grabLock = focus;
            }
            InitializeSimulation();
            if (_dynamicBone != null) _dynamicBone.enabled = false;
            Logger.LogInfo("Chain/collar grabbed: " + controller.name);
        }

        private void ReleaseGrip()
        {
            if (_grabController == null && (_grabLock == null || !_grabLock.IsValid)) return;
            _grabController = null;
            if (_grabLock != null && _grabLock.IsValid) _grabLock.SafeRelease();
            _grabLock = Controller.Lock.Invalid;
            Logger.LogInfo("Chain released");
        }

        private void ReleaseRootGrip()
        {
            if (_rootController != null && !_rootAttached) _rootAttachPending = true;
            _rootController = null;
            if (_rootLock != null && _rootLock.IsValid) _rootLock.SafeRelease();
            _rootLock = Controller.Lock.Invalid;
        }

        private void ReleaseCollarGrip()
        {
            if (_collarController != null && !_collarAttached) _collarAttachPending = true;
            _collarController = null;
            if (_collarLock != null && _collarLock.IsValid) _collarLock.SafeRelease();
            _collarLock = Controller.Lock.Invalid;
            TryAttachCollar();
            _collarAttachPending = false;
        }

        private void InitializeSimulation()
        {
            for (int i = 0; i < BoneCount; i++) _points[i] = _previous[i] = _bones[i].position;
        }

        private void UpdateRootAttachment()
        {
            if (_rootAttached) return;
            Vector3 root = _rootController != null ? _rootController.transform.position :
                _rootGizmoProxy != null ? _rootGizmoProxy.transform.position : _points[0];
            float distance = Vector3.Distance(root, NeckAnchor());
            if (!_rootSnapArmed && distance > _chainAttachRadius.Value * 1.5f) _rootSnapArmed = true;
            if (!_rootAttachPending || _rootController != null || _rootGizmoProxy != null) return;
            _rootAttachPending = false;
            if (distance > _chainAttachRadius.Value) return;
            _attachedRootLocal = _collar.transform.InverseTransformPoint(root);
            _rootAttached = true;
            _rootSnapArmed = false;
            ReleaseRootGrip();
            DetachRootGizmo();
            Logger.LogInfo("Chain root attached to collar at contact point");
        }

        private void DetachRoot()
        {
            if (!_rootAttached) return;
            InitializeSimulation();
            _rootAttached = false;
            _rootSnapArmed = false;
            _rootAttachPending = false;
            if (_dynamicBone != null) _dynamicBone.enabled = false;
        }

        private void SolveChain()
        {
            bool rootPinned = _rootAttached || _rootController != null || _rootGizmoProxy != null;
            bool tipPinned = _grabController != null || _desktopGizmoProxy != null;
            Vector3 root = _rootAttached ? _collar.transform.TransformPoint(_attachedRootLocal) :
                _rootController != null ? _rootController.transform.position :
                _rootGizmoProxy != null ? _rootGizmoProxy.transform.position : _points[0];
            Vector3 tip = _grabController != null ? _grabController.transform.position :
                _desktopGizmoProxy != null ? _desktopGizmoProxy.transform.position : _points[BoneCount - 1];
            if (tipPinned) tip = PushOutsideBody(tip);
            float distance = Vector3.Distance(root, tip);
            if (rootPinned && tipPinned && distance >= _chainLength - 0.005f)
            {
                Vector3 direction = distance < 0.0001f ? Vector3.down : (tip - root) / distance;
                float along = 0f;
                for (int i = 0; i < BoneCount; i++)
                {
                    if (i > 0) along += _segments[i - 1];
                    _points[i] = _previous[i] = root + direction * along;
                }
                ApplyChainPose();
                return;
            }
            float dt = Mathf.Min(Time.deltaTime, 0.033f);
            for (int i = 0; i < BoneCount; i++)
            {
                if ((i == 0 && rootPinned) || (i == BoneCount - 1 && tipPinned)) continue;
                Vector3 current = _points[i];
                _points[i] += (_points[i] - _previous[i]) * 0.94f + Vector3.down * (Gravity * dt * dt);
                _previous[i] = current;
            }
            for (int pass = 0; pass < 12; pass++)
            {
                if (rootPinned) _points[0] = root;
                if (tipPinned) _points[BoneCount - 1] = tip;
                for (int i = 0; i < BoneCount - 1; i++)
                {
                    Vector3 delta = _points[i + 1] - _points[i];
                    float length = delta.magnitude;
                    if (length < 0.000001f) continue;
                    Vector3 correction = delta * ((_segments[i] - length) / length);
                    bool leftFixed = i == 0 && rootPinned;
                    bool rightFixed = i == BoneCount - 2 && tipPinned;
                    if (leftFixed && rightFixed) continue;
                    if (leftFixed) _points[i + 1] += correction;
                    else if (rightFixed) _points[i] -= correction;
                    else { _points[i] -= correction * 0.5f; _points[i + 1] += correction * 0.5f; }
                }
                ResolveBodyCollisions(rootPinned, tipPinned);
            }
            if (rootPinned) _points[0] = root;
            if (tipPinned) _points[BoneCount - 1] = tip;
            ApplyChainPose();
        }

        private void ApplyChainPose()
        {
            _chain.transform.position = _points[0] - _chain.transform.TransformVector(_chainRootLocalOffset);
            for (int i = 0; i < BoneCount; i++)
            {
                int next = Math.Min(i + 1, BoneCount - 1);
                Vector3 segmentDirection = next == i ? _points[i] - _points[i - 1] : _points[next] - _points[i];
                Quaternion rest = _chain.transform.rotation * Quaternion.Inverse(_restRootRotation) * _restWorldRotations[i];
                Quaternion rotation = Quaternion.FromToRotation(rest * Vector3.back, segmentDirection) * rest;
                _bones[i].SetPositionAndRotation(_points[i], rotation);
            }
        }

        private void UpdateCollarPlacement()
        {
            if (_collar == null) return;
            _collar.transform.localScale = CollarScaleVector();
            if (!_collarAttached) return;
            _collar.transform.SetParent(_collarBone, false);
            _collar.transform.localPosition = new Vector3(
                _collarOffsetX.Value, _collarOffsetY.Value, _collarOffsetZ.Value);
            _collar.transform.localRotation = Quaternion.Euler(
                _collarRotationX.Value, _collarRotationY.Value, _collarRotationZ.Value);
        }

        private Vector3 CollarHome()
        {
            return _collarBone.TransformPoint(new Vector3(
                _collarOffsetX.Value, _collarOffsetY.Value, _collarOffsetZ.Value));
        }

        private void UpdateCollarHold()
        {
            if (_collarController != null || _collarGizmoProxy != null)
            {
                Vector3 target = _collarController != null
                    ? _collarController.transform.TransformPoint(_collarGrabOffset)
                    : _collarGizmoProxy.transform.position;
                if (_collarAttached && Vector3.Distance(target, CollarHome()) >
                    Mathf.Max(_collarDetachRadius.Value, _collarAttachRadius.Value + 0.01f))
                {
                    _collar.transform.SetParent(null, true);
                    _collarAttached = false;
                    _collarSnapArmed = false;
                    _collarPreviousPosition = _collar.transform.position;
                }
                if (!_collarAttached)
                {
                    _collar.transform.position = target;
                    _collarPreviousPosition = target;
                }
            }
            else if (!_collarAttached)
            {
                float distance = Vector3.Distance(_collar.transform.position, CollarHome());
                if (!_collarSnapArmed && distance > _collarAttachRadius.Value * 1.5f)
                    _collarSnapArmed = true;
                if (_collarAttachPending)
                {
                    TryAttachCollar();
                    _collarAttachPending = false;
                }
                if (!_collarAttached)
                {
                    float dt = Mathf.Min(Time.deltaTime, 0.033f);
                    Vector3 current = _collar.transform.position;
                    Vector3 falling = current + (current - _collarPreviousPosition) * 0.94f +
                        Vector3.down * (Gravity * dt * dt);
                    _collarPreviousPosition = current;
                    if (_rootAttached && (_grabController != null || _desktopGizmoProxy != null))
                    {
                        Vector3 tip = _grabController != null ? _grabController.transform.position :
                            _desktopGizmoProxy.transform.position;
                        Vector3 ringOffset = _collar.transform.TransformVector(_attachedRootLocal);
                        Vector3 root = falling + ringOffset;
                        Vector3 displacement = root - tip;
                        if (displacement.magnitude > _chainLength)
                            falling = tip + displacement.normalized * _chainLength - ringOffset;
                    }
                    _collar.transform.position = falling;
                }
            }
        }

        private void TryAttachCollar()
        {
            if (_collar == null || _collarAttached ||
                Vector3.Distance(_collar.transform.position, CollarHome()) > _collarAttachRadius.Value) return;
            _collarAttached = true;
            _collarSnapArmed = false;
            _collarAttachPending = false;
            UpdateCollarPlacement();
            Logger.LogInfo("Collar attached to neck");
        }

        private void UpdateChainScale()
        {
            if (_chain == null) return;
            Vector3 scale = ChainScaleVector();
            if ((_chain.transform.localScale - scale).sqrMagnitude < 0.000001f) return;
            _chain.transform.localScale = scale;
            _chainLength = 0f;
            for (int i = 0; i < BoneCount - 1; i++)
            {
                _segments[i] = Vector3.Scale(_baseSegmentVectors[i], scale).magnitude;
                _chainLength += _segments[i];
            }
            if (_customSimulation)
                for (int i = 0; i < BoneCount; i++)
                    _points[i] = _previous[i] = _bones[i].position;
        }

        private Vector3 CollarScaleVector() => new Vector3(
            _collarScale.Value * _collarScaleX.Value,
            _collarScale.Value * _collarScaleY.Value,
            _collarScale.Value * _collarScaleZ.Value);

        private Vector3 ChainScaleVector() => new Vector3(
            _chainScale.Value * _chainScaleX.Value,
            _chainScale.Value * _chainScaleY.Value,
            _chainScale.Value * _chainScaleZ.Value);

        private Vector3 NeckAnchor()
        {
            Vector3 ring = _collar.transform.TransformPoint(new Vector3(
                _ringOffsetX.Value, _ringOffsetY.Value, _ringOffsetZ.Value));
            Vector3 adjustment = _neck.rotation * new Vector3(_chainOffsetX.Value,
                _neckDown.Value - NeckOffset.y, _neckForward.Value - NeckOffset.z);
            return ring + adjustment;
        }

        private static ChaControl FirstFemale(HSceneProc scene)
        {
            var list = FemaleListField?.GetValue(scene) as IList;
            if (list == null) return null;
            foreach (var entry in list)
                if (entry is ChaControl female && female != null) return female;
            return null;
        }

        private static Transform FindBone(ChaControl character, string name)
        {
            if (character == null) return null;
            foreach (var bone in character.GetComponentsInChildren<Transform>(true))
                if (string.Equals(bone.name, name, StringComparison.OrdinalIgnoreCase)) return bone;
            return null;
        }

        private void ClearChain()
        {
            DetachDesktopGizmo();
            DetachRootGizmo();
            DetachCollarGizmo();
            ReleaseGrip();
            ReleaseRootGrip();
            ReleaseCollarGrip();
            ClearBodyColliders();
            if (_chain != null) Destroy(_chain);
            if (_collar != null) Destroy(_collar);
            _chain = null;
            _collar = null;
            _dynamicBone = null;
            _female = null;
            _neck = null;
            _collarBone = null;
            _collarAttached = false;
            _rootAttached = false;
            _customSimulation = false;
            _rootAttachPending = false;
            _collarAttachPending = false;
            for (int i = 0; i < BoneCount; i++) _bones[i] = null;
        }

        private void OnDestroy()
        {
            MainGameUnifiedUi.UnifiedUiApi.UnregisterTab(ChainTabId);
            ClearChain();
        }
    }
}
