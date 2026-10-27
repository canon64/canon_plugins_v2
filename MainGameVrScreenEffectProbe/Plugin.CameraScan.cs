using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using VRGIN.Core;

namespace MainGameVrScreenEffectProbe
{
    public sealed partial class Plugin
    {
        private void ScanAndAttach(string reason)
        {
            if (_cfgEnabled == null)
                return;

            bool vrActive = SafeVrActive();
            if (vrActive != _lastVrActive)
            {
                _lastVrActive = vrActive;
                LogVrState("scan-vr-change");
            }

            Log(ProbeLogLevel.Debug, "scan start reason=" + reason + " enabled=" + RuntimeEnabled + " vrActive=" + vrActive);

            if (!RuntimeEnabled)
            {
                _lastCandidateCount = 0;
                _lastAttachedCount = 0;
                _lastCameraSummary = "disabled";
                DetachAllLayers("scan-disabled");
                return;
            }

            if (_cfgOnlyVrActive != null && _cfgOnlyVrActive.Value && !vrActive)
            {
                _lastCandidateCount = 0;
                _lastAttachedCount = 0;
                _lastCameraSummary = "VR inactive; no layer attached";
                DetachAllLayers("vr-inactive");
                return;
            }

            Camera steamCam = TryGetSteamCamera();
            List<Camera> candidates = CollectCandidateCameras(steamCam);
            _lastCandidateCount = candidates.Count;

            List<Camera> selected = SelectTargetCameras(candidates, steamCam);
            AttachSelectedLayers(selected, reason);
            DetachRemovedLayers(selected, reason);

            _lastAttachedCount = _attachedLayers.Count(l => l != null);
            if ((_cfgUiVisible != null && _cfgUiVisible.Value) || (_fileLogger != null && _fileLogger.RuntimeEnabled))
            {
                string summary = BuildCameraSummary(candidates, selected, steamCam);
                if (!string.Equals(_lastCameraSummary, summary, StringComparison.Ordinal))
                {
                    _lastCameraSummary = summary;
                    Log(ProbeLogLevel.Info, "camera summary changed reason=" + reason + Environment.NewLine + summary);
                }
                else
                {
                    Log(ProbeLogLevel.Trace, "camera summary unchanged reason=" + reason);
                }
            }
        }

        private Camera TryGetSteamCamera()
        {
            try
            {
                if (VR.Camera == null || VR.Camera.SteamCam == null)
                    return null;

                Component steamComponent = VR.Camera.SteamCam as Component;
                if (steamComponent == null)
                    return null;

                Camera camera = steamComponent.GetComponent<Camera>();
                if (camera != null)
                    Log(ProbeLogLevel.Trace, "steam camera resolved name=" + camera.name + " id=" + camera.GetInstanceID());
                return camera;
            }
            catch (Exception ex)
            {
                Log(ProbeLogLevel.Warning, "steam camera resolve failed: " + ex.Message);
                return null;
            }
        }

        private List<Camera> CollectCandidateCameras(Camera steamCam)
        {
            var result = new List<Camera>();
            string filter = _cfgCameraNameFilter != null ? (_cfgCameraNameFilter.Value ?? string.Empty).Trim() : string.Empty;
            Camera[] cameras;
            try
            {
                cameras = Camera.allCameras ?? new Camera[0];
            }
            catch (Exception ex)
            {
                Log(ProbeLogLevel.Warning, "Camera.allCameras failed: " + ex.Message);
                cameras = new Camera[0];
            }

            if (steamCam != null)
                AddUniqueCamera(result, steamCam);

            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (!IsCandidateCamera(cam, steamCam, filter))
                    continue;
                AddUniqueCamera(result, cam);
            }

            if (result.Count == 0 && _cfgAttachToMainFallback != null && _cfgAttachToMainFallback.Value && Camera.main != null)
            {
                AddUniqueCamera(result, Camera.main);
                Log(ProbeLogLevel.Info, "main camera fallback selected name=" + Camera.main.name);
            }

            return result;
        }

