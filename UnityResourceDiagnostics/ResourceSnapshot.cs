using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityResourceDiagnostics
{
    internal static class ResourceSnapshot
    {
        public static Snapshot Capture()
        {
            var snapshot = new Snapshot();
            CountObjects(Resources.FindObjectsOfTypeAll<GameObject>(), snapshot.GameObjectNames, out snapshot.GameObjects);
            CountObjects(Resources.FindObjectsOfTypeAll<Material>(), snapshot.MaterialNames, out snapshot.Materials);
            CountObjects(Resources.FindObjectsOfTypeAll<Texture2D>(), snapshot.Texture2DNames, out snapshot.Texture2Ds);
            CountObjects(Resources.FindObjectsOfTypeAll<RenderTexture>(), snapshot.RenderTextureNames, out snapshot.RenderTextures);
            CaptureMeshes(snapshot);
            CountObjects(Resources.FindObjectsOfTypeAll<AudioClip>(), snapshot.AudioClipNames, out snapshot.AudioClips);
            return snapshot;
        }

        private static void CaptureMeshes(Snapshot snapshot)
        {
            Mesh[] meshes = Resources.FindObjectsOfTypeAll<Mesh>();
            CountObjects(meshes, snapshot.MeshNames, out snapshot.Meshes);

            if (meshes != null)
            {
                for (int i = 0; i < meshes.Length; i++)
                {
                    Mesh mesh = meshes[i];
                    if (mesh == null)
                        continue;

                    int id = mesh.GetInstanceID();
                    if (!snapshot.MeshInfos.ContainsKey(id))
                        snapshot.MeshInfos.Add(id, MeshInfo.From(mesh));
                }
            }

            CaptureMeshOwners(snapshot);
        }

        private static void CaptureMeshOwners(Snapshot snapshot)
        {
            MeshFilter[] filters = Resources.FindObjectsOfTypeAll<MeshFilter>();
            if (filters != null)
            {
                for (int i = 0; i < filters.Length; i++)
                    AddMeshOwner(snapshot, filters[i] != null ? filters[i].sharedMesh : null, "MeshFilter", filters[i]);
            }

            SkinnedMeshRenderer[] renderers = Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>();
            if (renderers != null)
            {
                for (int i = 0; i < renderers.Length; i++)
                    AddMeshOwner(snapshot, renderers[i] != null ? renderers[i].sharedMesh : null, "SkinnedMeshRenderer", renderers[i]);
            }

            MeshCollider[] colliders = Resources.FindObjectsOfTypeAll<MeshCollider>();
            if (colliders != null)
            {
                for (int i = 0; i < colliders.Length; i++)
                    AddMeshOwner(snapshot, colliders[i] != null ? colliders[i].sharedMesh : null, "MeshCollider", colliders[i]);
            }
        }

        private static void AddMeshOwner(Snapshot snapshot, Mesh mesh, string kind, Component owner)
        {
            if (mesh == null || owner == null)
                return;

            int meshId = mesh.GetInstanceID();
            if (!snapshot.MeshOwners.TryGetValue(meshId, out List<MeshOwnerInfo> owners))
            {
                owners = new List<MeshOwnerInfo>(1);
                snapshot.MeshOwners.Add(meshId, owners);
            }

            owners.Add(new MeshOwnerInfo
            {
                Kind = kind,
                OwnerId = owner.GetInstanceID(),
                Path = PathUtil.GetPath(owner.gameObject)
            });
        }

        private static void CountObjects<T>(T[] objects, Dictionary<string, int> names, out int count)
            where T : UnityEngine.Object
        {
            count = 0;
            if (objects == null)
                return;

            count = objects.Length;
            for (int i = 0; i < objects.Length; i++)
            {
                T obj = objects[i];
                string name = obj != null ? obj.name : "(null)";
                name = TextUtil.NormalizeName(name);
                if (!names.TryGetValue(name, out int current))
                    current = 0;
                names[name] = current + 1;
            }
        }
    }

    internal sealed class Snapshot
    {
        public int GameObjects;
        public int Materials;
        public int Texture2Ds;
        public int RenderTextures;
        public int Meshes;
        public int AudioClips;

        public readonly Dictionary<string, int> GameObjectNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> MaterialNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> Texture2DNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> RenderTextureNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> MeshNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> AudioClipNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<int, MeshInfo> MeshInfos = new Dictionary<int, MeshInfo>();
        public readonly Dictionary<int, List<MeshOwnerInfo>> MeshOwners = new Dictionary<int, List<MeshOwnerInfo>>();
    }

    internal sealed class SnapshotDelta
    {
        public int GameObjects;
        public int Materials;
        public int Texture2Ds;
        public int RenderTextures;
        public int Meshes;
        public int AudioClips;
        public readonly List<int> NewMeshIds = new List<int>();

        public static SnapshotDelta From(Snapshot previous, Snapshot current)
        {
            if (previous == null || current == null)
                return new SnapshotDelta();

            var delta = new SnapshotDelta
            {
                GameObjects = current.GameObjects - previous.GameObjects,
                Materials = current.Materials - previous.Materials,
                Texture2Ds = current.Texture2Ds - previous.Texture2Ds,
                RenderTextures = current.RenderTextures - previous.RenderTextures,
                Meshes = current.Meshes - previous.Meshes,
                AudioClips = current.AudioClips - previous.AudioClips
            };

            foreach (int id in current.MeshInfos.Keys)
            {
                if (!previous.MeshInfos.ContainsKey(id))
                    delta.NewMeshIds.Add(id);
            }

            return delta;
        }
    }

    internal struct MeshInfo
    {
        public int Id;
        public string Name;
        public int VertexCount;
        public int TriangleCount;
        public Bounds Bounds;

        public static MeshInfo From(Mesh mesh)
        {
            return new MeshInfo
            {
                Id = mesh.GetInstanceID(),
                Name = mesh.name,
                VertexCount = SafeVertexCount(mesh),
                TriangleCount = SafeTriangleCount(mesh),
                Bounds = mesh.bounds
            };
        }

        private static int SafeVertexCount(Mesh mesh)
        {
            try
            {
                return mesh != null ? mesh.vertexCount : 0;
            }
            catch
            {
                return -1;
            }
        }

        private static int SafeTriangleCount(Mesh mesh)
        {
            if (mesh == null)
                return 0;

            try
            {
                int total = 0;
                int subMeshCount = mesh.subMeshCount;
                for (int i = 0; i < subMeshCount; i++)
                    total += (int)(mesh.GetIndexCount(i) / 3);
                return total;
            }
            catch
            {
                try
                {
                    int[] triangles = mesh.triangles;
                    return triangles != null ? triangles.Length / 3 : 0;
                }
                catch
                {
                    return -1;
                }
            }
        }
    }

    internal struct MeshOwnerInfo
    {
        public string Kind;
        public int OwnerId;
        public string Path;
    }
}
