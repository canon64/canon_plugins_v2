using BepInEx.Bootstrap;

namespace MainGameBlankMapAdd.Compatibility
{
    /// <summary>
    /// Legacy static API and Harmony callbacks resolve the loaded plugin only at the compatibility boundary.
    /// The application itself does not keep a second static owner of runtime state.
    /// </summary>
    internal static class PluginCompatibilityResolver
    {
        internal static Plugin ResolveLoadedPlugin()
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(Plugin.GUID, out var info)) return null;
                return info?.Instance as Plugin;
            }
            catch
            {
                return null;
            }
        }
    }
}
