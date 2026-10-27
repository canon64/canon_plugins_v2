using BepInEx.Configuration;
using BepInEx.Logging;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        internal static MainGameRhythmSync.Plugin Host;
        private ConfigFile Config => Host.SpeedConfig;
        private ManualLogSource Logger => Host.SpeedLogger;
    }
}
