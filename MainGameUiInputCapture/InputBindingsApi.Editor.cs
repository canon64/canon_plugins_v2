using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Bootstrap;
using UnityEngine;

namespace MainGameUiInputCapture
{
    public static partial class InputBindingsApi
    {
        private const string EditorOwner = "MainGameUiInputCapture.BindingEditor";
        private static string _search = string.Empty, _editingId, _draft = string.Empty, _recordingId;
        private static bool _conflictsOnly, _fieldFocused;
        private static int _drawnFrame = -10, _recordStartFrame, _suppressUntilFrame = -1;
        private static string _modifierCandidate;
        private static Vector2 _operationsScroll;
        private static bool _showNoBindings, _showInactive, _showTechnical;
        internal static bool InputSuppressed => Time.frameCount <= _suppressUntilFrame
            || !string.IsNullOrEmpty(_recordingId) || (_fieldFocused && Time.frameCount - _drawnFrame <= 2);

        /// <summary>Call once per frame from the shared input host, before any hotkey evaluation.</summary>
        public static void TickEditor()
        {
            if (string.IsNullOrEmpty(_recordingId)) return;
            if (!Application.isFocused || Time.frameCount - _drawnFrame > 3)
            {
                StopRecording();
                return;
            }
            UiInputCaptureApi.Sync(EditorOwner, "record", true);
            UiInputCaptureApi.SetIdleCursorUnlock(EditorOwner, true);
            if (Time.frameCount <= _recordStartFrame + 1) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { StopRecording(); return; }
            foreach (KeyCode key in AllKeys)
            {
                if (key == KeyCode.None || !Input.GetKeyDown(key)) continue;
                if (IsModifier(key.ToString())) { _modifierCandidate = key.ToString(); continue; }
                var tokens = new List<string> { key.ToString() };
                foreach (KeyCode modifier in new[] { KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt })
                    if (Input.GetKey(modifier)) tokens.Add(modifier.ToString());
                _draft = string.Join(" + ", tokens);
                StopRecording();
                return;
            }
            if (!string.IsNullOrEmpty(_modifierCandidate) && Enum.TryParse(_modifierCandidate, out KeyCode candidate) && Input.GetKeyUp(candidate))
            {
                _draft = _modifierCandidate;
                StopRecording();
            }
            if (Mathf.Abs(Input.GetAxisRaw("Mouse ScrollWheel")) > 0.001f)
            {
                _draft = "Wheel";
                foreach (KeyCode modifier in new[] { KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt })
                    if (Input.GetKey(modifier)) _draft += " + " + modifier;
                StopRecording();
            }
        }

        private static void StopRecording()
        {
            _recordingId = null;
            _modifierCandidate = null;
            _suppressUntilFrame = Time.frameCount + 1;
            UiInputCaptureApi.Sync(EditorOwner, "record", false);
            UiInputCaptureApi.SetIdleCursorUnlock(EditorOwner, false);
        }

        public static void CancelEditor()
        {
            StopRecording();
            _fieldFocused = false;
            _drawnFrame = -10;
        }

        public static void DrawEditor() => DrawEditor(800f, 600f);

