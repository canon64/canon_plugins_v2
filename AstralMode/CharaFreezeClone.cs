using System.Collections.Generic;
using UnityEngine;

namespace AstralMode
{
    internal static class CharaFreezeClone
    {
        private const string CloneRootPrefix = "AstralMode_FrozenClone_";

        internal sealed class FrozenClone
        {
            public GameObject Root;
            public Vector3 WorldPos;
            public Quaternion WorldRot;
        }

        internal static FrozenClone CreateFrom(ChaControl cha)
        {
            return CreateFrom(cha, Vector3.zero);
        }

        internal static FrozenClone CreateFrom(ChaControl cha, Vector3 worldOffset)
        {
            if (cha == null) return null;
            if (Plugin.DiagnosticsEnabled) FrozenDisplayDiagnostics.Source(cha);

            try
            {
                Vector3 sourcePos = cha.transform.position;
                Quaternion sourceRot = cha.transform.rotation;
                Vector3 clonePos = sourcePos + worldOffset;

                GameObject root = new GameObject(CloneRootPrefix + cha.name);
                var materialOwner = root.AddComponent<FrozenMaterialOwner>();
                Object.DontDestroyOnLoad(root);
                root.hideFlags = HideFlags.DontSave;
                root.transform.position = clonePos;
                root.transform.rotation = sourceRot;
                root.transform.localScale = Vector3.one;

                Renderer[] smrs = cha.GetComponentsInChildren<Renderer>(includeInactive: false);
                SimpleFileLogger.Info($"FrozenClone begin for {cha.name}: chaPos={sourcePos}, clonePos={clonePos}, offset={worldOffset}, chaRot={sourceRot.eulerAngles}, chaLayer={cha.gameObject.layer}, smrCount={smrs.Length}");

                int baked = 0;
                int skippedInactive = 0;
                int skippedNoMesh = 0;
                int loggedParts = 0;
                Bounds totalBounds = new Bounds(sourcePos, Vector3.zero);
                bool hasBounds = false;

                foreach (var smr in smrs)
                {
                    if (smr == null) continue;
                    var skinned = smr as SkinnedMeshRenderer;
                    var rigid = smr as MeshRenderer;
                    if (skinned == null && rigid == null) continue;
                    // Disabled static meshes include hidden accessory variants.
                    if (rigid != null && !rigid.enabled) continue;
                    var sourceFilter = rigid != null ? rigid.GetComponent<MeshFilter>() : null;
                    Mesh sourceMesh = skinned != null ? skinned.sharedMesh : sourceFilter != null ? sourceFilter.sharedMesh : null;
                    if (sourceMesh == null)
                    {
                        skippedNoMesh++;
                        continue;
                    }
                    if (!smr.gameObject.activeInHierarchy)
                    {
                        skippedInactive++;
                        continue;
                    }

                    Vector3 smrWorldPos = smr.transform.position;
                    Quaternion smrWorldRot = smr.transform.rotation;
                    Vector3 smrLocalScale = skinned != null ? smr.transform.localScale : smr.transform.lossyScale;
                    int sourceLayer = smr.gameObject.layer;

                    Mesh bakedMesh = null;
                    try
                    {
                        if (skinned != null) {
                            bakedMesh = new Mesh { hideFlags = HideFlags.DontSave };
                            skinned.BakeMesh(bakedMesh);
                        } else {
                            // Own the copy: clone cleanup must never destroy the live mesh.
                            bakedMesh = Object.Instantiate(sourceMesh);
                            bakedMesh.hideFlags = HideFlags.DontSave;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        SimpleFileLogger.Warn($"BakeMesh failed for {smr.name}: {ex.Message}");
                        Object.Destroy(bakedMesh);
                        continue;
                    }

                    GameObject part = new GameObject(smr.name);
                    part.hideFlags = HideFlags.DontSave;
                    part.layer = sourceLayer;

                    // 先にワールド姿勢を元SMRへ一致させてから、root配下へ worldPositionStays=true で入れる。
                    // rootは元キャラ座標なので、偽物全体の代表位置もゲームマップ原点ではなく元座標になる。
                    part.transform.position = smrWorldPos;
                    part.transform.rotation = smrWorldRot;
                    part.transform.localScale = smrLocalScale;
                    part.transform.SetParent(root.transform, worldPositionStays: true);
                    if (worldOffset != Vector3.zero)
                        part.transform.position += worldOffset;

                    MeshFilter mf = part.AddComponent<MeshFilter>();
                    mf.sharedMesh = bakedMesh;

                    MeshRenderer mr = part.AddComponent<MeshRenderer>();
                    var sourceMaterials = smr.sharedMaterials;
                    var frozenMaterials = new Material[sourceMaterials.Length];
                    for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++) {
                        if (sourceMaterials[materialIndex] == null) continue;
                        var frozen = new Material(sourceMaterials[materialIndex]);
                        frozen.hideFlags = HideFlags.DontSave;
                        materialOwner.Materials.Add(frozen);
                        frozenMaterials[materialIndex] = frozen;
                    }
                    mr.sharedMaterials = frozenMaterials;
                    var properties = new MaterialPropertyBlock();
                    smr.GetPropertyBlock(properties);
                    mr.SetPropertyBlock(properties);
                    for (int materialIndex = 0; materialIndex < sourceMaterials.Length; materialIndex++) {
                        properties.Clear();
                        smr.GetPropertyBlock(properties, materialIndex);
                        if (!properties.isEmpty) mr.SetPropertyBlock(properties, materialIndex);
                    }
                    mr.shadowCastingMode = smr.shadowCastingMode;
                    mr.receiveShadows = smr.receiveShadows;
                    mr.enabled = true;

                    Bounds rb = mr.bounds;
                    if (!hasBounds)
                    {
                        totalBounds = rb;
                        hasBounds = true;
                    }
                    else
                    {
                        totalBounds.Encapsulate(rb);
                    }

                    if (loggedParts < 8)
                    {
                        int matCount = mr.sharedMaterials != null ? mr.sharedMaterials.Length : 0;
                        string shaderName = (matCount > 0 && mr.sharedMaterials[0] != null && mr.sharedMaterials[0].shader != null)
                            ? mr.sharedMaterials[0].shader.name
                            : "NULL";
                        SimpleFileLogger.Info($"FrozenClone part[{loggedParts}] {smr.name}: layer={part.layer}, smrEnabled={smr.enabled}, partActive={part.activeInHierarchy}, mrEnabled={mr.enabled}, mats={matCount}, shader={shaderName}, worldPos={part.transform.position}, localPos={part.transform.localPosition}, lossyScale={part.transform.lossyScale}, boundsCenter={rb.center}, boundsSize={rb.size}");
                        loggedParts++;
                    }

                    baked++;
                }

                if (baked == 0)
                {
                    SimpleFileLogger.Warn($"Clone created but no meshes baked for {cha.name}");
                }

                int rendererCount = root.GetComponentsInChildren<MeshRenderer>(includeInactive: true).Length;
                int filterCount = root.GetComponentsInChildren<MeshFilter>(includeInactive: true).Length;
                SimpleFileLogger.Info($"FrozenClone summary for {cha.name}: baked={baked}, renderers={rendererCount}, filters={filterCount}, skippedInactive={skippedInactive}, skippedNoMesh={skippedNoMesh}, rootActive={root.activeInHierarchy}, rootPos={root.transform.position}, rootRot={root.transform.rotation.eulerAngles}, totalBoundsCenter={(hasBounds ? totalBounds.center.ToString() : "none")}, totalBoundsSize={(hasBounds ? totalBounds.size.ToString() : "none")}");
                SimpleFileLogger.Info($"FrozenClone created for {cha.name}: {baked} meshes baked at {sourcePos}, clonePos={clonePos}, offset={worldOffset}");

                return new FrozenClone
                {
                    Root = root,
                    WorldPos = sourcePos,
                    WorldRot = sourceRot
                };
            }
            catch (System.Exception ex)
            {
                SimpleFileLogger.Warn($"CreateFrozenClone failed for {cha?.name}: {ex.Message}");
                return null;
            }
        }

