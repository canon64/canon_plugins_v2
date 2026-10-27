using System;
using System.Reflection;
using CanonPlugins.Shared;
using UnityEngine;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        private MethodInfo _subtitleTryShow;
        private Type _subtitleBackendType;
        private string _oralSubtitleFallback = "";
        private float _oralSubtitleFallbackUntil;
        private GUIStyle _oralSubtitleStyle;
        private bool _subtitleApiUnavailableLogged;

        private void ShowOralSubtitle(int voiceIndex, float duration)
        {
            if (voiceIndex < 0 || voiceIndex >= _oralCaptions.Count) return;
            string caption = (_oralCaptions[voiceIndex] ?? "").Trim();
            if (caption.Length == 0) return;
            float hold = Mathf.Max(0.1f, duration);
            if (BlankRoomGate.IsInBlankRoom && TryShowOralSubtitleCore(caption, hold))
            {
                ClearOralSubtitleFallback();
                return;
            }
            _oralSubtitleFallback = caption;
            _oralSubtitleFallbackUntil = Time.unscaledTime + hold;
        }

        private bool TryShowOralSubtitleCore(string caption, float hold)
        {
            if (_subtitleTryShow == null && !ResolveOralSubtitleApi()) return false;
            try
            {
                object backend = Enum.Parse(_subtitleBackendType, "Auto");
                return (bool)_subtitleTryShow.Invoke(null,
                    new object[] { caption, backend, "StackFemale", hold });
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Oral subtitle API failed: " + ex.Message);
                return false;
            }
        }

        private bool ResolveOralSubtitleApi()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type api = assembly.GetType("MainGameSubtitleCore.SubtitleApi", false);
                Type backend = assembly.GetType("MainGameSubtitleCore.SubtitleBackend", false);
                if (api == null || backend == null) continue;
                MethodInfo method = api.GetMethod("TryShow", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), backend, typeof(string), typeof(float) }, null);
                if (method == null) continue;
                _subtitleBackendType = backend;
                _subtitleTryShow = method;
                return true;
            }
            if (!_subtitleApiUnavailableLogged)
            {
                _subtitleApiUnavailableLogged = true;
                Logger.LogWarning("MainGameSubtitleCore unavailable; using oral subtitle overlay");
            }
            return false;
        }

        private void ClearOralSubtitleFallback()
        {
            _oralSubtitleFallback = "";
            _oralSubtitleFallbackUntil = 0f;
        }

        private void DrawOralSubtitleFallback()
        {
            if (string.IsNullOrEmpty(_oralSubtitleFallback) || Time.unscaledTime >= _oralSubtitleFallbackUntil)
                return;
            if (_oralSubtitleStyle == null)
            {
                _oralSubtitleStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    fontSize = 24
                };
                _oralSubtitleStyle.normal.textColor = Color.white;
            }
            float width = Mathf.Min(Screen.width - 30f, 1000f);
            var rect = new Rect((Screen.width - width) * 0.5f, Screen.height - 130f, width, 95f);
            int depth = GUI.depth;
            GUI.depth = -50;
            GUI.Box(rect, GUIContent.none);
            GUI.Label(rect, _oralSubtitleFallback, _oralSubtitleStyle);
            GUI.depth = depth;
        }
    }
}
