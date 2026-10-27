using System;
using BepInEx.Configuration;

namespace ConfigurationManager
{
    // ConfigurationManager discovers these fields from the config description tag.
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? HideDefaultButton;
        public Action<ConfigEntryBase> CustomDrawer;
    }
}
