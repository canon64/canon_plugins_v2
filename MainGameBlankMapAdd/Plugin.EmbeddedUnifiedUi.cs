using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private const int EmbeddedPlaybackWindowId = 0x50425549;
        private const string EmbeddedMapTab = "__blankmapadd_map";
        private const string EmbeddedHelpTab = "__blankmapadd_operations";
        private Func<string[]> _embeddedReadTabs;
        private Action<string> _embeddedDrawTab;
        private float _embeddedResolveAfter;
        private string[] _embeddedTabs = Array.Empty<string>();
        private string _embeddedSelectedTab = ""; // empty = local room controls
        private string _embeddedPendingTab;
        private readonly Dictionary<string, Vector2> _embeddedScroll = new Dictionary<string, Vector2>();
        private Rect _embeddedBarRect;
        private bool _embeddedBarDragging;
        private int _embeddedHotControl;
        private Action<float> _embeddedRoomDraw;
        private GUIStyle _embeddedWindowStyle;

        private float GetPlaybackBarHeightPx()
        {
            return Mathf.Min(Mathf.Max(GetPlaybackBarMinHeightPx(), _settings.PlaybackBarHeight), Mathf.Max(1f, Screen.height - 8f));
        }

        private void UpdateEmbeddedBarDrag(Vector2 mouse)
        {
            if (!UnityEngine.Application.isFocused || !_playbackRoomControlsExpanded)
            {
                ClearEmbeddedBarInteraction();
                return;
            }
            bool held = Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2);
            if (!held) _embeddedBarDragging = false;
            if (_embeddedBarRect.width > 0 && _embeddedBarRect.Contains(mouse)
                && (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)))
                _embeddedBarDragging = true;
        }

        private void ClearEmbeddedBarInteraction()
        {
            if (_embeddedHotControl != 0 && GUIUtility.hotControl == _embeddedHotControl)
                GUIUtility.hotControl = 0;
            _embeddedHotControl = 0;
            _embeddedBarDragging = false;
            _embeddedBarRect = Rect.zero;
            _embeddedRoomDraw = null;
        }

        private void ResolveEmbeddedUi()
        {
            if (_embeddedReadTabs != null || Time.unscaledTime < _embeddedResolveAfter) return;
            _embeddedResolveAfter = Time.unscaledTime + 2f;
            if (!Chainloader.PluginInfos.TryGetValue("com.kks.maingame.unifiedui", out var info)) return;
            var api = info.Instance.GetType().Assembly.GetType("MainGameUnifiedUi.UnifiedUiApi");
            var list = api?.GetMethod("GetEmbeddedTabs", Type.EmptyTypes);
            var draw = api?.GetMethod("DrawEmbeddedTab", new[] { typeof(string) });
            if (list == null || draw == null) return;
            _embeddedReadTabs = (Func<string[]>)Delegate.CreateDelegate(typeof(Func<string[]>), list);
            _embeddedDrawTab = (Action<string>)Delegate.CreateDelegate(typeof(Action<string>), draw);
        }

        private void DrawEmbeddedPlaybackUi(Rect rect, Action<float> drawRoom)
        {
            ResolveEmbeddedUi();
            // Do not change GUILayout contents between Layout and Repaint/input.
            if (Event.current.type == EventType.Layout)
            {
                _embeddedTabs = _embeddedReadTabs?.Invoke() ?? Array.Empty<string>();
                if (_embeddedPendingTab != null)
                {
                    _embeddedSelectedTab = _embeddedPendingTab;
                    _embeddedPendingTab = null;
                }
                bool exists = _embeddedSelectedTab.Length == 0 || _embeddedSelectedTab == EmbeddedMapTab || _embeddedSelectedTab == EmbeddedHelpTab;
                for (int i = 0; i + 1 < _embeddedTabs.Length; i += 2)
                    if (_embeddedTabs[i] == _embeddedSelectedTab) exists = true;
                if (!exists) _embeddedSelectedTab = "";
            }
            _embeddedBarRect = rect;
            _embeddedRoomDraw = drawRoom;
            if (_embeddedWindowStyle == null)
                _embeddedWindowStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(0, 0, 0, 0) };
            GUI.Window(EmbeddedPlaybackWindowId, rect, DrawEmbeddedPlaybackWindow, GUIContent.none, _embeddedWindowStyle);
        }

        private void DrawEmbeddedPlaybackWindow(int id)
        {
            int before = GUIUtility.hotControl;
            bool completed = false;
            try
            {
                DrawEmbeddedPlaybackWindowBody();
                completed = true;
            }
            finally
            {
                // A tab can exit GUI early or throw after acquiring the pointer.
                // Record ownership even then, and never leave its gesture active.
                if (GUIUtility.hotControl != 0 && GUIUtility.hotControl != before)
                    _embeddedHotControl = GUIUtility.hotControl;
                if (GUIUtility.hotControl == 0) _embeddedHotControl = 0;
                if (!completed) ClearEmbeddedBarInteraction();
            }
        }

        private void DrawEmbeddedPlaybackWindowBody()
        {
            float width = _embeddedBarRect.width;
            float x = 4f, y = 4f;
            for (int i = -4; i < _embeddedTabs.Length + 2; i += 2)
            {
                bool help = i == _embeddedTabs.Length;
                string key = i == -4 ? "" : i == -2 ? EmbeddedMapTab : help ? EmbeddedHelpTab : _embeddedTabs[i];
                string label = i == -4 ? "部屋" : i == -2 ? "マップ" : help ? "操作説明" : _embeddedTabs[i + 1];
                float w = Mathf.Min(width - 8f, Mathf.Clamp(GUI.skin.button.CalcSize(new GUIContent(label)).x + 12f, 54f, 150f));
                if (x > 4f && x + w > width - 4f) { x = 4f; y += 25f; }
                Color old = GUI.backgroundColor;
                if (key == _embeddedSelectedTab) GUI.backgroundColor = new Color(.6f, .85f, 1f);
                if (GUI.Button(new Rect(x, y, w, 23f), new GUIContent(label, label + "の設定を表示")))
                    _embeddedPendingTab = key;
                GUI.backgroundColor = old;
                x += w + 3f;
            }
            var viewport = new Rect(4f, y + 28f, Mathf.Max(1f, width - 8f), Mathf.Max(1f, _embeddedBarRect.height - y - 32f));
            _embeddedScroll.TryGetValue(_embeddedSelectedTab, out Vector2 scroll);
            if (_embeddedSelectedTab == EmbeddedMapTab && _mapTabPendingScrollReset)
            {
                scroll = Vector2.zero;
                _mapTabPendingScrollReset = false;
            }
            if (_embeddedSelectedTab.Length == 0)
            {
                float contentWidth = Mathf.Max(1000f, viewport.width - 18f);
                scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0, 0, contentWidth, 514f));
                try { _embeddedRoomDraw?.Invoke(contentWidth); }
                finally { GUI.EndScrollView(); }
            }
            else if (_embeddedSelectedTab == EmbeddedHelpTab)
            {
                GUILayout.BeginArea(viewport);
                try { MainGameUiInputCapture.InputBindingsApi.DrawEditor(viewport.width, viewport.height); }
                finally { GUILayout.EndArea(); }
            }
            else
            {
                GUILayout.BeginArea(viewport);
                try
                {
                    scroll = GUILayout.BeginScrollView(scroll, false, false, GUILayout.Width(viewport.width), GUILayout.Height(viewport.height));
                    try
                    {
                        GUILayout.BeginVertical(GUILayout.MinWidth(Mathf.Max(_embeddedSelectedTab == EmbeddedMapTab ? 500f : 800f, viewport.width - 22f)));
                        try
                        {
                            if (_embeddedSelectedTab == EmbeddedMapTab) DrawEmbeddedMapTab();
                            else _embeddedDrawTab?.Invoke(_embeddedSelectedTab);
                        }
                        finally { GUILayout.EndVertical(); }
                    }
                    finally { GUILayout.EndScrollView(); }
                }
                finally { GUILayout.EndArea(); }
            }
            _embeddedScroll[_embeddedSelectedTab] = scroll;
        }
    }
}
