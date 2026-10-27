using System;

namespace MainGameBlankMapAdd.Diagnostics
{
    internal sealed class PluginLogger : IDisposable
    {
        private readonly Action<string> _info;
        private readonly Action<string> _warning;
        private readonly Action<string> _error;
        private readonly RotatingFileLogSink _file;

        internal PluginLogger(
            Action<string> info,
            Action<string> warning,
            Action<string> error,
            RotatingFileLogSink file)
        {
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _warning = warning ?? throw new ArgumentNullException(nameof(warning));
            _error = error ?? throw new ArgumentNullException(nameof(error));
            _file = file ?? throw new ArgumentNullException(nameof(file));
        }

        internal void WriteStartupBanner(string pluginName, string version)
        {
            _file.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] === {pluginName} {version} start ===");
        }

        internal void Write(string level, string message)
        {
            string normalizedLevel = string.IsNullOrWhiteSpace(level) ? "INFO" : level.Trim().ToUpperInvariant();
            if (normalizedLevel == "ERROR") _error(message);
            else if (normalizedLevel == "WARN") _warning(message);
            else _info(message);

            _file.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{normalizedLevel}] {message}");
        }

        public void Dispose()
        {
            _file.Dispose();
        }
    }
}
