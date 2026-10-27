using System.Collections.Generic;
using UnityEngine;

namespace AstralMode
{
    public sealed class FrozenMaterialOwner : MonoBehaviour
    {
        internal readonly List<Material> Materials = new List<Material>();
        private void OnDestroy()
        {
            foreach (var material in Materials)
                if (material != null) Object.Destroy(material);
            Materials.Clear();
        }
    }
}
