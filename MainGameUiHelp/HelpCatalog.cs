using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MainGameUiHelp
{
    [Serializable]
    public sealed class HelpCatalogFile
    {
        public HelpEntry[] entries;
    }

    [Serializable]
    public sealed class HelpEntry
    {
        public string owner;
        public string label;
        public string text;
    }

    // No Unity calls: matching and row association are also checked outside the game.
    internal sealed class HelpCatalog
    {
        private readonly Dictionary<string, string> _entries = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Regex RichText = new Regex("<[^>]*>", RegexOptions.Compiled);
        private static readonly Regex Spaces = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex ValueSuffix = new Regex(@"\s*[:：=]\s*[-+]?\d+(?:\.\d+)?\s*(?:%|cm|mm|m|Hz|°|秒)?\s*$", RegexOptions.Compiled);

        internal int Count => _entries.Count;

        internal void Add(string owner, string label, string text)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(text)) return;
            _entries[Key(owner, Normalize(label))] = text;
        }

        internal string Find(string owner, string label)
        {
            string normalized = Normalize(label);
            if (_entries.TryGetValue(Key(owner, normalized), out string text)) return text;
            if (_entries.TryGetValue(Key("*", normalized), out text)) return text;
            string withoutValue = ValueSuffix.Replace(normalized, "");
            if (_entries.TryGetValue(Key(owner, withoutValue), out text)) return text;
            if (_entries.TryGetValue(Key("*", withoutValue), out text)) return text;
            return null;
        }

        internal static string Normalize(string value)
        {
            return Spaces.Replace(RichText.Replace(value ?? "", ""), " ").Trim().TrimEnd(':', '：');
        }

        internal static string Range(float value, float min, float max)
        {
            return "現在: " + Number(value) + "　設定範囲: " + Number(min) + " ～ " + Number(max);
        }

        private static string Number(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        private static string Key(string owner, string label) => (owner ?? "*") + "\n" + label;

        internal static bool IsSameRow(float labelLeft, float labelRight, float labelTop,
            float labelBottom, float fieldLeft, float fieldTop, float fieldBottom)
        {
            return fieldLeft >= labelRight - 4f && fieldLeft - labelRight < 900f
                && Math.Min(labelBottom, fieldBottom) > Math.Max(labelTop, fieldTop);
        }
    }
}
