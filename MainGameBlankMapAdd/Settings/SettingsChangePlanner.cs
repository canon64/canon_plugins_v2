using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

namespace MainGameBlankMapAdd.Settings
{
    internal sealed class SettingsSnapshot
    {
        private readonly Dictionary<string, string> _values;

        private SettingsSnapshot(Dictionary<string, string> values)
        {
            _values = values;
        }

        internal static SettingsSnapshot Capture(PluginSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            FieldInfo[] fields = typeof(PluginSettings).GetFields(BindingFlags.Instance | BindingFlags.Public);
            foreach (FieldInfo field in fields.OrderBy(GetDataMemberOrder).ThenBy(f => f.Name, StringComparer.Ordinal))
            {
                if (field.GetCustomAttribute<DataMemberAttribute>() == null) continue;
                values[field.Name] = NormalizeValue(field.GetValue(settings));
            }
            return new SettingsSnapshot(values);
        }

        internal IReadOnlyList<string> GetChangedFields(SettingsSnapshot next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            var names = new SortedSet<string>(_values.Keys, StringComparer.Ordinal);
            names.UnionWith(next._values.Keys);
            return names.Where(name =>
            {
                _values.TryGetValue(name, out string left);
                next._values.TryGetValue(name, out string right);
                return !string.Equals(left, right, StringComparison.Ordinal);
            }).ToArray();
        }

        private static int GetDataMemberOrder(FieldInfo field)
        {
            return field.GetCustomAttribute<DataMemberAttribute>()?.Order ?? int.MaxValue;
        }

        private static string NormalizeValue(object value)
        {
            if (value == null) return "<null>";
            if (value is string text) return "s:" + text;
            if (value is IEnumerable sequence)
            {
                var items = new List<string>();
                foreach (object item in sequence)
                    items.Add(NormalizeValue(item));
                return "l:" + string.Join("\u001f", items);
            }
            if (value is IFormattable formattable)
                return "v:" + formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture);
            return "o:" + value;
        }
    }

    internal sealed class SettingsChangePlan
    {
        internal SettingsChangePlan(
            IReadOnlyList<string> changedFields,
            bool requiresRoomRebuild,
            bool isReverbOnly)
        {
            ChangedFields = changedFields ?? Array.Empty<string>();
            RequiresRoomRebuild = requiresRoomRebuild;
            IsReverbOnly = isReverbOnly;
        }

        internal IReadOnlyList<string> ChangedFields { get; }
        internal bool HasChanges => ChangedFields.Count > 0;
        internal bool RequiresRoomRebuild { get; }
        internal bool IsReverbOnly { get; }
    }

    internal static class SettingsChangePlanner
    {
        private static readonly HashSet<string> ReverbOnlyFields = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(PluginSettings.ApplyReverbToVideoAudio),
            nameof(PluginSettings.EnableVoiceReverb),
            nameof(PluginSettings.VoiceReverbPreset)
        };

        private static readonly HashSet<string> RuntimeOnlyFields = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(PluginSettings.EnableAudioDiagnosticsLog),
            nameof(PluginSettings.VerboseLog)
        };

        internal static SettingsChangePlan Create(SettingsSnapshot before, SettingsSnapshot after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));
            IReadOnlyList<string> changed = before.GetChangedFields(after);
            bool rebuild = changed.Any(name =>
                !ReverbOnlyFields.Contains(name) && !RuntimeOnlyFields.Contains(name));
            bool reverbOnly = changed.Count > 0 && changed.All(ReverbOnlyFields.Contains);
            return new SettingsChangePlan(changed, rebuild, reverbOnly);
        }
    }
}
