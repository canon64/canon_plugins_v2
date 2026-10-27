using System;
using UnityEngine;

namespace MainGameVrDildo
{
    [Serializable]
    public sealed class SegmentSettings
    {
        public string Bone;
        public bool Enabled = true;
        public float Radius = 0.02f;
        public float LengthScale = 1f;
        // Zero keeps the legacy bone-following length. Positive values are world metres.
        public float LengthMetres;
        public float OffsetX;
        public float OffsetY;
        public float OffsetZ;
        public float RotationX;
        public float RotationY;
        public float RotationZ;
        public float FkX;
        public float FkZ;
    }

    [Serializable]
    public sealed class DildoSettings
    {
        public int Version = 14;
        public bool Enabled = true;
        public bool DiagnosticLogEnabled;
        public bool AutoSpawnOnH;
        public bool AutoTarget = true;
        public bool OralTargetEnabled = true;
        public float MouthEntryOffsetX;
        public float MouthEntryOffsetY;
        public float MouthEntryOffsetZ;
        public float MouthDeepOffsetX;
        public float MouthDeepOffsetY;
        public float MouthDeepOffsetZ;
        public float MouthOpenStartDistance = 0.12f;
        public float MouthOpenFullDistance = 0.02f;
        public float MouthOpenReleaseDistance = 0.16f;
        public float MouthOpenAmount = 0.6f;
        public float MouthContactOpenAmount = 1f;
        public int MouthApproachPattern = 21;
        public int MouthContactPattern = 22;
        public float MouthOpenSeconds = 0.2f;
        public float OralContactRadius = 0.04f;
        public bool OralAudioEnabled = true;
        public string OralSelectionPath = "";
        public float OralAudioVolume = 1f;
        public float OralVoiceIntervalSeconds = 8f;
        public bool EnableVrGrab = true;
        public bool ShowColliders;
        public float ModelScale = 1f;
        public float SpawnDistance = 0.35f;
        public float GripRadius = 0.08f;
        public float GripOffsetX;
        public float GripOffsetY = 0.01f;
        public float GripOffsetZ;
        public float GripRotationX;
        public float GripRotationY;
        public float GripRotationZ;
        public float EngageDistance = 0.12f;
        public float ReleaseDistance = 0.18f;
        public float EntryOffsetX;
        public float EntryOffsetY;
        public float EntryOffsetZ;
        public float DeepOffsetX;
        public float DeepOffsetY;
        public float DeepOffsetZ;
        public float BendStrength = 0.65f;
        public float BendSpeed = 12f;
        public float MaxAngle = 25f;
        public int SolverIterations = 4;
        public bool AibuGaugeEnabled = true;
        public bool AibuRequireGrip = true;
        public bool ShowDesktopGizmo;
        public float AibuContactRadius = 0.06f;
        public float AibuMinMotionSpeed = 0.02f;
        public float AibuPointsPerMetre = 8f;
        public float AibuMaxPointsPerSecond = 2f;
        public float AibuWindowSeconds = 1f;
        public bool AibuStrongVoiceEnabled = true;
        public float AibuStrongVoiceOnCm = 12f;
        public float AibuStrongVoiceOffCm = 6f;
        public bool BodyReactionEnabled;
        public float SlowMaxSpeed = 0.12f;
        public float SlowConfirmSeconds = 0.25f;
        public float FastMinSpeed = 0.18f;
        public float DeepStrokeCm = 10f;
        public float DeepPathCapFraction = 0.65f;
        public float DeepMinSpeed = 0.16f;
        public float DeepEndFraction = 0.7f;
        public bool DeepVoiceEnabled = true;
        public float BodyFollowMetres = 0.012f;
        public float FastReactionMetres = 0.012f;
        public float FastPullReactionRatio = 0.45f;
        public float FastReactionReturnPerSecond = 6f;
        public float SlowBodyFollowRatio = 0.65f;
        public float DeepReactionMetres = 0.03f;
        public float DeepReturnSeconds = 0.45f;
        public float DeepHoldSeconds = 0.45f;
        public float BodyReturnSpeed = 0.1f;
        public float BodyFollowSpeed = 0.25f;
        public float BodyOffsetLimitMetres = 0.08f;
        public float UpperBodyRatio = 0.45f;
        public float UpperBodyFollowSpeed = 0.12f;
        public float UpperBodyReturnSpeed = 0.08f;
        public float BodyMaxAngleDeg = 20f;
        public float MotionGaugeFullCm = 30f;
        public float MotionGaugeDecayCmPerSecond = 8f;
        public float MotionGaugeBoost = 0.5f;
        public bool MotionGaugeEnabled = true;
        public float FaceStartGaugeFraction = 0.5f;
        public int HeartEyesPattern = 25;
        public float MouthChangeSeconds = 10f;
        public bool MouthPattern24Enabled = true;
        public bool MouthPattern43Enabled = true;
        public bool MouthPattern44Enabled = true;
        public bool MouthPattern45Enabled = true;
        public bool SweatEnabled = true;
        public float SweatStage1GaugeFraction = 0.25f;
        public float SweatStage2GaugeFraction = 0.5f;
        public float SweatStage3GaugeFraction = 0.75f;
        public int SweatStage1Level = 1;
        public int SweatStage2Level = 2;
        public int SweatStage3Level = 3;
        public bool OralFluidEnabled = true;
        public int OralTearLevel = 2;
        public int OralDroolLevel = 2;
        public float OralTearStepSeconds = 5f;
        public float OralDroolStepSeconds = 5f;
        public float MotionGaugeAnimationStrengthWeight = 1f;
        public float CloneAnimationMinMultiplier = 1f;
        public float CloneAnimationMaxMultiplier = 2.5f;
        public float CloneAnimationGaugeWeight = 1f;
        public float PistonMinStrokeCm = 2f;
        public float PistonMaxStrokeSeconds = 0.7f;
        public float PistonReversalWindowSeconds = 1f;
        public int PistonMinReversals = 2;
        public float PistonAcceptedRetrySeconds = 3f;
        public float PistonRejectedRetrySeconds = 0.75f;
        public float PistonVoiceVolume = 1f;
        public float PistonVoiceCooldownSeconds = 0f;
        public float HardPistonWindowSeconds = 1f;
        public float HardPistonDistanceCm = 25f;
        public float HardPistonMinSpeed = 0.35f;
        public int HardPistonMinReversals = 4;
        public float HardPistonReleaseSeconds = 0.5f;
        public float HardPistonVoiceVolume = 1f;
        public float HardPistonVoiceCooldownSeconds = 0f;
        public float HardPistonAcceptedRetrySeconds = 3f;
        public float HardPistonRejectedRetrySeconds = 0.75f;
        public bool OgazmEyesEnabled = true;
        public float OgazmEyeStartGaugeFraction = 0.5f;
        public float OgazmEyeFullGaugeFraction = 1f;
        public float OgazmEnterGaugeFraction = 0.95f;
        public float OgazmExitGaugeFraction = 0.90f;
        public float OgazmEyeInwardDegrees = 12f;
        public float OgazmEyeVerticalDegrees = 30f;
        public float OgazmIrisInwardUv = 0.08f;
        public float OgazmIrisUpUv = 0.12f;
        public bool OgazmInvertEyeDirection;
        public bool OgazmVoiceEnabled = true;
        public float OgazmVoiceVolume = 1f;
        public float OgazmVoiceCooldownSeconds = 5f;
        public SegmentSettings[] Segments;

