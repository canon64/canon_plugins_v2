using System;
using MainGameTransformGizmo;
using MainGirlHipHijack.Runtime;
using RootMotion.FinalIK;
using UnityEngine;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private static bool IsRotationDrivenEffector(int idx)
        {
            return idx == BIK_LH || idx == BIK_RH || idx == BIK_LF || idx == BIK_RF || idx == BIK_BODY;
        }

        private static void EnforceNoScaleMode(TransformGizmo gizmo)
        {
            if (gizmo == null)
                return;

            if (gizmo.Mode == GizmoMode.Scale)
                gizmo.SetMode(GizmoMode.Move);
        }

        private static System.Action<GizmoMode> CreateNoScaleModeHandler(TransformGizmo gizmo)
        {
            return mode =>
            {
                if (mode == GizmoMode.Scale && gizmo != null)
                    gizmo.SetMode(GizmoMode.Move);
            };
        }

        private void SetBodyIK(int idx, bool on, bool saveSettings = true, string reason = "unknown")
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;

            bool prev = _bikWant[idx];
            bool runtimeAligned = on ? _bikEff[idx].Running : !_bikEff[idx].Running;
            if (prev == on && _settings.Enabled[idx] == on && runtimeAligned)
                return;

            if (_settings.DetailLogEnabled)
                LogInfo("setBodyIK reason=" + reason + " idx=" + idx + " " + prev + "->" + on);

            if (on)
            {
                _abandonedByPostureChange = false;
                _pendingAbandonByPostureChange = false;
                _pendingAbandonTrigger = null;
                _pendingAbandonRequestTime = 0f;
            }

            _bikWant[idx] = on;
            _settings.Enabled[idx] = on;
            if (on)
                DoEnableBodyIK(idx);
            else
                DoDisableBodyIK(idx, preserveProxy: true);

            if (saveSettings)
                SaveSettings();

            LogStateSnapshot("setBodyIK:" + reason);
        }

        private void SetAllBodyIK(bool on, bool saveSettings = true, string reason = null)
        {
            string applyReason = reason ?? (on ? "ui-all-on" : "ui-all-off");
            for (int i = 0; i < BIK_TOTAL; i++)
                SetBodyIK(i, on, saveSettings: false, reason: applyReason);

            if (saveSettings)
                SaveSettings();

            LogStateSnapshot(on ? "setAll-on" : "setAll-off");
        }

        private void CompleteResetToAnimationPose()
        {
            bool hadActive = false;
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_bikWant[i] || _bikEff[i].Running)
                {
                    hadActive = true;
                    break;
                }
            }

            StopPoseTransitionIfRunning();
            for (int i = 0; i < BIK_TOTAL; i++)
                ResetBodyIKPartToAnimationPose(i, stopPoseTransition: false, logSnapshot: false, applyImmediate: false);

            _abandonedByPostureChange = false;

            // Clear leaked bindings immediately so animation pose is restored in the same frame.
            ForceOffBindingsForDisabledBodyIK();
            RecomputeGizmoDraggingState();

            LogInfo("complete reset to animation pose bodyIKActiveBefore=" + hadActive + " keepSavedSettings=true");
            LogStateSnapshot("complete-reset", force: true);
        }

        private void ResetBodyIKPartToAnimationPose(
            int idx,
            bool stopPoseTransition = true,
            bool logSnapshot = true,
            bool applyImmediate = true)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;

            if (stopPoseTransition)
                StopPoseTransitionIfRunning();

            bool wasWanted = _bikWant[idx];
            bool wasRunning = _bikEff[idx].Running;

            // Runtimeのみリセットし、保存設定(_settings.Enabled)は保持する。
            _bikWant[idx] = false;

            if (wasRunning)
                DoDisableBodyIK(idx, silent: true, deferDragRecompute: true);

            if (_sliderDragging && _sliderDraggingIndex == idx)
                SetSliderDragging(false, idx, "part-reset");

            if (applyImmediate)
            {
                ForceOffBindingsForDisabledBodyIK();
                RecomputeGizmoDraggingState();
            }

            if (wasWanted || wasRunning)
                LogInfo("part reset to animation pose idx=" + idx + " label=" + BIK_Labels[idx] + " keepSavedSettings=true");

            if (logSnapshot)
                LogStateSnapshot("part-reset:" + BIK_Labels[idx], force: true);
        }

        private bool GetGizmoVisible(int idx)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return true;

            if (_settings == null || _settings.GizmoVisible == null || _settings.GizmoVisible.Length != BIK_TOTAL)
                return true;

            return _settings.GizmoVisible[idx];
        }

        private bool IsGizmoVisible(int idx)
        {
            return _settings != null && GetGizmoVisible(idx);
        }

        private void SetGizmoVisible(int idx, bool on, bool saveSettings = true)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;
            if (_settings == null || _settings.GizmoVisible == null || _settings.GizmoVisible.Length != BIK_TOTAL)
                return;
            if (_settings.GizmoVisible[idx] == on)
                return;

            _settings.GizmoVisible[idx] = on;
            if (_bikEff[idx].Running && _bikEff[idx].Gizmo != null)
                _bikEff[idx].Gizmo.SetVisible(IsGizmoVisible(idx));

            if (saveSettings)
                SaveSettings();
        }

        private void SetAllGizmoVisible(bool on)
        {
            if (_settings == null || _settings.GizmoVisible == null || _settings.GizmoVisible.Length != BIK_TOTAL)
                return;

            bool changed = false;
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_settings.GizmoVisible[i] == on)
                    continue;
                _settings.GizmoVisible[i] = on;
                changed = true;
            }

            if (!changed)
                return;

            ApplyGizmoVisibilityToRunning();
            SaveSettings();
        }

        private void ApplyGizmoVisibilityToRunning()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_bikEff[i].Running || _bikEff[i].Gizmo == null)
                    continue;
                _bikEff[i].Gizmo.SetVisible(IsGizmoVisible(i));
            }

            UpdateMaleHeadTargetGizmoVisibility();
            UpdateAllMaleControlGizmoVisibility();
        }

        private float GetGizmoSizeMultiplier()
        {
            if (_settings == null)
                return 0.2f;

            return Mathf.Clamp(
                _settings.GizmoSizeMultiplier,
                TransformGizmo.MinSizeMultiplier,
                TransformGizmo.MaxSizeMultiplier);
        }

        private void SetGizmoSizeMultiplier(float value)
        {
            if (_settings == null)
                return;

            float clamped = Mathf.Clamp(value, TransformGizmo.MinSizeMultiplier, TransformGizmo.MaxSizeMultiplier);
            if (Mathf.Abs(_settings.GizmoSizeMultiplier - clamped) <= 0.0001f)
                return;

            _settings.GizmoSizeMultiplier = clamped;
            ApplyGizmoSizeToRunning();
            SaveSettings();
        }

        private void ApplyGizmoSizeToRunning()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_bikEff[i].Running || _bikEff[i].Gizmo == null)
                    continue;
                ApplyConfiguredGizmoSize(_bikEff[i].Gizmo);
            }

            if (_maleSession.MaleHeadTargetGizmo != null)
                ApplyConfiguredGizmoSize(_maleSession.MaleHeadTargetGizmo);
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                MaleControlState state = GetMaleControlState(i);
                if (state == null || state.Gizmo == null)
                    continue;
                ApplyConfiguredGizmoSize(state.Gizmo);
            }
        }

        private float GetConfiguredVrMarkerSize()
        {
            if (_settings == null)
                return 0.04f;

            return Mathf.Clamp(_settings.VRMarkerSize, 0.01f, 0.1f);
        }

        private void ApplyVrMarkerSizeToAll()
        {
            float size = GetConfiguredVrMarkerSize();
            Vector3 scale = Vector3.one * size;
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_bikEff[i] == null || _bikEff[i].VRMarkerGo == null)
                    continue;

                _bikEff[i].VRMarkerGo.transform.localScale = scale;
            }
        }

        private void ApplyConfiguredGizmoSize(TransformGizmo gizmo)
        {
            if (gizmo == null)
                return;

            TransformGizmoApi.SetSizeMultiplier(gizmo, GetGizmoSizeMultiplier());
        }

        private float GetBodyIKWeight(int idx)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return 1f;
            return _bikWeight[idx];
        }

        private float GetConfiguredBodyIKWeight(int idx)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return 1f;
            if (_settings == null || _settings.Weights == null || idx >= _settings.Weights.Length)
                return GetBodyIKWeight(idx);
            return Mathf.Clamp01(_settings.Weights[idx]);
        }

        private void SetBodyIKWeight(int idx, float weight, bool saveSettings = true, bool updateSettings = true)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;

            float clamped = Mathf.Clamp01(weight);
            if (Mathf.Approximately(_bikWeight[idx], clamped))
                return;

            _bikWeight[idx] = clamped;
            if (updateSettings && _settings != null && _settings.Weights != null && idx < _settings.Weights.Length)
                _settings.Weights[idx] = clamped;
            ApplyBodyIKWeight(idx);
            if (saveSettings && updateSettings)
                SaveSettings();
        }

        private void DoEnableBodyIK(int idx)
        {
            if (_bikEff[idx].Running)
                return;
            if (_femaleSession.FullBodyIk == null)
                return;

            bool reuseProxy = _bikEff[idx].ProxyGo != null;

            if (_bikEff[idx].IsBendGoal)
            {
                IKConstraintBend bc = BIK_GetBendConstraint(idx);
                if (bc == null)
                    return;
                CaptureOriginalBendGoalIfNeeded(idx, bc);
                if (!reuseProxy)
                {
                    float radius = 0.15f;
                    if (bc.bone1 != null && bc.bone2 != null)
                        radius = Vector3.Distance(bc.bone1.position, bc.bone2.position);
                    _bikEff[idx].BendGoalRadius = Mathf.Clamp(radius, 0.05f, 0.6f);
                }
            }
            else
            {
                IKEffector eff = BIK_GetEffector(idx);
                if (eff == null)
                    return;
                CaptureOriginalEffectorTargetIfNeeded(idx, eff);
                if (!reuseProxy)
                    _bikEff[idx].BendGoalRadius = 0f;
            }

            if (!reuseProxy)
            {
                Vector3 initPos;
                Quaternion initRot;
                if (_bikEff[idx].IsBendGoal)
                {
                    IKConstraintBend bc = BIK_GetBendConstraint(idx);
                    initPos = bc.bone2 != null ? bc.bone2.position : Vector3.zero;
                    initRot = bc.bone2 != null ? bc.bone2.rotation : Quaternion.identity;
                }
                else
                {
                    IKEffector eff = BIK_GetEffector(idx);
                    initPos = eff.bone != null ? eff.bone.position : Vector3.zero;
                    initRot = eff.bone != null ? eff.bone.rotation : Quaternion.identity;
                }

                GameObject go = new GameObject("__BodyIkGizmoProxy_" + idx);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.transform.SetPositionAndRotation(initPos, initRot);
                _bikEff[idx].ProxyGo = go;
                _bikEff[idx].Proxy = go.transform;
                _bikEff[idx].GizmoDragging = false;
                _bikEff[idx].GizmoDragHandler = null;
                _bikEff[idx].GizmoModeHandler = null;
                _bikEff[idx].FollowBone = null;
                _bikEff[idx].FollowBonePositionOffset = Vector3.zero;
                _bikEff[idx].FollowBoneRotationOffset = Quaternion.identity;
                _bikEff[idx].HasPostDragHold = false;
                _bikEff[idx].PostDragHoldFrames = 0;

                // VRスフィアマーカー生成（デフォルト非表示、VRGrabMode ONで表示）
                var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Destroy(sphere.GetComponent<Collider>());
                sphere.name = "__VRMarker_" + idx;
                sphere.hideFlags = HideFlags.HideAndDontSave;
                sphere.transform.SetParent(go.transform, false);
                sphere.transform.localPosition = Vector3.zero;
                sphere.transform.localScale    = Vector3.one * GetConfiguredVrMarkerSize();
                _bikEff[idx].VRMarkerGo   = sphere;
                _bikEff[idx].VRMarkerRend = sphere.GetComponent<Renderer>();
                if (_bikEff[idx].VRMarkerRend != null)
                {
                    var mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                    mat.color = new Color(0f, 1f, 1f);
                    mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    _bikEff[idx].VRMarkerRend.sharedMaterial = mat;
                }
                sphere.SetActive(false);
            }

            // IKエフェクターにプロキシを再アタッチ
            if (_bikEff[idx].IsBendGoal)
            {
                IKConstraintBend bc = BIK_GetBendConstraint(idx);
                if (bc == null)
                {
                    if (!reuseProxy) Destroy(_bikEff[idx].ProxyGo);
                    _bikEff[idx].ProxyGo = null;
                    _bikEff[idx].Proxy = null;
                    return;
                }
                bc.bendGoal = _bikEff[idx].Proxy;
                bc.weight = GetBodyIKWeight(idx);
            }
            else
            {
                IKEffector eff = BIK_GetEffector(idx);
                if (eff == null)
                {
                    if (!reuseProxy) Destroy(_bikEff[idx].ProxyGo);
                    _bikEff[idx].ProxyGo = null;
                    _bikEff[idx].Proxy = null;
                    return;
                }
                eff.target = _bikEff[idx].Proxy;
                eff.positionWeight = GetBodyIKWeight(idx);
                if (IsRotationDrivenEffector(idx))
                    eff.rotationWeight = GetBodyIKWeight(idx);
            }

            _femaleSession.FullBodyIk.enabled = true;
            _bikEff[idx].Running = true;
            NotifyArmIkRunningChanged(idx, running: true);
            if (idx == BIK_LH || idx == BIK_RH)
                SyncShoulderStabilizerEnabledFromArmState("arm-ik-enabled:" + idx);
            _bikEff[idx].GizmoDragging = false;
            _bikEff[idx].HasPostDragHold = false;
            _bikEff[idx].PostDragHoldFrames = 0;

            // VR掴み用マーカーは実際に掴んでいる間だけ表示する。
            if (_vrGrabMode && _bikEff[idx].VRMarkerGo != null)
            {
                _bikEff[idx].VRMarkerGo.SetActive(false);
                if (_bikEff[idx].VRMarkerRend != null)
                    _bikEff[idx].VRMarkerRend.material.color = new Color(0f, 1f, 1f);
            }

            // ギズモのアタッチ（再利用時は再登録のみ）
            if (_bikEff[idx].Gizmo == null)
            {
                _bikEff[idx].Gizmo = TransformGizmoApi.Attach(_bikEff[idx].ProxyGo);
                if (_bikEff[idx].Gizmo != null)
                {
                    ApplyConfiguredGizmoSize(_bikEff[idx].Gizmo);
                    EnforceNoScaleMode(_bikEff[idx].Gizmo);
                    // BodyIKギズモは常時追従中だが、中央左クリックでモード循環(Move/Rotate)させる
                    _bikEff[idx].Gizmo.CycleModeOnFollowClick = true;
                    int capturedIdx = idx;
                    _bikEff[idx].GizmoDragHandler = dragging => OnBodyIkGizmoDragStateChanged(capturedIdx, dragging);
                    _bikEff[idx].GizmoModeHandler = CreateNoScaleModeHandler(_bikEff[idx].Gizmo);
                    _bikEff[idx].Gizmo.DragStateChanged += _bikEff[idx].GizmoDragHandler;
                    _bikEff[idx].Gizmo.ModeChanged += _bikEff[idx].GizmoModeHandler;
                }
            }
            else
            {
                if (_bikEff[idx].GizmoDragHandler != null)
                    _bikEff[idx].Gizmo.DragStateChanged += _bikEff[idx].GizmoDragHandler;
                if (_bikEff[idx].GizmoModeHandler != null)
                    _bikEff[idx].Gizmo.ModeChanged += _bikEff[idx].GizmoModeHandler;
                EnforceNoScaleMode(_bikEff[idx].Gizmo);
            }

            if (_bikEff[idx].Gizmo != null)
                _bikEff[idx].Gizmo.SetVisible(IsGizmoVisible(idx));

            LogInfo("bodyIK enabled: " + BIK_Labels[idx] + (reuseProxy ? " (proxy reused)" : ""));
        }

        // preserveProxy=true: IKウェイトを0にするだけで座標・ギズモは保持（チェックボックスOFF用）
        // preserveProxy=false: プロキシとギズモを完全破棄（「戻す」ボタン・体位変更用）
        private void DoDisableBodyIK(int idx, bool silent = false, bool deferDragRecompute = false, bool preserveProxy = false)
        {
            if (!_bikEff[idx].Running)
                return;

            if (!silent && _settings != null && _settings.DetailLogEnabled)
                LogInfo("bodyIK disable req: " + BIK_Labels[idx] + " state=" + BuildSolverBindingStateText(idx));

            if (_bikEff[idx].Gizmo != null && _bikEff[idx].GizmoDragHandler != null)
                _bikEff[idx].Gizmo.DragStateChanged -= _bikEff[idx].GizmoDragHandler;
            if (_bikEff[idx].Gizmo != null && _bikEff[idx].GizmoModeHandler != null)
                _bikEff[idx].Gizmo.ModeChanged -= _bikEff[idx].GizmoModeHandler;

            if (_bikEff[idx].GizmoDragging)
            {
                _bikEff[idx].GizmoDragging = false;
                if (!deferDragRecompute)
                    RecomputeGizmoDraggingState();
            }
            _bikEff[idx].HasPostDragHold = false;
            _bikEff[idx].PostDragHoldFrames = 0;

            if (_femaleSession.FullBodyIk != null)
            {
                if (_bikEff[idx].IsBendGoal)
                {
                    IKConstraintBend bc = BIK_GetBendConstraint(idx);
                    if (bc != null)
                    {
                        Transform restore = GetRestoreBendGoalTarget(idx, bc);
                        if (!ReferenceEquals(bc.bendGoal, restore))
                            bc.bendGoal = restore;
                        bc.weight = 0f;
                    }
                }
                else
                {
                    IKEffector eff = BIK_GetEffector(idx);
                    if (eff != null)
                    {
                        eff.positionWeight = 0f;
                        if (IsRotationDrivenEffector(idx))
                            eff.rotationWeight = 0f;
                        Transform restore = GetRestoreEffectorTarget(idx, eff);
                        if (!ReferenceEquals(eff.target, restore))
                            eff.target = restore;
                    }
                }
            }

            if (preserveProxy)
            {
                // 座標・ギズモを保持：ギズモを非表示にするだけ
                if (_bikEff[idx].Gizmo != null)
                    _bikEff[idx].Gizmo.SetVisible(false);
                _bikEff[idx].GizmoDragging = false;
                // GizmoDragHandlerは保持して再ONで再登録できるようにする
                if (_bikEff[idx].VRMarkerGo != null)
                    _bikEff[idx].VRMarkerGo.SetActive(false);
            }
            else
            {
                // 完全破棄
                DestroyFollowVisuals(idx);
                _bikEff[idx].FollowBone = null;
                _bikEff[idx].FollowBonePositionOffset = Vector3.zero;
                _bikEff[idx].FollowBoneRotationOffset = Quaternion.identity;

                // VRMarkerGoはProxyGoの子なのでProxyGo破棄で自動消滅するが参照はクリア
                _bikEff[idx].VRMarkerGo   = null;
                _bikEff[idx].VRMarkerRend = null;

                if (_bikEff[idx].ProxyGo != null)
                    Destroy(_bikEff[idx].ProxyGo);

                _bikEff[idx].ProxyGo = null;
                _bikEff[idx].Proxy = null;
                _bikEff[idx].Gizmo = null;
                _bikEff[idx].GizmoDragHandler = null;
                _bikEff[idx].GizmoModeHandler = null;
                _bikEff[idx].GizmoDragging = false;
            }

            _bikEff[idx].Running = false;
            NotifyArmIkRunningChanged(idx, running: false);
            if (idx == BIK_LH || idx == BIK_RH)
                SyncShoulderStabilizerEnabledFromArmState("arm-ik-disabled:" + idx);
            if (!silent)
            {
                LogInfo("bodyIK disabled: " + BIK_Labels[idx] + (preserveProxy ? " (proxy preserved)" : ""));
                if (_settings != null && _settings.DetailLogEnabled)
                    LogInfo("bodyIK disabled post: " + BIK_Labels[idx] + " state=" + BuildSolverBindingStateText(idx));
            }
        }

        private void DisableAllBodyIK(bool silent = false)
        {
            for (int i = 0; i < BIK_TOTAL; i++)
                DoDisableBodyIK(i, silent, deferDragRecompute: true);
            RecomputeGizmoDraggingState();
        }

        private void RequestAbandonAllBodyIKByPostureChange(string trigger)
        {
            if (string.IsNullOrEmpty(trigger))
                trigger = "unknown";

            if (_pendingAbandonByPostureChange)
            {
                _pendingAbandonTrigger = trigger;
                return;
            }

            _pendingAbandonByPostureChange = true;
            _pendingAbandonTrigger = trigger;
            _pendingAbandonRequestTime = Time.unscaledTime;
            _abandonedByPostureChange = true;

            if (_settings != null && _settings.DetailLogEnabled)
                LogInfo("abandon request bodyIK trigger=" + trigger);
        }

        private void TryFlushPendingAbandonByPostureChange(string flushSource, bool force)
        {
            if (!_pendingAbandonByPostureChange)
                return;

            if (!force)
            {
                float elapsed = Time.unscaledTime - _pendingAbandonRequestTime;
                if (elapsed < PendingAbandonFallbackDelaySeconds)
                    return;
            }

            string trigger = string.IsNullOrEmpty(_pendingAbandonTrigger) ? "unknown" : _pendingAbandonTrigger;
            _pendingAbandonByPostureChange = false;
            _pendingAbandonTrigger = null;
            _pendingAbandonRequestTime = 0f;

            bool motionOnlyChange = IsMotionOnlyChangeTrigger(trigger);
            bool keepHip = motionOnlyChange
                && (_bikWant[BIK_BODY]
                    || (_bikEff[BIK_BODY] != null && _bikEff[BIK_BODY].Running)
                    || (_settings != null && _settings.Enabled != null && _settings.Enabled.Length > BIK_BODY && _settings.Enabled[BIK_BODY]));

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (keepHip && i == BIK_BODY)
                    continue;
                DoDisableBodyIK(i, silent: true, deferDragRecompute: true);
            }
            RecomputeGizmoDraggingState();

            bool changed = false;
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (keepHip && i == BIK_BODY)
                {
                    if (!_bikWant[i])
                    {
                        _bikWant[i] = true;
                        changed = true;
                    }

                    if (_settings != null && _settings.Enabled != null && i < _settings.Enabled.Length && !_settings.Enabled[i])
                    {
                        _settings.Enabled[i] = true;
                        changed = true;
                    }

                    _nextOffLeakWarnTime[i] = 0f;
                    continue;
                }

                if (_bikWant[i])
                    changed = true;
                _bikWant[i] = false;

                if (_settings != null && _settings.Enabled != null && i < _settings.Enabled.Length && _settings.Enabled[i])
                    changed = true;
                if (_settings != null && _settings.Enabled != null && i < _settings.Enabled.Length)
                    _settings.Enabled[i] = false;

                _nextOffLeakWarnTime[i] = 0f;
            }

            _abandonedByPostureChange = !motionOnlyChange;

            if (changed)
                SaveSettings();
            LogInfo("abandon bodyIK trigger=" + trigger
                + " flush=" + flushSource
                + " keepHip=" + keepHip
                + " motionOnly=" + motionOnlyChange);
            LogStateSnapshot("abandon-posture", force: true);
        }

        private static bool IsMotionOnlyChangeTrigger(string trigger)
        {
            if (string.IsNullOrEmpty(trigger))
                return false;

            return trigger.StartsWith("motion strength changed", StringComparison.Ordinal)
                || string.Equals(trigger, "animator-state-changed", StringComparison.Ordinal);
        }

        private bool HasAnyBodyIKActiveState()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_bikWant[i])
                    return true;
                if (_bikEff[i] != null && _bikEff[i].Running)
                    return true;
                if (_settings != null && _settings.Enabled != null && i < _settings.Enabled.Length && _settings.Enabled[i])
                    return true;
            }
            return false;
        }

        private struct BodyIkDiagSnapshot
        {
            public int Index;
            public string Label;
            public bool Want;
            public bool Enabled;
            public bool Running;
            public bool GizmoDragging;
            public bool IsBend;
            public float ConfigWeight;
            public float SolverPosWeight;
            public float SolverRotWeight;
            public Transform Binding;
            public bool BindingIsOwnedProxy;
            public Transform Bone;
            public Transform Proxy;
            public Vector3 BonePos;
            public Vector3 ProxyPos;
            public float BoneToProxyDistance;
            public bool FollowActive;
            public Transform FollowBone;
            public Vector3 FollowBonePos;
            public Vector3 FollowTargetPos;
            public float FollowTargetToProxyDistance;
            public float BendAngleDeg;
            public float BendGoalErrorDeg;
            public float BendGoalDistance;
        }

        private bool ShouldLogBodyIkDiagnostics(out int idx)
        {
            idx = -1;
            if (_settings == null || !_settings.BodyIkDiagnosticLog)
            {
                _lastBodyIkDiagHasPost = false;
                return false;
            }

            float interval = Mathf.Clamp(_settings.BodyIkDiagnosticLogInterval, 0.05f, 2f);
            if (Time.unscaledTime < _nextBodyIkDiagLogTime)
                return false;

            _nextBodyIkDiagLogTime = Time.unscaledTime + interval;
            idx = ResolveBodyIkDiagnosticIndex();
            return idx >= 0;
        }

        private int ResolveBodyIkDiagnosticIndex()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                BodyIkOperationPointState state = _bikEff[i];
                if (state != null && state.Running && state.GizmoDragging)
                    return i;
            }

            if (_bikEff[BIK_BODY] != null && _bikEff[BIK_BODY].Running)
                return BIK_BODY;

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                BodyIkOperationPointState state = _bikEff[i];
                if (state != null && state.Running)
                    return i;
            }

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_bikWant[i])
                    return i;
            }

            return -1;
        }

        private bool TryCaptureBodyIkDiagSnapshot(int idx, out BodyIkDiagSnapshot snap)
        {
            snap = default(BodyIkDiagSnapshot);
            if (idx < 0 || idx >= BIK_TOTAL)
                return false;

            BodyIkOperationPointState state = _bikEff[idx];
            snap.Index = idx;
            snap.Label = BIK_Labels[idx];
            snap.Want = _bikWant[idx];
            snap.Enabled = _settings != null
                && _settings.Enabled != null
                && idx < _settings.Enabled.Length
                && _settings.Enabled[idx];
            snap.Running = state != null && state.Running;
            snap.GizmoDragging = state != null && state.GizmoDragging;
            snap.ConfigWeight = GetBodyIKWeight(idx);
            snap.IsBend = idx >= BIK_BEND_START && idx != BIK_BODY;
            snap.BendAngleDeg = -1f;
            snap.BendGoalErrorDeg = -1f;
            snap.BendGoalDistance = -1f;
            snap.FollowTargetToProxyDistance = -1f;

            if (state != null)
            {
                snap.Proxy = state.Proxy;
                if (snap.Proxy != null)
                    snap.ProxyPos = snap.Proxy.position;

                if (state.FollowBone != null)
                {
                    snap.FollowActive = true;
                    snap.FollowBone = state.FollowBone;
                    snap.FollowBonePos = state.FollowBone.position;

                    Quaternion offsetRot = GetFollowOffsetRotation(state.FollowBone);
                    snap.FollowTargetPos = state.FollowBone.position
                        + (offsetRot * state.FollowBonePositionOffset);
                    if (snap.Proxy != null)
                        snap.FollowTargetToProxyDistance = Vector3.Distance(snap.FollowTargetPos, snap.Proxy.position);
                }
            }

            if (snap.IsBend)
            {
                IKConstraintBend bc = BIK_GetBendConstraint(idx);
                if (bc != null)
                {
                    snap.Binding = bc.bendGoal;
                    snap.SolverPosWeight = bc.weight;
                    snap.SolverRotWeight = -1f;
                    snap.Bone = bc.bone2;

                    if (bc.bone1 != null && bc.bone2 != null && bc.bone3 != null)
                    {
                        Vector3 upper = bc.bone1.position - bc.bone2.position;
                        Vector3 lower = bc.bone3.position - bc.bone2.position;
                        if (upper.sqrMagnitude > 0.00000001f && lower.sqrMagnitude > 0.00000001f)
                            snap.BendAngleDeg = Vector3.Angle(upper, lower);
                    }

                    Transform bendGoalRef = snap.Proxy != null ? snap.Proxy : bc.bendGoal;
                    if (bc.bone2 != null && bc.bone3 != null && bendGoalRef != null)
                    {
                        Vector3 currentDir = bc.bone3.position - bc.bone2.position;
                        Vector3 goalDir = bendGoalRef.position - bc.bone2.position;
                        snap.BendGoalDistance = goalDir.magnitude;
                        if (currentDir.sqrMagnitude > 0.00000001f && goalDir.sqrMagnitude > 0.00000001f)
                            snap.BendGoalErrorDeg = Vector3.Angle(currentDir, goalDir);
                    }
                }
            }
            else
            {
                IKEffector eff = BIK_GetEffector(idx);
                if (eff != null)
                {
                    snap.Binding = eff.target;
                    snap.SolverPosWeight = eff.positionWeight;
                    snap.SolverRotWeight = IsRotationDrivenEffector(idx) ? eff.rotationWeight : -1f;
                    snap.Bone = eff.bone;
                }
            }

            snap.BindingIsOwnedProxy = IsOwnedProxyTransform(snap.Binding);
            if (snap.Bone != null)
                snap.BonePos = snap.Bone.position;
            snap.BoneToProxyDistance = (snap.Bone != null && snap.Proxy != null)
                ? Vector3.Distance(snap.Bone.position, snap.Proxy.position)
                : -1f;
            return true;
        }

        private void LogBodyIkDiagnostics(BodyIkDiagSnapshot before, BodyIkDiagSnapshot after, bool skippedByAbandon)
        {
            bool hasCarry = _lastBodyIkDiagHasPost
                && _lastBodyIkDiagIndex == before.Index
                && _lastBodyIkDiagFrame == Time.frameCount - 1;
            float carryBone = hasCarry ? Vector3.Distance(_lastBodyIkDiagPostBonePos, before.BonePos) : -1f;
            float carryProxy = hasCarry ? Vector3.Distance(_lastBodyIkDiagPostProxyPos, before.ProxyPos) : -1f;

            string verdict = BuildBodyIkDiagVerdict(before, after, skippedByAbandon, hasCarry, carryBone, carryProxy);
            string bindingBefore = before.Binding != null ? before.Binding.name : "null";
            string bindingAfter = after.Binding != null ? after.Binding.name : "null";
            string followBoneBefore = before.FollowBone != null ? before.FollowBone.name : "null";
            string followBoneAfter = after.FollowBone != null ? after.FollowBone.name : "null";

            string msg = "[BodyIK-DIAG] frame=" + Time.frameCount
                + " idx=" + before.Index + "(" + before.Label + ")"
                + " verdict=" + verdict
                + " abandoned=" + _abandonedByPostureChange
                + " pendingAbandon=" + _pendingAbandonByPostureChange
                + " want=" + before.Want
                + " enabled=" + before.Enabled
                + " running=" + before.Running
                + " drag=" + before.GizmoDragging
                + " cfgW=" + before.ConfigWeight.ToString("F3")
                + " solverW(before->after)=" + before.SolverPosWeight.ToString("F3") + "->" + after.SolverPosWeight.ToString("F3")
                + " bind(before->after)=" + bindingBefore + "->" + bindingAfter
                + " bindOwned(before->after)=" + before.BindingIsOwnedProxy + "->" + after.BindingIsOwnedProxy
                + " bone(before->after)=" + Vec3(before.BonePos) + "->" + Vec3(after.BonePos)
                + " proxy(before->after)=" + Vec3(before.ProxyPos) + "->" + Vec3(after.ProxyPos)
                + " dist(before->after)=" + before.BoneToProxyDistance.ToString("F4") + "->" + after.BoneToProxyDistance.ToString("F4")
                + " followActive(before->after)=" + before.FollowActive + "->" + after.FollowActive
                + " followBone(before->after)=" + followBoneBefore + "->" + followBoneAfter
                + " followBonePos(before->after)=" + Vec3(before.FollowBonePos) + "->" + Vec3(after.FollowBonePos)
                + " followTarget(before->after)=" + Vec3(before.FollowTargetPos) + "->" + Vec3(after.FollowTargetPos)
                + " followTargetDist(before->after)=" + before.FollowTargetToProxyDistance.ToString("F4") + "->" + after.FollowTargetToProxyDistance.ToString("F4")
                + " bendAngleDeg(before->after)=" + before.BendAngleDeg.ToString("F2") + "->" + after.BendAngleDeg.ToString("F2")
                + " bendGoalErrorDeg(before->after)=" + before.BendGoalErrorDeg.ToString("F2") + "->" + after.BendGoalErrorDeg.ToString("F2")
                + " bendGoalDist(before->after)=" + before.BendGoalDistance.ToString("F4") + "->" + after.BendGoalDistance.ToString("F4")
                + " carry(bone/proxy)=" + carryBone.ToString("F4") + "/" + carryProxy.ToString("F4")
                + " shoulderLink={" + BuildShoulderLinkDiagStateText() + "}";
            LogBodyIkDiag(msg);

            if (after.Bone != null && after.Proxy != null)
            {
                _lastBodyIkDiagHasPost = true;
                _lastBodyIkDiagFrame = Time.frameCount;
                _lastBodyIkDiagIndex = after.Index;
                _lastBodyIkDiagPostBonePos = after.BonePos;
                _lastBodyIkDiagPostProxyPos = after.ProxyPos;
            }
            else
            {
                _lastBodyIkDiagHasPost = false;
            }
        }

        private void LogBodyIkBendDiagnostics()
        {
            LogBodyIkBendDiagnosticFor(BIK_LE);
            LogBodyIkBendDiagnosticFor(BIK_RE);
            LogBodyIkBendDiagnosticFor(BIK_LK);
            LogBodyIkBendDiagnosticFor(BIK_RK);
        }

        private void LogBodyIkBendDiagnosticFor(int idx)
        {
            BodyIkDiagSnapshot snap;
            if (!TryCaptureBodyIkDiagSnapshot(idx, out snap) || !snap.IsBend)
                return;

            string binding = snap.Binding != null ? snap.Binding.name : "null";
            string followBone = snap.FollowBone != null ? snap.FollowBone.name : "null";
            string msg = "[BodyIK-BEND-DIAG] frame=" + Time.frameCount
                + " idx=" + snap.Index + "(" + snap.Label + ")"
                + " want=" + snap.Want
                + " enabled=" + snap.Enabled
                + " running=" + snap.Running
                + " drag=" + snap.GizmoDragging
                + " bind=" + binding
                + " bindOwned=" + snap.BindingIsOwnedProxy
                + " bonePos=" + Vec3(snap.BonePos)
                + " proxyPos=" + Vec3(snap.ProxyPos)
                + " boneToProxyDist=" + snap.BoneToProxyDistance.ToString("F4")
                + " bendAngleDeg=" + snap.BendAngleDeg.ToString("F2")
                + " bendGoalErrorDeg=" + snap.BendGoalErrorDeg.ToString("F2")
                + " bendGoalDist=" + snap.BendGoalDistance.ToString("F4")
                + " followActive=" + snap.FollowActive
                + " followBone=" + followBone
                + " followBonePos=" + Vec3(snap.FollowBonePos)
                + " followTargetPos=" + Vec3(snap.FollowTargetPos)
                + " followTargetDist=" + snap.FollowTargetToProxyDistance.ToString("F4")
                + " shoulderLink={" + BuildShoulderLinkDiagStateText() + "}";
            LogBodyIkDiag(msg);
        }

        private static string BuildBodyIkDiagVerdict(
            BodyIkDiagSnapshot before,
            BodyIkDiagSnapshot after,
            bool skippedByAbandon,
            bool hasCarry,
            float carryBone,
            float carryProxy)
        {
            if (skippedByAbandon)
                return "skip:abandoned";
            if (!before.Want && !before.Running)
                return "skip:inactive";
            if (!before.Enabled)
                return "skip:enabled-off";
            if (before.ConfigWeight <= 0.0001f || after.SolverPosWeight <= 0.0001f)
                return "skip:weight-zero";
            if (!after.BindingIsOwnedProxy)
                return "warn:binding-not-proxy";
            if (hasCarry && carryProxy <= 0.001f && carryBone >= 0.01f)
                return "suspect:overwritten-between-frames";
            return "apply:path-active";
        }

        private void ResetBodyIkDiagnosticsState()
        {
            _bodyIkDiagnosticState.Reset();
        }

        private void OnAfterHSceneLateUpdateBodyIK()
        {
            if (_femaleSession.FullBodyIk == null)
                return;

            int diagIdx;
            bool doDiag = ShouldLogBodyIkDiagnostics(out diagIdx);
            BodyIkDiagSnapshot diagBefore = default(BodyIkDiagSnapshot);
            if (doDiag && !TryCaptureBodyIkDiagSnapshot(diagIdx, out diagBefore))
                doDiag = false;

            if (_abandonedByPostureChange)
            {
                if (doDiag)
                {
                    LogBodyIkDiagnostics(diagBefore, diagBefore, skippedByAbandon: true);
                    LogBodyIkBendDiagnostics();
                }
                return;
            }

            UpdateFollowBones();
            UpdateFollowBoneVisuals();
            EnsureOnBindingsForRunningBodyIK();
            ApplyPostDragHoldToBodyIK();

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_bikEff[i].Running)
                    continue;

                ApplyBodyIKWeight(i);
                if (_bikEff[i].IsBendGoal && _bikEff[i].Proxy != null)
                    SetBendGoalProxyByDirection(i, _bikEff[i].Proxy.position);
                if (_bikEff[i].Gizmo != null)
                    _bikEff[i].Gizmo.SetVisible(IsGizmoVisible(i));
            }

            ForceOffBindingsForDisabledBodyIK();
            CacheLateUpdateBonePositions();
            FlushPendingFollowRebinds();
            CacheTransitionFollowBonePositions();
            ApplyConfiguredFemaleHandPoses();

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_bikWant[i] || _bikEff[i].Running)
                    continue;
                WarnIfOffBindingStillActive(i);
            }

            if (doDiag)
            {
                BodyIkDiagSnapshot diagAfter;
                if (TryCaptureBodyIkDiagSnapshot(diagIdx, out diagAfter))
                    LogBodyIkDiagnostics(diagBefore, diagAfter, skippedByAbandon: false);
                LogBodyIkBendDiagnostics();
            }
        }

        private void CacheLateUpdateBonePositions()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_bikEff[i].Running)
                {
                    _bikEff[i].HasLateUpdateBoneCache = false;
                    continue;
                }

                Transform bone = null;
                if (_bikEff[i].IsBendGoal)
                {
                    IKConstraintBend bc = BIK_GetBendConstraint(i);
                    if (bc != null) bone = bc.bone2;
                }
                else
                {
                    IKEffector eff = BIK_GetEffector(i);
                    if (eff != null) bone = eff.bone;
                }

                if (bone != null)
                {
                    _bikEff[i].HasLateUpdateBoneCache = true;
                    _bikEff[i].LateUpdateBonePos = bone.position;
                    _bikEff[i].LateUpdateBoneRot = bone.rotation;
                }
                else
                {
                    _bikEff[i].HasLateUpdateBoneCache = false;
                }
            }
        }

        private void FlushPendingFollowRebinds()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_bikEff[i].PendingFollowRebind)
                    continue;

                _bikEff[i].PendingFollowRebind = false;
                Transform followBone = _bikEff[i].PendingFollowBone;
                bool hasPresetOffset = _bikEff[i].PendingFollowHasPresetOffset;
                Vector3 presetPosOff = _bikEff[i].PendingFollowPosOffset;
                Quaternion presetRotOff = _bikEff[i].PendingFollowRotOffset;
                _bikEff[i].PendingFollowBone = null;
                _bikEff[i].PendingFollowHasPresetOffset = false;

                if (followBone == null || !_bikEff[i].Running || _bikEff[i].Proxy == null)
                    continue;

                _bikEff[i].FollowBone = followBone;
                _bikEff[i].CandidateBone = null;

                if (hasPresetOffset)
                {
                    // プリセットから復元 → 保存済みオフセットをそのまま使用
                    _bikEff[i].FollowBonePositionOffset = presetPosOff;
                    _bikEff[i].FollowBoneRotationOffset = IsRotationDrivenEffector(i)
                        ? presetRotOff : Quaternion.identity;
                }
                else
                {
                    // 通常のrebind → 現在のプロキシ位置からオフセットを再計算
                    Vector3 bonePos = followBone.position;
                    Quaternion boneRot = followBone.rotation;
                    Vector3 proxyPos = _bikEff[i].Proxy.position;
                    Quaternion offsetRot = GetFollowOffsetRotation(followBone);
                    _bikEff[i].FollowBonePositionOffset =
                        Quaternion.Inverse(offsetRot) * (proxyPos - bonePos);
                    _bikEff[i].FollowBoneRotationOffset = IsRotationDrivenEffector(i)
                        ? Quaternion.Inverse(boneRot) * _bikEff[i].Proxy.rotation
                        : Quaternion.identity;
                }

                SetFollowDistanceBaseline(i);
                LogInfo("follow rebind (deferred) idx=" + i + " bone=" + followBone.name
                    + " presetOffset=" + hasPresetOffset);
            }
        }

        private void CacheTransitionFollowBonePositions()
        {
            var points = _activeTransitionPoints;
            if (points == null)
                return;

            for (int i = 0; i < points.Count; i++)
            {
                PoseTransitionPoint point = points[i];
                if (!point.UseFollowLocalTransition || point.FollowBone == null)
                    continue;

                Vector3 bonePos = point.FollowBone.position;
                Quaternion boneRot = point.FollowBone.rotation;

                point.HasFollowBoneLateCache = true;
                point.FollowBoneLateCachePos = bonePos;
                point.FollowBoneLateCacheRot = boneRot;

                // 開始オフセット未確定の場合、LateUpdateの正しいボーン位置で確定する
                if (point.PendingStartOffsetCalc)
                {
                    point.PendingStartOffsetCalc = false;
                    int idx = point.Index;
                    if (idx >= 0 && idx < BIK_TOTAL && _bikEff[idx].Proxy != null)
                    {
                        Vector3 proxyPos = _bikEff[idx].Proxy.position;
                        Quaternion proxyRot = _bikEff[idx].Proxy.rotation;
                        Quaternion offsetRot = GetFollowOffsetRotation(point.FollowBone);

                        point.StartFollowPosOffset = Quaternion.Inverse(offsetRot) * (proxyPos - bonePos);
                        point.StartFollowRotOffset = IsRotationDrivenEffector(idx)
                            ? Quaternion.Inverse(boneRot) * proxyRot
                            : Quaternion.identity;
                    }
                }
            }
        }

        private void ApplyPostDragHoldToBodyIK()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                BodyIkOperationPointState state = _bikEff[i];
                if (state == null || !state.Running || state.Proxy == null)
                    continue;
                if (!state.HasPostDragHold || state.PostDragHoldFrames <= 0)
                    continue;

                state.Proxy.SetPositionAndRotation(state.PostDragHoldPos, state.PostDragHoldRot);

                // While hold is active, keep follow offsets synchronized to the held world pose.
                // This prevents a snap when follow resumes against a moving follow bone (e.g. chest).
                if (state.FollowBone != null)
                {
                    Quaternion offsetRot = GetFollowOffsetRotation(state.FollowBone);
                    state.FollowBonePositionOffset =
                        Quaternion.Inverse(offsetRot) * (state.PostDragHoldPos - state.FollowBone.position);
                    if (IsRotationDrivenEffector(i))
                    {
                        state.FollowBoneRotationOffset =
                            Quaternion.Inverse(state.FollowBone.rotation) * state.PostDragHoldRot;
                    }
                }

                state.PostDragHoldFrames--;
                if (state.PostDragHoldFrames <= 0)
                    state.HasPostDragHold = false;
            }
        }

        private void ApplyBodyIKWeight(int idx)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;
            if (_femaleSession.FullBodyIk == null || !_bikEff[idx].Running)
                return;

            float w = GetBodyIKWeight(idx);
            ApplyBodyIKWeightDirect(idx, w);
        }

        /// <summary>
        /// ソルバーウェイトを直接設定する（設定値は変更しない）。
        /// </summary>
        private void ApplyBodyIKWeightDirect(int idx, float w)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;
            if (_femaleSession.FullBodyIk == null || !_bikEff[idx].Running)
                return;

            if (idx < BIK_BEND_START || idx == BIK_BODY)
            {
                IKEffector eff = BIK_GetEffector(idx);
                if (eff == null)
                    return;
                eff.positionWeight = w;
                if (IsRotationDrivenEffector(idx))
                    eff.rotationWeight = w;
            }
            else
            {
                IKConstraintBend bc = BIK_GetBendConstraint(idx);
                if (bc != null)
                    bc.weight = w;
            }
        }

        private void SetBendGoalProxyByDirection(int idx, Vector3 desiredWorldPos)
        {
            if (idx < BIK_BEND_START || idx >= BIK_TOTAL)
                return;
            if (!_bikEff[idx].Running || _bikEff[idx].Proxy == null)
                return;

            _bikEff[idx].Proxy.position = desiredWorldPos;
        }

        private void EnsureOnBindingsForRunningBodyIK()
        {
            if (_femaleSession.FullBodyIk == null)
                return;

            bool hasRunning = false;
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_bikEff[i].Running)
                {
                    hasRunning = true;
                    break;
                }
            }

            if (hasRunning && !_femaleSession.FullBodyIk.enabled)
            {
                _femaleSession.FullBodyIk.enabled = true;
                float now = Time.unscaledTime;
                if (_settings != null && _settings.DetailLogEnabled && now >= _nextFbbikReenableWarnTime)
                {
                    _nextFbbikReenableWarnTime = now + 0.5f;
                    LogWarn("fbbik was disabled while bodyIK running -> forced enabled");
                }
            }

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_bikEff[i].Running)
                    continue;
                if (_bikEff[i].Proxy == null)
                    continue;

                if (i >= BIK_BEND_START && i != BIK_BODY)
                {
                    IKConstraintBend bc = BIK_GetBendConstraint(i);
                    if (bc == null || ReferenceEquals(bc.bendGoal, _bikEff[i].Proxy))
                        continue;

                    bc.bendGoal = _bikEff[i].Proxy;
                    float now = Time.unscaledTime;
                    if (_settings != null && _settings.DetailLogEnabled && now >= _nextOnRebindWarnTime[i])
                    {
                        _nextOnRebindWarnTime[i] = now + 0.5f;
                        LogWarn("rebind bendGoal idx=" + i + " -> proxy restored");
                    }
                }
                else
                {
                    IKEffector eff = BIK_GetEffector(i);
                    if (eff == null || ReferenceEquals(eff.target, _bikEff[i].Proxy))
                        continue;

                    eff.target = _bikEff[i].Proxy;
                    float now = Time.unscaledTime;
                    if (_settings != null && _settings.DetailLogEnabled && now >= _nextOnRebindWarnTime[i])
                    {
                        _nextOnRebindWarnTime[i] = now + 0.5f;
                        LogWarn("rebind effector target idx=" + i + " -> proxy restored");
                    }
                }
            }
        }

        private IKEffector BIK_GetEffector(int idx)
        {
            if (_femaleSession.FullBodyIk == null)
                return null;
            if (idx == BIK_BODY)
                return _femaleSession.FullBodyIk.solver.bodyEffector;
            if (idx >= BIK_BEND_START)
                return null;
            switch (idx)
            {
                case BIK_LH: return _femaleSession.FullBodyIk.solver.GetEffector(FullBodyBipedEffector.LeftHand);
                case BIK_RH: return _femaleSession.FullBodyIk.solver.GetEffector(FullBodyBipedEffector.RightHand);
                case BIK_LF: return _femaleSession.FullBodyIk.solver.GetEffector(FullBodyBipedEffector.LeftFoot);
                case BIK_RF: return _femaleSession.FullBodyIk.solver.GetEffector(FullBodyBipedEffector.RightFoot);
                case BIK_LS: return _femaleSession.FullBodyIk.solver.GetEffector(FullBodyBipedEffector.LeftShoulder);
                case BIK_RS: return _femaleSession.FullBodyIk.solver.GetEffector(FullBodyBipedEffector.RightShoulder);
                case BIK_LT: return _femaleSession.FullBodyIk.solver.GetEffector(FullBodyBipedEffector.LeftThigh);
                case BIK_RT: return _femaleSession.FullBodyIk.solver.GetEffector(FullBodyBipedEffector.RightThigh);
                default: return null;
            }
        }

        private IKConstraintBend BIK_GetBendConstraint(int idx)
        {
            if (_femaleSession.FullBodyIk == null || idx < BIK_BEND_START)
                return null;
            return _femaleSession.FullBodyIk.solver.GetBendConstraint(_bikEff[idx].BendChain);
        }

        private static FullBodyBipedChain BIK_IndexToBendChain(int idx)
        {
            switch (idx)
            {
                case BIK_LE: return FullBodyBipedChain.LeftArm;
                case BIK_RE: return FullBodyBipedChain.RightArm;
                case BIK_LK: return FullBodyBipedChain.LeftLeg;
                case BIK_RK: return FullBodyBipedChain.RightLeg;
                default: return FullBodyBipedChain.LeftArm;
            }
        }

        private void WarnIfOffBindingStillActive(int idx)
        {
            if (_femaleSession.FullBodyIk == null || idx < 0 || idx >= BIK_TOTAL)
                return;
            if (_settings == null || !_settings.DetailLogEnabled)
                return;

            bool active = false;
            if (idx >= BIK_BEND_START && idx != BIK_BODY)
            {
                IKConstraintBend bc = BIK_GetBendConstraint(idx);
                if (bc != null)
                    active = IsOwnedProxyTransform(bc.bendGoal);
            }
            else
            {
                IKEffector eff = BIK_GetEffector(idx);
                if (eff != null)
                    active = IsOwnedProxyTransform(eff.target);
            }

            if (!active)
                return;

            float now = Time.unscaledTime;
            if (now < _nextOffLeakWarnTime[idx])
                return;

            _nextOffLeakWarnTime[idx] = now + 0.5f;
            LogWarn("bodyIK owned binding leak: " + BIK_Labels[idx] + " state=" + BuildSolverBindingStateText(idx));
        }

        private string BuildSolverBindingStateText(int idx)
        {
            if (_femaleSession.FullBodyIk == null)
                return "fbbik=null";
            if (idx < 0 || idx >= BIK_TOTAL)
                return "idx=invalid";

            if (idx >= BIK_BEND_START && idx != BIK_BODY)
            {
                IKConstraintBend bc = BIK_GetBendConstraint(idx);
                if (bc == null)
                    return "bend=null";
                string bendGoalName = bc.bendGoal != null ? bc.bendGoal.name : "null";
                return "bendGoal=" + bendGoalName + ", weight=" + bc.weight.ToString("F3");
            }

            IKEffector eff = BIK_GetEffector(idx);
            if (eff == null)
                return "eff=null";

            string targetName = eff.target != null ? eff.target.name : "null";
            string text = "target=" + targetName + ", posW=" + eff.positionWeight.ToString("F3");
            if (IsRotationDrivenEffector(idx))
                text += ", rotW=" + eff.rotationWeight.ToString("F3");
            return text;
        }

        private void ForceOffBindingsForDisabledBodyIK()
        {
            if (_femaleSession.FullBodyIk == null)
                return;

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_bikWant[i] || _bikEff[i].Running)
                    continue;

                if (i >= BIK_BEND_START && i != BIK_BODY)
                {
                    IKConstraintBend bc = BIK_GetBendConstraint(i);
                    if (bc == null)
                        continue;

                    bool owned = IsOwnedProxyTransform(bc.bendGoal);
                    if (!owned)
                        continue;

                    if (bc.bendGoal != null)
                        LogDebug("force-off bend idx=" + i + " before=" + BuildSolverBindingStateText(i));

                    Transform restore = GetRestoreBendGoalTarget(i, bc);
                    if (!ReferenceEquals(bc.bendGoal, restore))
                        bc.bendGoal = restore;
                    bc.weight = 0f;
                }
                else
                {
                    IKEffector eff = BIK_GetEffector(i);
                    if (eff == null)
                        continue;

                    bool hasLeak = IsOwnedProxyTransform(eff.target);
                    if (hasLeak)
                        LogDebug("force-off eff idx=" + i + " before=" + BuildSolverBindingStateText(i));
                    else
                        continue;

                    Transform restore = GetRestoreEffectorTarget(i, eff);
                    if (!ReferenceEquals(eff.target, restore))
                        eff.target = restore;
                    eff.positionWeight = 0f;
                    if (IsRotationDrivenEffector(i))
                        eff.rotationWeight = 0f;
                }
            }
        }

        private void ResetCachedBodyIkBindings()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                _bikEff[i].HasOriginalEffectorTarget = false;
                _bikEff[i].OriginalEffectorTarget = null;
                _bikEff[i].HasOriginalBendGoal = false;
                _bikEff[i].OriginalBendGoal = null;

                if (_bikEff[i].FallbackEffectorTargetGo != null)
                    Destroy(_bikEff[i].FallbackEffectorTargetGo);
                if (_bikEff[i].FallbackBendGoalGo != null)
                    Destroy(_bikEff[i].FallbackBendGoalGo);

                _bikEff[i].FallbackEffectorTargetGo = null;
                _bikEff[i].FallbackBendGoalGo = null;
            }
        }

        private void CaptureOriginalEffectorTargetIfNeeded(int idx, IKEffector eff)
        {
            if (eff == null)
                return;

            if (_bikEff[idx].HasOriginalEffectorTarget && !IsOwnedPluginBindingTransform(_bikEff[idx].OriginalEffectorTarget))
                return;

            if (IsOwnedPluginBindingTransform(eff.target))
                return;

            _bikEff[idx].HasOriginalEffectorTarget = true;
            _bikEff[idx].OriginalEffectorTarget = eff.target;
        }

        private void CaptureOriginalBendGoalIfNeeded(int idx, IKConstraintBend bend)
        {
            if (bend == null)
                return;

            if (_bikEff[idx].HasOriginalBendGoal && !IsOwnedPluginBindingTransform(_bikEff[idx].OriginalBendGoal))
                return;

            if (IsOwnedPluginBindingTransform(bend.bendGoal))
                return;

            _bikEff[idx].HasOriginalBendGoal = true;
            _bikEff[idx].OriginalBendGoal = bend.bendGoal;
        }

        private Transform GetRestoreEffectorTarget(int idx, IKEffector eff)
        {
            Transform restore = _bikEff[idx].HasOriginalEffectorTarget ? _bikEff[idx].OriginalEffectorTarget : null;
            if (IsOwnedPluginBindingTransform(restore))
                restore = null;

            if (restore == null && eff != null && !IsOwnedPluginBindingTransform(eff.target))
                restore = eff.target;

            if (restore == null)
                restore = GetOrCreateFallbackBindingTransform(idx, isBendGoal: false, eff != null ? eff.bone : null);

            return restore;
        }

        private Transform GetRestoreBendGoalTarget(int idx, IKConstraintBend bend)
        {
            Transform restore = _bikEff[idx].HasOriginalBendGoal ? _bikEff[idx].OriginalBendGoal : null;
            if (IsOwnedPluginBindingTransform(restore))
                restore = null;

            if (restore == null && bend != null && !IsOwnedPluginBindingTransform(bend.bendGoal))
                restore = bend.bendGoal;

            if (restore == null)
                restore = GetOrCreateFallbackBindingTransform(idx, isBendGoal: true, bend != null ? bend.bone2 : null);

            return restore;
        }

        private Transform GetOrCreateFallbackBindingTransform(int idx, bool isBendGoal, Transform srcBone)
        {
            GameObject go = isBendGoal ? _bikEff[idx].FallbackBendGoalGo : _bikEff[idx].FallbackEffectorTargetGo;
            if (go == null)
            {
                string name = "__BodyIkGizmoFallback_" + (isBendGoal ? "B" : "E") + "_" + idx;
                go = new GameObject(name);
                go.hideFlags = HideFlags.HideAndDontSave;
                if (isBendGoal)
                    _bikEff[idx].FallbackBendGoalGo = go;
                else
                    _bikEff[idx].FallbackEffectorTargetGo = go;
            }

            if (srcBone != null)
                go.transform.SetPositionAndRotation(srcBone.position, srcBone.rotation);

            return go.transform;
        }

        private static bool IsOwnedPluginBindingTransform(Transform tr)
        {
            if (tr == null || string.IsNullOrEmpty(tr.name))
                return false;

            return tr.name.StartsWith("__BodyIkGizmoProxy_", StringComparison.Ordinal)
                || tr.name.StartsWith("__BodyIkGizmoFallback_", StringComparison.Ordinal);
        }

        private static bool IsOwnedProxyTransform(Transform tr)
        {
            return tr != null && tr.name != null && tr.name.StartsWith("__BodyIkGizmoProxy_", StringComparison.Ordinal);
        }
    }
}
