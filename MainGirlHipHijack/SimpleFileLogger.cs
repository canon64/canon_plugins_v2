using System;
using System.IO;
using System.Text;

namespace MainGirlHipHijack
{
    internal sealed class SimpleFileLogger
    {
        private bool _enabled = false;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly object _sync = new object();
        private string _logPath;

        internal void Initialize(string path, bool truncateOnInitialize = false)
        {
            if (!_enabled) return;
            _logPath = path;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            if (truncateOnInitialize)
                File.WriteAllText(path, string.Empty, Utf8NoBom);
            Write("INFO", "=== session start ===");
        }

        internal void Write(string level, string message)
        {
            if (!_enabled) return;
            if (string.IsNullOrEmpty(_logPath))
                return;

            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                + " [" + level + "] " + message + Environment.NewLine;
            try
            {
                lock (_sync)
                {
                    File.AppendAllText(_logPath, line, Utf8NoBom);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
