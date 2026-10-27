using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    internal static class CommandParser
    {
        internal static bool TryParseIncoming(
            string line,
            PluginSettings settings,
            out ExternalVoiceFaceCommand command,
            out string reason)
        {
            command = null;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(line))
            {
                reason = "empty line";
                return false;
            }

            string trimmed = line.Trim();
            var s = settings ?? new PluginSettings();

            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                if (!s.AcceptJsonCommand)
                {
                    reason = "json command disabled";
                    return false;
                }

                try
                {
                    command = JsonUtility.FromJson<ExternalVoiceFaceCommand>(trimmed);
                }
                catch (Exception ex)
                {
                    reason = "json parse failed: " + ex.Message;
                    return false;
                }

                if (command == null)
                {
                    reason = "json parse returned null";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(command.type))
                {
                    command.type = "speak";
                }

                RecoverSequenceItemsIfNeeded(trimmed, command);

                return true;
            }

            if (s.AcceptPlainAudioPath)
            {
                command = ExternalVoiceFaceCommand.FromPlainAudioPath(trimmed);
                return true;
            }

            if (!s.AcceptPlainAssetName)
            {
                reason = "plain command disabled";
                return false;
            }

            command = ExternalVoiceFaceCommand.FromPlainAssetName(trimmed);
            return true;
        }

        internal static string NormalizePipeName(string pipeName)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
            {
                return "kks_voice_face_events";
            }

            string value = pipeName.Trim();
            if (value.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(@"\\.\pipe\".Length);
            }

            if (string.Equals(value, "kks_voice_face_events_diag_0423", StringComparison.OrdinalIgnoreCase))
            {
                return "kks_voice_face_events";
            }

            return value;
        }

        private static void RecoverSequenceItemsIfNeeded(string json, ExternalVoiceFaceCommand command)
        {
            if (command == null
                || (
                    !string.Equals(command.type, "speak_sequence", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(command.type, "speak_sequence_append", StringComparison.OrdinalIgnoreCase)
                )
                || (command.items != null && command.items.Length > 0))
            {
                return;
            }

            ExternalVoiceSequenceItem[] recoveredItems;
            if (TryRecoverSequenceItems(json, out recoveredItems) && recoveredItems.Length > 0)
            {
                command.items = recoveredItems;
            }
        }

        private static bool TryRecoverSequenceItems(string json, out ExternalVoiceSequenceItem[] items)
        {
            items = new ExternalVoiceSequenceItem[0];
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            int itemsValueStart;
            if (!TryFindPropertyValue(json, "items", 0, json.Length, out itemsValueStart)
                || itemsValueStart >= json.Length
                || json[itemsValueStart] != '[')
            {
                return false;
            }

            int itemsEnd = FindMatching(json, itemsValueStart, '[', ']');
            if (itemsEnd < 0)
            {
                return false;
            }

            var results = new List<ExternalVoiceSequenceItem>();
            int pos = itemsValueStart + 1;
            while (pos < itemsEnd)
            {
                pos = SkipWhitespaceAndCommas(json, pos, itemsEnd);
                if (pos >= itemsEnd)
                {
                    break;
                }

                if (json[pos] != '{')
                {
                    pos++;
                    continue;
                }

                int objectEnd = FindMatching(json, pos, '{', '}');
                if (objectEnd < 0 || objectEnd > itemsEnd)
                {
                    break;
                }

                ExternalVoiceSequenceItem item = ParseSequenceItem(json, pos, objectEnd);
                if (item != null)
                {
                    results.Add(item);
                }

                pos = objectEnd + 1;
            }

            items = results.ToArray();
            return items.Length > 0;
        }

        private static ExternalVoiceSequenceItem ParseSequenceItem(string json, int start, int end)
        {
            var item = new ExternalVoiceSequenceItem();

            int intValue;
            float floatValue;
            string stringValue;

            if (TryReadIntProperty(json, start + 1, end, "index", out intValue))
            {
                item.index = intValue;
            }

            if (TryReadStringProperty(json, start + 1, end, "audioPath", out stringValue))
            {
                item.audioPath = stringValue;
            }

            if (TryReadStringProperty(json, start + 1, end, "path", out stringValue))
            {
                item.path = stringValue;
            }

            if (TryReadStringProperty(json, start + 1, end, "subtitle", out stringValue))
            {
                item.subtitle = stringValue;
            }

            if (TryReadStringProperty(json, start + 1, end, "text", out stringValue))
            {
                item.text = stringValue;
            }

            if (TryReadFloatProperty(json, start + 1, end, "durationSeconds", out floatValue))
            {
                item.durationSeconds = floatValue;
            }

            if (TryReadFloatProperty(json, start + 1, end, "holdSeconds", out floatValue))
            {
                item.holdSeconds = floatValue;
            }

            if (TryReadIntProperty(json, start + 1, end, "deleteAfterPlay", out intValue))
            {
                item.deleteAfterPlay = intValue;
            }

            if (string.IsNullOrWhiteSpace(item.ResolveAudioPath()))
            {
                return null;
            }

            return item;
        }

        private static bool TryReadStringProperty(string json, int start, int end, string name, out string value)
        {
            value = string.Empty;
            int valueStart;
            if (!TryFindPropertyValue(json, name, start, end, out valueStart)
                || valueStart >= end
                || json[valueStart] != '"')
            {
                return false;
            }

            int valueEnd = FindStringEnd(json, valueStart);
            if (valueEnd < 0 || valueEnd > end)
            {
                return false;
            }

            value = DecodeJsonString(json, valueStart + 1, valueEnd);
            return true;
        }

        private static bool TryReadIntProperty(string json, int start, int end, string name, out int value)
        {
            value = 0;
            int valueStart;
            if (!TryFindPropertyValue(json, name, start, end, out valueStart))
            {
                return false;
            }

            string token = ReadValueToken(json, valueStart, end);
            return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryReadFloatProperty(string json, int start, int end, string name, out float value)
        {
            value = 0f;
            int valueStart;
            if (!TryFindPropertyValue(json, name, start, end, out valueStart))
            {
                return false;
            }

            string token = ReadValueToken(json, valueStart, end);
            return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryFindPropertyValue(string json, string name, int start, int end, out int valueStart)
        {
            valueStart = -1;
            int pos = Math.Max(0, start);
            int limit = Math.Min(json.Length, end);
            while (pos < limit)
            {
                int quote = json.IndexOf('"', pos);
                if (quote < 0 || quote >= limit)
                {
                    return false;
                }

                int keyEnd = FindStringEnd(json, quote);
                if (keyEnd < 0 || keyEnd >= limit)
                {
                    return false;
                }

                if (StringEquals(json, quote + 1, keyEnd, name))
                {
                    int colon = SkipWhitespace(json, keyEnd + 1, limit);
                    if (colon < limit && json[colon] == ':')
                    {
                        valueStart = SkipWhitespace(json, colon + 1, limit);
                        return valueStart < limit;
                    }
                }

                pos = keyEnd + 1;
            }

            return false;
        }

        private static int FindMatching(string json, int start, char open, char close)
        {
            bool inString = false;
            bool escaped = false;
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (c == '\\')
                    {
                        escaped = true;
                    }
                    else if (c == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == open)
                {
                    depth++;
                }
                else if (c == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        private static int FindStringEnd(string json, int quoteStart)
        {
            bool escaped = false;
            for (int i = quoteStart + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    return i;
                }
            }

            return -1;
        }

        private static string DecodeJsonString(string json, int start, int end)
        {
            var sb = new StringBuilder(Math.Max(0, end - start));
            for (int i = start; i < end; i++)
            {
                char c = json[i];
                if (c != '\\' || i + 1 >= end)
                {
                    sb.Append(c);
                    continue;
                }

                char escaped = json[++i];
                switch (escaped)
                {
                    case '"':
                    case '\\':
                    case '/':
                        sb.Append(escaped);
                        break;
                    case 'b':
                        sb.Append('\b');
                        break;
                    case 'f':
                        sb.Append('\f');
                        break;
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case 'u':
                        if (i + 4 < end)
                        {
                            string hex = json.Substring(i + 1, 4);
                            int code;
                            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                        }
                        break;
                    default:
                        sb.Append(escaped);
                        break;
                }
            }

            return sb.ToString();
        }

        private static string ReadValueToken(string json, int start, int end)
        {
            int pos = start;
            while (pos < end)
            {
                char c = json[pos];
                if (c == ',' || c == '}' || c == ']' || char.IsWhiteSpace(c))
                {
                    break;
                }

                pos++;
            }

            return json.Substring(start, pos - start);
        }

        private static int SkipWhitespace(string json, int start, int end)
        {
            int pos = start;
            while (pos < end && char.IsWhiteSpace(json[pos]))
            {
                pos++;
            }

            return pos;
        }

        private static int SkipWhitespaceAndCommas(string json, int start, int end)
        {
            int pos = start;
            while (pos < end && (char.IsWhiteSpace(json[pos]) || json[pos] == ','))
            {
                pos++;
            }

            return pos;
        }

        private static bool StringEquals(string json, int start, int end, string value)
        {
            int length = end - start;
            if (value == null || length != value.Length)
            {
                return false;
            }

            for (int i = 0; i < length; i++)
            {
                if (json[start + i] != value[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
