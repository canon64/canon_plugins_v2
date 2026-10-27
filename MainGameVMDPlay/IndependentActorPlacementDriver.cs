using UnityEngine;

namespace MainGameVMDPlay {
    // This component exists only on the actor created and owned by this plugin.
    // The stage owns placement; external world-space writes must not cancel it.
    [DefaultExecutionOrder(-31000)]
    public sealed class IndependentActorPlacementDriver : MonoBehaviour {
        internal Transform Stage;
        private void Awake() {
            gameObject.AddComponent<IndependentActorPlacementLateDriver>().Owner = this;
        }
        internal void Apply() {
            if (Stage == null) return;
            if (transform.parent != Stage) transform.SetParent(Stage, false);
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }
        private void Update() { Apply(); }
    }
    [DefaultExecutionOrder(29000)]
    public sealed class IndependentActorPlacementLateDriver : MonoBehaviour {
        internal IndependentActorPlacementDriver Owner;
        private void LateUpdate() { if (Owner != null && Owner.enabled) Owner.Apply(); }
    }
}
