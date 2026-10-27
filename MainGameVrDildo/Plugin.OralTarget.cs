using UnityEngine;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        private enum TargetKind { None, Vaginal, Oral }
        private ChaControl _mouthOpeningFemale;
        private float _originalMouthFixedRate;
        private float _lastMouthFixedRate;
        private bool _ownsMouthOpening;
        private bool _mouthOpeningRequested;
        private int _originalMouthPattern;
        private bool _ownsOralMouthPattern;
        private int _lastAppliedMouthPattern;

        private void ClearMouthTargets()
        {
            _mouthEntry = null;
            _mouthDeep = null;
            if (_mouthFallback != null) Destroy(_mouthFallback);
            _mouthFallback = null;
        }

        private void ResolveMouthTargets()
        {
            if (_female == null || !_settings.OralTargetEnabled) return;
            if (_mouthEntry == null) _mouthEntry = FindBone(_female, "cf_J_MouthLow");
            if (_mouthDeep == null) _mouthDeep = FindBone(_female, "cf_J_FaceBase");
            if (_mouthEntry != null && _mouthDeep != null) return;

            Transform head = FindBone(_female, "cf_j_head") ?? FindBone(_female, "cf_J_Head") ??
                FindBone(_female, "cf_J_FaceBase");
            if (head == null) return;
            if (_mouthFallback == null)
            {
                _mouthFallback = new GameObject("VR Vibe mouth target fallback");
                _mouthFallback.transform.SetParent(head, false);
            }
            if (_mouthEntry == null)
            {
                Transform hit = null;
                foreach (var transform in _female.GetComponentsInChildren<Transform>(true))
                    if (transform.tag == "H/Aibu/Hit/mouth") { hit = transform; break; }
                var anchor = new GameObject("Mouth entry").transform;
                anchor.SetParent(_mouthFallback.transform, false);
                anchor.localPosition = hit != null ? _mouthFallback.transform.InverseTransformPoint(hit.position)
                    : new Vector3(0f, -0.04f, 0.08f);
                _mouthEntry = anchor;
            }
            if (_mouthDeep == null)
            {
                var anchor = new GameObject("Mouth inner").transform;
                anchor.SetParent(_mouthFallback.transform, false);
                anchor.localPosition = new Vector3(0f, -0.04f, 0.01f);
                _mouthDeep = anchor;
            }
        }

        private bool TrySelectTarget(Vector3 tip, out TargetKind kind, out Vector3 entry, out Vector3 deep)
        {
            kind = TargetKind.None;
            entry = deep = Vector3.zero;
            bool vagina = _entry != null && _deep != null;
            bool mouth = _settings.OralTargetEnabled && _mouthEntry != null && _mouthDeep != null;
            if (!vagina && !mouth) return false;
            Vector3 vaginalEntry = vagina ? _entry.TransformPoint(new Vector3(
                _settings.EntryOffsetX, _settings.EntryOffsetY, _settings.EntryOffsetZ)) : Vector3.zero;
            Vector3 oralEntry = mouth ? _mouthEntry.TransformPoint(new Vector3(
                _settings.MouthEntryOffsetX, _settings.MouthEntryOffsetY, _settings.MouthEntryOffsetZ)) : Vector3.zero;
            bool chooseMouth = mouth && (!vagina || Vector3.Distance(tip, oralEntry) <
                Vector3.Distance(tip, vaginalEntry) || (_selectedTarget == TargetKind.Oral &&
                Mathf.Approximately(Vector3.Distance(tip, oralEntry), Vector3.Distance(tip, vaginalEntry))));
            if (chooseMouth)
            {
                kind = TargetKind.Oral;
                entry = oralEntry;
                deep = _mouthDeep.TransformPoint(new Vector3(_settings.MouthDeepOffsetX,
                    _settings.MouthDeepOffsetY, _settings.MouthDeepOffsetZ));
            }
            else
            {
                kind = TargetKind.Vaginal;
                entry = vaginalEntry;
                deep = _deep.TransformPoint(new Vector3(_settings.DeepOffsetX,
                    _settings.DeepOffsetY, _settings.DeepOffsetZ));
            }
            return (deep - entry).sqrMagnitude >= 0.000001f;
        }

        private void UpdateMouthOpening(bool selected, float tipDistance)
        {
            bool want = selected && (tipDistance <= _settings.MouthOpenStartDistance ||
                (_mouthOpeningRequested && tipDistance < _settings.MouthOpenReleaseDistance));
            _mouthOpeningRequested = want;
            if (!want && !_ownsMouthOpening) return;
            if (_female == null || _female.mouthCtrl == null) { ReleaseMouthOpening(); return; }
            if (!_ownsMouthOpening && want)
            {
                _mouthOpeningFemale = _female;
                _originalMouthFixedRate = _female.mouthCtrl.FixedRate;
                _originalMouthPattern = _female.fileStatus.mouthPtn;
                _lastMouthFixedRate = Mathf.Max(0f, _originalMouthFixedRate);
                _ownsMouthOpening = true;
            }
            if (_mouthOpeningFemale != _female) { ReleaseMouthOpening(); return; }
            // Do not overwrite another plugin's new mouth command.
            if (_ownsMouthOpening && !Mathf.Approximately(_female.mouthCtrl.FixedRate, _lastMouthFixedRate) &&
                !Mathf.Approximately(_female.mouthCtrl.FixedRate, _originalMouthFixedRate))
            {
                RestoreOralMouthPattern();
                _ownsMouthOpening = false;
                return;
            }
            if (want)
            {
                int pattern = _oralContact ? _settings.MouthContactPattern : _settings.MouthApproachPattern;
                if (_female.fileStatus.mouthPtn != pattern) _female.ChangeMouthPtn(pattern, true);
                _ownsOralMouthPattern = true;
                _lastAppliedMouthPattern = pattern;
            }
            else RestoreOralMouthPattern();
            float baseRate = Mathf.Clamp01(_originalMouthFixedRate);
            float target = baseRate;
            if (want)
            {
                float range = _settings.MouthOpenStartDistance - _settings.MouthOpenFullDistance;
                float progress = range > 0.0001f
                    ? Mathf.Clamp01((_settings.MouthOpenStartDistance - tipDistance) / range)
                    : (tipDistance <= _settings.MouthOpenStartDistance ? 1f : 0f);
                float approach = Mathf.Lerp(baseRate, _settings.MouthOpenAmount, progress);
                target = Mathf.Max(baseRate,
                    _oralContact ? _settings.MouthContactOpenAmount : approach);
            }
            float rate = 1f / Mathf.Max(0.02f, _settings.MouthOpenSeconds);
            _lastMouthFixedRate = Mathf.MoveTowards(_lastMouthFixedRate, target, rate * Time.deltaTime);
            _female.mouthCtrl.FixedRate = _lastMouthFixedRate;
            if (!want && Mathf.Approximately(_lastMouthFixedRate, target)) ReleaseMouthOpening();
        }

        private void ReleaseMouthOpening()
        {
            RestoreOralMouthPattern();
            if (_ownsMouthOpening && _mouthOpeningFemale != null && _mouthOpeningFemale.mouthCtrl != null &&
                Mathf.Approximately(_mouthOpeningFemale.mouthCtrl.FixedRate, _lastMouthFixedRate))
                _mouthOpeningFemale.mouthCtrl.FixedRate = _originalMouthFixedRate;
            _ownsMouthOpening = false;
            _mouthOpeningRequested = false;
            _mouthOpeningFemale = null;
        }

        private void RestoreOralMouthPattern()
        {
            if (_ownsOralMouthPattern && _mouthOpeningFemale != null &&
                _mouthOpeningFemale.fileStatus.mouthPtn == _lastAppliedMouthPattern)
                _mouthOpeningFemale.ChangeMouthPtn(_originalMouthPattern, true);
            _ownsOralMouthPattern = false;
        }
    }
}
