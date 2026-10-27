using System;
using System.Collections.Generic;
using UnityEngine;
using MainGirlHipHijack.Runtime;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        // BIK インデックス → エフェクタキー（外部公開用）
        // 順序は BIK_LH..BIK_BODY と一致
        private static readonly string[] EffectorKeys = new[]
        {
            "left_hand",
            "right_hand",
            "left_foot",
            "right_foot",
            "left_shoulder",
            "right_shoulder",
            "left_thigh",
            "right_thigh",
            "left_elbow",
            "right_elbow",
            "left_knee",
            "right_knee",
            "body",
        };

        public static IReadOnlyList<string> GetEffectorKeys() => EffectorKeys;

        public static bool TryGetBodyIkTrackingPosition(out Vector3 position, out bool bodyIkRunning, out string source)
        {
            position = Vector3.zero;
            bodyIkRunning = false;
            source = null;

            Plugin inst = Instance;
            if (inst == null)
            {
                return false;
            }

            BodyIkOperationPointState bodyState = inst._bikEff != null && inst._bikEff.Length > BIK_BODY
                ? inst._bikEff[BIK_BODY]
                : null;

            bodyIkRunning = bodyState != null && bodyState.Running;
            if (bodyIkRunning && bodyState.Proxy != null)
            {
                position = bodyState.Proxy.position;
                source = "body_proxy";
                return true;
            }

            if (inst._femaleSession.FullBodyIk != null
                && inst._femaleSession.FullBodyIk.solver != null
                && inst._femaleSession.FullBodyIk.solver.bodyEffector != null
                && inst._femaleSession.FullBodyIk.solver.bodyEffector.bone != null)
            {
                position = inst._femaleSession.FullBodyIk.solver.bodyEffector.bone.position;
                source = "body_effector_bone";
                return true;
            }

            return false;
        }

        /// <summary>
        /// 女キャラ頭ボーン(cf_j_head)の世界位置を返す。
        /// 当プラグインの頭操作(VRグラブ/角度ギズモ/HMD追従等)が反映された後の最終位置。
        /// </summary>
        public static bool TryGetFemaleHeadPosition(out Vector3 position, out string source)
        {
            position = Vector3.zero;
            source = null;

            Plugin inst = Instance;
            if (inst == null || inst._femaleSession.BoneCache == null)
            {
                return false;
            }

            Transform head = FindBoneInCache(inst._femaleSession.BoneCache, "cf_j_head");
            if (head == null)
            {
                return false;
            }

            position = head.position;
            source = "cf_j_head";
            return true;
        }

        private static int IndexOfEffectorKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return -1;
            for (int i = 0; i < EffectorKeys.Length; i++)
            {
                if (string.Equals(EffectorKeys[i], key, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// 指定エフェクタを Composer オブジェクト追従に設定。
        /// 既存追従（近傍ボーン）は解除される（排他）。
        /// </summary>
        public static bool TrySetEffectorFollowComposerObject(string effectorKey, string composerId)
        {
            Plugin inst = Instance;
            if (inst == null || inst._settings == null) return false;
            int idx = IndexOfEffectorKey(effectorKey);
            if (idx < 0) return false;

            EnsureComposerSettingsArrays(inst._settings);
            inst._settings.FollowComposerEnabled[idx] = true;
            inst._settings.FollowComposerObjectIds[idx] = composerId;

            // 排他: 既存ボーン追従を解除
            try
            {
                inst.ClearBodyIKFollowBone(idx);
            }
            catch (Exception)
            {
                // 内部関数アクセス失敗時もフラグ自体は維持する
            }

            inst.SaveSettings();
            return true;
        }

        public static bool TryClearEffectorFollowComposerObject(string effectorKey)
        {
            Plugin inst = Instance;
            if (inst == null || inst._settings == null) return false;
            int idx = IndexOfEffectorKey(effectorKey);
            if (idx < 0) return false;

            EnsureComposerSettingsArrays(inst._settings);
            inst._settings.FollowComposerEnabled[idx] = false;
            inst._settings.FollowComposerObjectIds[idx] = null;
            inst.SaveSettings();
            return true;
        }

        public static bool TryGetEffectorFollowComposerObject(string effectorKey, out string composerId)
        {
            composerId = null;
            Plugin inst = Instance;
            if (inst == null || inst._settings == null) return false;
            int idx = IndexOfEffectorKey(effectorKey);
            if (idx < 0) return false;

            EnsureComposerSettingsArrays(inst._settings);
            if (!inst._settings.FollowComposerEnabled[idx]) return false;
            composerId = inst._settings.FollowComposerObjectIds[idx];
            return !string.IsNullOrEmpty(composerId);
        }

        private static void EnsureComposerSettingsArrays(BodyIkGizmoSettings settings)
        {
            if (settings.FollowComposerEnabled == null || settings.FollowComposerEnabled.Length != BIK_TOTAL)
            {
                settings.FollowComposerEnabled = new bool[BIK_TOTAL];
            }
            if (settings.FollowComposerObjectIds == null || settings.FollowComposerObjectIds.Length != BIK_TOTAL)
            {
                settings.FollowComposerObjectIds = new string[BIK_TOTAL];
            }
        }

        // ── 外部プラグイン（ObjectComposer 等）からのIK制御API ─────────

        /// <summary>
        /// 指定エフェクタのIKをON/OFFする。
        /// </summary>
        public static bool TrySetEffectorEnabled(string effectorKey, bool on)
        {
            Plugin inst = Instance;
            if (inst == null) return false;
            int idx = IndexOfEffectorKey(effectorKey);
            if (idx < 0) return false;
            try
            {
                inst.SetBodyIK(idx, on, saveSettings: true, reason: "external-api");
                return true;
            }
            catch (Exception ex)
            {
                inst.LogWarn("TrySetEffectorEnabled failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 指定エフェクタIKのウエイトを設定する (0.0 〜 1.0)。
        /// </summary>
        public static bool TrySetEffectorWeight(string effectorKey, float weight)
        {
            Plugin inst = Instance;
            if (inst == null) return false;
            int idx = IndexOfEffectorKey(effectorKey);
            if (idx < 0) return false;
            try
            {
                inst.SetBodyIKWeight(idx, weight, saveSettings: true);
                return true;
            }
            catch (Exception ex)
            {
                inst.LogWarn("TrySetEffectorWeight failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 「速さゲージ乗っ取り」のON/OFF。
        /// </summary>
        public static bool TrySetSpeedHijackEnabled(bool enabled)
        {
            Plugin inst = Instance;
            if (inst == null || inst._settings == null) return false;
            inst._settings.SpeedHijackEnabled = enabled;
            inst.SaveSettings();
            return true;
        }

        /// <summary>
        /// 「女性アニメ速度切断」のON/OFF。
        /// </summary>
        public static bool TrySetCutFemaleAnimSpeedEnabled(bool enabled)
        {
            Plugin inst = Instance;
            if (inst == null || inst._settings == null) return false;
            inst._settings.CutFemaleAnimSpeedEnabled = enabled;
            inst.SaveSettings();
            return true;
        }

        /// <summary>
        /// 「左コントローラー腰リンク」(BodyCtrlLink) の解除。
        /// 現状 OFF にする用途のみ想定。
        /// </summary>
        public static bool TryDisableBodyCtrlLink()
        {
            Plugin inst = Instance;
            if (inst == null) return false;
            try
            {
                inst.DisableBodyCtrlLink();
                return true;
            }
            catch (Exception ex)
            {
                inst.LogWarn("TryDisableBodyCtrlLink failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 「左コントローラー腰リンク」の現在の有効状態を返す。
        /// </summary>
        public static bool IsBodyCtrlLinkEnabled()
        {
            Plugin inst = Instance;
            if (inst == null) return false;
            return inst._bodyCtrlLinkEnabled;
        }

        /// <summary>
        /// 「速さゲージ乗っ取り」の現在の有効状態を返す。
        /// </summary>
        public static bool IsSpeedHijackEnabled()
        {
            Plugin inst = Instance;
            if (inst == null || inst._settings == null) return false;
            return inst._settings.SpeedHijackEnabled;
        }

        /// <summary>
        /// 「女性アニメ速度切断」の現在の有効状態を返す。
        /// </summary>
        public static bool IsCutFemaleAnimSpeedEnabled()
        {
            Plugin inst = Instance;
            if (inst == null || inst._settings == null) return false;
            return inst._settings.CutFemaleAnimSpeedEnabled;
        }

        /// <summary>
        /// 「左コントローラー腰リンク」のトグル（OFF→ON 用）。
        /// 既存の <see cref="ToggleBodyCtrlLink"/> を一度だけ呼ぶ。既に ON のときは無視。
        /// </summary>
        public static bool TryEnableBodyCtrlLink()
        {
            Plugin inst = Instance;
            if (inst == null) return false;
            if (inst._bodyCtrlLinkEnabled) return true;
            try
            {
                inst.ToggleBodyCtrlLink();
                return true;
            }
            catch (Exception ex)
            {
                inst.LogWarn("TryEnableBodyCtrlLink failed: " + ex.Message);
                return false;
            }
        }
    }
}
