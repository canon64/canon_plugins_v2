using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Collections.Generic;
using System.Globalization;
using MainGameUiInputCapture;
using StudioFacePresetTool.Services;
using UnityEngine;

namespace StudioFacePresetTool.UI
{
    internal sealed class FacePresetWindowController
    {
        private const int WindowId = 0x53465054;
        private const string SrcWindowDrag = "window-drag";
        private const string SrcBodyDrag = "body-drag";
        private const string SrcScrollDrag = "scroll-drag";

        private readonly PluginLogger _log;
        private readonly StudioFaceRuntime _runtime;
        private readonly FacePresetSettingsStore _settingsStore;
        private readonly FacePresetStore _presetStore;
        private readonly string _inputOwner;

        private readonly List<FacePreset> _presets;
        private FacePresetToolSettings _settings;

        private Rect _windowRect;
        private Vector2 _listScroll;
        private string _statusText = string.Empty;
        private float _statusExpireTime;

        private bool _uiVisible;
        private bool _idleUnlockEnabled;
        private KeyCode _toggleKey;

        private bool _windowDragging;
        private bool _bodyDragging;
        private bool _scrollDragging;
        private bool _captureReleased;

        private int _selectedIndex;

        private string _name = string.Empty;
        private string _eyebrow = "0";
        private string _eye = "0";
        private string _mouth = "0";
        private string _eyeMin = "0.00";
        private string _eyeMax = "1.00";
        private string _mouthMin = "0.00";
        private string _mouthMax = "1.00";
        private string _tears = "0";
        private string _cheek = "0.00";

        internal FacePresetWindowController(
            PluginLogger log,
            StudioFaceRuntime runtime,
            FacePresetSettingsStore settingsStore,
            FacePresetStore presetStore,
            string inputOwner)
        {
            _log = log;
            _runtime = runtime;
            _settingsStore = settingsStore;
            _presetStore = presetStore;
            _inputOwner = inputOwner;

            _settings = _settingsStore.LoadOrCreate() ?? new FacePresetToolSettings();
            _presets = _presetStore.LoadOrCreate() ?? new List<FacePreset>();

            _windowRect = new Rect(
                _settings.Window?.X ?? 40f,
                _settings.Window?.Y ?? 40f,
                _settings.Window?.Width ?? 760f,
                _settings.Window?.Height ?? 620f);

            _uiVisible = _settings.UiVisible;
            _idleUnlockEnabled = _settings.IdleCursorUnlock;
            _selectedIndex = NormalizeSelectedIndex(_settings.SelectedPresetIndex);
            _toggleKey = ParseKeyCode(_settings.ToggleUiKey, KeyCode.F9);
            _captureReleased = false;

            bool hasSelected = _selectedIndex >= 0 && _selectedIndex < _presets.Count;
            _log.Branch("ctor-has-selected", hasSelected, "selectedIndex=" + _selectedIndex + " presets=" + _presets.Count);
            if (hasSelected)
            {
                PopulateInputFromPreset(_presets[_selectedIndex]);
            }

            _log.Info("window controller initialized uiVisible=" + _uiVisible + " presets=" + _presets.Count + " toggle=" + _toggleKey);
        }

        internal bool UiVisible => _uiVisible;

        internal void SetUiVisible(bool visible)
        {
            _log.Entry(nameof(SetUiVisible));
            _uiVisible = visible;
            if (_uiVisible)
                _captureReleased = false;
            _log.Info("ui visible changed=" + _uiVisible);
            if (!_uiVisible)
            {
                ReleaseCaptureFlags("ui-hidden");
            }
        }

        internal void SetIdleCursorUnlock(bool enabled)
        {
            _log.Entry(nameof(SetIdleCursorUnlock));
            _idleUnlockEnabled = enabled;
            _log.Info("idle cursor unlock changed=" + _idleUnlockEnabled);
            if (!_idleUnlockEnabled)
            {
                UiInputCaptureApi.SetIdleCursorUnlock(_inputOwner, false);
            }
        }

