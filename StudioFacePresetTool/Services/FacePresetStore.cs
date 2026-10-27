using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace StudioFacePresetTool.Services
{
    internal sealed class FacePresetStore
    {
        internal const string FileName = "StudioFacePresets.json";
        private const string LegacyFileName = "StudioFacePresetTool.json";

        private readonly string _path;
        private readonly string _legacyPath;
        private readonly PluginLogger _log;
        private readonly UTF8Encoding _utf8NoBom = new UTF8Encoding(false);

        internal FacePresetStore(string pluginDir, PluginLogger log)
        {
            _path = Path.Combine(pluginDir, FileName);
            _legacyPath = Path.Combine(pluginDir, LegacyFileName);
            _log = log;
        }

        internal List<FacePreset> LoadOrCreate()
        {
            _log.Entry(nameof(LoadOrCreate));
            try
            {
                _log.Info("preset-load/start path=" + _path);
                bool exists = File.Exists(_path);
                _log.Branch("preset-file-exists", exists, _path);
                if (!exists)
                {
                    bool imported = TryLoadLegacy(out List<FacePreset> legacyPresets);
                    _log.Branch("preset-load-legacy-when-missing", imported, "count=" + legacyPresets.Count);
                    if (imported)
                    {
                        if (Save(legacyPresets, out string saveError))
                            _log.Info("preset file created from legacy count=" + legacyPresets.Count);
                        else
                            _log.Error("preset file create from legacy failed reason=" + saveError + " path=" + _path);
                        return legacyPresets;
                    }

                    if (Save(new List<FacePreset>(), out string saveEmptyError))
                        _log.Info("preset file created empty path=" + _path);
                    else
                        _log.Error("preset file create empty failed reason=" + saveEmptyError + " path=" + _path);
                    return new List<FacePreset>();
                }

                string json = File.ReadAllText(_path, Encoding.UTF8);
                _log.Info("preset-load/read result=ok len=" + (json == null ? 0 : json.Length));
                bool empty = string.IsNullOrWhiteSpace(json);
                _log.Branch("preset-json-empty", empty, "len=" + (json == null ? 0 : json.Length));
                if (empty)
                {
                    bool imported = TryLoadLegacy(out List<FacePreset> legacyPresets);
                    _log.Branch("preset-load-legacy-when-empty", imported, "count=" + legacyPresets.Count);
                    if (imported)
                    {
                        if (Save(legacyPresets, out string saveError))
                            _log.Info("preset empty replaced by legacy count=" + legacyPresets.Count);
                        else
                            _log.Error("preset empty replace by legacy failed reason=" + saveError + " path=" + _path);
                        return legacyPresets;
                    }

                    return new List<FacePreset>();
                }

                bool modernOk = TryParseModern(json, out List<FacePreset> modernPresets, out bool hasModernSchema);
                _log.Info("preset-load/parse-modern result=" + (modernOk ? "ok" : "ng")
                    + " schema=" + hasModernSchema
                    + " count=" + modernPresets.Count);
                if (modernOk)
                {
                    _log.Info("presets loaded modern count=" + modernPresets.Count);
                    return modernPresets;
                }

                List<FacePreset> legacyFallback = new List<FacePreset>();
                bool importedFallback = false;
                if (!hasModernSchema)
                {
                    importedFallback = TryLoadLegacy(out legacyFallback);
                }
                _log.Branch("preset-modern-invalid-fallback-legacy", importedFallback, "schema=" + hasModernSchema + " count=" + legacyFallback.Count);
                if (importedFallback)
                {
                    if (Save(legacyFallback, out string saveError))
                        _log.Info("preset migrated from legacy after invalid modern json count=" + legacyFallback.Count);
                    else
                        _log.Error("preset migrate from legacy failed reason=" + saveError + " path=" + _path);
                    return legacyFallback;
                }

                _log.Warn("preset load failed: modern json invalid and legacy fallback unavailable");
                return new List<FacePreset>();
            }
            catch (Exception ex)
            {
                _log.Error("preset load failed", ex);
                return new List<FacePreset>();
            }
        }

        internal bool Save(List<FacePreset> presets, out string failureReason)
        {
            _log.Entry(nameof(Save));
            failureReason = string.Empty;
            try
            {
                List<FacePreset> source = presets ?? new List<FacePreset>();
                _log.Info("preset-save/start count=" + source.Count + " path=" + _path);

                List<FacePreset> normalized = source.Select(NormalizePreset).ToList();
                _log.Info("preset-save/normalize result=ok count=" + normalized.Count);

                string json = BuildModernJson(normalized);
                bool serialized = !string.IsNullOrWhiteSpace(json);
                bool hasPresetsKey = serialized && json.IndexOf("\"Presets\"", StringComparison.Ordinal) >= 0;
                _log.Info("preset-save/serialize result=" + (serialized && hasPresetsKey ? "ok" : "ng")
                    + " len=" + (json == null ? 0 : json.Length)
                    + " hasPresetsKey=" + hasPresetsKey
                    + " sample=" + Compact(json, 160));
                if (!serialized)
                {
                    failureReason = "serialize returned empty json";
                    _log.Error("preset-save/serialize result=ng reason=" + failureReason + " path=" + _path);
                    return false;
                }

                if (!hasPresetsKey)
                {
                    failureReason = "serialize missing Presets key";
                    _log.Error("preset-save/serialize result=ng reason=" + failureReason + " path=" + _path);
                    return false;
                }

                string dir = Path.GetDirectoryName(_path) ?? string.Empty;
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                    _log.Info("preset-save/dir result=created path=" + dir);
                }
                else
                {
                    _log.Info("preset-save/dir result=ok path=" + dir);
                }

                File.WriteAllText(_path, json, _utf8NoBom);
                long bytes = -1;
                try
                {
                    bytes = new FileInfo(_path).Length;
                }
                catch
                {
                    // ignore
                }
                _log.Info("preset-save/write result=ok bytes=" + bytes + " path=" + _path);

                string verifyJson = File.ReadAllText(_path, Encoding.UTF8);
                bool verifyParsed = TryParseModern(verifyJson, out List<FacePreset> verifyPresets, out bool hasVerifySchema);
                _log.Info("preset-save/verify-parse result=" + (verifyParsed ? "ok" : "ng")
                    + " schema=" + hasVerifySchema
                    + " count=" + verifyPresets.Count);
                if (!verifyParsed)
                {
                    failureReason = "verify parse failed after write";
                    _log.Error("preset-save/verify result=ng reason=" + failureReason + " path=" + _path);
                    return false;
                }

                if (verifyPresets.Count != normalized.Count)
                {
                    failureReason = "verify count mismatch expected=" + normalized.Count + " actual=" + verifyPresets.Count;
                    _log.Error("preset-save/verify result=ng reason=" + failureReason + " path=" + _path);
                    return false;
                }

                _log.Info("preset-save/result=ok count=" + normalized.Count + " bytes=" + bytes + " path=" + _path);
                return true;
            }
            catch (Exception ex)
            {
                failureReason = ex.Message ?? "unknown exception";
                _log.Error("preset-save/result=ng reason=" + failureReason + " path=" + _path, ex);
                return false;
            }
        }

        private static string Compact(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            string single = text.Replace("\r", "\\r").Replace("\n", "\\n");
            if (single.Length <= maxLen)
                return single;

            return single.Substring(0, maxLen) + "...";
        }

        private bool TryParseModern(string json, out List<FacePreset> presets, out bool hasModernSchema)
        {
            _log.Entry(nameof(TryParseModern));
            presets = new List<FacePreset>();
            hasModernSchema = false;
            if (string.IsNullOrWhiteSpace(json))
                return false;

            object rootObj = MiniJson.Deserialize(json);
            if (!(rootObj is Dictionary<string, object> root))
                return false;

            hasModernSchema = root.TryGetValue("Presets", out object presetsObj);
            if (!hasModernSchema)
                return false;

            if (!(presetsObj is List<object> items))
                return false;

            for (int i = 0; i < items.Count; i++)
            {
                if (!(items[i] is Dictionary<string, object> dict))
                    continue;

                FacePreset preset = ParsePresetDict(dict, "preset-" + i);
                presets.Add(NormalizePreset(preset));
            }

            return true;
        }

        private bool TryLoadLegacy(out List<FacePreset> presets)
        {
            _log.Entry(nameof(TryLoadLegacy));
            presets = new List<FacePreset>();

            bool legacyExists = File.Exists(_legacyPath);
            _log.Branch("preset-legacy-exists", legacyExists, _legacyPath);
            if (!legacyExists)
                return false;

            try
            {
                string json = File.ReadAllText(_legacyPath, Encoding.UTF8);
                bool empty = string.IsNullOrWhiteSpace(json);
                _log.Branch("preset-legacy-json-empty", empty, "len=" + (json == null ? 0 : json.Length));
                if (empty)
                    return false;

                object rootObj = MiniJson.Deserialize(json);
                bool rootIsDict = rootObj is Dictionary<string, object>;
                _log.Branch("preset-legacy-root-dict", rootIsDict, string.Empty);
                if (!rootIsDict)
                    return false;

                var root = (Dictionary<string, object>)rootObj;
                bool hasItems = root.TryGetValue("Items", out object itemsObj) && itemsObj is List<object>;
                _log.Branch("preset-legacy-has-items", hasItems, string.Empty);
                if (!hasItems)
                    return false;

                var items = (List<object>)itemsObj;
                for (int i = 0; i < items.Count; i++)
                {
                    bool isDict = items[i] is Dictionary<string, object>;
                    _log.Branch("preset-legacy-item-dict", isDict, "index=" + i);
                    if (!isDict)
                        continue;

                    var dict = (Dictionary<string, object>)items[i];
                    FacePreset preset = ParsePresetDict(dict, "legacy-" + i);
                    presets.Add(NormalizePreset(preset));
                }

                bool hasAny = presets.Count > 0;
                _log.Branch("preset-legacy-imported-any", hasAny, "count=" + presets.Count);
                return hasAny;
            }
            catch (Exception ex)
            {
                _log.Error("legacy import failed", ex);
                presets = new List<FacePreset>();
                return false;
            }
        }

        private FacePreset NormalizePreset(FacePreset preset)
        {
            _log.Entry(nameof(NormalizePreset));
            if (preset == null)
            {
                _log.Branch("preset-null", true, string.Empty);
                preset = new FacePreset();
            }
            else
            {
                _log.Branch("preset-null", false, preset.Name ?? string.Empty);
            }

            preset.Id = string.IsNullOrWhiteSpace(preset.Id)
                ? Guid.NewGuid().ToString("N")
                : preset.Id.Trim();

            preset.Name = string.IsNullOrWhiteSpace(preset.Name)
                ? "preset-" + preset.Id.Substring(0, Math.Min(8, preset.Id.Length))
                : preset.Name.Trim();

            preset.Eyebrow = Mathf.Max(0, preset.Eyebrow);
            preset.Eye = Mathf.Max(0, preset.Eye);
            preset.Mouth = Mathf.Max(0, preset.Mouth);
            preset.EyeMin = Clamp01Round2(preset.EyeMin);
            preset.EyeMax = Clamp01Round2(Mathf.Max(preset.EyeMin, preset.EyeMax));
            preset.MouthMin = Clamp01Round2(preset.MouthMin);
            preset.MouthMax = Clamp01Round2(Mathf.Max(preset.MouthMin, preset.MouthMax));
            preset.Tears = Mathf.Clamp(preset.Tears, 0, 10);
            preset.Cheek = Clamp01Round2(preset.Cheek);

            return preset;
        }

        private static FacePreset ParsePresetDict(Dictionary<string, object> dict, string fallbackName)
        {
            return new FacePreset
            {
                Id = GetString(dict, "Id", Guid.NewGuid().ToString("N")),
                Name = GetString(dict, "Name", fallbackName),
                Eyebrow = GetInt(dict, "Eyebrow", 0),
                Eye = GetInt(dict, "Eye", 0),
                Mouth = GetInt(dict, "Mouth", 0),
                EyeMin = GetFloat(dict, "EyeMin", 0f),
                EyeMax = GetFloat(dict, "EyeMax", 1f),
                MouthMin = GetFloat(dict, "MouthMin", 0f),
                MouthMax = GetFloat(dict, "MouthMax", 1f),
                Tears = GetInt(dict, "Tears", 0),
                Cheek = GetFloat(dict, "Cheek", 0f)
            };
        }

        private static string BuildModernJson(IReadOnlyList<FacePreset> presets)
        {
            var sb = new StringBuilder(256 + (presets.Count * 192));
            sb.Append("{\"Presets\":[");
            for (int i = 0; i < presets.Count; i++)
            {
                FacePreset p = presets[i] ?? new FacePreset();
                if (i > 0)
                    sb.Append(',');

                bool first = true;
                sb.Append('{');
                AppendStringField(sb, "Id", p.Id, ref first);
                AppendStringField(sb, "Name", p.Name, ref first);
                AppendIntField(sb, "Eyebrow", p.Eyebrow, ref first);
                AppendIntField(sb, "Eye", p.Eye, ref first);
                AppendIntField(sb, "Mouth", p.Mouth, ref first);
                AppendFloatField(sb, "EyeMin", p.EyeMin, ref first);
                AppendFloatField(sb, "EyeMax", p.EyeMax, ref first);
                AppendFloatField(sb, "MouthMin", p.MouthMin, ref first);
                AppendFloatField(sb, "MouthMax", p.MouthMax, ref first);
                AppendIntField(sb, "Tears", p.Tears, ref first);
                AppendFloatField(sb, "Cheek", p.Cheek, ref first);
                sb.Append('}');
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static void AppendStringField(StringBuilder sb, string name, string value, ref bool first)
        {
            AppendFieldPrefix(sb, name, ref first);
            sb.Append('\"');
            sb.Append(EscapeJsonString(value ?? string.Empty));
            sb.Append('\"');
        }

        private static void AppendIntField(StringBuilder sb, string name, int value, ref bool first)
        {
            AppendFieldPrefix(sb, name, ref first);
            sb.Append(value.ToString(CultureInfo.InvariantCulture));
        }

        private static void AppendFloatField(StringBuilder sb, string name, float value, ref bool first)
        {
            AppendFieldPrefix(sb, name, ref first);
            sb.Append(value.ToString("0.##", CultureInfo.InvariantCulture));
        }

        private static void AppendFieldPrefix(StringBuilder sb, string name, ref bool first)
        {
            if (!first)
                sb.Append(',');

            first = false;
            sb.Append('\"');
            sb.Append(name);
            sb.Append("\":");
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }

            return sb.ToString();
        }

        private static string GetString(Dictionary<string, object> dict, string key, string fallback)
        {
            if (dict == null || !dict.TryGetValue(key, out object raw) || raw == null)
                return fallback;

            string value = raw.ToString();
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static int GetInt(Dictionary<string, object> dict, string key, int fallback)
        {
            if (dict == null || !dict.TryGetValue(key, out object raw) || raw == null)
                return fallback;

            try
            {
                if (raw is long l) return Convert.ToInt32(l);
                if (raw is int i) return i;
                if (raw is double d) return Convert.ToInt32(Math.Round(d, MidpointRounding.AwayFromZero));
                if (raw is float f) return Convert.ToInt32(Math.Round(f, MidpointRounding.AwayFromZero));

                string text = raw.ToString();
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedInt))
                    return parsedInt;

                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedDouble))
                    return Convert.ToInt32(Math.Round(parsedDouble, MidpointRounding.AwayFromZero));
            }
            catch
            {
                // ignore
            }

            return fallback;
        }

        private static float GetFloat(Dictionary<string, object> dict, string key, float fallback)
        {
            if (dict == null || !dict.TryGetValue(key, out object raw) || raw == null)
                return fallback;

            try
            {
                if (raw is float f) return f;
                if (raw is double d) return (float)d;
                if (raw is long l) return l;
                if (raw is int i) return i;

                string text = raw.ToString();
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                    return parsed;
            }
            catch
            {
                // ignore
            }

            return fallback;
        }

        private static float Clamp01Round2(float value)
        {
            float clamped = Mathf.Clamp01(value);
            return (float)Math.Round(clamped, 2, MidpointRounding.AwayFromZero);
        }
    }
}
