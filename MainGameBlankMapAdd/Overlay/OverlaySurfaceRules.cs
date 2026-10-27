using System;
using System.Collections.Generic;
using System.Linq;

namespace MainGameBlankMapAdd.Overlay
{
    internal static class OverlaySurfaceRules
    {
        internal static readonly string[] LogicalFaces = { "Front", "Back", "Left", "Right", "Floor", "Ceiling" };

        internal static HashSet<string> Parse(string raw)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw)) return result;
            foreach (string tokenValue in raw.Split(',', ';', '|', ' '))
            {
                string token = tokenValue.Trim();
                if (string.Equals(token, "All", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string face in LogicalFaces) result.Add(face);
                    return result;
                }
                if (string.Equals(token, "None", StringComparison.OrdinalIgnoreCase))
                {
                    result.Clear();
                    continue;
                }
                string match = LogicalFaces.FirstOrDefault(face => string.Equals(face, token, StringComparison.OrdinalIgnoreCase));
                if (match != null) result.Add(match);
            }
            return result;
        }

        internal static string Normalize(string raw)
        {
            if (string.Equals(raw, "All", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(raw)
                || string.Equals(raw, "None", StringComparison.OrdinalIgnoreCase))
                return "All";
            HashSet<string> selected = Parse(raw);
            if (selected.Count == 0 || LogicalFaces.All(selected.Contains)) return "All";
            return string.Join(",", LogicalFaces.Where(selected.Contains));
        }

        internal static string Serialize(HashSet<string> selected)
        {
            if (selected == null || selected.Count == 0) return "None";
            if (LogicalFaces.All(selected.Contains)) return "All";
            return string.Join(",", LogicalFaces.Where(selected.Contains));
        }

        internal static string NormalizeTileMode(string raw)
        {
            return string.Equals(raw, "tile", StringComparison.OrdinalIgnoreCase) ? "tile" : "single";
        }
    }
}
