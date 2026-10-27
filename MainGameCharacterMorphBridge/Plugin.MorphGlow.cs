using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MainGameCharacterMorphBridge
{
    /// <summary>
    /// 変身（Blend遷移）に合わせて MainGameGlow を焚く連携。
    ///
    /// 発光そのものは MainGameGlow が持っているので、ここは「いつ」「どれだけ」「誰を光らせないか」
    /// だけを渡す。MainGameGlow が入っていない環境では黙って何もしない（DLL参照ではなくリフレクション）。
    /// </summary>
    public sealed partial class Plugin
    {
        private const string GlowApiTypeName = "MainGameGlow.MainGameGlowApi";

        private static bool _glowApiResolved;
        private static MethodInfo _glowBeginMethod;
        private static MethodInfo _glowSetScaleMethod;
        private static MethodInfo _glowSetParamsMethod;
        private static MethodInfo _glowEndMethod;

        private bool _morphGlowDriving;
        private bool _tattooGlowDriving;

        internal bool BeginTattooEffect(int femaleIndex)
        {
            PlayMorphSound();
            if (_morphGlowDriving) return true;
            _tattooGlowDriving = BeginMorphGlow(ResolveFemaleIndex(femaleIndex));
            if (_tattooGlowDriving) SetMorphGlowScale(0f);
            return true;
        }

        internal void UpdateTattooEffect(float elapsed, float duration)
        {
            if (_tattooGlowDriving) SetMorphGlowScale(MorphGlowScale(elapsed, duration));
        }

        internal void EndTattooEffect()
        {
            if (!_tattooGlowDriving) return;
            _tattooGlowDriving = false;
            SetMorphGlowScale(0f);
            EndMorphGlow();
        }

        private static void ResolveGlowApi()
        {
            if (_glowApiResolved)
                return;

            _glowApiResolved = true;

            Type type = AccessTools.TypeByName(GlowApiTypeName);
            if (type == null)
                return;

            _glowBeginMethod = AccessTools.Method(type, "TryBeginExternalGlow", new[] { typeof(GameObject[]) });
            _glowSetScaleMethod = AccessTools.Method(type, "TrySetExternalGlowScale", new[] { typeof(float) });
            _glowSetParamsMethod = AccessTools.Method(
                type, "TrySetExternalGlowParameters", new[] { typeof(float), typeof(float) });
            _glowEndMethod = AccessTools.Method(type, "TryEndExternalGlow", new Type[0]);
        }

        private bool IsMorphGlowAvailable()
        {
            ResolveGlowApi();
            return _glowBeginMethod != null && _glowSetScaleMethod != null && _glowEndMethod != null;
        }

        /// <summary>変身の開始時に一度だけ呼ぶ。光らせない側（男・もう一人の女）をここで確定させる。</summary>
        private bool BeginMorphGlow(int femaleIndex)
        {
            if (_tattooGlowDriving) return false;
            if (_settings == null || !_settings.MorphGlowEnabled)
                return false;

            if (!IsMorphGlowAvailable())
            {
                LogExecution("morph glow skipped: MainGameGlow not found");
                return false;
            }

            ChaControl target;
            string error;
            if (!TryResolveTarget(femaleIndex, out target, out error))
            {
                LogExecution("morph glow skipped: " + (error ?? "target not resolved"));
                return false;
            }

            GameObject[] excludeRoots = CollectGlowExcludeRoots(target);
            LogTargetRendererLayers(target);

            try
            {
                bool started = (bool)_glowBeginMethod.Invoke(null, new object[] { excludeRoots });
                _morphGlowDriving = started;

                // 閾値と強さは焚いている間だけ上書きする。MainGameGlow側の常用設定は触らない。
                if (started && _glowSetParamsMethod != null)
                {
                    _glowSetParamsMethod.Invoke(null, new object[]
                    {
                        PluginSettings.ClampFloat(_settings.MorphGlowThreshold, 0f, 5f),
                        PluginSettings.ClampFloat(_settings.MorphGlowStrength, 0f, 10f)
                    });
                }

                LogExecution("morph glow begin: started=" + started + " excluded=" + excludeRoots.Length);
                return started;
            }
            catch (Exception ex)
            {
                LogWarnAlways("morph glow begin failed: " + ex.Message);
                _morphGlowDriving = false;
                return false;
            }
        }

        /// <summary>
        /// 変身する本人のレンダラが実際にどのレイヤーに乗っているかを数える。
        /// グロー側が撮るレイヤーと一致していなければ、何をしても写らない。
        /// </summary>
        private void LogTargetRendererLayers(ChaControl target)
        {
            if (target == null)
                return;

            try
            {
                Renderer[] renderers = target.gameObject.GetComponentsInChildren<Renderer>(true);
                var counts = new Dictionary<int, int>();
                int visible = 0;
                foreach (Renderer renderer in renderers)
                {
                    if (renderer == null)
                        continue;

                    int layer = renderer.gameObject.layer;
                    counts[layer] = counts.ContainsKey(layer) ? counts[layer] + 1 : 1;
                    if (renderer.enabled && renderer.gameObject.activeInHierarchy)
                        visible++;
                }

                var parts = new List<string>();
                foreach (var pair in counts)
                    parts.Add(pair.Key + "(" + LayerMask.LayerToName(pair.Key) + ")=" + pair.Value);

                LogExecution("morph glow target layers: total=" + renderers.Length
                    + " visible=" + visible
                    + " layers=" + string.Join(", ", parts.ToArray()));
            }
            catch (Exception ex)
            {
                LogWarnAlways("morph glow target layer log failed: " + ex.Message);
            }
        }

        private float _morphGlowNextDiagTime;

        /// <summary>実際に送っている発光量を間引いて残す。毎フレームは出さない。</summary>
        private void LogMorphGlowDiag(string detail)
        {
            if (Time.unscaledTime < _morphGlowNextDiagTime)
                return;

            _morphGlowNextDiagTime = Time.unscaledTime + 0.5f;
            LogExecution("morph glow " + detail);
        }

        private void SetMorphGlowScale(float scale)
        {
            if (!_morphGlowDriving || _glowSetScaleMethod == null)
                return;

            try
            {
                _glowSetScaleMethod.Invoke(null, new object[] { scale });
            }
            catch (Exception ex)
            {
                LogWarnAlways("morph glow scale failed: " + ex.Message);
                _morphGlowDriving = false;
            }
        }

        /// <summary>変身が終わった時と、中断された時の両方から呼ぶ。焚きっぱなしを作らない。</summary>
        internal void EndMorphGlow()
        {
            if (!_morphGlowDriving)
                return;

            _morphGlowDriving = false;
            if (_glowEndMethod == null)
                return;

            try
            {
                _glowEndMethod.Invoke(null, new object[0]);
                LogExecution("morph glow end");
            }
            catch (Exception ex)
            {
                LogWarnAlways("morph glow end failed: " + ex.Message);
            }
        }

        /// <summary>変身する本人以外のキャラ。ここに入れた分だけグローのキャプチャから外れる。</summary>
        private GameObject[] CollectGlowExcludeRoots(ChaControl target)
        {
            var roots = new List<GameObject>();
            HSceneProc hscene = ResolveHScene();
            if (hscene == null)
                return roots.ToArray();

            List<ChaControl> females = GetFemales(hscene);
            if (females != null)
            {
                foreach (ChaControl female in females)
                    AddGlowExcludeRoot(roots, female, target);
            }

            AddGlowExcludeRoot(roots, FieldMale?.GetValue(hscene) as ChaControl, target);
            AddGlowExcludeRoot(roots, FieldMale1?.GetValue(hscene) as ChaControl, target);

            return roots.ToArray();
        }

        private static void AddGlowExcludeRoot(List<GameObject> roots, ChaControl chara, ChaControl target)
        {
            if (chara == null || ReferenceEquals(chara, target))
                return;

            GameObject root = chara.gameObject;
            if (root != null && !roots.Contains(root))
                roots.Add(root);
        }

        /// <summary>
        /// 経過秒から発光量を出す。立ち上がりの秒数で0→最大、終わりぎわに引きの秒数で最大→0。
        /// あいだは最大で保つ。合計が変身の長さを超える場合は、比率を保ったまま収まるよう詰める。
        /// </summary>
        private float MorphGlowScale(float elapsed, float duration)
        {
            if (_settings == null || duration <= 0f)
                return 0f;

            float fadeIn = Mathf.Max(0f, _settings.MorphGlowFadeInSeconds);
            float fadeOut = Mathf.Max(0f, _settings.MorphGlowFadeOutSeconds);

            float total = fadeIn + fadeOut;
            if (total > duration && total > 0f)
            {
                float shrink = duration / total;
                fadeIn *= shrink;
                fadeOut *= shrink;
            }

            float x;
            if (fadeIn > 0f && elapsed < fadeIn)
                x = elapsed / fadeIn;
            else if (fadeOut > 0f && elapsed > duration - fadeOut)
                x = (duration - elapsed) / fadeOut;
            else
                x = 1f;

            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x)) * MorphGlowMaxScale();
        }

        private float MorphGlowMaxScale()
        {
            return _settings != null ? PluginSettings.Clamp01(_settings.MorphGlowMaxScale) : 0f;
        }
    }
}
