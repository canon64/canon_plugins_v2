using System;
using UnityEditor;
using UnityEngine;

public static class FlareSkirtSmoke
{
    public static void Run()
    {
        var bundle = AssetBundle.LoadFromFile(@"F:\kks\abdata\chara\co_bot_00.unity3d");
        if (bundle == null) throw new Exception("AssetBundle load failed");
        var prefab = bundle.LoadAsset<GameObject>("p_o_bot_skirt05");
        if (prefab == null) throw new Exception("Flare skirt A prefab missing");
        var root = UnityEngine.Object.Instantiate(prefab);
        int tested = 0;
        foreach (var source in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (source.sharedMesh == null || !source.sharedMesh.isReadable) continue;
            if (source.sharedMesh.name == "o_bot_skirt05_a" && source.sharedMesh.vertexCount == 847)
            {
                var names = new string[source.bones.Length];
                for (int i = 0; i < names.Length; i++)
                    names[i] = source.bones[i] == null ? "null" : source.bones[i].name;
                Debug.Log("FLARE_BONES count=" + names.Length + " bindposes=" +
                          source.sharedMesh.bindposes.Length + " names=" + string.Join(",", names));
                var weights = source.sharedMesh.boneWeights;
                Debug.Log("FLARE_WAIST firstBone=" + weights[0].boneIndex0 +
                          " firstName=" + names[weights[0].boneIndex0] +
                          " rootBone=" + (source.rootBone == null ? "null" : source.rootBone.name));
            }
            var vertices = source.sharedMesh.vertices;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            foreach (var vertex in vertices)
            {
                minY = Mathf.Min(minY, vertex.y);
                maxY = Mathf.Max(maxY, vertex.y);
            }
            var clone = new GameObject("smoke-clone");
            clone.transform.SetParent(source.transform.parent, false);
            var smr = clone.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = source.sharedMesh;
            smr.bones = source.bones;
            smr.rootBone = source.rootBone;
            smr.sharedMaterials = source.sharedMaterials;
            var cloth = clone.AddComponent<Cloth>();
            var coeffs = new ClothSkinningCoefficient[vertices.Length];
            int pinned = 0;
            float cutoff = maxY - (maxY - minY) * 0.12f;
            for (int i = 0; i < vertices.Length; i++)
            {
                bool fixedVertex = vertices[i].y >= cutoff;
                if (fixedVertex) pinned++;
                coeffs[i].maxDistance = fixedVertex ? 0 : Mathf.Max(0.5f, (maxY - minY) * 2f);
            }
            cloth.coefficients = coeffs;
            var applied = cloth.coefficients;
            int free = 0;
            float appliedMax = 0f;
            foreach (var coefficient in applied)
            {
                if (coefficient.maxDistance > 0.001f) free++;
                appliedMax = Mathf.Max(appliedMax, coefficient.maxDistance);
            }
            if (applied.Length != vertices.Length || pinned == 0 || pinned == vertices.Length ||
                free == 0 || appliedMax < 0.5f)
                throw new Exception("Invalid cloth coefficients");
            Debug.Log("FLARE_SMOKE_OK mesh=" + source.sharedMesh.name +
                      " sourceVerts=" + vertices.Length + " clothVerts=" + cloth.vertices.Length +
                      " coefficients=" + applied.Length + " pinned=" + pinned +
                      " free=" + free + " maxDistance=" + appliedMax);
            tested++;
        }
        if (tested == 0) throw new Exception("No readable skirt meshes");
        Debug.Log("FLARE_SMOKE_PASS renderers=" + tested);
        EditorApplication.Exit(0);
    }
}
