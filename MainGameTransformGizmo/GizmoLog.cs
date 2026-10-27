using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace MainGameTransformGizmo
{
    /// <summary>
    /// ギズモ専用のログ。BepInEx の Logger はディスクに落ちない設定があり、
    /// 「ログを見ろ」と言った先に何も無い事故を避けるため必ずファイルへ書く。
    /// </summary>
    internal static class GizmoLog
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object Sync = new object();
        private static string _path;
        private static bool _initialized;
        internal static bool Enabled { get; set; }
        private static bool RuntimeLoggingEnabled = false;

        internal static void Initialize()
        {
            if (!RuntimeLoggingEnabled) return;
            if (!Enabled) return;
            try
            {
                lock (Sync)
                {
                if (!_initialized)
                {
                    _initialized = true;
                    string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        _path = Path.Combine(dir, "MainGameTransformGizmo.log");
                        File.WriteAllText(
                            _path,
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " === session start ===" + Environment.NewLine,
                            Utf8NoBom);
                    }
                }
                }
            }
            catch
            {
                // ログで本体を落とさない
            }
        }

        internal static void Write(string message)
        {
            if (!RuntimeLoggingEnabled) return;
            if (!Enabled) return;
            try
            {
                Initialize();

                if (string.IsNullOrEmpty(_path)) return;

                lock (Sync)
                {
                    File.AppendAllText(
                        _path,
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine,
                        Utf8NoBom);
                }
            }
            catch
            {
                // ログで本体を落とさない
            }
        }
    }
}