        internal void SetToggleKey(string keyText)
        {
            _log.Entry(nameof(SetToggleKey));
            _toggleKey = ParseKeyCode(keyText, _toggleKey);
            _log.Info("toggle key changed=" + _toggleKey);
        }

        internal void Tick()
        {
            bool hotkeyPressed = Bindings.Down("StudioFacePresetTool.toggle", _toggleKey.ToString());
            if (hotkeyPressed)
            {
                _uiVisible = !_uiVisible;
                _log.Info("ui toggled via hotkey visible=" + _uiVisible);
                ShowStatus(_uiVisible ? "UIを表示" : "UIを非表示");
            }

            bool leftDown = Input.GetMouseButton(0);
            if (!leftDown)
            {
                bool hadDragFlags = _windowDragging || _bodyDragging || _scrollDragging;
                _windowDragging = false;
                _bodyDragging = false;
                _scrollDragging = false;
                if (hadDragFlags)
                {
                    _log.Info("drag flags reset by mouse release");
                }
            }

            if (_uiVisible)
            {
                _captureReleased = false;
                if (_idleUnlockEnabled)
                {
                    UiInputCaptureApi.SetIdleCursorUnlock(_inputOwner, true);
                }

                UiInputCaptureApi.Sync(_inputOwner, SrcWindowDrag, _windowDragging);
                UiInputCaptureApi.Sync(_inputOwner, SrcBodyDrag, _bodyDragging);
                UiInputCaptureApi.Sync(_inputOwner, SrcScrollDrag, _scrollDragging);
            }
            else
            {
                ReleaseCaptureFlags("tick-ui-not-visible");
            }
        }

        internal void OnGui()
        {
            if (!_uiVisible)
                return;

            GUI.depth = 0;
            _windowRect = GUILayout.Window(WindowId, _windowRect, DrawWindow, "Studio Face Preset Tool");
        }

        internal void Shutdown()
        {
            _log.Entry(nameof(Shutdown));
            SaveAll();
            ReleaseCaptureFlags("shutdown");
            UiInputCaptureApi.SetIdleCursorUnlock(_inputOwner, false);
            UiInputCaptureApi.EndOwner(_inputOwner);
            _log.Info("window controller shutdown completed");
        }

