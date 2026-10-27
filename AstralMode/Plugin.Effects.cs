using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;
using UnityEngine.UI;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        // ---------------- White Canvas ----------------
        private GameObject _canvasRoot;
        private CanvasGroup _canvasGroup;

        internal void EnsureWhiteCanvas()
        {
            if (_canvasRoot != null) return;

            _canvasRoot = new GameObject("AstralMode_WhiteCanvas");
            Object.DontDestroyOnLoad(_canvasRoot);
            _canvasRoot.hideFlags = HideFlags.DontSave;

            Canvas canvas = _canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760; // 最前面

            CanvasScaler scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            _canvasGroup = _canvasRoot.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            GameObject imageGo = new GameObject("WhitePanel");
            imageGo.transform.SetParent(_canvasRoot.transform, false);

            Image img = imageGo.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;

            RectTransform rt = img.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            SimpleFileLogger.Info("White canvas created");
        }

        internal void SetCanvasAlpha(float a)
        {
            if (_canvasGroup == null) return;

            // Use WhiteCanvas が false なら、どの経路から来ても白を出さない。
            // ワープ中の傾斜は DriveWhiteCanvasMixer が設定を見ているが、突入/帰還それぞれの
            // ワープ末尾 SetCanvasAlpha(1f) とフェードの SetCanvasAlpha(1f - eased) は
            // ミキサを通らず直接叩いていたため、設定を切っても白が出ていた。ここで一括して止める。
            if (_cfgUseWhiteCanvas != null && !_cfgUseWhiteCanvas.Value)
                a = 0f;

            _canvasGroup.alpha = Mathf.Clamp01(a);
        }

        // ---------------- Animator Freeze ----------------
        private readonly List<Animator> _frozenAnimators = new List<Animator>();
        private readonly List<float> _frozenSpeeds = new List<float>();

        internal void FreezeAllCharacterAnimators()
        {
            _frozenAnimators.Clear();
            _frozenSpeeds.Clear();

            if (_hSceneProc == null)
            {
                SimpleFileLogger.Warn("FreezeAnimators: HSceneProc is null");
                return;
            }

            // HSceneProc の private fields を AccessTools で取得
            var lstFemale = AccessTools.Field(typeof(HSceneProc), "lstFemale")
                ?.GetValue(_hSceneProc) as System.Collections.IEnumerable;
            var male = AccessTools.Field(typeof(HSceneProc), "male")?.GetValue(_hSceneProc) as ChaControl;
            var male1 = AccessTools.Field(typeof(HSceneProc), "male1")?.GetValue(_hSceneProc) as ChaControl;

            CollectCharaAnimators(lstFemale);
            CollectChaAnimator(male);
            CollectChaAnimator(male1);

            SimpleFileLogger.Info($"Frozen {_frozenAnimators.Count} animators");
        }

        private void CollectCharaAnimators(System.Collections.IEnumerable list)
        {
            if (list == null) return;
            foreach (object obj in list)
            {
                CollectChaAnimator(obj as ChaControl);
            }
        }

        private void CollectChaAnimator(ChaControl cha)
        {
            if (cha == null) return;
            Animator anim = cha.animBody;
            if (anim != null)
            {
                _frozenSpeeds.Add(anim.speed);
                anim.speed = 0f;
                _frozenAnimators.Add(anim);
            }
        }

        internal void UnfreezeAllCharacterAnimators()
        {
            for (int i = 0; i < _frozenAnimators.Count; i++)
            {
                if (_frozenAnimators[i] != null)
                {
                    _frozenAnimators[i].speed = _frozenSpeeds[i];
                }
            }
            SimpleFileLogger.Info($"Unfrozen {_frozenAnimators.Count} animators");
            _frozenAnimators.Clear();
            _frozenSpeeds.Clear();
        }

        // ---------------- PostProcessing (Bloom) ----------------
        private PostProcessVolume _ppVolume;
        private PostProcessProfile _ppProfile;
        private Bloom _bloom;

        private static readonly FieldInfo PostProcessLayerResourcesField =
            typeof(PostProcessLayer).GetField("m_Resources", BindingFlags.Instance | BindingFlags.NonPublic);

        internal void EnsureBloom()
        {
            if (_bloom != null) return;

            try
            {
                GameObject volumeGo = new GameObject("AstralMode_Bloom");
                Object.DontDestroyOnLoad(volumeGo);
                volumeGo.hideFlags = HideFlags.DontSave;

                _ppVolume = volumeGo.AddComponent<PostProcessVolume>();
                _ppVolume.isGlobal = true;
                _ppVolume.priority = 9999f;
                _ppVolume.weight = 1f;

                _ppProfile = ScriptableObject.CreateInstance<PostProcessProfile>();
                _ppProfile.hideFlags = HideFlags.DontSave;

                _bloom = _ppProfile.AddSettings<Bloom>();
                _bloom.enabled.Override(true);
                _bloom.intensity.Override(0f);
                _bloom.threshold.Override(0.5f);
                _bloom.diffusion.Override(7f);
                _bloom.color.Override(Color.white);
                _bloom.fastMode.Override(true);

                _ppVolume.sharedProfile = _ppProfile;
                _ppVolume.enabled = false;

                SimpleFileLogger.Info("Bloom volume created");
            }
            catch (System.Exception ex)
            {
                SimpleFileLogger.Warn("EnsureBloom failed: " + ex.Message);
            }
        }

        internal void SetBloomIntensity(float intensity01)
        {
            if (_bloom == null || _ppVolume == null) return;

            float maxI = _cfgBloomMaxIntensity != null ? _cfgBloomMaxIntensity.Value : 8f;
            float threshold = _cfgBloomThreshold != null ? _cfgBloomThreshold.Value : 0.5f;

            _bloom.intensity.Override(intensity01 * maxI);
            _bloom.threshold.Override(threshold);
            _ppVolume.enabled = intensity01 > 0.001f;
        }

        // ---------------- Cleanup ----------------
        internal void DestroyWarpEffects()
        {
            UnfreezeAllCharacterAnimators();

            if (_ppVolume != null)
            {
                _ppVolume.sharedProfile = null;
                Object.Destroy(_ppVolume.gameObject);
                _ppVolume = null;
            }
            if (_ppProfile != null)
            {
                Object.Destroy(_ppProfile);
                _ppProfile = null;
            }
            _bloom = null;

            if (_canvasRoot != null)
            {
                Object.Destroy(_canvasRoot);
                _canvasRoot = null;
                _canvasGroup = null;
            }
        }
    }
}
