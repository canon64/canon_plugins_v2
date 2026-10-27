using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        // VaM baked every room mesh into large static batches. Their renderer bounds
        // describe the entire combined mesh, and moving their transforms does not
        // move the visible geometry. Split each renderer's own submeshes into a
        // movable mesh while preserving the original materials and lightmap UVs.
        private void ConvertStaticHotelGeometry(GameObject root)
        {
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer == null || !renderer.isPartOfStaticBatch) continue;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                Mesh source = filter != null ? filter.sharedMesh : null;
                if (source == null || !source.isReadable)
                    throw new InvalidOperationException("VaM static mesh is not readable: " + renderer.name);
                Material[] materials = renderer.sharedMaterials;
                int firstSubmesh = renderer.subMeshStartIndex;
                if (materials.Length == 0 || firstSubmesh < 0 ||
                    firstSubmesh + materials.Length > source.subMeshCount)
                    throw new InvalidOperationException("VaM static submesh range invalid: " + renderer.name);

                var used = new List<int>();
                var remap = new Dictionary<int, int>();
                var triangles = new int[materials.Length][];
                for (int sub = 0; sub < materials.Length; sub++)
                {
                    int[] original = source.GetIndices(firstSubmesh + sub);
                    int[] compact = new int[original.Length];
                    for (int i = 0; i < original.Length; i++)
                    {
                        int mapped;
                        if (!remap.TryGetValue(original[i], out mapped))
                        {
                            mapped = used.Count;
                            remap.Add(original[i], mapped);
                            used.Add(original[i]);
                        }
                        compact[i] = mapped;
                    }
                    triangles[sub] = compact;
                }
                if (used.Count == 0) continue;

                Mesh mesh = new Mesh { name = renderer.name + " movable" };
                if (used.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.vertices = SubsetHotelVertexData(source.vertices, used);
                Vector3[] normals = SubsetHotelVertexData(source.normals, used);
                if (normals != null) mesh.normals = normals;
                Vector4[] tangents = SubsetHotelVertexData(source.tangents, used);
                if (tangents != null) mesh.tangents = tangents;
                Vector2[] uv = SubsetHotelVertexData(source.uv, used);
                if (uv != null) mesh.uv = uv;
                Vector2[] uv2 = SubsetHotelVertexData(source.uv2, used);
                if (uv2 != null) mesh.uv2 = uv2;
                Color32[] colors = SubsetHotelVertexData(source.colors32, used);
                if (colors != null) mesh.colors32 = colors;
                mesh.subMeshCount = materials.Length;
                for (int sub = 0; sub < triangles.Length; sub++)
                    mesh.SetTriangles(triangles[sub], sub, false);
                mesh.RecalculateBounds();
                _vamHotelMovableMeshes.Add(mesh);

                GameObject movable = new GameObject(renderer.name + " (movable)");
                movable.layer = renderer.gameObject.layer;
                movable.transform.SetParent(root.transform, false);
                movable.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer display = movable.AddComponent<MeshRenderer>();
                display.sharedMaterials = materials;
                display.lightmapIndex = renderer.lightmapIndex;
                display.lightmapScaleOffset = renderer.lightmapScaleOffset;
                display.shadowCastingMode = renderer.shadowCastingMode;
                display.receiveShadows = renderer.receiveShadows;
                display.lightProbeUsage = renderer.lightProbeUsage;
                display.reflectionProbeUsage = renderer.reflectionProbeUsage;
                display.enabled = renderer.enabled;
                renderer.enabled = false;
                mesh.UploadMeshData(true);
            }
        }

        private static T[] SubsetHotelVertexData<T>(T[] source, List<int> used)
        {
            if (source == null || source.Length == 0) return null;
            T[] result = new T[used.Count];
            for (int i = 0; i < used.Count; i++)
            {
                if (used[i] < 0 || used[i] >= source.Length) return null;
                result[i] = source[used[i]];
            }
            return result;
        }
    }
}
