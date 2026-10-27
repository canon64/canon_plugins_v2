using System;

namespace MainGameLogRelay
{
    public static class LogRelayApi
    {
        public static bool IsAvailable => Plugin.Instance != null;

        public static void Log(string owner, LogRelayLevel level, string message)
        {
            Plugin.Instance?.Log(owner, level, message);
        }

        public static void LogLazy(string owner, LogRelayLevel level, Func<string> messageFactory)
        {
            Plugin.Instance?.LogLazy(owner, level, messageFactory);
        }

        public static void Debug(string owner, string message) => Log(owner, LogRelayLevel.Debug, message);
        public static void Info(string owner, string message) => Log(owner, LogRelayLevel.Info, message);
        public static void Warn(string owner, string message) => Log(owner, LogRelayLevel.Warning, message);
        public static void Error(string owner, string message) => Log(owner, LogRelayLevel.Error, message);

        public static void SetOwnerEnabled(string owner, bool enabled)
        {
            Plugin.Instance?.SetOwnerEnabled(owner, enabled);
        }

        public static void SetOwnerOutputMode(string owner, LogRelayOutputMode mode)
        {
            Plugin.Instance?.SetOwnerOutputMode(owner, mode);
        }

        public static void SetOwnerMinimumLevel(string owner, LogRelayLevel level)
        {
            Plugin.Instance?.SetOwnerMinimumLevel(owner, level);
        }

        public static void SetOwnerLogKey(string owner, string logKey)
        {
            Plugin.Instance?.SetOwnerLogKey(owner, logKey);
        }

        public static void ClearOwnerRuntimeOverrides(string owner)
        {
            Plugin.Instance?.ClearOwnerRuntimeOverrides(owner);
        }

        public static string GetOwnerSummary(string owner)
        {
            return Plugin.Instance == null ? "plugin-unavailable" : Plugin.Instance.GetOwnerSummary(owner);
        }

        public static string GetRelaySummary()
        {
            return Plugin.Instance == null ? "plugin-unavailable" : Plugin.Instance.GetRelaySummary();
        }
    }
}
