using System;
using System.IO;

namespace AstralMode
{
    internal static class SimpleFileLogger
    {
        private static string _path;
        internal static bool Enabled = false;
        private static readonly object _lock = new object();

        internal static void Init(string path)
        {
            _path = path;
            if (!Enabled) return;
            try
            {
                File.WriteAllText(_path, $"=== AstralMode log start {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===" + Environment.NewLine);
            }
            catch { }
        }

        internal static void Info(string msg) => Write("INFO", msg);
        internal static void Warn(string msg) => Write("WARN", msg);
        internal static void Debug(string msg) => Write("DEBUG", msg);

        private static void Write(string level, string msg)
        {
            if (!Enabled) return;
            if (string.IsNullOrEmpty(_path)) return;
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {msg}" + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
