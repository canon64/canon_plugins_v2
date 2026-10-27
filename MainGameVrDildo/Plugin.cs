using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using Valve.VR;
using VRGIN.Controls;
using VRGIN.Core;

namespace MainGameVrDildo
{
    [BepInPlugin(Guid, "MainGameVrDildo", Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInDependency("com.kks.maingame.unifiedui", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency(MainGameTransformGizmo.Plugin.GUID, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency(MainGameUiInputCapture.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.kks.maingame.voicefaceeventbridge", BepInDependency.DependencyFlags.HardDependency)]
    [DefaultExecutionOrder(12150)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.kks.maingame.vrdildo";
        public const string Version = "0.3.0";
        private const string BundlePath = "studio/00.unity3d";
        private const string AssetName = "p_koi_stu_vibe02_00";
        private const string Manifest = "studio00";
        private const int BoneCount = 8;
        private static readonly float[] BoneY = { 0.080f, 0.103f, 0.126f, 0.149f, 0.172f, 0.195f, 0.218f, 0.245f };
        private static readonly FieldInfo FemaleListField = typeof(HSceneProc).GetField("lstFemale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        public static Plugin Instance { get; private set; }
        private DildoSettings _settings;
        private string _settingsPath;
        private HSceneProc _scene;
        private bool _autoSpawnAttempted;
        private float _sceneDiscoveredAt;
        private ChaControl _female;
        private Transform _entry;
        private Transform _deep;
        private Transform _mouthEntry;
        private Transform _mouthDeep;
        private GameObject _mouthFallback;
        private TargetKind _selectedTarget;
        private Vector3 _selectedAxis;
        private bool _oralContact;
        private GameObject _model;
        private Transform[] _bones;
        private Transform[] _colliderBones;
        private Quaternion[] _rest;
        private readonly List<DynamicBoneCollider> _colliders = new List<DynamicBoneCollider>();
        private readonly List<GameObject> _previews = new List<GameObject>();
        private readonly List<DynamicBone> _registeredBones = new List<DynamicBone>();
        private Controller _grabController;
        private Controller.Lock _grabLock = Controller.Lock.Invalid;
        private Vector3 _grabOffset;
        private Quaternion _grabRotationOffset;
        private bool _engaged;
        private float _nextAutoTargetDiagnostic;
        private bool _aibuHasSample;
        private Vector3 _aibuLastRigidTip;
        private readonly AibuMotionTracker _aibuMotion = new AibuMotionTracker();
        private bool _aibuStrongVoice;
        private bool _voiceHookObserved;
        private bool _motionActive;
        private Harmony _voiceHarmony;
        private Harmony _motionGaugeHarmony;
        private bool _showSettings;
        private bool _settingsChanged;
        private float _nextSceneCheck;
        private float _nextColliderRefresh;
        private Rect _window = new Rect(30, 80, 480, 650);
        private Vector2 _scroll;
        private int _selectedSegment;
        private float _lastColliderEditorDrawAt = -100f;
        private readonly Dictionary<string, string> _edits = new Dictionary<string, string>();
        private bool _motionLogEnabled;
        private string _motionLogPath;

        private void LogMotionEvent(string state, string detail = null)
        {
            if (MotionTraceEnabled) TraceVibeEvent(state, detail);
            if (!_motionLogEnabled) return;
            try
            {
                File.AppendAllText(_motionLogPath, "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " +
                    state + (string.IsNullOrEmpty(detail) ? "" : " " + detail) + Environment.NewLine);
            }
            catch (Exception ex)
            {
                _motionLogEnabled = false;
                Logger.LogWarning("Motion log disabled: " + ex.Message);
            }
        }

        private void LogDiagnostic(string message)
        {
            if (_settings != null && _settings.DiagnosticLogEnabled) Logger.LogInfo(message);
        }

        private void Awake()
        {
            Instance = this;
            _settingsPath = Path.Combine(Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath, "VrDildoSettings.json");
            string pluginDir = Path.GetDirectoryName(_settingsPath);
            _motionTrace = new CanonPlugins.Shared.MotionTraceLog(pluginDir, GetType().Assembly,
                message => Logger.LogWarning(message));
            _traceInventoryAt = Time.unscaledTime + 5f;
            _motionLogPath = Path.Combine(pluginDir, "VrDildoMotion.log");
            _motionLogEnabled = File.Exists(Path.Combine(pluginDir, "VrDildoMotionDebug.enabled"));
            if (_motionLogEnabled) LogMotionEvent("START", "legacy reversal diagnostic enabled");
            LoadSettings();
            if (MotionTraceEnabled) TraceVibeEvent("CONFIG", "path=" + _settingsPath
                + " settings=" + JsonUtility.ToJson(_settings));
            InstallVoiceHook();
            InstallOralVoiceHooks();
            InstallMotionGaugeHook();
            InstallOgazmEyes();
            MainGameVoiceFaceEventBridge.PublicApi.SetImpactCueEnabled(Guid, true);
            Logger.LogInfo("MainGameVrDildo ready");
            if (Environment.GetEnvironmentVariable("KKS_VRVIBE_SELFTEST") == "1")
                StartCoroutine(VerifyRigInGame());
        }

        private IEnumerator VerifyRigInGame()
        {
            yield return new WaitForSecondsRealtime(15f);
            GameObject sample = null;
            Mesh rest = null;
            Mesh bent = null;
            try
            {
                sample = CommonLib.LoadAsset<GameObject>(BundlePath, AssetName, true, Manifest);
                if (sample == null) throw new InvalidOperationException("Stock Vibe prefab did not load");
                BuildWeightedRig(sample);
                var skin = sample.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skin == null || skin.bones.Length != BoneCount || skin.sharedMesh.vertexCount != 1912 ||
                    skin.sharedMaterial == null)
                    throw new InvalidOperationException("Weighted mesh or eight-bone chain is invalid");
                rest = new Mesh();
                bent = new Mesh();
                skin.BakeMesh(rest);
                for (int i = 2; i <= 4; i++) skin.bones[i].localRotation = Quaternion.Euler(0f, 0f, 15f);
                skin.BakeMesh(bent);
                var a = rest.vertices;
                var b = bent.vertices;
                float handleMotion = 0f;
                float tipMotion = 0f;
                var original = skin.sharedMesh.vertices;
                for (int i = 0; i < a.Length; i++)
                {
                    float moved = Vector3.Distance(a[i], b[i]);
                    if (original[i].y <= BoneY[0]) handleMotion = Mathf.Max(handleMotion, moved);
                    if (original[i].y >= BoneY[BoneCount - 1]) tipMotion = Mathf.Max(tipMotion, moved);
                }
                if (handleMotion > 0.0001f || tipMotion < 0.03f)
                    throw new InvalidOperationException("Unexpected deformation: handle=" + handleMotion + " tip=" + tipMotion);
                Logger.LogInfo("VRVIBE_SELFTEST_PASS vertices=" + a.Length + " handleMotion=" + handleMotion.ToString("F6") +
                               " tipMotion=" + tipMotion.ToString("F4") + " material=" + (skin.sharedMaterial != null));
            }
            catch (Exception ex) { Logger.LogError("VRVIBE_SELFTEST_FAIL " + ex); }
            finally
            {
                if (sample != null) Destroy(sample);
                if (rest != null) Destroy(rest);
                if (bent != null) Destroy(bent);
            }
        }

        private void OnDestroy()
        {
            ReleaseGaugeEffects();
            RemoveOgazmEyes();
            MainGameVoiceFaceEventBridge.PublicApi.SetImpactCueEnabled(Guid, false);
            ClearStandaloneInputCapture();
            MainGameUnifiedUi.UnifiedUiApi.UnregisterTab(TestTabId);
            DeleteModel();
            RemoveOralVoiceHooks();
            if (_voiceHarmony != null) _voiceHarmony.UnpatchSelf();
            if (_motionGaugeHarmony != null) _motionGaugeHarmony.UnpatchSelf();
            Instance = null;
            _motionTrace?.Dispose();
        }

        public bool IsSpawned() { return _model != null; }
        public bool IsSettingsOpen() { return _showSettings; }
        public void ToggleSettings()
        {
            if (MainGameUnifiedUi.UnifiedUiApi.ShowTab(TestTabId))
            {
                _showSettings = false;
                return;
            }
            _showSettings = !_showSettings;
        }
        public void ToggleSpawn()
        {
            _autoSpawnAttempted = true;
            if (_model != null) DeleteModel();
            else Spawn();
        }

        private void Spawn()
        {
            ResolveScene();
            if (_scene == null) { Logger.LogWarning("Dildo spawn requires an active H scene"); return; }
            try
            {
                var go = CommonLib.LoadAsset<GameObject>(BundlePath, AssetName, true, Manifest);
                if (go == null) { Logger.LogError("Dildo asset missing: " + BundlePath); return; }
                _model = go;
                _model.name = "VR Vibe (weighted FK/IK)";
                BuildWeightedRig(_model);
                _model.transform.localScale = Vector3.one * _settings.ModelScale;
                var camera = Camera.main;
                if (camera != null)
                {
                    _model.transform.position = camera.transform.position + camera.transform.forward * _settings.SpawnDistance;
                    _model.transform.rotation = camera.transform.rotation;
                }
                _bones = new Transform[BoneCount];
                _rest = new Quaternion[BoneCount];
                var all = go.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < _bones.Length; i++)
                {
                    string name = "VRVibe_" + i.ToString("00");
                    foreach (var candidate in all)
                        if (candidate.name == name) { _bones[i] = candidate; break; }
                    if (_bones[i] == null) throw new InvalidOperationException("Missing FK bone " + name);
                    _rest[i] = _bones[i].localRotation;
                }
                _colliderBones = new Transform[_settings.Segments.Length];
                for (int i = 0; i < _colliderBones.Length; i++)
                {
                    string name = _settings.Segments[i].Bone;
                    foreach (var candidate in all)
                        if (candidate.name == name) { _colliderBones[i] = candidate; break; }
                    if (_colliderBones[i] == null) throw new InvalidOperationException("Missing collider bone " + name);
                }
                RebuildColliders();
                Logger.LogInfo("VR Vibe spawned with 8 weighted FK/IK bones and 8 collider anchors");
            }
            catch (Exception ex)
            {
                Logger.LogError("Dildo spawn failed: " + ex);
                DeleteModel();
            }
        }

        // The selected stock Vibe is a static MeshRenderer. Skin a private mesh copy so
        // neither the game's shared mesh nor its material is changed by the FK chain.
        private static void BuildWeightedRig(GameObject root)
        {
            var filter = root.GetComponentInChildren<MeshFilter>(true);
            var original = filter != null ? filter.GetComponent<MeshRenderer>() : null;
            if (filter == null || filter.sharedMesh == null || original == null)
            {
                var filters = root.GetComponentsInChildren<MeshFilter>(true);
                var renderers = root.GetComponentsInChildren<Renderer>(true);
                throw new InvalidOperationException("Stock Vibe mesh or renderer is missing: root=" + root.name +
                    " filters=" + filters.Length + " renderers=" + renderers.Length);
            }
            var meshRoot = filter.transform;
            var mesh = Instantiate(filter.sharedMesh);
            mesh.name = "VRVibe_weighted_mesh";
            var vertices = mesh.vertices;
            var weights = new BoneWeight[vertices.Length];
            var bones = new Transform[BoneCount];
            Transform parent = meshRoot;
            for (int i = 0; i < BoneCount; i++)
            {
                var bone = new GameObject("VRVibe_" + i.ToString("00")).transform;
                bone.SetParent(parent, false);
                bone.localPosition = new Vector3(0f, i == 0 ? BoneY[0] : BoneY[i] - BoneY[i - 1], 0f);
                bones[i] = bone;
                parent = bone;
            }
            var bindposes = new Matrix4x4[BoneCount];
            for (int i = 0; i < BoneCount; i++)
                bindposes[i] = bones[i].worldToLocalMatrix * meshRoot.localToWorldMatrix;
            for (int v = 0; v < vertices.Length; v++)
            {
                float y = vertices[v].y;
                int lower = 0;
                while (lower < BoneCount - 2 && y > BoneY[lower + 1]) lower++;
                float blend = Mathf.Clamp01((y - BoneY[lower]) / (BoneY[lower + 1] - BoneY[lower]));
                weights[v] = new BoneWeight
                {
                    boneIndex0 = lower, weight0 = 1f - blend,
                    boneIndex1 = lower + 1, weight1 = blend
                };
            }
            mesh.bindposes = bindposes;
            mesh.boneWeights = weights;
            var skin = filter.gameObject.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = bones;
            skin.rootBone = bones[0];
            skin.sharedMaterials = original.sharedMaterials;
            skin.shadowCastingMode = original.shadowCastingMode;
            skin.receiveShadows = original.receiveShadows;
            skin.updateWhenOffscreen = true;
            skin.localBounds = new Bounds(new Vector3(0f, 0.1f, 0f), Vector3.one);
            original.enabled = false;
            Destroy(original);
            Destroy(filter);
        }

        private void DeleteModel()
        {
            ReleaseGaugeSweat();
            ReleaseGaugeFace();
            StopOralAudio();
            ReleaseMouthOpening();
            DetachDesktopGizmo();
            ReleaseGrab();
            UnregisterColliders();
            DestroyColliderPreviewMaterials();
            _colliders.Clear();
            _previews.Clear();
            _engaged = false;
            _selectedTarget = TargetKind.None;
            _selectedAxis = Vector3.zero;
            _oralContact = false;
            ResetAibuMotion();
            if (_model != null) Destroy(_model);
            _model = null;
            _bones = null;
            _colliderBones = null;
            _rest = null;
        }

        private void ResolveScene()
        {
            var current = FindObjectOfType<HSceneProc>();
            if (current == null || !current.isActiveAndEnabled)
            {
                ReleaseGaugeEffects();
                if (_scene != null) DeleteModel();
                ClearMouthTargets();
                _scene = null; _female = null; _entry = null; _deep = null;
                _autoSpawnAttempted = false;
                return;
            }
            if (current == _scene && _female != null && _entry != null && _deep != null &&
                (!_settings.OralTargetEnabled || (_mouthEntry != null && _mouthDeep != null))) return;
            if (current != _scene)
            {
                ReleaseGaugeEffects();
                if (_scene != null) DeleteModel();
                _autoSpawnAttempted = false;
                _sceneDiscoveredAt = Time.unscaledTime;
            }
            _scene = current;
            var list = FemaleListField?.GetValue(current) as IList;
            ChaControl female = null;
            if (list != null)
                foreach (var item in list) { female = item as ChaControl; if (female != null) break; }
            if (female != _female) { ReleaseGaugeEffects(); ReleaseGaugeFace(); ReleaseMouthOpening(); ClearMouthTargets(); UnregisterColliders(); _engaged = false; ResetAibuMotion(); }
            _female = female;
            _entry = FindBone(female, "cf_J_Vagina_root");
            _deep = FindBone(female, "cf_j_waist01");
            ResolveMouthTargets();
        }

        private static Transform FindBone(ChaControl character, string name)
        {
            if (character == null) return null;
            foreach (var bone in character.GetComponentsInChildren<Transform>(true))
                if (bone.name == name) return bone;
            return null;
        }

        private void Update()
        {
            TraceWheelInput();
            RegisterTestTabIfAvailable();
            if (Bindings.Down("MainGameVrDildo.spawn", "V + Ctrl + Shift")) { _autoSpawnAttempted = true; ToggleSpawn(); }
            if (Bindings.Down("MainGameVrDildo.settings", "B + Ctrl + Shift")) ToggleSettings();
            SyncStandaloneInputCapture();
            UpdateOralAudio();
            if (Time.unscaledTime >= _nextSceneCheck)
            {
                _nextSceneCheck = Time.unscaledTime + 0.5f;
                ResolveScene();
                if (_settings.AutoSpawnOnH && !_autoSpawnAttempted && _scene != null && _model == null &&
                    Time.unscaledTime - _sceneDiscoveredAt >= 1f)
                {
                    _autoSpawnAttempted = true;
                    Spawn();
                }
            }
            if (_model == null) return;
            UpdateColliderPreviewAppearance();
            UpdateGrab();
            UpdateDesktopGizmo();
            if (_settingsChanged)
            {
                _settingsChanged = false;
                _settings.Normalize();
                PauseAibuMotion("settings_changed");
                _model.transform.localScale = Vector3.one * _settings.ModelScale;
                UnregisterColliders();
                UpdateColliders();
                if (_engaged && _selectedTarget == TargetKind.Vaginal) RegisterColliders();
            }
        }

        private void LateUpdate()
        {
            try { UpdateVibeLate(); }
            finally { DispatchPendingSpecialVoices(); UpdateGaugeFace(); UpdateGaugeEffects(); TickVibeDiagnostics(); }
        }

        private void UpdateVibeLate()
        {
            if (_model == null || _bones == null) return;
            if (!_settings.Enabled)
            {
                StopOralAudio();
                ReleaseMouthOpening();
                _selectedTarget = TargetKind.None;
                _selectedAxis = Vector3.zero;
                _oralContact = false;
                UnregisterColliders();
                _engaged = false;
                ResetAibuMotion();
                RelaxBones();
                return;
            }
            if (!_settings.AutoTarget) ApplyManualFk();
            if (!TrySelectTarget(_bones[BoneCount - 1].position, out TargetKind target, out Vector3 entry, out Vector3 deep))
            {
                StopOralAudio();
                ReleaseMouthOpening();
                _selectedTarget = TargetKind.None;
                _selectedAxis = Vector3.zero;
                _oralContact = false;
                if (_settings.AutoTarget && Time.unscaledTime >= _nextAutoTargetDiagnostic)
                {
                    _nextAutoTargetDiagnostic = Time.unscaledTime + 10f;
                    Logger.LogWarning("VR Vibe auto bend waiting for target: female=" + (_female != null)
                        + " entry=" + (_entry != null) + " deep=" + (_deep != null));
                }
                UnregisterColliders();
                _engaged = false;
                PauseAibuMotion("target_unavailable");
                return;
            }
            if (target != _selectedTarget)
            {
                StopOralAudio();
                UnregisterColliders();
                _engaged = false;
                PauseAibuMotion("target_changed");
            }
            _selectedTarget = target;
            _selectedAxis = (deep - entry).normalized;
            Vector3 tip = _bones[BoneCount - 1].position;
            Vector3 path = deep - entry;
            float pathLength = path.magnitude;
            if (pathLength < 0.001f) { ReleaseMouthOpening(); UnregisterColliders(); PauseAibuMotion("invalid_path"); RelaxBones(); return; }
            Vector3 direction = path / pathLength;
            float along = Vector3.Dot(tip - entry, direction);
            Vector3 closest = entry + direction * Mathf.Clamp(along, 0f, pathLength);
            // The tip may already be past the entrance when the object is moved quickly.
            // Test the shaft as well so an insertion does not miss the one-frame tip gate.
            Vector3 shaftStart = _bones[0].position;
            Vector3 shaft = tip - shaftStart;
            float shaftT = shaft.sqrMagnitude > 0.000001f
                ? Mathf.Clamp01(Vector3.Dot(entry - shaftStart, shaft) / shaft.sqrMagnitude) : 0f;
            float shaftDistance = Vector3.Distance(entry, shaftStart + shaft * shaftT);
            if (!_engaged && along >= -_settings.EngageDistance
                && (Vector3.Distance(tip, entry) <= _settings.EngageDistance
                    || shaftDistance <= _settings.EngageDistance)) _engaged = true;
            else if (_engaged && (Vector3.Distance(tip, closest) >= _settings.ReleaseDistance || along < -_settings.ReleaseDistance)) _engaged = false;
            if (_settings.AutoTarget && Time.unscaledTime >= _nextAutoTargetDiagnostic)
            {
                _nextAutoTargetDiagnostic = Time.unscaledTime + 10f;
                LogDiagnostic("VR Vibe auto bend: engaged=" + _engaged
                    + " tipEntry=" + Vector3.Distance(tip, entry).ToString("F3")
                    + " shaftEntry=" + shaftDistance.ToString("F3")
                    + " depth=" + along.ToString("F3") + " path=" + pathLength.ToString("F3"));
            }
            // BetterPenetration drives the bone chain toward the entrance before contact;
            // contact only controls colliders, not whether the shaft follows its target.
            if (_settings.AutoTarget) BendToward(entry, deep);
            if (target == TargetKind.Oral)
            {
                // A deeply inserted tip can pass the inner mouth target while the
                // shaft still crosses the mouth entrance.
                _oralContact = along >= 0f &&
                    (Vector3.Distance(tip, closest) <= _settings.OralContactRadius ||
                     shaftDistance <= _settings.OralContactRadius);
                UpdateMouthOpening(true, Vector3.Distance(tip, entry));
                UpdateOralContact();
                UnregisterColliders();
                _engaged = false;
                PauseAibuMotion("oral_target");
                return;
            }
            UpdateMouthOpening(false, Vector3.Distance(tip, entry));
            StopOralAudio();
            _oralContact = false;
            if (!_engaged)
            {
                UnregisterColliders();
                UpdateAibuMotion(entry, deep);
                return;
            }
            if (Time.unscaledTime >= _nextColliderRefresh)
            {
                _nextColliderRefresh = Time.unscaledTime + 1f;
                RegisterColliders();
            }
            UpdateAibuMotion(entry, deep);
        }

        private void ResetAibuMotion()
        {
            _aibuHasSample = false;
            _aibuMotion.Reset();
            _aibuStrongVoice = false;
            _voiceHookObserved = false;
            ResetVibeReaction();
        }

        private void UpdateAibuMotion(Vector3 entry, Vector3 deep)
        {
            HFlag flags = _scene != null ? _scene.flags : null;
            if ((!_settings.AibuGaugeEnabled && !_settings.AibuStrongVoiceEnabled &&
                 !_settings.MotionGaugeEnabled && !_settings.BodyReactionEnabled) ||
                flags == null || flags.mode != HFlag.EMode.aibu)
            {
                ResetAibuMotion();
                return;
            }
            if (_settings.AibuRequireGrip && _grabController == null && !IsDesktopGizmoDragging())
            {
                PauseAibuMotion("grip_required");
                return;
            }
            Vector3 axis = deep - entry;
            float length = axis.magnitude;
            if (length < 0.001f || Time.deltaTime <= 0f)
            {
                PauseAibuMotion("invalid_motion_sample");
                return;
            }
            axis /= length;
            Vector3 fromEntry = _bones[BoneCount - 1].position - entry;
            float tipDepth = Vector3.Dot(fromEntry, axis);
            float radial = (fromEntry - axis * tipDepth).magnitude;
            bool nearPassage = tipDepth >= -0.02f && tipDepth <= length + 0.04f &&
                               radial <= _settings.AibuContactRadius;
            if (!nearPassage)
            {
                PauseAibuMotion("outside_passage");
                return;
            }
            // Use the rigid tip location for travel. IK bending alone must not earn gauge.
            Vector3 rigidTip = _bones[0].parent.TransformPoint(new Vector3(0f, BoneY[BoneCount - 1], 0f));
            float depth = Vector3.Dot(rigidTip - entry, axis);
            float travel = 0f;
            float signedTravel = 0f;
            if (_aibuHasSample)
            {
                // Only the hand-driven rigid Vibe movement counts. Female IK and
                // target-bone motion must not feed back into the reaction detector.
                signedTravel = Vector3.Dot(rigidTip - _aibuLastRigidTip, axis);
                travel = Mathf.Abs(signedTravel);
                // A single-frame jump is a scene/target discontinuity, not physical travel.
                if (travel > Mathf.Max(0.2f, 3f * Time.deltaTime))
                {
                    PauseAibuMotion("motion_jump");
                    travel = 0f;
                    signedTravel = 0f;
                }
                else if (travel / Time.deltaTime < _settings.AibuMinMotionSpeed)
                {
                    travel = 0f;
                    signedTravel = 0f;
                }
            }
            _aibuLastRigidTip = rigidTip;
            _aibuHasSample = true;
            // Keep the rolling distance for voice selection; gauge gain is applied
            // continuously so a completed window cannot cause a sudden jump.
            _aibuMotion.Add(Time.time, travel * 100f,
                            _settings.AibuWindowSeconds, out _);
            UpdateVibeReaction(axis, length, depth, signedTravel, rigidTip);
            if (_settings.AibuGaugeEnabled && travel > 0f)
            {
                float points = Mathf.Min(travel * _settings.AibuPointsPerMetre,
                                         _settings.AibuMaxPointsPerSecond * Time.deltaTime);
                if (points > 0f) flags.FemaleGaugeUp(points, false, true);
            }
            bool wasStrong = _aibuStrongVoice;
            if (!_settings.AibuStrongVoiceEnabled) _aibuStrongVoice = false;
            else if (!_aibuStrongVoice && _aibuMotion.RollingCm >= _settings.AibuStrongVoiceOnCm)
                _aibuStrongVoice = true;
            else if (_aibuStrongVoice && _aibuMotion.RollingCm <= _settings.AibuStrongVoiceOffCm)
                _aibuStrongVoice = false;
            if (wasStrong != _aibuStrongVoice)
            {
                _voiceHookObserved = false;
                LogDiagnostic("VR Vibe strong voice " + (_aibuStrongVoice ? "ON" : "OFF") +
                               " travel=" + _aibuMotion.RollingCm.ToString("F1") + "cm");
            }
        }

        private void RebuildColliders()
        {
            UnregisterColliders();
            DestroyColliderPreviewMaterials();
            foreach (var collider in _colliders)
                if (collider != null) Destroy(collider.gameObject);
            _colliders.Clear();
            _previews.Clear();
            if (_colliderBones == null) return;
            for (int i = 0; i < _colliderBones.Length; i++)
            {
                var bone = _colliderBones[i];
                var node = new GameObject("VRDildoCollider_" + i.ToString("00"));
                node.transform.SetParent(bone, false);
                var collider = node.AddComponent<DynamicBoneCollider>();
                collider.m_Bound = DynamicBoneCollider.Bound.Outside;
                collider.m_Direction = DynamicBoneCollider.Direction.Y;
                _colliders.Add(collider);
                var preview = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                preview.name = "VRDildoColliderPreview_" + i.ToString("00");
                preview.transform.SetParent(node.transform, false);
                var previewCollider = preview.GetComponent<Collider>();
                if (previewCollider != null) DestroyImmediate(previewCollider);
                var renderer = preview.GetComponent<Renderer>();
                var shader = Shader.Find("Hidden/Internal-Colored") ?? Shader.Find("Sprites/Default");
                if (renderer != null && shader != null)
                {
                    var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                    material.SetInt("_ZWrite", 0);
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                _previews.Add(preview);
            }
            UpdateColliders();
        }

        private void UpdateColliders()
        {
            if (_colliderBones == null || _colliders.Count != _colliderBones.Length) return;
            for (int i = 0; i < _colliders.Count; i++)
            {
                var setting = _settings.Segments[i];
                var bone = _colliderBones[i];
                var collider = _colliders[i];
                if (collider == null || bone == null) continue;
                var next = i + 1 < _colliderBones.Length ? _colliderBones[i + 1] : null;
                Vector3 endLocal = next != null ? bone.InverseTransformPoint(next.position) : Vector3.up * 0.01f;
                var tf = collider.transform;
                tf.localPosition = endLocal * 0.5f + new Vector3(setting.OffsetX, setting.OffsetY, setting.OffsetZ);
                tf.localRotation = Quaternion.FromToRotation(Vector3.up, endLocal.normalized) * Quaternion.Euler(setting.RotationX, setting.RotationY, setting.RotationZ);
                float scaleY = Mathf.Max(0.0001f, Mathf.Abs(tf.lossyScale.y));
                float scaleZ = Mathf.Max(0.0001f, Mathf.Abs(tf.lossyScale.z));
                collider.m_Radius = setting.Radius / scaleZ;
                collider.m_Height = setting.LengthMetres > 0f
                    ? setting.LengthMetres / scaleY
                    : ((next != null ? Vector3.Distance(bone.position, next.position) : setting.Radius) / scaleY + collider.m_Radius) * setting.LengthScale;
                collider.enabled = setting.Enabled;
                if (i < _previews.Count && _previews[i] != null)
                {
                    _previews[i].transform.localScale = new Vector3(collider.m_Radius * 2f, collider.m_Height * 0.5f, collider.m_Radius * 2f);
                }
            }
            UpdateColliderPreviewAppearance();
        }

        private void DestroyColliderPreviewMaterials()
        {
            foreach (GameObject preview in _previews)
            {
                if (preview == null) continue;
                Renderer renderer = preview.GetComponent<Renderer>();
                if (renderer != null && renderer.sharedMaterial != null)
                    Destroy(renderer.sharedMaterial);
            }
        }

        private void UpdateColliderPreviewAppearance()
        {
            bool editing = Application.isFocused &&
                (_showSettings || Time.unscaledTime - _lastColliderEditorDrawAt < 0.3f);
            for (int i = 0; i < _previews.Count; i++)
            {
                GameObject preview = _previews[i];
                if (preview == null || i >= _settings.Segments.Length) continue;
                bool selected = editing && i == _selectedSegment;
                bool enabled = _settings.Segments[i].Enabled;
                preview.SetActive(selected || (_settings.ShowColliders && enabled));
                Renderer renderer = preview.GetComponent<Renderer>();
                Material material = renderer != null ? renderer.sharedMaterial : null;
                if (material == null) continue;
                material.color = selected
                    ? (enabled ? new Color(1f, 0.9f, 0f, 0.85f) : new Color(1f, 0.35f, 0f, 0.85f))
                    : new Color(0f, 0.8f, 1f, 0.25f);
                material.renderQueue = selected ? 4000 : 3000;
                material.SetInt("_ZTest", selected
                    ? (int)UnityEngine.Rendering.CompareFunction.Always
                    : (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            }
        }

        private void RegisterColliders()
        {
            if (_female == null) return;
            var candidates = _female.GetComponentsInChildren<DynamicBone>(true);
            foreach (var dynamicBone in candidates)
            {
                if (dynamicBone == null || !dynamicBone.name.Contains("cf_J_Vagina") || dynamicBone.GetComponentInParent<ChaControl>() != _female || dynamicBone.m_Colliders == null) continue;
                foreach (var collider in _colliders)
                    if (collider != null && collider.enabled && !dynamicBone.m_Colliders.Contains(collider)) dynamicBone.m_Colliders.Add(collider);
                if (!_registeredBones.Contains(dynamicBone)) _registeredBones.Add(dynamicBone);
            }
        }

        private void UnregisterColliders()
        {
            foreach (var dynamicBone in _registeredBones)
            {
                if (dynamicBone == null || dynamicBone.m_Colliders == null) continue;
                foreach (var collider in _colliders) dynamicBone.m_Colliders.Remove(collider);
            }
            _registeredBones.Clear();
        }

        private void RelaxBones()
        {
            if (_bones == null) return;
            float t = 1f - Mathf.Exp(-_settings.BendSpeed * Time.deltaTime);
            for (int i = 0; i < _bones.Length; i++)
                if (_bones[i] != null) _bones[i].localRotation = Quaternion.Slerp(_bones[i].localRotation, _rest[i], t);
        }

        private void ApplyManualFk()
        {
            float smoothing = 1f - Mathf.Exp(-_settings.BendSpeed * Time.deltaTime);
            for (int i = 1; i < BoneCount - 1; i++)
            {
                var segment = _settings.Segments[i];
                var target = _rest[i] * Quaternion.Euler(segment.FkX, 0f, segment.FkZ);
                _bones[i].localRotation = Quaternion.Slerp(_bones[i].localRotation, target, smoothing);
            }
        }

        private void BendToward(Vector3 entry, Vector3 deep)
        {
            Vector3 inside = deep - entry;
            if (inside.sqrMagnitude < 0.000001f) return;
            inside.Normalize();
            Quaternion[] previous = new Quaternion[_bones.Length];
            for (int i = 0; i < _bones.Length; i++) { previous[i] = _bones[i].localRotation; _bones[i].localRotation = _rest[i]; }
            Vector3 rawTip = _bones[BoneCount - 1].position;
            float depth = Mathf.Clamp(Vector3.Dot(rawTip - entry, inside), 0f, Vector3.Distance(entry, deep));
            Vector3 goal = entry + inside * depth;
            if (depth > 0.005f)
            {
                // Choose the joint whose distance from the tip matches the depth of insertion.
                // Upstream joints place it at the entry; downstream joints aim the tip deeper.
                int entryJoint = BoneCount - 2;
                float shaftLength = 0f;
                for (int i = BoneCount - 2; i >= 2; i--)
                {
                    shaftLength += Vector3.Distance(_bones[i].position, _bones[i + 1].position);
                    entryJoint = i;
                    if (shaftLength >= depth) break;
                }
                SolveJoint(entryJoint, 1, entry);
                SolveJoint(BoneCount - 1, entryJoint, goal);
            }
            else SolveJoint(BoneCount - 1, 1, entry);
            float smoothing = 1f - Mathf.Exp(-_settings.BendSpeed * Time.deltaTime);
            for (int i = 0; i < _bones.Length; i++)
            {
                Quaternion desired = Quaternion.Slerp(_rest[i], _bones[i].localRotation, _settings.BendStrength);
                _bones[i].localRotation = Quaternion.Slerp(previous[i], desired, smoothing);
            }
        }

        private void SolveJoint(int joint, int firstPivot, Vector3 target)
        {
            for (int iteration = 0; iteration < _settings.SolverIterations; iteration++)
                for (int i = joint - 1; i >= firstPivot; i--)
                {
                    Vector3 from = _bones[joint].position - _bones[i].position;
                    Vector3 to = target - _bones[i].position;
                    if (from.sqrMagnitude < 0.000001f || to.sqrMagnitude < 0.000001f) continue;
                    _bones[i].rotation = Quaternion.FromToRotation(from, to) * _bones[i].rotation;
                    _bones[i].localRotation = Quaternion.RotateTowards(_rest[i], _bones[i].localRotation, _settings.MaxAngle);
                }
        }

        private void UpdateGrab()
        {
            if (!_settings.EnableVrGrab) { ReleaseGrab(); return; }
            if (!VR.Active || VR.Mode == null) { ReleaseGrab(); return; }
            if (_grabController != null)
            {
                if (_grabController.Input == null || _grabLock == null || !_grabLock.IsValid ||
                    !Bindings.VrHeld("MainGameVrDildo.grab", _grabController.Input, "Grip")) { ReleaseGrab(); return; }
                Transform hand = _grabController.transform;
                _model.transform.rotation = hand.rotation * _grabRotationOffset;
                _model.transform.position = hand.position + hand.rotation * _grabOffset;
                return;
            }
            TryBeginGrab(VR.Mode.Left);
            if (_grabController == null) TryBeginGrab(VR.Mode.Right);
        }

        private void TryBeginGrab(Controller controller)
        {
            if (controller == null || controller.Input == null || !Bindings.VrDown("MainGameVrDildo.grab", controller.Input, "Grip")) return;
            Transform hand = controller.transform;
            Vector3 grip = _model.transform.TransformPoint(new Vector3(_settings.GripOffsetX, _settings.GripOffsetY, _settings.GripOffsetZ));
            float distance = Vector3.Distance(hand.position, grip);
            if (distance > _settings.GripRadius)
            {
                LogDiagnostic("VR Vibe grip miss: " + controller.name + " distance=" + distance.ToString("F3") +
                               " allowed=" + _settings.GripRadius.ToString("F3"));
                return;
            }
            if (!controller.TryAcquireFocus(out Controller.Lock focus) || focus == null || !focus.IsValid)
            {
                Logger.LogWarning("VR Vibe grip rejected: focus unavailable on " + controller.name);
                return;
            }
            _grabController = controller;
            _grabLock = focus;
            // Preserve the item's orientation at pickup. The configured rotation is an
            // optional adjustment; zero means the shaft does not snap to the controller.
            _grabRotationOffset = Quaternion.Inverse(hand.rotation) * _model.transform.rotation *
                                  Quaternion.Euler(_settings.GripRotationX, _settings.GripRotationY, _settings.GripRotationZ);
            Vector3 localGrip = new Vector3(_settings.GripOffsetX, _settings.GripOffsetY, _settings.GripOffsetZ);
            _grabOffset = -(_grabRotationOffset * Vector3.Scale(localGrip, _model.transform.localScale));
            LogDiagnostic("VR Vibe grip start: " + controller.name + " distance=" + Vector3.Distance(hand.position, grip).ToString("F3"));
        }

        private void ReleaseGrab()
        {
            if (_grabController != null) LogDiagnostic("VR Vibe grip released: " + _grabController.name);
            _grabController = null;
            if (_grabLock != null && _grabLock.IsValid) _grabLock.SafeRelease();
            _grabLock = Controller.Lock.Invalid;
        }

        private void LoadSettings()
        {
            _settings = DildoSettings.CreateDefault();
            try
            {
                if (File.Exists(_settingsPath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(_settingsPath), _settings);
                _settings.Normalize();
            }
            catch (Exception ex) { Logger.LogWarning("Settings load failed: " + ex.Message); _settings = DildoSettings.CreateDefault(); }
        }

        private void SaveSettings()
        {
            try
            {
                _settings.Normalize();
                File.WriteAllText(_settingsPath, JsonUtility.ToJson(_settings, true));
                Logger.LogInfo("Settings saved: " + _settingsPath);
            }
            catch (Exception ex) { Logger.LogError("Settings save failed: " + ex); }
        }
    }
}
