using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MainGameClubLights
{
    public sealed partial class Plugin
    {
        private static List<string> _barProfileNames;
        private static float _barProfilesRefreshAt;
        public static bool GetPlaybackLightGizmos() => Instance != null && Instance._settings.Lights.Any(l => l != null && l.ShowGizmo);
        public static void TogglePlaybackLightGizmos() {
            var p = Instance; if (p == null) return;
            bool visible = !GetPlaybackLightGizmos();
            foreach (var light in p._settings.Lights) if (light != null) { light.ShowGizmo = visible; light.ShowMarker = visible; }
            foreach (var folder in p._folderEntries) { folder.Settings.ShowHandleGizmo = visible; folder.Settings.ShowHandle = visible; }
            p.SaveSettingsNow("bar-gizmos");
        }

        private List<string> GetBarProfileNames()
        {
            if (_barProfileNames == null || Time.unscaledTime >= _barProfilesRefreshAt)
            {
                _barProfileNames = ListProfileNames();
                _barProfilesRefreshAt = Time.unscaledTime + 2f;
            }
            return _barProfileNames;
        }

        // [usable, profile index, profile count, current profile, enabled light names]
        public static object[] GetPlaybackBarLightState()
        {
            var plugin = Instance;
            if (plugin == null || plugin._settings == null)
                return new object[] { false, -1, 0, "未接続", "" };

            var names = plugin.GetBarProfileNames();
            int index = names.FindIndex(name => string.Equals(name, plugin.CurrentProfileName, StringComparison.OrdinalIgnoreCase));
            var lights = plugin._settings.Lights;
            string activeNames = lights == null ? "点灯中なし" :
                string.Join(", ", lights.Where(light => light != null && light.Enabled)
                    .Select(light => light.Name).ToArray());
            if (string.IsNullOrEmpty(activeNames)) activeNames = "点灯中なし";
            return new object[] { names.Count > 0, index, names.Count, plugin.CurrentProfileName, activeNames };
        }

        public static void StepPlaybackBarLightPreset(int direction)
        {
            var plugin = Instance;
            if (direction == 0 || plugin == null) return;
            var names = plugin.GetBarProfileNames();
            if (names.Count == 0) return;
            int current = names.FindIndex(name => string.Equals(name, plugin.CurrentProfileName, StringComparison.OrdinalIgnoreCase));
            int next = current < 0 ? (direction > 0 ? 0 : names.Count - 1) :
                (current + (direction > 0 ? 1 : names.Count - 1)) % names.Count;
            plugin.LoadProfileByName(names[next], out _);
            _barProfilesRefreshAt = 0f;
        }
    }
}
