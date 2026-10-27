using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private GameObject _realWorldSubCameraRoot;
        private Camera _realWorldSubCamera;
        private OrbitCamera _realWorldSubCamOrbit;
        private RenderTexture _realWorldRT;
        private readonly System.Collections.Generic.List<GameObject> _displayQuads = new System.Collections.Generic.List<GameObject>();
        private readonly System.Collections.Generic.List<Material> _displayMats = new System.Collections.Generic.List<Material>();
        private ChaControl _nearDisplayLookAtOwner;
        private Transform _nearDisplayLookAtHead;
        private float _nextNearDisplayHeadLookupAt;

        private const float NearDisplayHeight = 1.8f;
        private float GetNearDisplayWidth()
        {
            return NearDisplayHeight;
        }

        private Vector3 GetNearDisplayOffset()
        {
            return _skyHeading * new Vector3(_skyX.Value,_skyY.Value,_skyZ.Value);
        }

        private float GetNearDisplayScale()
        {
            return _skyDiameter.Value / NearDisplayHeight;
        }
        private static readonly Vector3 SubCamRelOffset = new Vector3(0f, 0.4f, 2.5f);
        private static readonly Vector3 FrozenCloneWorldOffset = Vector3.down * 100f;

        internal void CreateRealWorldSubCamera(Vector3 realCenter)
        {
            int rtW = 1024;
            int rtH = 1024;
            Vector3 astralCenter = _skyOrigin;
            Vector3 realFemaleWaistCenter = GetRealWorldFemaleWaistCenter();
            Vector3 orbitCenter = realFemaleWaistCenter + FrozenCloneWorldOffset;
            Vector3 dynamicWaist;
            Vector3 dynamicHead;
            bool hasCapturedLine = TryGetSubCameraTargetLineOnClone(FrozenCloneWorldOffset, out dynamicWaist, out dynamicHead);
            if (!hasCapturedLine)
            {
                dynamicWaist = orbitCenter;
                dynamicHead = dynamicWaist + Vector3.up * 0.75f;
            }
            SimpleFileLogger.Info($"SubCamera orbit center using frozen clone waist: realWaist={realFemaleWaistCenter}, cloneOffset={FrozenCloneWorldOffset}, orbitCenter={orbitCenter}, realCenter={realCenter}, displayCenter={astralCenter}, hasCapturedLine={hasCapturedLine}, dynamicWaist={dynamicWaist}, dynamicHead={dynamicHead}");

            if (_realWorldSubCamera != null && _realWorldRT != null && _displayQuads.Count > 0)
            {
                _realWorldSubCameraRoot.SetActive(true);
                _realWorldSubCamera.enabled = true;
                _realWorldSubCamera.targetTexture = _realWorldRT;
                _realWorldSubCamera.transform.position = orbitCenter + SubCamRelOffset;
                _realWorldSubCamera.transform.LookAt(orbitCenter);
                if (_realWorldSubCamOrbit != null)
                {
                    _realWorldSubCamOrbit.enabled = false;
                    _realWorldSubCamOrbit.Center = orbitCenter;
                    _realWorldSubCamOrbit.CurrentAngleDeg = 0f;
                }

                IsolateFrozenDisplay();
            BeginDynamicSubCamera(dynamicWaist, dynamicHead);

                for (int i = 0; i < _displayQuads.Count; i++)
                    if (_displayQuads[i] != null) _displayQuads[i].SetActive(true);

                RepositionDisplays(astralCenter);
                LogSubCameraState("reused", orbitCenter);
                SimpleFileLogger.Info($"SubCamera+NearAvatarDisplay reused. RT={_realWorldRT.width}x{_realWorldRT.height}, orbitCenter={orbitCenter}, astralCenter={astralCenter}, nearDisplayPos={astralCenter + GetNearDisplayOffset()}");
                return;
            }

            _realWorldSubCameraRoot = new GameObject("AstralMode_RealWorldSubCamera");
            Object.DontDestroyOnLoad(_realWorldSubCameraRoot);
            _realWorldSubCameraRoot.hideFlags = HideFlags.DontSave;

            _realWorldSubCamera = _realWorldSubCameraRoot.AddComponent<Camera>();
            _realWorldSubCamera.clearFlags = CameraClearFlags.SolidColor;
            _realWorldSubCamera.backgroundColor = new Color(0.05f, 0.05f, 0.08f, 1f);
            _realWorldSubCamera.fieldOfView = 50f;
            _realWorldSubCamera.nearClipPlane = 0.05f;
            _realWorldSubCamera.farClipPlane = 200f;
            _realWorldSubCamera.allowHDR = false;
            _realWorldSubCamera.allowMSAA = false;
            _realWorldSubCamera.depth = -10;
            _realWorldSubCameraRoot.transform.position = orbitCenter + SubCamRelOffset;
            _realWorldSubCameraRoot.transform.LookAt(orbitCenter);

            _realWorldSubCamOrbit = _realWorldSubCameraRoot.AddComponent<OrbitCamera>();
            _realWorldSubCamOrbit.Center = orbitCenter;
            _realWorldSubCamOrbit.Radius = 2.8f;
            _realWorldSubCamOrbit.Height = 0.4f;
            _realWorldSubCamOrbit.DegreesPerSecond = 18f;
            _realWorldSubCamOrbit.CurrentAngleDeg = 0f;
            _realWorldSubCamOrbit.LookAtCenter = true;
            _realWorldSubCamOrbit.enabled = false;

            _realWorldRT = new RenderTexture(rtW, rtH, 24, RenderTextureFormat.ARGB32)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                useMipMap = false,
                autoGenerateMips = false
            };
            _realWorldRT.Create();
            _realWorldSubCamera.targetTexture = _realWorldRT;

            Shader shader = Shader.Find("Unlit/Texture")
                          ?? Shader.Find("Unlit/Transparent")
                          ?? Shader.Find("Sprites/Default")
                          ?? Shader.Find("Standard");
            SimpleFileLogger.Info($"Display shader: {(shader != null ? shader.name : "NULL")}");

            CreateOneDisplay("Display_NearAvatar", astralCenter + GetNearDisplayOffset(), shader, GetNearDisplayWidth(), NearDisplayHeight);
            IsolateFrozenDisplay();
            BeginDynamicSubCamera(dynamicWaist, dynamicHead);

            LogSubCameraState("created", orbitCenter);
            SimpleFileLogger.Info($"SubCamera+NearAvatarDisplay created. RT={rtW}x{rtH}, orbitCenter={orbitCenter}, astralCenter={astralCenter}, nearDisplayPos={astralCenter + GetNearDisplayOffset()}");
        }

        private void LogSubCameraState(string phase, Vector3 orbitCenter)
        {
            if (_realWorldSubCamera == null)
            {
                SimpleFileLogger.Warn($"SubCamera diag {phase}: camera=null, orbitCenter={orbitCenter}");
                return;
            }

            RenderTexture rt = _realWorldSubCamera.targetTexture;
            string rtInfo = rt != null ? $"{rt.width}x{rt.height}, created={rt.IsCreated()}" : "null";
            float dist = Vector3.Distance(_realWorldSubCamera.transform.position, orbitCenter);
            SimpleFileLogger.Info($"SubCamera diag {phase}: enabled={_realWorldSubCamera.enabled}, active={_realWorldSubCamera.gameObject.activeInHierarchy}, pos={_realWorldSubCamera.transform.position}, forward={_realWorldSubCamera.transform.forward}, orbitCenter={orbitCenter}, dist={dist:F3}, cullingMask=0x{_realWorldSubCamera.cullingMask:X8}, targetRT={rtInfo}, depth={_realWorldSubCamera.depth}, fov={_realWorldSubCamera.fieldOfView}");
        }

        private Quaternion GetNearDisplayRotation(Vector3 worldPos, Vector3 astralCenter)
        {
            Vector3 dir=_skyOrigin-worldPos;
            if(dir.sqrMagnitude<0.001f) dir=Vector3.forward;
            return Quaternion.LookRotation(dir,Vector3.up)*Quaternion.Euler(0,180,0)*Quaternion.Euler(_skyRotX.Value,_skyRotY.Value,_skyRotZ.Value);
        }

        private Vector3 GetAstralHeadLookAtTarget(Vector3 fallback)
        {
            if ((_nearDisplayLookAtOwner == null || _nearDisplayLookAtHead == null)
                && Time.unscaledTime >= _nextNearDisplayHeadLookupAt)
            {
                _nextNearDisplayHeadLookupAt = Time.unscaledTime + 1f;
                _nearDisplayLookAtOwner = GetPrimaryAstralMale();
                if (_nearDisplayLookAtOwner != null)
                {
                    _nearDisplayLookAtHead = FindFirstTransformByName(
                        _nearDisplayLookAtOwner.transform,
                        "cf_J_FaceRoot", "cf_J_Head", "cf_J_Head_s",
                        "cf_J_Neck", "cf_J_Neck_s");
                }
            }

            return _nearDisplayLookAtHead != null ? _nearDisplayLookAtHead.position : fallback;
        }

        private ChaControl GetPrimaryAstralMale()
        {
            var charas = CollectCurrentHSceneCharas();
            for (int i = 0; i < charas.Count; i++)
            {
                ChaControl c = charas[i];
                if (c != null && c.name.StartsWith("chaM_"))
                    return c;
            }

            for (int i = 0; i < charas.Count; i++)
            {
                ChaControl c = charas[i];
                if (c != null && c.name.StartsWith("chaF_"))
                    return c;
            }

            return charas.Count > 0 ? charas[0] : null;
        }

        private void RepositionDisplays(Vector3 astralCenter)
        {
            SetDisplayTransform(0, _skyOrigin + GetNearDisplayOffset(), GetNearDisplayWidth(), NearDisplayHeight);
        }

        private void SetDisplayTransform(int index, Vector3 worldPos, float w, float h)
        {
            if (index < 0 || index >= _displayQuads.Count || _displayQuads[index] == null) return;
            Vector3 astralCenter = _skyOrigin;
            GameObject q = _displayQuads[index];
            if (q.transform.position != worldPos)
                q.transform.position = worldPos;
            Quaternion rotation = GetNearDisplayRotation(worldPos, astralCenter);
            if (q.transform.rotation != rotation)
                q.transform.rotation = rotation;
            float displayScale = GetNearDisplayScale();
            Vector3 scale = new Vector3(w * displayScale, h * displayScale, 1f);
            if (q.transform.localScale != scale)
                q.transform.localScale = scale;
            { } // [perf] per-frame log removed (astral stutter fix)
        }

        private void CreateOneDisplay(string name, Vector3 worldPos, Shader shader, float w, float h)
        {
            Vector3 astralCenter = _skyOrigin;
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "AstralMode_" + name;
            Object.DontDestroyOnLoad(q);
            q.hideFlags = HideFlags.DontSave;
            q.layer = 0;

            Collider col = q.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            q.transform.position = worldPos;
            q.transform.rotation = GetNearDisplayRotation(worldPos, astralCenter);
            float displayScale = GetNearDisplayScale();
            q.transform.localScale = new Vector3(w * displayScale, h * displayScale, 1f);

            Material mat = new Material(shader) { hideFlags = HideFlags.DontSave };
            mat.mainTexture = _realWorldRT;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.white);
                mat.SetTexture("_EmissionMap", _realWorldRT);
            }

            MeshRenderer mr = q.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.enabled = true;
            }
            SimpleFileLogger.Info($"  {name} created at {worldPos}, rot={q.transform.rotation.eulerAngles}, scale=({w:F1},{h:F1}), mr={(mr != null ? "OK" : "NULL")}");

            _displayQuads.Add(q);
            _displayMats.Add(mat);
        }

        internal void DestroyRealWorldSubCamera()
        {
            EndDynamicSubCamera();
            _nearDisplayLookAtOwner = null;
            _nearDisplayLookAtHead = null;
            _nextNearDisplayHeadLookupAt = 0f;

            if (_realWorldSubCamera != null)
            {
                LogSubCameraState("before-hide", GetRealWorldFemaleWaistCenter() + FrozenCloneWorldOffset);
                _realWorldSubCamera.targetTexture = null;
                _realWorldSubCamera.enabled = false;
            }
            if (_realWorldSubCamOrbit != null)
                _realWorldSubCamOrbit.enabled = false;
            if (_realWorldSubCameraRoot != null)
                _realWorldSubCameraRoot.SetActive(false);

            for (int i = 0; i < _displayQuads.Count; i++)
                if (_displayQuads[i] != null) _displayQuads[i].SetActive(false);

            SimpleFileLogger.Info("SubCamera+NearAvatarDisplay hidden for reuse");
        }

        internal void DisposeRealWorldSubCamera()
        {
            EndDynamicSubCamera();
            _nearDisplayLookAtOwner = null;
            _nearDisplayLookAtHead = null;
            _nextNearDisplayHeadLookupAt = 0f;

            if (_realWorldSubCamera != null)
            {
                _realWorldSubCamera.targetTexture = null;
                Object.Destroy(_realWorldSubCamera.gameObject);
                _realWorldSubCamera = null;
                _realWorldSubCamOrbit = null;
                _realWorldSubCameraRoot = null;
            }
            if (_realWorldRT != null)
            {
                _realWorldRT.Release();
                Object.Destroy(_realWorldRT);
                _realWorldRT = null;
            }
            foreach (var q in _displayQuads)
                if (q != null) Object.Destroy(q);
            _displayQuads.Clear();

            foreach (var m in _displayMats)
                if (m != null) Object.Destroy(m);
            _displayMats.Clear();

            SimpleFileLogger.Info("SubCamera+NearAvatarDisplay disposed");
        }
    }
}
