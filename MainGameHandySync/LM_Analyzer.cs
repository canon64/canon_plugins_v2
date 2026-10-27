// LoveMachine (Sauceke, GPLv3) からの移植。アルゴリズムは verbatim。
// 元: Game/AnimationAnalyzer.cs + Game/SamplingAnimationAnalyzer.cs
// 変更点: CoroutineHandler/HandleCoroutine → 素の IEnumerator（Plugin が StartCoroutine で駆動）、
//         Logger → SimpleFileLogger、GameAdapter → LmGame。
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MainGameHandySync.LM
{
    internal sealed class LmAnalyzer
    {
        private readonly Dictionary<TrackingKey, Result> resultCache = new Dictionary<TrackingKey, Result>();
        private readonly HashSet<TrackingKey> keysInProgress = new HashSet<TrackingKey>();

        public void Clear()
        {
            resultCache.Clear();
            keysInProgress.Clear();
        }

        public bool IsAnalyzed(TrackingKey key) => resultCache.ContainsKey(key);
        public bool IsAnalyzing(TrackingKey key) => keysInProgress.Contains(key);

        // AnimationAnalyzer.TryGetCurrentStrokeInfo (verbatim)
        public bool TryGetCurrentStrokeInfo(LmGame game, TrackingKey trackingKey, float normalizedTime,
            out StrokeInfo strokeInfo)
        {
            if (!resultCache.TryGetValue(trackingKey, out var result) || result.StrokeDelimiters.Length == 0)
            {
                strokeInfo = default;
                return false;
            }
            var delimiters = result.StrokeDelimiters;
            float animTimeSecs = game.GetAnimationTimeSecs(trackingKey.GirlIndex);
            float wrappedTime = normalizedTime % 1f;
            int delimIndex = delimiters.Length - 1;
            for (int i = 0; i < delimiters.Length; i++)
                if (delimiters[i] <= wrappedTime)
                    delimIndex = i;
            float start = delimiters[delimIndex];
            float end = delimIndex == delimiters.Length - 1
                ? delimiters[0] + 1f
                : delimiters[delimIndex + 1];
            if (wrappedTime < start)
            {
                start -= 1f;
                end -= 1f;
            }
            float normalizedStrokeDuration = end - start;
            strokeInfo = new StrokeInfo
            {
                Amplitude = result.Amplitude,
                DurationSecs = animTimeSecs * normalizedStrokeDuration,
                Completion = Mathf.InverseLerp(start, end, wrappedTime),
                Pattern = result.Patterns[delimIndex]
            };
            return true;
        }

        // SamplingAnimationAnalyzer.AnalyzeAnimation (verbatim, 駆動だけ素のコルーチンに)
        public IEnumerator AnalyzeAnimation(LmGame game, TrackingKey trackingKey)
        {
            if (resultCache.ContainsKey(trackingKey) || keysInProgress.Contains(trackingKey))
                yield break;
            keysInProgress.Add(trackingKey);
            SimpleFileLogger.Info("[LM] New animation playing, starting to analyze.");

            int girlIndex = trackingKey.GirlIndex;
            var penisBases = game.PenisBases;
            var femaleBones = game.GetFemaleBones(girlIndex);
            var trackedFemaleBones = trackingKey.Bone == Bone.Auto
                ? femaleBones
                : new Dictionary<Bone, Transform> { { trackingKey.Bone, femaleBones[trackingKey.Bone] } };
            string pose = trackingKey.Pose;
            yield return game.WaitAfterPoseChange();
            var samples = new List<Sample>();
            game.GetAnimState(girlIndex, out float startTime, out _, out _);
            float currentTime = startTime;
            while (currentTime - 1f < startTime)
            {
                yield return new WaitForEndOfFrame();
                game.GetAnimState(girlIndex, out currentTime, out _, out _);
                var newSamples = trackedFemaleBones
                    .Where(entry => entry.Value != null)
                    .SelectMany(entry => penisBases, (entry, penisBase) => new Sample
                    {
                        Bone = entry.Key,
                        PenisBase = penisBase,
                        Time = currentTime,
                        MalePos = penisBase.position,
                        FemalePos = entry.Value.position,
                        MaleRot = penisBase.rotation,
                        FemaleRot = entry.Value.rotation
                    });
                samples.AddRange(newSamples);
                if (pose != game.GetPose(girlIndex) || currentTime < startTime)
                {
                    SimpleFileLogger.Warn($"[LM] Pose {pose} interrupted; canceling analysis.");
                    keysInProgress.Remove(trackingKey);
                    yield break;
                }
            }
            var keys = trackedFemaleBones
                .Select(entry => { var key = trackingKey; key.Bone = entry.Key; return key; })
                .ToArray();
            var results = samples
                .GroupBy(sample => sample.PenisBase)
                .Select(group => keys.ToDictionary(key => key, key => EvaluateSamples(game, group, key)))
                .ToArray();
            if (results.Length == 0) { keysInProgress.Remove(trackingKey); yield break; }
            var preferredResults = keys.ToDictionary(
                key => key,
                key => results.Maximize(dict => dict[key].Preference)[key]);
            var bestKey = keys.Maximize(key => preferredResults[key].Preference);
            var autoKey = bestKey;
            autoKey.Bone = Bone.Auto;
            preferredResults[autoKey] = preferredResults[bestKey];
            foreach (var kvp in preferredResults)
                resultCache[kvp.Key] = kvp.Value;
            SimpleFileLogger.Info($"[LM] Analysis of pose {pose} completed. " +
                $"{samples.Count / Mathf.Max(1, trackedFemaleBones.Count)} frames inspected.");
            keysInProgress.Remove(trackingKey);
        }

        private Result EvaluateSamples(LmGame game, IEnumerable<Sample> samplesIn, TrackingKey trackingKey)
        {
            var samples = samplesIn.Where(sample => sample.Bone == trackingKey.Bone).ToArray();
            var femaleCenter = samples
                .Select(sample => sample.FemalePos)
                .Aggregate(Vector3.zero, (acc, pos) => acc + pos / samples.Length);
            var maleFarthest = samples
                .Maximize(sample => (sample.MalePos - femaleCenter).sqrMagnitude)
                .MalePos;
            var femaleFarthest = samples
                .Maximize(sample => (sample.FemalePos - maleFarthest).sqrMagnitude)
                .MalePos;
            Vector3 GetRelativePos(Sample sample) =>
                GetRelativePosition(sample, trackingKey.POV, maleFarthest, femaleFarthest);
            var relativePositions = samples.Select(GetRelativePos).ToArray();
            var crest = relativePositions.Maximize(pos => pos.magnitude);
            var trough = relativePositions.Maximize(pos => (pos - crest).magnitude);
            var longestAxis = crest - trough;
            Vector3 GetTrackedAxis(Sample sample) => GetAxis(sample, trackingKey.Axis, longestAxis);
            float GetDistance(Sample sample) =>
                Vector3.Project(GetRelativePos(sample) - trough, GetTrackedAxis(sample)).magnitude;
            float GetTwist(Sample sample) =>
                RotationToTwist(GetRelativeRotation(sample, trackingKey.POV), GetTrackedAxis(sample));
            var nodes = samples
                .Select(sample => new Node
                {
                    Time = sample.Time,
                    Position = trackingKey.MovementType == MovementType.Linear
                        ? GetDistance(sample)
                        : GetTwist(sample)
                })
                .ToArray();
            if (trackingKey.MovementType == MovementType.Rotation)
                nodes = NormalizeAngles(nodes);
            return NodesToResult(game, nodes, trough.magnitude);
        }

        // ===== AnimationAnalyzer の解析→Result 変換 (verbatim) =====

        private Result NodesToResult(LmGame game, Node[] nodes, float proximity)
        {
            float amplitude = nodes.Max(node => node.Position) - nodes.Min(node => node.Position);
            var delimiters = GetStrokeDelimiters(nodes, amplitude * game.MinStrokeLength);
            var patterns = GetPatterns(nodes, delimiters);
            float preference = amplitude == 0
                ? float.NegativeInfinity
                : -Mathf.Pow(proximity, 3f) / amplitude;
            return new Result
            {
                StrokeDelimiters = delimiters,
                Patterns = patterns,
                Amplitude = amplitude,
                Preference = preference
            };
        }

        private static float[] GetStrokeDelimiters(Node[] nodes, float tolerance)
        {
            int edgeIndex = Enumerable.Range(0, nodes.Length).Minimize(i => nodes[i].Position);
            var edge = nodes[edgeIndex];
            var nodesAligned = nodes.Skip(edgeIndex).Concat(nodes.Take(edgeIndex));
            int direction = 1;
            var edges = new List<Node>();
            foreach (var node in nodesAligned)
            {
                float delta = edge.Position - node.Position;
                edge = Math.Sign(delta) == direction ? node : edge;
                if (Mathf.Abs(delta) > tolerance)
                {
                    edges.Add(edge);
                    edge = node;
                    direction *= -1;
                }
            }
            return edges.Where((node, i) => i % 2 == 0)
                .Select(node => node.Time % 1f)
                .OrderBy(time => time)
                .ToArray();
        }

        private static float[][] GetPatterns(Node[] nodes, float[] delimiters)
        {
            int strokeCount = delimiters.Length;
            return Enumerable.Range(0, strokeCount)
                .Select(i => GetPattern(
                    nodes,
                    delimiters[i],
                    i == strokeCount - 1 ? delimiters[0] + 1f : delimiters[i + 1]))
                .ToArray();
        }

        private static float[] GetPattern(Node[] nodes, float start, float end)
        {
            float min = nodes.Min(node => node.Position);
            float max = nodes.Max(node => node.Position);
            return nodes
                .Where(node => (node.Time - start + 1f) % 1f < end - start)
                .OrderBy(node => (node.Time - start + 1f) % 1f)
                .Select(node => Mathf.InverseLerp(min, max, value: node.Position))
                .ToArray();
        }

        // ===== POV/Axis/Twist ヘルパ (verbatim) =====

        private static Vector3 GetAxis(Sample sample, Axis axis, Vector3 longest)
        {
            switch (axis)
            {
                case Axis.Longest: return longest;
                case Axis.X: return sample.MaleRot * Vector3.right;
                case Axis.Y: return sample.MaleRot * Vector3.up;
                case Axis.Z: return sample.MaleRot * Vector3.forward;
            }
            throw new Exception("unreachable");
        }

        private static Vector3 GetRelativePosition(Sample sample, POV pov, Vector3 male, Vector3 female)
        {
            switch (pov)
            {
                case POV.Balanced: return sample.MalePos - sample.FemalePos;
                case POV.Male: return male - sample.FemalePos;
                case POV.Female: return sample.MalePos - female;
            }
            throw new Exception("unreachable");
        }

        private static Quaternion GetRelativeRotation(Sample sample, POV pov)
        {
            switch (pov)
            {
                case POV.Balanced: return sample.MaleRot * Quaternion.Inverse(sample.FemaleRot);
                case POV.Male: return sample.FemaleRot;
                case POV.Female: return sample.MaleRot;
            }
            throw new Exception("unreachable");
        }

        private static float RotationToTwist(Quaternion rotation, Vector3 axis)
        {
            (rotation * Quaternion.FromToRotation(rotation * axis, axis))
                .ToAngleAxis(out float angle, out _);
            return angle;
        }

        private static Node[] NormalizeAngles(Node[] nodes)
        {
            var normalized = new List<Node> { nodes.First() };
            foreach (var node in nodes.Skip(1))
            {
                float lastAngle = normalized.Last().Position;
                float angle = node.Position - lastAngle;
                angle = (angle + 360f + 180f) % 360f - 180f;
                normalized.Add(new Node { Time = node.Time, Position = lastAngle + angle });
            }
            return normalized.ToArray();
        }

        private struct Result
        {
            public float[] StrokeDelimiters { get; set; }
            public float[][] Patterns { get; set; }
            public float Amplitude { get; set; }
            public float Preference { get; set; }
        }

        private struct Sample
        {
            public Bone Bone { get; set; }
            public Transform PenisBase { get; set; }
            public float Time { get; set; }
            public Vector3 MalePos { get; set; }
            public Vector3 FemalePos { get; set; }
            public Quaternion MaleRot { get; set; }
            public Quaternion FemaleRot { get; set; }
        }
    }
}
