using System;
using System.IO;
using System.Text;

namespace MainGameVoiceFaceEventBridge
{
    // Plugin.Logging.cs
    //
    // 責務: ロギング (BepInEx Logger + 自前のファイル追記)。
    //       Log / LogAlways / LogWarn / LogError / AppendFileLog。
    internal sealed partial class Plugin
    {
        internal static bool RuntimeLoggingEnabled = false;
        internal static bool ImpactCueDiagnosticsEnabled => Instance != null
            && Instance._impactCueDiagnosticLog != null && Instance._impactCueDiagnosticLog.Value;
        internal static void LogImpactCueDiagnostic(string message)
        {
            if (!ImpactCueDiagnosticsEnabled) return;
            string line = "[impact-cue-diagnostic] " + message;
            Logger?.LogInfo(line);
            AppendFileLog(line);
        }
        internal static void Log(string message)
        {
            if (!RuntimeLoggingEnabled) return;
            if (Settings != null && !Settings.VerboseLog)
            {
                return;
            }
            Logger?.LogInfo(message);
            AppendFileLog(message);
        }

        internal static void LogAlways(string message)
        {
            if (!RuntimeLoggingEnabled) return;
            Logger?.LogInfo(message);
            AppendFileLog(message);
        }

        internal static void LogWarn(string message)
        {
            if (!RuntimeLoggingEnabled) return;
            Logger?.LogWarning(message);
            AppendFileLog("[WARN] " + message);
        }

        internal static void LogError(string message)
        {
            if (!RuntimeLoggingEnabled) return;
            Logger?.LogError(message);
            AppendFileLog("[ERROR] " + message);
        }

        private static void AppendFileLog(string message)
        {
            if (string.IsNullOrEmpty(LogFilePath))
            {
                return;
            }

            try
            {
                lock (FileLogLock)
                {
                    File.AppendAllText(
                        LogFilePath,
                        $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}",
                        Utf8NoBom);
                }
            }
            catch
            {
            }
        }
    }
}
