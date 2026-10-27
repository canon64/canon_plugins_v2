using UnityEngine;

namespace AstralMode
{
    // メインカメラのTransformを乗っ取って、指定中心をオービットするMonoBehaviour。
    // CameraControl 等の本体側スクリプトと競合するため、付加中は本体側を無効化する想定。
    internal sealed class OrbitCamera : MonoBehaviour
    {
        public Vector3 Center;
        public float Radius = 2.5f;
        public float Height = 0.4f;
        public float DegreesPerSecond = 18f;
        public float CurrentAngleDeg = 0f;
        public bool LookAtCenter = true;

        // 元のカメラ状態(復元用)
        private Vector3 _origLocalPos;
        private Quaternion _origLocalRot;
        private Transform _origParent;
        private bool _origStateCaptured;

        // 競合する本体側コンポーネントを無効化
        private Behaviour[] _disabledOthers;

        internal void Capture()
        {
            if (_origStateCaptured) return;
            _origLocalPos = transform.localPosition;
            _origLocalRot = transform.localRotation;
            _origParent = transform.parent;
            _origStateCaptured = true;

            // 他の Camera 制御スクリプトを止める
            var behaviours = GetComponents<Behaviour>();
            var disabled = new System.Collections.Generic.List<Behaviour>();
            foreach (var b in behaviours)
            {
                if (b == null || b == this) continue;
                if (b is Camera) continue;
                if (b is AudioListener) continue;
                if (b.enabled)
                {
                    b.enabled = false;
                    disabled.Add(b);
                }
            }
            _disabledOthers = disabled.ToArray();
        }

        internal void Restore()
        {
            if (!_origStateCaptured) return;
            transform.SetParent(_origParent, false);
            transform.localPosition = _origLocalPos;
            transform.localRotation = _origLocalRot;
            _origStateCaptured = false;

            if (_disabledOthers != null)
            {
                foreach (var b in _disabledOthers)
                {
                    if (b != null) b.enabled = true;
                }
                _disabledOthers = null;
            }
        }

        private void LateUpdate()
        {
            CurrentAngleDeg += DegreesPerSecond * Time.unscaledDeltaTime;
            if (CurrentAngleDeg > 360f) CurrentAngleDeg -= 360f;

            float rad = CurrentAngleDeg * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(Mathf.Sin(rad) * Radius, Height, Mathf.Cos(rad) * Radius);
            transform.position = Center + offset;

            if (LookAtCenter)
            {
                Vector3 look = Center - transform.position;
                if (look.sqrMagnitude > 1e-6f)
                {
                    transform.rotation = Quaternion.LookRotation(look, Vector3.up);
                }
            }
        }
    }
}
