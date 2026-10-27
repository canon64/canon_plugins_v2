using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace MainGameVoiceFaceEventBridge.External
{
    internal sealed class SubtitleEventClient
    {
        private readonly Action<string> _log;
        private readonly Action<string> _warn;
        private readonly LocalSubtitleApiClient _local = new LocalSubtitleApiClient();

        public SubtitleEventClient(Action<string> log, Action<string> warn)
        {
            _log = log;
            _warn = warn;
        }

        public bool TrySendLocal(SubtitleEventClientOptions options, SubtitleEventMessage message)
        {
            if (!_local.TrySend(options, message)) return false;
            if (options.VerboseLog)
                _log?.Invoke("[subtitle-seq] sent transport=local-api session="
                    + message.SessionId + " index=" + message.Index);
            return true;
        }

        public void Send(SubtitleEventClientOptions options, SubtitleEventMessage message)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (message == null) throw new ArgumentNullException(nameof(message));

            string url = SubtitleEventPayloadBuilder.BuildUrl(options.Host, options.Port, options.EndpointPath);
            string payload = SubtitleEventPayloadBuilder.BuildPayload(message);
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.Timeout = 1500;
                request.ReadWriteTimeout = 1500;
                if (!string.IsNullOrWhiteSpace(options.Token))
                {
                    request.Headers["X-Auth-Token"] = options.Token.Trim();
                }

                byte[] body = Encoding.UTF8.GetBytes(payload);
                request.ContentLength = body.Length;
                using (Stream stream = request.GetRequestStream())
                {
                    stream.Write(body, 0, body.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    string responseText = reader.ReadToEnd();
                    _log?.Invoke("[subtitle-seq] sent status=" + (int)response.StatusCode
                        + " session=" + message.SessionId + " index=" + message.Index);
                    if (options.VerboseLog && !string.IsNullOrWhiteSpace(responseText))
                    {
                        _log?.Invoke("[subtitle-seq] response: " + TrimPreview(responseText, 120));
                    }
                }
            }
            catch (WebException webEx)
            {
                string detail = webEx.Message;
                try
                {
                    if (webEx.Response != null)
                    {
                        using (var stream = webEx.Response.GetResponseStream())
                        using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
                        {
                            string body = reader.ReadToEnd();
                            if (!string.IsNullOrWhiteSpace(body)) detail += " body=" + TrimPreview(body, 120);
                        }
                    }
                }
                catch { }

                _warn?.Invoke("[subtitle-seq] request failed: " + detail + " url=" + url);
            }
            catch (Exception ex)
            {
                _warn?.Invoke("[subtitle-seq] request error: " + ex.Message + " url=" + url);
            }
        }

        private static string TrimPreview(string value, int maxLength)
        {
            string text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }
    }

    internal sealed class SubtitleEventClientOptions
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public string EndpointPath { get; set; }
        public string Token { get; set; }
        public bool VerboseLog { get; set; }
    }

    internal sealed class SubtitleEventMessage
    {
        public string Text { get; set; }
        public string WavName { get; set; }
        public string Backend { get; set; }
        public string DisplayMode { get; set; }
        public float HoldSeconds { get; set; }
        public string SessionId { get; set; }
        public int Index { get; set; }
    }

    internal static class SubtitleEventPayloadBuilder
    {
        public static string NormalizeSendMode(string mode)
        {
            return string.Equals((mode ?? string.Empty).Trim(), "FullTextOnce", StringComparison.OrdinalIgnoreCase)
                ? "FullTextOnce"
                : "PerLine";
        }

        public static string BuildProgressText(string text, int index, int total)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length <= 0 || total <= 1) return value;

            int safeIndex = index > 0 ? index : 1;
            return "[" + safeIndex.ToString(CultureInfo.InvariantCulture)
                + "/" + total.ToString(CultureInfo.InvariantCulture) + "] " + value;
        }

        public static string BuildDisplayText(string text, string displayMode)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length <= 0) return value;
            if (string.Equals(displayMode, "StackFemale", StringComparison.OrdinalIgnoreCase)
                && value.IndexOf("<color=", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return "<color=#FF7ACDFF>" + value + "</color>";
            }

            return value;
        }

        public static string BuildUrl(string host, int port, string endpointPath)
        {
            string safeHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
            int safePort = port > 0 && port <= 65535 ? port : 18766;
            string endpoint = string.IsNullOrWhiteSpace(endpointPath) ? "/subtitle-event" : endpointPath.Trim();
            if (!endpoint.StartsWith("/", StringComparison.Ordinal)) endpoint = "/" + endpoint;
            return "http://" + safeHost + ":" + safePort + endpoint;
        }

        public static string BuildPayload(SubtitleEventMessage message)
        {
            if (message == null) throw new ArgumentNullException(nameof(message));
            string hold = Math.Max(0.1f, message.HoldSeconds).ToString("0.###", CultureInfo.InvariantCulture);
            return "{\"text\":\"" + EscapeJson(message.Text)
                + "\",\"source\":\"voiceface_sequence\""
                + ",\"wav_name\":\"" + EscapeJson(message.WavName)
                + "\",\"backend\":\"" + EscapeJson(message.Backend)
                + "\",\"display_mode\":\"" + EscapeJson(message.DisplayMode)
                + "\",\"speaker_gender\":\"female\""
                + ",\"session_id\":\"" + EscapeJson(message.SessionId)
                + "\",\"index\":" + message.Index.ToString(CultureInfo.InvariantCulture)
                + ",\"hold_seconds\":" + hold + "}";
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new StringBuilder(value.Length + 16);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
