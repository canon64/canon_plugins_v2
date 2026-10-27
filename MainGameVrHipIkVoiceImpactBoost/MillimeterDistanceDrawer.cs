using System;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameVrHipIkVoiceImpactBoost
{
    internal static class MillimeterDistanceDrawer
    {
        private static string _text;
        private static float _lastMeters = float.NaN;

        internal static void Draw(ConfigEntryBase entryBase)
        {
            var entry = (ConfigEntry<float>)entryBase;
            float millimeters = entry.Value * 1000f;
            if (_text == null || entry.Value != _lastMeters)
                _text = millimeters.ToString("0.##", CultureInfo.InvariantCulture);

            GUILayout.BeginHorizontal();
            GUILayout.Label("0", GUILayout.Width(16f));
            float slider = GUILayout.HorizontalSlider(Mathf.Clamp(millimeters, 0f, 20f), 0f, 20f);
            GUILayout.Label("20 mm", GUILayout.Width(48f));
            if (Math.Abs(slider - Mathf.Clamp(millimeters, 0f, 20f)) > 0.00001f)
            {
                entry.Value = (float)Math.Round(slider, 2) / 1000f;
                _text = (entry.Value * 1000f).ToString("0.##", CultureInfo.InvariantCulture);
            }

            string next = GUILayout.TextField(_text, GUILayout.Width(72f));
            if (next != _text)
            {
                _text = next;
                if (float.TryParse(next, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                    && !float.IsNaN(parsed) && !float.IsInfinity(parsed) && parsed >= 0f && parsed <= 1000f)
                    entry.Value = parsed / 1000f;
            }
            GUILayout.Label("mm", GUILayout.Width(25f));
            GUILayout.EndHorizontal();
            _lastMeters = entry.Value;
        }
    }
}