        private void DrawWindow(int id)
        {
            Event ev = Event.current;
            HandleWindowEventFlags(ev);

            GUILayout.BeginVertical();

            DrawHeader();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            DrawPresetList();
            DrawEditorPanel();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            DrawBottomActions();

            GUILayout.EndVertical();

            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width, 22f));
        }

        private void DrawHeader()
        {
            bool studioReady = _runtime.IsStudioReady();

            string left = studioReady ? "Studio: Ready" : "Studio: NotReady";
            string center = "Selected=" + (_selectedIndex >= 0 ? _selectedIndex.ToString() : "none");
            string right = "Presets=" + _presets.Count;

            GUILayout.BeginHorizontal();
            GUILayout.Label(left, GUILayout.Width(160f));
            GUILayout.Label(center, GUILayout.Width(160f));
            GUILayout.Label(right, GUILayout.Width(160f));
            GUILayout.EndHorizontal();

            bool hasStatus = !string.IsNullOrEmpty(_statusText) && Time.unscaledTime <= _statusExpireTime;
            if (hasStatus)
            {
                GUILayout.Label(_statusText);
            }
        }

        private void DrawPresetList()
        {
            GUILayout.BeginVertical(GUILayout.Width(280f));
            GUILayout.Label("Preset List");

            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Width(280f), GUILayout.Height(420f));

            bool hasPreset = _presets.Count > 0;
            if (!hasPreset)
            {
                GUILayout.Label("(empty)");
            }
            else
            {
                for (int i = 0; i < _presets.Count; i++)
                {
                    FacePreset p = _presets[i];
                    string label = i + ": " + (p == null ? "<null>" : p.Name);
                    bool clicked = GUILayout.Button(label, GUILayout.Height(28f));
                    if (clicked)
                    {
                        _selectedIndex = i;
                        _log.Info("preset selected index=" + i + " name=" + (p?.Name ?? "<null>"));
                        if (p != null)
                        {
                            PopulateInputFromPreset(p);
                            ShowStatus("プリセット選択: " + p.Name);
                        }
                    }
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawEditorPanel()
        {
            GUILayout.BeginVertical();
            GUILayout.Label("Editor");

            DrawTextRow("Name", ref _name);
            DrawTextRow("Eyebrow", ref _eyebrow);
            DrawTextRow("Eye", ref _eye);
            DrawTextRow("Mouth", ref _mouth);
            DrawTextRow("EyeMin", ref _eyeMin);
            DrawTextRow("EyeMax", ref _eyeMax);
            DrawTextRow("MouthMin", ref _mouthMin);
            DrawTextRow("MouthMax", ref _mouthMax);
            DrawTextRow("Tears", ref _tears);
            DrawTextRow("Cheek", ref _cheek);

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("現在値取得", GUILayout.Height(32f), GUILayout.Width(110f)))
                HandleCaptureCurrent();
            if (GUILayout.Button("新規クリア", GUILayout.Height(32f), GUILayout.Width(110f)))
                ClearEditor();
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private static void DrawTextRow(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90f));
            value = GUILayout.TextField(value ?? string.Empty, GUILayout.Width(180f));
            GUILayout.EndHorizontal();
        }

        private void DrawBottomActions()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存/更新", GUILayout.Height(34f), GUILayout.Width(110f)))
                HandleSaveOrUpdate();
            if (GUILayout.Button("選択適用", GUILayout.Height(34f), GUILayout.Width(110f)))
                HandleApplySelected();
            if (GUILayout.Button("削除", GUILayout.Height(34f), GUILayout.Width(110f)))
                HandleRemoveSelected();
            if (GUILayout.Button("ファイル保存", GUILayout.Height(34f), GUILayout.Width(110f)))
                HandleManualSave();
            GUILayout.EndHorizontal();
        }

        private void HandleWindowEventFlags(Event ev)
        {
            bool hasEvent = ev != null;
            if (!hasEvent)
                return;

            Rect titleBar = new Rect(0f, 0f, _windowRect.width, 20f);
            Rect bodyArea = new Rect(0f, 20f, _windowRect.width - 18f, _windowRect.height - 20f);
            Rect scrollArea = new Rect(_windowRect.width - 18f, 20f, 18f, _windowRect.height - 20f);

            if (ev.rawType == EventType.MouseDown && ev.button == 0 && titleBar.Contains(ev.mousePosition))
            {
                if (!_windowDragging)
                {
                    _windowDragging = true;
                    _log.Info("window drag on pos=" + ev.mousePosition);
                }
            }
            else if (ev.rawType == EventType.MouseUp && ev.button == 0)
            {
                if (_windowDragging)
                {
                    _windowDragging = false;
                    _log.Info("window drag off pos=" + ev.mousePosition);
                }
            }

            if (ev.rawType == EventType.MouseDown && ev.button == 0 && bodyArea.Contains(ev.mousePosition))
            {
                if (!_bodyDragging)
                {
                    _bodyDragging = true;
                    _log.Info("body drag on pos=" + ev.mousePosition);
                }
            }
            else if (ev.rawType == EventType.MouseUp && ev.button == 0)
            {
                if (_bodyDragging)
                {
                    _bodyDragging = false;
                    _log.Info("body drag off pos=" + ev.mousePosition);
                }
            }

            if (ev.rawType == EventType.MouseDown && ev.button == 0 && scrollArea.Contains(ev.mousePosition))
            {
                if (!_scrollDragging)
                {
                    _scrollDragging = true;
                    _log.Info("scroll drag on pos=" + ev.mousePosition);
                }
            }
            else if (ev.rawType == EventType.MouseUp && ev.button == 0)
            {
                if (_scrollDragging)
                {
                    _scrollDragging = false;
                    _log.Info("scroll drag off pos=" + ev.mousePosition);
                }
            }
        }

        private void HandleCaptureCurrent()
        {
            _log.Entry(nameof(HandleCaptureCurrent));
            bool ok = _runtime.TryCaptureFromSelected(out FacePreset captured, out string message);
            _log.Branch("capture-result", ok, message ?? string.Empty);
            if (!ok)
            {
                ShowStatus(message);
                return;
            }

            PopulateInputFromPreset(captured);
            ShowStatus(message);
        }

        private void HandleSaveOrUpdate()
        {
            _log.Info("preset-save-ui/start inputName=" + ((_name ?? string.Empty).Trim()) + " selected=" + _selectedIndex + " count=" + _presets.Count);
            bool ok = TryBuildPresetFromInput(out FacePreset preset, out string message);
            if (!ok)
            {
                _log.Warn("preset-save-ui/validate-failed reason=" + (message ?? string.Empty));
                ShowStatus(message);
                return;
            }

            int index = FindPresetIndexByName(preset.Name);
            bool exists = index >= 0;
            string successStatus;
            if (exists)
            {
                preset.Id = _presets[index].Id;
                _presets[index] = preset;
                _selectedIndex = index;
                successStatus = "更新: " + preset.Name;
                _log.Info("preset-save-ui/mode=update index=" + index + " id=" + preset.Id);
            }
            else
            {
                preset.Id = Guid.NewGuid().ToString("N");
                _presets.Add(preset);
                _selectedIndex = _presets.Count - 1;
                successStatus = "追加: " + preset.Name;
                _log.Info("preset-save-ui/mode=add index=" + _selectedIndex + " id=" + preset.Id);
            }

            if (!SavePresets("save-or-update", out string saveError))
            {
                ShowStatus("保存失敗: " + saveError);
                return;
            }

            ShowStatus(successStatus);
            _log.Info("preset-save-ui/end ok selected=" + _selectedIndex + " count=" + _presets.Count);
        }

        private void HandleManualSave()
        {
            _log.Info("preset-manual-save/start selected=" + _selectedIndex + " count=" + _presets.Count);
            if (!SavePresets("manual-save", out string saveError))
            {
                ShowStatus("ファイル保存失敗: " + saveError);
                return;
            }

            ShowStatus("ファイル保存完了");
        }

        private void HandleApplySelected()
        {
            _log.Entry(nameof(HandleApplySelected));
            bool hasValidSelection = _selectedIndex >= 0 && _selectedIndex < _presets.Count;
            _log.Branch("apply-has-valid-selection", hasValidSelection, "selected=" + _selectedIndex);
            if (!hasValidSelection)
            {
                ShowStatus("選択プリセットがありません");
                return;
            }

            FacePreset preset = _presets[_selectedIndex];
            int applied = _runtime.ApplyToSelected(preset, out string message);
            bool appliedAny = applied > 0;
            _log.Branch("apply-any", appliedAny, message ?? string.Empty);
            ShowStatus(message);
        }

        private void HandleRemoveSelected()
        {
            _log.Entry(nameof(HandleRemoveSelected));
            bool hasValidSelection = _selectedIndex >= 0 && _selectedIndex < _presets.Count;
            _log.Branch("remove-has-valid-selection", hasValidSelection, "selected=" + _selectedIndex);
            if (!hasValidSelection)
            {
                ShowStatus("削除対象がありません");
                return;
            }

            string removedName = _presets[_selectedIndex]?.Name ?? "(null)";
            _presets.RemoveAt(_selectedIndex);
            _selectedIndex = NormalizeSelectedIndex(_selectedIndex);

            bool hasNextSelected = _selectedIndex >= 0 && _selectedIndex < _presets.Count;
            _log.Branch("remove-has-next-selected", hasNextSelected, "selected=" + _selectedIndex);
            if (hasNextSelected)
            {
                PopulateInputFromPreset(_presets[_selectedIndex]);
            }

            if (!SavePresets("remove-selected", out string saveError))
            {
                ShowStatus("削除後の保存失敗: " + saveError);
                return;
            }

            ShowStatus("削除: " + removedName);
        }

        private void SaveAll()
        {
            _log.Entry(nameof(SaveAll));
            SavePresets("shutdown", out _);
            SaveSettings();
        }

        private bool SavePresets(string reason, out string errorMessage)
        {
            _log.Info("preset-save-file/start reason=" + reason + " count=" + _presets.Count);
            bool ok = _presetStore.Save(_presets, out errorMessage);
            if (ok)
            {
                _log.Info("preset-save-file/ok reason=" + reason + " count=" + _presets.Count);
            }
            else
            {
                _log.Error("preset-save-file/fail reason=" + reason + " detail=" + errorMessage);
            }

            return ok;
        }

        private void SaveSettings()
        {
            _log.Entry(nameof(SaveSettings));
            _settings.UiVisible = _uiVisible;
            _settings.IdleCursorUnlock = _idleUnlockEnabled;
            _settings.ToggleUiKey = _toggleKey.ToString();
            _settings.SelectedPresetIndex = _selectedIndex;

            if (_settings.Window == null)
                _settings.Window = new WindowRectData();

            _settings.Window.X = _windowRect.x;
            _settings.Window.Y = _windowRect.y;
            _settings.Window.Width = _windowRect.width;
            _settings.Window.Height = _windowRect.height;

            _settingsStore.Save(_settings);
        }

        private void PopulateInputFromPreset(FacePreset preset)
        {
            _log.Entry(nameof(PopulateInputFromPreset));
            bool presetNull = preset == null;
            _log.Branch("populate-preset-null", presetNull, string.Empty);
            if (presetNull)
                return;

            _name = preset.Name ?? string.Empty;
            _eyebrow = preset.Eyebrow.ToString(CultureInfo.InvariantCulture);
            _eye = preset.Eye.ToString(CultureInfo.InvariantCulture);
            _mouth = preset.Mouth.ToString(CultureInfo.InvariantCulture);
            _eyeMin = preset.EyeMin.ToString("0.##", CultureInfo.InvariantCulture);
            _eyeMax = preset.EyeMax.ToString("0.##", CultureInfo.InvariantCulture);
            _mouthMin = preset.MouthMin.ToString("0.##", CultureInfo.InvariantCulture);
            _mouthMax = preset.MouthMax.ToString("0.##", CultureInfo.InvariantCulture);
            _tears = preset.Tears.ToString(CultureInfo.InvariantCulture);
            _cheek = preset.Cheek.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void ClearEditor()
        {
            _log.Entry(nameof(ClearEditor));
            _name = string.Empty;
            _eyebrow = "0";
            _eye = "0";
            _mouth = "0";
            _eyeMin = "0.00";
            _eyeMax = "1.00";
            _mouthMin = "0.00";
            _mouthMax = "1.00";
            _tears = "0";
            _cheek = "0.00";
            _selectedIndex = -1;
            ShowStatus("入力をクリアしました");
        }

        private bool TryBuildPresetFromInput(out FacePreset preset, out string message)
        {
            _log.Entry(nameof(TryBuildPresetFromInput));
            preset = null;
            message = string.Empty;

            string name = (_name ?? string.Empty).Trim();
            bool hasName = !string.IsNullOrWhiteSpace(name);
            _log.Branch("build-has-name", hasName, name);
            if (!hasName)
            {
                message = "Nameは必須";
                return false;
            }

            if (!TryParseInt(_eyebrow, out int eyebrow, "Eyebrow", out message))
                return false;
            if (!TryParseInt(_eye, out int eye, "Eye", out message))
                return false;
            if (!TryParseInt(_mouth, out int mouth, "Mouth", out message))
                return false;
            if (!TryParseFloat(_eyeMin, out float eyeMin, "EyeMin", out message))
                return false;
            if (!TryParseFloat(_eyeMax, out float eyeMax, "EyeMax", out message))
                return false;
            if (!TryParseFloat(_mouthMin, out float mouthMin, "MouthMin", out message))
                return false;
            if (!TryParseFloat(_mouthMax, out float mouthMax, "MouthMax", out message))
                return false;
            if (!TryParseInt(_tears, out int tears, "Tears", out message))
                return false;
            if (!TryParseFloat(_cheek, out float cheek, "Cheek", out message))
                return false;

            eyeMin = Mathf.Clamp01(eyeMin);
            eyeMax = Mathf.Clamp01(Mathf.Max(eyeMin, eyeMax));
            mouthMin = Mathf.Clamp01(mouthMin);
            mouthMax = Mathf.Clamp01(Mathf.Max(mouthMin, mouthMax));
            tears = Mathf.Clamp(tears, 0, 10);
            cheek = Mathf.Clamp01(cheek);

            preset = new FacePreset
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Eyebrow = Mathf.Max(0, eyebrow),
                Eye = Mathf.Max(0, eye),
                Mouth = Mathf.Max(0, mouth),
                EyeMin = Round2(eyeMin),
                EyeMax = Round2(eyeMax),
                MouthMin = Round2(mouthMin),
                MouthMax = Round2(mouthMax),
                Tears = tears,
                Cheek = Round2(cheek)
            };

            return true;
        }

        private bool TryParseInt(string text, out int value, string label, out string message)
        {
            _log.Entry(nameof(TryParseInt));
            message = string.Empty;
            bool ok = int.TryParse((text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            _log.Branch("parse-int", ok, label + "=" + text);
            if (!ok)
            {
                message = label + " は整数で入力";
                return false;
            }

            return true;
        }

        private bool TryParseFloat(string text, out float value, string label, out string message)
        {
            _log.Entry(nameof(TryParseFloat));
            message = string.Empty;
            bool ok = float.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            _log.Branch("parse-float", ok, label + "=" + text);
            if (!ok)
            {
                message = label + " は小数で入力";
                return false;
            }

            return true;
        }

        private int FindPresetIndexByName(string name)
        {
            _log.Entry(nameof(FindPresetIndexByName));
            for (int i = 0; i < _presets.Count; i++)
            {
                string candidate = _presets[i]?.Name ?? string.Empty;
                if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }

            return -1;
        }

        private int NormalizeSelectedIndex(int candidate)
        {
            _log.Entry(nameof(NormalizeSelectedIndex));
            if (_presets.Count == 0)
                return -1;

            if (candidate < 0)
                return 0;

            if (candidate >= _presets.Count)
                return _presets.Count - 1;

            return candidate;
        }

        private static float Round2(float value)
        {
            return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        private KeyCode ParseKeyCode(string raw, KeyCode fallback)
        {
            _log.Entry(nameof(ParseKeyCode));
            bool rawEmpty = string.IsNullOrWhiteSpace(raw);
            _log.Branch("parse-key-raw-empty", rawEmpty, raw ?? string.Empty);
            if (rawEmpty)
                return fallback;

            try
            {
                KeyCode parsed = (KeyCode)Enum.Parse(typeof(KeyCode), raw.Trim(), true);
                _log.Info("toggle key parsed=" + parsed);
                return parsed;
            }
            catch (Exception ex)
            {
                _log.Error("toggle key parse failed raw=" + raw, ex);
                return fallback;
            }
        }

        private void ShowStatus(string text)
        {
            _log.Entry(nameof(ShowStatus));
            _statusText = text ?? string.Empty;
            _statusExpireTime = Time.unscaledTime + 4f;
            _log.Info("status=" + _statusText);
        }

        private void ReleaseCaptureFlags(string reason)
        {
            if (_captureReleased)
                return;

            _windowDragging = false;
            _bodyDragging = false;
            _scrollDragging = false;
            UiInputCaptureApi.Sync(_inputOwner, SrcWindowDrag, false);
            UiInputCaptureApi.Sync(_inputOwner, SrcBodyDrag, false);
            UiInputCaptureApi.Sync(_inputOwner, SrcScrollDrag, false);
            UiInputCaptureApi.SetIdleCursorUnlock(_inputOwner, false);
            _captureReleased = true;
            _log.Info("capture released reason=" + reason);
        }
    }
}
