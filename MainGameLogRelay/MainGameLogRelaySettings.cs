using System;

namespace MainGameLogRelay
{
    public enum LogRelayLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3
    }

    public enum LogRelayOutputMode
    {
        FileOnly = 0,
        BepInExOnly = 1,
        Both = 2
    }

    public enum LogRelayFileLayout
    {
        PerPlugin = 0,
        Shared = 1
    }

    [Serializable]
    public sealed class OwnerRule
    {
        public string Owner = string.Empty;
        public bool OverrideEnabled = false;
        public bool Enabled = true;
        public bool OverrideOutputMode = false;
        public LogRelayOutputMode OutputMode = LogRelayOutputMode.Both;
        public bool OverrideMinimumLevel = false;
        public LogRelayLevel MinimumLevel = LogRelayLevel.Info;
        public bool OverrideFileLayout = false;
        public LogRelayFileLayout FileLayout = LogRelayFileLayout.PerPlugin;
        public string LogKey = string.Empty;
    }

    [Serializable]
    internal sealed class MainGameLogRelaySettings
    {
        public bool Enabled = false;
        public bool ResetOwnerLogsOnStartup = true;
        public bool DefaultOwnerEnabled = false;
        public LogRelayOutputMode DefaultOutputMode = LogRelayOutputMode.Both;
        public LogRelayLevel DefaultMinimumLevel = LogRelayLevel.Info;
        public LogRelayFileLayout FileLayout = LogRelayFileLayout.PerPlugin;
        public bool LogInternalState = false;
        public OwnerRule[] OwnerRules = new OwnerRule[0];
    }
}
