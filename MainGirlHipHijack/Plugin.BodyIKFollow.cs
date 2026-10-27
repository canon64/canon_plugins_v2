using System;
using UnityEngine;
using MainGirlHipHijack.Runtime;
using VRGIN.Core;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private const string FollowHmdTargetName = "HMD";

        private static bool CanUseBoneFollow(int idx)
        {
            return idx == BIK_LH || idx == BIK_RH || idx == BIK_LF || idx == BIK_RF;
        }

        private void UpdateFollowBones()
        {
            UpdateExternalFollowTargets();
            UpdateComposerFollowTargets();
            ApplyHeadBoneToHmdConversion();
            UpdateFollowDistanceThreshold();

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_bikEff[i].Running)
                    continue;
                if (_bikEff[i].FollowBone == null)
                    continue;
                if (_bikEff[i].GizmoDragging)
                    continue;
                if (_bikEff[i].Proxy == null)
                    continue;
                if (_bikEff[i].HasPostDragHold && _bikEff[i].PostDragHoldFrames > 0)
                    continue;

                Vector3 targetPos = GetFollowTargetPosition(i);

                if (_bikEff[i].IsBendGoal)
                    SetBendGoalProxyByDirection(i, targetPos);
                else
                    _bikEff[i].Proxy.position = targetPos;

                if (IsRotationDrivenEffector(i))
                    _bikEff[i].Proxy.rotation = _bikEff[i].FollowBone.rotation * _bikEff[i].FollowBoneRotationOffset;
            }
        }

        private void ClearBodyIKFollowBone(int idx)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;

            _bikEff[idx].FollowBone = null;
            _bikEff[idx].FollowBonePositionOffset = Vector3.zero;
            _bikEff[idx].FollowBoneRotationOffset = Quaternion.identity;
            _bikEff[idx].CandidateBone = null;
            _bikEff[idx].PendingFollowRebind = false;
            _bikEff[idx].PendingFollowBone = null;
            _bikEff[idx].PendingFollowHasPresetOffset = false;
            _bikEff[idx].PendingFollowPosOffset = Vector3.zero;
            _bikEff[idx].PendingFollowRotOffset = Quaternion.identity;
            ClearFollowDistanceBaseline(idx);
            SetBodyIKWeight(idx, GetConfiguredBodyIKWeight(idx), saveSettings: false, updateSettings: false);
        }

        private void ArmBodyIKPostDragHold(int idx, string reason)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;

            BodyIkOperationPointState state = _bikEff[idx];
            if (state == null || state.Proxy == null)
                return;

            bool alreadyHolding = state.HasPostDragHold && state.PostDragHoldFrames > 0;
            state.PostDragHoldPos = state.Proxy.position;
            state.PostDragHoldRot = state.Proxy.rotation;
            state.PostDragHoldFrames = BodyIkPostDragHoldFrames;
            state.HasPostDragHold = true;

            if (!alreadyHolding)
                LogDebug("post-drag hold armed idx=" + idx + " reason=" + reason + " frames=" + state.PostDragHoldFrames);
        }

        private void QueueBodyIKFollowRebind(
            int idx,
            Transform followBone,
            bool hasPresetOffset,
            Vector3 presetPosOffset,
            Quaternion presetRotOffset,
            string reason)
        {
            if (idx < 0 || idx >= BIK_TOTAL || followBone == null)
                return;

            BodyIkOperationPointState state = _bikEff[idx];
            if (state == null)
                return;

            state.PendingFollowRebind = true;
            state.PendingFollowBone = followBone;
            state.PendingFollowHasPresetOffset = hasPresetOffset;
            state.PendingFollowPosOffset = presetPosOffset;
            state.PendingFollowRotOffset = presetRotOffset;

            if (!string.IsNullOrEmpty(reason))
                LogDebug("follow rebind queued idx=" + idx + " bone=" + followBone.name + " reason=" + reason);
        }

        private bool TrySetNearestFollowBone(int idx)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return false;
            if (!CanUseBoneFollow(idx))
                return false;
            if (!_bikEff[idx].Running || _bikEff[idx].Proxy == null)
                return false;

            Transform bone = _bikEff[idx].CandidateBone;
            if (bone == null)
                bone = FindNearestBone(idx, _bikEff[idx].Proxy.position);
            if (bone == null)
            {
                LogWarn("follow bone not found idx=" + idx + " snapDist=" + _settings.FollowSnapDistance.ToString("F3"));
                return false;
            }

            _bikEff[idx].FollowBone = bone;
            _bikEff[idx].CandidateBone = null;
            ArmBodyIKPostDragHold(idx, "nearest-follow");
            QueueBodyIKFollowRebind(idx, bone, false, Vector3.zero, Quaternion.identity, "nearest-follow");

            string cacheType = "UNKNOWN";
            if (_maleSession.MaleBoneCache != null && IsBoneInCache(_maleSession.MaleBoneCache, bone))
                cacheType = "MALE";
            else if (_femaleSession.BoneCache != null && IsBoneInCache(_femaleSession.BoneCache, bone))
                cacheType = "FEMALE";
            LogInfo($"follow bone queued idx={idx} bone={bone.name} cache={cacheType} bonePos=({bone.position.x:F4},{bone.position.y:F4},{bone.position.z:F4}) proxyPos=({_bikEff[idx].Proxy.position.x:F4},{_bikEff[idx].Proxy.position.y:F4},{_bikEff[idx].Proxy.position.z:F4})");
            return true;
        }

        private Transform FindNearestBone(int ikIdx, Vector3 pos)
        {
            if (!CanUseBoneFollow(ikIdx))
                return null;

            float bestDist = Mathf.Max(0.02f, _settings.FollowSnapDistance);
            float hmdBestDist = _settings != null
                ? Mathf.Max(bestDist, _settings.HmdFollowSnapDistance)
                : bestDist;
            Transform best = null;

            Transform hmd = GetOrCreateHmdFollowTarget();
            if (hmd != null && Vector3.Distance(hmd.position, pos) <= hmdBestDist)
                return hmd;

            FindNearestBoneInCache(ikIdx, pos, _femaleSession.BoneCache, ref best, ref bestDist);
            FindNearestBoneInCache(ikIdx, pos, EnsureMaleBoneCacheForFollow(), ref best, ref bestDist);

            return best;
        }

        private void FindNearestBoneInCache(int ikIdx, Vector3 pos, Transform[] cache, ref Transform best, ref float bestDist)
        {
            if (cache == null || cache.Length == 0)
                return;

            for (int i = 0; i < cache.Length; i++)
            {
                Transform t = cache[i];
                if (!CanSnapToBone(ikIdx, t))
                    continue;

                float d = Vector3.Distance(t.position, pos);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = t;
                }
            }
        }

        private bool CanSnapToBone(int ikIdx, Transform bone)
        {
            if (bone == null)
                return false;

            string boneName = bone.name ?? string.Empty;
            if (IsCameraHelperBoneName(boneName))
                return false;

            bool allowAllHeadBones = _settings != null
                && _settings.FollowAllowAllHeadBonesForSnap
                && IsHeadOrAboveBone(bone);
            if (!allowAllHeadBones)
            {
                bool isCheekSub = boneName.StartsWith("cf_J_CheekLow_s_", StringComparison.Ordinal);
                bool isCf = boneName.StartsWith("cf_j_", StringComparison.Ordinal);
                bool isCm = boneName.StartsWith("cm_j_", StringComparison.Ordinal);
                if (!isCf && !isCm && !isCheekSub)
                    return false;
                if (IsFingerBoneName(boneName))
                    return false;
                if (IsExcludedBoneName(boneName))
                    return false;
            }

            Transform lowerBoundary = GetLowerBoundaryBone(ikIdx);
            if (lowerBoundary != null && IsSameOrDescendantOf(bone, lowerBoundary))
                return false;

            switch (ikIdx)
            {
                case BIK_LH: return !IsLeftArmLowerBoneName(boneName);
                case BIK_RH: return !IsRightArmLowerBoneName(boneName);
                case BIK_LF: return !IsLeftLegLowerBoneName(boneName);
                case BIK_RF: return !IsRightLegLowerBoneName(boneName);
                default: return false;
            }
        }

        private Transform[] EnsureMaleBoneCacheForFollow()
        {
            if (_hSceneSession.Process == null)
                return null;

            if (_maleSession.TargetMaleCha != null && !_maleSession.TargetMaleCha)
            {
                _maleSession.TargetMaleCha = null;
                _maleSession.MaleBoneCache = null;
            }

            if (_maleSession.TargetMaleCha == null)
            {
                _maleSession.TargetMaleCha = ResolveMainMale(_hSceneSession.Process);
                if (_maleSession.TargetMaleCha != null)
                    _maleSession.MaleBoneCache = null;
            }

            if (_maleSession.TargetMaleCha == null)
                return null;

            if (_maleSession.MaleBoneCache == null || _maleSession.MaleBoneCache.Length == 0)
            {
                Transform root = _maleSession.TargetMaleCha.objBodyBone != null
                    ? _maleSession.TargetMaleCha.objBodyBone.transform
                    : _maleSession.TargetMaleCha.transform;
                _maleSession.MaleBoneCache = root != null ? root.GetComponentsInChildren<Transform>(true) : null;
            }

            return _maleSession.MaleBoneCache;
        }

        private void UpdateExternalFollowTargets()
        {
            if (!VR.Active)
                return;

            GetOrCreateHmdFollowTarget();
        }

        private Transform GetOrCreateHmdFollowTarget()
        {
            if (!VR.Active)
                return null;

            _followHmdState.Target = ResolveVrHmdHeadTransform();
            return _followHmdState.Target;
        }

        private Transform ResolveVrHmdHeadTransform()
        {
            if (!VR.Active)
                return null;

            try
            {
                VRCamera vrCamera = VR.Camera;
                if (vrCamera == null)
                    return null;
                if (vrCamera.Head != null)
                    return vrCamera.Head;
                if (vrCamera.SteamCam != null && vrCamera.SteamCam.head != null)
                    return vrCamera.SteamCam.head;
            }
            catch (Exception ex)
            {
                LogDebug("[VRGrab] HMD head transform resolve failed: " + ex.GetType().Name + " " + ex.Message);
            }

            return null;
        }

        private void ClearExternalFollowTargets()
        {
            if (_followHmdState.TargetObject != null)
                Destroy(_followHmdState.TargetObject);
            _followHmdState.TargetObject = null;
            _followHmdState.Target = null;
        }

        private Transform GetLowerBoundaryBone(int ikIdx)
        {
            if (_femaleSession.FullBodyIk == null || _femaleSession.FullBodyIk.references == null)
                return null;

            var refs = _femaleSession.FullBodyIk.references;
            switch (ikIdx)
            {
                case BIK_LH: return refs.leftForearm != null ? refs.leftForearm : refs.leftHand;
                case BIK_RH: return refs.rightForearm != null ? refs.rightForearm : refs.rightHand;
                case BIK_LF: return refs.leftCalf != null ? refs.leftCalf : refs.leftFoot;
                case BIK_RF: return refs.rightCalf != null ? refs.rightCalf : refs.rightFoot;
                default: return null;
            }
        }

        private static bool IsSameOrDescendantOf(Transform candidate, Transform ancestor)
        {
            if (candidate == null || ancestor == null)
                return false;

            Transform t = candidate;
            while (t != null)
            {
                if (ReferenceEquals(t, ancestor))
                    return true;
                t = t.parent;
            }

            return false;
        }

        private static bool IsHeadOrAboveBone(Transform bone)
        {
            Transform t = bone;
            while (t != null)
            {
                string n = (t.name ?? string.Empty).ToLowerInvariant();
                if (IsHeadBoundaryBoneName(n))
                    return true;
                if (n.Contains("spine") || n.Contains("chest") || n.Contains("waist")
                    || n == "cf_j_root" || n == "cm_j_root")
                    return false;
                t = t.parent;
            }

            return false;
        }

        private static bool IsHeadBoundaryBoneName(string lowerBoneName)
        {
            if (string.IsNullOrEmpty(lowerBoneName))
                return false;

            return lowerBoneName.Contains("head")
                || lowerBoneName.Contains("neck")
                || lowerBoneName.Contains("face")
                || lowerBoneName.Contains("cheek")
                || lowerBoneName.Contains("jaw")
                || lowerBoneName.Contains("mouth")
                || lowerBoneName.Contains("nose")
                || lowerBoneName.Contains("eye")
                || lowerBoneName.Contains("brow")
                || lowerBoneName.Contains("ear");
        }

        private static bool IsFingerBoneName(string boneName)
        {
            string n = (boneName ?? string.Empty).ToLowerInvariant();
            return n.Contains("finger")
                || n.Contains("thumb")
                || n.Contains("index")
                || n.Contains("middle")
                || n.Contains("ring")
                || n.Contains("little")
                || n.Contains("yubi")
                || n.Contains("tang")
                || n.Contains("toes");
        }

        private static bool IsExcludedBoneName(string boneName)
        {
            string n = (boneName ?? string.Empty).ToLowerInvariant();
            if (n.Contains("cf_pv_"))
                return true;
            if (n.Contains("cf_j_sk_"))
                return true;
            if (n.Contains("cf_j_backsk_"))
                return true;
            if (n.Contains("cf_j_spinesk_"))
                return true;
            if (n.Contains("cf_j_bnip"))
                return true;
            if (n == "cf_j_root")
                return true;
            if (n == "cf_j_ana")
                return true;
            // _L_01 / _R_01 など、左右サフィックスの後に数字がつくサブボーンを除外
            if (System.Text.RegularExpressions.Regex.IsMatch(n, @"_[lr]_\d+$"))
                return true;
            return false;
        }

        private static bool IsLeftArmLowerBoneName(string boneName)
        {
            return IsLeftSideBoneName(boneName) && IsArmLowerBoneNameCommon(boneName);
        }

        private static bool IsRightArmLowerBoneName(string boneName)
        {
            return IsRightSideBoneName(boneName) && IsArmLowerBoneNameCommon(boneName);
        }

        private static bool IsLeftLegLowerBoneName(string boneName)
        {
            return IsLeftSideBoneName(boneName) && IsLegLowerBoneNameCommon(boneName);
        }

        private static bool IsRightLegLowerBoneName(string boneName)
        {
            return IsRightSideBoneName(boneName) && IsLegLowerBoneNameCommon(boneName);
        }

        private static bool IsArmLowerBoneNameCommon(string boneName)
        {
            string n = (boneName ?? string.Empty).ToLowerInvariant();
            return n.Contains("arm") || n.Contains("elbo") || n.Contains("elbow")
                || n.Contains("forearm") || n.Contains("wrist") || n.Contains("hand");
        }

        private static bool IsLegLowerBoneNameCommon(string boneName)
        {
            string n = (boneName ?? string.Empty).ToLowerInvariant();
            return n.Contains("knee") || n.Contains("leg") || n.Contains("ankle")
                || n.Contains("foot") || n.Contains("toe");
        }

        private static bool IsLeftSideBoneName(string boneName)
        {
            string u = (boneName ?? string.Empty).ToUpperInvariant();
            return u.EndsWith("_L", StringComparison.Ordinal) || u.Contains("_L_");
        }

        private static bool IsRightSideBoneName(string boneName)
        {
            string u = (boneName ?? string.Empty).ToUpperInvariant();
            return u.EndsWith("_R", StringComparison.Ordinal) || u.Contains("_R_");
        }

        private static bool IsCameraHelperBoneName(string boneName)
        {
            return !string.IsNullOrEmpty(boneName)
                && boneName.StartsWith("n_cam", StringComparison.Ordinal);
        }

        private void UpdateFollowBoneVisuals()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
            {
                BodyIkOperationPointState state = _bikEff[i];
                if (state == null)
                    continue;

                // ドラッグ中かつ追従未確定時のみ候補ボーン検索
                if (state.Running && CanUseBoneFollow(i) && state.Proxy != null && state.GizmoDragging && state.FollowBone == null)
                    state.CandidateBone = FindNearestBone(i, state.Proxy.position);
                else
                    state.CandidateBone = null;

                // 追従確定後は常時表示の邪魔になるため、候補表示だけ出す。
                Transform displayBone = state.FollowBone == null ? state.CandidateBone : null;
                bool shouldShow = state.Running && state.GizmoDragging && state.FollowBone == null && displayBone != null;

                UpdateBoneMarker(i, state, displayBone, shouldShow);
                UpdateFollowLine(i, state, displayBone, shouldShow);

                // ギズモ中央球の色: 追従確定中はシアン
                if (state.Gizmo != null)
                    state.Gizmo.SetFollowActive(state.FollowBone != null);
            }
        }

        private void UpdateBoneMarker(int idx, BodyIkOperationPointState state, Transform displayBone, bool shouldShow)
        {
            float markerSize = _settings != null ? _settings.BoneMarkerSize : 0.04f;

            if (shouldShow)
            {
                if (state.BoneMarkerGo == null)
                {
                    state.BoneMarkerGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    state.BoneMarkerGo.name = "__BoneMarker_" + idx;
                    state.BoneMarkerGo.hideFlags = HideFlags.HideAndDontSave;
                    Destroy(state.BoneMarkerGo.GetComponent<Collider>());
                    var mr = state.BoneMarkerGo.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        var mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                        mat.color = new Color(0.2f, 0.4f, 1f);
                        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                        mr.sharedMaterial = mat;
                    }
                    state.BoneMarkerGo.layer = VR.Active ? 0 : 31;
                }

                state.BoneMarkerGo.SetActive(true);
                state.BoneMarkerGo.transform.position = displayBone.position;
                state.BoneMarkerGo.transform.localScale = Vector3.one * markerSize;
            }
            else
            {
                if (state.BoneMarkerGo != null)
                    state.BoneMarkerGo.SetActive(false);
            }
        }

        private void UpdateFollowLine(int idx, BodyIkOperationPointState state, Transform displayBone, bool shouldShow)
        {
            if (shouldShow && state.Proxy != null)
            {
                if (state.FollowLine == null)
                {
                    var go = new GameObject("__FollowLine_" + idx);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    go.layer = VR.Active ? 0 : 31;
                    state.FollowLine = go.AddComponent<LineRenderer>();
                    state.FollowLine.useWorldSpace = true;
                    state.FollowLine.positionCount = 2;
                    state.FollowLine.startWidth = 0.008f;
                    state.FollowLine.endWidth = 0.008f;
                    state.FollowLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    state.FollowLine.receiveShadows = false;
                    var mat = new Material(Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color") ?? Shader.Find("Standard"));
                    if (mat.HasProperty("_Color"))
                        mat.SetColor("_Color", new Color(0.2f, 0.4f, 1f, 0.8f));
                    state.FollowLine.sharedMaterial = mat;
                    state.FollowLine.startColor = new Color(0.2f, 0.4f, 1f, 0.8f);
                    state.FollowLine.endColor = new Color(0.2f, 0.4f, 1f, 0.8f);
                }

                state.FollowLine.gameObject.SetActive(true);
                state.FollowLine.SetPosition(0, state.Proxy.position);
                state.FollowLine.SetPosition(1, displayBone.position);
            }
            else
            {
                if (state.FollowLine != null)
                    state.FollowLine.gameObject.SetActive(false);
            }
        }

        internal void DestroyFollowVisuals(int idx)
        {
            BodyIkOperationPointState state = _bikEff[idx];
            if (state == null) return;

            if (state.BoneMarkerGo != null)
            {
                var mr = state.BoneMarkerGo.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null)
                {
                    if (mr.sharedMaterial.mainTexture != null)
                        Destroy(mr.sharedMaterial.mainTexture);
                    Destroy(mr.sharedMaterial);
                }
                Destroy(state.BoneMarkerGo);
                state.BoneMarkerGo = null;
            }

            if (state.FollowLine != null)
            {
                if (state.FollowLine.sharedMaterial != null)
                    Destroy(state.FollowLine.sharedMaterial);
                Destroy(state.FollowLine.gameObject);
                state.FollowLine = null;
            }

            state.CandidateBone = null;
        }

        private void DrawBodyIkFollowSection()
        {
            DrawFollowExtensionsSection();

            GUILayout.Space(4f);
            GUILayout.Label("── IK追従設定 ──");

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("追従スナップ距離", "近傍追従でボーンにスナップする距離閾値（メートル）"), GUILayout.Width(100f));
            float snapDist = _settings.FollowSnapDistance;
            float nextSnapDist = GUILayout.HorizontalSlider(snapDist, 0.02f, 0.6f, GUILayout.Width(160f));
            GUILayout.Label(nextSnapDist.ToString("F2"), GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(snapDist, nextSnapDist))
            {
                _settings.FollowSnapDistance = nextSnapDist;
                SaveSettings();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("ボーンマーカーサイズ", "追従先ボーンを示すキューブマーカーのサイズ"), GUILayout.Width(120f));
            float markerSize = _settings.BoneMarkerSize;
            float nextMarkerSize = GUILayout.HorizontalSlider(markerSize, 0.01f, 0.15f, GUILayout.Width(160f));
            GUILayout.Label(nextMarkerSize.ToString("F2"), GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(markerSize, nextMarkerSize))
            {
                _settings.BoneMarkerSize = nextMarkerSize;
                SaveSettings();
            }

        }

        private void DrawBodyIkFollowRow(int idx)
        {
            if (!CanUseBoneFollow(idx))
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(16f);

            bool prevEnabled = GUI.enabled;
            GUI.enabled = _bikEff[idx].Running;
            if (GUILayout.Button(new GUIContent("近傍追従", "最も近い追従先（女/男ボーン + HMD）を自動検索してIKエフェクターを追従させる"), GUILayout.Width(72f)))
                TrySetNearestFollowBone(idx);

            GUI.enabled = _bikEff[idx].FollowBone != null;
            if (GUILayout.Button(new GUIContent("解除", "ボーン追従を解除してIKエフェクターを自由に動かせる状態に戻す"), GUILayout.Width(52f)))
                ClearBodyIKFollowBone(idx);

            // ミラーボタン（左手⇔右手、左足⇔右足のみ）
            if (GetMirrorIndex(idx) >= 0)
            {
                GUI.enabled = _bikEff[idx].FollowBone != null;
                if (GUILayout.Button(new GUIContent("ミラー", "現在のオフセットを反対側にコピー"), GUILayout.Width(52f)))
                    ApplyMirrorFollow(idx);
            }
            GUI.enabled = prevEnabled;

            string followName = _bikEff[idx].FollowBone != null ? _bikEff[idx].FollowBone.name : "-";
            GUILayout.Label(followName);
            GUILayout.EndHorizontal();

            DrawComposerFollowRow(idx);
        }

        // ── オフセット回転決定 ─────────────────────────────────────────────

        private Quaternion GetFollowOffsetRotation(Transform followBone)
        {
            if (followBone == null)
                return Quaternion.identity;

            string name = followBone.name ?? string.Empty;

            // HMD追従時 → HMD自身の回転を使う
            if (IsHmdFollowTarget(followBone))
            {
                LogDebug($"[FollowOffsetRot] HMD bone={name} rot={followBone.rotation.eulerAngles}");
                return followBone.rotation;
            }

            // ボーン追従時 → そのボーンが属するキャラのルート回転を使う
            // 男ボーンか判定
            if (_maleSession.TargetMaleCha != null && _maleSession.MaleBoneCache != null
                && IsBoneInCache(_maleSession.MaleBoneCache, followBone))
            {
                Quaternion rot = _maleSession.TargetMaleCha.transform.rotation;
                LogDebug($"[FollowOffsetRot] MALE bone={name} rootRot={rot.eulerAngles}");
                return rot;
            }

            // 女ボーンか判定（BoneCacheに存在するか確認）
            if (_femaleSession.Character != null && _femaleSession.BoneCache != null
                && IsBoneInCache(_femaleSession.BoneCache, followBone))
            {
                Quaternion rot = _femaleSession.Character.transform.rotation;
                LogDebug($"[FollowOffsetRot] FEMALE bone={name} rootRot={rot.eulerAngles}");
                return rot;
            }

            // フォールバック: ボーン自身の回転
            LogWarn($"[FollowOffsetRot] FALLBACK bone={name} — not in male or female cache, boneRot={followBone.rotation.eulerAngles}");
            return followBone.rotation;
        }

        // ── 追従拡張設定 UI ──────────────────────────────────────────────

        private void DrawFollowExtensionsSection()
        {
            GUILayout.Space(4f);
            GUILayout.Label("── 追従拡張 ──");

            // 距離閾値
            {
                bool distEnabled = GUILayout.Toggle(_settings.FollowDistanceThresholdEnabled,
                    new GUIContent("距離閾値で自動切断/復帰", "追従先が閾値距離を超えたら滑らかに切断、戻ったら復帰"));
                if (distEnabled != _settings.FollowDistanceThresholdEnabled)
                {
                    _settings.FollowDistanceThresholdEnabled = distEnabled;
                    SaveSettings();
                }

                if (_settings.FollowDistanceThresholdEnabled)
                {
                    float threshold = DrawSliderWithField("切断距離", _settings.FollowDistanceThreshold, 0.05f, 10f, "F2",
                        tooltip: "追従確定時の基準位置からこの距離まではウェイトを維持（メートル）");
                    if (!Mathf.Approximately(threshold, _settings.FollowDistanceThreshold))
                    {
                        _settings.FollowDistanceThreshold = threshold;
                        SaveSettings();
                    }

                    float fadeRange = DrawSliderWithField("完全切断まで", _settings.FollowDistanceFadeRange, 0.01f, 5f, "F2",
                        tooltip: "切断距離を超えてからウェイト0になるまでの追加距離（メートル）");
                    if (!Mathf.Approximately(fadeRange, _settings.FollowDistanceFadeRange))
                    {
                        _settings.FollowDistanceFadeRange = fadeRange;
                        SaveSettings();
                    }

                    float blendSpeed = DrawSliderWithField("ブレンド速度", _settings.FollowDistanceBlendSpeed, 0.5f, 20f, "F1",
                        tooltip: "切断/復帰時のブレンド速度（大きいほど速い）");
                    if (!Mathf.Approximately(blendSpeed, _settings.FollowDistanceBlendSpeed))
                    {
                        _settings.FollowDistanceBlendSpeed = blendSpeed;
                        SaveSettings();
                    }
                }
            }

            // VR時 頭ボーン→HMD変換
            {
                bool headToHmd = GUILayout.Toggle(_settings.FollowHeadBoneToHmdEnabled,
                    new GUIContent("VR時 頭ボーン→HMD変換", "追従先が男の頭ボーンの時、VRではHMD位置に自動差し替え"));
                if (headToHmd != _settings.FollowHeadBoneToHmdEnabled)
                {
                    _settings.FollowHeadBoneToHmdEnabled = headToHmd;
                    SaveSettings();
                }
            }

            // 首より上の候補を全許可（検証）
            {
                bool allowAllHead = GUILayout.Toggle(_settings.FollowAllowAllHeadBonesForSnap,
                    new GUIContent("首より上候補を全許可(検証)", "ON中は首より上の骨を除外せず近接追従の候補にする。OFFで従来挙動へ戻る"));
                if (allowAllHead != _settings.FollowAllowAllHeadBonesForSnap)
                {
                    _settings.FollowAllowAllHeadBonesForSnap = allowAllHead;
                    SaveSettings();
                }
            }
        }

        // ── 女の頭角度 UI ────────────────────────────────────────────────

        private void DrawFemaleHeadAngleSection()
        {
            GUILayout.Space(4f);
            GUILayout.Label("── 女の頭角度 ──");

            {
                bool enabled = GUILayout.Toggle(_settings.FemaleHeadAngleEnabled,
                    new GUIContent("頭角度スライダー有効", "デスクトップで女キャラの頭角度をスライダーで操作する"));
                if (enabled != _settings.FemaleHeadAngleEnabled)
                {
                    _settings.FemaleHeadAngleEnabled = enabled;
                    SaveSettings();
                }
            }

            if (_settings.FemaleHeadAngleEnabled)
            {
                float ax = DrawSliderWithField("X (上下)", _settings.FemaleHeadAngleX, -120f, 120f, "F1");
                if (!Mathf.Approximately(ax, _settings.FemaleHeadAngleX))
                {
                    _settings.FemaleHeadAngleX = ax;
                    SaveSettings();
                }

                float ay = DrawSliderWithField("Y (左右)", _settings.FemaleHeadAngleY, -120f, 120f, "F1");
                if (!Mathf.Approximately(ay, _settings.FemaleHeadAngleY))
                {
                    _settings.FemaleHeadAngleY = ay;
                    SaveSettings();
                }

                float az = DrawSliderWithField("Z (傾き)", _settings.FemaleHeadAngleZ, -120f, 120f, "F1");
                if (!Mathf.Approximately(az, _settings.FemaleHeadAngleZ))
                {
                    _settings.FemaleHeadAngleZ = az;
                    SaveSettings();
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("リセット", GUILayout.Width(72f)))
                {
                    _settings.FemaleHeadAngleX = 0f;
                    _settings.FemaleHeadAngleY = 0f;
                    _settings.FemaleHeadAngleZ = 0f;
                    SaveSettings();
                }

                bool gizmo = GUILayout.Toggle(_settings.FemaleHeadAngleGizmoVisible,
                    new GUIContent("ギズモ表示", "頭の回転ギズモを表示"));
                if (gizmo != _settings.FemaleHeadAngleGizmoVisible)
                {
                    _settings.FemaleHeadAngleGizmoVisible = gizmo;
                    SaveSettings();
                }
                GUILayout.EndHorizontal();

                bool keepOnContextChange = GUILayout.Toggle(
                    _settings.FemaleHeadAngleKeepOnMotionOrPostureChange,
                    new GUIContent("体位/モーション変更後も維持", "OFF時は体位やモーションの変化で頭角度をリセット"));
                if (keepOnContextChange != _settings.FemaleHeadAngleKeepOnMotionOrPostureChange)
                {
                    _settings.FemaleHeadAngleKeepOnMotionOrPostureChange = keepOnContextChange;
                    SaveSettings();
                }

                // トリガーフェード時間
                float fadeTime = DrawSliderWithField("フェード時間(秒)", _settings.FemaleHeadOffsetFadeTime, 0.1f, 5f, "F2",
                    tooltip: "NeckLookトリガー発火時にオフセットを消すまでの時間");
                if (!Mathf.Approximately(fadeTime, _settings.FemaleHeadOffsetFadeTime))
                {
                    _settings.FemaleHeadOffsetFadeTime = fadeTime;
                    SaveSettings();
                }

                // イージング選択
                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("イージング", "フェードの加速カーブ"), GUILayout.Width(130f));
                string[] easingNames = { "Linear", "EaseIn", "EaseOut", "EaseInOut" };
                for (int i = 0; i < easingNames.Length; i++)
                {
                    bool selected = _settings.FemaleHeadOffsetEasing == i;
                    bool next = GUILayout.Toggle(selected, easingNames[i], "Button", GUILayout.Width(72f));
                    if (next && !selected)
                    {
                        _settings.FemaleHeadOffsetEasing = i;
                        SaveSettings();
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        // ── ミラーロジック ──────────────────────────────────────────

        private void ApplyMirrorFollow(int sourceIdx)
        {
            if (_settings == null)
                return;

            int mirrorIdx = GetMirrorIndex(sourceIdx);
            if (mirrorIdx < 0)
                return;

            Transform sourceBone = _bikEff[sourceIdx].FollowBone;
            if (sourceBone == null)
                return;

            // IKが有効でなければ有効化
            if (!_bikEff[mirrorIdx].Running)
            {
                SetBodyIK(mirrorIdx, on: true, saveSettings: false, reason: "mirror-auto");
            }

            string boneName = sourceBone.name ?? string.Empty;
            Transform mirrorBone = FindMirrorBone(boneName, sourceBone);

            // 位置オフセットX反転（左右対称）
            var posOff = _bikEff[sourceIdx].FollowBonePositionOffset;
            posOff.x = -posOff.x;

            // 回転オフセットの鏡像化（Y,Z成分を反転 = X軸ミラー）
            var rotOff = _bikEff[sourceIdx].FollowBoneRotationOffset;
            rotOff.y = -rotOff.y;
            rotOff.z = -rotOff.z;

            if (mirrorBone != null)
            {
                // L/R対称ボーンがある → 反対側ボーンを追従先にセット
                _bikEff[mirrorIdx].FollowBone = mirrorBone;
                _bikEff[mirrorIdx].CandidateBone = null;
                _bikEff[mirrorIdx].FollowBonePositionOffset = posOff;
                _bikEff[mirrorIdx].FollowBoneRotationOffset = rotOff;
            }
            else
            {
                // 対称ボーンなし（頭など）→ 同じボーンでオフセットX反転
                _bikEff[mirrorIdx].FollowBone = sourceBone;
                _bikEff[mirrorIdx].CandidateBone = null;
                _bikEff[mirrorIdx].FollowBonePositionOffset = posOff;
                _bikEff[mirrorIdx].FollowBoneRotationOffset = rotOff;
            }

            string mirrorCacheType = "UNKNOWN";
            if (_maleSession.MaleBoneCache != null && IsBoneInCache(_maleSession.MaleBoneCache, _bikEff[mirrorIdx].FollowBone))
                mirrorCacheType = "MALE";
            else if (_femaleSession.BoneCache != null && IsBoneInCache(_femaleSession.BoneCache, _bikEff[mirrorIdx].FollowBone))
                mirrorCacheType = "FEMALE";
            var mpo = _bikEff[mirrorIdx].FollowBonePositionOffset;
            var mro = _bikEff[mirrorIdx].FollowBoneRotationOffset;
            var spo = _bikEff[sourceIdx].FollowBonePositionOffset;
            var sro = _bikEff[sourceIdx].FollowBoneRotationOffset;
            var mbp = _bikEff[mirrorIdx].FollowBone.position;
            LogInfo($"[Mirror] {sourceIdx} -> {mirrorIdx} bone={_bikEff[mirrorIdx].FollowBone.name} cache={mirrorCacheType} posOffset=({mpo.x:F4},{mpo.y:F4},{mpo.z:F4}) srcPosOffset=({spo.x:F4},{spo.y:F4},{spo.z:F4}) rotQ=({mro.x:F4},{mro.y:F4},{mro.z:F4},{mro.w:F4}) srcRotQ=({sro.x:F4},{sro.y:F4},{sro.z:F4},{sro.w:F4}) mirrorBonePos=({mbp.x:F4},{mbp.y:F4},{mbp.z:F4})");
        }

        private static int GetMirrorIndex(int idx)
        {
            switch (idx)
            {
                case BIK_LH: return BIK_RH;
                case BIK_RH: return BIK_LH;
                case BIK_LF: return BIK_RF;
                case BIK_RF: return BIK_LF;
                default: return -1;
            }
        }

        private Transform FindMirrorBone(string boneName, Transform sourceBone)
        {
            if (string.IsNullOrEmpty(boneName))
                return null;

            string mirrorName = null;
            if (boneName.Contains("_L"))
                mirrorName = boneName.Replace("_L", "_R");
            else if (boneName.Contains("_R"))
                mirrorName = boneName.Replace("_R", "_L");

            if (mirrorName == null || mirrorName == boneName)
                return null;

            // ソースボーンがどちらのキャッシュに属するか判定し、同じキャッシュから検索
            Transform[] sourceCache = DetermineBoneCache(sourceBone);
            if (sourceCache != null)
                return FindBoneByName(sourceCache, mirrorName);

            // フォールバック: 両方検索
            Transform found = FindBoneByName(_femaleSession.BoneCache, mirrorName);
            if (found != null) return found;
            return FindBoneByName(_maleSession.MaleBoneCache, mirrorName);
        }

        private Transform[] DetermineBoneCache(Transform bone)
        {
            if (bone == null) return null;

            if (_maleSession.MaleBoneCache != null && IsBoneInCache(_maleSession.MaleBoneCache, bone))
                return _maleSession.MaleBoneCache;
            if (_femaleSession.BoneCache != null && IsBoneInCache(_femaleSession.BoneCache, bone))
                return _femaleSession.BoneCache;

            return null;
        }

        private static bool IsBoneInCache(Transform[] cache, Transform bone)
        {
            for (int i = 0; i < cache.Length; i++)
            {
                if (ReferenceEquals(cache[i], bone))
                    return true;
            }
            return false;
        }

        private static Transform FindBoneByName(Transform[] cache, string name)
        {
            if (cache == null) return null;
            for (int i = 0; i < cache.Length; i++)
            {
                if (cache[i] != null && cache[i].name == name)
                    return cache[i];
            }
            return null;
        }

        // ── 距離閾値ロジック ─────────────────────────────────────────────

        private void UpdateFollowDistanceThreshold()
        {
            if (_settings == null)
                return;
            if (!_settings.FollowDistanceThresholdEnabled)
            {
                RestoreRuntimeBodyIKWeightsToConfigured();
                return;
            }

            float dt = Time.unscaledDeltaTime;
            float threshold = Mathf.Max(0.05f, _settings.FollowDistanceThreshold);
            float fadeRange = Mathf.Max(0.01f, _settings.FollowDistanceFadeRange);
            float speed = Mathf.Max(0.5f, _settings.FollowDistanceBlendSpeed);

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                float configuredWeight = GetConfiguredBodyIKWeight(i);
                BodyIkOperationPointState state = _bikEff[i];
                if (state == null || !state.Running || state.FollowBone == null)
                {
                    ClearFollowDistanceBaseline(i);
                    RestoreRuntimeBodyIKWeightToConfigured(i, configuredWeight);
                    continue;
                }
                if (state.PendingFollowRebind || state.Proxy == null)
                {
                    RestoreRuntimeBodyIKWeightToConfigured(i, configuredWeight);
                    continue;
                }

                Vector3 currentTargetPos = GetFollowTargetPosition(i);
                if (!state.HasFollowDistanceBaseline)
                {
                    state.FollowDistanceBaselineTargetPos = currentTargetPos;
                    state.HasFollowDistanceBaseline = true;
                    RestoreRuntimeBodyIKWeightToConfigured(i, configuredWeight);
                    continue;
                }

                float dist = Vector3.Distance(currentTargetPos, state.FollowDistanceBaselineTargetPos);
                float excess = Mathf.Max(0f, dist - threshold);
                float cutT = Mathf.Clamp01(excess / fadeRange);

                float current = GetBodyIKWeight(i);
                float target = Mathf.Lerp(configuredWeight, 0f, cutT);
                if (cutT > 0f && current > target + 0.001f)
                    StopPoseTransitionIfRunning();

                float next = Mathf.MoveTowards(current, target, speed * dt);
                if (!Mathf.Approximately(current, next))
                    SetBodyIKWeight(i, next, saveSettings: false, updateSettings: false);
            }
        }

        private void RestoreRuntimeBodyIKWeightsToConfigured()
        {
            for (int i = 0; i < BIK_TOTAL; i++)
                RestoreRuntimeBodyIKWeightToConfigured(i, GetConfiguredBodyIKWeight(i));
        }

        private void RestoreRuntimeBodyIKWeightToConfigured(int idx, float configuredWeight)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;
            if (!Mathf.Approximately(GetBodyIKWeight(idx), configuredWeight))
                SetBodyIKWeight(idx, configuredWeight, saveSettings: false, updateSettings: false);
        }

        private Vector3 GetFollowTargetPosition(int idx)
        {
            Transform followBone = _bikEff[idx].FollowBone;
            Quaternion offsetRot = GetFollowOffsetRotation(followBone);
            return followBone.position + (offsetRot * _bikEff[idx].FollowBonePositionOffset);
        }

        private void SetFollowDistanceBaseline(int idx)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;
            BodyIkOperationPointState state = _bikEff[idx];
            if (state == null || state.FollowBone == null)
                return;

            state.FollowDistanceBaselineTargetPos = GetFollowTargetPosition(idx);
            state.HasFollowDistanceBaseline = true;
        }

        private void ClearFollowDistanceBaseline(int idx)
        {
            if (idx < 0 || idx >= BIK_TOTAL)
                return;
            BodyIkOperationPointState state = _bikEff[idx];
            if (state == null)
                return;

            state.HasFollowDistanceBaseline = false;
            state.FollowDistanceBaselineTargetPos = Vector3.zero;
        }

        private bool IsHmdFollowTarget(Transform followBone)
        {
            if (followBone == null)
                return false;
            if (_followHmdState.Target != null && ReferenceEquals(followBone, _followHmdState.Target))
                return true;
            if (VR.Active)
            {
                Transform currentHmd = ResolveVrHmdHeadTransform();
                if (currentHmd != null && ReferenceEquals(followBone, currentHmd))
                {
                    _followHmdState.Target = currentHmd;
                    return true;
                }
            }
            return string.Equals(followBone.name, FollowHmdTargetName, StringComparison.Ordinal);
        }

        // ── VR時 頭ボーン→HMD変換 ───────────────────────────────────────

        private void ApplyHeadBoneToHmdConversion()
        {
            if (_settings == null || !_settings.FollowHeadBoneToHmdEnabled)
                return;
            if (!VR.Active)
                return;

            Transform hmd = GetOrCreateHmdFollowTarget();
            if (hmd == null)
                return;

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                if (!_bikEff[i].Running || _bikEff[i].FollowBone == null)
                    continue;

                string name = _bikEff[i].FollowBone.name ?? string.Empty;
                if (name == "cm_j_head" || name == "cf_j_head")
                {
                    _bikEff[i].FollowBone = hmd;
                }
            }
        }

        private void DrawComposerFollowRow(int idx)
        {
            if (!_objectComposerGateway.IsAvailable || _settings == null)
                return;
            if (_settings.FollowComposerEnabled == null || _settings.FollowComposerObjectIds == null)
                return;
            if (idx < 0 || idx >= _settings.FollowComposerEnabled.Length)
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Space(16f);

            bool wasOn = _settings.FollowComposerEnabled[idx];
            bool isOn = GUILayout.Toggle(wasOn, new GUIContent("Composer連動",
                "MainGameObjectComposer のオブジェクトに追従する（Composer 側 UI でオブジェクトを選択）"),
                GUILayout.Width(110f));
            if (isOn != wasOn)
            {
                _settings.FollowComposerEnabled[idx] = isOn;
                if (!isOn)
                    _settings.FollowComposerObjectIds[idx] = null;
                SaveSettings();
            }

            string id = _settings.FollowComposerObjectIds[idx];
            string label = string.IsNullOrEmpty(id)
                ? "(未設定)"
                : _objectComposerGateway.GetObjectName(id) ?? id;
            GUILayout.Label("target: " + label);
            GUILayout.EndHorizontal();
        }

        private void UpdateComposerFollowTargets()
        {
            _composerFollowController.UpdateTargets(
                _settings,
                _bikEff,
                _objectComposerGateway,
                BIK_TOTAL);
        }
    }
}
