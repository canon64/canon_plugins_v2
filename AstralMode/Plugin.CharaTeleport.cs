using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private sealed class TransformSnapshot
        {
            public Transform Target;
            public Vector3 WorldPos;
            public Quaternion WorldRot;
        }

        private readonly List<TransformSnapshot> _charaSnapshots = new List<TransformSnapshot>();
        private readonly List<CharaFreezeClone.FrozenClone> _frozenClones = new List<CharaFreezeClone.FrozenClone>();
        private Vector3 _realWorldCharaCenter;
        private Vector3 _realWorldFemaleWaistCenter;
        private Vector3 _astralCharaCenter;
        private Vector3 _astralCharaOffset;
        private float _nextAstralCharaMonitorTime;
        private float _nextAstralCharaPositionDiagTime;
        private float _nextHSceneProcUpdateCharaDiagTime;
        private float _nextAstralCharaDriftDetailTime;
        private int _astralCharaDriftHits;
        private int _astralCharaReapplyHits;
        private int _lastLoggedCloneRootCount = -1;

        internal Vector3 GetAstralCharaCenter() => _astralCharaCenter;
        internal Vector3 GetRealWorldCharaCenter() => _realWorldCharaCenter;
        internal Vector3 GetRealWorldFemaleWaistCenter() => _realWorldFemaleWaistCenter != Vector3.zero ? _realWorldFemaleWaistCenter : _realWorldCharaCenter + Vector3.up * 0.9f;

        private static string TransformPath(Transform t)
        {
            if (t == null) return "null";
            var names = new List<string>();
            while (t != null)
            {
                names.Add(t.name);
                t = t.parent;
            }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static void LogCharaTransform(string tag, ChaControl c)
        {
            if (c == null)
            {
                SimpleFileLogger.Warn($"{tag}: cha=null");
                return;
            }

            Transform t = c.transform;
            Transform p = t.parent;
            SimpleFileLogger.Info($"{tag}: name={c.name}, go={c.gameObject.name}, active={c.gameObject.activeInHierarchy}, worldPos={t.position}, localPos={t.localPosition}, worldRot={t.rotation.eulerAngles}, localRot={t.localRotation.eulerAngles}, parent={(p != null ? p.name : "null")}, parentWorldPos={(p != null ? p.position.ToString() : "null")}, path={TransformPath(t)}");
        }

        private List<ChaControl> CollectCurrentHSceneCharas()
        {
            var charas = new List<ChaControl>();
            if (_hSceneProc == null) return charas;

            var lstFemale = AccessTools.Field(typeof(HSceneProc), "lstFemale")
                ?.GetValue(_hSceneProc) as System.Collections.IEnumerable;
            var male = AccessTools.Field(typeof(HSceneProc), "male")?.GetValue(_hSceneProc) as ChaControl;
            var male1 = AccessTools.Field(typeof(HSceneProc), "male1")?.GetValue(_hSceneProc) as ChaControl;

            if (lstFemale != null)
            {
                foreach (object o in lstFemale)
                    if (o is ChaControl c) charas.Add(c);
            }
            if (male != null) charas.Add(male);
            if (male1 != null) charas.Add(male1);
            return charas;
        }

        internal void TeleportCharasToAstral()
        {
            _charaSnapshots.Clear();
            _frozenClones.Clear();
            _realWorldFemaleWaistCenter = Vector3.zero;
            _astralCharaOffset = Vector3.zero;
            _astralCharaDriftHits = 0;
            _astralCharaReapplyHits = 0;
            _nextAstralCharaDriftDetailTime = 0f;
            _nextAstralCharaPositionDiagTime = 0f;
            _nextHSceneProcUpdateCharaDiagTime = 0f;
            _lastLoggedCloneRootCount = -1;

            if (_hSceneProc == null)
            {
                SimpleFileLogger.Warn("Teleport: HSceneProc is null");
                return;
            }

            var charas = CollectCurrentHSceneCharas();
            ChaControl primaryFemale = null;
            for (int i = 0; i < charas.Count; i++)
            {
                if (charas[i] != null && charas[i].name.StartsWith("chaF_") && primaryFemale == null)
                    primaryFemale = charas[i];
            }
            if (primaryFemale == null && charas.Count > 0) primaryFemale = charas[0];

            if (charas.Count == 0)
            {
                SimpleFileLogger.Warn("Teleport: no chara found");
                return;
            }

            SimpleFileLogger.Info($"TeleportCharas begin: count={charas.Count}");
            Vector3 realCenter = Vector3.zero;
            foreach (var c in charas)
            {
                LogCharaTransform("TeleportCharas before", c);
                realCenter += c.transform.position;
            }
            realCenter /= charas.Count;
            _realWorldCharaCenter = realCenter;

            if (primaryFemale != null)
            {
                _realWorldFemaleWaistCenter = primaryFemale.transform.position + Vector3.up * 0.9f;
                SimpleFileLogger.Info($"Real female waist center captured before teleport: chara={primaryFemale.name}, footPos={primaryFemale.transform.position}, waistCenter={_realWorldFemaleWaistCenter}, waistOffsetY=0.900");
            }
            else
            {
                _realWorldFemaleWaistCenter = realCenter + Vector3.up * 0.9f;
                SimpleFileLogger.Warn($"Primary female not found. Female waist fallback from realCenter: waistCenter={_realWorldFemaleWaistCenter}");
            }

            Vector3 cloneOffset = Vector3.down * 100f;
            _astralCharaCenter = realCenter;
            _skyOrigin = realCenter;
            _astralCharaOffset = Vector3.zero;
            SimpleFileLogger.Info($"TeleportCharas astral-in-place: realCenter={realCenter}, astralCenter={_astralCharaCenter}, cloneOffset={cloneOffset}, finalOffset={_astralCharaOffset}");

            foreach (var c in charas)
            {
                if (c == null) continue;
                var clone = CharaFreezeClone.CreateFrom(c, cloneOffset);
                if (clone != null) _frozenClones.Add(clone);
            }
            LogFrozenCloneState("after-create", detailed: true);

            foreach (var c in charas)
            {
                if (c == null) continue;
                _charaSnapshots.Add(new TransformSnapshot
                {
                    Target = c.transform,
                    WorldPos = c.transform.position,
                    WorldRot = c.transform.rotation
                });

                SimpleFileLogger.Info($"TeleportCharas keep-real: name={c.name}, pos={c.transform.position}, finalOffset={_astralCharaOffset}");
                LogCharaTransform("TeleportCharas keep-real", c);
            }

            SimpleFileLogger.Info($"Teleported {charas.Count} charas in-place. realCenter={realCenter}, realFemaleWaistCenter={_realWorldFemaleWaistCenter}, astralCenter={_astralCharaCenter}, clones={_frozenClones.Count}");
            MonitorAstralCharaState("after-teleport", force: true);
        }

        internal void MonitorAstralCharaState(string reason, bool force = false)
        {
            if (!SimpleFileLogger.Enabled) return;
            if (_state == AstralState.Normal || _state == AstralState.ReturnFadeOut) return;
            if (!force && Time.unscaledTime < _nextAstralCharaMonitorTime) return;
            _nextAstralCharaMonitorTime = Time.unscaledTime + 1.0f;

            if (_charaSnapshots.Count == 0)
            {
                if (force) SimpleFileLogger.Warn($"AstralCharaMonitor[{reason}]: no snapshots, state={_state}");
                return;
            }

            int alive = 0;
            float maxErr = 0f;
            string worst = "none";
            for (int i = 0; i < _charaSnapshots.Count; i++)
            {
                TransformSnapshot snap = _charaSnapshots[i];
                if (snap == null || snap.Target == null) continue;
                alive++;
                Vector3 expected = snap.WorldPos + _astralCharaOffset;
                float err = Vector3.Distance(snap.Target.position, expected);
                if (err > maxErr)
                {
                    maxErr = err;
                    worst = snap.Target.name;
                }
            }

            if (maxErr > 1.0f)
            {
                _astralCharaDriftHits++;
                bool detail = force || _astralCharaDriftHits == 1 || Time.unscaledTime >= _nextAstralCharaDriftDetailTime;
                if (detail) _nextAstralCharaDriftDetailTime = Time.unscaledTime + 10.0f;

                SimpleFileLogger.Warn($"ASTRAL_CHARA_DRIFT detected[{reason}]: hit={_astralCharaDriftHits}, alive={alive}/{_charaSnapshots.Count}, worst={worst}, maxErr={maxErr:F3}, state={_state}, detail={detail}");

                // In-place AstralMode: do not rewrite restore snapshots during astral.
                // KKS pose changes may move the real character root; that is not an astral offset drift.
                // Rewriting snap.WorldPos here corrupts the return-to-real restore target.
                // ReapplyAstralCharaOffsetFromCurrentPose(reason, detail);
            }
            else if (force)
            {
                SimpleFileLogger.Info($"AstralCharaMonitor[{reason}]: ok, alive={alive}/{_charaSnapshots.Count}, maxErr={maxErr:F3}, state={_state}");
            }
        }

        private void LogAstralCharaPositionDriftOnly(string reason)
        {
            if (!SimpleFileLogger.Enabled) return;
            if (_state == AstralState.Normal || _state == AstralState.ReturnFadeOut) return;
            if (_charaSnapshots.Count == 0) return;
            if (Time.unscaledTime < _nextAstralCharaPositionDiagTime) return;

            int alive = 0;
            float maxErr = 0f;
            TransformSnapshot worst = null;
            for (int i = 0; i < _charaSnapshots.Count; i++)
            {
                TransformSnapshot snap = _charaSnapshots[i];
                if (snap == null || snap.Target == null) continue;
                alive++;
                Vector3 expected = snap.WorldPos + _astralCharaOffset;
                float err = Vector3.Distance(snap.Target.position, expected);
                if (err > maxErr)
                {
                    maxErr = err;
                    worst = snap;
                }
            }

            if (maxErr <= 1.0f)
            {
                _nextAstralCharaPositionDiagTime = Time.unscaledTime + 0.25f;
                return;
            }

            _nextAstralCharaPositionDiagTime = Time.unscaledTime + 0.05f;
            Transform target = worst != null ? worst.Target : null;
            Vector3 actual = target != null ? target.position : Vector3.zero;
            Vector3 savedReal = worst != null ? worst.WorldPos : Vector3.zero;
            Vector3 expectedPos = worst != null ? worst.WorldPos + _astralCharaOffset : Vector3.zero;
            SimpleFileLogger.Warn(
                "ASTRAL_CHARA_POSITION_DIAG"
                + " reason=" + reason
                + " alive=" + alive + "/" + _charaSnapshots.Count
                + " worst=" + (target != null ? target.name : "null")
                + " actual=" + actual
                + " savedReal=" + savedReal
                + " expected=" + expectedPos
                + " offset=" + _astralCharaOffset
                + " err=" + maxErr.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                + " state=" + _state
                + " parent=" + (target != null && target.parent != null ? target.parent.name : "null")
                + " path=" + (target != null ? TransformPath(target) : "null"));
        }

        private void LogHSceneProcUpdateCharaState(string phase)
        {
            if (!SimpleFileLogger.Enabled) return;
            if (_state == AstralState.Normal || _state == AstralState.ReturnFadeOut) return;
            if (_charaSnapshots.Count == 0) return;

            bool forced = Time.unscaledTime <= _poseChangeReapplyUntil + 0.25f;
            if (!forced && Time.unscaledTime < _nextHSceneProcUpdateCharaDiagTime) return;
            _nextHSceneProcUpdateCharaDiagTime = Time.unscaledTime + 0.25f;

            int alive = 0;
            float maxErr = 0f;
            TransformSnapshot worst = null;
            for (int i = 0; i < _charaSnapshots.Count; i++)
            {
                TransformSnapshot snap = _charaSnapshots[i];
                if (snap == null || snap.Target == null) continue;
                alive++;
                Vector3 expected = snap.WorldPos + _astralCharaOffset;
                float err = Vector3.Distance(snap.Target.position, expected);
                if (err > maxErr)
                {
                    maxErr = err;
                    worst = snap;
                }
            }

            Transform target = worst != null ? worst.Target : null;
            Vector3 actual = target != null ? target.position : Vector3.zero;
            Vector3 savedReal = worst != null ? worst.WorldPos : Vector3.zero;
            Vector3 expectedPos = worst != null ? worst.WorldPos + _astralCharaOffset : Vector3.zero;
            SimpleFileLogger.Warn(
                "HSCENE_UPDATE_CHARA_DIAG"
                + " phase=" + phase
                + " alive=" + alive + "/" + _charaSnapshots.Count
                + " worst=" + (target != null ? target.name : "null")
                + " actual=" + actual
                + " savedReal=" + savedReal
                + " expected=" + expectedPos
                + " offset=" + _astralCharaOffset
                + " err=" + maxErr.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                + " state=" + _state
                + " forced=" + forced
                + " parent=" + (target != null && target.parent != null ? target.parent.name : "null")
                + " path=" + (target != null ? TransformPath(target) : "null"));
        }

        internal void ReapplyAstralCharaOffsetFromCurrentPose(string reason, bool detail)
        {
            if (_state == AstralState.Normal || _state == AstralState.ReturnFadeOut) return;
            if (_charaSnapshots.Count == 0) return;

            int fixedCount = 0;
            for (int i = 0; i < _charaSnapshots.Count; i++)
            {
                TransformSnapshot snap = _charaSnapshots[i];
                if (snap == null || snap.Target == null) continue;

                Vector3 oldSavedReal = snap.WorldPos;
                Vector3 actual = snap.Target.position;
                Vector3 expected = oldSavedReal + _astralCharaOffset;
                float err = Vector3.Distance(actual, expected);
                if (err <= 0.05f) continue;

                Vector3 newSavedReal = actual;
                Vector3 newAstral = newSavedReal + _astralCharaOffset;
                snap.WorldPos = newSavedReal;
                snap.WorldRot = snap.Target.rotation;
                snap.Target.position = newAstral;
                fixedCount++;

                if (detail)
                {
                    SimpleFileLogger.Warn($"AstralCharaReapply[{reason}]: target={snap.Target.name}, oldSavedReal={oldSavedReal}, resetPos={actual}, oldExpected={expected}, newExpected={newAstral}, err={err:F3}, parent={(snap.Target.parent != null ? snap.Target.parent.name : "null")}, path={TransformPath(snap.Target)}, state={_state}");
                }
            }

            if (fixedCount <= 0) return;

            _astralCharaReapplyHits++;
            SimpleFileLogger.Warn($"ASTRAL_CHARA_REAPPLY done[{reason}]: hit={_astralCharaReapplyHits}, fixed={fixedCount}/{_charaSnapshots.Count}, offset={_astralCharaOffset}, state={_state}");

            if (detail)
                LogFrozenCloneState("chara-reapply", detailed: false);
        }

        private void LogFrozenCloneState(string reason, bool detailed = false)
        {
            if (!SimpleFileLogger.Enabled) return;
            int managedAlive = 0;
            for (int i = 0; i < _frozenClones.Count; i++)
            {
                var c = _frozenClones[i];
                if (c == null || c.Root == null) continue;
                managedAlive++;
                if (!detailed) continue;
                MeshRenderer[] renderers = c.Root.GetComponentsInChildren<MeshRenderer>(true);
                MeshFilter[] filters = c.Root.GetComponentsInChildren<MeshFilter>(true);
                SimpleFileLogger.Info($"FrozenCloneState[{reason}] managed[{i}]: root={c.Root.name}, active={c.Root.activeInHierarchy}, pos={c.Root.transform.position}, rot={c.Root.transform.rotation.eulerAngles}, renderers={renderers.Length}, filters={filters.Length}, savedWorld={c.WorldPos}");
            }

            // [perf] periodic(非detailed)では全GameObject走査を行わない(アストラル中のカクつき対策)
            if (!detailed && reason == "periodic") return;

            int rootCount = 0;
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null) continue;
                if (go.transform.parent != null) continue;
                if (!go.name.StartsWith("AstralMode_FrozenClone_")) continue;
                rootCount++;
            }

            if (detailed || rootCount != _lastLoggedCloneRootCount || reason != "periodic")
            {
                _lastLoggedCloneRootCount = rootCount;
                SimpleFileLogger.Info($"FrozenCloneState[{reason}]: managedAlive={managedAlive}/{_frozenClones.Count}, rootCountInResources={rootCount}, state={_state}");
            }
        }

        internal void RestoreCharasToReal()
        {
            MonitorAstralCharaState("before-restore", force: true);
            LogFrozenCloneState("before-restore", detailed: true);

            foreach (var snap in _charaSnapshots)
            {
                if (snap.Target == null) continue;
                SimpleFileLogger.Info($"RestoreChara before: target={snap.Target.name}, pos={snap.Target.position}, restorePos={snap.WorldPos}, parent={(snap.Target.parent != null ? snap.Target.parent.name : "null")}, path={TransformPath(snap.Target)}");
                snap.Target.position = snap.WorldPos;
                snap.Target.rotation = snap.WorldRot;
                SimpleFileLogger.Info($"RestoreChara after: target={snap.Target.name}, pos={snap.Target.position}, rot={snap.Target.rotation.eulerAngles}");
            }
            SimpleFileLogger.Info($"Restored {_charaSnapshots.Count} charas to real world");
            _charaSnapshots.Clear();

            CharaFreezeClone.DestroyAll(_frozenClones);
            SimpleFileLogger.Info("FrozenClones destroyed");
            LogFrozenCloneState("after-destroy", detailed: false);
        }
    }
}
