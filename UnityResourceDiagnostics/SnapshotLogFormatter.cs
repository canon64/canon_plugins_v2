using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace UnityResourceDiagnostics
{
    internal static class SnapshotLogFormatter
    {
        private static readonly string[] SuspectNameTokens =
        {
            "VideoMat_",
            "VideoRT_",
            "VideoSource_",
            "__BlankMapVideoRoom",
            "__VideoRoomAudioSource",
            "SubCameraOverlayMat",
            "ImageSlideshow",
            "RotationObjectStriped",
            "EllipticalTorus",
            "PistonRail",
            "AngleFan",
            "__MainGameObjectComposerRoot",
            "__ObjComposerGizmoProxy_",
            "MainGameSubtitleEventBridge",
            "SimpleAfterimage",
            "SubCamera",
            "VideoSurface_"
        };

        public static StringBuilder Build(
            string reason,
            Snapshot snapshot,
            SnapshotDelta delta,
            bool verbose,
            int topNameLimit,
            int meshSampleLimit)
        {
            var sb = new StringBuilder(8192);
            sb.AppendLine();
            sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] snapshot reason={reason} frame={Time.frameCount} t={Time.unscaledTime:F1}");
            AppendTypeLine(sb, "GameObject", snapshot.GameObjects, delta.GameObjects);
            AppendTypeLine(sb, "Material", snapshot.Materials, delta.Materials);
            AppendTypeLine(sb, "Texture2D", snapshot.Texture2Ds, delta.Texture2Ds);
            AppendTypeLine(sb, "RenderTexture", snapshot.RenderTextures, delta.RenderTextures);
            AppendTypeLine(sb, "Mesh", snapshot.Meshes, delta.Meshes);
            AppendTypeLine(sb, "AudioClip", snapshot.AudioClips, delta.AudioClips);

            AppendSuspectLines(sb, snapshot);
            AppendMeshDiagnostics(sb, snapshot, delta, meshSampleLimit);

            if (verbose)
            {
                AppendTopNames(sb, "GameObject", snapshot.GameObjectNames, topNameLimit);
                AppendTopNames(sb, "Material", snapshot.MaterialNames, topNameLimit);
                AppendTopNames(sb, "Texture2D", snapshot.Texture2DNames, topNameLimit);
                AppendTopNames(sb, "RenderTexture", snapshot.RenderTextureNames, topNameLimit);
                AppendTopNames(sb, "Mesh", snapshot.MeshNames, topNameLimit);
                AppendTopNames(sb, "AudioClip", snapshot.AudioClipNames, topNameLimit);
            }

            return sb;
        }

        private static void AppendTypeLine(StringBuilder sb, string label, int count, int delta)
        {
            sb.Append(label)
                .Append(" total=")
                .Append(count)
                .Append(" delta=")
                .Append(delta >= 0 ? "+" : string.Empty)
                .Append(delta)
                .AppendLine();
        }

        private static void AppendSuspectLines(StringBuilder sb, Snapshot snapshot)
        {
            sb.AppendLine("suspects:");
            for (int i = 0; i < SuspectNameTokens.Length; i++)
            {
                string token = SuspectNameTokens[i];
                int go = CountMatching(snapshot.GameObjectNames, token);
                int mat = CountMatching(snapshot.MaterialNames, token);
                int tex = CountMatching(snapshot.Texture2DNames, token);
                int rt = CountMatching(snapshot.RenderTextureNames, token);
                int mesh = CountMatching(snapshot.MeshNames, token);
                int audio = CountMatching(snapshot.AudioClipNames, token);
                int total = go + mat + tex + rt + mesh + audio;
                if (total == 0)
                    continue;

                sb.Append("  token=")
                    .Append(token)
                    .Append(" total=")
                    .Append(total)
                    .Append(" go=")
                    .Append(go)
                    .Append(" mat=")
                    .Append(mat)
                    .Append(" tex2d=")
                    .Append(tex)
                    .Append(" rt=")
                    .Append(rt)
                    .Append(" mesh=")
                    .Append(mesh)
                    .Append(" audio=")
                    .Append(audio)
                    .AppendLine();
            }
        }

        private static int CountMatching(Dictionary<string, int> names, string token)
        {
            int total = 0;
            foreach (KeyValuePair<string, int> kv in names)
            {
                if (kv.Key.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    total += kv.Value;
            }

            return total;
        }

        private static void AppendTopNames(StringBuilder sb, string label, Dictionary<string, int> names, int limit)
        {
            if (limit <= 0)
                return;

            var entries = new List<KeyValuePair<string, int>>(names);
            entries.Sort((a, b) =>
            {
                int cmp = b.Value.CompareTo(a.Value);
                return cmp != 0 ? cmp : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });

            sb.Append("top ").Append(label).AppendLine(":");
            int count = Mathf.Min(limit, entries.Count);
            for (int i = 0; i < count; i++)
            {
                sb.Append("  ")
                    .Append(entries[i].Value)
                    .Append(" x ")
                    .Append(entries[i].Key)
                    .AppendLine();
            }
        }

        private static void AppendMeshDiagnostics(StringBuilder sb, Snapshot snapshot, SnapshotDelta delta, int sampleLimit)
        {
            var newMeshes = new List<MeshInfo>();
            for (int i = 0; i < delta.NewMeshIds.Count; i++)
            {
                if (snapshot.MeshInfos.TryGetValue(delta.NewMeshIds[i], out MeshInfo info))
                    newMeshes.Add(info);
            }

            CountMeshOwnerKinds(snapshot, out int meshFilterCount, out int skinnedCount, out int colliderCount);

            var orphanMeshes = new List<MeshInfo>();
            foreach (KeyValuePair<int, MeshInfo> kv in snapshot.MeshInfos)
            {
                if (!snapshot.MeshOwners.ContainsKey(kv.Key))
                    orphanMeshes.Add(kv.Value);
            }

            sb.AppendLine("mesh diagnostics:");
            sb.Append("  newMeshes=").Append(newMeshes.Count).AppendLine();
            AppendMeshTopNames(sb, "  new mesh top names", newMeshes, 10);
            AppendMeshSamples(sb, "  new mesh samples", newMeshes, snapshot, sampleLimit);

            sb.Append("  owner summary MeshFilter=")
                .Append(meshFilterCount)
                .Append(" SkinnedMeshRenderer=")
                .Append(skinnedCount)
                .Append(" MeshCollider=")
                .Append(colliderCount)
                .Append(" orphan=")
                .Append(orphanMeshes.Count)
                .AppendLine();

            AppendMeshTopNames(sb, "  orphan top names", orphanMeshes, 10);
            AppendMeshSamples(sb, "  orphan samples", orphanMeshes, snapshot, sampleLimit);
        }

        private static void CountMeshOwnerKinds(Snapshot snapshot, out int meshFilterCount, out int skinnedCount, out int colliderCount)
        {
            meshFilterCount = 0;
            skinnedCount = 0;
            colliderCount = 0;

            foreach (KeyValuePair<int, List<MeshOwnerInfo>> kv in snapshot.MeshOwners)
            {
                bool hasFilter = false;
                bool hasSkinned = false;
                bool hasCollider = false;
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    string kind = kv.Value[i].Kind;
                    hasFilter |= kind == "MeshFilter";
                    hasSkinned |= kind == "SkinnedMeshRenderer";
                    hasCollider |= kind == "MeshCollider";
                }

                if (hasFilter)
                    meshFilterCount++;
                if (hasSkinned)
                    skinnedCount++;
                if (hasCollider)
                    colliderCount++;
            }
        }

        private static void AppendMeshTopNames(StringBuilder sb, string label, List<MeshInfo> meshes, int limit)
        {
            if (meshes.Count == 0 || limit <= 0)
                return;

            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < meshes.Count; i++)
            {
                string name = TextUtil.NormalizeName(meshes[i].Name);
                if (!counts.TryGetValue(name, out int current))
                    current = 0;
                counts[name] = current + 1;
            }

            sb.AppendLine(label + ":");
            AppendTopNames(sb, "mesh", counts, limit);
        }

        private static void AppendMeshSamples(StringBuilder sb, string label, List<MeshInfo> meshes, Snapshot snapshot, int limit)
        {
            if (meshes.Count == 0 || limit <= 0)
                return;

            meshes.Sort((a, b) =>
            {
                int cmp = b.VertexCount.CompareTo(a.VertexCount);
                return cmp != 0 ? cmp : a.Id.CompareTo(b.Id);
            });

            int count = Mathf.Min(limit, meshes.Count);
            sb.AppendLine(label + ":");
            for (int i = 0; i < count; i++)
            {
                MeshInfo info = meshes[i];
                sb.Append("    id=")
                    .Append(info.Id)
                    .Append(" name=")
                    .Append(TextUtil.NormalizeName(info.Name))
                    .Append(" verts=")
                    .Append(info.VertexCount)
                    .Append(" tris=")
                    .Append(info.TriangleCount)
                    .Append(" bounds=")
                    .Append(TextUtil.FormatBounds(info.Bounds));

                if (snapshot.MeshOwners.TryGetValue(info.Id, out List<MeshOwnerInfo> owners) && owners.Count > 0)
                {
                    MeshOwnerInfo owner = owners[0];
                    sb.Append(" ownerKind=")
                        .Append(owner.Kind)
                        .Append(" ownerId=")
                        .Append(owner.OwnerId)
                        .Append(" owner=")
                        .Append(owner.Path);
                    if (owners.Count > 1)
                        sb.Append(" ownerCount=").Append(owners.Count);
                }
                else
                {
                    sb.Append(" owner=(none)");
                }

                sb.AppendLine();
            }
        }
    }
}
