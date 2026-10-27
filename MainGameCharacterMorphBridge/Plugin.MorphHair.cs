using System;
using System.Text;
using UnityEngine;

namespace MainGameCharacterMorphBridge
{
    /// <summary>
    /// 髪型(メッシュ)の差し替え。
    ///
    /// 髪の「色」は Plugin.MorphColor.cs が ChaFileHair.parts[i] の各色を補間して扱う。
    /// こちらが扱うのは parts[i].id、つまりどの髪メッシュを使うかという番号。
    /// IDなので混ぜられない。上絵(KSOX)や肌ID・化粧IDと同じく、変身の中間点で切り替える。
    /// 切り替えはイージング後の値ではなく経過時間の真ん中で行う（TickOverlaySwapと同じ規則）。
    ///
    /// 差し替え手段は ChaControl.Reload の髪だけ再構築するフラグ。
    ///   Reload(noChangeClothes:true, noChangeHead:true, noChangeHair:false, noChangeBody:true)
    /// 服・頭・体はそのままなので、モーフや肌色は壊れない。
    /// ただし髪は作り直されるので、髪の色は塗り直さないと元に戻る。
    /// そのため差し替え後は _forceColorAfterHair を立てて、色の間引きを飛ばして塗り直させる。
    /// </summary>
    public sealed partial class Plugin
    {
        private HairSnapshot _originalHair;
        private HairSnapshot _targetHair;
        private bool _hairSwapped;
        private bool _hairSupportLogged;

        /// <summary>髪を作り直した直後は、色の間引きを飛ばして塗り直す必要がある。</summary>
        internal bool _forceColorAfterHair;

        // ---------------- スナップショット ----------------

        internal sealed class HairSnapshot
        {
            /// <summary>parts の並び順そのまま。値は髪メッシュのID。読めなかった要素は -1。</summary>
            internal int[] Ids;
        }

        internal static HairSnapshot HairSnapshotFrom(ChaFileControl file)
        {
            object[] parts = GetHairParts(file);
            if (parts == null)
                return null;

            var ids = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                int id;
                ids[i] = TryGetInt(parts[i], "id", out id) ? id : -1;
            }

            return new HairSnapshot { Ids = ids };
        }

        internal void CaptureOriginalHair(ChaControl target)
        {
            _originalHair = target != null ? HairSnapshotFrom(target.chaFile) : null;
            LogHairSnapshot("original", _originalHair);
        }

        internal void CaptureTargetHair(ChaFileControl card)
        {
            _targetHair = HairSnapshotFrom(card);
            LogHairSnapshot("card", _targetHair);
        }

        internal void ResetHairSwapState()
        {
            _hairSwapped = false;
        }

        // ---------------- 反映 ----------------

        /// <summary>
        /// 遷移の中間点で1回だけ、行き先の髪型へ差し替える。
        /// 毎フレーム Reload を呼ぶと落ちるので、_hairSwapped で必ず1回に絞る。
        /// </summary>
        internal void TickHairSwap(ChaControl target, float targetBlend, float timeRatio)
        {
            if (_hairSwapped || timeRatio < 0.5f)
                return;

            if (_settings == null || !_settings.MorphHairStyleEnabled)
                return;

            // 通過は1回きり。この先で失敗しても再入しない。
            _hairSwapped = true;

            HairSnapshot to = targetBlend >= 0.5f ? _targetHair : _originalHair;
            if (to == null || to.Ids == null)
            {
                LogExecution("hair style swap skipped: no snapshot for destination");
                return;
            }

            ApplyHairIds(target, to);
        }

        private void ApplyHairIds(ChaControl target, HairSnapshot to)
        {
            if (target == null || target.chaFile == null)
                return;

            Array parts = GetHairPartsArray(target.chaFile);
            if (parts == null)
            {
                if (!_hairSupportLogged)
                {
                    _hairSupportLogged = true;
                    LogWarnAlways("hair style swap unsupported: hair.parts not found");
                }
                return;
            }

            int count = Mathf.Min(parts.Length, to.Ids.Length);
            var before = new StringBuilder();
            var after = new StringBuilder();
            bool changed = false;

            for (int p = 0; p < count; p++)
            {
                object part = parts.GetValue(p);
                if (part == null)
                    continue;

                int current;
                if (!TryGetInt(part, "id", out current))
                    continue;

                int wanted = to.Ids[p];

                if (before.Length > 0)
                    before.Append(",");
                before.Append(current);

                // 読めなかった要素(-1)は触らない。元の髪型を維持する。
                if (wanted < 0 || wanted == current)
                {
                    if (after.Length > 0)
                        after.Append(",");
                    after.Append(current);
                    continue;
                }

                if (TrySetInt(part, "id", wanted))
                {
                    // 要素が構造体なら GetValue はコピーなので、書き戻さないと反映されない。
                    parts.SetValue(part, p);
                    changed = true;
                }

                if (after.Length > 0)
                    after.Append(",");
                after.Append(wanted);
            }

            if (!changed)
            {
                LogExecution("hair style swap: no change parts=" + parts.Length + " ids=" + before);
                return;
            }

            // 髪だけ作り直す。服・頭・体は触らないのでモーフと肌色は壊れない。
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool ok;
            try
            {
                ok = target.Reload(noChangeClothes: true, noChangeHead: true, noChangeHair: false, noChangeBody: true);
            }
            catch (Exception ex)
            {
                LogWarnAlways("hair style swap failed on Reload: " + ex.Message);
                return;
            }
            watch.Stop();

            // 作り直した髪には色が乗っていない。次の色反映で必ず塗り直させる。
            _forceColorAfterHair = true;

            LogExecution("hair style swap applied: parts=" + parts.Length
                + " from=[" + before + "] to=[" + after + "]"
                + " reload=" + ok
                + " cost=" + watch.ElapsedMilliseconds + "ms");
        }

        // ---------------- 診断 ----------------

        /// <summary>
        /// 髪パーツの構成をログに出す。parts の要素数と並び順(どの位置が後ろ髪/前髪/横髪か)は
        /// 実機で1回見ておかないと確定しないため、取得時に必ず記録する。
        /// </summary>
        private void LogHairSnapshot(string label, HairSnapshot snapshot)
        {
            if (_settings == null || !_settings.EnableLogs)
                return;

            if (snapshot == null || snapshot.Ids == null)
            {
                LogExecution("hair snapshot " + label + ": none");
                return;
            }

            var ids = new StringBuilder();
            for (int i = 0; i < snapshot.Ids.Length; i++)
            {
                if (i > 0)
                    ids.Append(",");
                ids.Append(snapshot.Ids[i]);
            }

            LogExecution("hair snapshot " + label + ": parts=" + snapshot.Ids.Length + " ids=[" + ids + "]");
        }
    }
}
