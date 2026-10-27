using UnityEngine;
using MainGameUiInputCapture;

namespace CanonPlugins.Shared
{
    internal static class PluginInput
    {
        internal static bool Down(string id, string fallback, bool exclusiveMouse = false) => InputBindingsApi.Down(id, fallback, exclusiveMouse);
        internal static bool Held(string id, string fallback, bool exclusiveMouse = false) => InputBindingsApi.Held(id, fallback, exclusiveMouse);
        internal static bool MainHeld(string id, string fallback) => InputBindingsApi.MainHeld(id, fallback);
        internal static float Axis(string id, string fallback) => InputBindingsApi.Axis(id, fallback);
        internal static float Stick(string id, Vector2 axis) => InputBindingsApi.Stick(id, axis);
        internal static bool VrHeld(string id, object input, string fallback) => InputBindingsApi.Vr(id, input, 0, fallback);
        internal static bool VrDown(string id, object input, string fallback) => InputBindingsApi.Vr(id, input, 1, fallback);
        internal static bool VrUp(string id, object input, string fallback) => InputBindingsApi.Vr(id, input, 2, fallback);
    }
}
