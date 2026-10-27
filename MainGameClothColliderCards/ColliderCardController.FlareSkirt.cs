using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameClothColliderCards
{
    public sealed class ClothDiagnostics
    {
        public int SourceVertexCount;
        public int SimulatedVertexCount;
        public int CoefficientCount;
        public int FixedCoefficientCount;
        public int FreeCoefficientCount;
        public float MaxAppliedDistance;
        public float MeanAppliedDistance;
        public float MeanVertexMotionPerSample;
        public float MeanVertexOffsetFromCreation;
        public float MaxVertexOffsetFromCreation;
        public float AnchorMotionPerSample;
        public float SourceWorldHeight;
        public float CloneWorldHeight;
        public float SourceWorldScaleY;
        public float CloneWorldScaleY;
        public float SampleIntervalSeconds;
        public bool ClothEnabled;
        public bool UseGravity;
        public bool UseTethers;
        public float ExternalAccelerationY;
    }

    public sealed partial class ColliderCardController
    {
        private const int FlareSkirtCloneId = 91025008;
        private const string FlareSkirtCloneName = "フレアスカートA（Cloth試作）";
        private GameObject _flareRoot;
        private readonly List<GameObject> _flareClones = new List<GameObject>();
        private readonly List<Mesh> _flareGeneratedMeshes = new List<Mesh>();
        private readonly Dictionary<SkinnedMeshRenderer, bool> _flareOriginals = new Dictionary<SkinnedMeshRenderer, bool>();
        private readonly Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer> _flareRenderers = new Dictionary<SkinnedMeshRenderer, SkinnedMeshRenderer>();
        private readonly Dictionary<Behaviour, bool> _flareDynamics = new Dictionary<Behaviour, bool>();
        private readonly Dictionary<Cloth, Vector3[]> _clothRestVertices = new Dictionary<Cloth, Vector3[]>();
        private int _flareClothesState = -1;
        private float _nextFlareCheck;
        private string _lastFlareSkip;
        private Vector3[] _previousClothVertices;
        private float _clothVertexMotion;
        private float _clothVertexOffset;
        private float _clothVertexMaxOffset;
        private Vector3 _previousAnchorPosition;
        private bool _hasPreviousAnchorPosition;
        private float _anchorMotion;
        private float _previousSampleTime;
        private float _sampleInterval;
        public float ClothVertexMotion => _clothVertexMotion;
        public int ClothVertexCount => _previousClothVertices == null ? 0 : _previousClothVertices.Length;

        public bool ResetClothSimulation()
        {
            RefreshFlareSkirt(force: true);
            return _flareClones.Count > 0;
        }

        public ClothDiagnostics GetClothDiagnostics()
        {
            if (_flareClones.Count == 0 || _flareClones[0] == null) return null;
            var clone = _flareClones[0];
            var cloth = clone.GetComponent<Cloth>();
            var renderer = clone.GetComponent<SkinnedMeshRenderer>();
            if (cloth == null || renderer == null) return null;
            SkinnedMeshRenderer source = null;
            foreach (var pair in _flareRenderers)
                if (pair.Value == renderer) { source = pair.Key; break; }
            var coefficients = cloth.coefficients;
            var result = new ClothDiagnostics
            {
                SourceVertexCount = renderer.sharedMesh == null ? 0 : renderer.sharedMesh.vertexCount,
                SimulatedVertexCount = ClothVertexCount,
                CoefficientCount = coefficients.Length,
                MeanVertexMotionPerSample = _clothVertexMotion,
                MeanVertexOffsetFromCreation = _clothVertexOffset,
                MaxVertexOffsetFromCreation = _clothVertexMaxOffset,
                AnchorMotionPerSample = _anchorMotion,
                SourceWorldHeight = source == null ? 0f : source.bounds.size.y,
                CloneWorldHeight = renderer.bounds.size.y,
                SourceWorldScaleY = source == null ? 0f : source.transform.lossyScale.y,
                CloneWorldScaleY = renderer.transform.lossyScale.y,
                SampleIntervalSeconds = _sampleInterval,
                ClothEnabled = cloth.enabled && cloth.gameObject.activeInHierarchy,
                UseGravity = cloth.useGravity,
                UseTethers = cloth.useTethers,
                ExternalAccelerationY = cloth.externalAcceleration.y
            };
            double distanceSum = 0;
            foreach (var coefficient in coefficients)
            {
                if (coefficient.maxDistance <= 0.001f) result.FixedCoefficientCount++;
                else result.FreeCoefficientCount++;
                result.MaxAppliedDistance = Mathf.Max(result.MaxAppliedDistance, coefficient.maxDistance);
                distanceSum += coefficient.maxDistance;
            }
            result.MeanAppliedDistance = coefficients.Length == 0 ? 0f : (float)(distanceSum / coefficients.Length);
            return result;
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextFlareCheck) return;
            _nextFlareCheck = Time.unscaledTime + 0.5f;
            bool restored = false;
            for (int i = 0; i < _records.Count && i < _objects.Count; i++)
            {
                if (_objects[i] != null) continue;
                _objects[i] = Create(_records[i]);
                restored |= _objects[i] != null;
            }
            RefreshFlareSkirt(force: restored);
            SampleClothMotion();
            foreach (var pair in _flareRenderers)
            {
                var source = pair.Key;
                var renderer = pair.Value;
                if (source == null || renderer == null) continue;
                var originalMaterials = source.sharedMaterials;
                var cloneMaterials = renderer.sharedMaterials;
                bool materialsChanged = originalMaterials.Length != cloneMaterials.Length;
                if (!materialsChanged)
                    for (int i = 0; i < originalMaterials.Length; i++)
                        if (originalMaterials[i] != cloneMaterials[i]) { materialsChanged = true; break; }
                if (materialsChanged) renderer.sharedMaterials = originalMaterials;
                var mesh = source.sharedMesh;
                if (mesh != null && renderer.sharedMesh != null)
                    for (int i = 0; i < Mathf.Min(mesh.blendShapeCount, renderer.sharedMesh.blendShapeCount); i++)
                        renderer.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));
            }
        }

        private void SampleClothMotion()
        {
            if (_flareClones.Count == 0 || _flareClones[0] == null) return;
            var cloth = _flareClones[0].GetComponent<Cloth>();
            if (cloth == null) return;
            var vertices = cloth.vertices;
            if (vertices == null) return;
            float now = Time.unscaledTime;
            _sampleInterval = _previousSampleTime > 0f ? now - _previousSampleTime : 0f;
            _previousSampleTime = now;
            if (_previousClothVertices != null && _previousClothVertices.Length == vertices.Length && vertices.Length > 0)
            {
                double sum = 0;
                for (int i = 0; i < vertices.Length; i++)
                    sum += Vector3.Distance(vertices[i], _previousClothVertices[i]);
                _clothVertexMotion = (float)(sum / vertices.Length);
            }
            if (_clothRestVertices.TryGetValue(cloth, out var rest) && rest != null && rest.Length == vertices.Length && vertices.Length > 0)
            {
                double sum = 0;
                float max = 0f;
                for (int i = 0; i < vertices.Length; i++)
                {
                    float distance = Vector3.Distance(vertices[i], rest[i]);
                    sum += distance;
                    max = Mathf.Max(max, distance);
                }
                _clothVertexOffset = (float)(sum / vertices.Length);
                _clothVertexMaxOffset = max;
            }
            var renderer = _flareClones[0].GetComponent<SkinnedMeshRenderer>();
            var anchor = renderer == null ? null : renderer.rootBone;
            if (anchor != null)
            {
                Vector3 position = anchor.position;
                _anchorMotion = _hasPreviousAnchorPosition ? Vector3.Distance(position, _previousAnchorPosition) : 0f;
                _previousAnchorPosition = position;
                _hasPreviousAnchorPosition = true;
            }
            _previousClothVertices = vertices;
        }

        private void RefreshFlareSkirt(bool force)
        {
            GameObject root = null;
            if ((!_hasFlareClothSetting || _flareSkirtACloth) && ChaControl != null && ChaControl.sex == 1 &&
                ChaControl.nowCoordinate != null && ChaControl.nowCoordinate.clothes != null &&
                ChaControl.nowCoordinate.clothes.parts != null &&
                ChaControl.nowCoordinate.clothes.parts.Length > 1 &&
                (ChaControl.nowCoordinate.clothes.parts[1].id == FlareSkirtCloneId ||
                 (ChaControl.infoClothes != null && ChaControl.infoClothes.Length > 1 &&
                  ChaControl.infoClothes[1] != null &&
                  ChaControl.infoClothes[1].GetInfo(ChaListDefine.KeyType.Name) == FlareSkirtCloneName)) &&
                ChaControl.objClothes != null && ChaControl.objClothes.Length > 1)
                root = ChaControl.objClothes[1];

            int state = ChaControl != null && ChaControl.fileStatus != null &&
                        ChaControl.fileStatus.clothesState != null && ChaControl.fileStatus.clothesState.Length > 1
                ? ChaControl.fileStatus.clothesState[1] : -1;
            if (!force && root == _flareRoot && state == _flareClothesState && _flareClones.Count > 0) return;
            ClearFlareSkirt();
            if (root == null)
            {
                if (_flareSkirtACloth && ChaControl != null)
                {
                    int id = ChaControl.nowCoordinate != null && ChaControl.nowCoordinate.clothes != null &&
                             ChaControl.nowCoordinate.clothes.parts != null &&
                             ChaControl.nowCoordinate.clothes.parts.Length > 1
                        ? ChaControl.nowCoordinate.clothes.parts[1].id : -1;
                    string name = ChaControl.infoClothes != null && ChaControl.infoClothes.Length > 1 &&
                                  ChaControl.infoClothes[1] != null
                        ? ChaControl.infoClothes[1].GetInfo(ChaListDefine.KeyType.Name) : "null";
                    string reason = "id=" + id + " name=" + name + " object=" +
                                    (ChaControl.objClothes != null && ChaControl.objClothes.Length > 1 &&
                                     ChaControl.objClothes[1] != null);
                    if (_lastFlareSkip != reason)
                    {
                        Plugin.LogSource.LogWarning("Cloth not started: " + reason);
                        _lastFlareSkip = reason;
                    }
                }
                return;
            }
            _lastFlareSkip = null;

            try
            {
                var sources = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                int eligible = 0;
                foreach (var source in sources)
                {
                    if (source == null || source.GetComponent<Cloth>() != null || !source.enabled ||
                        !source.gameObject.activeInHierarchy || source.sharedMesh == null)
                        continue;
                    eligible++;
                    if (!source.sharedMesh.isReadable)
                    {
                        Plugin.LogSource.LogWarning("Flare skirt mesh is not readable: " + source.name);
                        continue;
                    }
                    GameObject clone = CreateClothClone(source);
                    if (clone == null) continue;
                    _flareClones.Add(clone);
                    _flareOriginals.Add(source, source.enabled);
                    _flareRenderers.Add(source, clone.GetComponent<SkinnedMeshRenderer>());
                    source.enabled = false;
                }
                if (_flareClones.Count == 0)
                {
                    Plugin.LogSource.LogWarning("Cloth not started: skirt root=" + root.name +
                                     " skinnedMeshes=" + sources.Length + " eligible=" + eligible);
                    return;
                }
                // The original skirt's bone simulation would otherwise drive the cloth's skinned rest pose.
                foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
                {
                    if (behaviour == null || behaviour.GetType().Name != "DynamicBone") continue;
                    _flareDynamics[behaviour] = behaviour.enabled;
                    behaviour.enabled = false;
                }
                _flareRoot = root;
                _flareClothesState = state;
                Plugin.LogSource.LogInfo("Flare skirt A cloth clones=" + _flareClones.Count);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError("Flare skirt clone failed: " + ex);
                ClearFlareSkirt();
            }
        }

        private GameObject CreateClothClone(SkinnedMeshRenderer source)
        {
            var mesh = source.sharedMesh.name == "o_bot_skirt05_a" && source.sharedMesh.vertexCount == 847
                ? CreateTrialAnnulusMesh(source)
                : source.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0) return null;
            if (mesh == source.sharedMesh && mesh.bounds.size.y <= 0.001f) return null;

            var clone = new GameObject("FlareSkirtA_ClothClone");
            try
            {
                clone.transform.SetParent(source.transform.parent, false);
                clone.transform.localPosition = source.transform.localPosition;
                clone.transform.localRotation = source.transform.localRotation;
                clone.transform.localScale = source.transform.localScale;
                var renderer = clone.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = mesh;
                renderer.rootBone = source.rootBone;
                renderer.bones = source.bones;
                renderer.sharedMaterials = source.sharedMaterials;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                    renderer.SetBlendShapeWeight(i, source.GetBlendShapeWeight(i));
                renderer.quality = source.quality;
                renderer.updateWhenOffscreen = true;
                if (mesh == source.sharedMesh) renderer.localBounds = source.localBounds;
                else
                {
                    var center = mesh.bounds.center;
                    center.y -= 0.25f;
                    renderer.localBounds = new Bounds(center, new Vector3(1.2f, 0.8f, 1.2f));
                }
                renderer.shadowCastingMode = source.shadowCastingMode;
                renderer.receiveShadows = source.receiveShadows;

                var cloth = clone.AddComponent<Cloth>();
                if (mesh != source.sharedMesh)
                {
                    var center = mesh.bounds.center;
                    center.y -= 0.25f;
                    renderer.localBounds = new Bounds(center, new Vector3(1.2f, 0.8f, 1.2f));
                }
                _clothRestVertices[cloth] = cloth.vertices;
                int fixedCount = ApplyClothTuning(cloth, mesh);
                var capsules = new List<CapsuleCollider>();
                var spheres = new List<ClothSphereColliderPair>();
                foreach (var obj in _objects)
                {
                    if (obj == null) continue;
                    var capsule = obj.GetComponent<CapsuleCollider>();
                    if (capsule != null) capsules.Add(capsule);
                    var sphere = obj.GetComponent<SphereCollider>();
                    if (sphere != null) spheres.Add(new ClothSphereColliderPair(sphere));
                }
                cloth.capsuleColliders = capsules.ToArray();
                cloth.sphereColliders = spheres.ToArray();
                var appliedCoefficients = cloth.coefficients;
                int freeCount = 0;
                float maxAppliedDistance = 0f;
                foreach (var coefficient in appliedCoefficients)
                {
                    if (coefficient.maxDistance > 0.001f) freeCount++;
                    maxAppliedDistance = Mathf.Max(maxAppliedDistance, coefficient.maxDistance);
                }
                Plugin.LogSource.LogInfo("Cloth mesh=" + mesh.name + " sourceVertices=" + vertices.Length +
                                         " clothVertices=" + cloth.vertices.Length +
                                         " coefficients=" + appliedCoefficients.Length +
                                         " pinned=" + fixedCount + " hemTravel=" + _clothTuning.HemDistance +
                                         " free=" + freeCount + " maxAppliedDistance=" + maxAppliedDistance +
                                         " sourceWorldScale=" + source.transform.lossyScale +
                                         " cloneWorldScale=" + clone.transform.lossyScale +
                                         " gravity=" + cloth.useGravity + " gravityVector=" + Physics.gravity +
                                         " enabled=" + cloth.enabled + " damping=" + cloth.damping +
                                         " stretch=" + cloth.stretchingStiffness +
                                         " bend=" + cloth.bendingStiffness +
                                         " capsules=" + capsules.Count + " spheres=" + spheres.Count);
                if (mesh != source.sharedMesh) _flareGeneratedMeshes.Add(mesh);
                return clone;
            }
            catch
            {
                Destroy(clone);
                if (mesh != source.sharedMesh) Destroy(mesh);
                throw;
            }
        }

        private static Mesh CreateTrialAnnulusMesh(SkinnedMeshRenderer source)
        {
            const int segments = 48;
            const int radialSegments = 12;
            const float innerRadius = 0.11f;
            const float outerRadius = 0.46f;
            var sourceMesh = source.sharedMesh;
            var sourceVertices = sourceMesh.vertices;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            foreach (var vertex in sourceVertices)
            {
                minY = Mathf.Min(minY, vertex.y);
                maxY = Mathf.Max(maxY, vertex.y);
            }
            float height = maxY - minY;
            float centerX = 0f, centerZ = 0f;
            int centerCount = 0;
            foreach (var vertex in sourceVertices)
                if (vertex.y >= maxY - height * 0.1f)
                {
                    centerX += vertex.x;
                    centerZ += vertex.z;
                    centerCount++;
                }
            if (centerCount > 0) { centerX /= centerCount; centerZ /= centerCount; }
            float waistY = maxY - height * 0.06f;

            int waistBone = -1;
            for (int i = 0; i < source.bones.Length; i++)
                if (source.bones[i] != null &&
                    source.bones[i].name == "cf_s_waist01")
                { waistBone = i; break; }
            if (waistBone < 0)
            {
                var sourceWeights = sourceMesh.boneWeights;
                waistBone = sourceWeights.Length > 0 ? sourceWeights[0].boneIndex0 : 0;
            }
            if (source.bones.Length == 0 || waistBone >= source.bones.Length ||
                waistBone >= sourceMesh.bindposes.Length)
                throw new InvalidOperationException("Trial annulus skirt has no matching waist bone/bindpose");
            var positions = new Vector3[(radialSegments + 1) * segments];
            var uv = new Vector2[positions.Length];
            var weights = new BoneWeight[positions.Length];
            for (int ring = 0; ring <= radialSegments; ring++)
            {
                float fraction = (float)ring / radialSegments;
                float radius = Mathf.Lerp(innerRadius, outerRadius, fraction);
                for (int around = 0; around < segments; around++)
                {
                    float angle = around * Mathf.PI * 2f / segments;
                    int index = ring * segments + around;
                    positions[index] = new Vector3(centerX + Mathf.Cos(angle) * radius,
                                                    waistY - 0.01f * fraction,
                                                    centerZ + Mathf.Sin(angle) * radius);
                    uv[index] = new Vector2((Mathf.Cos(angle) * fraction + 1f) * 0.5f,
                                            (Mathf.Sin(angle) * fraction + 1f) * 0.5f);
                    weights[index] = new BoneWeight { boneIndex0 = waistBone, weight0 = 1f };
                }
            }
            var triangles = new int[radialSegments * segments * 6];
            int write = 0;
            for (int ring = 0; ring < radialSegments; ring++)
                for (int around = 0; around < segments; around++)
                {
                    int next = (around + 1) % segments;
                    int inner = ring * segments + around;
                    int innerNext = ring * segments + next;
                    int outer = (ring + 1) * segments + around;
                    int outerNext = (ring + 1) * segments + next;
                    triangles[write++] = inner;
                    triangles[write++] = innerNext;
                    triangles[write++] = outer;
                    triangles[write++] = innerNext;
                    triangles[write++] = outerNext;
                    triangles[write++] = outer;
                }
            var mesh = new Mesh { name = "FlareSkirtTrialAnnulus" };
            mesh.vertices = positions;
            mesh.uv = uv;
            mesh.boneWeights = weights;
            mesh.bindposes = sourceMesh.bindposes;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Plugin.LogSource.LogInfo("Trial annulus skirt inner=" + innerRadius + " outer=" + outerRadius +
                                     " waistY=" + waistY + " waistBone=" + waistBone +
                                     " boneName=" + (source.bones[waistBone] == null ? "null" : source.bones[waistBone].name));
            return mesh;
        }

        private int ApplyClothTuning(Cloth cloth, Mesh mesh)
        {
            var vertices = mesh.vertices;
            if (vertices.Length == 0) return 0;
            bool annulus = mesh.name == "FlareSkirtTrialAnnulus" && vertices.Length == 624;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            foreach (var vertex in vertices)
            {
                if (vertex.y < minY) minY = vertex.y;
                if (vertex.y > maxY) maxY = vertex.y;
            }
            float cutoff = maxY - (maxY - minY) * _clothTuning.PinnedTop;
            var coefficients = new ClothSkinningCoefficient[vertices.Length];
            int fixedCount = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                float fraction = annulus ? (float)(i / 48) / 12f :
                    Mathf.Clamp01((cutoff - vertices[i].y) / Mathf.Max(0.001f, cutoff - minY));
                bool pinned = annulus ? fraction < _clothTuning.PinnedTop : vertices[i].y >= cutoff;
                if (pinned) fixedCount++;
                coefficients[i] = new ClothSkinningCoefficient
                {
                    maxDistance = pinned ? 0f : _clothTuning.HemDistance * fraction,
                    collisionSphereDistance = 0f
                };
            }
            cloth.coefficients = coefficients;
            cloth.useGravity = _clothTuning.Gravity;
            cloth.useTethers = _clothTuning.UseTethers;
            cloth.damping = _clothTuning.Damping;
            cloth.friction = _clothTuning.Friction;
            cloth.stretchingStiffness = _clothTuning.Stretch;
            cloth.bendingStiffness = _clothTuning.Bend;
            cloth.clothSolverFrequency = _clothTuning.SolverHz;
            cloth.externalAcceleration = new Vector3(0f, -_clothTuning.ExtraDown, 0f);
            cloth.worldVelocityScale = _clothTuning.WorldVelocity;
            cloth.worldAccelerationScale = _clothTuning.WorldAcceleration;
            cloth.sleepThreshold = 0f;
            return fixedCount;
        }

        private void ClearFlareSkirt()
        {
            foreach (var pair in _flareOriginals)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            _flareOriginals.Clear();
            _flareRenderers.Clear();
            foreach (var pair in _flareDynamics)
                if (pair.Key != null) pair.Key.enabled = pair.Value;
            _flareDynamics.Clear();
            _clothRestVertices.Clear();
            foreach (var clone in _flareClones)
                if (clone != null) Destroy(clone);
            _flareClones.Clear();
            foreach (var mesh in _flareGeneratedMeshes)
                if (mesh != null) Destroy(mesh);
            _flareGeneratedMeshes.Clear();
            _flareRoot = null;
            _flareClothesState = -1;
            _previousClothVertices = null;
            _clothVertexMotion = 0f;
            _clothVertexOffset = 0f;
            _clothVertexMaxOffset = 0f;
            _anchorMotion = 0f;
            _hasPreviousAnchorPosition = false;
            _previousSampleTime = 0f;
            _sampleInterval = 0f;
        }
    }
}