        internal static void DestroyClone(FrozenClone clone)
        {
            if (clone == null || clone.Root == null) return;
            DestroyCloneRoot(clone.Root, clone.WorldPos);
            clone.Root = null;
        }

        private static void DestroyCloneRoot(GameObject root, Vector3 worldPos)
        {
            if (root == null) return;

            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(includeInactive: true);
            MeshFilter[] mfs = root.GetComponentsInChildren<MeshFilter>(includeInactive: true);
            SimpleFileLogger.Info($"DestroyFrozenClone: root={root.name}, rootActive={root.activeInHierarchy}, rootPos={root.transform.position}, renderers={renderers.Length}, filters={mfs.Length}, worldPos={worldPos}");

            foreach (var mf in mfs)
            {
                if (mf != null && mf.sharedMesh != null)
                {
                    Mesh m = mf.sharedMesh;
                    mf.sharedMesh = null;
                    Object.Destroy(m);
                }
            }

            Object.Destroy(root);
        }

        internal static void DestroyAll(List<FrozenClone> clones)
        {
            if (clones == null) return;
            foreach (var c in clones) DestroyClone(c);
            clones.Clear();
        }

        internal static int DestroyLeakedCloneRoots()
        {
            int destroyed = 0;
            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject go in all)
            {
                if (go == null) continue;
                if (go.transform.parent != null) continue;
                if (!go.name.StartsWith(CloneRootPrefix)) continue;

                DestroyCloneRoot(go, go.transform.position);
                destroyed++;
            }

            if (destroyed > 0)
                SimpleFileLogger.Warn($"Leaked FrozenClone roots destroyed: count={destroyed}");
            else
                SimpleFileLogger.Info("Leaked FrozenClone roots check: none");

            return destroyed;
        }
    }
}
