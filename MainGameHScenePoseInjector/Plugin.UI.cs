using System.Collections.Generic;
using UnityEngine;

namespace MainGameHScenePoseInjector
{
    public sealed partial class Plugin
    {
        private Rect _windowRect;
        private Vector2 _scrollPose;
        private Vector2 _scrollEb;
        private Vector2 _scrollEye;
        private Vector2 _scrollMouth;
        private Vector2 _scrollPreset;
        private string _presetNameInput = "";
        private const int WindowId = 0x504F5345; // 'POSE'
        private string _statusMessage = "";
        private float _statusUntil = 0f;

        private void DrawWindow()
        {
            EnsureRectFromSettings();
            _windowRect = GUI.Window(WindowId, _windowRect, id => DrawWindowContents(id, true), "ポーズ・表情 (HScene)");
            PersistRectToSettings();
        }

        private void EnsureRectFromSettings()
        {
            if (_windowRect.width <= 0f || _windowRect.height <= 0f)
            {
                _windowRect = new Rect(
                    _settings.WindowX,
                    _settings.WindowY,
                    _settings.WindowWidth,
                    _settings.WindowHeight);
            }
        }

        private void PersistRectToSettings()
        {
            bool changed = false;
            if (Mathf.Abs(_settings.WindowX - _windowRect.x) > 0.5f) { _settings.WindowX = _windowRect.x; changed = true; }
            if (Mathf.Abs(_settings.WindowY - _windowRect.y) > 0.5f) { _settings.WindowY = _windowRect.y; changed = true; }
            if (Mathf.Abs(_settings.WindowWidth - _windowRect.width) > 0.5f) { _settings.WindowWidth = _windowRect.width; changed = true; }
            if (Mathf.Abs(_settings.WindowHeight - _windowRect.height) > 0.5f) { _settings.WindowHeight = _windowRect.height; changed = true; }
            if (changed) SaveSettings();
        }

        private void DrawWindowContents(int id, bool allowDrag)
        {
            GUILayout.BeginVertical();

            // Status
            GUILayout.Label("HScene: " + (IsInHScene() ? "検出中" : "未検出"));
            int curId = CurrentAppliedPoseId();
            GUILayout.Label("ポーズ適用中: " + (IsAnyPoseApplied() ? ("ID=" + curId) : "なし")
                + "  /  最後の表情: 眉=" + _settings.LastEyebrowPtn
                + " 目=" + _settings.LastEyesPtn
                + " 口=" + _settings.LastMouthPtn);

            // Toolbar
            GUILayout.BeginHorizontal();
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && IsAnyPoseApplied();
            if (GUILayout.Button("ポーズ解除", GUILayout.Height(26f), GUILayout.Width(100f)))
            {
                RestoreAllSnapshots("ui-cancel");
                SetStatus("ポーズ解除しました");
            }
            GUI.enabled = prevEnabled;
            if (GUILayout.Button("一覧再読込", GUILayout.Height(26f), GUILayout.Width(100f)))
            {
                _poseList = null;
                _poseListLoadAttempted = false;
                _eyebrowList = _eyesList = _mouthList = null;
                _patternListLoadAttempted = false;
                SetStatus("一覧をクリア");
            }
            DrawToggleRow("複数女キャラ全員に適用", ref _settings.ApplyToAllFemales);
            DrawToggleRow("Animator差替で自動解除", ref _settings.AutoRestoreOnControllerChange);
            DrawToggleRow("選択した表情をゲームの上書きから維持", ref _settings.KeepSelectedExpressionWhilePoseApplied);
            GUILayout.EndHorizontal();

            DrawAutoCycleSection();

            GUILayout.Space(4f);

            // 5-column layout
            // 自動変更の段が増えたぶん、下の一覧の高さを引く。
            // 畳んでいる時と開いている時で必要な高さが違うので、開閉に応じて変える。
            float autoSectionHeight = 56f;
            if (Auto != null && Auto.BulkMode) autoSectionHeight += 40f;
            if (_autoPoseFilterOpen) autoSectionHeight += 120f;
            if (_autoPoseFilterOpen && Auto != null
                && (ExpressionCycleSource)Auto.ExpressionSource == ExpressionCycleSource.PerChannel)
                autoSectionHeight += 140f;
            if (_autoDetailOpen) autoSectionHeight += 150f;
            float listHeight = Mathf.Max(120f, _windowRect.height - 130f - autoSectionHeight);
            float colWidth = Mathf.Max(120f, (_windowRect.width - 70f) / 5f);

            GUILayout.BeginHorizontal();

            DrawPoseColumn(curId, colWidth, listHeight);
            DrawExpressionPresetColumn(colWidth, listHeight);
            DrawPatternColumn("眉", GetEyebrowList(), ref _scrollEb,
                _settings.LastEyebrowPtn, id => ApplyExpressionPick(id, null, null), colWidth, listHeight);
            DrawPatternColumn("目", GetEyesList(), ref _scrollEye,
                _settings.LastEyesPtn, id => ApplyExpressionPick(null, id, null), colWidth, listHeight);
            DrawPatternColumn("口", GetMouthList(), ref _scrollMouth,
                _settings.LastMouthPtn, id => ApplyExpressionPick(null, null, id), colWidth, listHeight);

            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_statusMessage) && Time.unscaledTime < _statusUntil)
                GUILayout.Label(_statusMessage);

