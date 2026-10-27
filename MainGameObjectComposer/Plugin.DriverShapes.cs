using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Mesh builders
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// ピストンレール（Z軸方向）のメッシュを生成する。
        /// Z=-amplitude〜+amplitude のロッド + 両端に端板。
        /// Visual.localRotation で pistonAxis に向ける。
        /// </summary>
        private static Mesh BuildPistonRailMesh(float amplitude, float rodRadius)
        {
            const int segs = 12;
            float amp     = Mathf.Max(0.001f, amplitude);
            float rad     = Mathf.Max(0.0005f, rodRadius);
            float capRad  = rad * 2.5f;
            float capHalf = rad * 0.8f;

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs   = new List<Vector2>();
            var tris  = new List<int>();

            // 外向き法線の円柱側面
            void AddSideCyl(float z0, float z1, float r)
            {
                int b = verts.Count;
                for (int i = 0; i <= segs; i++)
                {
                    float a = (float)i / segs * Mathf.PI * 2f;
                    float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
                    Vector3 n = new Vector3(cx, cy, 0f);
                    float u = (float)i / segs;
                    verts.Add(new Vector3(cx * r, cy * r, z0)); norms.Add(n); uvs.Add(new Vector2(u, 0f));
                    verts.Add(new Vector3(cx * r, cy * r, z1)); norms.Add(n); uvs.Add(new Vector2(u, 1f));
                }
                for (int i = 0; i < segs; i++)
                {
                    int v0 = b + i * 2;
                    int v1 = b + (i + 1) * 2;
                    // CCW from outside → outward-facing
                    tris.Add(v0);   tris.Add(v1 + 1); tris.Add(v0 + 1);
                    tris.Add(v0);   tris.Add(v1);     tris.Add(v1 + 1);
                }
            }

            // 塗り円盤: faceSign=+1 で +Z 向き、-1 で -Z 向き
            void AddDisk(float z, float r, float faceSign)
            {
                int b = verts.Count;
                Vector3 n = new Vector3(0f, 0f, faceSign);
                verts.Add(new Vector3(0f, 0f, z)); norms.Add(n); uvs.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i < segs; i++)
                {
                    float a = (float)i / segs * Mathf.PI * 2f;
                    float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
                    verts.Add(new Vector3(cx * r, cy * r, z)); norms.Add(n);
                    uvs.Add(new Vector2(cx * 0.5f + 0.5f, cy * 0.5f + 0.5f));
                }
                for (int i = 0; i < segs; i++)
                {
                    int va = b + 1 + i;
                    int vb = b + 1 + (i + 1) % segs;
                    if (faceSign > 0f) { tris.Add(b); tris.Add(va); tris.Add(vb); }
                    else               { tris.Add(b); tris.Add(vb); tris.Add(va); }
                }
            }

            // ロッド本体
            AddSideCyl(-amp, +amp, rad);

            // 下端板
            AddSideCyl(-amp - capHalf, -amp + capHalf, capRad);
            AddDisk(-amp - capHalf, capRad, -1f);
            AddDisk(-amp + capHalf, capRad, +1f);

            // 上端板
            AddSideCyl(+amp - capHalf, +amp + capHalf, capRad);
            AddDisk(+amp - capHalf, capRad, -1f);
            AddDisk(+amp + capHalf, capRad, +1f);

            var mesh = new Mesh { name = "PistonRail", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// アングル扇形メッシュを生成する（XZ 平面、法線 ±Y）。
        /// -amplitudeDeg〜+amplitudeDeg の扇。Visual.localRotation で angleAxis に向ける。
        /// </summary>
        private static Mesh BuildAngleFanMesh(float amplitudeDeg, float radius)
        {
            float amp  = Mathf.Clamp(amplitudeDeg, 1f, 180f);
            float r    = Mathf.Max(0.01f, radius);
            int   segs = Mathf.Max(4, Mathf.CeilToInt(amp * 2f / 5f));

            // 表裏 2 面分
            int faceVerts  = 1 + (segs + 1);
            int totalVerts = faceVerts * 2;

            var verts = new Vector3[totalVerts];
            var norms = new Vector3[totalVerts];
            var uvs   = new Vector2[totalVerts];
            var tris  = new List<int>(segs * 6);

            for (int face = 0; face < 2; face++)
            {
                int b  = face * faceVerts;
                float ny = face == 0 ? 1f : -1f;
                Vector3 n = new Vector3(0f, ny, 0f);

                verts[b] = Vector3.zero;
                norms[b] = n;
                uvs[b]   = new Vector2(0.5f, 0.5f);

                for (int i = 0; i <= segs; i++)
                {
                    float t        = (float)i / segs;
                    float angleDeg = Mathf.Lerp(-amp, +amp, t);
                    float angleRad = angleDeg * Mathf.Deg2Rad;
                    float x = Mathf.Sin(angleRad) * r;
                    float z = Mathf.Cos(angleRad) * r;
                    verts[b + 1 + i] = new Vector3(x, 0f, z);
                    norms[b + 1 + i] = n;
                    uvs[b + 1 + i]   = new Vector2(x / r * 0.5f + 0.5f, z / r * 0.5f + 0.5f);
                }

                for (int i = 0; i < segs; i++)
                {
                    int c   = b;
                    int va  = b + 1 + i;
                    int vbb = b + 1 + i + 1;
                    if (face == 0) { tris.Add(c); tris.Add(va);  tris.Add(vbb); }
                    else           { tris.Add(c); tris.Add(vbb); tris.Add(va);  }
                }
            }

            var mesh = new Mesh { name = "AngleFan", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices  = verts;
            mesh.normals   = norms;
            mesh.uv        = uvs;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Visual setup
        // ─────────────────────────────────────────────────────────────────────

        private void ApplyPistonObjectVisualSetup(RuntimeObjectRef rr)
        {
            if (rr == null || rr.Visual == null || rr.Data == null) return;
            if (!rr.Data.isPistonObject) return;

            GameObject visual = rr.Visual;
            var col = visual.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            var mf = visual.GetComponent<MeshFilter>();
            if (mf == null) mf = visual.AddComponent<MeshFilter>();

            var mr = visual.GetComponent<MeshRenderer>();
            if (mr == null) mr = visual.AddComponent<MeshRenderer>();
            rr.CachedRendererVisual = visual;
            rr.CachedVisualRenderer = mr;

            if (rr.GeneratedMaterial == null)
                rr.GeneratedMaterial = BuildStripedMaterial();
            mr.sharedMaterial = rr.GeneratedMaterial;

            RebuildPistonMeshIfNeeded(rr);
            ApplyVisibility(rr);
        }

        private void ApplyAngleObjectVisualSetup(RuntimeObjectRef rr)
        {
            if (rr == null || rr.Visual == null || rr.Data == null) return;
            if (!rr.Data.isAngleObject) return;

            GameObject visual = rr.Visual;
            var col = visual.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            var mf = visual.GetComponent<MeshFilter>();
            if (mf == null) mf = visual.AddComponent<MeshFilter>();

            var mr = visual.GetComponent<MeshRenderer>();
            if (mr == null) mr = visual.AddComponent<MeshRenderer>();
            rr.CachedRendererVisual = visual;
            rr.CachedVisualRenderer = mr;

            if (rr.GeneratedMaterial == null)
                rr.GeneratedMaterial = BuildStripedMaterial();
            mr.sharedMaterial = rr.GeneratedMaterial;

            RebuildAngleMeshIfNeeded(rr);
            ApplyVisibility(rr);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Cached rebuild
        // ─────────────────────────────────────────────────────────────────────

        private void RebuildPistonMeshIfNeeded(RuntimeObjectRef rr)
        {
            if (rr == null || rr.Data == null || rr.Visual == null) return;
            ManagedObjectData d = rr.Data;
            if (!d.isPistonObject) return;

            // 軸回転は常に更新（安価）
            Vector3 axis = d.pistonAxis.sqrMagnitude > 1e-6f ? d.pistonAxis.normalized : Vector3.forward;
            rr.Visual.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, axis);

            float amp = Mathf.Max(0.001f, d.pistonAmplitude);
            float rod = Mathf.Max(0.0005f, d.pistonRodRadius);

            if (Mathf.Approximately(amp, rr.CachedRx)
                && Mathf.Approximately(rod, rr.CachedRz)
                && rr.GeneratedMesh != null)
                return;

            var mf = rr.Visual.GetComponent<MeshFilter>();
            if (mf == null) return;

            if (rr.GeneratedMesh != null) { Destroy(rr.GeneratedMesh); rr.GeneratedMesh = null; }
            rr.GeneratedMesh = BuildPistonRailMesh(amp, rod);
            mf.sharedMesh    = rr.GeneratedMesh;
            rr.CachedRx = amp;
            rr.CachedRz = rod;
        }

        private void RebuildAngleMeshIfNeeded(RuntimeObjectRef rr)
        {
            if (rr == null || rr.Data == null || rr.Visual == null) return;
            ManagedObjectData d = rr.Data;
            if (!d.isAngleObject) return;

            Vector3 axis = d.angleAxis.sqrMagnitude > 1e-6f ? d.angleAxis.normalized : Vector3.up;
            rr.Visual.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);

            float ampDeg = Mathf.Clamp(d.angleAmplitudeDeg, 1f, 180f);
            float fanR   = Mathf.Max(0.01f, d.angleFanRadius);

            if (Mathf.Approximately(ampDeg, rr.CachedRx)
                && Mathf.Approximately(fanR, rr.CachedRz)
                && rr.GeneratedMesh != null)
                return;

            var mf = rr.Visual.GetComponent<MeshFilter>();
            if (mf == null) return;

            if (rr.GeneratedMesh != null) { Destroy(rr.GeneratedMesh); rr.GeneratedMesh = null; }
            rr.GeneratedMesh = BuildAngleFanMesh(ampDeg, fanR);
            mf.sharedMesh    = rr.GeneratedMesh;
            rr.CachedRx = ampDeg;
            rr.CachedRz = fanR;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Create
        // ─────────────────────────────────────────────────────────────────────

        private void CreatePistonObject()
        {
            RecordUndoSnapshot("create piston object");
            string id   = System.Guid.NewGuid().ToString("N");
            string name = BuildUniqueObjectName("Piston");

            Vector3 localPosition = Vector3.zero;
            if (TryGetCameraFrontWorldPos(_settings.DefaultSpawnDistance, out Vector3 spawnWorld))
            {
                Transform rootTf = (_runtime != null && _runtime.Root != null) ? _runtime.Root.transform : null;
                localPosition = WorldToManagedLocalPosition(spawnWorld, rootTf);
            }

            var data = new ManagedObjectData
            {
                id             = id,
                name           = name,
                primitive      = PrimitiveType.Sphere,
                parentKind     = ParentKindRoot,
                parentRefId    = null,
                localPosition  = localPosition,
                localEulerAngles = Vector3.zero,
                localScale     = Vector3.one,
                isPistonObject = true,
                pistonAmplitude = _settings.DefaultPistonAmplitude,
                pistonAxis      = _settings.DefaultPistonAxis,
                pistonSpeedHz   = _settings.DefaultPistonSpeedHz,
                pistonRodRadius = _settings.DefaultPistonRodRadius,
                visible = true,
            };

            _objects.Add(data);

            // フック点: レール上を往復する先端マーカー（IK追従先として使う）
            string hookId = System.Guid.NewGuid().ToString("N");
            var hookData = new ManagedObjectData
            {
                id               = hookId,
                name             = name + "_Hook",
                primitive        = PrimitiveType.Sphere,
                parentKind       = ParentKindManaged,
                parentRefId      = id,
                parentId         = id,
                localPosition    = Vector3.zero,
                localEulerAngles = Vector3.zero,
                localScale       = new Vector3(0.04f, 0.04f, 0.04f),
                visible          = true,
            };
            _objects.Add(hookData);

            _selectedId     = id;
            _selectionDirty = true;
            RebuildAllRuntimeObjects();
            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();
            LogInfo("created piston object id=" + id + " hook id=" + hookId);
        }

        private void CreateAngleObject()
        {
            RecordUndoSnapshot("create angle object");
            string id   = System.Guid.NewGuid().ToString("N");
            string name = BuildUniqueObjectName("Angle");

            Vector3 localPosition = Vector3.zero;
            if (TryGetCameraFrontWorldPos(_settings.DefaultSpawnDistance, out Vector3 spawnWorld))
            {
                Transform rootTf = (_runtime != null && _runtime.Root != null) ? _runtime.Root.transform : null;
                localPosition = WorldToManagedLocalPosition(spawnWorld, rootTf);
            }

            var data = new ManagedObjectData
            {
                id              = id,
                name            = name,
                primitive       = PrimitiveType.Sphere,
                parentKind      = ParentKindRoot,
                parentRefId     = null,
                localPosition   = localPosition,
                localEulerAngles = Vector3.zero,
                localScale      = Vector3.one,
                isAngleObject    = true,
                angleAmplitudeDeg = _settings.DefaultAngleAmplitudeDeg,
                angleAxis         = _settings.DefaultAngleAxis,
                angleSpeedHz      = _settings.DefaultAngleSpeedHz,
                angleFanRadius    = _settings.DefaultAngleFanRadius,
                visible = true,
            };

            _objects.Add(data);

            // フック点: 扇の先端でswingする先端マーカー（IK追従先として使う）
            string hookId = System.Guid.NewGuid().ToString("N");
            Vector3 axisN  = _settings.DefaultAngleAxis.sqrMagnitude > 1e-6f
                                ? _settings.DefaultAngleAxis.normalized
                                : Vector3.up;
            // 扇メッシュは angle=0 (ローカル+Z) を正面に開く。Visual は
            // localRotation = FromToRotation(up, axisN) で軸へ向くので、同じ回転を
            // +Z に掛けた向きが扇の正面。フックもそこに置く（扇とフックの向きを一致させる）。
            Vector3 forward = Quaternion.FromToRotation(Vector3.up, axisN) * Vector3.forward;
            var hookData = new ManagedObjectData
            {
                id               = hookId,
                name             = name + "_Hook",
                primitive        = PrimitiveType.Sphere,
                parentKind       = ParentKindManaged,
                parentRefId      = id,
                parentId         = id,
                localPosition    = forward * _settings.DefaultAngleFanRadius,
                localEulerAngles = Vector3.zero,
                localScale       = new Vector3(0.04f, 0.04f, 0.04f),
                visible          = true,
            };
            _objects.Add(hookData);

            _selectedId     = id;
            _selectionDirty = true;
            RebuildAllRuntimeObjects();
            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();
            LogInfo("created angle object id=" + id + " hook id=" + hookId);
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Tick
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 毎フレーム: ピストンドライバの子の localPosition を直線往復させる。
        /// 同時にメッシュ・軸回転を反映する。
        /// </summary>
        private void TickLinearDrivers()
        {
            if (_runtimeObjects == null || _runtimeObjects.Count == 0) return;

            double tSec = GetSyncTime();

            foreach (KeyValuePair<string, RuntimeObjectRef> kv in _runtimeObjects)
            {
                RuntimeObjectRef rr = kv.Value;
                if (rr == null || rr.Data == null || !rr.Data.isPistonObject) continue;

                RebuildPistonMeshIfNeeded(rr);
                ApplyVisibility(rr);

                ManagedObjectData d     = rr.Data;
                Vector3           axisN = d.pistonAxis.sqrMagnitude > 1e-6f
                                            ? d.pistonAxis.normalized
                                            : Vector3.forward;

                if (!TryGetManagedChildren(d.id, out List<ManagedObjectData> children)) continue;
                for (int i = 0; i < children.Count; i++)
                {
                    ManagedObjectData child = children[i];

                    RuntimeObjectRef childRr = FindRuntimeById(child.id);
                    if (childRr == null || childRr.GameObject == null) continue;

                    float sinPhase = (float)(2.0 * System.Math.PI
                        * (d.pistonSpeedHz * tSec + d.pistonPhaseTurns + child.orbitPhaseTurns));
                    float slide = Mathf.Sin(sinPhase) * d.pistonAmplitude;

                    if (d.pistonLocalSpace)
                    {
                        childRr.GameObject.transform.localPosition = child.localPosition + axisN * slide;
                    }
                    else
                    {
                        Transform parentTf = childRr.GameObject.transform.parent;
                        Vector3 localAxis  = parentTf != null
                            ? parentTf.InverseTransformDirection(axisN)
                            : axisN;
                        childRr.GameObject.transform.localPosition = child.localPosition + localAxis * slide;
                    }
                }
            }
        }

        /// <summary>
        /// 毎フレーム: アングルドライバの子の localPosition/Rotation を扇内でswingさせる。
        /// 同時にメッシュ・軸回転を反映する。
        /// </summary>
        private void TickAngleDrivers()
        {
            if (_runtimeObjects == null || _runtimeObjects.Count == 0) return;

            double tSec = GetSyncTime();

            foreach (KeyValuePair<string, RuntimeObjectRef> kv in _runtimeObjects)
            {
                RuntimeObjectRef rr = kv.Value;
                if (rr == null || rr.Data == null || !rr.Data.isAngleObject) continue;

                RebuildAngleMeshIfNeeded(rr);
                ApplyVisibility(rr);

                ManagedObjectData d     = rr.Data;
                Vector3           axisN = d.angleAxis.sqrMagnitude > 1e-6f
                                            ? d.angleAxis.normalized
                                            : Vector3.up;

                if (!TryGetManagedChildren(d.id, out List<ManagedObjectData> children)) continue;
                for (int i = 0; i < children.Count; i++)
                {
                    ManagedObjectData child = children[i];

                    RuntimeObjectRef childRr = FindRuntimeById(child.id);
                    if (childRr == null || childRr.GameObject == null) continue;

                    float sinPhase = (float)(2.0 * System.Math.PI
                        * (d.angleSpeedHz * tSec + d.anglePhaseTurns + child.orbitPhaseTurns));
                    float deg = Mathf.Sin(sinPhase) * d.angleAmplitudeDeg;

                    Quaternion swing;
                    if (d.angleLocalSpace)
                    {
                        swing = Quaternion.AngleAxis(deg, axisN);
                    }
                    else
                    {
                        Transform driverTf = rr.GameObject.transform;
                        Vector3 localAxis  = driverTf.InverseTransformDirection(axisN);
                        swing = Quaternion.AngleAxis(deg,
                            localAxis.sqrMagnitude > 1e-6f ? localAxis.normalized : axisN);
                    }

                    childRr.GameObject.transform.localPosition = swing * child.localPosition;
                    childRr.GameObject.transform.localRotation = swing * Quaternion.Euler(child.localEulerAngles);
                }
            }
        }
    }
}
