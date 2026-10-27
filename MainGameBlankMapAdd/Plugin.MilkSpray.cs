using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace MainGameBlankMapAdd
{
    public partial class Plugin
    {
        private static ConfigEntry<bool> GetMilkSprayEnabledEntry()
        {
            if (!Chainloader.PluginInfos.TryGetValue("com.kks.maingame.milkspray", out var plugin) || plugin.Instance == null)
                return null;
            return plugin.Instance.Config.TryGetEntry<bool>("01.全般", "Enabled", out var entry) ? entry : null;
        }
    }
}
