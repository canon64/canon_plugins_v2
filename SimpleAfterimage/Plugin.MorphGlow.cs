using System;
using System.Reflection;

namespace SimpleAfterimage
{
    public sealed partial class Plugin
    {
        private static PropertyInfo _externalGlowState;

        private bool PauseForExternalGlow()
        {
            bool active = false;
            try
            {
                if (_externalGlowState == null)
                {
                    Type api = Type.GetType("MainGameGlow.MainGameGlowApi, MainGameGlow", false);
                    if (api != null)
                        _externalGlowState = api.GetProperty("IsExternalGlowActive", BindingFlags.Public | BindingFlags.Static);
                }
                if (_externalGlowState != null)
                    active = (bool)_externalGlowState.GetValue(null, null);
            }
            catch (Exception ex)
            {
                LogPlugin("WARN", "external glow state unavailable: " + ex.Message);
            }

            if (active && !_externalGlowPaused)
            {
                ClearAfterimageSlots();
                LogPlugin("INFO", "afterimage paused during external glow");
            }
            else if (!active && _externalGlowPaused)
            {
                LogPlugin("INFO", "afterimage resumed after external glow");
            }
            _externalGlowPaused = active;
            return active;
        }
    }
}
