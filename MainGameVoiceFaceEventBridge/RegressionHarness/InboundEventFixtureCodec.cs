using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace MainGameVoiceFaceEventBridge.RegressionHarness
{
    internal static class InboundEventFixtureCodec
    {
        private static readonly HashSet<string> TextKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "text", "responseText", "subtitle", "lineTexts", "lineSourceTexts", "coordName", "poseName",
            "cameraPresetName", "facePresetName", "facePresetId"
        };

        private static readonly HashSet<string> PathKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "audioPath", "path"
        };

        private static readonly HashSet<string> IdentityKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sessionId", "traceId"
        };

        internal static RecordedInboundEvent Record(long sequenceNo, long offsetMs, string rawJson)
        {
            string anonymized = Anonymize(rawJson);
            return new RecordedInboundEvent
            {
                SequenceNo = sequenceNo,
                OffsetMs = Math.Max(0L, offsetMs),
                AnonymizedJson = anonymized,
                PayloadSha256 = Sha256Hex(anonymized)
            };
        }

        internal static string Anonymize(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return string.Empty;
            }

            var serializer = new JavaScriptSerializer();
            object root = serializer.DeserializeObject(rawJson);
            object anonymized = AnonymizeValue(root, null);
            return serializer.Serialize(anonymized);
        }

        internal static void WriteJsonLines(TextWriter writer, IEnumerable<RecordedInboundEvent> events)
        {
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            var serializer = new DataContractJsonSerializer(typeof(RecordedInboundEvent));
            foreach (RecordedInboundEvent item in events ?? Enumerable.Empty<RecordedInboundEvent>())
            {
                using (var stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, item);
                    writer.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
                }
            }
        }

        internal static List<RecordedInboundEvent> ReadJsonLines(TextReader reader)
        {
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            var result = new List<RecordedInboundEvent>();
            var serializer = new DataContractJsonSerializer(typeof(RecordedInboundEvent));
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                {
                    var item = serializer.ReadObject(stream) as RecordedInboundEvent;
                    if (item != null) result.Add(item);
                }
            }
            return result.OrderBy(v => v.OffsetMs).ThenBy(v => v.SequenceNo).ToList();
        }

        private static object AnonymizeValue(object value, string key)
        {
            if (value == null) return null;

            var dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                var output = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> pair in dictionary)
                {
                    output[pair.Key] = AnonymizeValue(pair.Value, pair.Key);
                }
                return output;
            }

            var array = value as IEnumerable;
            if (!(value is string) && array != null)
            {
                var output = new List<object>();
                foreach (object item in array) output.Add(AnonymizeValue(item, key));
                return output;
            }

            string text = value as string;
            if (text == null) return value;
            if (TextKeys.Contains(key ?? string.Empty)) return Token("TEXT", text, includeLength: true);
            if (PathKeys.Contains(key ?? string.Empty)) return Token("PATH", text, includeLength: false) + Path.GetExtension(text);
            if (IdentityKeys.Contains(key ?? string.Empty)) return Token("ID", text, includeLength: false);
            return text;
        }

        private static string Token(string kind, string value, bool includeLength)
        {
            string safe = value ?? string.Empty;
            string suffix = includeLength ? ":" + safe.Length.ToString(CultureInfo.InvariantCulture) : string.Empty;
            return "<" + kind + ":" + Sha256Hex(safe).Substring(0, 12) + suffix + ">";
        }

        private static string Sha256Hex(string value)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value ?? string.Empty));
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return builder.ToString();
            }
        }
    }
}
