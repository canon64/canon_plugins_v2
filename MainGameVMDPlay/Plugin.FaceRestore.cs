using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private sealed class FaceState
        {
            internal FBSBase Control;
            internal float FixedRate, OpenMax;
            internal Dictionary<int, float> Weights;
            internal FaceState(FBSBase control)
            {
                Control = control;
                FixedRate = control.FixedRate;
                OpenMax = control.OpenMax;
                Weights = new Dictionary<int, float>((Dictionary<int, float>)AccessTools.Field(typeof(FBSBase), "dictNowFace").GetValue(control));
            }
            internal void Restore()
            {
                Control.FixedRate = FixedRate;
                Control.OpenMax = OpenMax;
                var weights = (Dictionary<int, float>)AccessTools.Field(typeof(FBSBase), "dictFace").GetValue(Control);
                weights.Clear();
                foreach (var pair in Weights) weights[pair.Key] = pair.Value;
                Control.ChangeFace(false);
            }
        }
        private ChaControl _faceChara;
        private FaceState[] _faceBefore;
        private int _eyesPattern;
        private byte _tongueState;

        private void CaptureFaceControl()
        {
            if (_faceBefore != null || _trialChara == null) return;
            var c = _trialChara;
            if (c.eyebrowCtrl == null || c.eyesCtrl == null || c.mouthCtrl == null) return;
            var states = new[] { new FaceState(c.eyebrowCtrl), new FaceState(c.eyesCtrl), new FaceState(c.mouthCtrl) };
            _faceChara = c;
            _eyesPattern = c.fileStatus.eyesPtn;
            _tongueState = c.fileStatus.tongueState;
            _faceBefore = states;
        }
        private void RestoreFaceControl()
        {
            try
            {
                var face = _controller == null ? null : AccessTools.Field(_controller.GetType(), "faceController")?.GetValue(_controller) as Behaviour;
                if (face != null) face.enabled = false;
                if (_faceBefore == null || _faceChara == null) return;
                if (_faceChara.fileStatus.eyesPtn != _eyesPattern) _faceChara.ChangeEyesPtn(_eyesPattern, false);
                _faceChara.fileStatus.tongueState = _tongueState;
                foreach (var state in _faceBefore) state.Restore();
            }
            catch (Exception ex) { LogError("[Release] face restore failed: " + ex); }
            finally { _faceBefore = null; _faceChara = null; }
        }
        private void ResumeFaceControl()
        {
            if (!_vmdOwnsCharacter || _controller == null || !(bool)ReadMember(_controller, "faceAnimeEnabled")) return;
            if (_faceBefore == null) CaptureFaceControl();
            var face = AccessTools.Field(_controller.GetType(), "faceController")?.GetValue(_controller) as Behaviour;
            if (face != null) face.enabled = true;
        }
    }
}
