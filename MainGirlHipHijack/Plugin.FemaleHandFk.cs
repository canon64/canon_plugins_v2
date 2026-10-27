using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MainGirlHipHijack.Runtime;
using MainGirlHipHijack.Services;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private const int FemaleHandFkLayerCount = 3;

        private readonly FemaleHandFkStateStore _femaleHandFkState =
            new FemaleHandFkStateStore(2, FemaleHandFkLayerCount);
        private FemaleHandFkRuntimeState[] _femaleHandFk => _femaleHandFkState.Hands;

        private void DrawFemaleHandFkSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("女性手FKブレンド");
            DrawFemaleHandFkHand(FemaleHandLeft, "左手FK");
            DrawFemaleHandFkHand(FemaleHandRight, "右手FK");
        }

        private void DrawFemaleHandFkHand(int hand, string label)
        {
            FemaleHandFkSettings handSettings = GetFemaleHandFkSettings(hand);
            if (handSettings == null)
                return;

            GUILayout.BeginVertical("box");
            GUILayout.BeginHorizontal();
            bool nextEnabled = GUILayout.Toggle(handSettings.Enabled, label, GUILayout.Width(84f));
            if (nextEnabled != handSettings.Enabled)
            {
                handSettings.Enabled = nextEnabled;
                if (nextEnabled)
                    QueueCaptureAllEnabledFemaleHandFkLayers(hand);
                else
                    ResetFemaleHandFkRuntime(hand);
                SaveSettings();
            }

            if (GUILayout.Button("リセット", GUILayout.Width(66f)))
                ResetFemaleHandFkHand(hand, saveSettings: true);

            GUILayout.Label("base " + GetFemaleHandFkBaseWeight(handSettings).ToString("F2"), GUILayout.Width(70f));
            GUILayout.Label(GetFemaleHandFkStatus(hand), GUILayout.Width(160f));
            GUILayout.EndHorizontal();

            for (int i = 0; i < FemaleHandFkLayerCount; i++)
                DrawFemaleHandFkLayer(hand, i, handSettings.Layers[i]);

            GUILayout.EndVertical();
        }

        private void DrawFemaleHandFkLayer(int hand, int layer, FemaleHandFkLayerSettings layerSettings)
        {
            if (layerSettings == null)
                return;

            FemaleHandFkRuntimeState runtime = _femaleHandFk[hand];
            bool captured = runtime != null
                && runtime.LayerCaptured != null
                && layer >= 0
                && layer < runtime.LayerCaptured.Length
                && runtime.LayerCaptured[layer];

            GUILayout.BeginHorizontal();
            bool nextEnabled = GUILayout.Toggle(layerSettings.Enabled, ((char)('A' + layer)).ToString(), GUILayout.Width(36f));
            if (nextEnabled != layerSettings.Enabled)
            {
                layerSettings.Enabled = nextEnabled;
                if (nextEnabled)
                    QueueCaptureFemaleHandFkLayer(hand, layer);
                SaveSettings();
            }

            GUI.enabled = _femaleSession.Character != null;
            if (GUILayout.Button("<<", GUILayout.Width(32f)))
                StepFemaleHandFkLayerIndex(hand, layer, -10);
            if (GUILayout.Button("<", GUILayout.Width(26f)))
                StepFemaleHandFkLayerIndex(hand, layer, -1);
            GUILayout.Label(layerSettings.Index.ToString(), GUILayout.Width(34f));
            if (GUILayout.Button(">", GUILayout.Width(26f)))
                StepFemaleHandFkLayerIndex(hand, layer, 1);
            if (GUILayout.Button(">>", GUILayout.Width(32f)))
                StepFemaleHandFkLayerIndex(hand, layer, 10);
            if (GUILayout.Button("取得", GUILayout.Width(42f)))
                QueueCaptureFemaleHandFkLayer(hand, layer);
            GUI.enabled = true;

            GUILayout.Label("W", GUILayout.Width(16f));
            float nextWeight = GUILayout.HorizontalSlider(layerSettings.Weight, 0f, 1f, GUILayout.Width(100f));
            GUILayout.Label(nextWeight.ToString("F2"), GUILayout.Width(36f));
            if (!Mathf.Approximately(nextWeight, layerSettings.Weight))
            {
                layerSettings.Weight = Mathf.Clamp01(nextWeight);
                SaveSettings();
            }

            GUILayout.Label(captured ? "FK" : "-", GUILayout.Width(24f));
            GUILayout.EndHorizontal();
        }

        private FemaleHandFkSettings GetFemaleHandFkSettings(int hand)
        {
            if (_settings == null)
                return null;
            return hand == FemaleHandLeft ? _settings.FemaleLeftHandFk : _settings.FemaleRightHandFk;
        }

        private void StepFemaleHandFkLayerIndex(int hand, int layer, int delta)
        {
            FemaleHandFkSettings handSettings = GetFemaleHandFkSettings(hand);
            if (handSettings == null || handSettings.Layers == null || layer < 0 || layer >= handSettings.Layers.Length)
                return;

            int maxCount = GetFemaleHandPoseMaxCount();
            FemaleHandFkLayerSettings layerSettings = handSettings.Layers[layer];
            int next = layerSettings.Index + delta;
            if (maxCount > 0)
            {
                while (next < 0)
                    next += maxCount;
                while (next >= maxCount)
                    next -= maxCount;
            }
            else
            {
                next = Mathf.Max(0, next);
            }

            layerSettings.Index = next;
            layerSettings.Enabled = next != 0;
            handSettings.Enabled = true;
            SaveSettings();
        }

        private void QueueCaptureAllEnabledFemaleHandFkLayers(int hand)
        {
            FemaleHandFkSettings handSettings = GetFemaleHandFkSettings(hand);
            if (handSettings == null || handSettings.Layers == null)
                return;

            for (int i = 0; i < handSettings.Layers.Length; i++)
            {
                FemaleHandFkLayerSettings layer = handSettings.Layers[i];
                if (layer != null && layer.Enabled && layer.Index != 0)
                    QueueCaptureFemaleHandFkLayer(hand, i);
            }
        }

        private void QueueCaptureFemaleHandFkLayer(int hand, int layer)
        {
            FemaleHandFkSettings handSettings = GetFemaleHandFkSettings(hand);
            if (handSettings != null && handSettings.Layers != null && layer >= 0 && layer < handSettings.Layers.Length)
            {
                handSettings.Enabled = true;
                if (handSettings.Layers[layer] != null)
                {
                    handSettings.Layers[layer].Enabled = true;
                    if (handSettings.Layers[layer].Weight <= 0.0001f)
                        handSettings.Layers[layer].Weight = 1f;
                }
                SaveSettings();
            }

            if (hand >= 0 && hand < _femaleHandFk.Length)
                _femaleHandFk[hand].Status = "capture queued";
            StartCoroutine(CaptureFemaleHandFkLayerCoroutine(hand, layer));
        }

        private IEnumerator CaptureFemaleHandFkLayerCoroutine(int hand, int layer)
        {
            if (_settings == null || _femaleSession.Character == null)
            {
                SetFemaleHandFkStatus(hand, "no target");
                yield break;
            }

            FemaleHandFkSettings handSettings = GetFemaleHandFkSettings(hand);
            if (handSettings == null || handSettings.Layers == null || layer < 0 || layer >= handSettings.Layers.Length)
            {
                SetFemaleHandFkStatus(hand, "bad layer");
                yield break;
            }

            FemaleHandFkLayerSettings layerSettings = handSettings.Layers[layer];
            if (layerSettings == null || layerSettings.Index == 0)
            {
                SetFemaleHandFkStatus(hand, "index 0");
                yield break;
            }

            ChaControl cha = _femaleSession.Character;
            int maxCount = Mathf.Max(0, cha.GetShapeIndexCount());
            int index = maxCount > 0 ? Mathf.Clamp(layerSettings.Index, 0, maxCount - 1) : Mathf.Max(0, layerSettings.Index);

            EnsureFemaleHandFkRuntime(hand, cha);
            if (_femaleHandFk[hand].Bones.Length == 0)
            {
                SetFemaleHandFkStatus(hand, "bones=0");
                LogWarn("[FemaleHandFK] no finger bones hand=" + hand);
                yield break;
            }

            try
            {
                cha.SetShapeHandValue(hand, index, 0, 0f);
                cha.SetEnableShapeHand(hand, true);
                SetFemaleHandFkStatus(hand, "shape " + index);
            }
            catch (System.Exception ex)
            {
                SetFemaleHandFkStatus(hand, "shape err");
                LogWarn("[FemaleHandFK] shape apply failed hand=" + hand + " index=" + index + " error=" + ex.Message);
                yield break;
            }

            yield return null;
            yield return new WaitForEndOfFrame();

            float maxDelta = CaptureFemaleHandFkLayerRotations(hand, layer);
            if (maxDelta < 0.25f)
            {
                FemaleHandFkRuntimeState runtime = _femaleHandFk[hand];
                if (runtime.LayerCaptured != null && layer >= 0 && layer < runtime.LayerCaptured.Length)
                    runtime.LayerCaptured[layer] = false;
                if (runtime.LayerRotations != null && layer >= 0 && layer < runtime.LayerRotations.Length)
                    runtime.LayerRotations[layer] = null;
                layerSettings.Enabled = false;
                SaveSettings();
                SetFemaleHandFkStatus(hand, "no bone delta");
                LogWarn("[FemaleHandFK] capture rejected: no bone delta hand=" + hand + " layer=" + layer + " index=" + index + " bones=" + _femaleHandFk[hand].Bones.Length);
                try
                {
                    cha.SetEnableShapeHand(hand, false);
                }
                catch
                {
                    // ignore
                }
                yield break;
            }

            try
            {
                cha.SetEnableShapeHand(hand, false);
            }
            catch
            {
                // ignore
            }

            ApplyFemaleHandFkBlend(hand);
            LogInfo("[FemaleHandFK] captured hand=" + hand + " layer=" + layer + " index=" + index + " bones=" + _femaleHandFk[hand].Bones.Length + " maxDelta=" + maxDelta.ToString("F3"));
            SetFemaleHandFkStatus(hand, "captured bones=" + _femaleHandFk[hand].Bones.Length + " d=" + maxDelta.ToString("F1"));
        }

        private float CaptureFemaleHandFkLayerRotations(int hand, int layer)
        {
            FemaleHandFkRuntimeState runtime = _femaleHandFk[hand];
            if (runtime == null || runtime.Bones == null || runtime.Bones.Length == 0)
                return 0f;

            var rotations = new Quaternion[runtime.Bones.Length];
            float maxDelta = 0f;
            for (int i = 0; i < runtime.Bones.Length; i++)
            {
                rotations[i] = runtime.Bones[i] != null ? runtime.Bones[i].localRotation : Quaternion.identity;
                if (runtime.BaseRotations != null && i < runtime.BaseRotations.Length)
                    maxDelta = Mathf.Max(maxDelta, Quaternion.Angle(runtime.BaseRotations[i], rotations[i]));
            }

            runtime.LayerRotations[layer] = rotations;
            runtime.LayerCaptured[layer] = true;
            return maxDelta;
        }

        private void EnsureFemaleHandFkRuntime(int hand, ChaControl cha)
        {
            if (cha == null)
                return;

            FemaleHandFkRuntimeState runtime = _femaleHandFk[hand];
            int chaId = cha.GetInstanceID();
            if (runtime.TargetChaId == chaId && runtime.Bones != null && runtime.Bones.Length > 0)
                return;

            Transform handBone = FindFemaleHandBone(hand, cha);
            Transform[] bones = handBone != null ? CollectFingerBones(handBone, hand) : new Transform[0];
            runtime.TargetChaId = chaId;
            runtime.Bones = bones;
            runtime.BaseRotations = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                runtime.BaseRotations[i] = bones[i] != null ? bones[i].localRotation : Quaternion.identity;
            runtime.LayerRotations = new Quaternion[FemaleHandFkLayerCount][];
            runtime.LayerCaptured = new bool[FemaleHandFkLayerCount];
            runtime.Status = "bones=" + bones.Length;
            LogInfo("[FemaleHandFK] runtime hand=" + hand + " handBone=" + (handBone != null ? handBone.name : "(null)") + " bones=" + bones.Length);
        }

        private Transform FindFemaleHandBone(int hand, ChaControl cha)
        {
            Transform[] cache = _femaleSession.BoneCache;
            if ((cache == null || cache.Length == 0) && cha != null)
            {
                Transform root = cha.animBody != null ? cha.animBody.transform : cha.transform;
                cache = root != null ? root.GetComponentsInChildren<Transform>(true) : null;
            }
            if (cache == null || cache.Length == 0)
                return null;

            string suffix = hand == FemaleHandLeft ? "_L" : "_R";
            string exact1 = hand == FemaleHandLeft ? "cf_j_hand_L" : "cf_j_hand_R";
            string exact2 = hand == FemaleHandLeft ? "cf_J_Hand_L" : "cf_J_Hand_R";
            string sideWord = hand == FemaleHandLeft ? "left" : "right";
            Transform fallback = null;
            for (int i = 0; i < cache.Length; i++)
            {
                Transform bone = cache[i];
                if (bone == null || string.IsNullOrEmpty(bone.name))
                    continue;

                string stripped = StripWorkSuffix(bone.name);
                if (string.Equals(stripped, exact1, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(stripped, exact2, System.StringComparison.OrdinalIgnoreCase))
                    return bone;
            }

            for (int i = 0; i < cache.Length; i++)
            {
                Transform bone = cache[i];
                if (bone == null || string.IsNullOrEmpty(bone.name))
                    continue;

                string lower = bone.name.ToLowerInvariant();
                bool side = bone.name.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase)
                    || lower.Contains(sideWord);
                if (!side)
                    continue;

                if (lower.Contains("cf_j_hand") || lower.Contains("cm_j_hand"))
                    return bone;
                if (fallback == null && lower.Contains("hand") && !lower.Contains("cf_t_") && !lower.Contains("cm_t_"))
                    fallback = bone;
                if (fallback == null && lower.Contains("wrist"))
                    fallback = bone;
            }

            return fallback;
        }

        private static Transform[] CollectFingerBones(Transform handBone, int hand)
        {
            if (handBone == null)
                return new Transform[0];

            string suffix = hand == FemaleHandLeft ? "_l" : "_r";
            var result = new List<Transform>();
            Transform[] all = handBone.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform bone = all[i];
                if (bone == null || ReferenceEquals(bone, handBone) || string.IsNullOrEmpty(bone.name))
                    continue;

                string lower = bone.name.ToLowerInvariant();
                bool finger = lower.Contains("finger")
                    || lower.Contains("thumb")
                    || lower.Contains("index")
                    || lower.Contains("middle")
                    || lower.Contains("ring")
                    || lower.Contains("little")
                    || lower.Contains("f_")
                    || lower.Contains("_oya")
                    || lower.Contains("_hito")
                    || lower.Contains("_naka")
                    || lower.Contains("_kusu")
                    || lower.Contains("_ko")
                    || lower.Contains("kusu")
                    || lower.Contains("ko")
                    || lower.Contains("hito")
                    || lower.Contains("naka")
                    || lower.Contains("oya");
                bool side = lower.EndsWith(suffix)
                    || lower.Contains(suffix + "_")
                    || lower.Contains((hand == FemaleHandLeft ? "left" : "right"));
                if (finger && side)
                    result.Add(bone);
            }

            return result.ToArray();
        }

        private void ApplyFemaleHandFkBlends()
        {
            ApplyFemaleHandFkBlend(FemaleHandLeft);
            ApplyFemaleHandFkBlend(FemaleHandRight);
        }

        private void ApplyFemaleHandFkBlend(int hand)
        {
            FemaleHandFkSettings handSettings = GetFemaleHandFkSettings(hand);
            if (handSettings == null || !handSettings.Enabled || handSettings.Layers == null)
                return;
            if (_femaleSession.Character == null)
                return;

            EnsureFemaleHandFkRuntime(hand, _femaleSession.Character);
            FemaleHandFkRuntimeState runtime = _femaleHandFk[hand];
            if (runtime == null || runtime.Bones == null || runtime.Bones.Length == 0)
                return;

            try
            {
                _femaleSession.Character.SetEnableShapeHand(hand, false);
            }
            catch
            {
                // ignore
            }

            float total = 0f;
            for (int i = 0; i < handSettings.Layers.Length; i++)
            {
                FemaleHandFkLayerSettings layer = handSettings.Layers[i];
                if (layer == null || !layer.Enabled || layer.Weight <= 0f || runtime.LayerCaptured == null || !runtime.LayerCaptured[i])
                    continue;
                total += Mathf.Clamp01(layer.Weight);
            }

            if (total <= 0.0001f)
                return;

            for (int boneIndex = 0; boneIndex < runtime.Bones.Length; boneIndex++)
            {
                Transform bone = runtime.Bones[boneIndex];
                if (bone == null)
                    continue;

                Quaternion result = runtime.BaseRotations != null && boneIndex < runtime.BaseRotations.Length
                    ? runtime.BaseRotations[boneIndex]
                    : bone.localRotation;
                float used = Mathf.Max(0f, 1f - total);
                for (int layerIndex = 0; layerIndex < handSettings.Layers.Length; layerIndex++)
                {
                    FemaleHandFkLayerSettings layer = handSettings.Layers[layerIndex];
                    Quaternion[] rotations = runtime.LayerRotations != null && layerIndex < runtime.LayerRotations.Length
                        ? runtime.LayerRotations[layerIndex]
                        : null;
                    if (layer == null || !layer.Enabled || layer.Weight <= 0f || rotations == null || boneIndex >= rotations.Length)
                        continue;

                    float weight = total > 1f ? Mathf.Clamp01(layer.Weight) / total : Mathf.Clamp01(layer.Weight);
                    BlendQuaternionInto(ref result, ref used, rotations[boneIndex], weight);
                }

                bone.localRotation = result;
            }
        }

        private static void BlendQuaternionInto(ref Quaternion current, ref float used, Quaternion target, float weight)
        {
            if (weight <= 0f)
                return;

            float nextUsed = used + weight;
            float t = nextUsed > 0.0001f ? weight / nextUsed : 1f;
            current = Quaternion.Slerp(current, target, Mathf.Clamp01(t));
            used = nextUsed;
        }

        private float GetFemaleHandFkBaseWeight(FemaleHandFkSettings handSettings)
        {
            if (handSettings == null || handSettings.Layers == null)
                return 1f;

            float total = 0f;
            for (int i = 0; i < handSettings.Layers.Length; i++)
            {
                FemaleHandFkLayerSettings layer = handSettings.Layers[i];
                if (layer != null && layer.Enabled)
                    total += Mathf.Clamp01(layer.Weight);
            }
            return Mathf.Clamp01(1f - total);
        }

        private void ResetFemaleHandFkHand(int hand, bool saveSettings)
        {
            FemaleHandFkSettings handSettings = GetFemaleHandFkSettings(hand);
            if (handSettings != null)
            {
                handSettings.Enabled = false;
                if (handSettings.Layers != null)
                {
                    for (int i = 0; i < handSettings.Layers.Length; i++)
                    {
                        if (handSettings.Layers[i] == null)
                            continue;
                        handSettings.Layers[i].Enabled = false;
                        handSettings.Layers[i].Weight = 0f;
                    }
                }
            }

            ResetFemaleHandFkRuntime(hand);
            if (saveSettings)
                SaveSettings();
        }

        private void ResetFemaleHandFkRuntime(int hand)
        {
            if (hand < 0 || hand >= _femaleHandFk.Length)
                return;

            FemaleHandFkRuntimeState runtime = _femaleHandFk[hand];
            if (runtime == null || runtime.Bones == null || runtime.BaseRotations == null)
                return;

            for (int i = 0; i < runtime.Bones.Length && i < runtime.BaseRotations.Length; i++)
            {
                if (runtime.Bones[i] != null)
                    runtime.Bones[i].localRotation = runtime.BaseRotations[i];
            }
        }

        private void ResetFemaleHandFkRuntimeAll()
        {
            ResetFemaleHandFkRuntime(FemaleHandLeft);
            ResetFemaleHandFkRuntime(FemaleHandRight);
        }

        private void SetFemaleHandFkStatus(int hand, string status)
        {
            if (hand < 0 || hand >= _femaleHandFk.Length)
                return;
            _femaleHandFk[hand].Status = status ?? "-";
        }

        private string GetFemaleHandFkStatus(int hand)
        {
            if (hand < 0 || hand >= _femaleHandFk.Length || _femaleHandFk[hand] == null)
                return "-";
            return _femaleHandFk[hand].Status ?? "-";
        }
    }
}
