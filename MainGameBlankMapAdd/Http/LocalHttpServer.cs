using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace MainGameBlankMapAdd.Http
{
    internal sealed class LocalHttpResponse
    {
        internal LocalHttpResponse(int statusCode, string json)
        {
            StatusCode = statusCode;
            Json = string.IsNullOrWhiteSpace(json) ? "{}" : json;
        }

        internal int StatusCode { get; }
        internal string Json { get; }

        internal static LocalHttpResponse Message(int statusCode, string message)
        {
            return new LocalHttpResponse(
                statusCode,
                $"{{\"ok\":{(statusCode == 200 ? "true" : "false")},\"msg\":\"{EscapeJson(message)}\"}}");
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var builder = new StringBuilder(value.Length + 8);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\t': builder.Append("\\t"); break;
                    default: builder.Append(c); break;
                }
            }
            return builder.ToString();
        }
    }

    internal sealed class LocalHttpServer : IDisposable
    {
        private readonly int _port;
        private readonly Func<string, string, LocalHttpResponse> _router;
        private readonly Action<string> _info;
        private readonly Action<string> _warning;
        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        internal LocalHttpServer(
            int port,
            Func<string, string, LocalHttpResponse> router,
            Action<string> info,
            Action<string> warning)
        {
            _port = port;
            _router = router ?? throw new ArgumentNullException(nameof(router));
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _warning = warning ?? throw new ArgumentNullException(nameof(warning));
        }

        internal bool Start()
        {
            if (_running) return true;
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                _listener.Start();
                _running = true;
                _thread = new Thread(ServerLoop) { IsBackground = true, Name = "BlankMapHttpServer" };
                _thread.Start();
                _info($"[http] server started port={_port}");
                return true;
            }
            catch (Exception ex)
            {
                _warning($"[http] server start failed: {ex.Message}");
                _listener = null;
                return false;
            }
        }

        private void ServerLoop()
        {
            while (_running)
            {
                try
                {
                    HttpListenerContext context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => Handle(context));
                }
                catch (Exception ex)
                {
                    if (_running) _warning($"[http] server error: {ex.Message}");
                }
            }
        }

        private void Handle(HttpListenerContext context)
        {
            HttpListenerResponse response = context.Response;
            try
            {
                string path = context.Request.Url.AbsolutePath.TrimEnd('/').ToLowerInvariant();
                string body = string.Empty;
                if (context.Request.HasEntityBody)
                {
                    using (var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8))
                        body = reader.ReadToEnd();
                }

                LocalHttpResponse result = _router(path, body) ?? LocalHttpResponse.Message(500, "empty response");
                byte[] bytes = Encoding.UTF8.GetBytes(result.Json);
                response.StatusCode = result.StatusCode;
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = bytes.Length;
                response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            catch (Exception ex)
            {
                _warning($"[http] request error: {ex.Message}");
            }
            finally
            {
                try { response.Close(); } catch { }
            }
        }

        public void Dispose()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            _listener = null;
            _thread = null;
        }
    }
}
