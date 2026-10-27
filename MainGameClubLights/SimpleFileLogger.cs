using System;
using System.IO;
using System.Text;

namespace MainGameClubLights
{
    internal sealed class SimpleFileLogger
    {
        private readonly string      _path;
        private readonly object      _lock = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal SimpleFileLogger(string path)
        {
            _path = path;
        }

        internal bool Enabled { get { return false; } set { } }

        internal void Info(string msg)  => Write("INFO",  msg);
        internal void Warn(string msg)  => Write("WARN",  msg);
        internal void Error(string msg) => Write("ERROR", msg);

        private void Write(string level, string msg)
        {
            if (!Enabled) return;
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_path,
                        $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {msg}{Environment.NewLine}",
                        Utf8NoBom);
                }
            }
            catch { }
        }
    }
}