        public static DildoSettings CreateDefault()
        {
            var settings = new DildoSettings { Segments = new SegmentSettings[8] };
            for (int i = 0; i < settings.Segments.Length; i++)
                settings.Segments[i] = new SegmentSettings { Bone = "VRVibe_" + i.ToString("00"), Radius = i == 7 ? 0.017f : 0.019f };
            return settings;
        }

        public void Normalize()
        {
            SpawnDistance = Mathf.Clamp(SpawnDistance, 0.1f, 3f);
            ModelScale = Mathf.Clamp(ModelScale, 0.25f, 3f);
            GripRadius = Mathf.Clamp(GripRadius, 0.02f, 0.5f);
            EngageDistance = Mathf.Clamp(EngageDistance, 0.02f, 0.5f);
            ReleaseDistance = Mathf.Clamp(ReleaseDistance, EngageDistance, 0.8f);
            BendStrength = Mathf.Clamp01(BendStrength);
            BendSpeed = Mathf.Clamp(BendSpeed, 0.1f, 60f);
            MaxAngle = Mathf.Clamp(MaxAngle, 0f, 80f);
            SolverIterations = Mathf.Clamp(SolverIterations, 1, 12);
            AibuContactRadius = Mathf.Clamp(AibuContactRadius, 0.01f, 0.2f);
            AibuMinMotionSpeed = Mathf.Clamp(AibuMinMotionSpeed, 0f, 1f);
            AibuPointsPerMetre = Mathf.Clamp(AibuPointsPerMetre, 0f, 300f);
            AibuMaxPointsPerSecond = Mathf.Clamp(AibuMaxPointsPerSecond, 0f, 100f);
            AibuWindowSeconds = Mathf.Clamp(AibuWindowSeconds, 0.2f, 5f);
            AibuStrongVoiceOnCm = Mathf.Clamp(AibuStrongVoiceOnCm, 1f, 100f);
            AibuStrongVoiceOffCm = Mathf.Clamp(AibuStrongVoiceOffCm, 0f, AibuStrongVoiceOnCm);
            SlowMaxSpeed = Mathf.Clamp(SlowMaxSpeed, 0.02f, 1f);
            SlowConfirmSeconds = Mathf.Clamp(SlowConfirmSeconds, 0.05f, 2f);
            FastMinSpeed = Mathf.Clamp(FastMinSpeed, SlowMaxSpeed, 2f);
            DeepStrokeCm = Mathf.Clamp(DeepStrokeCm, 2f, 100f);
            DeepPathCapFraction = Mathf.Clamp(DeepPathCapFraction, 0.1f, 1f);
            DeepMinSpeed = Mathf.Clamp(DeepMinSpeed, SlowMaxSpeed, 2f);
            DeepEndFraction = Mathf.Clamp01(DeepEndFraction);
            BodyFollowMetres = Mathf.Clamp(BodyFollowMetres, 0f, 0.08f);
            FastReactionMetres = Mathf.Clamp(FastReactionMetres, 0f, 0.08f);
            FastPullReactionRatio = Mathf.Clamp(FastPullReactionRatio, 0f, 2f);
            FastReactionReturnPerSecond = Mathf.Clamp(FastReactionReturnPerSecond, 0.1f, 30f);
            SlowBodyFollowRatio = Mathf.Clamp(SlowBodyFollowRatio, 0f, 2f);
            DeepReactionMetres = Mathf.Clamp(DeepReactionMetres, 0f, 0.1f);
            DeepReturnSeconds = Mathf.Clamp(DeepReturnSeconds, 0.05f, 5f);
            DeepHoldSeconds = Mathf.Clamp(DeepHoldSeconds, 0f, 5f);
            BodyReturnSpeed = Mathf.Clamp(BodyReturnSpeed, 0.005f, 2f);
            BodyFollowSpeed = Mathf.Clamp(BodyFollowSpeed, 0.005f, 2f);
            BodyOffsetLimitMetres = Mathf.Clamp(BodyOffsetLimitMetres, 0.01f, 0.3f);
            UpperBodyRatio = Mathf.Clamp01(UpperBodyRatio);
            UpperBodyFollowSpeed = Mathf.Clamp(UpperBodyFollowSpeed, 0.01f, 1f);
            UpperBodyReturnSpeed = Mathf.Clamp(UpperBodyReturnSpeed, 0.01f, 1f);
            BodyMaxAngleDeg = Mathf.Clamp(BodyMaxAngleDeg, 0f, 45f);
            MotionGaugeFullCm = Mathf.Clamp(MotionGaugeFullCm, 1f, 3000f);
            FaceStartGaugeFraction = Mathf.Clamp01(FaceStartGaugeFraction);
            HeartEyesPattern = Mathf.Clamp(HeartEyesPattern, 0, 38);
            MouthChangeSeconds = Mathf.Clamp(MouthChangeSeconds, 1f, 120f);
            SweatStage1GaugeFraction = Mathf.Clamp01(SweatStage1GaugeFraction);
            SweatStage2GaugeFraction = Mathf.Clamp(SweatStage2GaugeFraction, SweatStage1GaugeFraction, 1f);
            SweatStage3GaugeFraction = Mathf.Clamp(SweatStage3GaugeFraction, SweatStage2GaugeFraction, 1f);
            SweatStage1Level = Mathf.Clamp(SweatStage1Level, 0, 10);
            SweatStage2Level = Mathf.Clamp(SweatStage2Level, 0, 10);
            SweatStage3Level = Mathf.Clamp(SweatStage3Level, 0, 10);
            OralTearLevel = Mathf.Clamp(OralTearLevel, 0, 10);
            OralDroolLevel = Mathf.Clamp(OralDroolLevel, 0, 10);
            OralTearStepSeconds = Mathf.Clamp(OralTearStepSeconds, 0.5f, 120f);
            OralDroolStepSeconds = Mathf.Clamp(OralDroolStepSeconds, 0.5f, 120f);
            MotionGaugeDecayCmPerSecond = Mathf.Clamp(MotionGaugeDecayCmPerSecond, 0f, 100f);
            MotionGaugeBoost = Mathf.Clamp(MotionGaugeBoost, 0f, 2f);
            MotionGaugeAnimationStrengthWeight = Mathf.Clamp01(MotionGaugeAnimationStrengthWeight);
            CloneAnimationMinMultiplier = Mathf.Clamp(CloneAnimationMinMultiplier, 0.1f, 4f);
            CloneAnimationMaxMultiplier = Mathf.Clamp(CloneAnimationMaxMultiplier, CloneAnimationMinMultiplier, 4f);
            CloneAnimationGaugeWeight = Mathf.Clamp01(CloneAnimationGaugeWeight);
            PistonMinStrokeCm = Mathf.Clamp(PistonMinStrokeCm, 0.1f, 30f);
            PistonMaxStrokeSeconds = Mathf.Clamp(PistonMaxStrokeSeconds, 0.05f, 3f);
            PistonReversalWindowSeconds = Mathf.Clamp(PistonReversalWindowSeconds, 0.1f, 5f);
            PistonMinReversals = Mathf.Clamp(PistonMinReversals, 1, 12);
            PistonAcceptedRetrySeconds = Mathf.Clamp(PistonAcceptedRetrySeconds, 0f, 30f);
            PistonRejectedRetrySeconds = Mathf.Clamp(PistonRejectedRetrySeconds, 0.05f, 10f);
            PistonVoiceVolume = Mathf.Clamp01(PistonVoiceVolume);
            PistonVoiceCooldownSeconds = Mathf.Clamp(PistonVoiceCooldownSeconds, 0f, 30f);
            HardPistonWindowSeconds = Mathf.Clamp(HardPistonWindowSeconds, 0.1f, 5f);
            HardPistonDistanceCm = Mathf.Clamp(HardPistonDistanceCm, 1f, 100f);
            HardPistonMinSpeed = Mathf.Clamp(HardPistonMinSpeed, 0.02f, 2f);
            HardPistonMinReversals = Mathf.Clamp(HardPistonMinReversals, 1, 12);
            HardPistonReleaseSeconds = Mathf.Clamp(HardPistonReleaseSeconds, 0.05f, 5f);
            HardPistonVoiceVolume = Mathf.Clamp01(HardPistonVoiceVolume);
            HardPistonVoiceCooldownSeconds = Mathf.Clamp(HardPistonVoiceCooldownSeconds, 0f, 30f);
            HardPistonAcceptedRetrySeconds = Mathf.Clamp(HardPistonAcceptedRetrySeconds, 0f, 30f);
            HardPistonRejectedRetrySeconds = Mathf.Clamp(HardPistonRejectedRetrySeconds, 0.05f, 10f);
            OgazmEnterGaugeFraction = Mathf.Clamp(OgazmEnterGaugeFraction, 0.05f, 1f);
            OgazmExitGaugeFraction = Mathf.Clamp(OgazmExitGaugeFraction, 0f, OgazmEnterGaugeFraction);
            OgazmEyeStartGaugeFraction = Mathf.Clamp(OgazmEyeStartGaugeFraction, 0f, 0.99f);
            OgazmEyeFullGaugeFraction = Mathf.Clamp(OgazmEyeFullGaugeFraction,
                OgazmEyeStartGaugeFraction + 0.01f, 1f);
            OgazmEyeInwardDegrees = Mathf.Clamp(OgazmEyeInwardDegrees, 0f, 30f);
            OgazmEyeVerticalDegrees = Mathf.Clamp(OgazmEyeVerticalDegrees, -60f, 60f);
            OgazmIrisInwardUv = Mathf.Clamp(OgazmIrisInwardUv, -0.3f, 0.3f);
            OgazmIrisUpUv = Mathf.Clamp(OgazmIrisUpUv, -0.3f, 0.3f);
            OgazmVoiceVolume = Mathf.Clamp01(OgazmVoiceVolume);
            OgazmVoiceCooldownSeconds = Mathf.Clamp(OgazmVoiceCooldownSeconds, 0f, 60f);
            MouthOpenStartDistance = Mathf.Clamp(MouthOpenStartDistance, 0.01f, 0.5f);
            MouthOpenFullDistance = Mathf.Clamp(MouthOpenFullDistance, 0f, MouthOpenStartDistance);
            MouthOpenReleaseDistance = Mathf.Clamp(MouthOpenReleaseDistance, MouthOpenStartDistance, 0.8f);
            MouthOpenAmount = Mathf.Clamp01(MouthOpenAmount);
            MouthContactOpenAmount = Mathf.Clamp01(MouthContactOpenAmount);
            MouthApproachPattern = Mathf.Clamp(MouthApproachPattern, 0, 40);
            MouthContactPattern = Mathf.Clamp(MouthContactPattern, 0, 40);
            MouthOpenSeconds = Mathf.Clamp(MouthOpenSeconds, 0.02f, 2f);
            OralContactRadius = Mathf.Clamp(OralContactRadius, 0.005f, 0.2f);
            OralAudioVolume = Mathf.Clamp01(OralAudioVolume);
            OralVoiceIntervalSeconds = Mathf.Clamp(OralVoiceIntervalSeconds, 2f, 60f);
            if (Version < 2)
            {
                GripOffsetX = 0f; GripOffsetY = 0.01f; GripOffsetZ = 0f; GripRadius = 0.08f;
                Version = 2;
            }
            if (Version < 3)
            {
                AibuWindowSeconds = 1f;
                AibuStrongVoiceEnabled = true;
                AibuStrongVoiceOnCm = 12f;
                AibuStrongVoiceOffCm = 6f;
                Version = 3;
            }
            if (Version < 4)
            {
                BodyReactionEnabled = false;
                Version = 4;
            }
            if (Version < 5)
            {
                // Lower only the former defaults; retain values users tuned themselves.
                if (Mathf.Approximately(AibuPointsPerMetre, 40f)) AibuPointsPerMetre = 8f;
                if (Mathf.Approximately(AibuMaxPointsPerSecond, 12f)) AibuMaxPointsPerSecond = 2f;
                Version = 5;
            }
            if (Version < 6)
            {
                AutoSpawnOnH = false;
                OralTargetEnabled = true;
                MouthOpenStartDistance = 0.12f;
                MouthOpenReleaseDistance = 0.16f;
                MouthOpenAmount = 0.6f;
                MouthOpenSeconds = 0.2f;
                OralContactRadius = 0.04f;
                Version = 6;
            }
            if (Version < 7)
            {
                OralAudioEnabled = true;
                OralAudioVolume = 1f;
                OralVoiceIntervalSeconds = 8f;
                Version = 7;
            }
            if (Version < 8)
            {
                MouthOpenFullDistance = Mathf.Min(0.02f, MouthOpenStartDistance);
                MouthContactOpenAmount = 1f;
                MouthApproachPattern = 21;
                MouthContactPattern = 22;
                Version = 8;
            }
            if (Version < 9) Version = 9;
            if (Version < 10) Version = 10;
            if (Version < 11) Version = 11;
            if (Version < 12) Version = 12;
            if (Version < 13)
            {
                if (Mathf.Approximately(OgazmEyeVerticalDegrees, 0f)) OgazmEyeVerticalDegrees = 30f;
                Version = 13;
            }
            if (Version < 14) Version = 14;
            if (Segments == null || Segments.Length != 8) Segments = CreateDefault().Segments;
            for (int i = 0; i < Segments.Length; i++)
            {
                if (Segments[i] == null) Segments[i] = new SegmentSettings();
                Segments[i].Bone = "VRVibe_" + i.ToString("00");
                Segments[i].Radius = Mathf.Clamp(Segments[i].Radius, 0.001f, 0.1f);
                Segments[i].LengthScale = Mathf.Clamp(Segments[i].LengthScale, 0.1f, 4f);
                if (Segments[i].LengthMetres > 0f)
                    Segments[i].LengthMetres = Mathf.Clamp(Segments[i].LengthMetres,
                        Segments[i].Radius * 2f, 0.3f);
                else Segments[i].LengthMetres = 0f;
                Segments[i].FkX = Mathf.Clamp(Segments[i].FkX, -80f, 80f);
                Segments[i].FkZ = Mathf.Clamp(Segments[i].FkZ, -80f, 80f);
            }
        }
    }
}
