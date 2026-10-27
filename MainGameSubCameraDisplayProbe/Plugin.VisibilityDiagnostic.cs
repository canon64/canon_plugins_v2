using System;
using System.IO;
using System.Text;
using UnityEngine;
namespace MainGameSubCameraDisplayProbe {
    public sealed partial class Plugin {
        private System.Reflection.MethodInfo _visibilityReadyActor;
        private float _visibilityDiagnosticAt;
        private int _visibilityDiagnosticSamples;
        private void TickVisibilityDiagnostic() {
            if (_settings == null || !_settings.LoggingEnabled) return;
            if (_subCamera == null || _visibilityDiagnosticSamples >= 12 || Time.unscaledTime < _visibilityDiagnosticAt) return;
            _visibilityDiagnosticAt = Time.unscaledTime + 2f;
            if (_visibilityReadyActor == null) {
                Type api = null;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) { api = assembly.GetType("MainGameVMDPlay.IndependentActorApi", false); if (api != null) break; }
                if (api == null) return;
                _visibilityReadyActor = api.GetMethod("GetReadyActor", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            }
            if (_visibilityReadyActor == null || _visibilityReadyActor.Invoke(null, null) as ChaControl == null) return;
            var actors = UnityEngine.Object.FindObjectsOfType<ChaControl>();
            _visibilityDiagnosticSamples++;
            try {
                var text = new StringBuilder();
                text.AppendLine(DateTime.UtcNow.ToString("o") + " lease=" + _actorLease + " charactersOnly=" + _settings.CharactersOnlyMode + " applying=" + _charactersOnlyApplying);
                text.AppendLine("camera=" + _subCamera.name + " position=" + _subCamera.transform.position + " rotation=" + _subCamera.transform.eulerAngles + " mask=" + _subCamera.cullingMask.ToString("X8") + " enabled=" + _subCamera.enabled);
                var planes = GeometryUtility.CalculateFrustumPlanes(_subCamera);
                foreach (var actor in actors) {
                    if (actor == null) continue;
                    text.AppendLine("actor=" + actor.name + " id=" + actor.GetInstanceID() + " independent=" + (actor.GetComponent("IndependentActorMarker") != null) + " pos=" + actor.transform.position + " active=" + actor.gameObject.activeInHierarchy + " visibleAll=" + actor.visibleAll);
                    foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true)) {
                        if (renderer == null) continue;
                        text.AppendLine(" renderer=" + renderer.name + " enabled=" + renderer.enabled + " active=" + renderer.gameObject.activeInHierarchy + " layer=" + renderer.gameObject.layer + " maskIncludes=" + ((_subCamera.cullingMask & (1 << renderer.gameObject.layer)) != 0) + " inFrustum=" + GeometryUtility.TestPlanesAABB(planes, renderer.bounds) + " center=" + renderer.bounds.center);
                    }
                }
                string path = Path.Combine(_pluginDir, "CharacterVisibility.log");
                if (File.Exists(path) && new FileInfo(path).Length > 524288) File.WriteAllText(path, "");
                File.AppendAllText(path, text.ToString());
            } catch (Exception ex) { Logger.LogWarning("Visibility diagnostic: " + ex.Message); }
        }
    }
}
