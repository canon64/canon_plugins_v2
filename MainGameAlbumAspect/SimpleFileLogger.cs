using System;
using System.IO;
using System.Text;

namespace MainGameAlbumAspect
{
    /// <summary>
    /// プラグインフォルダ直下へ吐き出す簡易ファイルロガー。
    /// Init() の時点でファイルを truncate するため、起動のたびにログは空から始まる。
    /// </summary>
    internal static class SimpleFileLogger
    {
        private static string _path;
        internal static bool Enabled = false;
        private static readonly object _lock = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        internal static string Path => _path;

        /// <summary>ログファイルを開き直す（既存内容は破棄＝起動時リセット）。</summary>
        internal static void Init(string path)
        {
            if (!Enabled) return;
            lock (_lock)
            {
                _path = path;
                try
                {
                    File.WriteAllText(
                        _path,
                        "=== " + Plugin.PluginName + " " + Plugin.Version + " log start "
                            + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" + Environment.NewLine,
                        Utf8NoBom);
                }
                catch
                {
                    // ログが書けない程度でゲームを壊さない。
                }
            }
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
                    File.AppendAllText(
                        _path,
                        "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [" + level + "] " + msg + Environment.NewLine,
                        Utf8NoBom);
                }
            }
            catch
            {
                // 同上。
            }
        }
    }
}
