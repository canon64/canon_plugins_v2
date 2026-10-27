using System;
using System.Collections.Generic;
using UnityEngine;

namespace AstralMode
{
    /// <summary>
    /// 挿入体位のあいだ、ベッドをキャラの真下へ移動して「ベッドの上でしている」絵にする。
    ///
    /// キャラは動かさない（動かすとアストラルの座標設計が壊れる）。動かすのはベッドだけ。
    /// キャラの足元にマットレスの上面を合わせることで、乗っているように見せる。
    ///
    /// 高さの根拠:
    ///   ChaControl.transform.position は足元（Plugin.CharaTeleport.cs が footPos として扱っている）。
    ///   ベッドはフレーム底からマットレス上面まで 0.62m。よってベッド原点を足元の 0.62m 下へ置けば
    ///   マットレス上面がちょうど足元の高さに来る。脚(0.22m)は地面に埋まって隠れる。
    ///
    /// 移動は Transform の付け替えだけで、景色の組み直しは一切走らせない。
    /// </summary>
    public sealed partial class Plugin
    {
        private bool _bedFollowActive;
        private bool _bedFollowLogged;

        /// <summary>毎フレーム呼ぶ。体位変更のフックだけだと取りこぼす経路があるため。</summary>
        internal void UpdateFurnitureFollow()
        {
            if (_astralHillRoot == null || !_astralHillRoot.activeSelf) return;

            bool insertion = IsInsertionMode();

            // ベンチの出し入れはベッドの追従とは切り離す。
            // ベッドを置かない設定でも、挿入体位ならベンチは退かす必要がある。
            ApplyBenchVisibility(insertion);

            bool want = _cfgBedFollowOnInsertion != null && _cfgBedFollowOnInsertion.Value && insertion;

            if (want != _bedFollowActive)
            {
                _bedFollowActive = want;
                _bedFollowLogged = false;
                if (!want) RestoreBedPlacement();
            }

            if (_bedFollowActive) PlaceBedUnderCharas();
        }

        /// <summary>
        /// ベンチに座る体位（ベンチフェラ）以外ではベンチを隠す。
        /// 座らない体位では床に置いた板が体を貫くだけで、挿入体位ではベッドとも重なる。
        /// </summary>
        private void ApplyBenchVisibility(bool insertion)
        {
            GameObject bench;
            if (!_astralFurnitureByName.TryGetValue("Bench", out bench) || bench == null) return;

            string poseName;
            bool benchPose = IsBenchPose(out poseName);

            bool offPose;
            bool allowedByToggle;
            if (TryGetBenchPoseAllow(poseName, out allowedByToggle))
            {
                // 体位ごとのチェックボックスがあるならそれが最優先。
                offPose = !allowedByToggle;
            }
            else if (poseName != null)
            {
                // まだ登録されていない体位は名前で判定する。
                offPose = !benchPose;
            }
            else
            {
                // 体位名が読めない一瞬（切替の最中など）は、従来どおり挿入かどうかで決める。
                offPose = insertion;
            }

            bool hideWanted = offPose
                && (_cfgBenchHideOffPose == null || _cfgBenchHideOffPose.Value);
            bool benchEnabled = _cfgUseAstralBench == null || _cfgUseAstralBench.Value;
            bool shouldBeActive = benchEnabled && !hideWanted;

            if (bench.activeSelf == shouldBeActive) return;
            bench.SetActive(shouldBeActive);
            SimpleFileLogger.Info($"Astral bench {(shouldBeActive ? "shown" : "hidden")}: "
                + $"pose={poseName ?? "unknown"}, insertion={insertion}, "
                + $"mode={(_hFlag != null ? _hFlag.mode.ToString() : "null")}");
        }

        /// <summary>いまの体位がベンチに座るものか。体位名が取れない場合は poseName が null になる。</summary>
        private bool IsBenchPose(out string poseName)
        {
            poseName = null;
            if (_hFlag == null) return false;

            HSceneProc.AnimationListInfo now = _hFlag.nowAnimationInfo;
            string name = now != null ? now.nameAnimation : null;
            if (string.IsNullOrEmpty(name)) return false;

            poseName = name;

            string keyword = (AstralPoseKeyword ?? DefaultAstralPoseKeyword).Trim();
            if (keyword.Length == 0) keyword = DefaultAstralPoseKeyword;

            return name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>挿入系の体位かどうか。3P/MMF も挿入として扱う。</summary>
        private static bool IsInsertionMode()
        {
            if (_hFlag == null) return false;
            HFlag.EMode mode = _hFlag.mode;
            return mode == HFlag.EMode.sonyu
                || mode == HFlag.EMode.sonyu3P
                || mode == HFlag.EMode.sonyu3PMMF;
        }

        /// <summary>追従の基準になるキャラの足元と向き。女キャラを優先する。</summary>
        private bool ResolveCharaAnchor(out Vector3 footPosition, out float yaw)
        {
            System.Collections.IEnumerable females = GetCurrentHSceneFemaleObjects();
            if (females != null)
            {
                foreach (object candidate in females)
                {
                    ChaControl c = candidate as ChaControl;
                    if (c == null) continue;
                    footPosition = c.transform.position;
                    yaw = c.transform.eulerAngles.y;
                    return true;
                }
            }

            Vector3 fallback = GetAstralCharaCenter();
            if (fallback == Vector3.zero) fallback = GetAstralWorldOrigin();
            footPosition = fallback;
            yaw = 0f;
            return false;
        }

        private void PlaceBedUnderCharas()
        {
            GameObject bed;
            if (!_astralFurnitureByName.TryGetValue("Bed", out bed) || bed == null) return;

            Vector3 foot;
            float yaw;
            bool fromChara = ResolveCharaAnchor(out foot, out yaw);

            float scale = ReadScale(_cfgAstralBedScale);
            Vector3 targetScale = Vector3.one * scale;
            if (bed.transform.localScale != targetScale)
                bed.transform.localScale = targetScale;

            // 親（丘）に倍率が掛かっている場合も込みで、実寸のマットレス上面の高さを出す
            float parentScaleY = _astralHillGeometryRoot != null ? _astralHillGeometryRoot.lossyScale.y : 1f;
            float mattressTop = BedMattressTopLocal * scale * parentScaleY;

            float heightOffset = _cfgBedFollowHeightOffset != null ? _cfgBedFollowHeightOffset.Value : 0f;
            float yawOffset = _cfgBedFollowYawOffset != null ? _cfgBedFollowYawOffset.Value : 0f;

            Vector3 targetPosition = new Vector3(foot.x, foot.y - mattressTop + heightOffset, foot.z);
            if (bed.transform.position != targetPosition)
                bed.transform.position = targetPosition;
            Quaternion targetRotation = Quaternion.Euler(0f, yaw + yawOffset, 0f);
            if (bed.transform.rotation != targetRotation)
                bed.transform.rotation = targetRotation;

            if (_bedFollowLogged) return;
            _bedFollowLogged = true;
            SimpleFileLogger.Info($"Bed follow ON: mode={( _hFlag != null ? _hFlag.mode.ToString() : "null")}, "
                + $"anchor={(fromChara ? "chara" : "fallback")}, foot={foot}, yaw={yaw:F1}, "
                + $"mattressTop={mattressTop:F3}, bedPos={bed.transform.position}");
        }

        /// <summary>ベッドの追従をやめて、Config で指定された元の置き場所へ戻す。</summary>
        private void RestoreBedPlacement()
        {
            ApplyFurnitureTransforms();
            SimpleFileLogger.Info("Bed follow OFF: bed returned to configured placement");
        }
    }
}
