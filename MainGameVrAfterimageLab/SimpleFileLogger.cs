using System;
using System.IO;
using System.Text;

namespace MainGameVrAfterimageLab
{
    // プラグインフォルダ内へ専用ログを出力する。実機(VR)での挙動確認が主目的なので
    // 1行ずつ追記し、フラッシュも都度行う。
    internal static class SimpleFileLogger
    {
        private static string _path;
        private static readonly object _lock = new object();
        internal static bool Enabled { get; set; }

        internal static void Init(string path, bool enabled)
        {
            _path = path;
            Enabled = enabled;
            if (Enabled) try { File.WriteAllText(_path, "", Encoding.UTF8); } catch { }
        }

        internal static void Write(string level, string msg)
        {
            if (!Enabled || string.IsNullOrEmpty(_path)) return;
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_path,
                        "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "][" + level + "] " + msg + "\n",
                        Encoding.UTF8);
                }
            }
            catch { }
        }

        internal static void Info(string msg) => Write("INFO", msg);
        internal static void Warn(string msg) => Write("WARN", msg);
    }
}
