using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using MainGameBlankMapAdd.Http;
using MainGameClothColliderCards;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        [DataContract]
        private sealed class ClothParameters
        {
            [DataMember(Name = "pinnedTop", EmitDefaultValue = false)] public float? PinnedTop;
            [DataMember(Name = "hemDistance", EmitDefaultValue = false)] public float? HemDistance;
            [DataMember(Name = "damping", EmitDefaultValue = false)] public float? Damping;
            [DataMember(Name = "friction", EmitDefaultValue = false)] public float? Friction;
            [DataMember(Name = "stretch", EmitDefaultValue = false)] public float? Stretch;
            [DataMember(Name = "bend", EmitDefaultValue = false)] public float? Bend;
            [DataMember(Name = "solverHz", EmitDefaultValue = false)] public float? SolverHz;
            [DataMember(Name = "extraDown", EmitDefaultValue = false)] public float? ExtraDown;
            [DataMember(Name = "worldVelocity", EmitDefaultValue = false)] public float? WorldVelocity;
            [DataMember(Name = "worldAcceleration", EmitDefaultValue = false)] public float? WorldAcceleration;
            [DataMember(Name = "gravity", EmitDefaultValue = false)] public bool? Gravity;
            [DataMember(Name = "useTethers", EmitDefaultValue = false)] public bool? UseTethers;
        }

        [DataContract]
        private sealed class ClothDiagnosticsView
        {
            [DataMember(Name = "sourceVertices")] public int SourceVertices;
            [DataMember(Name = "simulatedVertices")] public int SimulatedVertices;
            [DataMember(Name = "coefficientCount")] public int CoefficientCount;
            [DataMember(Name = "fixedCoefficients")] public int FixedCoefficients;
            [DataMember(Name = "freeCoefficients")] public int FreeCoefficients;
            [DataMember(Name = "maxAppliedDistance")] public float MaxAppliedDistance;
            [DataMember(Name = "meanAppliedDistance")] public float MeanAppliedDistance;
            [DataMember(Name = "meanVertexMotionPerSample")] public float MeanVertexMotionPerSample;
            [DataMember(Name = "meanOffsetFromCreation")] public float MeanOffsetFromCreation;
            [DataMember(Name = "maxOffsetFromCreation")] public float MaxOffsetFromCreation;
            [DataMember(Name = "anchorMotionPerSample")] public float AnchorMotionPerSample;
            [DataMember(Name = "sourceWorldHeight")] public float SourceWorldHeight;
            [DataMember(Name = "cloneWorldHeight")] public float CloneWorldHeight;
            [DataMember(Name = "sourceWorldScaleY")] public float SourceWorldScaleY;
            [DataMember(Name = "cloneWorldScaleY")] public float CloneWorldScaleY;
            [DataMember(Name = "sampleIntervalSeconds")] public float SampleIntervalSeconds;
            [DataMember(Name = "clothEnabled")] public bool ClothEnabled;
            [DataMember(Name = "useGravity")] public bool UseGravity;
            [DataMember(Name = "useTethers")] public bool UseTethers;
            [DataMember(Name = "externalAccelerationY")] public float ExternalAccelerationY;
        }

        [DataContract]
        private sealed class ClothStatus
        {
            [DataMember(Name = "ok")] public bool Ok;
            [DataMember(Name = "character")] public string Character;
            [DataMember(Name = "activeCloth")] public bool ActiveCloth;
            [DataMember(Name = "colliders")] public int Colliders;
            [DataMember(Name = "simulatedVertices")] public int SimulatedVertices;
            [DataMember(Name = "meanVertexMotionPerSample")] public float MeanVertexMotionPerSample;
            [DataMember(Name = "diagnostics")] public ClothDiagnosticsView Diagnostics;
            [DataMember(Name = "parameters")] public ClothParameters Parameters;
            [DataMember(Name = "message")] public string Message;
        }

        private LocalHttpResponse RouteClothRequest(string path, string body)
        {
            ClothParameters input = null;
            if (path == "/cloth/parameters")
            {
                if (string.IsNullOrWhiteSpace(body)) return LocalHttpResponse.Message(400, "JSON body required");
                try
                {
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(body)))
                        input = (ClothParameters)new DataContractJsonSerializer(typeof(ClothParameters)).ReadObject(stream);
                }
                catch (Exception ex) { return LocalHttpResponse.Message(400, "invalid JSON: " + ex.Message); }
                if (input == null) return LocalHttpResponse.Message(400, "invalid parameters");
                if (!ValidClothParameters(input)) return LocalHttpResponse.Message(400, "parameter out of range");
            }

            if (!_mainThreadDispatcher.TryInvoke(() => ApplyOrReadCloth(input, path == "/cloth/reset"), 1000,
                    out ClothStatus status, out string error))
                return LocalHttpResponse.Message(503, "cloth " + error);
            return new LocalHttpResponse(status.Ok ? 200 : 404, SerializeToJson(status));
        }

        private static bool ValidClothParameters(ClothParameters p)
        {
            return ValidRange(p.PinnedTop, 0.01f, 0.3f) &&
                   ValidRange(p.HemDistance, 0.02f, 1f) &&
                   ValidRange(p.Damping, 0f, 1f) &&
                   ValidRange(p.Friction, 0f, 1f) &&
                   ValidRange(p.Stretch, 0f, 1f) &&
                   ValidRange(p.Bend, 0f, 1f) &&
                   ValidRange(p.SolverHz, 30f, 240f) &&
                   ValidRange(p.ExtraDown, 0f, 20f) &&
                   ValidRange(p.WorldVelocity, 0f, 1f) &&
                   ValidRange(p.WorldAcceleration, 0f, 1f);
        }

        private static bool ValidRange(float? value, float min, float max) =>
            !value.HasValue || (!float.IsNaN(value.Value) && !float.IsInfinity(value.Value) &&
                                value.Value >= min && value.Value <= max);

        private static ClothStatus ApplyOrReadCloth(ClothParameters input, bool reset)
        {
            ColliderCardController selected = null;
            foreach (var controller in UnityEngine.Object.FindObjectsOfType<ColliderCardController>())
            {
                if (controller == null || controller.ChaControl == null || controller.ChaControl.sex != 1) continue;
                if (selected == null || controller.HasActiveCloth) selected = controller;
                if (controller.HasActiveCloth) break;
            }
            if (selected == null)
                return new ClothStatus { Ok = false, Message = "female character not found" };

            if (reset && !selected.ResetClothSimulation())
                return new ClothStatus { Ok = false, Character = selected.ChaControl.name,
                    Message = "active flare skirt Cloth not found" };

            var tuning = selected.CurrentClothTuning;
            if (input != null)
            {
                if (!selected.HasActiveCloth)
                    return new ClothStatus { Ok = false, Character = selected.ChaControl.name,
                        Message = "active flare skirt Cloth not found" };
                if (input.PinnedTop.HasValue) tuning.PinnedTop = input.PinnedTop.Value;
                if (input.HemDistance.HasValue) tuning.HemDistance = input.HemDistance.Value;
                if (input.Damping.HasValue) tuning.Damping = input.Damping.Value;
                if (input.Friction.HasValue) tuning.Friction = input.Friction.Value;
                if (input.Stretch.HasValue) tuning.Stretch = input.Stretch.Value;
                if (input.Bend.HasValue) tuning.Bend = input.Bend.Value;
                if (input.SolverHz.HasValue) tuning.SolverHz = input.SolverHz.Value;
                if (input.ExtraDown.HasValue) tuning.ExtraDown = input.ExtraDown.Value;
                if (input.WorldVelocity.HasValue) tuning.WorldVelocity = input.WorldVelocity.Value;
                if (input.WorldAcceleration.HasValue) tuning.WorldAcceleration = input.WorldAcceleration.Value;
                if (input.Gravity.HasValue) tuning.Gravity = input.Gravity.Value;
                if (input.UseTethers.HasValue) tuning.UseTethers = input.UseTethers.Value;
                selected.SetClothTuning(tuning);
            }
            return new ClothStatus
            {
                Ok = true, Character = selected.ChaControl.name,
                ActiveCloth = selected.HasActiveCloth, Colliders = selected.Records.Count,
                SimulatedVertices = selected.ClothVertexCount,
                MeanVertexMotionPerSample = selected.ClothVertexMotion,
                Diagnostics = MapClothDiagnostics(selected.GetClothDiagnostics()),
                Message = reset ? "reset" : input == null ? "ok" : "applied",
                Parameters = new ClothParameters
                {
                    PinnedTop = tuning.PinnedTop, HemDistance = tuning.HemDistance,
                    Damping = tuning.Damping, Friction = tuning.Friction,
                    Stretch = tuning.Stretch, Bend = tuning.Bend,
                    SolverHz = tuning.SolverHz, ExtraDown = tuning.ExtraDown,
                    WorldVelocity = tuning.WorldVelocity, WorldAcceleration = tuning.WorldAcceleration,
                    Gravity = tuning.Gravity, UseTethers = tuning.UseTethers
                }
            };
        }

        private static ClothDiagnosticsView MapClothDiagnostics(ClothDiagnostics d)
        {
            if (d == null) return null;
            return new ClothDiagnosticsView
            {
                SourceVertices = d.SourceVertexCount,
                SimulatedVertices = d.SimulatedVertexCount,
                CoefficientCount = d.CoefficientCount,
                FixedCoefficients = d.FixedCoefficientCount,
                FreeCoefficients = d.FreeCoefficientCount,
                MaxAppliedDistance = d.MaxAppliedDistance,
                MeanAppliedDistance = d.MeanAppliedDistance,
                MeanVertexMotionPerSample = d.MeanVertexMotionPerSample,
                MeanOffsetFromCreation = d.MeanVertexOffsetFromCreation,
                MaxOffsetFromCreation = d.MaxVertexOffsetFromCreation,
                AnchorMotionPerSample = d.AnchorMotionPerSample,
                SourceWorldHeight = d.SourceWorldHeight,
                CloneWorldHeight = d.CloneWorldHeight,
                SourceWorldScaleY = d.SourceWorldScaleY,
                CloneWorldScaleY = d.CloneWorldScaleY,
                SampleIntervalSeconds = d.SampleIntervalSeconds,
                ClothEnabled = d.ClothEnabled,
                UseGravity = d.UseGravity,
                UseTethers = d.UseTethers,
                ExternalAccelerationY = d.ExternalAccelerationY
            };
        }
    }
}