        public static void DrawEditor(float width, float height)
        {
            EnsureLoaded();
            _drawnFrame = Time.frameCount;
            var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
            var rowStyle = new GUIStyle(GUI.skin.label) { wordWrap = false };
            GUILayout.BeginHorizontal(GUILayout.Height(26f));
            GUILayout.Label("検索", GUILayout.Width(32f));
            GUI.SetNextControlName("bindings:search");
            _search = GUILayout.TextField(_search ?? string.Empty);
            _conflictsOnly = GUILayout.Toggle(_conflictsOnly, "重複だけ", GUILayout.Width(86f));
            if (GUILayout.Button("再読込", GUILayout.Width(66f))) Reload();
            GUILayout.Label(Definitions.Count + "操作", GUILayout.Width(58f));
            GUILayout.EndHorizontal();

            var conflicts = Definitions.Where(d => d.Status != "gate-disabled")
                .GroupBy(d => Canonical(Resolve(d.Id))).Where(g => g.Key.Length > 0 && g.Count() > 1)
                .ToDictionary(g => g.Key, g => string.Join("、", g.Select(x => x.Plugin + " / " + x.Title).ToArray()));
            string previousPlugin = null;
            int shown = 0;
            _operationsScroll = GUILayout.BeginScrollView(_operationsScroll, false, false,
                GUILayout.Width(Mathf.Max(100f, width)), GUILayout.Height(Mathf.Max(40f, height - 32f)));
            try
            {
            foreach (var d in Definitions)
            {
                string value = Resolve(d.Id);
                string canonical = Canonical(value);
                bool conflict = canonical.Length > 0 && conflicts.ContainsKey(canonical);
                if (_conflictsOnly && !conflict) continue;
                if (!string.IsNullOrWhiteSpace(_search) && (PluginLabel(d.Plugin) + " " + d.Plugin + " " + d.Title + " " + d.Description + " " + value + " " + InputLabel(value)).IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (previousPlugin != d.Plugin)
                {
                    GUILayout.Space(4f);
                    string guid = InputBindingCatalog.PluginGuids.TryGetValue(d.Plugin, out var g) ? g : string.Empty;
                    bool loaded = Chainloader.PluginInfos.ContainsKey(guid);
                    GUILayout.Label(new GUIContent(PluginLabel(d.Plugin) + (loaded ? string.Empty : "（未ロード）"), d.Plugin), GUILayout.Height(23f));
                    previousPlugin = d.Plugin;
                }
                shown++;
                bool expanded = _editingId == d.Id;
                string summary = (expanded ? "▼ " : "▶ ") + d.Title + "  [" + InputLabel(value) + "]";
                if (GUILayout.Button(new GUIContent(summary, "押すと説明と割り当て編集を開閉"), rowStyle, GUILayout.Height(25f)))
                {
                    StopRecording();
                    _editingId = expanded ? null : d.Id;
                    _draft = value;
                    _showTechnical = false;
                    GUI.FocusControl(null);
                }
                if (_editingId == d.Id)
                {
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(DescriptionLabel(d.Description), wrap);
                    if (conflict)
                    {
                        var others = Definitions.Where(x => x.Id != d.Id && x.Status != "gate-disabled" && Canonical(Resolve(x.Id)) == canonical);
                        GUILayout.Label("同じ割り当て: " + string.Join("、", others.Select(x => PluginLabel(x.Plugin) + " / " + x.Title).ToArray()), wrap);
                    }
                    if (d.Status == "gate-disabled") GUILayout.Label("開発用の機能が無効なため、現在は実行されません。", wrap);
                    string[] choices = d.Kind == "vr" ? new[] { "Grip", "Trigger", "Axis0", "A", "ApplicationMenu", "System" }
                        : d.Kind == "axis" ? new[] { "Wheel", "MouseX", "MouseY" }
                        : d.Kind == "stick" ? new[] { "StickX", "StickY", "-StickX", "-StickY" } : null;
                    if (choices != null)
                    {
                        TryParse(_draft, d.Kind, out var currentChords, out _);
                        Chord current = currentChords.FirstOrDefault();
                        int oldChoice = current == null ? -1 : Array.FindIndex(choices, x => x == current.Main);
                        int nextChoice = GUILayout.SelectionGrid(oldChoice, choices.Select(InputLabel).ToArray(), 3);
                        if (nextChoice >= 0 && nextChoice != oldChoice)
                            _draft = choices[nextChoice] + (current != null && current.Modifiers.Count > 0 ? " + " + string.Join(" + ", current.Modifiers) : string.Empty);
                    }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("割り当て", GUILayout.Width(62f));
                    GUI.SetNextControlName("bindings:" + d.Id);
                    _draft = GUILayout.TextField(_draft ?? string.Empty, GUILayout.MaxWidth(440f));
                    if (d.Kind != "vr" && d.Kind != "stick" && d.Kind != "axis" && GUILayout.Button(_recordingId == d.Id ? "記録中（Escで中止）" : "入力を記録", GUILayout.Width(160f)))
                    {
                        if (_recordingId == d.Id) StopRecording();
                        else { _recordingId = d.Id; _recordStartFrame = Time.frameCount; _modifierCandidate = null; GUI.FocusControl(null); }
                    }
                    if (GUILayout.Button("適用", GUILayout.Width(60f))) { StopRecording(); if (Set(d.Id, _draft)) GUI.FocusControl(null); }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    GUILayout.Label("割り当て: " + InputLabel(_draft), wrap);
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("割当なし", GUILayout.Width(82f))) { StopRecording(); if (Set(d.Id, "None")) _draft = "None"; }
                    if (GUILayout.Button("既定値に戻す", GUILayout.Width(108f))) { StopRecording(); if (Set(d.Id, d.Default)) _draft = d.Default; }
                    if (GUILayout.Button("元の個別設定を使う", GUILayout.Width(142f))) { StopRecording(); if (Set(d.Id, null, true)) _draft = Resolve(d.Id); }
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                    if (!string.IsNullOrEmpty(Error)) GUILayout.Label(Error, wrap);
                    _showTechnical = GUILayout.Toggle(_showTechnical, "入力名の書き方・元設定の詳細");
                    if (_showTechnical)
                    {
                        GUILayout.Label(d.Kind == "vr" ? "VR: Grip / Trigger / Axis0 / Axis1 / A / ApplicationMenu。例: Axis0 + Trigger" : d.Kind == "stick" ? "軸: StickX / StickY / -StickX / -StickY" : d.Kind == "axis" ? "軸: Wheel / MouseX / MouseY。例: Wheel + Shift" : "主キー: F1〜F12 / A〜Z / Return / Mouse0〜Mouse6。修飾: Ctrl / Shift / Alt。候補の併用: Return | KeypadEnter。Noneは割当なし。", wrap);
                        GUILayout.Label("既定: " + InputLabel(d.Default) + " / 元設定: " + InputLabel(d.Baseline) + "\n" + d.Plugin + " / " + d.Source, wrap);
                    }
                    GUILayout.EndVertical();
                }
            }
            GUILayout.Label("表示 " + shown + " / 登録 " + Definitions.Count + " 操作", wrap);
            if (!_conflictsOnly)
            {
                GUILayout.Space(8f);
                if (GUILayout.Button((_showNoBindings ? "▼ " : "▶ ") + "独自のショートカット割当がないプラグイン", rowStyle, GUILayout.Height(25f))) _showNoBindings = !_showNoBindings;
                if (_showNoBindings)
                foreach (string name in InputBindingCatalog.PluginGuids.Keys.OrderBy(x => x))
                {
                    if (Definitions.Any(d => d.Plugin == name)) continue;
                    if (!string.IsNullOrWhiteSpace(_search) && name.IndexOf(_search.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                    GUILayout.Label(PluginLabel(name) + "：独自割当なし", wrap);
                }
                GUILayout.Space(8f);
                if (GUILayout.Button((_showInactive ? "▼ " : "▶ ") + "使われていない旧設定", rowStyle, GUILayout.Height(25f))) _showInactive = !_showInactive;
                if (_showInactive) foreach (string note in InputBindingCatalog.Inactive) GUILayout.Label(note, wrap);
            }
            }
            finally { GUILayout.EndScrollView(); }
            _fieldFocused = (GUI.GetNameOfFocusedControl() ?? string.Empty).StartsWith("bindings:", StringComparison.Ordinal);
        }

        private static string InputLabel(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || input == "None" || input == "Not set") return "割当なし";
            string value = input.Replace("LeftControl", "左Ctrl").Replace("RightControl", "右Ctrl")
                .Replace("LeftShift", "左Shift").Replace("RightShift", "右Shift").Replace("LeftAlt", "左Alt").Replace("RightAlt", "右Alt")
                .Replace("Mouse0", "左ボタン").Replace("Mouse1", "右ボタン").Replace("Mouse2", "中ボタン")
                .Replace("MouseX", "マウス左右").Replace("MouseY", "マウス上下").Replace("Wheel", "ホイール")
                .Replace("KeypadEnter", "テンキーEnter").Replace("Return", "Enter").Replace("Escape", "Esc")
                .Replace("Grip", "グリップ").Replace("Trigger", "トリガー").Replace("ApplicationMenu", "メニュー")
                .Replace("Axis0", "ボタン0").Replace("Axis1", "トリガー").Replace("System", "システム")
                .Replace("StickX", "スティック左右").Replace("StickY", "スティック上下").Replace("Alpha", "")
                .Replace("Equals", "＝").Replace("RightBracket", "］").Replace("Semicolon", "；").Replace("Colon", "：");
            return string.Join(" または ", value.Split('|').Select(alternative =>
            {
                var tokens = alternative.Split('+').Select(x => x.Trim()).ToArray();
                Func<string, bool> modifier = x => x.Contains("Ctrl") || x.Contains("Shift") || x.Contains("Alt");
                return string.Join("＋", tokens.Where(modifier).Concat(tokens.Where(x => !modifier(x))));
            }));
        }

        private static string DescriptionLabel(string text)
        {
            return text.Replace("DebugDumpKeyEnabled=false", "開発用の診断設定が無効").Replace("RuntimeLoggingEnabled=false", "診断ログが無効")
                .Replace("BoneReleaseByRightClick", "ボーン切替設定").Replace("General.Enabled", "プラグインの有効設定")
                .Replace("EnableMouseWheelSpeedControl", "ホイール速度操作").Replace("WheelRequireCtrl", "Ctrl必須設定")
                .Replace("DevelopmentHotkeyEnabled", "手動撮影キー設定").Replace("InputPanelSendOnEnter", "キーで送信する設定")
                .Replace("InputPanelEnabled", "字幕入力パネル").Replace("EnableCtrlRReload", "再読込キー設定")
                .Replace("KeepOpenOnOutsideClick", "パネル外クリックでも開いたままにする設定").Replace("EnableVrGrip", "VRで掴む設定")
                .Replace("VrFovStickEnabled", "スティックで画角を変える設定").Replace("EnableVrGrab", "VRで掴む設定")
                .Replace("Enabled", "有効設定");
        }

        private static string PluginLabel(string name)
        {
            switch (name)
            {
                case "AstralMode": return "アストラル";
                case "LiquidEffectPreview": return "液体プレビュー";
                case "MainGameBlankMapAdd": return "部屋・プレイバックバー";
                case "MainGameCameraControl": return "メインカメラ";
                case "MainGameCharacterAfterimage": return "キャラ残像";
                case "MainGameClothColliderCards": return "衣装コライダー";
                case "MainGameClothesLoadProbe": return "衣装読込の診断";
                case "MainGameClubLights": return "ライト";
                case "MainGameCoordinateCardSwap": return "衣装カード切替";
                case "MainGameFaceSweatControl": return "汗・赤み";
                case "MainGameFreeHMasturbationMenu": return "自慰メニュー・速度";
                case "MainGameMapOverlayProbe": return "マップ追加の診断";
                case "MainGameObjectComposer": return "オブジェクト管理";
                case "MainGamePortraitCapture": return "キャラ撮影";
                case "MainGamePoseCatalogCapture": return "全ポーズ撮影";
                case "MainGamePregnancyPlusBridge": return "腹ボコ";
                case "MainGameRhythmSync": return "リズム・速度同期";
                case "MainGameSubCameraDisplayProbe": return "サブカメラ";
                case "MainGameSubtitleCore": return "字幕・テキスト送信";
                case "MainGameSubtitleEventBridge": return "字幕イベント受信";
                case "MainGameTransformGizmo": return "移動・回転ギズモ";
                case "MainGameUiInputCapture": return "入力の強制解除";
                case "MainGameUnifiedUi": return "総合UI";
                case "MainGameVMDPlay": return "VMD・呼出キャラ・衣装";
                case "MainGameVoiceFaceEventBridge": return "音声・表情イベント";
                case "MainGameVrDildo": return "ディルド";
                case "MainGameVrNeckChain": return "鎖・首輪";
                case "MainGameVrVoiceGate": return "VR録音";
                case "MainGirlHipHijack": return "キャラIK・VR掴み";
                case "StudioCharaStateLogger": return "Studioキャラ診断";
                case "StudioFacePresetTool": return "Studio表情プリセット";
                case "UnityResourceDiagnostics": return "Unityリソース診断";
                default: return name;
            }
        }

        internal static string Canonical(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("None", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return string.Join("|", value.Split('|').Select(chord => string.Join("+", chord.Split('+').Select(x => x.Trim().ToLowerInvariant().Replace("leftcontrol", "ctrl").Replace("rightcontrol", "ctrl").Replace("leftshift", "shift").Replace("rightshift", "shift").Replace("leftalt", "alt").Replace("rightalt", "alt")).OrderBy(x => x))).Distinct().OrderBy(x => x));
        }
    }
}
