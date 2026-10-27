using BepInEx.Configuration;
using UnityEngine;

namespace MainGameVMDPlay {
    public sealed partial class Plugin {
        private ConfigEntry<bool> _actorEyesCamera, _actorFaceCamera;
        private ChaControl _lookActor;
        private EyeLookController _lookEyes;
        private NeckLookControllerVer2 _lookNeck;
        private bool _eyesOverridden, _neckOverridden, _eyesEnabled, _neckEnabled, _neckSkip;
        private bool _neckCalcEnabled;
        private int _lookEyesPattern, _neckPattern;
        private float _neckRate;
        private Transform _eyesTarget, _neckTarget;
        private Camera _lookCamera;
        private float _lookCameraCheck;
        private void InitActorLook() {
            InitNeckDiagnostics();
            _actorEyesCamera = Config.Bind("IndependentActor", "EyesLookAtCamera", false, "呼出キャラの目をメインカメラへ向ける");
            _actorFaceCamera = Config.Bind("IndependentActor", "FaceLookAtCamera", false, "呼出キャラの顔をメインカメラへ向ける（目とは独立）");
            gameObject.AddComponent<IndependentActorLookDriver>().Owner = this;
        }
        private void DrawActorLook() {
            if (!IsIndependentTarget || !_actorReady) return;
            bool eyes = GUILayout.Toggle(_actorEyesCamera.Value, "目をこちらに向ける");
            bool face = GUILayout.Toggle(_actorFaceCamera.Value, "顔をこちらに向ける");
            if (eyes != _actorEyesCamera.Value || face != _actorFaceCamera.Value) {
                _actorEyesCamera.Value = eyes; _actorFaceCamera.Value = face; Config.Save();
            }
        }
        internal void TickActorLook() {
            if (AstralBlocked) return;
            var actor = ReadyIndependentActor();
            if (!_cfgEnabled.Value || actor == null) { RestoreActorLook(); return; }
            if (_lookActor != actor || _lookEyes != actor.eyeLookCtrl || _lookNeck != actor.neckLookCtrl) {
                RestoreActorLook(); _lookActor = actor; _lookEyes = actor.eyeLookCtrl; _lookNeck = actor.neckLookCtrl;
            }
            if (Time.unscaledTime >= _lookCameraCheck) { _lookCameraCheck = Time.unscaledTime + 1f; _lookCamera = _hSceneProc != null && _hSceneProc.flags != null && _hSceneProc.flags.ctrlCamera != null ? _hSceneProc.flags.ctrlCamera.thisCmaera : Camera.main; }
            bool eyes = _actorEyesCamera.Value && _lookCamera != null;
            bool face = _actorFaceCamera.Value && _lookCamera != null;
            if (!eyes) RestoreActorEyes();
            else if (_lookEyes != null) {
                if (!_eyesOverridden) {
                    _eyesTarget = _lookEyes.target; _eyesEnabled = _lookEyes.enabled; _lookEyesPattern = actor.GetLookEyesPtn(); _eyesOverridden = true;
                }
                _lookEyes.target = _lookCamera.transform; _lookEyes.enabled = true; actor.ChangeLookEyesPtn(1);
            }
            if (!face) RestoreActorNeck();
            else if (_lookNeck != null) {
                if (!_neckOverridden) {
                    _neckTarget = _lookNeck.target; _neckEnabled = _lookNeck.enabled; _neckPattern = actor.GetLookNeckPtn();
                    _neckRate = _lookNeck.rate; _neckSkip = _lookNeck.neckLookScript != null && _lookNeck.neckLookScript.skipCalc; _neckCalcEnabled = _lookNeck.neckLookScript != null && _lookNeck.neckLookScript.isEnabled; _neckOverridden = true;
                }
                _lookNeck.target = _lookCamera.transform; _lookNeck.enabled = true; actor.ChangeLookNeckPtn(1, 1f);
                if (_lookNeck.neckLookScript != null) { _lookNeck.neckLookScript.skipCalc = false; _lookNeck.neckLookScript.isEnabled = true; }
                _lookNeck.ForceLateUpdate();
            }
            if (eyes && _lookEyes != null) {
                bool previous = EyeLookCalc.isEnabled;
                try { EyeLookCalc.isEnabled = true; _lookEyes.ForceLateUpdate(); }
                finally { EyeLookCalc.isEnabled = previous; }
            }
        }
        private void RestoreActorEyes() {
            if (!_eyesOverridden) return;
            if (_lookEyes != null) { _lookEyes.target = _eyesTarget; _lookEyes.enabled = _eyesEnabled; _lookEyes.ptnNo = _lookEyesPattern; }
            if (_lookActor != null) _lookActor.fileStatus.eyesLookPtn = _lookEyesPattern;
            _eyesOverridden = false;
        }
        private void RestoreActorNeck() {
            if (!_neckOverridden) return;
            if (_lookNeck != null) {
                _lookNeck.target = _neckTarget; _lookNeck.enabled = _neckEnabled; _lookNeck.ptnNo = _neckPattern; _lookNeck.rate = _neckRate;
                if (_lookNeck.neckLookScript != null) { _lookNeck.neckLookScript.skipCalc = _neckSkip; _lookNeck.neckLookScript.isEnabled = _neckCalcEnabled; }
            }
            if (_lookActor != null) _lookActor.fileStatus.neckLookPtn = _neckPattern;
            _neckOverridden = false;
        }
        private void RestoreActorLook() {
            RestoreActorEyes(); RestoreActorNeck(); _lookActor = null; _lookEyes = null; _lookNeck = null;
        }
    }
    [DefaultExecutionOrder(30000)]
    public sealed class IndependentActorLookDriver : MonoBehaviour {
        internal Plugin Owner;
        private void LateUpdate() { if (Owner != null) { Owner.TraceActorLookBefore(); Owner.TickActorLook(); Owner.TraceActorLookAfter(); } }
    }
}
