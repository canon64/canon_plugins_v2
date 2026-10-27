using System;
using UnityEngine;

namespace MainGamePregnancyPlusBridge
{
    public sealed partial class Plugin
    {
        private static Plugin _instance;

        private static float _currentBellyBokoIntensity;
        private static bool _hasCurrentBellyBokoIntensity;

        /// <summary>
        /// 腹ボコがピーク(極小距離→反転)に達したフレームで発火するイベント。
        /// 引数: そのピーク時の正規化強度 (0..1)。
        /// 購読は Awake などで行い、不要になったら必ず解除すること。
        /// </summary>
        public static event Action<float> OnBellyBokoPeak;

        /// <summary>
        /// 現在のHシーンにおける男女股間ボーン間の距離 (メートル) を取得。
        /// プラグイン未起動 / HSceneProc 未検出 / 男女キャラ未解決 / ボーン未検出のいずれかで false。
        ///
        /// ボーン解決は PregnancyPlus が belly 演算で使うものと同一:
        ///   女: k_f_kokan_00, a_n_kokan, cf_j_hips, cf_j_root
        ///   男: a_n_kokan, cf_j_hips, cf_j_waist01, cm_j_waist01, cf_j_root
        /// (上から順に最初に見つかった Transform を採用)
        /// </summary>
        public static bool TryGetCurrentGroinDistance(out float meters)
        {
            meters = 0f;
            Plugin inst = _instance;
            if (inst == null) return false;

            // HSceneProc キャッシュ更新
            if (inst._bellyHSceneProc == null)
            {
                inst._bellyHSceneProc = FindObjectOfType<HSceneProc>();
            }
            else if (!inst._bellyHSceneProc)
            {
                inst._bellyHSceneProc = null;
            }
            if (inst._bellyHSceneProc == null) return false;

            ChaControl female = inst.ResolveMainFemaleForBelly(inst._bellyHSceneProc);
            if (female == null) return false;

            return inst.TryGetBellyDistance(inst._bellyHSceneProc, female, out meters);
        }

        /// <summary>
        /// 現在の腹ボコ正規化強度 (0..1) を取得。
        /// BellyBoko 計算が走っていない状態 (Hシーン外 / disabled / コンテキスト未確定 / 距離参照不可) では false。
        /// </summary>
        public static bool TryGetCurrentBellyBokoIntensity(out float normalized)
        {
            if (_hasCurrentBellyBokoIntensity)
            {
                normalized = _currentBellyBokoIntensity;
                return true;
            }
            normalized = 0f;
            return false;
        }

        internal static void NotifyBellyBokoIntensity(float normalized)
        {
            _currentBellyBokoIntensity = Mathf.Clamp01(normalized);
            _hasCurrentBellyBokoIntensity = true;
        }

        internal static void ClearBellyBokoIntensity()
        {
            _hasCurrentBellyBokoIntensity = false;
            _currentBellyBokoIntensity = 0f;
        }

        internal static void NotifyBellyBokoPeak(float normalized)
        {
            Action<float> handler = OnBellyBokoPeak;
            if (handler == null) return;
            float clamped = Mathf.Clamp01(normalized);
            try
            {
                handler(clamped);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PregnancyPlusBridge] OnBellyBokoPeak handler error: " + ex.Message);
            }
        }
    }
}
