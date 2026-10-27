using System;
using System.Runtime.Serialization;

namespace MainGameDollMode
{
    [DataContract]
    internal sealed class PluginSettings
    {
        [DataMember] public bool Enabled = true;
        [DataMember] public bool DollModeEnabled = false;
        [DataMember] public bool ApplyFemaleCharacters = true;
        [DataMember] public bool ApplyMaleCharacters = false;
        [DataMember] public bool InfoLogEnabled = false;
        [DataMember] public bool VerboseLog = false;
        [DataMember] public bool BlockEventLogEnabled = false;
        [DataMember] public bool CauseProbeLogEnabled = false;
        [DataMember] public bool EyeMovementTraceLog = false;
        [DataMember] public bool DisableAhegaoPlugins = true;
        [DataMember] public string AhegaoPluginKeywordCsv = "ahegao,maingameahegao,ksplug,kplug";
        [DataMember] public int DollModeEyePattern = 0;
        [DataMember] public int DollModeEyebrowPattern = 0;
        [DataMember] public int DollModeMouthPattern = 0;
        [DataMember] public float DollModeCheekRate = 0f;
        [DataMember] public int DollModeTearsLevel = 0;
        [DataMember] public int DollModeFaceSweatLevel = 0;
        [DataMember] public bool AllowFaceSiruDuringDollMode = true;
        [DataMember] public float DollModeMouthOpen = 0.30f;
        [DataMember] public float DollModeEyesOpen = 0.80f;
        [DataMember] public bool DollModeMotionLockWEnabled = false;
        [DataMember] public float DollModeMotionLockWValue = 0.30f;
        [DataMember] public bool DollModeMotionLockSEnabled = false;
        [DataMember] public float DollModeMotionLockSValue = 0.80f;
        [DataMember] public bool DollModeEyeOverlayEnabled = false;
        [DataMember] public string DollModeEyeOverlayPngPath = "";
        [DataMember] public float DollModeTransitionSeconds = 1.0f;

        public void Normalize()
        {
            if (!ApplyFemaleCharacters && !ApplyMaleCharacters)
            {
                ApplyFemaleCharacters = true;
            }

            AhegaoPluginKeywordCsv = NormalizeKeywordCsv(
                AhegaoPluginKeywordCsv,
                "ahegao,maingameahegao,ksplug,kplug");

            if (DollModeEyePattern < 0)
            {
                DollModeEyePattern = 0;
            }

            if (DollModeEyebrowPattern < 0)
            {
                DollModeEyebrowPattern = 0;
            }

            if (DollModeMouthPattern < 0)
            {
                DollModeMouthPattern = 0;
            }

            DollModeCheekRate = NormalizeRate(DollModeCheekRate, 0f);
            DollModeTearsLevel = Math.Max(0, Math.Min(10, DollModeTearsLevel));
            DollModeFaceSweatLevel = Math.Max(0, Math.Min(3, DollModeFaceSweatLevel));
            DollModeMouthOpen = NormalizeRate(DollModeMouthOpen, 0.30f);
            DollModeEyesOpen = NormalizeRate(DollModeEyesOpen, 0.80f);
            DollModeMotionLockWValue = NormalizeRate(DollModeMotionLockWValue, 0.30f);
            DollModeMotionLockSValue = NormalizeRate(DollModeMotionLockSValue, 0.80f);
            DollModeEyeOverlayPngPath = NormalizeOptionalPath(DollModeEyeOverlayPngPath);
            DollModeTransitionSeconds = NormalizeSeconds(DollModeTransitionSeconds, 1.0f);
        }

        private static float NormalizeRate(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                value = fallback;
            }

            value = Math.Max(0f, Math.Min(1f, value));
            return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private static string NormalizeKeywordCsv(string value, string fallback)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return fallback;
            }

            return normalized;
        }

        private static string NormalizeOptionalPath(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static float NormalizeSeconds(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                value = fallback;
            }

            value = Math.Max(0f, Math.Min(10f, value));
            return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

    }
}
