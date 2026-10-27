using Bindings = CanonPlugins.Shared.PluginInput;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using CanonPlugins.Shared;
using MainGameUiInputCapture;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MainGameUnifiedUi
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    [BepInDependency("com.kks.maingame.uiinputcapture", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        public const string GUID = "com.kks.maingame.unifiedui";
        public const string PluginName = "MainGameUnifiedUi";
        public const string Version = "0.1.0";

        private const int WindowId = 0x554E4946; // 'UNIF'
        private const string InputOwnerKey = GUID + ".input";
        private const string InputSourceDrag = "drag";
        private const string InputSourceInteraction = "ui-interaction";
        private const float TitleBarHeight = 24f;
        private const float DefaultWindowWidth = 820f;
        private const float DefaultWindowHeight = 700f;
        private const float MaxContentViewportHeight = DefaultWindowHeight - TitleBarHeight - 48f;

        internal static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger { get; private set; }
        internal static string PluginDir { get; private set; }

        private sealed class TabEntry
        {
            public string Id;
            public string Label;
            public Action Draw;
            public int Order;
        }

        private readonly List<TabEntry> _tabs = new List<TabEntry>();
        private readonly List<TabEntry> _visibleTabs = new List<TabEntry>();
        private string _selectedId = string.Empty;
        private string[] _embeddedCatalog;

        internal string[] GetEmbeddedTabs()
        {
            if (!BlankRoomGate.IsInBlankRoom) return Array.Empty<string>();
            if (_embeddedCatalog == null)
            {
                _embeddedCatalog = new string[_tabs.Count * 2];
                for (int i = 0; i < _tabs.Count; i++) {
                    _embeddedCatalog[i * 2] = _tabs[i].Id;
                    _embeddedCatalog[i * 2 + 1] = _tabs[i].Label;
                }
            }
            return _embeddedCatalog;
        }

        internal void DrawEmbeddedTab(string id)
        {
            if (!BlankRoomGate.IsInBlankRoom) return;
            TabEntry tab = _tabs.Find(t => string.Equals(t.Id, id, StringComparison.Ordinal));
            if (tab == null) { GUILayout.Label("タブが利用できません"); return; }
            bool enabled = GUI.enabled;
            Color color = GUI.color, background = GUI.backgroundColor, content = GUI.contentColor;
            GUISkin skin = GUI.skin;
            try { tab.Draw?.Invoke(); }
            catch (ExitGUIException) { throw; }
            catch (Exception ex) { GUILayout.Label("タブ描画エラー: " + tab.Label); LogWarn("embedded tab " + id + ": " + ex); }
            finally { GUI.enabled = enabled; GUI.color = color; GUI.backgroundColor = background; GUI.contentColor = content; GUI.skin = skin; }
        }
        private const string PoseTabId = "hscene-pose";
        private bool _hSceneActive;
        private float _nextHSceneProbe;

        private bool CanShowUi => BlankRoomGate.IsInBlankRoom || _hSceneActive;

        private void RefreshVisibleTabs()
        {
            _visibleTabs.Clear();
            bool room = BlankRoomGate.IsInBlankRoom;
            foreach (TabEntry tab in _tabs)
                if (room || string.Equals(tab.Id, PoseTabId, StringComparison.Ordinal))
                    _visibleTabs.Add(tab);
            if (_visibleTabs.Count > 0 && !_visibleTabs.Exists(t => t.Id == _selectedId))
                _selectedId = _visibleTabs[0].Id;
        }

        private ConfigEntry<bool> _cfgUiVisible;
        private ConfigEntry<KeyboardShortcut> _cfgToggleKey;

        private bool _uiVisible;
        private Rect _windowRect;
        private Vector2 _scroll;
        private bool _windowPointerActive;
        private bool _windowDragging;
        private bool _uiInteractionActive;
        private Vector2 _dragPointerOffset;
        private Rect _contentLocalRect;
        private bool _hasMeasuredContentRect;
        private readonly List<Rect> _inputBlockRects = new List<Rect>(2);

        private string _logPath;
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object FileLogLock = new object();

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            PluginDir = Path.GetDirectoryName(Info.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            _logPath = Path.Combine(PluginDir, "MainGameUnifiedUi.log");
            ResetFileLog();

            _cfgUiVisible = Config.Bind("UI", "UiVisible", false, "Show the unified UI window.");
            _cfgToggleKey = Config.Bind("UI", "ToggleKey", new KeyboardShortcut(KeyCode.F8), "Toggle the unified UI window.");
            _uiVisible = _cfgUiVisible.Value;

            // 毎回左上から開始（位置は保存しない）。
            _windowRect = new Rect(20f, 20f, DefaultWindowWidth, DefaultWindowHeight);

            LogInfo(PluginName + " " + Version + " initialized");
            LogInfo("pluginDir=" + PluginDir);
        }

        private void OnDestroy()
        {
            ReleaseInputCapture();
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            if (Time.unscaledTime >= _nextHSceneProbe)
            {
                _nextHSceneProbe = Time.unscaledTime + 0.5f;
                _hSceneActive = UnityEngine.Object.FindObjectOfType<HSceneProc>() != null;
            }
            // 追加マップ外ではHシーン中のPoseタブだけを許可する。
            if (!CanShowUi)
            {
                if (_uiVisible)
                    CloseForBlankRoomExit();
                return;
            }

            if (_cfgToggleKey != null && Bindings.Down("MainGameUnifiedUi.toggle", _cfgToggleKey.Value.ToString()))
                SetVisible(!_uiVisible);

            UpdateWindowDrag();
            UpdateInputCapture();
        }

        /// <summary>
        /// 退室時の後始末。窓を閉じるだけでなく、掴んだ入力占有も必ず返す。
        /// ここで返し忘れると、部屋の外でマウスが利かないまま取り残される。
        /// </summary>
        private void CloseForBlankRoomExit()
        {
            SetVisible(false);
            ReleaseInputCapture();
            LogInfo("blank room left -> unified UI closed");
        }

        // ── 公開API実体 ───────────────────────────────────────────────

        internal void RegisterTab(string id, string label, Action draw, int order)
        {
            if (string.IsNullOrEmpty(id) || draw == null)
                return;

            for (int i = 0; i < _tabs.Count; i++)
            {
                if (string.Equals(_tabs[i].Id, id, StringComparison.Ordinal))
                {
                    string nextLabel = label ?? id;
                    if (string.Equals(_tabs[i].Label, nextLabel, StringComparison.Ordinal)
                        && _tabs[i].Order == order && Equals(_tabs[i].Draw, draw))
                        return;

                    _tabs[i].Label = nextLabel;
                    _tabs[i].Draw = draw;
                    _tabs[i].Order = order;
                    SortTabs();
                    return;
                }
            }

            _tabs.Add(new TabEntry { Id = id, Label = label ?? id, Draw = draw, Order = order });
            SortTabs();
            if (string.IsNullOrEmpty(_selectedId))
                _selectedId = id;
            LogInfo("tab registered: " + id + " (" + (label ?? id) + ")");
        }

        internal void UnregisterTab(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (string.Equals(_tabs[i].Id, id, StringComparison.Ordinal))
                {
                    _tabs.RemoveAt(i);
                    _embeddedCatalog = null;
                    if (string.Equals(_selectedId, id, StringComparison.Ordinal))
                        _selectedId = _tabs.Count > 0 ? _tabs[0].Id : string.Empty;
                    LogInfo("tab unregistered: " + id);
                    return;
                }
            }
        }

        internal bool IsHosting(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;
            for (int i = 0; i < _tabs.Count; i++)
            {
                if (string.Equals(_tabs[i].Id, id, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        internal bool ShowTab(string id)
        {
            // 追加マップ外からの他タブ表示要求は受け付けない。
            if (!CanShowUi || (!BlankRoomGate.IsInBlankRoom && id != PoseTabId))
                return false;
            if (!IsHosting(id))
                return false;
            if (!string.Equals(_selectedId, id, StringComparison.Ordinal))
                InvalidateMeasuredContentRect();
            _selectedId = id;
            SetVisible(true);
            return true;
        }

        internal bool IsVisible => _uiVisible;

        internal void SetVisible(bool visible)
        {
            if (_uiVisible == visible)
                return;
            _uiVisible = visible;
            if (_cfgUiVisible != null)
                _cfgUiVisible.Value = visible;
            if (!visible)
            {
                _windowDragging = false;
                _windowPointerActive = false;
                _uiInteractionActive = false;
                ClearInputBlockRectsImmediately();
            }
            else
                InvalidateMeasuredContentRect();
        }

        private void SortTabs()
        {
            _embeddedCatalog = null;
            _tabs.Sort((a, b) =>
            {
                int byOrder = a.Order.CompareTo(b.Order);
                return byOrder != 0 ? byOrder : string.Compare(a.Label, b.Label, StringComparison.Ordinal);
            });
        }

        // ── 窓描画 ────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (!_uiVisible || !CanShowUi)
                return;

            _windowRect.width = DefaultWindowWidth;
            _windowRect.height = DefaultWindowHeight;
            _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, PluginName);
        }

        private void DrawWindow(int id)
        {
            RefreshVisibleTabs();
            GUILayout.BeginVertical(GUILayout.ExpandHeight(false));

            if (_visibleTabs.Count == 0)
            {
                GUILayout.Space(8f);
                GUILayout.Label("登録タブなし。各プラグインがタブを登録するとここに並びます。");
            }
            else
            {
                int selectedIndex = Mathf.Max(0, IndexOfSelected());
                string[] labels = new string[_visibleTabs.Count];
                for (int i = 0; i < _visibleTabs.Count; i++)
                    labels[i] = _visibleTabs[i].Label;

                int next = GUILayout.Toolbar(selectedIndex, labels);
                if (next != selectedIndex)
                {
                    selectedIndex = Mathf.Clamp(next, 0, _visibleTabs.Count - 1);
                    _selectedId = _visibleTabs[selectedIndex].Id;
                    _scroll = Vector2.zero;
                    InvalidateMeasuredContentRect();
                }

                GUILayout.Space(4f);
                _scroll = GUILayout.BeginScrollView(
                    _scroll,
                    false,
                    true,
                    GUILayout.ExpandHeight(false),
                    GUILayout.MaxHeight(MaxContentViewportHeight));
                TabEntry active = _visibleTabs[Mathf.Clamp(selectedIndex, 0, _visibleTabs.Count - 1)];
                try
                {
                    active.Draw?.Invoke();
                }
                catch (Exception ex)
                {
                    // 1タブの例外で窓全体を巻き込まない。
                    GUILayout.Label("タブ描画エラー: " + active.Id);
                    GUILayout.Label(ex.Message);
                    LogWarn("tab draw error id=" + active.Id + " : " + ex);
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();

            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                Rect measured = GUILayoutUtility.GetLastRect();
                if (InputBlockGeometry.IsUsableRect(measured))
                {
                    _contentLocalRect = measured;
                    _hasMeasuredContentRect = true;
                    RefreshInputBlockRects();
                }
            }
        }

        private int IndexOfSelected()
        {
            for (int i = 0; i < _visibleTabs.Count; i++)
            {
                if (string.Equals(_visibleTabs[i].Id, _selectedId, StringComparison.Ordinal))
                    return i;
            }
            return _visibleTabs.Count > 0 ? 0 : -1;
        }

        // ── 自前ドラッグ＋入力ブロック（GUI.DragWindowは使わない）──────

        private void UpdateWindowDrag()
        {
            if (!_uiVisible)
            {
                _windowPointerActive = false;
                _windowDragging = false;
                _uiInteractionActive = false;
                return;
            }

            ClampWindowRectToScreen();
            RefreshInputBlockRects();

            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            _windowPointerActive = InputBlockGeometry.Contains(_inputBlockRects, guiMouse);

            if (Input.GetMouseButtonDown(0) && _windowPointerActive)
                _uiInteractionActive = true;
            else if (!Input.GetMouseButton(0))
                _uiInteractionActive = false;

            Rect titleBar = new Rect(_windowRect.x, _windowRect.y, _windowRect.width, TitleBarHeight);
            if (!_windowDragging)
            {
                if (Input.GetMouseButtonDown(0) && titleBar.Contains(guiMouse))
                {
                    _windowDragging = true;
                    _dragPointerOffset = guiMouse - new Vector2(_windowRect.x, _windowRect.y);
                }
                return;
            }

            if (!Input.GetMouseButton(0))
            {
                _windowDragging = false;
                return;
            }

            // マウスが画面外へ出たら掴みを自動で離す（ウィンドウが画面外へ飛ぶのを防ぐ）。
            Vector3 rawMouse = Input.mousePosition;
            if (rawMouse.x < 0f || rawMouse.x > Screen.width || rawMouse.y < 0f || rawMouse.y > Screen.height)
            {
                _windowDragging = false;
                return;
            }

            _windowRect.x = guiMouse.x - _dragPointerOffset.x;
            _windowRect.y = guiMouse.y - _dragPointerOffset.y;
            ClampWindowRectToScreen();
            RefreshInputBlockRects();
        }

        private void UpdateInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable)
                return;

            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, _uiVisible);

            if (_uiVisible)
                RefreshInputBlockRects();
            else
                UiInputCaptureApi.ClearInputBlockRects(InputOwnerKey);

            UiInputCaptureApi.Sync(InputOwnerKey, InputSourceDrag, _windowDragging);
            UiInputCaptureApi.Sync(InputOwnerKey, InputSourceInteraction, _uiInteractionActive);
        }

        private void RefreshInputBlockRects()
        {
            if (!UiInputCaptureApi.IsAvailable || !_uiVisible)
                return;

            InputBlockGeometry.BuildWindowInputRects(
                _windowRect,
                TitleBarHeight,
                _contentLocalRect,
                _hasMeasuredContentRect,
                Screen.width,
                Screen.height,
                _inputBlockRects);

            if (_inputBlockRects.Count > 0)
                UiInputCaptureApi.SetInputBlockRects(InputOwnerKey, _inputBlockRects);
            else
                UiInputCaptureApi.ClearInputBlockRects(InputOwnerKey);
        }

        private void ClearInputBlockRectsImmediately()
        {
            _inputBlockRects.Clear();
            if (!UiInputCaptureApi.IsAvailable)
                return;

            UiInputCaptureApi.ClearInputBlockRects(InputOwnerKey);
            UiInputCaptureApi.Sync(InputOwnerKey, InputSourceDrag, false);
            UiInputCaptureApi.Sync(InputOwnerKey, InputSourceInteraction, false);
        }

        private void InvalidateMeasuredContentRect()
        {
            _hasMeasuredContentRect = false;
            _contentLocalRect = new Rect();
        }

        private void ClampWindowRectToScreen()
        {
            float maxX = Mathf.Max(0f, Screen.width - _windowRect.width);
            float maxY = Mathf.Max(0f, Screen.height - TitleBarHeight);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, maxX);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, maxY);
        }

        private void ReleaseInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable)
                return;

            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, false);
            UiInputCaptureApi.ClearInputBlockRects(InputOwnerKey);
            UiInputCaptureApi.EndOwner(InputOwnerKey);
            _windowPointerActive = false;
            _windowDragging = false;
            _uiInteractionActive = false;
            _inputBlockRects.Clear();
        }

        // ── ログ ──────────────────────────────────────────────────────

        private void LogInfo(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger?.LogInfo(message);
            WriteFileLog("INFO", message);
        }

        private void LogWarn(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            Logger?.LogWarning(message);
            WriteFileLog("WARN", message);
        }

        private void ResetFileLog()
        {
            if (!_runtimeLoggingEnabled) return;
            try
            {
                Directory.CreateDirectory(PluginDir);
                File.WriteAllText(
                    _logPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [INFO] === session start ===" + Environment.NewLine,
                    Utf8NoBom);
            }
            catch
            {
            }
        }

        private void WriteFileLog(string level, string message)
        {
            if (!_runtimeLoggingEnabled) return;
            try
            {
                lock (FileLogLock)
                {
                    File.AppendAllText(
                        _logPath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [" + level + "] " + (message ?? string.Empty) + Environment.NewLine,
                        Utf8NoBom);
                }
            }
            catch
            {
            }
        }
    }
}
