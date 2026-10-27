using System;
using System.IO;
using System.Text;

namespace MainGameLogRelay
{
    internal sealed class OwnerFileLogger
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly object _sync = new object();

        internal void Write(string fullPath, string levelText, string message)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(levelText))
                return;

            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")
                + " [" + levelText + "] " + (message ?? string.Empty) + Environment.NewLine;

            try
            {
                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                lock (_sync)
                {
                    File.AppendAllText(fullPath, line, Utf8NoBom);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
