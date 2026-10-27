using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private ChaControl _trialChara { get => _target.Actor; set => _target.Actor = value; }
        private Harmony _trialHarmony;
        private IEnumerator _trialIterator;
        private Coroutine _trialCoroutine;
        private int _trialSteps;
        private string _trialStatus = "idle";
        private string _trialLegPose = "";
        private Coroutine _poseProbe;
        private Component _trialPvCopy;

        private void PrepareDiagnosticTrial(ChaControl chara)
        {
            _trialChara = chara;
            var pvType = AccessTools.TypeByName("Studio.PVCopy");
            if (pvType == null) throw new InvalidOperationException("PVCopy type missing");
            _trialPvCopy = chara.GetComponentInChildren(pvType);
            if (_diagnosticsEnabled && _poseProbe == null) _poseProbe = StartCoroutine(ProbeLegPose());
            if (_trialHarmony != null) return;
            var t = _tCtrl.Assembly.GetType("KKVMDPlayPlugin.DefaultCharaAnimOverride", true);
            _trialHarmony = new Harmony(Guid + ".diagnostic");
            _trialHarmony.Patch(AccessTools.Method(typeof(MotionIK), "Calc", new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterMotionIkRecalculation)));
            _trialHarmony.Patch(AccessTools.Method(t, "DisableEnableDefaultAnim"),
                prefix: new HarmonyMethod(typeof(Plugin), nameof(DiagnosticOverridePrefix)));
            LogInfo("[Trial] scoped PVCopy absence guard installed");
        }

        private IEnumerator ProbeLegPose()
        {
            float next = 0;
            while (true) {
                yield return new WaitForEndOfFrame();
                if (Time.unscaledTime < next || _trialChara == null || _trialChara.objBodyBone == null) continue;
                next = Time.unscaledTime + 0.5f;
                var text = new System.Text.StringBuilder();
                foreach (var bone in _trialChara.objBodyBone.GetComponentsInChildren<Transform>(true)) {
                    if (!bone.name.StartsWith("cf_j_") ||
                        !(bone.name.Contains("thigh") || bone.name.Contains("leg") || bone.name.Contains("foot"))) continue;
                    text.Append(bone.name).Append(":p=").Append(bone.localPosition.ToString("F4"))
                        .Append(":r=").Append(bone.localRotation.ToString("F4")).Append(';');
                }
                _trialLegPose = text.ToString();
                DiagnosticLog("POSE t=" + VmdBridgeGetAnimTime(_controller) + " " + _trialLegPose);
            }
        }

        private static bool DiagnosticOverridePrefix(Component __instance)
        {
            var p = Instance;
            if (p == null || p._trialChara == null || __instance.gameObject != p._trialChara.gameObject) return true;
            return p._trialPvCopy != null;
        }

        private void StartDiagnosticLoad(string path, bool cancelAfterFirstYield)
        {
            if ((_controller as UnityEngine.Object) == null) throw new InvalidOperationException("No live controller");
            CancelDiagnosticLoad();
            _diagnosticError = "";
            var method = AccessTools.Method(_controller.GetType(), "ResetAndLoadVMDAnimationCo",
                new[] { typeof(string), typeof(bool), typeof(bool) });
            if (method == null) throw new MissingMethodException("ResetAndLoadVMDAnimationCo");
            _trialIterator = (IEnumerator)method.Invoke(_controller, new object[] { path, false, true });
            _trialSteps = 0;
            _trialStatus = "loading";
            LogInfo("[Trial] before " + TrialSnapshot());
            _trialCoroutine = StartCoroutine(RunDiagnosticLoad(cancelAfterFirstYield));
        }

        private IEnumerator RunDiagnosticLoad(bool cancelAfterFirstYield)
        {
            while (_trialIterator != null)
            {
                bool more = false;
                object next = null;
                Exception failure = null;
                try { more = _trialIterator.MoveNext(); if (more) next = _trialIterator.Current; }
                catch (Exception ex) { failure = ex; }
                _trialSteps++;
                if (failure != null) {
                    _diagnosticError = failure.ToString();
                    _trialStatus = "failed";
                    LogError("[Trial] load failed " + failure);
                    break;
                }
                if (!more) {
                    _trialStatus = "completed";
                    LogInfo("[Trial] completed " + TrialSnapshot());
                    break;
                }
                LogInfo("[Trial] yield " + _trialSteps + " " + TrialSnapshot());
                if (cancelAfterFirstYield) { _trialStatus = "cancelled-at-first-yield"; break; }
                yield return next;
            }
            (_trialIterator as IDisposable)?.Dispose();
            _trialIterator = null;
            _trialCoroutine = null;
            LogInfo("[Trial] exit " + _trialStatus + " " + TrialSnapshot());
        }

        private void CancelDiagnosticLoad()
        {
            if (_trialCoroutine != null) StopCoroutine(_trialCoroutine);
            (_trialIterator as IDisposable)?.Dispose();
            _trialCoroutine = null;
            _trialIterator = null;
        }

        private string TrialSnapshot()
        {
            if ((_controller as UnityEngine.Object) == null) return "controller=none";
            var t = _controller.GetType();
            object enabled = AccessTools.Property(t, "VMDAnimEnabled")?.GetValue(_controller, null);
            object ik = AccessTools.Method(t, "GetAnimIK")?.Invoke(_controller, new object[] { _trialChara });
            object animator = AccessTools.Method(t, "GetAnimBody")?.Invoke(_controller, new object[] { _trialChara });
            return "vmd=" + enabled + " animator=" + (animator as Behaviour)?.enabled +
                " ik=" + (ik as Behaviour)?.enabled + " active=" + VmdBridgeIsActive(_controller) +
                " time=" + VmdBridgeGetAnimTime(_controller) + " length=" + VmdBridgeGetAnimationLength();
        }
    }
}
