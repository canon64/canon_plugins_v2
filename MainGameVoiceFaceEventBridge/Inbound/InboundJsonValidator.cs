using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace MainGameVoiceFaceEventBridge.Inbound
{
    internal static class InboundJsonValidator
    {
        public static bool TryValidateObject(string json, out string reason)
        {
            string value = (json ?? string.Empty).Trim();
            if (value.Length <= 0 || value[0] != '{')
            {
                reason = "json object required";
                return false;
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value);
                using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(
                    bytes,
                    0,
                    bytes.Length,
                    Encoding.UTF8,
                    XmlDictionaryReaderQuotas.Max,
                    null))
                {
                    while (reader.Read()) { }
                }
                reason = null;
                return true;
            }
            catch (Exception ex) when (ex is XmlException || ex is SerializationException || ex is FormatException)
            {
                reason = "invalid json: " + ex.Message;
                return false;
            }
        }
    }
}
