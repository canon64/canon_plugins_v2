using System;
using System.IO;
using System.Text;

namespace MainGamePregnancyPlusBridge
{
    internal sealed class SimpleFileLogger
    {
        private readonly string _path;
        internal bool Enabled = false;
        private readonly object _lockObj = new object();

        public SimpleFileLogger(string path, bool resetOnStart)
        {
            _path = path;
            if (!Enabled) return;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (resetOnStart)
                File.WriteAllText(_path, string.Empty, new UTF8Encoding(false));
        }

        public void Write(string level, string message)
        {
            if (!Enabled) return;
            lock (_lockObj)
            {
                string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [" + level + "] " + message + Environment.NewLine;
                File.AppendAllText(_path, line, new UTF8Encoding(false));
            }
        }
    }
}
