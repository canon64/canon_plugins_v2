using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MainGameSpeedLimitBreak
{
    [DataContract]
    internal sealed class PluginSettings
    {
        private static float EstimateBpmFromSpeed(
            float speed,
            float sourceMin,
            float sourceMax,
            float bpmMin,
            float bpmMax)
        {
            float safeSourceMax = Math.Max(sourceMin + 0.0001f, sourceMax);
            if (bpmMin > 0f && bpmMax > bpmMin && safeSourceMax > sourceMin + 0.0001f)
            {
                float t = (speed - sourceMin) / (safeSourceMax - sourceMin);
                return bpmMin + (bpmMax - bpmMin) * t;
            }

            float safeBpmMax = Math.Max(1f, bpmMax);
            return (speed / safeSourceMax) * safeBpmMax;
        }

        private static bool CueHasAnyAction(VideoTimeSpeedCue c)
        {
            if (c == null)
            {
                return false;
            }

            if (CueHasGaugeSignal(c))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(c.PresetName))
            {
                return true;
            }

            if (c.FaceDbEnabled)
            {
                return true;
            }

            if (c.TaiiId.HasValue || !string.IsNullOrWhiteSpace(c.TaiiName))
            {
                return true;
            }

            if (c.TaiiMode.HasValue)
            {
                return true;
            }

            if (c.CoordinateType.HasValue)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(c.ClickKind))
            {
                return true;
            }

            if (c.VoicePlayCode.HasValue)
            {
                return true;
            }

            if (c.ClothesStates != null && c.ClothesStates.Count > 0)
            {
                return true;
            }

            return false;
        }

        private static bool CueHasGaugeSignal(VideoTimeSpeedCue c)
        {
            if (c == null)
            {
                return false;
            }

            if (c.GaugeSpeed13 >= 1f && c.GaugeSpeed13 <= 3f)
            {
                return true;
            }

            if (c.GaugePos01 >= 0f && c.GaugePos01 <= 1f)
            {
                return true;
            }

            // Legacy/hand-edited input: 1..3 accidentally stored in GaugePos01.
            if (c.GaugePos01 > 1f && c.GaugePos01 <= 3f)
            {
                return true;
            }

            return false;
        }

        private static void NormalizeGaugeFields(VideoTimeSpeedCue cue)
        {
            if (cue == null)
            {
                return;
            }

            if (cue.GaugeSpeed13 >= 1f && cue.GaugeSpeed13 <= 3f)
            {
                cue.GaugePos01 = (cue.GaugeSpeed13 - 1f) / 2f;
                return;
            }

            if (cue.GaugePos01 >= 0f && cue.GaugePos01 <= 1f)
            {
                cue.GaugeSpeed13 = 1f + (cue.GaugePos01 * 2f);
                return;
            }

            if (cue.GaugePos01 > 1f && cue.GaugePos01 <= 3f)
            {
                cue.GaugeSpeed13 = cue.GaugePos01;
                cue.GaugePos01 = (cue.GaugePos01 - 1f) / 2f;
                return;
            }

            cue.GaugePos01 = -1f;
            cue.GaugeSpeed13 = -1f;
        }

        private static string NormalizeGaugeEasingName(string raw)
        {
            string key = raw?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return "linear";
            }

            return key;
        }

        private static void NormalizeGaugeTransitionFields(VideoTimeSpeedCue cue)
        {
            if (cue == null)
            {
                return;
            }

            if (cue.GaugeTransitionSec < 0f)
            {
                cue.GaugeTransitionSec = 0f;
            }

            cue.GaugeEasing = NormalizeGaugeEasingName(cue.GaugeEasing);
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            if (TargetMaxSpeed < TargetMinSpeed)
            {
                TargetMaxSpeed = TargetMinSpeed;
            }

            if (SourceMaxSpeed <= SourceMinSpeed)
            {
                SourceMaxSpeed = SourceMinSpeed + 0.01f;
            }

            if (LogIntervalSec < 0.1f)
            {
                LogIntervalSec = 0.1f;
            }

            float legacyRefMin = BpmReferenceAtSourceMin;
            float legacyRefMax = BpmReferenceAtSpeed3;
            if (legacyRefMin < 0f)
            {
                legacyRefMin = 44.5f;
            }
            if (legacyRefMax < 1f)
            {
                legacyRefMax = 135.6f;
            }

            if (BpmReferenceAtSourceMinSonyu < 0f)
            {
                BpmReferenceAtSourceMinSonyu = legacyRefMin;
            }

            if (BpmReferenceAtSpeed3Sonyu < 1f)
            {
                BpmReferenceAtSpeed3Sonyu = legacyRefMax;
            }

            if (BpmReferenceAtSourceMinSonyu < 0f)
            {
                BpmReferenceAtSourceMinSonyu = 0f;
            }

            if (BpmReferenceAtSpeed3Sonyu < 1f)
            {
                BpmReferenceAtSpeed3Sonyu = 1f;
            }

            if (BpmReferenceAtSourceMinSonyu > 0f && BpmReferenceAtSpeed3Sonyu <= BpmReferenceAtSourceMinSonyu)
            {
                BpmReferenceAtSpeed3Sonyu = BpmReferenceAtSourceMinSonyu + 1f;
            }

            if (BpmReferenceAtSourceMinHoushi < 0f)
            {
                BpmReferenceAtSourceMinHoushi = 45f;
            }

            if (BpmReferenceAtSpeed3Houshi < 1f)
            {
                BpmReferenceAtSpeed3Houshi = 113f;
            }

            if (BpmReferenceAtSourceMinHoushi < 0f)
            {
                BpmReferenceAtSourceMinHoushi = 0f;
            }

            if (BpmReferenceAtSpeed3Houshi < 1f)
            {
                BpmReferenceAtSpeed3Houshi = 1f;
            }

            if (BpmReferenceAtSourceMinHoushi > 0f && BpmReferenceAtSpeed3Houshi <= BpmReferenceAtSourceMinHoushi)
            {
                BpmReferenceAtSpeed3Houshi = BpmReferenceAtSourceMinHoushi + 1f;
            }

            BpmReferenceAtSourceMin = BpmReferenceAtSourceMinSonyu;
            BpmReferenceAtSpeed3 = BpmReferenceAtSpeed3Sonyu;

            if (BpmReferenceAtSpeed3 < 1f)
            {
                BpmReferenceAtSpeed3 = 1f;
            }

            if (BpmReferenceAtSourceMin < 0f)
            {
                BpmReferenceAtSourceMin = 0f;
            }

            if (AppliedBpmMax <= 0f)
            {
                AppliedBpmMax = EstimateBpmFromSpeed(
                    TargetMaxSpeed,
                    SourceMinSpeed,
                    SourceMaxSpeed,
                    BpmReferenceAtSourceMin,
                    BpmReferenceAtSpeed3);
            }

            if (AppliedBpmMin < 0f)
            {
                AppliedBpmMin = 0f;
            }

            if (AppliedBpmMin <= 0f)
            {
                AppliedBpmMin = EstimateBpmFromSpeed(
                    TargetMinSpeed,
                    SourceMinSpeed,
                    SourceMaxSpeed,
                    BpmReferenceAtSourceMin,
                    BpmReferenceAtSpeed3);
            }

            if (AppliedBpmMax <= 0f)
            {
                AppliedBpmMax = Math.Max(1f, BpmReferenceAtSpeed3);
            }

            if (AppliedBpmMin < 0f)
            {
                AppliedBpmMin = 0f;
            }

            if (AppliedBpmMax <= AppliedBpmMin)
            {
                AppliedBpmMin = Math.Max(0f, AppliedBpmMax * 0.25f);
            }

            if (VideoTimeSpeedCues == null)
            {
                VideoTimeSpeedCues = new List<VideoTimeSpeedCue>();
            }

            for (int i = 0; i < VideoTimeSpeedCues.Count; i++)
            {
                var cue = VideoTimeSpeedCues[i];
                if (cue == null)
                {
                    continue;
                }

                cue.PresetName = cue.PresetName?.Trim() ?? string.Empty;
                cue.FaceDbPath = cue.FaceDbPath?.Trim() ?? string.Empty;
                cue.FaceDbChara = cue.FaceDbChara?.Trim() ?? string.Empty;
                cue.FaceDbNameContains = cue.FaceDbNameContains?.Trim() ?? string.Empty;
                cue.TaiiName = cue.TaiiName?.Trim() ?? string.Empty;
                cue.ClickKind = cue.ClickKind?.Trim() ?? string.Empty;
                NormalizeGaugeFields(cue);
                NormalizeGaugeTransitionFields(cue);

                if (cue.VoicePlayCode.HasValue && cue.VoicePlayCode.Value < 0)
                {
                    cue.VoicePlayCode = null;
                }

                if (cue.TargetFemaleIndex < 0)
                {
                    cue.TargetFemaleIndex = 0;
                }

                if (cue.ClothesStates == null)
                {
                    cue.ClothesStates = new List<ClothesPartStateEntry>();
                }
                else
                {
                    cue.ClothesStates.RemoveAll(x => x == null);
                }
            }

            VideoTimeSpeedCues.RemoveAll(c =>
                c == null ||
                c.TimeSec < 0d ||
                !CueHasAnyAction(c));

            if (BpmPresets == null)
            {
                BpmPresets = new List<BpmPreset>();
            }

            for (int i = 0; i < BpmPresets.Count; i++)
            {
                var p = BpmPresets[i];
                if (p == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(p.AnimationName) && !string.IsNullOrWhiteSpace(p.Name))
                {
                    p.AnimationName = p.Name.Trim();
                }

                if (string.IsNullOrWhiteSpace(p.Folder))
                {
                    p.Folder = p.AnimationName;
                }

                if (p.BaseBpmMin <= 0f && p.ReferenceBpmMin > 0f)
                {
                    p.BaseBpmMin = p.ReferenceBpmMin;
                }

                if ((p.BaseBpmMax <= 1f && p.ReferenceBpmMax > 1f) ||
                    (p.BaseBpmMax <= 0f && p.ReferenceBpmMax > 0f))
                {
                    p.BaseBpmMax = p.ReferenceBpmMax;
                }

                if (p.BaseBpmMin < 0f)
                {
                    p.BaseBpmMin = 0f;
                }

                if (p.BaseBpmMax < 1f)
                {
                    p.BaseBpmMax = 1f;
                }

                if (p.BaseBpmMax <= p.BaseBpmMin)
                {
                    p.BaseBpmMax = p.BaseBpmMin + 1f;
                }

                if (p.AppliedBpmMax <= 0f)
                {
                    if (p.AppliedBpm > 0f)
                    {
                        p.AppliedBpmMax = p.AppliedBpm;
                    }
                    else if (p.Bpm > 0f)
                    {
                        p.AppliedBpmMax = p.Bpm;
                    }
                    else
                    {
                        p.AppliedBpmMax = AppliedBpmMax;
                    }
                }

                if (p.AppliedBpmMin < 0f)
                {
                    p.AppliedBpmMin = 0f;
                }

                if (p.AppliedBpmMin <= 0f)
                {
                    p.AppliedBpmMin = p.AppliedBpmMax > 0f
                        ? p.AppliedBpmMax * 0.25f
                        : AppliedBpmMin;
                }

                if (p.AppliedBpmMax <= p.AppliedBpmMin)
                {
                    p.AppliedBpmMin = Math.Max(0f, p.AppliedBpmMax * 0.25f);
                }

                p.ReferenceBpmMin = p.BaseBpmMin;
                p.ReferenceBpmMax = p.BaseBpmMax;
                p.AppliedBpm = p.AppliedBpmMax;
                p.Bpm = p.AppliedBpmMax;

                if (string.IsNullOrWhiteSpace(p.Name))
                {
                    p.Name = p.AnimationName;
                }
            }

            BpmPresets.RemoveAll(p =>
                p == null ||
                string.IsNullOrWhiteSpace(p.AnimationName) ||
                p.AppliedBpmMax <= 0f);

            if (AutoSonyuHijackFixedSourceSpeed < 0f)
            {
                AutoSonyuHijackFixedSourceSpeed = 0f;
            }

            if (BpmMeasureWindowSec < 0.2f)
            {
                BpmMeasureWindowSec = 0.2f;
            }

            if (BpmMeasureStrokesPerLoop <= 0f)
            {
                BpmMeasureStrokesPerLoop = 1f;
            }

            if (BpmMeasureMinAccumSec < 0.1f)
            {
                BpmMeasureMinAccumSec = 0.1f;
            }

            if (!EnableBpmSpeedRemap.HasValue)
            {
                EnableBpmSpeedRemap = true;
            }

            if (MasturbationTraceIntervalSec < 0.1f)
            {
                MasturbationTraceIntervalSec = 0.5f;
            }
            else if (MasturbationTraceIntervalSec > 10f)
            {
                MasturbationTraceIntervalSec = 10f;
            }
        }

        [DataMember(Order = 0)]
        public bool Enabled = true;

        [DataMember(Order = 1)]
        public bool AffectsSpeed = true;

        [DataMember(Order = 2)]
        public bool AffectsSpeedBody = true;

        [DataMember(Order = 3)]
        public bool ApplyOnlyInsideHScene = true;

        [DataMember(Order = 4)]
        public bool IgnoreValuesBelowSourceMin = true;

        [DataMember(Order = 5)]
        public float SourceMinSpeed = 1.0f;

        [DataMember(Order = 6)]
        public float SourceMaxSpeed = 3.0f;

        [DataMember(Order = 7)]
        public float TargetMinSpeed = 0.8014706f;

        [DataMember(Order = 8)]
        public float TargetMaxSpeed = 4.17647076f;

        [DataMember(Order = 9)]
        public bool VerboseLog = false;

        [DataMember(Order = 10)]
        public float LogIntervalSec = 1.0f;

        [DataMember(Order = 11)]
        public float BpmReferenceAtSpeed3 = 113f;

        [DataMember(Order = 12)]
        public List<BpmPreset> BpmPresets = new List<BpmPreset>
        {
            new BpmPreset { AnimationName = "M_SLoop1", AppliedBpmMax = 150f, AppliedBpmMin = 37.5f, BaseBpmMin = 47f, BaseBpmMax = 135f, Folder = "M_SLoop1", Name = "挿入 150-37.5" },
            new BpmPreset { AnimationName = "M_WLoop1", AppliedBpmMax = 150f, AppliedBpmMin = 37.5f, BaseBpmMin = 45.08f, BaseBpmMax = 112.63f, Folder = "M_WLoop1", Name = "フェラ 150-37.5" },
        };

        [DataMember(Order = 13)]
        public bool EnableAutoSonyuHijack = true;

        [DataMember(Order = 14)]
        public bool AutoSonyuHijackRequireAutoLock = true;

        [DataMember(Order = 15)]
        public bool AutoSonyuHijackAlsoSonyu3P = false;

        [DataMember(Order = 16)]
        public bool AutoSonyuHijackAlsoSonyu3PMMF = false;

        [DataMember(Order = 17)]
        public bool AutoSonyuHijackUseSourceMax = true;

        [DataMember(Order = 18)]
        public float AutoSonyuHijackFixedSourceSpeed = 3.0f;

        [DataMember(Order = 19)]
        public float BpmReferenceAtSourceMin = 45f;

        [DataMember(Order = 20)]
        public float BpmMeasureWindowSec = 0.2f;

        [DataMember(Order = 21)]
        public float BpmMeasureStrokesPerLoop = 1.0f;

        [DataMember(Order = 22)]
        public float BpmMeasureNegativeDeltaResetThreshold = 0f;

        [DataMember(Order = 23)]
        public float BpmMeasureMinAccumSec = 0.1f;

        [DataMember(Order = 24)]
        public bool BpmMeasureAbortOnStateChange = false;

        [DataMember(Order = 25)]
        public float AppliedBpmMin = 38.25f;

        [DataMember(Order = 26)]
        public float AppliedBpmMax = 153f;

        [DataMember(Order = 27)]
        public bool EnableVideoTimeSpeedCues = false;

        [DataMember(Order = 28)]
        public bool VideoTimeCuesResetOnLoop = true;

        [DataMember(Order = 29)]
        public List<VideoTimeSpeedCue> VideoTimeSpeedCues = new List<VideoTimeSpeedCue>();

        [DataMember(Order = 30, EmitDefaultValue = false)]
        public bool? EnableBpmSpeedRemap = true;

        // 毎フレーム系の診断ログ（patch/timeline trace）を出すか。
        [DataMember(Order = 31)]
        public bool EnablePerFrameTrace = false;

        [DataMember(Order = 32)]
        public bool ForceVanillaSpeed = false;

        // 挿入系モーション向け基準BPM（速度1/速度3）。
        [DataMember(Order = 33)]
        public float BpmReferenceAtSourceMinSonyu = 47f;

        [DataMember(Order = 34)]
        public float BpmReferenceAtSpeed3Sonyu = 135f;

        // 奉仕系モーション向け基準BPM（速度1/速度3）。
        [DataMember(Order = 35)]
        public float BpmReferenceAtSourceMinHoushi = 45f;

        [DataMember(Order = 36)]
        public float BpmReferenceAtSpeed3Houshi = 113f;

        [DataMember(Order = 37)]
        public bool ShowBpmUi = false;

        [DataMember(Order = 38)]
        public bool EnableMasturbationTransitionTrace = false;

        [DataMember(Order = 39)]
        public float MasturbationTraceIntervalSec = 0.5f;
    }

    [DataContract]
    internal sealed class BpmPreset
    {
        [DataMember(Order = 0)]
        public string AnimationName = "";

        [DataMember(Order = 1)]
        public float AppliedBpmMax = 0f;

        [DataMember(Order = 2)]
        public float AppliedBpmMin = 0f;

        [DataMember(Order = 3)]
        public float BaseBpmMin = 0f;

        [DataMember(Order = 4)]
        public float BaseBpmMax = 1f;

        [DataMember(Order = 5)]
        public string Folder = "";

        // Legacy compatibility (read/write both for smooth migration).
        [DataMember(Order = 10)]
        public string Name = "";

        [DataMember(Order = 11)]
        public float Bpm = 0f;

        [DataMember(Order = 12)]
        public float AppliedBpm = 0f;

        [DataMember(Order = 13)]
        public float ReferenceBpmMin = 0f;

        [DataMember(Order = 14)]
        public float ReferenceBpmMax = 1f;
    }

    [DataContract]
    internal sealed class VideoTimeSpeedCue
    {
        [DataMember(Order = 0)]
        public double TimeSec = 0d;

        [DataMember(Order = 1)]
        public string PresetName = "";

        [DataMember(Order = 2)]
        public bool Enabled = true;

        [DataMember(Order = 3)]
        public bool TriggerOnce = false;

        [DataMember(Order = 4)]
        public float GaugePos01 = -1f;

        [DataMember(Order = 5)]
        public bool FaceDbEnabled = false;

        [DataMember(Order = 6)]
        public bool FaceDbRandom = true;

        [DataMember(Order = 7)]
        public string FaceDbPath = "";

        [DataMember(Order = 8, EmitDefaultValue = false)]
        public int? FaceDbFileId = null;

        [DataMember(Order = 9, EmitDefaultValue = false)]
        public int? FaceDbFaceId = null;

        [DataMember(Order = 10)]
        public string FaceDbChara = "";

        [DataMember(Order = 11, EmitDefaultValue = false)]
        public int? FaceDbVoiceKind = null;

        [DataMember(Order = 12, EmitDefaultValue = false)]
        public int? FaceDbAction = null;

        [DataMember(Order = 13)]
        public string FaceDbNameContains = "";

        [DataMember(Order = 14)]
        public int TargetFemaleIndex = 0;

        [DataMember(Order = 15, EmitDefaultValue = false)]
        public int? TaiiId = null;

        [DataMember(Order = 16)]
        public string TaiiName = "";

        [DataMember(Order = 17, EmitDefaultValue = false)]
        public int? TaiiMode = null;

        [DataMember(Order = 18, EmitDefaultValue = false)]
        public int? CoordinateType = null;

        [DataMember(Order = 19)]
        public List<ClothesPartStateEntry> ClothesStates = new List<ClothesPartStateEntry>();

        [DataMember(Order = 20)]
        public string ClickKind = "";

        [DataMember(Order = 21, EmitDefaultValue = false)]
        public int? VoicePlayCode = null;

        // 速度の正規値(1..3)。GaugePos01(0..1) と相互変換して扱う。
        [DataMember(Order = 22)]
        public float GaugeSpeed13 = -1f;

        // 目標速度までの補間秒数。0以下なら即時反映。
        [DataMember(Order = 23)]
        public float GaugeTransitionSec = 0f;

        // 補間イージング。linear / easeInQuad / easeOutQuad / easeInOutQuad など。
        [DataMember(Order = 24)]
        public string GaugeEasing = "linear";
    }

    [DataContract]
    internal sealed class ClothesPartStateEntry
    {
        [DataMember(Order = 0)]
        public int Kind = -1;

        [DataMember(Order = 1)]
        public int State = -1;
    }
}
