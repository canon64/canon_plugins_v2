using Bindings = CanonPlugins.Shared.PluginInput;
using System.Collections;
using UnityEngine;
using VRGIN.Controls;
using VRGIN.Core;
using Valve.VR;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        // ── モード切替 ────────────────────────────────────────────────────────

        private void ToggleVRGrabMode()
        {
            if (!VR.Active || VR.Mode == null)
            {
                LogInfo("[VRGrab] VR未起動のため切替不可");
                return;
            }

            _vrGrabMode = !_vrGrabMode;

            if (_vrGrabMode)
            {
                SetAllVRMarkersVisible(false);
                LogInfo("[VRGrab] ON");
            }
            else
            {
                ExitVRGrabMode();
            }
        }

        private void ExitVRGrabMode()
        {
            DisableBodyCtrlLink();
            ReleaseVRGrab(ref _vrInteractionState.LeftGrabIndex,  0);
            ReleaseVRGrab(ref _vrInteractionState.RightGrabIndex, 1);
            _vrGrabLockLeft?.Release();  _vrGrabLockLeft  = null;
            _vrGrabLockRight?.Release(); _vrGrabLockRight = null;

            // 全ギズモの掴み状態をリセット
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_bikEff[i].Gizmo != null)
                    _bikEff[i].Gizmo.SetVRGrabState(0);
                _bikEff[i].GizmoDragging = false;
            }
            RecomputeGizmoDraggingState();

            SetAllVRMarkersVisible(false);
            _vrGrabMode = false;
            LogInfo("[VRGrab] OFF");
        }

        // ── 左コントローラー腰リンク ──────────────────────────────────────────

        private void ToggleBodyCtrlLink()
        {
            if (!VR.Active || VR.Mode == null)
            {
                LogInfo("[BodyCtrlLink] VR未起動のため切替不可");
                return;
            }

            if (!_bikEff[BIK_BODY].Running || _bikEff[BIK_BODY].Proxy == null)
            {
                LogInfo("[BodyCtrlLink] 腰中央IKが有効でないため切替不可");
                return;
            }

            if (_bodyCtrlLinkEnabled)
            {
                DisableBodyCtrlLink();
                RestoreLeftControllerVisuals();
            }
            else
            {
                VR.Mode.Left.TryAcquireFocus(out _vrInteractionState.BodyControlLock);

                Transform proxy = _bikEff[BIK_BODY].Proxy;
                Transform ctrl  = ((Component)VR.Mode.Left).transform;

                _bodyControlState.BaseProxyPosition = proxy.position;
                _bodyControlState.BaseProxyRotation = proxy.rotation;
                _bodyControlState.BaseControllerPosition  = ctrl.position;
                _bodyControlState.BaseControllerRotation  = ctrl.rotation;
                _bodyControlState.SmoothVelocity = Vector3.zero;

                _bodyCtrlLinkEnabled = true;
                HideLeftControllerVisuals();
                LogInfo("[BodyCtrlLink] ON");
            }
        }

        internal void RebaselineBodyCtrlLink()
        {
            if (!_bikEff[BIK_BODY].Running)
            {
                LogInfo("[BodyCtrlLink] 腰中央IKが有効でないためリベースライン不可");
                return;
            }

            DisableBodyCtrlLink();
            ResetBodyIKPartToAnimationPose(BIK_BODY, stopPoseTransition: false, logSnapshot: false, applyImmediate: true);
            SetBodyIK(BIK_BODY, true, saveSettings: false, reason: "rebaseline");
            ToggleBodyCtrlLink();
            LogInfo("[BodyCtrlLink] リベースライン完了");
        }

        private void DisableBodyCtrlLink()
        {
            if (!_bodyCtrlLinkEnabled)
                return;

            _vrBodyCtrlLock?.Release();
            _vrBodyCtrlLock = null;
            _bodyCtrlLinkEnabled = false;
            RestoreLeftControllerVisuals();
            LogInfo("[BodyCtrlLink] OFF");
        }

        private void UpdateBodyCtrlLink(float dt)
        {
            if (!_bodyCtrlLinkEnabled || !VR.Active || VR.Mode == null)
                return;
            if (!_bikEff[BIK_BODY].Running || _bikEff[BIK_BODY].Proxy == null)
            {
                DisableBodyCtrlLink();
                return;
            }

            Transform proxy = _bikEff[BIK_BODY].Proxy;
            Transform ctrl  = ((Component)VR.Mode.Left).transform;

            Vector3 delta = ctrl.position - _bodyControlState.BaseControllerPosition;
            Vector3 scaledDelta = new Vector3(
                delta.x * (_settings != null ? _settings.BodyCtrlChangeFactorX : 5f),
                delta.y * (_settings != null ? _settings.BodyCtrlChangeFactorY : 4f),
                delta.z * (_settings != null ? _settings.BodyCtrlChangeFactorZ : 5f));

            Vector3 targetPos = _bodyControlState.BaseProxyPosition + scaledDelta;
            float dampen = _settings != null ? _settings.BodyCtrlDampen : 0.05f;

            if (dampen <= 0f)
            {
                proxy.position = targetPos;
                _bodyControlState.SmoothVelocity = Vector3.zero;
            }
            else
            {
                proxy.position = Vector3.SmoothDamp(
                    proxy.position, targetPos,
                    ref _bodyControlState.SmoothVelocity,
                    dampen, Mathf.Infinity, dt);
            }

            Quaternion deltaRot = ctrl.rotation * Quaternion.Inverse(_bodyControlState.BaseControllerRotation);
            proxy.rotation = deltaRot * _bodyControlState.BaseProxyRotation;
        }

        // ── 毎フレーム処理 ───────────────────────────────────────────────────

        private void HandleVRGrab()
        {
            if (!VR.Active || VR.Mode == null) return;
            ProcessControllerGrab(VR.Mode.Left,  ref _vrInteractionState.LeftGrabIndex,  0);
            ProcessControllerGrab(VR.Mode.Right, ref _vrInteractionState.RightGrabIndex, 1);
            UpdateVRGrabHighlights();
        }

        private void ProcessControllerGrab(Controller ctrl, ref int grabbedIdx, int ctrlIdx)
        {
            if (ctrl == null) return;

            var       input  = ctrl.Input;
            Transform ctrlTf = ((Component)ctrl).transform;

            if (Bindings.VrDown("MainGirlHipHijack.grab", input, "Grip"))
            {
                int nearest = FindNearestProxy(ctrlTf.position);
                if (nearest >= 0)
                {
                    grabbedIdx = nearest;
                    _bikEff[nearest].GizmoDragging = true;
                    _bikEff[nearest].GrabStartCtrlRot  = ctrlTf.rotation;
                    _bikEff[nearest].GrabStartProxyRot = _bikEff[nearest].Proxy != null
                        ? _bikEff[nearest].Proxy.rotation
                        : Quaternion.identity;

                    // 掴んでいる間だけそのコントローラーのVRデフォルト挙動を抑制
                    if (ctrlIdx == 0) ctrl.TryAcquireFocus(out _vrInteractionState.LeftGrabLock);
                    else              ctrl.TryAcquireFocus(out _vrInteractionState.RightGrabLock);

                    if (_bikEff[nearest].Gizmo != null)
                        _bikEff[nearest].Gizmo.SetVRGrabState(2);
                    if (_bikEff[nearest].VRMarkerGo != null)
                        _bikEff[nearest].VRMarkerGo.SetActive(true);
                    if (_bikEff[nearest].VRMarkerRend != null)
                        _bikEff[nearest].VRMarkerRend.sharedMaterial.color = Color.yellow;

                    RecomputeGizmoDraggingState();
                    LogInfo("[VRGrab] Ctrl" + ctrlIdx + " Grip↓ → " + BIK_Labels[nearest] + " 掴み開始");
                }
                else
                {
                    LogDebug("[VRGrab] Ctrl" + ctrlIdx + " Grip↓ 範囲内にProxy無し");
                }
            }

            if (grabbedIdx >= 0)
            {
                if (Bindings.VrHeld("MainGirlHipHijack.grab", input, "Grip"))
                {
                    if (_bikEff[grabbedIdx].Proxy != null)
                    {
                        if (_bikEff[grabbedIdx].IsBendGoal)
                            SetBendGoalProxyByDirection(grabbedIdx, ctrlTf.position);
                        else
                            _bikEff[grabbedIdx].Proxy.position = ctrlTf.position;

                        if (IsRotationDrivenEffector(grabbedIdx))
                        {
                            Quaternion deltaRot = ctrlTf.rotation * Quaternion.Inverse(_bikEff[grabbedIdx].GrabStartCtrlRot);
                            _bikEff[grabbedIdx].Proxy.rotation = deltaRot * _bikEff[grabbedIdx].GrabStartProxyRot;
                        }

                        // 追従中の場合はオフセットをリアルタイム更新（相対位置を再調整）
                        if (_bikEff[grabbedIdx].FollowBone != null)
                        {
                            Transform followBone = _bikEff[grabbedIdx].FollowBone;
                            QueueBodyIKFollowRebind(grabbedIdx, followBone, false, Vector3.zero, Quaternion.identity, null);
                        }
                    }

                    // 候補ボーンあり（追従未確定時のみ）= 紫、なし or 追従中 = 黄
                    bool hasCandidate = CanUseBoneFollow(grabbedIdx)
                        && _bikEff[grabbedIdx].FollowBone == null
                        && _bikEff[grabbedIdx].CandidateBone != null;
                    Color grabColor = hasCandidate ? new Color(0.8f, 0.2f, 1f) : Color.yellow;
                    if (_bikEff[grabbedIdx].VRMarkerRend != null)
                        _bikEff[grabbedIdx].VRMarkerRend.sharedMaterial.color = grabColor;
                    if (_bikEff[grabbedIdx].Gizmo != null)
                        _bikEff[grabbedIdx].Gizmo.SetVRGrabState(hasCandidate ? 1 : 2);

                    // トリガー押下: 追従確定 or 追従解消
                    if (Bindings.VrDown("MainGirlHipHijack.follow", input, "Trigger"))
                    {
                        if (hasCandidate)
                        {
                            Transform bone = _bikEff[grabbedIdx].CandidateBone;
                            _bikEff[grabbedIdx].FollowBone = bone;
                            _bikEff[grabbedIdx].CandidateBone = null;
                            if (_bikEff[grabbedIdx].IsBendGoal)
                                SetBendGoalProxyByDirection(grabbedIdx, bone.position);
                            ArmBodyIKPostDragHold(grabbedIdx, "vr-follow-bind");
                            QueueBodyIKFollowRebind(grabbedIdx, bone, false, Vector3.zero, Quaternion.identity, "vr-follow-bind");
                            HighlightSnapBone(bone);
                            LogInfo("[VRGrab] Ctrl" + ctrlIdx + " Trigger↓ → " + BIK_Labels[grabbedIdx] + " 追従確定: " + bone.name);
                        }
                        else if (_bikEff[grabbedIdx].FollowBone != null)
                        {
                            ClearBodyIKFollowBone(grabbedIdx);
                            LogInfo("[VRGrab] Ctrl" + ctrlIdx + " Trigger↓ → " + BIK_Labels[grabbedIdx] + " 追従解消");
                        }
                    }
                }
                else if (Bindings.VrUp("MainGirlHipHijack.grab", input, "Grip"))
                {
                    if (_bikEff[grabbedIdx].Proxy != null)
                    {
                        if (_bikEff[grabbedIdx].IsBendGoal)
                            SetBendGoalProxyByDirection(grabbedIdx, ctrlTf.position);
                        else
                            _bikEff[grabbedIdx].Proxy.position = ctrlTf.position;

                        if (IsRotationDrivenEffector(grabbedIdx))
                        {
                            Quaternion deltaRot = ctrlTf.rotation * Quaternion.Inverse(_bikEff[grabbedIdx].GrabStartCtrlRot);
                            _bikEff[grabbedIdx].Proxy.rotation = deltaRot * _bikEff[grabbedIdx].GrabStartProxyRot;
                        }

                        if (_bikEff[grabbedIdx].FollowBone != null)
                        {
                            Transform followBone = _bikEff[grabbedIdx].FollowBone;
                            ArmBodyIKPostDragHold(grabbedIdx, "vr-grip-release");
                            QueueBodyIKFollowRebind(grabbedIdx, followBone, false, Vector3.zero, Quaternion.identity, "vr-grip-release");
                        }
                    }

                    // グリップ離し: フォーカス解放・追従はそのまま維持・自動スナップなし
                    if (ctrlIdx == 0) { _vrGrabLockLeft?.Release();  _vrGrabLockLeft  = null; }
                    else              { _vrGrabLockRight?.Release(); _vrGrabLockRight = null; }

                    _bikEff[grabbedIdx].GizmoDragging = false;
                    if (_bikEff[grabbedIdx].Gizmo != null)
                        _bikEff[grabbedIdx].Gizmo.SetVRGrabState(0);
                    if (_bikEff[grabbedIdx].VRMarkerRend != null)
                        _bikEff[grabbedIdx].VRMarkerRend.sharedMaterial.color =
                            _bikEff[grabbedIdx].FollowBone != null ? Color.green : new Color(0f, 1f, 1f);
                    if (_bikEff[grabbedIdx].VRMarkerGo != null)
                        _bikEff[grabbedIdx].VRMarkerGo.SetActive(false);

                    RecomputeGizmoDraggingState();
                    LogInfo("[VRGrab] Ctrl" + ctrlIdx + " Grip↑ → " + BIK_Labels[grabbedIdx]
                        + (_bikEff[grabbedIdx].FollowBone != null ? " 追従維持" : " フリー解放"));
                    grabbedIdx = -1;
                }
            }
        }

        private void ReleaseVRGrab(ref int grabbedIdx, int ctrlIdx)
        {
            if (grabbedIdx >= 0 && grabbedIdx < BIK_TOTAL)
            {
                _bikEff[grabbedIdx].GizmoDragging = false;
                if (_bikEff[grabbedIdx].Gizmo != null)
                    _bikEff[grabbedIdx].Gizmo.SetVRGrabState(0);
                if (_bikEff[grabbedIdx].VRMarkerRend != null)
                    _bikEff[grabbedIdx].VRMarkerRend.sharedMaterial.color = new Color(0f, 1f, 1f);
                if (_bikEff[grabbedIdx].VRMarkerGo != null)
                    _bikEff[grabbedIdx].VRMarkerGo.SetActive(false);
                LogInfo("[VRGrab] Ctrl" + ctrlIdx + " → " + BIK_Labels[grabbedIdx] + " 解放");
            }
            grabbedIdx = -1;
        }

        // ── 近傍検索 ─────────────────────────────────────────────────────────

        private int FindNearestProxy(Vector3 pos)
        {
            int   best     = -1;
            float grabDist = _settings != null ? _settings.VRGrabDistance : 0.15f;
            float bestDist = grabDist;

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_bikEff[i].Running)         continue;
                if (_vrLeftGrabIdx  == i)         continue;
                if (_vrRightGrabIdx == i)         continue;
                if (_bikEff[i].Proxy == null)     continue;
                if (i == BIK_BODY && _bodyCtrlLinkEnabled) continue;
                float d = Vector3.Distance(_bikEff[i].Proxy.position, pos);
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }

        // ── ハイライト ────────────────────────────────────────────────────────

        private void UpdateVRGrabHighlights()
        {
            if (VR.Mode == null) return;

            var ctrls   = new[] { VR.Mode.Left, VR.Mode.Right };
            var inRange = new bool[BIK_TOTAL];
            float grabDist = _settings != null ? _settings.VRGrabDistance : 0.15f;

            foreach (var ctrl in ctrls)
            {
                if (ctrl == null) continue;
                Transform ctrlTf = ((Component)ctrl).transform;
                for (int i = 0; i < BIK_TOTAL; i++)
                {
                    if (!_bikEff[i].Running)        continue;
                    if (_bikEff[i].Proxy == null)   continue;
                    if (_vrLeftGrabIdx  == i)        continue;
                    if (_vrRightGrabIdx == i)        continue;
                    if (Vector3.Distance(_bikEff[i].Proxy.position, ctrlTf.position) <= grabDist)
                        inRange[i] = true;
                }
            }

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_vrLeftGrabIdx  == i) continue;
                if (_vrRightGrabIdx == i) continue;

                if (_bikEff[i].Gizmo != null)
                    _bikEff[i].Gizmo.SetVRGrabState(inRange[i] ? 1 : 0);
                if (_bikEff[i].VRMarkerGo != null && _bikEff[i].VRMarkerGo.activeSelf)
                    _bikEff[i].VRMarkerGo.SetActive(false);

                if (_bikEff[i].VRMarkerRend != null && _bikEff[i].VRMarkerGo != null && _bikEff[i].VRMarkerGo.activeSelf)
                {
                    Color c;
                    if (_bikEff[i].FollowBone != null)   c = Color.green;
                    else if (inRange[i])                  c = Color.white;
                    else                                  c = new Color(0f, 1f, 1f);
                    _bikEff[i].VRMarkerRend.sharedMaterial.color = c;
                }
            }
        }

        // ── VRスフィアマーカー管理 ────────────────────────────────────────────

        private void SetAllVRMarkersVisible(bool visible)
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (_bikEff[i].VRMarkerGo == null) continue;
                if (visible && !_bikEff[i].Running) continue;
                _bikEff[i].VRMarkerGo.SetActive(visible);
                if (visible && _bikEff[i].VRMarkerRend != null)
                    _bikEff[i].VRMarkerRend.sharedMaterial.color = new Color(0f, 1f, 1f);
            }
        }

        // ── スナップボーン一時ハイライト ──────────────────────────────────────

        private void HighlightSnapBone(Transform bone)
        {
            if (bone == null) return;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            if (go == null) return;
            Destroy(go.GetComponent<Collider>());

            go.name = "__VRSnapHighlight";
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(bone, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * 0.07f;

            var r = go.GetComponent<Renderer>();
            if (r != null)
                r.material.color = new Color(1f, 0.35f, 0.1f, 1f);

            StartCoroutine(DestroyAfter(go, 1.0f));
        }

        private IEnumerator DestroyAfter(GameObject go, float sec)
        {
            yield return new WaitForSeconds(sec);
            if (go != null) Destroy(go);
        }

        // ── 左コントローラー表示制御 ────────────────────────────────────────────

        private void HideLeftControllerVisuals()
        {
            if (_leftCtrlHidden)
                return;
            if (!VR.Active || VR.Mode == null || VR.Mode.Left == null)
                return;

            Transform ctrlTf = ((Component)VR.Mode.Left).transform;
            if (ctrlTf == null)
                return;

            // Renderer
            _leftCtrlRenderers = ctrlTf.GetComponentsInChildren<Renderer>(true) ?? System.Array.Empty<Renderer>();
            _leftCtrlRendererEnabled = new bool[_leftCtrlRenderers.Length];
            for (int i = 0; i < _leftCtrlRenderers.Length; i++)
            {
                if (_leftCtrlRenderers[i] == null) continue;
                _leftCtrlRendererEnabled[i] = _leftCtrlRenderers[i].enabled;
                if (_leftCtrlRenderers[i].enabled)
                    _leftCtrlRenderers[i].enabled = false;
            }

            // Collider
            _leftCtrlColliders = ctrlTf.GetComponentsInChildren<Collider>(true) ?? System.Array.Empty<Collider>();
            _leftCtrlColliderEnabled = new bool[_leftCtrlColliders.Length];
            for (int i = 0; i < _leftCtrlColliders.Length; i++)
            {
                if (_leftCtrlColliders[i] == null) continue;
                _leftCtrlColliderEnabled[i] = _leftCtrlColliders[i].enabled;
                if (_leftCtrlColliders[i].enabled)
                    _leftCtrlColliders[i].enabled = false;
            }

            _leftCtrlHidden = true;
            LogInfo("[LeftCtrl] 非表示化 renderers=" + _leftCtrlRenderers.Length + " colliders=" + _leftCtrlColliders.Length);
        }

        private void RestoreLeftControllerVisuals()
        {
            if (!_leftCtrlHidden)
                return;

            for (int i = 0; i < _leftCtrlRenderers.Length; i++)
            {
                if (_leftCtrlRenderers[i] == null) continue;
                bool orig = i < _leftCtrlRendererEnabled.Length ? _leftCtrlRendererEnabled[i] : true;
                if (_leftCtrlRenderers[i].enabled != orig)
                    _leftCtrlRenderers[i].enabled = orig;
            }

            for (int i = 0; i < _leftCtrlColliders.Length; i++)
            {
                if (_leftCtrlColliders[i] == null) continue;
                bool orig = i < _leftCtrlColliderEnabled.Length ? _leftCtrlColliderEnabled[i] : true;
                if (_leftCtrlColliders[i].enabled != orig)
                    _leftCtrlColliders[i].enabled = orig;
            }

            _leftCtrlRenderers = System.Array.Empty<Renderer>();
            _leftCtrlRendererEnabled = System.Array.Empty<bool>();
            _leftCtrlColliders = System.Array.Empty<Collider>();
            _leftCtrlColliderEnabled = System.Array.Empty<bool>();
            _leftCtrlHidden = false;
            LogInfo("[LeftCtrl] 表示復帰");
        }
    }
}