        private bool IsCandidateCamera(Camera cam, Camera steamCam, string filter)
        {
            if (cam == null || !cam.enabled || !cam.gameObject.activeInHierarchy)
                return false;

            string name = cam.name ?? string.Empty;
            if (!string.IsNullOrEmpty(filter) && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            if (ReferenceEquals(cam, steamCam))
                return true;

            if (cam.stereoEnabled || cam.stereoTargetEye != StereoTargetEyeMask.None)
                return true;

            string lower = name.ToLowerInvariant();
            if (lower.Contains("vr") || lower.Contains("hmd") || lower.Contains("eye") ||
                lower.Contains("left") || lower.Contains("right") || lower.Contains("steam") ||
                lower.Contains("openxr") || lower.Contains("vrcam"))
                return true;

            return SafeVrActive() && cam.targetTexture == null;
        }

        private static void AddUniqueCamera(List<Camera> list, Camera cam)
        {
            if (cam == null)
                return;
            int id = cam.GetInstanceID();
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && list[i].GetInstanceID() == id)
                    return;
            }
            list.Add(cam);
        }

        private List<Camera> SelectTargetCameras(List<Camera> candidates, Camera steamCam)
        {
            if (candidates == null || candidates.Count == 0)
                return new List<Camera>();
            if (_cfgAttachToAllCandidates == null || _cfgAttachToAllCandidates.Value)
                return new List<Camera>(candidates);

            Camera best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                Camera cam = candidates[i];
                float score = ScoreCamera(cam, steamCam);
                if (score > bestScore)
                {
                    best = cam;
                    bestScore = score;
                }
            }

            return best != null ? new List<Camera> { best } : new List<Camera>();
        }

        private float ScoreCamera(Camera cam, Camera steamCam)
        {
            if (cam == null)
                return float.MinValue;

            float score = cam.depth;
            if (ReferenceEquals(cam, steamCam))
                score += 1000f;
            if (cam.stereoEnabled)
                score += 200f;
            if (cam.stereoTargetEye != StereoTargetEyeMask.None)
                score += 100f;

            string lower = (cam.name ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("vr") || lower.Contains("hmd"))
                score += 60f;
            if (lower.Contains("eye") || lower.Contains("left") || lower.Contains("right"))
                score += 40f;
            return score;
        }

        private void AttachSelectedLayers(List<Camera> selected, string reason)
        {
            for (int i = 0; i < selected.Count; i++)
            {
                Camera cam = selected[i];
                if (cam == null)
                    continue;

                VrEyeScreenEffectLayer layer = cam.GetComponent<VrEyeScreenEffectLayer>();
                bool created = false;
                if (layer == null)
                {
                    layer = cam.gameObject.AddComponent<VrEyeScreenEffectLayer>();
                    created = true;
                }

                layer.Owner = this;
                if (!_attachedLayers.Contains(layer))
                    _attachedLayers.Add(layer);

                Log(created ? ProbeLogLevel.Info : ProbeLogLevel.Trace,
                    "layer " + (created ? "attached" : "kept")
                    + " reason=" + reason
                    + " cam=" + DescribeCameraOneLine(cam));
            }
        }

        private void DetachRemovedLayers(List<Camera> selected, string reason)
        {
            var selectedIds = new HashSet<int>();
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] != null)
                    selectedIds.Add(selected[i].GetInstanceID());
            }

            for (int i = _attachedLayers.Count - 1; i >= 0; i--)
            {
                VrEyeScreenEffectLayer layer = _attachedLayers[i];
                if (layer == null)
                {
                    _attachedLayers.RemoveAt(i);
                    continue;
                }

                Camera cam = layer.GetComponent<Camera>();
                if (cam != null && selectedIds.Contains(cam.GetInstanceID()))
                    continue;

                layer.Owner = null;
                _attachedLayers.RemoveAt(i);
                Log(ProbeLogLevel.Info, "layer detached reason=" + reason + " cam=" + (cam != null ? DescribeCameraOneLine(cam) : "null"));
                Destroy(layer);
            }
        }

        private void DetachAllLayers(string reason)
        {
            for (int i = _attachedLayers.Count - 1; i >= 0; i--)
            {
                VrEyeScreenEffectLayer layer = _attachedLayers[i];
                if (layer == null)
                {
                    _attachedLayers.RemoveAt(i);
                    continue;
                }

                Camera cam = layer.GetComponent<Camera>();
                layer.Owner = null;
                Log(ProbeLogLevel.Info, "layer detached reason=" + reason + " cam=" + (cam != null ? DescribeCameraOneLine(cam) : "null"));
                Destroy(layer);
                _attachedLayers.RemoveAt(i);
            }
            _lastAttachedCount = 0;
        }

        private void ResetAllLayerBuffers(string reason)
        {
            for (int i = 0; i < _attachedLayers.Count; i++)
            {
                if (_attachedLayers[i] != null)
                    _attachedLayers[i].ResetBuffers();
            }
            Log(ProbeLogLevel.Info, "layer buffers reset reason=" + reason + " count=" + _attachedLayers.Count);
        }

        internal void NoteRender(Camera cam, VrEyeKind eye, int width, int height)
        {
            switch (eye)
            {
                case VrEyeKind.Left:
                    _renderLeft++;
                    break;
                case VrEyeKind.Right:
                    _renderRight++;
                    break;
                default:
                    _renderMono++;
                    break;
            }

            bool logging = _fileLogger != null && _fileLogger.RuntimeEnabled;
            if (!logging && (_cfgUiVisible == null || !_cfgUiVisible.Value))
                return;

            _lastRenderInfo = "cam=" + (cam != null ? cam.name : "null")
                + " eye=" + eye
                + " size=" + width + "x" + height
                + " stereoEnabled=" + (cam != null && cam.stereoEnabled)
                + " targetEye=" + (cam != null ? cam.stereoTargetEye.ToString() : "?")
                + " activeEye=" + (cam != null ? cam.stereoActiveEye.ToString() : "?");

            if (logging)
            {
                string sig = (cam != null ? cam.GetInstanceID().ToString() : "null")
                    + "|" + (cam != null ? cam.name : "null")
                    + "|" + eye
                    + "|" + width + "x" + height
                    + "|" + (cam != null ? cam.stereoEnabled.ToString() : "?")
                    + "|" + (cam != null ? cam.stereoTargetEye.ToString() : "?")
                    + "|" + (cam != null ? cam.stereoActiveEye.ToString() : "?");
                if (_renderSignatureLogged.Add(sig))
                    Log(ProbeLogLevel.Info, "render signature " + _lastRenderInfo);

                Log(ProbeLogLevel.Trace, "render " + _lastRenderInfo);

                float interval = _cfgRenderSummaryIntervalSec != null ? Mathf.Max(0.1f, _cfgRenderSummaryIntervalSec.Value) : 1f;
                if (Time.unscaledTime >= _nextRenderSummaryTime)
                {
                    _nextRenderSummaryTime = Time.unscaledTime + interval;
                    Log(ProbeLogLevel.Debug, "render summary left=" + _renderLeft + " right=" + _renderRight + " mono=" + _renderMono + " last=" + _lastRenderInfo);
                }
            }
        }

        private string BuildCameraSummary(List<Camera> candidates, List<Camera> selected, Camera steamCam)
        {
            var selectedIds = new HashSet<int>();
            for (int i = 0; i < selected.Count; i++)
            {
                if (selected[i] != null)
                    selectedIds.Add(selected[i].GetInstanceID());
            }

            var sb = new StringBuilder();
            sb.Append("candidates=").Append(candidates.Count)
                .Append(" selected=").Append(selected.Count)
                .Append(" attachAll=").Append(_cfgAttachToAllCandidates != null && _cfgAttachToAllCandidates.Value)
                .Append(" filter=").Append(_cfgCameraNameFilter != null ? _cfgCameraNameFilter.Value : string.Empty)
                .AppendLine();

            for (int i = 0; i < candidates.Count; i++)
            {
                Camera cam = candidates[i];
                if (cam == null)
                    continue;

                bool selectedNow = selectedIds.Contains(cam.GetInstanceID());
                sb.Append(selectedNow ? "[x] " : "[ ] ")
                    .Append(DescribeCameraOneLine(cam))
                    .Append(" steam=").Append(ReferenceEquals(cam, steamCam))
                    .Append(" score=").Append(ScoreCamera(cam, steamCam).ToString("0.##"))
                    .AppendLine();

                string summaryKey = cam.GetInstanceID()
                    + "|" + cam.name
                    + "|" + cam.stereoEnabled
                    + "|" + cam.stereoTargetEye
                    + "|" + cam.targetTexture;
                if (_cameraSummaryLogged.Add(summaryKey))
                    Log(ProbeLogLevel.Debug, "camera candidate " + DescribeCameraOneLine(cam) + " steam=" + ReferenceEquals(cam, steamCam));
            }

            return sb.ToString().TrimEnd();
        }

        private static string DescribeCameraOneLine(Camera cam)
        {
            if (cam == null)
                return "null";

            string targetTexture = cam.targetTexture != null
                ? cam.targetTexture.width + "x" + cam.targetTexture.height
                : "screen";

            return "id=" + cam.GetInstanceID()
                + " name=" + cam.name
                + " enabled=" + cam.enabled
                + " active=" + cam.gameObject.activeInHierarchy
                + " depth=" + cam.depth.ToString("0.##")
                + " pixel=" + cam.pixelWidth + "x" + cam.pixelHeight
                + " stereoEnabled=" + cam.stereoEnabled
                + " stereoTargetEye=" + cam.stereoTargetEye
                + " activeEye=" + cam.stereoActiveEye
                + " target=" + targetTexture;
        }
    }
}
