using System;
using System.IO;

namespace MainGameHandySync
{
    internal static class SimpleFileLogger
    {
        private static string _path;
        internal static bool Enabled = false;
        private static readonly object _lock = new object();
        private static Func<bool> _normalEnabled;
        private static Func<bool> _detailedEnabled;
        private static Func<bool> _errorEnabled;

        internal static void Configure(Func<bool> normalEnabled, Func<bool> detailedEnabled, Func<bool> errorEnabled)
        {
            _normalEnabled = normalEnabled;
            _detailedEnabled = detailedEnabled;
            _errorEnabled = errorEnabled;
        }

        internal static void Init(string path)
        {
            _path = path;
        }

        internal static void Info(string msg) => Write("INFO", msg);
        internal static void Warn(string msg) => Write("WARN", msg);
        internal static void Debug(string msg) => Write("DEBUG", msg);

        private static void Write(string level, string msg)
        {
            if (!Enabled) return;
            if (string.IsNullOrEmpty(_path)) return;
            Func<bool> enabled = level == "DEBUG" ? _detailedEnabled : level == "WARN" ? _errorEnabled : _normalEnabled;
            if (enabled == null || !enabled()) return;
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
