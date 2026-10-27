using System.Collections.Generic;
using UnityEngine;

namespace UnityResourceDiagnostics
{
    internal static class TextUtil
    {
        public static string NormalizeName(string name)
        {
            return string.IsNullOrEmpty(name) ? "(empty)" : name;
        }

        public static string FormatBounds(Bounds bounds)
        {
            Vector3 size = bounds.size;
            Vector3 center = bounds.center;
            return string.Format(
                "center=({0:F2},{1:F2},{2:F2}) size=({3:F2},{4:F2},{5:F2})",
                center.x,
                center.y,
                center.z,
                size.x,
                size.y,
                size.z);
        }
    }

    internal static class PathUtil
    {
        public static string GetPath(GameObject go)
        {
            if (go == null)
                return "(null)";

            var names = new List<string>(8);
            Transform current = go.transform;
            while (current != null)
            {
                names.Add(TextUtil.NormalizeName(current.name));
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names.ToArray());
        }
    }
}
