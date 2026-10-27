using System;
using BepInEx.Configuration;

namespace MainGameVrHipIkVoiceImpactBoost
{
    // ConfigurationManager reads these fields from ConfigDescription.Tags.
    internal sealed class ConfigurationManagerAttributes
    {
        public string DispName;
        public Action<ConfigEntryBase> CustomDrawer;
    }
}
