using System;
using System.Text;

namespace MainGameBlankMapAdd.Media
{
    internal static class VideoPathRules
    {
        internal static bool IsStreamUrl(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return path.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("rtsps://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("rtmp://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        internal static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string normalized = value.Trim();
            while (normalized.Length >= 2)
            {
                char first = normalized[0];
                char last = normalized[normalized.Length - 1];
                if (!((first == '"' && last == '"') || (first == '\'' && last == '\''))) break;
                normalized = normalized.Substring(1, normalized.Length - 2).Trim();
            }
            return RestoreEscapedControlCharacters(normalized);
        }

        private static string RestoreEscapedControlCharacters(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var builder = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\b': builder.Append("\\b"); break;
                    case '\t': builder.Append("\\t"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\v': builder.Append("\\v"); break;
                    case '\0': builder.Append("\\0"); break;
                    default: if (!char.IsControl(c)) builder.Append(c); break;
                }
            }
            return builder.ToString();
        }
    }
}
