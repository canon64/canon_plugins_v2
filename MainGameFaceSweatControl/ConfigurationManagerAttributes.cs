using System;
using BepInEx.Configuration;

namespace ConfigurationManager
{
#pragma warning disable 0649
    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
        public bool? Browsable;
        public string Category;
        public object DefaultValue;
        public bool? ReadOnly;
        public bool? HideDefaultButton;
        public Action<ConfigEntryBase> CustomDrawer;
    }
#pragma warning restore 0649
}
