using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace MainGameGlow
{
    public sealed partial class Plugin
    {
        private const int CaptureGlowVolumeLayer = 31;

        private static readonly FieldInfo PostProcessLayerResourcesField =
            typeof(PostProcessLayer).GetField("m_Resources", BindingFlags.Instance | BindingFlags.NonPublic);

        private bool EnsureCaptureGlowPipeline(Camera src)
        {
            if (_captureGlowInitFailed)
            {
                LogGlowDecision("pipeline blocked: previous init failed");
                return false;
            }

            if (_captureCamera == null)
            {
                LogGlowDecision("pipeline pending: capture camera is null");
                return false;
            }

            if (_capturePostProcessLayer != null && _capturePostProcessVolume != null && _captureBloom != null)
            {
                LogGlowDecision("pipeline ready: existing post-process components reused");
                return true;
            }

            try
            {
                PostProcessLayer template = FindPostProcessLayerTemplate(src);
                PostProcessResources resources = ResolvePostProcessResources(template);

                if (resources == null)
                {
                    LogGlowDecision("pipeline pending: PostProcessResources not found");
                    return false;
                }

                if (_capturePostProcessLayer == null)
                    _capturePostProcessLayer = _captureCamera.gameObject.AddComponent<PostProcessLayer>();

                if (template != null)
                {
                    string templateJson = JsonUtility.ToJson(template);
                    JsonUtility.FromJsonOverwrite(templateJson, _capturePostProcessLayer);
                }
                else
                {
                    _capturePostProcessLayer.Init(resources);
                }

                if (PostProcessLayerResourcesField != null)
                    PostProcessLayerResourcesField.SetValue(_capturePostProcessLayer, resources);

                _capturePostProcessLayer.volumeTrigger = _captureCamera.transform;
                _capturePostProcessLayer.volumeLayer = 1 << CaptureGlowVolumeLayer;
                _capturePostProcessLayer.antialiasingMode = PostProcessLayer.Antialiasing.None;
                _capturePostProcessLayer.enabled = false;

                if (_captureGlowVolumeRoot == null)
                {
                    _captureGlowVolumeRoot = new GameObject("MainGameGlowVolume");
                    _captureGlowVolumeRoot.hideFlags = HideFlags.DontSave;
                    _captureGlowVolumeRoot.layer = CaptureGlowVolumeLayer;
                    _captureGlowVolumeRoot.transform.SetParent(_cameraRoot.transform, false);
                }

                if (_capturePostProcessVolume == null)
                    _capturePostProcessVolume = _captureGlowVolumeRoot.AddComponent<PostProcessVolume>();

                if (_capturePostProcessProfile != null)
                    Destroy(_capturePostProcessProfile);

                _capturePostProcessProfile = ScriptableObject.CreateInstance<PostProcessProfile>();
                _capturePostProcessProfile.hideFlags = HideFlags.DontSave;

                _captureBloom = _capturePostProcessProfile.AddSettings<Bloom>();
                _captureBloom.enabled.Override(true);
                _captureBloom.fastMode.Override(true);
                _captureBloom.softKnee.Override(0.5f);
                _captureBloom.clamp.Override(65472f);
                _captureBloom.color.Override(Color.white);
                _captureBloom.dirtIntensity.Override(0f);
                _captureBloom.dirtTexture.Override(null);
                _captureBloom.anamorphicRatio.Override(0f);

                _capturePostProcessVolume.isGlobal = true;
                _capturePostProcessVolume.priority = 10000f;
                _capturePostProcessVolume.weight = 1f;
                _capturePostProcessVolume.sharedProfile = _capturePostProcessProfile;
                _capturePostProcessVolume.enabled = false;

                string templateName = template != null ? template.gameObject.name : "(manual)";
                LogGlowDecision("pipeline created: template=" + templateName + ", volumeLayer=" + CaptureGlowVolumeLayer, force: true);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("capture glow init failed: " + ex.Message);
                _captureGlowInitFailed = true;
                LogGlowDecision("pipeline error: init failed (" + ex.Message + ")", force: true);
                CleanupCaptureGlowPipeline();
                return false;
            }
        }

        private PostProcessLayer FindPostProcessLayerTemplate(Camera src)
        {
            if (src != null)
            {
                PostProcessLayer onSrc = src.GetComponent<PostProcessLayer>();
                if (onSrc != null)
                    return onSrc;
            }

            PostProcessLayer[] activeLayers = FindObjectsOfType<PostProcessLayer>();
            for (int i = 0; i < activeLayers.Length; i++)
            {
                PostProcessLayer layer = activeLayers[i];
                if (layer == null || layer == _capturePostProcessLayer)
                    continue;
                return layer;
            }

            PostProcessLayer[] allLayers = Resources.FindObjectsOfTypeAll<PostProcessLayer>();
            for (int i = 0; i < allLayers.Length; i++)
            {
                PostProcessLayer layer = allLayers[i];
                if (layer == null || layer == _capturePostProcessLayer)
                    continue;
                if (layer.hideFlags == HideFlags.HideAndDontSave)
                    continue;
                return layer;
            }

            if (!_captureGlowTemplateWarned)
            {
                Logger.LogWarning("capture glow pending: PostProcessLayer が見つからないためテンプレート無しで初期化します");
                _captureGlowTemplateWarned = true;
            }

            return null;
        }

        private PostProcessResources ResolvePostProcessResources(PostProcessLayer template)
        {
            if (template != null && PostProcessLayerResourcesField != null)
            {
                PostProcessResources templateResources = PostProcessLayerResourcesField.GetValue(template) as PostProcessResources;
                if (templateResources != null)
                {
                    _captureGlowTemplateWarned = false;
                    return templateResources;
                }
            }

            PostProcessResources[] resources = Resources.FindObjectsOfTypeAll<PostProcessResources>();
            if (resources != null && resources.Length > 0)
            {
                _captureGlowTemplateWarned = false;
                return resources[0];
            }

            return null;
        }

        private void ApplyCaptureGlowSettings(bool pipelineReady)
        {
            bool enableGlow = IsGlowRequested() || (_externalGlowActive && _externalGlowStrength > 0.0001f);

            if (!enableGlow)
            {
                if (_capturePostProcessLayer != null) _capturePostProcessLayer.enabled = false;
                if (_capturePostProcessVolume != null) _capturePostProcessVolume.enabled = false;

                string reason = "disabled by config:";
                if (_cfgGlowStrength == null || _cfgGlowBlurPercent == null)
                    reason += " config not ready";
                else if (_cfgGlowStrength.Value <= 0.0001f)
                    reason += " strength<=0";
                else if (_cfgGlowBlurPercent.Value <= 0.0001f)
                    reason += " blur<=0";
                else
                    reason += " unknown";

                LogGlowDecision(reason);
                return;
            }

            if (!pipelineReady || _captureBloom == null || _capturePostProcessLayer == null || _capturePostProcessVolume == null)
                return;

            // 外部が焚いている間は、そちらが指定した閾値/強さを優先する（未指定なら設定値のまま）。
            float threshold = Mathf.Clamp(
                _externalGlowThreshold >= 0f ? _externalGlowThreshold : _cfgGlowThreshold.Value, 0f, 5f);
            float strength = Mathf.Clamp(
                _externalGlowStrength >= 0f ? _externalGlowStrength : _cfgGlowStrength.Value, 0f, 10f);
            float blur01 = Mathf.Clamp01(_cfgGlowBlurPercent.Value / 100f);
            float diffusion = Mathf.Lerp(1f, 10f, blur01);

            _captureBloom.threshold.Override(threshold);
            _captureBloom.intensity.Override(strength);
            _captureBloom.diffusion.Override(diffusion);

            _capturePostProcessVolume.enabled = true;
            _capturePostProcessLayer.enabled = true;

            LogGlowDecision(
                $"active: threshold={threshold:0.##}, strength={strength:0.##}, blur%={_cfgGlowBlurPercent.Value:0.##}, diffusion={diffusion:0.##}");
        }

        private void CleanupCaptureGlowPipeline()
        {
            if (_capturePostProcessVolume != null)
            {
                _capturePostProcessVolume.sharedProfile = null;
                Destroy(_capturePostProcessVolume);
                _capturePostProcessVolume = null;
            }

            if (_captureGlowVolumeRoot != null)
            {
                Destroy(_captureGlowVolumeRoot);
                _captureGlowVolumeRoot = null;
            }

            if (_capturePostProcessLayer != null)
            {
                Destroy(_capturePostProcessLayer);
                _capturePostProcessLayer = null;
            }

            if (_capturePostProcessProfile != null)
            {
                Destroy(_capturePostProcessProfile);
                _capturePostProcessProfile = null;
            }

            _captureBloom = null;
            LogGlowDecision("pipeline cleaned up", force: true);
        }
    }
}
