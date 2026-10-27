using System;
using System.IO;
using System.Text;

namespace MainGameBlankMapAdd.Diagnostics
{
    internal sealed class RotatingFileLogSink : IDisposable
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly object _sync = new object();
        private readonly string _path;
        private readonly long _maxBytes;
        private readonly int _generations;
        private bool _disposed;

        internal RotatingFileLogSink(string path, long maxBytes = 16L * 1024L * 1024L, int generations = 4)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException(nameof(path));
            if (maxBytes < 1024L) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            if (generations < 1) throw new ArgumentOutOfRangeException(nameof(generations));
            _path = path;
            _maxBytes = maxBytes;
            _generations = generations;
            string directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_path, string.Empty, Utf8NoBom);
            for (int generation = 1; generation <= _generations; generation++)
            {
                string oldPath = GetGenerationPath(generation);
                if (File.Exists(oldPath)) File.Delete(oldPath);
            }
        }

        internal void AppendLine(string line)
        {
            lock (_sync)
            {
                if (_disposed) return;
                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                long incomingBytes = Utf8NoBom.GetByteCount((line ?? string.Empty) + Environment.NewLine);
                RotateIfNeeded(incomingBytes);
                File.AppendAllText(_path, (line ?? string.Empty) + Environment.NewLine, Utf8NoBom);
            }
        }

        private void RotateIfNeeded(long incomingBytes)
        {
            var current = new FileInfo(_path);
            if (!current.Exists || current.Length + incomingBytes <= _maxBytes)
                return;

            string oldest = GetGenerationPath(_generations);
            if (File.Exists(oldest))
                File.Delete(oldest);

            for (int generation = _generations - 1; generation >= 1; generation--)
            {
                string source = GetGenerationPath(generation);
                if (!File.Exists(source)) continue;
                File.Move(source, GetGenerationPath(generation + 1));
            }

            File.Move(_path, GetGenerationPath(1));
        }

        private string GetGenerationPath(int generation)
        {
            return _path + "." + generation;
        }

        public void Dispose()
        {
            lock (_sync)
                _disposed = true;
        }
    }
}
