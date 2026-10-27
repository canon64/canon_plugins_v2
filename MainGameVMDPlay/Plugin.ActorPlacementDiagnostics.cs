using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGameVMDPlay {
    public sealed partial class Plugin {
        private Coroutine _placementDiagnostic;
        private string _placementBefore, _placementImmediate;
        private int _placementRevision;
        private float _placementChangedAt;
        private string DescribeActorPlacement(string phase, ChaControl actor) {
            var text = new StringBuilder();
            text.AppendLine(phase + " frame=" + Time.frameCount + " state=" + _state + " independent=" + IsIndependentTarget + " owns=" + _vmdOwnsCharacter);
            AppendPlacementTransform(text, "stage", _actorStage == null ? null : _actorStage.transform);
            AppendPlacementTransform(text, "actor", actor == null ? null : actor.transform);
            AppendPlacementTransform(text, "objRoot", actor == null || actor.objRoot == null ? null : actor.objRoot.transform);
            AppendPlacementTransform(text, "objTop", actor == null || actor.objTop == null ? null : actor.objTop.transform);
            AppendPlacementTransform(text, "objAnim", actor == null || actor.objAnim == null ? null : actor.objAnim.transform);
            AppendPlacementTransform(text, "bodyBone", actor == null || actor.objBodyBone == null ? null : actor.objBodyBone.transform);
            if (actor != null && actor.animBody != null) text.AppendLine("animator enabled=" + actor.animBody.enabled + " rootMotion=" + actor.animBody.applyRootMotion);
            return text.ToString();
        }
        private static void AppendPlacementTransform(StringBuilder text, string label, Transform transform) {
            text.Append(label).Append(": ");
            if (transform == null) { text.AppendLine("null"); return; }
            text.Append(transform.name).Append('#').Append(transform.GetInstanceID())
                .Append(" world=").Append(transform.position.ToString("F4"))
                .Append(" angles=").Append(transform.eulerAngles.ToString("F3"))
                .Append(" local=").Append(transform.localPosition.ToString("F4"))
                .Append(" scale=").Append(transform.lossyScale.ToString("F4"));
            text.Append(" parents=");
            for (var parent = transform.parent; parent != null; parent = parent.parent)
                text.Append(parent.name).Append('#').Append(parent.GetInstanceID()).Append('/');
            text.AppendLine();
        }
        private void QueueActorPlacementDiagnostic(ChaControl actor, string before) {
            if (!_diagnosticsEnabled) return;
            _placementBefore = before;
            _placementImmediate = DescribeActorPlacement("immediate", actor);
            _placementRevision++; _placementChangedAt = Time.realtimeSinceStartup;
            if (_placementDiagnostic == null) _placementDiagnostic = StartCoroutine(RecordActorPlacement(actor, _actorRequest));
        }
        private IEnumerator RecordActorPlacement(ChaControl actor, int request) {
            yield return null;
            try {
                while (actor != null && request == _actorRequest) {
                    int revision = _placementRevision;
                    yield return new WaitForEndOfFrame();
                    if (actor == null || request != _actorRequest) yield break;
                    if (revision != _placementRevision || Time.realtimeSinceStartup - _placementChangedAt < .3f) { yield return null; continue; }
                    string text = DateTime.UtcNow.ToString("o") + "\n" + _placementBefore + _placementImmediate + DescribeActorPlacement("settled", actor);
                    try {
                        string path = Path.Combine(_pluginDir, "IndependentActorPlacement.log");
                        if (File.Exists(path) && new FileInfo(path).Length > 262144) File.WriteAllText(path, "");
                        File.AppendAllText(path, text);
                    } catch (Exception ex) { LogError("[ActorPlacement] " + ex.Message); }
                    yield break;
                }
            } finally { _placementDiagnostic = null; }
        }
    }
}
