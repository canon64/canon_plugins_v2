using System;
using HarmonyLib;
using UnityEngine;

namespace MainGameCameraControl
{
    /// <summary>
    /// 変身などで体型（身長）が変わった時、構図を保つ。
    ///
    /// 通常プリセットは transBase 空間に固定された構図で、キャラには結び付いていない。
    /// 身長が変われば顔の位置だけが上下し、注視点は元の高さに取り残されて画が崩れる。
    /// CameraData の Pos は「注視点であり構図の基準点」で、カメラ位置は
    /// Pos + 回転済みDir で決まる。つまり Pos を動かせば注視点とカメラが一緒に動き、
    /// 角度(Rot)も寄り(Dirの長さ)も保ったまま構図ごと平行移動する。
    /// 直すのに必要なのは「体型変化でキャラが何cm動いたか」だけになる。
    ///
    /// 問題はその測り方で、フレームをまたいで骨の位置を比べるとアニメーションの動きが
    /// 混ざってしまい、体型由来の分を取り出せない。
    /// 骨が実際に動くのは ChaControl.LateUpdateForce() 内の UpdateShapeBody() で、
    /// この呼び出しの前後にはアニメーションが挟まらない（逆コンパイルで確認済み）。
    /// そこで UpdateShapeBody を挟んで測る。ここでの差分は体型変化だけが原因になる。
    ///
    /// なお ChangeValue / UpdateShapeBodyValueFromCustomInfo の前後で測っても差は出ない。
    /// あちらは BoneInfo に数値を書くだけで、Unityのtransformには触らないため。
    ///
    /// ボーン連動プリセットは注視点が既に骨へ追従するので対象外。
    /// </summary>
    internal sealed partial class Plugin
    {
        private static bool _heightShiftPatched;

        // 挟んで測るための一時値。プレフィクスで入れ、ポストフィクスで使う。
        private static bool _heightShiftMeasuring;
        private static Vector3 _heightShiftBefore;

        // 追従の基準にする骨。注視点に一番近いものを選び、注視点が動いたら選び直す。
        private Transform _heightShiftBone;
        private Vector3 _heightShiftBonePickedLookAt;
        private bool _heightShiftBonePicked;

        // 選び直すしきい値。これ以上注視点が動いたら別の部位を見ていると見なす。
        private const float HeightShiftRepickDistance = 0.08f;

        // 測定誤差を無視するしきい値。これ未満の移動は捨てる。
        private const float HeightShiftEpsilon = 0.00002f;

        private static void PatchHeightShift()
        {
            if (_heightShiftPatched)
                return;
            _heightShiftPatched = true;

            try
            {
                var target = AccessTools.Method(typeof(ChaControl), "UpdateShapeBody");
                var prefix = AccessTools.Method(typeof(Plugin), nameof(ChaControl_UpdateShapeBody_Prefix));
                var postfix = AccessTools.Method(typeof(Plugin), nameof(ChaControl_UpdateShapeBody_Postfix));
                if (target == null || prefix == null || postfix == null)
                {
                    Instance?.LogWarn("MGCC_HEIGHT_SHIFT patch failed target=" + (target != null)
                        + " prefix=" + (prefix != null) + " postfix=" + (postfix != null));
                    return;
                }

                new Harmony(GUID + ".heightShift").Patch(target, new HarmonyMethod(prefix), new HarmonyMethod(postfix));
                Instance?.LogInfo("MGCC_HEIGHT_SHIFT patched");
            }
            catch (Exception ex)
            {
                Instance?.LogWarn("MGCC_HEIGHT_SHIFT patch error: " + ex.Message);
            }
        }

        private static void ChaControl_UpdateShapeBody_Prefix(ChaControl __instance)
        {
            _heightShiftMeasuring = false;

            Plugin self = Instance;
            if (self == null || !self.IsHeightShiftTarget(__instance))
                return;

            Transform bone = self.ResolveHeightShiftBone(__instance);
            if (bone == null)
                return;

            _heightShiftBefore = bone.position;
            _heightShiftMeasuring = true;
        }

        private static void ChaControl_UpdateShapeBody_Postfix(ChaControl __instance)
        {
            if (!_heightShiftMeasuring)
                return;
            _heightShiftMeasuring = false;

            Plugin self = Instance;
            if (self == null || self._heightShiftBone == null)
                return;

            Vector3 delta = self._heightShiftBone.position - _heightShiftBefore;
            if (delta.sqrMagnitude < HeightShiftEpsilon * HeightShiftEpsilon)
                return;

            self.ShiftCompositionBy(delta);
        }

        /// <summary>補正して良い状況か。ボーン連動中は注視点が骨を追うので触らない。</summary>
        private bool IsHeightShiftTarget(ChaControl cha)
        {
            if (cha == null || _ctrlCamera == null)
                return false;
            if (_cfgHeightShiftEnabled == null || !_cfgHeightShiftEnabled.Value)
                return false;
            if (_boneLinkActive)
                return false;

            return ReferenceEquals(cha, ResolveMainFemale());
        }

        /// <summary>
        /// 今の注視点に一番近い骨を選ぶ。顔を見ていれば頭、腰を見ていれば腰が選ばれ、
        /// その部位が体型変化でどれだけ動いたかがそのまま補正量になる。
        /// </summary>
        private Transform ResolveHeightShiftBone(ChaControl female)
        {
            Vector3 lookAt = GetCurrentLookAtWorld();

            if (_heightShiftBonePicked
                && _heightShiftBone != null
                && (lookAt - _heightShiftBonePickedLookAt).sqrMagnitude
                    < HeightShiftRepickDistance * HeightShiftRepickDistance)
            {
                return _heightShiftBone;
            }

            Transform[] bones = GetFemaleBoneCache(female);
            if (bones == null || bones.Length == 0)
                return null;

            Transform best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i];
                if (bone == null)
                    continue;

                float distance = (bone.position - lookAt).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = bone;
                }
            }

            if (best == null)
                return null;

            _heightShiftBone = best;
            _heightShiftBonePickedLookAt = lookAt;
            _heightShiftBonePicked = true;
            return best;
        }

        private Vector3 GetCurrentLookAtWorld()
        {
            if (_ctrlCamera == null)
                return Vector3.zero;

            return _ctrlCamera.transBase != null
                ? _ctrlCamera.transBase.TransformPoint(_currentData.Pos)
                : _currentData.Pos;
        }

        /// <summary>
        /// 構図の基準点をワールド差分ぶん平行移動する。
        /// Rot と Dir は触らないので、角度も寄りも変わらない。
        /// 遷移中でも崩れないよう、現在値と目標値の両方を同じだけ動かす。
        /// </summary>
        private void ShiftCompositionBy(Vector3 worldDelta)
        {
            Vector3 localDelta = _ctrlCamera.transBase != null
                ? _ctrlCamera.transBase.InverseTransformVector(worldDelta)
                : worldDelta;

            _currentData.Pos += localDelta;
            _targetData.Pos += localDelta;
            ApplyCameraData(_currentData);

            LogHeightShift(worldDelta, localDelta);
        }

        private void LogHeightShift(Vector3 worldDelta, Vector3 localDelta)
        {
            if (_cfgDetailLogEnabled == null || !_cfgDetailLogEnabled.Value)
                return;

            LogInfo("[height-shift] bone=" + (_heightShiftBone != null ? _heightShiftBone.name : "?")
                + " world=" + worldDelta.ToString("0.#####")
                + " local=" + localDelta.ToString("0.#####"));
        }

        internal void InvalidateHeightShiftBone()
        {
            _heightShiftBone = null;
            _heightShiftBonePicked = false;
        }
    }
}
