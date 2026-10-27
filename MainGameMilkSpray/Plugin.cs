using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MainGameMilkSpray
{
    [BepInPlugin(GUID, "MainGameMilkSpray", "1.3.8")]
    [BepInProcess("KoikatsuSunshine")]
    [DefaultExecutionOrder(12200)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.kks.maingame.milkspray";
        private MilkSettings _settings;
        private readonly LiquidSimulation _liquid = new LiquidSimulation(41729);
        private LiquidRenderer _renderer;
        private MilkPerformanceLog _performance;
        private bool _performanceErrorReported, _performancePaused;
        private bool _fireRequested;
        private float _retryAt;
        private HSceneProc _scene;
        private ChaControl _female, _male;
        private Transform _femaleHip, _maleHip, _nipL, _nipR, _nipBoneL, _nipBoneR;
        private float _lastDistance, _nearDistance, _farDistance, _lastFireAt;
        private bool _hasDistance, _approaching, _hasApproaching;
        private float _lastAnimationTime;
        private bool _hasAnimationTime;
        private ShotTiming _lastShotTiming;
        private int _motionHash;
        private float _nextFindAt;
        private bool _contextValid;
        private Transform _femaleBodyRoot, _maleBodyRoot;
        private readonly List<AnimatorClipInfo> _clips = new List<AnimatorClipInfo>(16);
        private AnimationClip _lastClip;
        private bool _loopClipAllowed;
        private static readonly FieldInfo FlagsField = AccessTools.Field(typeof(HSceneProc), "flags");
        private static readonly FieldInfo FemalesField = AccessTools.Field(typeof(HSceneProc), "lstFemale");
        private static readonly FieldInfo MaleField = AccessTools.Field(typeof(HSceneProc), "male");

        private void Awake()
        {
            _settings = new MilkSettings(Config);
            try { _performance = new MilkPerformanceLog(Path.Combine(Path.GetDirectoryName(Info.Location), "_logs")); }
            catch (Exception e) { Logger.LogWarning("Milk performance log unavailable: " + e.Message); }
            _settings.Enabled.SettingChanged += EnabledChanged;
            SceneManager.activeSceneChanged += SceneChanged;
            SceneManager.sceneUnloaded += SceneUnloaded;
        }

        private void DetectShot()
        {
            _contextValid = false;
            if (!_settings.Enabled.Value) { ClearStroke(); ClearLiquid(); return; }
            if (Time.unscaledTime >= _nextFindAt)
            {
                _nextFindAt = Time.unscaledTime + 0.5f;
                HSceneProc scene = FindObjectOfType<HSceneProc>();
                if (scene != _scene) { ResetScene(); _scene = scene; }
            }
            if (_scene == null) { ClearStroke(); ClearLiquid(); return; }
            HFlag flags = FlagsField?.GetValue(_scene) as HFlag;
            if (flags == null || flags.mode != HFlag.EMode.sonyu || flags.gaugeFemale < _settings.GaugeMin.Value || flags.gaugeFemale >= 100f)
            { ClearStroke(); ClearLiquid(); return; }
            if (!ResolveCharacters() || !ChestClothesOpen()) { ClearStroke(); ClearLiquid(); return; }
            Animator animator = _female.animBody;
            if (animator == null) { ClearStroke(); ClearLiquid(); return; }
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            animator.GetCurrentAnimatorClipInfo(0, _clips);
            AnimationClip clip = _clips.Count > 0 ? _clips[0].clip : null;
            if (clip != _lastClip)
            {
                _lastClip = clip;
                string name = clip != null ? clip.name : "";
                _loopClipAllowed = name.IndexOf("SLoop", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("WLoop", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            if (!_loopClipAllowed)
            { ClearStroke(); ClearLiquid(); return; }
            if (_motionHash != state.fullPathHash) { _motionHash = state.fullPathHash; ClearStroke(); ClearLiquid(); }
            if (_lastShotTiming != _settings.Timing.Value) { _lastShotTiming = _settings.Timing.Value; ClearStroke(); }
            _contextValid = true;
            if (IsPaused()) { ClearStroke(); return; }
            if (_settings.Timing.Value == ShotTiming.AnimationPhase)
            {
                TickAnimationPhase(state.normalizedTime);
                return;
            }
            if (_femaleHip == null || _maleHip == null) { _contextValid = false; ClearStroke(); ClearLiquid(); return; }
            float distance = Vector3.Distance(_femaleHip.position, _maleHip.position);
            if (!_hasDistance)
            {
                _lastDistance = _nearDistance = _farDistance = distance;
                _hasDistance = true;
                return;
            }
            float delta = distance - _lastDistance;
            bool approaching = delta < -0.00025f;
            bool receding = delta > 0.00025f;
            if (approaching)
            {
                if (!_approaching) _farDistance = _lastDistance;
                _nearDistance = distance;
                _approaching = true;
                _hasApproaching = true;
            }
            else if (receding && _hasApproaching && _approaching)
            {
                float stroke = _farDistance - _nearDistance;
                _approaching = false;
                if (stroke >= _settings.MinStroke.Value && Time.unscaledTime - _lastFireAt >= _settings.Interval.Value)
                {
                    Fire();
                    _lastFireAt = Time.unscaledTime;
                    if (_settings.Verbose.Value) Logger.LogInfo("milk stroke=" + stroke.ToString("F3") + " gauge=" + flags.gaugeFemale.ToString("F1"));
                }
            }
            _lastDistance = distance;
        }

        private void TickAnimationPhase(float normalizedTime)
        {
            if (!_hasAnimationTime)
            {
                _lastAnimationTime = normalizedTime;
                _hasAnimationTime = true;
                return;
            }
            float target = _settings.PhasePercent.Value * 0.01f;
            bool crossed;
            if (normalizedTime >= _lastAnimationTime)
                crossed = Mathf.FloorToInt(normalizedTime - target) > Mathf.FloorToInt(_lastAnimationTime - target);
            else
            {
                // Some animators reset normalizedTime to 0 at each loop instead of counting upward.
                float previousPhase = Mathf.Repeat(_lastAnimationTime, 1f);
                float currentPhase = Mathf.Repeat(normalizedTime, 1f);
                crossed = currentPhase < previousPhase && (target > previousPhase || target <= currentPhase);
            }
            _lastAnimationTime = normalizedTime;
            if (!crossed || Time.unscaledTime - _lastFireAt < _settings.Interval.Value) return;
            Fire();
            _lastFireAt = Time.unscaledTime;
            if (_settings.Verbose.Value) Logger.LogInfo("milk animation phase=" + _settings.PhasePercent.Value + "%");
        }

        private bool ResolveCharacters()
        {
            IList females = FemalesField?.GetValue(_scene) as IList;
            ChaControl female = females != null && females.Count > 0 ? females[0] as ChaControl : null;
            ChaControl male = MaleField?.GetValue(_scene) as ChaControl;
            if (female == null || male == null) return false;
            Transform froot = female.objBodyBone != null ? female.objBodyBone.transform : female.transform;
            Transform mroot = male.objBodyBone != null ? male.objBodyBone.transform : male.transform;
            if (female != _female || male != _male || froot != _femaleBodyRoot || mroot != _maleBodyRoot)
            {
                ClearStroke(); ClearLiquid();
                _female = female; _male = male;
                _femaleBodyRoot = froot; _maleBodyRoot = mroot;
                _femaleHip = Find(froot, "k_f_kokan_00", "a_n_kokan", "cf_j_hips");
                _maleHip = Find(mroot, "a_n_kokan", "cm_j_waist01", "cm_J_Hips");
                ClearStroke();
            }
            // These are the same reference objects used by ChaControl.ChangeAccessoryParent.
            // Refresh even if a body replacement keeps the root object.
            Transform left = female.GetReferenceTransform(ChaReference.RefObjKey.a_n_nip_L);
            Transform right = female.GetReferenceTransform(ChaReference.RefObjKey.a_n_nip_R);
            if (left != _nipL || right != _nipR)
            {
                ClearStroke(); ClearLiquid();
                _nipL = left; _nipR = right;
                _nipBoneL = FindAncestor(left, "cf_j_bnip02_L");
                _nipBoneR = FindAncestor(right, "cf_j_bnip02_R");
                if (_nipL == null || _nipR == null) Logger.LogWarning("Nipple accessory attachment point missing on selected character");
            }
            return _femaleHip != null && _maleHip != null && (_nipL != null || _nipR != null);
        }

        private bool ChestClothesOpen()
        {
            byte[] states = _female?.fileStatus?.clothesState;
            // Clothes slots: 0=top, 2=bra. 0 is worn; 1/2 are shifted and 3 is removed.
            return states != null && states.Length > 2
                && states[0] >= 1 && states[0] <= 3
                && states[2] >= 1 && states[2] <= 3;
        }

        private static Transform Find(Transform root, params string[] names)
        {
            if (root == null) return null;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            foreach (string name in names)
                foreach (Transform t in all)
                    if (t != null && string.Equals(t.name, name, StringComparison.Ordinal)) return t;
            return null;
        }

        private static Transform FindAncestor(Transform child, string name)
        {
            for (Transform t = child; t != null; t = t.parent)
                if (string.Equals(t.name, name, StringComparison.Ordinal)) return t;
            return null;
        }

        private void Fire() { _fireRequested = true; }

        // Follow the nipple bone directly; apply no size-dependent or fixed position offset.
        // Fall back to the game's accessory anchor when the bone is unavailable.
        private static EmitterPose Pose(Transform nipple, Transform tipBone)
        {
            Transform source = tipBone != null ? tipBone : nipple;
            return new EmitterPose(LiquidRenderer.V(source.position),
                LiquidRenderer.V(source.forward), LiquidRenderer.V(source.right));
        }

        private bool IsPaused() => Time.timeScale <= 0f ||
            (_female != null && _female.animBody != null && _female.animBody.speed <= 0f);

        private void EnabledChanged(object sender, EventArgs e)
        {
            if (!_settings.Enabled.Value) { ClearStroke(); ClearLiquid(); }
        }

        private void Update()
        {
            if (_settings == null || _performance == null) return;
            _performance.BeginFrame(Time.frameCount, _settings.PerformanceLog.Value);
            if (!_performanceErrorReported && _performance.Error != null)
            {
                _performanceErrorReported = true;
                Logger.LogWarning("Milk performance log stopped: " + _performance.Error);
            }
        }

        private void LateUpdate()
        {
            if (_settings == null) return;
            _performance?.BeginFrame(Time.frameCount, _settings.PerformanceLog.Value);
            _performancePaused = false;
            using (MilkPerformanceLog.Measure(_performance, MilkStage.Late))
            {
                try { LateUpdateCore(); }
                finally { _performance?.SetContext(_settings.Enabled.Value, _contextValid, _performancePaused); }
            }
        }

        private void LateUpdateCore()
        {
            using (MilkPerformanceLog.Measure(_performance, MilkStage.Detect)) DetectShot();
            if (!_contextValid) return;
            if (Time.unscaledTime < _retryAt) { _fireRequested = false; return; }
            try
            {
                if (_renderer == null)
                    using (MilkPerformanceLog.Measure(_performance, MilkStage.Init))
                        _renderer = new LiquidRenderer("MainGame", _performance);
                bool paused = IsPaused();
                _performancePaused = paused;
                EmitterPose left = _nipL != null ? Pose(_nipL, _nipBoneL) : default;
                EmitterPose right = _nipR != null ? Pose(_nipR, _nipBoneR) : default;
                if (_hasLeftPose && _nipL != null && (left.Position - _lastPoseL.Position).Length > .25f) ClearLiquid();
                if (_hasRightPose && _nipR != null && (right.Position - _lastPoseR.Position).Length > .25f) ClearLiquid();
                if (!paused)
                {
                    using (MilkPerformanceLog.Measure(_performance, MilkStage.Simulation))
                        _liquid.Advance(Time.deltaTime, left, right, _performance);
                    if (_fireRequested)
                    {
                        using (MilkPerformanceLog.Measure(_performance, MilkStage.Fire))
                        {
                            LiquidSpec spec = _settings.Snapshot();
                            _liquid.Fire(spec, left, right, _settings.Left.Value && _nipL != null, _settings.Right.Value && _nipR != null);
                        }
                    }
                }
                else _liquid.SetPoses(left, right);
                using (MilkPerformanceLog.Measure(_performance, MilkStage.Prepare)) _renderer.Render(_liquid);
                _lastPoseL = left; _lastPoseR = right;
                _hasLeftPose = _nipL != null; _hasRightPose = _nipR != null;
            }
            catch (Exception e)
            {
                ClearLiquid();
                _renderer?.Dispose(); _renderer = null;
                _retryAt = Time.unscaledTime + 5f;
                Logger.LogError("Milk liquid rendering: " + e);
            }
            finally { _fireRequested = false; }
        }
        private EmitterPose _lastPoseL, _lastPoseR;
        private bool _hasLeftPose, _hasRightPose;

        private void ClearLiquid()
        {
            _performance?.TouchFrame(Time.frameCount);
            using (MilkPerformanceLog.Measure(_performance, MilkStage.Clear))
            {
                _fireRequested = false; _hasLeftPose = _hasRightPose = false;
                _liquid.Clear(); _renderer?.Clear();
                _performance?.SetParticles(0, 0, 0);
            }
        }
        private void ClearStroke()
        {
            _hasDistance = _approaching = _hasApproaching = false;
            _hasAnimationTime = false;
            _fireRequested = false;
        }
        private void ResetScene()
        {
            ClearStroke(); ClearLiquid();
            _female = _male = null;
            _femaleHip = _maleHip = _nipL = _nipR = _nipBoneL = _nipBoneR = null;
            _femaleBodyRoot = _maleBodyRoot = null;
            _lastClip = null; _loopClipAllowed = false;
            _motionHash = 0;
        }
        private void SceneChanged(Scene previous, Scene current) => ResetScene();
        private void SceneUnloaded(Scene scene) => ResetScene();
        private void OnDisable() { ClearStroke(); ClearLiquid(); }
        private void OnDestroy()
        {
            if (_settings != null) _settings.Enabled.SettingChanged -= EnabledChanged;
            SceneManager.activeSceneChanged -= SceneChanged;
            SceneManager.sceneUnloaded -= SceneUnloaded;
            _renderer?.Dispose();
            _performance?.Dispose();
        }
    }
}
