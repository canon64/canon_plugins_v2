using System;
using System.IO;
using System.Text;

namespace MainGameObjectComposer
{
    internal sealed class SimpleFileLogger
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly object _sync = new object();
        private string _logPath;
        internal bool Enabled = false;

        internal string LogPath => _logPath;

        internal void Initialize(string path, bool clearOnStart = false)
        {
            _logPath = path;
            if (!Enabled) return;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (clearOnStart)
            {
                try { File.WriteAllText(_logPath, string.Empty, Utf8NoBom); }
                catch { /* Keep the plugin available if diagnostics cannot be reset. */ }
            }
        }

        internal void Write(string level, string message)
        {
            if (!Enabled) return;
            if (string.IsNullOrEmpty(_logPath))
            {
                return;
            }

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
                // ignore file logging failures
            }
        }
    }
}
