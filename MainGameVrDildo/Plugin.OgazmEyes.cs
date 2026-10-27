using System;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        private float _ogazmHeldVoiceEyeWeight;
        private ChaControl _ogazmEyeFemale;
        private Transform _ogazmLeftEye, _ogazmRightEye;
        private Quaternion _ogazmLeftNative, _ogazmRightNative;
        private readonly List<IrisRenderBinding> _ogazmIrises = new List<IrisRenderBinding>();
        private readonly List<EyeRenderScope> _ogazmScopes = new List<EyeRenderScope>();
        private int _ogazmScopeDepth;
        private string _ogazmEyeStatus = "描画待ち";
        private float _ogazmEyeNextValueLogAt;
        private int _ogazmEyeRenderCalls, _ogazmEyeLastAppliedFrame = -1;
        private int _ogazmIrisAppliedCount;
        private float _ogazmEyeGauge, _ogazmEyeWeight;

        private void InstallOgazmEyes()
        {
            Camera.onPreRender += ApplyOgazmEyesBeforeRender;
            Camera.onPostRender += RestoreOgazmEyesAfterRender;
            TraceVibeEvent("EYES_HOOK_READY", "preRender apply, postRender restore; actual iris texture ST override");
        }

        private void RemoveOgazmEyes()
        {
            Camera.onPreRender -= ApplyOgazmEyesBeforeRender;
            Camera.onPostRender -= RestoreOgazmEyesAfterRender;
            RestoreOgazmEyes();
        }

        private void ApplyOgazmEyesBeforeRender(Camera camera)
        {
            try { ApplyOgazmEyes(camera); }
            catch (Exception ex)
            {
                RestoreOgazmEyes();
                SetOgazmEyeStatus("目の描画適用でエラー");
                TraceVibeEvent("EYES_ERROR", ex.ToString());
            }
        }

        private void ApplyOgazmEyes(Camera camera)
        {
            _ogazmEyeRenderCalls++;
            // Recover if a camera was destroyed or its post-render callback was skipped.
            if (_ogazmScopeDepth > 0 && _ogazmScopes[_ogazmScopeDepth - 1].Frame != Time.frameCount)
                RestoreEyeRenderScopes();
            bool cuePlaying = MainGameVoiceFaceEventBridge.PublicApi.IsOgazmCuePlaying();
            string block = _settings == null ? "設定未読込" :
                !_settings.Enabled ? "プラグイン無効" :
                !_settings.MotionGaugeEnabled ? "移動距離計測が無効" :
                !_settings.OgazmEyesEnabled ? "より目が無効" :
                _model == null && !cuePlaying ? "ディルド未生成" :
                _female == null ? "対象キャラ未取得" :
                _scene == null || _scene.flags == null || _scene.flags.mode != HFlag.EMode.aibu ? "愛撫シーン外" :
                _selectedTarget != TargetKind.Vaginal && !cuePlaying ? "膣ターゲット未選択" : null;
            if (block != null)
            {
                SetOgazmEyeStatus(block);
                _ogazmHeldVoiceEyeWeight = 0f;
                _ogazmEyeWeight = 0f;
                RestoreOgazmEyes();
                return;
            }
            _ogazmEyeGauge = Mathf.Clamp01(_motionGaugeCm / _settings.MotionGaugeFullCm);
            float weight = Mathf.InverseLerp(_settings.OgazmEyeStartGaugeFraction,
                _settings.OgazmEyeFullGaugeFraction, _ogazmEyeGauge);
            if (cuePlaying)
            {
                float voiceStartWeight = Mathf.InverseLerp(_settings.OgazmEyeStartGaugeFraction,
                    _settings.OgazmEyeFullGaugeFraction, _settings.OgazmEnterGaugeFraction);
                _ogazmHeldVoiceEyeWeight = Mathf.Max(_ogazmHeldVoiceEyeWeight, Mathf.Max(weight, voiceStartWeight));
                weight = Mathf.Max(weight, _ogazmHeldVoiceEyeWeight);
            }
            else _ogazmHeldVoiceEyeWeight = 0f;
            _ogazmEyeWeight = weight;
            if (weight <= 0f)
            {
                SetOgazmEyeStatus("開始ゲージ未満");
                RestoreOgazmEyes();
                return;
            }

            if (_ogazmEyeFemale != _female)
            {
                RestoreOgazmEyes();
                TryResolveOgazmEyes(_female, out _ogazmLeftEye, out _ogazmRightEye);
                _ogazmIrises.Clear();
                var controllers = _female.eyeLookMatCtrl;
                if (controllers != null)
                    foreach (var controller in controllers)
                    {
                        if (controller == null || controller._renderer == null) continue;
                        bool duplicate = false;
                        foreach (var iris in _ogazmIrises)
                            if (iris.Renderer == controller._renderer) { duplicate = true; break; }
                        if (!duplicate) _ogazmIrises.Add(new IrisRenderBinding(controller));
                    }
                if (_ogazmLeftEye == null && _ogazmRightEye == null && _ogazmIrises.Count == 0)
                {
                    SetOgazmEyeStatus("目ボーン・虹彩制御を取得できない");
                    return;
                }
                _ogazmEyeFemale = _female;
                if (MotionTraceEnabled) TraceVibeEvent("EYES_RESOLVED", "leftBone=" + _ogazmLeftEye?.name
                    + " rightBone=" + _ogazmRightEye?.name + " irisRenderers=" + _ogazmIrises.Count);
            }
            if (_ogazmScopeDepth == 0)
            {
                if (_ogazmLeftEye != null) _ogazmLeftNative = _ogazmLeftEye.localRotation;
                if (_ogazmRightEye != null) _ogazmRightNative = _ogazmRightEye.localRotation;
                foreach (var iris in _ogazmIrises) iris.CaptureNativeTextureTransforms();
            }

            if (_ogazmScopeDepth == _ogazmScopes.Count) _ogazmScopes.Add(new EyeRenderScope());
            EyeRenderScope scope = _ogazmScopes[_ogazmScopeDepth++];
            scope.Capture(camera, _ogazmLeftEye, _ogazmRightEye, _ogazmIrises.Count);
            scope.Trace = MotionTraceEnabled && Time.unscaledTime >= _ogazmEyeNextValueLogAt;
            if (scope.Trace) _ogazmEyeNextValueLogAt = Time.unscaledTime + 0.5f;
            float sign = _settings.OgazmInvertEyeDirection ? -1f : 1f;
            float inward = _settings.OgazmEyeInwardDegrees * weight * sign;
            float upward = _settings.OgazmEyeVerticalDegrees * weight;
            var calc = _female.eyeLookCtrl != null ? _female.eyeLookCtrl.eyeLookScript : null;
            Transform head = calc != null ? calc.rootNode : null;
            Vector3 forward = head != null ? head.TransformDirection(calc.headLookVector).normalized : _female.transform.forward;
            Vector3 up = head != null ? head.TransformDirection(calc.headUpVector).normalized : _female.transform.up;
            bool bonesReady = _ogazmLeftEye != null && _ogazmRightEye != null;
            Vector3 center = bonesReady
                ? (_ogazmLeftEye.position + _ogazmRightEye.position) * 0.5f : Vector3.zero;
            if (bonesReady)
            {
                ApplyConvergentEyeRotation(_ogazmLeftEye, _ogazmLeftNative, center, forward, up, inward, upward);
                ApplyConvergentEyeRotation(_ogazmRightEye, _ogazmRightNative, center, forward, up, inward, upward);
            }
            _ogazmIrisAppliedCount = 0;
            for (int i = 0; i < _ogazmIrises.Count; i++)
                if (_ogazmIrises[i].Apply(scope.Irises[i], _settings.OgazmIrisInwardUv * weight * sign,
                    _settings.OgazmIrisUpUv * weight)) _ogazmIrisAppliedCount++;
            _ogazmEyeLastAppliedFrame = Time.frameCount;
            SetOgazmEyeStatus(_ogazmIrisAppliedCount > 0 ? "虹彩UVに描画適用中" :
                bonesReady ? "目ボーンのみ / 虹彩UV未適用" : "描画に使える目の制御がない");
            if (scope.Trace) TraceEyeRender(scope, "EYES_RENDER_APPLY");
        }

        private static void ApplyConvergentEyeRotation(Transform eye, Quaternion nativeLocal,
            Vector3 center, Vector3 forward, Vector3 up, float inward, float upward)
        {
            if (eye == null || eye.parent == null) return;
            // Derive inward from the anatomical eye positions, not a guessed local-Y sign.
            float yawSign = Mathf.Sign(Vector3.Dot(Vector3.Cross(forward, center - eye.position), up));
            Vector3 right = Vector3.Cross(up, forward).normalized;
            eye.rotation = Quaternion.AngleAxis(inward * yawSign, up)
                * Quaternion.AngleAxis(-upward, right) * eye.parent.rotation * nativeLocal;
        }

        private void RestoreOgazmEyesAfterRender(Camera camera)
        {
            if (_ogazmScopeDepth == 0 || camera == null) return;
            EyeRenderScope scope = _ogazmScopes[_ogazmScopeDepth - 1];
            if (scope.CameraId != camera.GetInstanceID()) return;
            try
            {
                if (scope.Trace) TraceEyeRender(scope, "EYES_RENDER_END");
            }
            finally
            {
                scope.Restore();
                _ogazmScopeDepth--;
            }
        }

        private void TraceEyeRender(EyeRenderScope scope, string stage)
        {
            TraceVibeEvent(stage, "camera=" + scope.CameraId + " gauge01=" + TraceNumber(_ogazmEyeGauge)
                + " weight=" + TraceNumber(_ogazmEyeWeight) + " leftEuler="
                + (_ogazmLeftEye != null ? TraceVector(_ogazmLeftEye.localEulerAngles) : "missing")
                + " rightEuler=" + (_ogazmRightEye != null ? TraceVector(_ogazmRightEye.localEulerAngles) : "missing")
                + " irisCount=" + _ogazmIrises.Count);
            foreach (var iris in _ogazmIrises) TraceVibeEvent(stage + "_IRIS", iris.Describe());
        }

        private void SetOgazmEyeStatus(string status) { _ogazmEyeStatus = status; }

        private void RestoreEyeRenderScopes()
        {
            while (_ogazmScopeDepth > 0) _ogazmScopes[--_ogazmScopeDepth].Restore();
        }

        private void RestoreOgazmEyes()
        {
            RestoreEyeRenderScopes();
            _ogazmIrisAppliedCount = 0;
            _ogazmEyeFemale = null;
            _ogazmLeftEye = null;
            _ogazmRightEye = null;
            _ogazmIrises.Clear();
        }

        private sealed class EyeRenderScope
        {
            internal int CameraId, Frame;
            internal bool Trace;
            private Transform _left, _right;
            private Quaternion _leftBefore, _rightBefore;
            internal readonly List<IrisRenderSnapshot> Irises = new List<IrisRenderSnapshot>();
            internal void Capture(Camera camera, Transform left, Transform right, int irisCount)
            {
                CameraId = camera != null ? camera.GetInstanceID() : 0;
                Frame = Time.frameCount;
                _left = left; _right = right;
                if (left != null) _leftBefore = left.localRotation;
                if (right != null) _rightBefore = right.localRotation;
                while (Irises.Count < irisCount) Irises.Add(new IrisRenderSnapshot());
            }
            internal void Restore()
            {
                if (_left != null) _left.localRotation = _leftBefore;
                if (_right != null) _right.localRotation = _rightBefore;
                foreach (var iris in Irises) iris.Restore();
            }
        }

        private sealed class IrisRenderSnapshot
        {
            internal readonly MaterialPropertyBlock Before = new MaterialPropertyBlock();
            internal readonly MaterialPropertyBlock Applied = new MaterialPropertyBlock();
            internal Renderer Renderer;
            internal void Capture(Renderer renderer)
            {
                Renderer = renderer;
                Before.Clear(); Applied.Clear();
                renderer.GetPropertyBlock(Before, 0);
                renderer.GetPropertyBlock(Applied, 0);
            }
            internal void Restore()
            {
                if (Renderer != null) Renderer.SetPropertyBlock(Before.isEmpty ? null : Before, 0);
                Renderer = null;
            }
        }

        private sealed class IrisRenderBinding
        {
            internal readonly Renderer Renderer;
            private readonly EyeLookMaterialControll _controller;
            private readonly List<string> _textureNames = new List<string>();
            private readonly List<int> _stIds = new List<int>();
            private readonly List<Vector4> _nativeSt = new List<Vector4>();
            private readonly List<Vector4> _expectedSt = new List<Vector4>();
            private readonly MaterialPropertyBlock _global = new MaterialPropertyBlock();
            private readonly MaterialPropertyBlock _slot = new MaterialPropertyBlock();
            private Material _material;
            internal IrisRenderBinding(EyeLookMaterialControll controller)
            {
                _controller = controller;
                Renderer = controller._renderer;
            }
            internal void CaptureNativeTextureTransforms()
            {
                _textureNames.Clear(); _stIds.Clear(); _nativeSt.Clear(); _expectedSt.Clear();
                if (Renderer == null || (_material = Renderer.sharedMaterial) == null) return;
                _global.Clear(); _slot.Clear();
                Renderer.GetPropertyBlock(_global);
                Renderer.GetPropertyBlock(_slot, 0);
                if (_controller.texStates != null)
                    foreach (var state in _controller.texStates)
                        if (state != null) AddTexture(state.texName);
                if (_textureNames.Count == 0) AddTexture("_MainTex");
            }
            private void AddTexture(string name)
            {
                if (string.IsNullOrEmpty(name) || !_material.HasProperty(name) || _textureNames.Contains(name)) return;
                int id = Shader.PropertyToID(name + "_ST");
                Vector2 scale = _material.GetTextureScale(name), offset = _material.GetTextureOffset(name);
                Vector4 st = new Vector4(scale.x, scale.y, offset.x, offset.y);
                Vector4 global = _global.GetVector(id), slot = _slot.GetVector(id);
                if (global != Vector4.zero) st = global;
                if (slot != Vector4.zero) st = slot;
                _textureNames.Add(name); _stIds.Add(id); _nativeSt.Add(st); _expectedSt.Add(st);
            }
            internal bool Apply(IrisRenderSnapshot snapshot, float inwardUv, float upwardUv)
            {
                if (Renderer == null || _material == null || _stIds.Count == 0) return false;
                snapshot.Capture(Renderer);
                // Moving a texture's UV offset shifts the visible iris in the
                // opposite direction. Anatomical left must use positive UV X
                // to move the iris toward the face center; right uses negative.
                float inwardSign = _controller.eyeLR == EYE_LR.EYE_L ? 1f : -1f;
                float upSign = _controller.UpWait <= _controller.DownWait ? -1f : 1f;
                for (int i = 0; i < _stIds.Count; i++)
                {
                    Vector4 st = _nativeSt[i];
                    st.z += inwardUv * inwardSign;
                    st.w += upwardUv * upSign;
                    _expectedSt[i] = st;
                    snapshot.Applied.SetVector(_stIds[i], st);
                }
                Renderer.SetPropertyBlock(snapshot.Applied, 0);
                return true;
            }
            internal string Describe()
            {
                if (Renderer == null) return "renderer=missing";
                _slot.Clear(); Renderer.GetPropertyBlock(_slot, 0);
                string st = _stIds.Count == 0 ? "textureST=missing" : "texture=" + _textureNames[0]
                    + " nativeST=" + _nativeSt[0].ToString("F4") + " expectedST=" + _expectedSt[0].ToString("F4")
                    + " readbackST=" + _slot.GetVector(_stIds[0]).ToString("F4");
                return "side=" + _controller.eyeLR + " renderer=" + Renderer.name
                    + " rendererEnabled=" + Renderer.enabled + " active=" + Renderer.gameObject.activeInHierarchy
                    + " material=" + _material?.name + " shader=" + _material?.shader?.name
                    + " insideWait=" + _controller.InsideWait + " outsideWait=" + _controller.OutsideWait + " " + st;
            }
        }

        private static bool TryResolveOgazmEyes(ChaControl female, out Transform left, out Transform right)
        {
            left = null; right = null;
            var calc = female != null && female.eyeLookCtrl != null ? female.eyeLookCtrl.eyeLookScript : null;
            var eyes = calc != null ? calc.eyeObjs : null;
            if (eyes == null) return false;
            foreach (var eye in eyes)
            {
                if (eye == null || eye.eyeTransform == null) continue;
                if (eye.eyeLR == EYE_LR.EYE_L) left = eye.eyeTransform;
                else if (eye.eyeLR == EYE_LR.EYE_R) right = eye.eyeTransform;
            }
            return left != null && right != null;
        }
    }
}
