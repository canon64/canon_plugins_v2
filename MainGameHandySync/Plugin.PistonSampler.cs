using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace MainGameHandySync
{
    /// <summary>
    /// Computes normalized 0-1 "piston/insertion" phase from KKS HScene animation.
    /// Primary: male penis/hip bone relative to female hips (robust across positions).
    /// Fallback: HFlag internal speed state.
    /// </summary>
    internal sealed class PistonSampler
    {
        private static readonly string[] MaleDanBoneCandidates = { "cm_J_dan101_00", "cm_J_dan100_00", "cm_J_dan109_00" };
        private static readonly string[] FemaleHipsCandidates = { "cf_j_hips", "cf_J_Hips", "cf_j_waist01", "cf_J_Waist01" };

        private Transform _maleHipsOrDan;
        private Transform _femaleHips;
        private bool _bonesInitialized;
        private bool _boneMethodWorking;

        private float _lastPhase;
        private int _sampleCount;

        internal float GetCurrentPhase(HSceneProc hSceneProc, HFlag hFlag)
        {
            if (hSceneProc == null || hFlag == null) return _lastPhase;

            // Try bone method first (preferred)
            if (TryInitBones(hSceneProc, hFlag))
            {
                float phase = ComputeBonePhase();
                if (!float.IsNaN(phase))
                {
                    _lastPhase = Mathf.Clamp01(phase);
                    _boneMethodWorking = true;
                    LogPhaseIfNeeded("bone");
                    return _lastPhase;
                }
            }

            // Fallback to HFlag / animator speed state (less accurate but always available in sonyu)
            float fallback = ComputeHFlagFallback(hFlag);
            _lastPhase = fallback;
            LogPhaseIfNeeded("fallback");
            return _lastPhase;
        }

        private bool TryInitBones(HSceneProc proc, HFlag hFlag)
        {
            if (_bonesInitialized) return _maleHipsOrDan != null && _femaleHips != null;

            _bonesInitialized = true;

            try
            {
                // Male via reflection (documented in male_hip_ik_research_2026-03-16.md)
                var maleField = AccessTools.Field(typeof(HSceneProc), "male");
                var maleCha = maleField?.GetValue(proc) as ChaControl;
                if (maleCha != null && maleCha.objBodyBone != null)
                {
                    var maleRoot = maleCha.objBodyBone.transform;
                    _maleHipsOrDan = FindFirstBone(maleRoot, MaleDanBoneCandidates);
                    if (_maleHipsOrDan == null)
                    {
                        _maleHipsOrDan = maleRoot.Find("cm_J_Hips") ?? maleRoot.Find("cm_j_root");
                    }
                }

                // Female - safe access via HFlag (most reliable in practice)
                ChaControl femaleCha = null;
                if (hFlag != null)
                {
                    var heroines = hFlag.lstHeroine;
                    if (heroines != null && heroines.Count > 0)
                        femaleCha = heroines[0]?.chaCtrl;
                }
                if (femaleCha == null && proc != null)
                {
                    // fallback reflection
                    var fField = AccessTools.Field(typeof(HSceneProc), "lstFemale");
                    var list = fField?.GetValue(proc) as System.Collections.IList;
                    if (list != null && list.Count > 0)
                        femaleCha = (list[0] as ChaControl) ?? (list[0]?.GetType().GetProperty("chaCtrl")?.GetValue(list[0]) as ChaControl);
                }

                if (femaleCha != null && femaleCha.objBodyBone != null)
                {
                    var femaleRoot = femaleCha.objBodyBone.transform;
                    _femaleHips = FindFirstBone(femaleRoot, FemaleHipsCandidates);
                    if (_femaleHips == null)
                        _femaleHips = femaleRoot.Find("cf_j_root/cf_n_height/cf_j_hips") ?? femaleRoot.Find("cf_j_hips");
                }

                if (_maleHipsOrDan != null && _femaleHips != null)
                {
                    SimpleFileLogger.Info($"PistonSampler: Bones found. Male={_maleHipsOrDan.name}, Female={_femaleHips.name}");
                    return true;
                }
                else
                {
                    SimpleFileLogger.Warn($"PistonSampler: Bone init partial. maleDan={_maleHipsOrDan?.name ?? "MISSING"}, femaleHips={_femaleHips?.name ?? "MISSING"}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"PistonSampler bone init exception: {ex.Message}");
                return false;
            }
        }

        private Transform FindFirstBone(Transform root, string[] candidates)
        {
            if (root == null) return null;
            foreach (var name in candidates)
            {
                var t = root.Find(name);
                if (t != null) return t;

                // deep search (cheap, HScene only)
                var all = root.GetComponentsInChildren<Transform>(true);
                foreach (var tr in all)
                    if (tr.name.Equals(name, StringComparison.OrdinalIgnoreCase)) return tr;
            }
            return null;
        }

        private float ComputeBonePhase()
        {
            if (_maleHipsOrDan == null || _femaleHips == null) return float.NaN;

            // Simple depth proxy: signed distance along world up or male forward (tune later)
            // Better: project male→female vector onto a reasonable thrust axis.
            Vector3 diff = _maleHipsOrDan.position - _femaleHips.position;

            // Heuristic: use the dominant vertical (Y) component for most common positions, or magnitude
            float raw = Mathf.Clamp(diff.y, -0.6f, 0.6f); // typical insertion range in KKS units
            float normalized = Mathf.InverseLerp(-0.35f, 0.35f, raw); // rough 0-1 mapping (will be tuned by user stroke min/max)

            return normalized;
        }

        private float ComputeHFlagFallback(HFlag flag)
        {
            // Very rough: use current speed as proxy for "active motion intensity"
            // Real piston phase needs loop progress. This is a placeholder until better anim sampling.
            float speed = flag.speed;
            // Map typical 0-1.5 speed range into 0-1 oscillating placeholder
            float t = (Time.time * 1.5f) % 2f;
            float fakePhase = t > 1f ? (2f - t) : t;
            return Mathf.Lerp(0.2f, 0.8f, fakePhase * Mathf.Clamp01(speed / 1.2f));
        }

        private void LogPhaseIfNeeded(string source)
        {
            _sampleCount++;
            if (_sampleCount % 30 == 0) // ~every 1-2s at 20Hz
            {
                SimpleFileLogger.Debug($"Piston phase [{source}]: {_lastPhase:F3} (samples={_sampleCount})");
            }
        }

        internal void Reset()
        {
            _bonesInitialized = false;
            _maleHipsOrDan = null;
            _femaleHips = null;
            _boneMethodWorking = false;
            _sampleCount = 0;
            _lastPhase = 0.5f;
        }
    }
}