            GUILayout.EndVertical();
            if (allowDrag)
                GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
        }

        private void DrawExpressionPresetColumn(float width, float height)
        {
            GUILayout.BeginVertical(GUILayout.Width(width));
            GUILayout.Label("【表情プリセット】");
            _presetNameInput = GUILayout.TextField(_presetNameInput ?? "", GUILayout.Height(22f));
            if (GUILayout.Button("現在の組合せを登録／上書き", GUILayout.Height(24f)))
                RegisterCurrentExpressionPreset();

            float scrollHeight = Mathf.Max(70f, height - 52f);
            _scrollPreset = GUILayout.BeginScrollView(_scrollPreset, false, true, GUILayout.Height(scrollHeight));
            List<ExpressionPreset> presets = _settings.ExpressionPresets;
            for (int i = 0; i < presets.Count; i++)
            {
                ExpressionPreset preset = presets[i];
                if (preset == null) continue;
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(preset.Name, GUILayout.Height(22f)))
                {
                    _presetNameInput = preset.Name;
                    ApplyExpressionPreset(preset);
                }
                if (GUILayout.Button("削除", GUILayout.Width(42f), GUILayout.Height(22f)))
                {
                    presets.RemoveAt(i);
                    SaveSettings();
                    SetStatus("表情プリセット削除: " + preset.Name);
                    i--;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void RegisterCurrentExpressionPreset()
        {
            string name = (_presetNameInput ?? "").Trim();
            if (name.Length == 0)
            {
                SetStatus("プリセット名を入力してください");
                return;
            }
            if (_settings.LastEyebrowPtn < 0 && _settings.LastEyesPtn < 0 && _settings.LastMouthPtn < 0)
            {
                SetStatus("先に眉・目・口を選んでください");
                return;
            }

            ExpressionPreset target = null;
            for (int i = 0; i < _settings.ExpressionPresets.Count; i++)
            {
                ExpressionPreset candidate = _settings.ExpressionPresets[i];
                if (candidate != null && string.Equals(candidate.Name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    target = candidate;
                    break;
                }
            }
            if (target == null)
            {
                target = new ExpressionPreset { Name = name };
                _settings.ExpressionPresets.Add(target);
            }
            target.Name = name;
            target.EyebrowPtn = _settings.LastEyebrowPtn;
            target.EyesPtn = _settings.LastEyesPtn;
            target.MouthPtn = _settings.LastMouthPtn;
            SaveSettings();
            SetStatus("表情プリセット保存: " + name);
        }

        private void ApplyExpressionPreset(ExpressionPreset preset)
        {
            int? eyebrow = preset.EyebrowPtn >= 0 ? (int?)preset.EyebrowPtn : null;
            int? eyes = preset.EyesPtn >= 0 ? (int?)preset.EyesPtn : null;
            int? mouth = preset.MouthPtn >= 0 ? (int?)preset.MouthPtn : null;
            bool ok = ApplyExpression(eyebrow, eyes, mouth);
            if (!ok)
            {
                SetStatus("表情プリセット適用失敗: " + preset.Name);
                return;
            }

            if (eyebrow.HasValue) _settings.LastEyebrowPtn = eyebrow.Value;
            if (eyes.HasValue) _settings.LastEyesPtn = eyes.Value;
            if (mouth.HasValue) _settings.LastMouthPtn = mouth.Value;
            SaveSettings();
            SetStatus("表情プリセット適用: " + preset.Name);
        }

        private void DrawPoseColumn(int curPoseId, float width, float height)
        {
            GUILayout.BeginVertical(GUILayout.Width(width));
            GUILayout.Label("【ポーズ】");

            List<PoseEntry> poses = GetPoseList();
            if (poses.Count == 0)
            {
                GUILayout.Label("(空)");
            }
            else
            {
                _scrollPose = GUILayout.BeginScrollView(_scrollPose, false, true, GUILayout.Height(height));
                for (int i = 0; i < poses.Count; i++)
                {
                    PoseEntry p = poses[i];
                    Color prev = GUI.backgroundColor;
                    if (p.Id == curPoseId && IsAnyPoseApplied())
                        GUI.backgroundColor = Color.cyan;
                    string label = p.Name + " (" + p.Id + ")";
                    if (GUILayout.Button(label, GUILayout.Height(22f)))
                    {
                        bool ok = ApplyPose(p);
                        SetStatus(ok ? ("ポーズ適用: " + p.Name) : ("失敗: " + p.Name));
                    }
                    GUI.backgroundColor = prev;
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
        }

        private void DrawPatternColumn(string title, List<PatternEntry> list, ref Vector2 scroll,
            int currentSelectedId, System.Action<int> onPick, float width, float height)
        {
            GUILayout.BeginVertical(GUILayout.Width(width));
            GUILayout.Label("【" + title + "】");

            if (list == null || list.Count == 0)
            {
                GUILayout.Label("(空)");
            }
            else
            {
                scroll = GUILayout.BeginScrollView(scroll, false, true, GUILayout.Height(height));
                for (int i = 0; i < list.Count; i++)
                {
                    PatternEntry p = list[i];
                    Color prev = GUI.backgroundColor;
                    if (p.Id == currentSelectedId)
                        GUI.backgroundColor = Color.yellow;
                    string label = p.Name + " (" + p.Id + ")";
                    if (GUILayout.Button(label, GUILayout.Height(22f)))
                    {
                        onPick(p.Id);
                    }
                    GUI.backgroundColor = prev;
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();
        }

        private void ApplyExpressionPick(int? eb, int? eye, int? mouth)
        {
            bool ok = ApplyExpression(eb, eye, mouth);
            if (ok)
            {
                if (eb.HasValue) _settings.LastEyebrowPtn = eb.Value;
                if (eye.HasValue) _settings.LastEyesPtn = eye.Value;
                if (mouth.HasValue) _settings.LastMouthPtn = mouth.Value;
                SaveSettings();
                SetStatus("表情適用: 眉=" + eb + " 目=" + eye + " 口=" + mouth);
            }
            else
            {
                SetStatus("表情適用失敗");
            }
        }

        private void DrawToggleRow(string label, ref bool value)
        {
            bool next = GUILayout.Toggle(value, label);
            if (next != value)
            {
                value = next;
                SaveSettings();
            }
        }

        private void SetStatus(string msg)
        {
            _statusMessage = msg;
            _statusUntil = Time.unscaledTime + 3f;
            LogInfo("status: " + msg);
        }
    }
}
