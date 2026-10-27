using System;
using System.IO;
using System.Text;

namespace MainGirlShoulderIkStabilizer;

internal sealed class SimpleFileLogger
{
    private bool _runtimeLoggingEnabled = false;
	private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

	private readonly object _sync = new object();

	private string _logPath;

	internal void Initialize(string logPath)
	{
		if (!_runtimeLoggingEnabled) return;
		_logPath = logPath;
		string dir = Path.GetDirectoryName(_logPath);
		if (!string.IsNullOrEmpty(dir))
		{
			Directory.CreateDirectory(dir);
		}
		File.WriteAllText(_logPath, string.Empty, Utf8NoBom);
		Write("INFO", "=== session start ===");
	}

	internal void Write(string level, string message)
	{
		if (!_runtimeLoggingEnabled) return;
		if (string.IsNullOrEmpty(_logPath))
		{
			return;
		}
		string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " + message + Environment.NewLine;
		try
		{
			lock (_sync)
			{
				File.AppendAllText(_logPath, line, Utf8NoBom);
			}
		}
		catch
		{
		}
	}
}
