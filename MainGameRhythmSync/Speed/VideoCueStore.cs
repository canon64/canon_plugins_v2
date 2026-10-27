using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace MainGameSpeedLimitBreak
{
    [DataContract]
    internal sealed class VideoCueTimelineFile
    {
        [DataMember(Order = 0)]
        public bool ResetOnLoop = true;

        [DataMember(Order = 1)]
        public List<VideoTimeSpeedCue> Cues = new List<VideoTimeSpeedCue>();
    }

    internal static class VideoCueStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private static string NormalizeGaugeEasingName(string raw)
        {
            string key = raw?.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                return "linear";
            }

            return key;
        }

        private static bool TryResolveGauge(VideoTimeSpeedCue cue, out float gauge01, out float speed13)
        {
            gauge01 = -1f;
            speed13 = -1f;
            if (cue == null)
            {
                return false;
            }

            if (cue.GaugeSpeed13 >= 1f && cue.GaugeSpeed13 <= 3f)
            {
                speed13 = cue.GaugeSpeed13;
                gauge01 = (speed13 - 1f) / 2f;
                return true;
            }

            if (cue.GaugePos01 >= 0f && cue.GaugePos01 <= 1f)
            {
                gauge01 = cue.GaugePos01;
                speed13 = 1f + (gauge01 * 2f);
                return true;
            }

            // Legacy/hand-edited input: 1..3 accidentally stored in GaugePos01.
            if (cue.GaugePos01 > 1f && cue.GaugePos01 <= 3f)
            {
                speed13 = cue.GaugePos01;
                gauge01 = (speed13 - 1f) / 2f;
                return true;
            }

            return false;
        }

        private static void NormalizeLegacyActionDefaults(VideoTimeSpeedCue cue)
        {
            if (cue == null)
            {
                return;
            }

            bool hasPreset = !string.IsNullOrWhiteSpace(cue.PresetName);
            bool hasClothes = cue.ClothesStates != null && cue.ClothesStates.Count > 0;
            bool hasClick = !string.IsNullOrWhiteSpace(cue.ClickKind);
            bool hasVoice = cue.VoicePlayCode.HasValue && cue.VoicePlayCode.Value >= 0;
            bool hasFaceSignal =
                cue.FaceDbEnabled ||
                !string.IsNullOrWhiteSpace(cue.FaceDbPath) ||
                !string.IsNullOrWhiteSpace(cue.FaceDbChara) ||
                !string.IsNullOrWhiteSpace(cue.FaceDbNameContains) ||
                (cue.FaceDbFileId.HasValue && cue.FaceDbFileId.Value != 0) ||
                (cue.FaceDbFaceId.HasValue && cue.FaceDbFaceId.Value != 0) ||
                (cue.FaceDbVoiceKind.HasValue && cue.FaceDbVoiceKind.Value != 0) ||
                (cue.FaceDbAction.HasValue && cue.FaceDbAction.Value != 0);
            bool hasTaiiName = !string.IsNullOrWhiteSpace(cue.TaiiName);
            bool hasTaiiSignal =
                hasTaiiName ||
                (cue.TaiiId.HasValue && cue.TaiiId.Value != 0) ||
                (cue.TaiiMode.HasValue && cue.TaiiMode.Value != 0);

            if (cue.TaiiId.HasValue &&
                cue.TaiiMode.HasValue &&
                cue.TaiiId.Value == 0 &&
                cue.TaiiMode.Value == 0 &&
                !hasTaiiName)
            {
                bool keepAsTaiiOnly =
                    !hasPreset &&
                    !hasClothes &&
                    !hasClick &&
                    !hasVoice &&
                    !hasFaceSignal &&
                    !cue.CoordinateType.HasValue;

                if (!keepAsTaiiOnly)
                {
                    cue.TaiiId = null;
                    cue.TaiiMode = null;
                }
            }

            if (cue.CoordinateType.HasValue && cue.CoordinateType.Value == 0)
            {
                bool hasAnyOtherSignal =
                    hasPreset ||
                    hasClothes ||
                    hasClick ||
                    hasVoice ||
                    hasFaceSignal ||
                    hasTaiiSignal ||
                    cue.TaiiId.HasValue ||
                    cue.TaiiMode.HasValue;

                bool keepAsCoordinateOnly = !hasAnyOtherSignal;
                if (!keepAsCoordinateOnly)
                {
                    cue.CoordinateType = null;
                }
            }

            if (!cue.FaceDbEnabled)
            {
                if (cue.FaceDbFileId.HasValue && cue.FaceDbFileId.Value == 0)
                {
                    cue.FaceDbFileId = null;
                }

                if (cue.FaceDbFaceId.HasValue && cue.FaceDbFaceId.Value == 0)
                {
                    cue.FaceDbFaceId = null;
                }

                if (cue.FaceDbVoiceKind.HasValue && cue.FaceDbVoiceKind.Value == 0)
                {
                    cue.FaceDbVoiceKind = null;
                }

                if (cue.FaceDbAction.HasValue && cue.FaceDbAction.Value == 0)
                {
                    cue.FaceDbAction = null;
                }
            }
        }

        private static bool CueHasAnyAction(VideoTimeSpeedCue cue)
        {
            if (cue == null)
            {
                return false;
            }

            if (TryResolveGauge(cue, out _, out _))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(cue.PresetName))
            {
                return true;
            }

            if (cue.FaceDbEnabled)
            {
                return true;
            }

            if (cue.TaiiId.HasValue || !string.IsNullOrWhiteSpace(cue.TaiiName))
            {
                return true;
            }

            if (cue.TaiiMode.HasValue)
            {
                return true;
            }

            if (cue.CoordinateType.HasValue)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(cue.ClickKind))
            {
                return true;
            }

            if (cue.VoicePlayCode.HasValue && cue.VoicePlayCode.Value >= 0)
            {
                return true;
            }

            if (cue.ClothesStates != null && cue.ClothesStates.Count > 0)
            {
                return true;
            }

            return false;
        }

        internal static VideoCueTimelineFile LoadOrCreate(
            string path,
            bool legacyResetOnLoop,
            List<VideoTimeSpeedCue> legacyCues,
            Action<string> info,
            Action<string> warn,
            Action<string> error)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (!File.Exists(path))
                {
                    var seeded = new VideoCueTimelineFile
                    {
                        ResetOnLoop = legacyResetOnLoop,
                        Cues = Sanitize(legacyCues)
                    };

                    Save(path, seeded);
                    if (seeded.Cues.Count > 0)
                    {
                        info?.Invoke("video cue file created from legacy settings: " + path);
                    }
                    else
                    {
                        info?.Invoke("video cue file created: " + path);
                    }

                    return seeded;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                var parsed = Deserialize(json);
                if (parsed == null)
                {
                    warn?.Invoke("video cue file parse failed; fallback default");
                    return new VideoCueTimelineFile
                    {
                        ResetOnLoop = legacyResetOnLoop,
                        Cues = Sanitize(legacyCues)
                    };
                }

                parsed.Cues = Sanitize(parsed.Cues);
                Save(path, parsed);
                return parsed;
            }
            catch (Exception ex)
            {
                error?.Invoke("video cue file load failed: " + ex.Message);
                return new VideoCueTimelineFile
                {
                    ResetOnLoop = legacyResetOnLoop,
                    Cues = Sanitize(legacyCues)
                };
            }
        }

        private static void Save(string path, VideoCueTimelineFile settings)
        {
            var serializer = new DataContractJsonSerializer(typeof(VideoCueTimelineFile));
            using (var ms = new MemoryStream())
            {
                serializer.WriteObject(ms, settings ?? new VideoCueTimelineFile());
                string json = Encoding.UTF8.GetString(ms.ToArray());
                File.WriteAllText(path, json, Utf8NoBom);
            }
        }

        private static VideoCueTimelineFile Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var serializer = new DataContractJsonSerializer(typeof(VideoCueTimelineFile));
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (var ms = new MemoryStream(bytes))
            {
                return serializer.ReadObject(ms) as VideoCueTimelineFile;
            }
        }

        private static List<VideoTimeSpeedCue> Sanitize(List<VideoTimeSpeedCue> cues)
        {
            var result = new List<VideoTimeSpeedCue>();
            if (cues == null)
                return result;

            for (int i = 0; i < cues.Count; i++)
            {
                var c = cues[i];
                if (c == null)
                    continue;
                NormalizeLegacyActionDefaults(c);
                if (c.TimeSec < 0d)
                    continue;
                if (!CueHasAnyAction(c))
                    continue;

                var clothes = new List<ClothesPartStateEntry>();
                if (c.ClothesStates != null)
                {
                    for (int j = 0; j < c.ClothesStates.Count; j++)
                    {
                        var e = c.ClothesStates[j];
                        if (e == null)
                            continue;
                        if (e.Kind < 0 || e.State < 0)
                            continue;

                        clothes.Add(new ClothesPartStateEntry
                        {
                            Kind = e.Kind,
                            State = e.State
                        });
                    }
                }

                result.Add(new VideoTimeSpeedCue
                {
                    TimeSec = c.TimeSec,
                    PresetName = c.PresetName?.Trim() ?? string.Empty,
                    Enabled = c.Enabled,
                    TriggerOnce = c.TriggerOnce,
                    GaugePos01 = TryResolveGauge(c, out var gauge01, out _) ? gauge01 : -1f,
                    GaugeSpeed13 = TryResolveGauge(c, out _, out var speed13) ? speed13 : -1f,
                    GaugeTransitionSec = c.GaugeTransitionSec < 0f ? 0f : c.GaugeTransitionSec,
                    GaugeEasing = NormalizeGaugeEasingName(c.GaugeEasing),
                    FaceDbEnabled = c.FaceDbEnabled,
                    FaceDbRandom = c.FaceDbRandom,
                    FaceDbPath = c.FaceDbPath?.Trim() ?? string.Empty,
                    FaceDbFileId = c.FaceDbFileId,
                    FaceDbFaceId = c.FaceDbFaceId,
                    FaceDbChara = c.FaceDbChara?.Trim() ?? string.Empty,
                    FaceDbVoiceKind = c.FaceDbVoiceKind,
                    FaceDbAction = c.FaceDbAction,
                    FaceDbNameContains = c.FaceDbNameContains?.Trim() ?? string.Empty,
                    TargetFemaleIndex = c.TargetFemaleIndex < 0 ? 0 : c.TargetFemaleIndex,
                    TaiiId = c.TaiiId,
                    TaiiName = c.TaiiName?.Trim() ?? string.Empty,
                    TaiiMode = c.TaiiMode,
                    CoordinateType = c.CoordinateType,
                    ClothesStates = clothes,
                    ClickKind = c.ClickKind?.Trim() ?? string.Empty,
                    VoicePlayCode = (c.VoicePlayCode.HasValue && c.VoicePlayCode.Value >= 0) ? c.VoicePlayCode : null
                });
            }

            return result;
        }
    }
}
