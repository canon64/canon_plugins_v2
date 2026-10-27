using System.Collections.Generic;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        // エフェクタキー → 日本語ラベル
        private static readonly Dictionary<string, string> EffectorLabels = new Dictionary<string, string>
        {
            { "body",            "腰 (Body)" },
            { "left_hand",       "左手" },
            { "right_hand",      "右手" },
            { "left_foot",       "左足" },
            { "right_foot",      "右足" },
            { "left_shoulder",   "左肩" },
            { "right_shoulder",  "右肩" },
            { "left_thigh",      "左腿" },
            { "right_thigh",     "右腿" },
            { "left_elbow",      "左肘" },
            { "right_elbow",     "右肘" },
            { "left_knee",       "左膝" },
            { "right_knee",      "右膝" },
        };

        private Vector2 _hipHijackLinkScroll;

        private void DrawHipHijackLinkSection()
        {
            if (!HipHijackBridge.IsAvailable) return;

            ManagedObjectData selected = GetSelectedData();
            if (selected == null) return;

            IList<string> keys = HipHijackBridge.GetEffectorKeys();
            if (keys == null || keys.Count == 0) return;

            GUILayout.Space(6f);
            GUILayout.Label("腰IK連動: チェックを入れたIKがこのオブジェクトを親として追従");

            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                if (string.IsNullOrEmpty(key)) continue;

                string label = EffectorLabels.TryGetValue(key, out string l) ? l : key;

                // チェックボックスの読み取り元:
                //   アクティブプリセット中 → preset.ikLinks (自分の UI 状態が真実)
                //   非アクティブ           → HipHijack の Follow リンク (一時的)
                bool linkedToThis;
                string curIdForDisplay;
                if (_activePreset != null)
                {
                    linkedToThis = IsIkLinkedInActivePreset(key, selected.id);
                    HipHijackBridge.TryGet(key, out curIdForDisplay);
                }
                else
                {
                    HipHijackBridge.TryGet(key, out curIdForDisplay);
                    linkedToThis = !string.IsNullOrEmpty(curIdForDisplay)
                        && string.Equals(curIdForDisplay, selected.id, System.StringComparison.Ordinal);
                }

                GUILayout.BeginHorizontal();

                bool nextLinked = GUILayout.Toggle(linkedToThis, label, GUILayout.Width(120f));
                if (nextLinked != linkedToThis)
                {
                    if (nextLinked)
                    {
                        // 他のオブジェクトとの連動を上書きしてこちらに付け替え
                        HipHijackBridge.TrySet(key, selected.id);
                        ActivateHipHijackIkForKey(key);
                        // アクティブプリセットがあれば永続化 (チェック操作 = preset 編集)
                        AddIkLinkToActivePreset(key, selected.id);
                    }
                    else
                    {
                        // この対象から外す
                        HipHijackBridge.TryClear(key);
                        DeactivateHipHijackIkForKey(key);
                        RemoveIkLinkFromActivePreset(key);
                    }
                }

                // 別オブジェクトに連動中ならその ID 先頭8文字を表示
                if (!linkedToThis && !string.IsNullOrEmpty(curIdForDisplay)
                    && !string.Equals(curIdForDisplay, selected.id, System.StringComparison.Ordinal))
                {
                    GUILayout.Label("(別: " + curIdForDisplay.Substring(0, System.Math.Min(8, curIdForDisplay.Length)) + "…)",
                        GUILayout.Width(160f));
                }

                GUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// 指定エフェクタの IK を HipHijack 側で ON + ウエイト 1.0 にする。
        /// body の場合は競合機能（速さゲージ乗っ取り / 女性アニメ速度切断 / 腰リンク）を解除。
        /// </summary>
        private void ActivateHipHijackIkForKey(string effectorKey)
        {
            if (!HipHijackBridge.IsAvailable || string.IsNullOrEmpty(effectorKey)) return;

            HipHijackBridge.TrySetEffectorEnabled(effectorKey, true);
            HipHijackBridge.TrySetEffectorWeight(effectorKey, 1.0f);

            if (string.Equals(effectorKey, "body", System.StringComparison.OrdinalIgnoreCase))
            {
                HipHijackBridge.TrySetSpeedHijackEnabled(false);
                HipHijackBridge.TrySetCutFemaleAnimSpeedEnabled(false);
                if (HipHijackBridge.IsBodyCtrlLinkEnabled())
                {
                    HipHijackBridge.TryDisableBodyCtrlLink();
                }
                LogInfo("HipHijack body IK activated + conflicts disabled (speedHijack/cutFemaleSpeed/bodyCtrlLink)");
            }
            else
            {
                LogInfo("HipHijack IK activated: " + effectorKey + " (weight=1.0)");
            }
        }

        /// <summary>
        /// 指定エフェクタの IK を HipHijack 側で OFF にする（同期解除）。
        /// 競合機能（速さゲージ乗っ取り 等）はユーザーが手動で戻す想定で復元しない。
        /// </summary>
        private void DeactivateHipHijackIkForKey(string effectorKey)
        {
            if (!HipHijackBridge.IsAvailable || string.IsNullOrEmpty(effectorKey)) return;
            HipHijackBridge.TrySetEffectorEnabled(effectorKey, false);
            LogInfo("HipHijack IK deactivated: " + effectorKey);
        }
    }
}
