using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private const int TorusMajorSegments = 96;
        private const int TorusMinorSegments = 12;

        /// <summary>
        /// 回転オブジェクトの「素の位相」(連続性オフセットを含まない) を計算する。
        /// 同期切替や速度変更の前後で「同じ瞬間にいくつだったか」を比較するために使う。
        /// </summary>
        private float ComputeRawPhaseTurns(ManagedObjectData data, double tSec)
        {
            if (data == null) return 0f;
            if (data.animSync)
            {
                return GetAnimNormalizedPhase() * data.animSpeedMultiplier;
            }
            return (float)(data.orbitSpeedHz * tSec);
        }

        /// <summary>
        /// アニメ同期/倍率/速度を変更する直前に呼ぶ。
        /// 変更後の式と前の式の差分を phaseContinuityOffsetTurns に取り込んで、
        /// 「式が変わっても見た目の位置が連続する」ようにする。
        /// </summary>
        internal void RebasePhaseContinuity(ManagedObjectData data, System.Action mutate)
        {
            if (data == null || !data.isRotationObject) { mutate?.Invoke(); return; }
            double tSec = GetSyncTime();
            float before = ComputeRawPhaseTurns(data, tSec);
            mutate?.Invoke();
            float after = ComputeRawPhaseTurns(data, tSec);
            data.phaseContinuityOffsetTurns += before - after;
        }

        /// <summary>
        /// HScene のメス側アニメーターから現在アニメの正規化時間を取得（累積値）。
        /// 整数部 = ループ回数、小数部 = 現在ループ内の進行率。
        /// 0..1 に折りたたまない（折りたたむとループ毎に位相が -1 ジャンプして
        /// 子オブジェクトが「定期的に元の位置に戻る」現象になる）。
        /// アニメーターが取れないときは 0 を返す。
        /// </summary>
        private float GetAnimNormalizedPhase()
        {
            try
            {
                var anim = _runtime != null ? _runtime.MainFemaleAnimBody : null;
                if (anim == null) return 0f;
                var st = anim.GetCurrentAnimatorStateInfo(0);
                return st.normalizedTime;
            }
            catch
            {
                return 0f;
            }
        }

        // 縞模様テクスチャ（プロセス内で1枚共有、各オブジェクトのマテリアルから参照する）
        private static Texture2D _sharedStripedTexture;

        private static Texture2D GetStripedTexture()
        {
            if (_sharedStripedTexture != null) return _sharedStripedTexture;

            const int width = 256;
            const int height = 4;
            const int stripeCount = 16; // 主半径まわり16本

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Point;
            tex.hideFlags = HideFlags.HideAndDontSave;

            var pixels = new Color32[width * height];
            for (int x = 0; x < width; x++)
            {
                bool dark = (x * stripeCount / width) % 2 == 0;
                Color32 c = dark
                    ? new Color32(40, 40, 40, 255)
                    : new Color32(230, 230, 230, 255);
                for (int y = 0; y < height; y++)
                {
                    pixels[y * width + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            _sharedStripedTexture = tex;
            return tex;
        }

        private static Material BuildStripedMaterial()
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            var mat = new Material(shader)
            {
                name = "RotationObjectStriped",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = GetStripedTexture(),
            };
            return mat;
        }

        /// <summary>
        /// 回転オブジェクトを新規作成。ルート直下にトーラス（ドーナツ）として生成する。
        /// </summary>
        private void CreateRotationObject()
        {
            RecordUndoSnapshot("create rotation object");

            string id = System.Guid.NewGuid().ToString("N");
            string name = BuildUniqueObjectName("RotationObject");

            Vector3 localPosition = Vector3.zero;
            if (TryGetCameraFrontWorldPos(_settings.DefaultSpawnDistance, out Vector3 spawnWorld))
            {
                Transform rootTf = (_runtime != null && _runtime.Root != null) ? _runtime.Root.transform : null;
                localPosition = WorldToManagedLocalPosition(spawnWorld, rootTf);
            }

            var data = new ManagedObjectData
            {
                id = id,
                name = name,
                primitive = PrimitiveType.Sphere, // 使わない（メッシュは差し替える）
                parentKind = ParentKindRoot,
                parentRefId = null,
                localPosition = localPosition,
                localEulerAngles = Vector3.zero,
                localScale = Vector3.one,
                isRotationObject = true,
                orbitRadiusX = _settings.DefaultOrbitRadiusX,
                orbitRadiusZ = _settings.DefaultOrbitRadiusZ,
                tubeRadius = _settings.DefaultTubeRadius,
                orbitSpeedHz = _settings.DefaultOrbitSpeedHz,
                visible = true,
            };

            _objects.Add(data);

            ManagedObjectData hookData = CreateRotationObjectHookData(data);
            _objects.Add(hookData);

            _selectedId = id;
            _selectionDirty = true;

            RebuildAllRuntimeObjects();
            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();
            LogInfo("created rotation object id=" + id + " hook id=" + hookData.id);
        }

        private ManagedObjectData CreateRotationObjectHookData(ManagedObjectData rotationObject)
        {
            string hookId = System.Guid.NewGuid().ToString("N");
            string parentId = rotationObject != null ? rotationObject.id : null;
            string parentName = rotationObject != null && !string.IsNullOrEmpty(rotationObject.name)
                ? rotationObject.name
                : "RotationObject";

            // フック点: 軌道上を公転する先端マーカー（IK追従先として使う）。
            // 子の localPosition=zero で軌道phase位置に乗る。
            return new ManagedObjectData
            {
                id = hookId,
                name = parentName + "_Hook",
                primitive = PrimitiveType.Sphere,
                parentKind = ParentKindManaged,
                parentRefId = parentId,
                parentId = parentId,
                localPosition = Vector3.zero,
                localEulerAngles = Vector3.zero,
                localScale = new Vector3(0.04f, 0.04f, 0.04f),
                orbitPhaseTurns = 0f,
                visible = true,
                orientToTangent = true,
            };
        }

        private bool EnsureRotationObjectHooks()
        {
            if (_objects == null || _objects.Count == 0) return false;

            bool added = false;
            int originalCount = _objects.Count;
            for (int i = 0; i < originalCount; i++)
            {
                ManagedObjectData data = _objects[i];
                if (data == null || !data.isRotationObject) continue;
                if (HasRotationObjectHook(data)) continue;

                ManagedObjectData hookData = CreateRotationObjectHookData(data);
                _objects.Add(hookData);
                added = true;
                LogInfo("added missing rotation hook id=" + hookData.id + " parentId=" + data.id);
            }

            return added;
        }

        private bool HasRotationObjectHook(ManagedObjectData rotationObject)
        {
            if (rotationObject == null || string.IsNullOrEmpty(rotationObject.id)) return false;

            string expectedName = (rotationObject.name ?? string.Empty) + "_Hook";
            for (int i = 0; i < _objects.Count; i++)
            {
                ManagedObjectData child = _objects[i];
                if (child == null) continue;
                if (!string.Equals(child.parentKind, ParentKindManaged, StringComparison.Ordinal)) continue;
                if (!string.Equals(child.parentRefId, rotationObject.id, StringComparison.Ordinal)) continue;
                if (string.Equals(child.name, expectedName, StringComparison.Ordinal)) return true;
                if (!string.IsNullOrEmpty(child.name) && child.name.EndsWith("_Hook", StringComparison.Ordinal)) return true;
            }

            return false;
        }

        /// <summary>
        /// 既存ランタイムオブジェクトをトーラス用にセットアップ（メッシュ差し替え・コライダー削除）。
        /// BuildRuntimeObject から呼ばれる。
        /// </summary>
        private void ApplyRotationObjectVisualSetup(RuntimeObjectRef runtimeRef)
        {
            if (runtimeRef == null || runtimeRef.Visual == null || runtimeRef.Data == null) return;
            if (!runtimeRef.Data.isRotationObject) return;

            GameObject visual = runtimeRef.Visual;

            // Visual に付いてるコライダーは不要
            var col = visual.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);

            var mf = visual.GetComponent<MeshFilter>();
            if (mf == null) mf = visual.AddComponent<MeshFilter>();

            var mr = visual.GetComponent<MeshRenderer>();
            if (mr == null) mr = visual.AddComponent<MeshRenderer>();
            runtimeRef.CachedRendererVisual = visual;
            runtimeRef.CachedVisualRenderer = mr;
            if (MotionTraceEnabled)
            {
                var probe = visual.GetComponent<MotionRingRenderProbe>() ?? visual.AddComponent<MotionRingRenderProbe>();
                probe.Owner = this;
                probe.ObjectId = runtimeRef.Data.id;
            }

            // 専用マテリアル（縞テクスチャ付き）を1個ずつ持たせる。UVスクロールは個別。
            if (runtimeRef.GeneratedMaterial == null)
            {
                runtimeRef.GeneratedMaterial = BuildStripedMaterial();
            }
            mr.sharedMaterial = runtimeRef.GeneratedMaterial;

            RebuildTorusMeshIfNeeded(runtimeRef);
            ApplyVisibility(runtimeRef);
        }

        private void RebuildTorusMeshIfNeeded(RuntimeObjectRef runtimeRef)
        {
            if (runtimeRef == null || runtimeRef.Data == null || runtimeRef.Visual == null) return;
            ManagedObjectData d = runtimeRef.Data;
            if (!d.isRotationObject) return;

            GetEffectiveOrbitRadii(d, out float rx, out float rz);
            float tube = Mathf.Max(0.0005f, d.tubeRadius);

            if (Mathf.Approximately(rx, runtimeRef.CachedRx)
                && Mathf.Approximately(rz, runtimeRef.CachedRz)
                && Mathf.Approximately(tube, runtimeRef.CachedTube)
                && runtimeRef.GeneratedMesh != null)
            {
                return;
            }

            var mf = runtimeRef.Visual.GetComponent<MeshFilter>();
            if (mf == null) return;

            if (runtimeRef.GeneratedMesh != null)
            {
                Destroy(runtimeRef.GeneratedMesh);
                runtimeRef.GeneratedMesh = null;
            }

            runtimeRef.GeneratedMesh = BuildEllipticalTorusMesh(rx, rz, tube, TorusMajorSegments, TorusMinorSegments);
            mf.sharedMesh = runtimeRef.GeneratedMesh;

            runtimeRef.CachedRx = rx;
            runtimeRef.CachedRz = rz;
            runtimeRef.CachedTube = tube;
        }

        private void ApplyVisibility(RuntimeObjectRef runtimeRef)
        {
            if (runtimeRef == null || runtimeRef.Data == null || runtimeRef.Visual == null) return;
            if (runtimeRef.CachedRendererVisual != runtimeRef.Visual || runtimeRef.CachedVisualRenderer == null)
            {
                runtimeRef.CachedRendererVisual = runtimeRef.Visual;
                runtimeRef.CachedVisualRenderer = runtimeRef.Visual.GetComponent<MeshRenderer>();
            }
            MeshRenderer mr = runtimeRef.CachedVisualRenderer;
            if (mr != null && mr.enabled != runtimeRef.Data.visible)
                mr.enabled = runtimeRef.Data.visible;
        }

        /// <summary>
        /// 楕円トーラスのメッシュを生成する。
        /// 中心線: P(θ) = (Rx cos θ, 0, Rz sin θ)
        /// 各 θ で接線に直交する平面に半径 r の円管を配置する。
        /// </summary>
        private static Mesh BuildEllipticalTorusMesh(float rx, float rz, float tube, int majorSegments, int minorSegments)
        {
            int majorCount = Mathf.Max(8, majorSegments);
            int minorCount = Mathf.Max(4, minorSegments);

            int vertexCount = (majorCount + 1) * (minorCount + 1);
            int triangleCount = majorCount * minorCount * 2;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uv = new Vector2[vertexCount];
            var triangles = new int[triangleCount * 3];

            for (int i = 0; i <= majorCount; i++)
            {
                float u = (float)i / majorCount;
                float theta = u * Mathf.PI * 2f;
                float cosT = Mathf.Cos(theta);
                float sinT = Mathf.Sin(theta);

                Vector3 center = new Vector3(rx * cosT, 0f, rz * sinT);

                // 接線方向（中心線の微分）
                Vector3 tangent = new Vector3(-rx * sinT, 0f, rz * cosT).normalized;
                // 軌道平面の法線（Y軸固定）
                Vector3 up = Vector3.up;
                // 管断面の半径方向（接線と上方向に直交）
                Vector3 radial = Vector3.Cross(tangent, up).normalized;

                for (int j = 0; j <= minorCount; j++)
                {
                    float v = (float)j / minorCount;
                    float phi = v * Mathf.PI * 2f;
                    float cosP = Mathf.Cos(phi);
                    float sinP = Mathf.Sin(phi);

                    Vector3 offset = (radial * cosP + up * sinP) * tube;
                    int idx = i * (minorCount + 1) + j;
                    vertices[idx] = center + offset;
                    normals[idx] = offset.normalized;
                    uv[idx] = new Vector2(u, v);
                }
            }

            int t = 0;
            for (int i = 0; i < majorCount; i++)
            {
                for (int j = 0; j < minorCount; j++)
                {
                    int a = i * (minorCount + 1) + j;
                    int b = a + (minorCount + 1);
                    int c = a + 1;
                    int d = b + 1;

                    triangles[t++] = a; triangles[t++] = b; triangles[t++] = d;
                    triangles[t++] = a; triangles[t++] = d; triangles[t++] = c;
                }
            }

            var mesh = new Mesh
            {
                name = "EllipticalTorus",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = vertices,
                normals = normals,
                uv = uv,
                triangles = triangles,
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 毎フレーム: 回転オブジェクトの子の localPosition を楕円軌道上に強制配置する。
        /// 同時にメッシュパラメータ変化を反映する。
        /// </summary>
        private void TickRotationOrbits()
        {
            if (_runtimeObjects == null || _runtimeObjects.Count == 0) return;

            double tSec = GetSyncTime();
            float vibeSlow = ReadVibeSlowState();

            foreach (KeyValuePair<string, RuntimeObjectRef> kv in _runtimeObjects)
            {
                RuntimeObjectRef rr = kv.Value;
                if (rr == null || rr.Data == null || !rr.Data.isRotationObject) continue;

                // メッシュ更新（パラメータが変わったときだけ実コスト）
                RebuildTorusMeshIfNeeded(rr);
                ApplyVisibility(rr);

                GetEffectiveOrbitRadii(rr.Data, out float rx, out float rz);
                float speed = rr.Data.orbitSpeedHz;
                // 回転オブジェクトの Visual.localScale で見た目のドーナツが伸縮するので、
                // 軌道位置も同じ scale を掛けて見た目に追従させる
                Vector3 visualScale = (rr.Visual != null) ? rr.Visual.transform.localScale : Vector3.one;
                float effRx = rx * visualScale.x;
                float effRz = rz * visualScale.z;

                // 位相源:
                //   アニメ同期ON → animPhase × multiplier + animSyncPhaseShift (連続性オフセット無視、常にアニメ厳密同期)
                //   アニメ同期OFF → speed × t + phaseContinuityOffsetTurns (速度変更時の連続性保持)
                // 子の公転とドーナツUVスクロールはこの同じ値を使う（速度感が一致）
                float phaseSourceTurns;
                if (rr.Data.animSync)
                {
                    phaseSourceTurns = ComputeRawPhaseTurns(rr.Data, tSec)
                                       + rr.Data.animSyncPhaseShift;
                }
                else
                {
                    phaseSourceTurns = ComputeRawPhaseTurns(rr.Data, tSec)
                                       + rr.Data.phaseContinuityOffsetTurns;
                }

                // 縞テクスチャをUVスクロール = ドーナツが「同じ速度で」回って見える
                if (rr.Data.visible && rr.GeneratedMaterial != null)
                {
                    rr.GeneratedMaterial.mainTextureOffset = new Vector2(-phaseSourceTurns, 0f);
                }

                // 直下の子を探して位相に応じて配置
                if (!TryGetManagedChildren(rr.Data.id, out List<ManagedObjectData> children)) continue;
                for (int i = 0; i < children.Count; i++)
                {
                    ManagedObjectData child = children[i];

                    RuntimeObjectRef childRr = FindRuntimeById(child.id);
                    if (childRr == null || childRr.GameObject == null) continue;

                    float phaseTurns = phaseSourceTurns + child.orbitPhaseTurns;
                    float ang = phaseTurns * Mathf.PI * 2f;
                    Vector3 orbitPos = new Vector3(effRx * Mathf.Cos(ang), 0f, effRz * Mathf.Sin(ang));

                    // 子の localPosition は orbit-local frame のオフセットとして扱う:
                    //   X = 軌道半径方向 (外向き+)
                    //   Y = 縦方向
                    //   Z = 軌道接線方向 (先行+)
                    // → X を増やすと中心からの距離が増えて、その距離で円軌道を回る
                    Vector3 outward = orbitPos.sqrMagnitude > 1e-8f ? orbitPos.normalized : new Vector3(1f, 0f, 0f);
                    Vector3 along = Vector3.Cross(outward, Vector3.up).normalized;
                    Vector3 rotatedOffset = outward * child.localPosition.x
                                          + Vector3.up * child.localPosition.y
                                          + along * child.localPosition.z;
                    bool vibeBlend = child.isRotationObject && child.vibeSlowParentEnabled;
                    if (!vibeBlend) _vibeParentWeights.Remove(child.id);
                    float parentWeight = vibeBlend ? UpdateVibeParentWeight(child, vibeSlow) : 1f;
                    Vector3 fullPosition = orbitPos + rotatedOffset;
                    childRr.GameObject.transform.localPosition = vibeBlend
                        ? Vector3.Lerp(child.localPosition, fullPosition, parentWeight)
                        : fullPosition;

                    // 接線方向追従ON: 軌道の進行方向を向かせる。
                    // ユーザーが localEulerAngles で追加回転を入れた場合はその分だけ追加で回す。
                    // 判定は子個別フラグ（子化時に親のデフォルトが引き継がれる）
                    if (child.orientToTangent)
                    {
                        Vector3 tangent = new Vector3(-effRx * Mathf.Sin(ang), 0f, effRz * Mathf.Cos(ang));
                        if (tangent.sqrMagnitude > 1e-6f)
                        {
                            Quaternion orbitRot = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                            Quaternion userRot = Quaternion.Euler(child.localEulerAngles);
                            Quaternion fullRotation = orbitRot * userRot;
                            childRr.GameObject.transform.localRotation = vibeBlend
                                ? Quaternion.Slerp(userRot, fullRotation, parentWeight)
                                : fullRotation;
                        }
                    }
                    else if (vibeBlend || child.vibeInsertionAlignEnabled)
                    {
                        childRr.GameObject.transform.localRotation = Quaternion.Euler(child.localEulerAngles);
                    }
                    // 接線追従OFFのときは触らない（RebuildAllRuntimeObjects で設定された data.localEulerAngles のまま）
                }
            }
        }
    }
}
