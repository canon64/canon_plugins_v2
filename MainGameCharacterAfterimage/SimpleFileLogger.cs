using System;
using System.IO;
using System.Text;

namespace MainGameCharacterAfterimage
{
    internal sealed class SimpleFileLogger
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly object _sync = new object();
        private string _path;
        internal bool Enabled = false;

        internal string Path => _path;

        internal void Initialize(string path)
        {
            if (!Enabled) return;
            _path = path;
            string dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(_path, string.Empty, Utf8NoBom);
            Write("INFO", "=== session start ===");
        }

        internal void Write(string level, string message)
        {
            if (!Enabled) return;
            if (string.IsNullOrEmpty(_path))
            {
                return;
            }

            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                + " [" + level + "] " + message + Environment.NewLine;
            try
            {
                lock (_sync)
                {
                    File.AppendAllText(_path, line, Utf8NoBom);
                }
            }
            catch
            {
                // ignore file logging failure
            }
        }
    }
}
