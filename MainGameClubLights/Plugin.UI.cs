using System.Collections.Generic;
using UnityEngine;

namespace MainGameClubLights
{
    public sealed partial class Plugin
    {
        private Rect   _windowRect = new Rect(20, 20, 420, 620);
        private Vector2 _scrollPos;

        private readonly HashSet<string> _expandedLights   = new HashSet<string>();
        private readonly HashSet<string> _expandedSections = new HashSet<string>();
        private bool _expandedPresets;
        private bool _expandedProfiles = true;
        private bool _expandedVideoMap;
        private bool _expandedNative;
        private bool _expandedBeat;

        // プリセット保存用入力バッファ（ライトID → 入力文字列）
        private readonly Dictionary<string, string> _presetNameBuf = new Dictionary<string, string>();
        private string _videoMapPathBuf  = "";
        private string _videoMapPresetId = "";
        private string _profileNameBuf = "";
        private string _profileSelection = "";
        private string _profileUiMessage = "";
        private float _nextProfileActionAt = 0f;
        private float _suppressLightActionsUntil = 0f;
        private string _pendingRemoveLightId = "";
        private float _pendingRemoveUntil = 0f;

        private GUIStyle _boldLabel;
        private GUIStyle _boldFoldout;
        private bool     _stylesInit;

        private void DrawUI()
        {
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0, Screen.width  - _windowRect.width);
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0, Screen.height - _windowRect.height);
            _windowRect = GUILayout.Window(GetHashCode(), _windowRect, DrawWindow,
                "ClubLights", GUILayout.Width(420), GUILayout.Height(620));
        }

        private void EnsureStyles()
        {
            if (_stylesInit) return;
            _boldLabel   = new GUIStyle(GUI.skin.label)  { fontStyle = FontStyle.Bold };
            _boldFoldout = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            _stylesInit  = true;
        }

        private void DrawWindow(int id)
        {
            EnsureStyles();

            // ── ドラッグ検知（GUILayout描画より前・rawTypeで消費済みイベントも拾う）──
            Event ev = Event.current;
            HandleWindowDragFlag(ev);
            HandleSliderDragFlag(ev);
            HandleScrollDragFlag(ev);

            // ── 描画 ──────────────────────────────────────────────────────────
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);
            DrawClubLightsBody();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, _windowRect.width, 20));
        }

        // 窓の中身本体。スクロール枠/ドラッグ枠の外側は呼び出し側が持つ。総合UIタブと共用。
        private void DrawClubLightsBody()
        {
            EnsureStyles();

            // ログ有効/無効トグル
            bool logEnabled = _log.Enabled;
            bool newLogEnabled = GUILayout.Toggle(logEnabled, "ログ出力");
            if (newLogEnabled != logEnabled)
            {
                _log.Enabled = newLogEnabled;
                _cfgLogEnabled.Value = newLogEnabled;
            }

            GUILayout.Space(4);
            if (DrawProfileStorageSection())
                return;
            DrawLightSection();
            DrawNativeLightSection();
        }

        internal void SyncProfileUiBuffersFromCurrent()
        {
            _profileNameBuf = CurrentProfileName;
            _profileSelection = CurrentProfileName;
        }

        private bool DrawProfileStorageSection()
        {
            if (!string.IsNullOrEmpty(_pendingRemoveLightId) && Time.unscaledTime >= _pendingRemoveUntil)
                _pendingRemoveLightId = "";

            string label = $"━━ 設定プロファイル ━━  {(_expandedProfiles ? "▲" : "▼")}";
            if (GUILayout.Button(label, _boldFoldout))
                _expandedProfiles = !_expandedProfiles;

            if (!_expandedProfiles) return false;

            if (string.IsNullOrEmpty(_profileNameBuf))
                _profileNameBuf = CurrentProfileName;
            if (string.IsNullOrEmpty(_profileSelection))
                _profileSelection = CurrentProfileName;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"現在: {CurrentProfileName}");

            GUILayout.BeginHorizontal();
            GUILayout.Label("プロファイル名", GUILayout.Width(90));
            _profileNameBuf = GUILayout.TextField(_profileNameBuf, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("選択", GUILayout.Width(90));
            GUILayout.Label(string.IsNullOrEmpty(_profileSelection) ? "(なし)" : _profileSelection, GUILayout.Width(140));
            if (GUILayout.Button("◀", GUILayout.Width(24))) CycleProfileName(ref _profileSelection, -1);
            if (GUILayout.Button("▶", GUILayout.Width(24))) CycleProfileName(ref _profileSelection, 1);
            if (GUILayout.Button("↺", GUILayout.Width(24))) _profileSelection = CurrentProfileName;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("上書き保存"))
            {
                if (!TryBeginProfileAction()) { ConsumeCurrentGuiEvent(); return true; }
                bool ok = SaveCurrentProfile(out string msg);
                _profileUiMessage = msg;
                if (ok) SyncProfileUiBuffersFromCurrent();
                ConsumeCurrentGuiEvent();
                return true;
            }
            if (GUILayout.Button("名前を付けて保存"))
            {
                if (!TryBeginProfileAction()) { ConsumeCurrentGuiEvent(); return true; }
                bool ok = SaveProfileAsNew(_profileNameBuf, out string msg);
                _profileUiMessage = msg;
                if (ok) SyncProfileUiBuffersFromCurrent();
                ConsumeCurrentGuiEvent();
                return true;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("呼び出し"))
            {
                if (!TryBeginProfileAction()) { ConsumeCurrentGuiEvent(); return true; }
                string target = ResolveProfileLoadTarget();
                bool ok = LoadProfileByName(target, out string msg);
                _profileUiMessage = msg;
                if (ok) SyncProfileUiBuffersFromCurrent();
                ConsumeCurrentGuiEvent();
                return true;
            }

            bool hasDeleteTarget = !string.IsNullOrEmpty(_profileSelection);
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && hasDeleteTarget;
            if (GUILayout.Button("削除"))
            {
                if (!TryBeginProfileAction()) { ConsumeCurrentGuiEvent(); return true; }
                bool ok = DeleteProfileByName(_profileSelection, out string msg);
                _profileUiMessage = msg;
                if (ok)
                {
                    var names = ListProfileNames();
                    _profileSelection = names.Count > 0 ? names[0] : CurrentProfileName;
                }
                ConsumeCurrentGuiEvent();
                return true;
            }
            GUI.enabled = prevEnabled;
            GUILayout.EndHorizontal();

            GUILayout.Label($"登録数: {ListProfileNames().Count}");
            if (!string.IsNullOrEmpty(_profileUiMessage))
                GUILayout.Label(_profileUiMessage);
            GUILayout.EndVertical();
            return false;
        }

        private bool TryBeginProfileAction()
        {
            float now = Time.unscaledTime;
            if (now < _nextProfileActionAt)
                return false;

            _nextProfileActionAt = now + 0.25f;
            _suppressLightActionsUntil = now + 0.35f;
            _pendingRemoveLightId = "";
            return true;
        }

        private bool IsLightActionSuppressed()
        {
            return Time.unscaledTime < _suppressLightActionsUntil;
        }

        private static void ConsumeCurrentGuiEvent()
        {
            Event ev = Event.current;
            if (ev == null) return;
            if (ev.type == EventType.Layout || ev.type == EventType.Repaint) return;
            ev.Use();
        }

        private string ResolveProfileLoadTarget()
        {
            if (!string.IsNullOrWhiteSpace(_profileSelection))
                return _profileSelection;
            return _profileNameBuf;
        }

        private void CycleProfileName(ref string profileName, int dir)
        {
            var names = ListProfileNames();
            if (names.Count == 0)
            {
                profileName = "";
                return;
            }

            int current = -1;
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], profileName, System.StringComparison.OrdinalIgnoreCase))
                {
                    current = i;
                    break;
                }
            }
            if (current < 0)
            {
                for (int i = 0; i < names.Count; i++)
                {
                    if (string.Equals(names[i], CurrentProfileName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        current = i;
                        break;
                    }
                }
                if (current < 0) current = 0;
            }

            int next = (current + dir + names.Count) % names.Count;
            profileName = names[next];
        }

        private void HandleWindowDragFlag(Event ev)
        {
            if (ev == null) return;
            var titleBar = new Rect(0f, 0f, _windowRect.width, 20f);
            if (ev.rawType == EventType.MouseDown && ev.button == 0 && titleBar.Contains(ev.mousePosition))
                SetWindowDragging(true);
            else if (ev.rawType == EventType.MouseUp && ev.button == 0)
                SetWindowDragging(false);
        }

        private void HandleSliderDragFlag(Event ev)
        {
            if (ev == null) return;
            var bodyArea = new Rect(0f, 20f, _windowRect.width - 18f, _windowRect.height - 20f);
            if (ev.rawType == EventType.MouseDown && ev.button == 0 && bodyArea.Contains(ev.mousePosition))
                SetSliderDragging(true);
            else if (ev.rawType == EventType.MouseUp && ev.button == 0 && _sliderDragging)
                SetSliderDragging(false);
        }

        private void HandleScrollDragFlag(Event ev)
        {
            if (ev == null) return;
            var scrollBarArea = new Rect(_windowRect.width - 18f, 20f, 18f, _windowRect.height - 20f);
            if (ev.rawType == EventType.MouseDown && ev.button == 0 && scrollBarArea.Contains(ev.mousePosition))
                SetScrollDragging(true);
            else if (ev.rawType == EventType.MouseUp && ev.button == 0 && _scrollDragging)
                SetScrollDragging(false);
        }

        // ────────────────────────────────────────────────────────────────────
        // ライト一覧
        // ────────────────────────────────────────────────────────────────────

        private void DrawLightSection()
        {
            // ━━ フォルダ（複数自由に作成可。掴むと所属ライトを一括移動） ━━
            GUILayout.BeginHorizontal();
            GUILayout.Label("━━ フォルダ ━━", _boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("追加", GUILayout.Width(50)))
                CreateFolder();
            GUILayout.EndHorizontal();

            var folders = _settings.Folders;
            if (folders != null && folders.Count > 0)
            {
                for (int fi = 0; fi < folders.Count; fi++)
                {
                    var f = folders[fi];
                    if (f == null) continue;
                    // 行1: 名前＋削除
                    GUILayout.BeginHorizontal();
                    f.Name = GUILayout.TextField(f.Name ?? "", GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("削除", GUILayout.Width(50)))
                    {
                        RemoveFolder(f.Id);
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();

                    // 行2: 表示トグル（玉=マーカー / ◇=ギズモ を個別 ＋ 内包ライトのマーカー一括）
                    GUILayout.BeginHorizontal();
                    bool sm = GUILayout.Toggle(f.ShowHandle, "玉", GUILayout.Width(44));
                    if (sm != f.ShowHandle) { f.ShowHandle = sm; SaveSettingsNow("folder-marker-toggle"); }
                    bool sg = GUILayout.Toggle(f.ShowHandleGizmo, "◇", GUILayout.Width(44));
                    if (sg != f.ShowHandleGizmo) { f.ShowHandleGizmo = sg; SaveSettingsNow("folder-gizmo-toggle"); }
                    GUILayout.Space(8);
                    bool lm = AreFolderLightMarkersOn(f.Id);
                    bool lmNext = GUILayout.Toggle(lm, "光マーカー一括");
                    if (lmNext != lm) SetFolderLightsMarker(f.Id, lmNext);
                    GUILayout.EndHorizontal();

                    // 行3: サイズ（玉=マーカー球, ◇=ギズモ）。per-folder のため直接スライダー
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("玉", GUILayout.Width(18));
                    float ns = GUILayout.HorizontalSlider(f.HandleSize, 0.02f, 0.5f, GUILayout.Width(90));
                    if (!Mathf.Approximately(ns, f.HandleSize)) f.HandleSize = ns;
                    GUILayout.Label("◇", GUILayout.Width(18));
                    float ng = GUILayout.HorizontalSlider(f.GizmoSize, 0.1f, 2f, GUILayout.Width(90));
                    if (!Mathf.Approximately(ng, f.GizmoSize)) f.GizmoSize = ng;
                    GUILayout.EndHorizontal();
                    GUILayout.Space(4);
                }
            }
            else
            {
                GUILayout.Label("(フォルダなし。「追加」で作成)");
            }
            GUILayout.Space(6);

            // ━━ 体位連動（丸ごとスナップショット） ━━
            GUILayout.Label("━━ 体位連動 ━━", _boldLabel);
            GUILayout.BeginHorizontal();
            bool autoApply = GUILayout.Toggle(_settings.PosePresetAutoApply, " 体位で自動切替");
            if (autoApply != _settings.PosePresetAutoApply)
            {
                _settings.PosePresetAutoApply = autoApply;
                if (autoApply) _poseTrackingInitialized = false; // ONにした直後、現在体位を適用させる
                SaveSettingsNow("pose-autoapply-toggle");
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("体位を上書き", GUILayout.Width(100)))
                CaptureCurrentPoseSnapshot();
            GUILayout.EndHorizontal();

            string poseLabel = TryGetCurrentPoseInfo(out string curKey, out string curDisp)
                ? $"{curDisp} ({curKey})"
                : "未取得 (Hシーン外)";
            GUILayout.Label("現在の体位: " + poseLabel);

            var snaps = _settings.LightPoseSnapshots;
            if (snaps != null && snaps.Count > 0)
            {
                for (int si = 0; si < snaps.Count; si++)
                {
                    var s = snaps[si];
                    if (s == null) continue;
                    GUILayout.BeginHorizontal();
                    bool isCur = !string.IsNullOrEmpty(curKey) && string.Equals(s.Key, curKey, System.StringComparison.Ordinal);
                    string nm = string.IsNullOrWhiteSpace(s.DisplayName) ? s.Key : s.DisplayName;
                    GUILayout.Label((isCur ? "★ " : "・") + nm + $"  (光{s.Lights?.Count ?? 0})", GUILayout.ExpandWidth(true));
                    if (GUILayout.Button("削除", GUILayout.Width(50)))
                    {
                        RemovePoseSnapshot(si);
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label("(体位スナップショットなし)");
            }
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label("━━ ライト一覧 ━━", _boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("追加", GUILayout.Width(50)))
            {
                AddLight();
            }
            GUILayout.EndHorizontal();

            for (int i = 0; i < _settings.Lights.Count; i++)
            {
                DrawLightRow(i);
            }

            GUILayout.Space(6);
        }

        // セクションヘッダ（閉じてる=▲、開いてる=▼）
        private bool DrawSectionHeader(string key, string title)
        {
            bool open = _expandedSections.Contains(key);
            if (GUILayout.Button((open ? "▼ " : "▲ ") + title, _boldFoldout))
            {
                if (open) _expandedSections.Remove(key);
                else      _expandedSections.Add(key);
                open = !open;
            }
            return open;
        }

        private void DrawLightRow(int index)
        {
            var li = _settings.Lights[index];
            bool expanded = _expandedLights.Contains(li.Id);

            // ─ ライト全体ヘッダ ─
            GUILayout.BeginHorizontal();
            bool newEnabled = GUILayout.Toggle(li.Enabled, "", GUILayout.Width(20));
            if (newEnabled != li.Enabled)
            {
                li.Enabled = newEnabled;
                var entry = FindEntry(li);
                if (entry?.Light != null) entry.Light.enabled = li.Enabled;
            }
            string label = $"{li.Name}  {(expanded ? "▲" : "▼")}";
            if (GUILayout.Button(label, GUI.skin.label, GUILayout.ExpandWidth(true)))
            {
                if (expanded) _expandedLights.Remove(li.Id);
                else          _expandedLights.Add(li.Id);
            }
            bool removeEnabled = !IsLightActionSuppressed();
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && removeEnabled;
            if (GUILayout.Button("×", GUILayout.Width(24)))
            {
                _pendingRemoveLightId = "";
                RemoveLight(index);
                GUI.enabled = prevEnabled;
                return;
            }
            GUI.enabled = prevEnabled;
            GUILayout.EndHorizontal();

            if (!expanded) return;

            GUILayout.BeginVertical(GUI.skin.box);

            // 名前（常時表示）
            GUILayout.BeginHorizontal();
            GUILayout.Label("名前", GUILayout.Width(80));
            li.Name = GUILayout.TextField(li.Name, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.Space(2);

            // ── セクション1: 位置・配置 ──────────────────────────────────────
            if (DrawSectionHeader(li.Id + "_pos", "位置・配置"))
            {
                // 所属フォルダ（押すたびに 未所属→各フォルダ→未所属 をサイクル。位置は保たれる）
                GUILayout.BeginHorizontal();
                GUILayout.Label("フォルダ", GUILayout.Width(80));
                if (GUILayout.Button(GetFolderName(li.FolderId), GUILayout.ExpandWidth(true)))
                    AssignLightToFolder(li, NextFolderId(li.FolderId));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                bool follow = GUILayout.Toggle(li.FollowCamera, "カメラ追従");
                if (follow != li.FollowCamera)
                {
                    var entry = FindEntry(li);
                    if (entry != null) SetLightFollowCamera(entry, follow);
                    else               li.FollowCamera = follow;
                }
                GUILayout.EndHorizontal();

                if (li.FollowCamera)
                {
                    DrawSlider("X オフセット", ref li.OffsetX, -5f, 5f, 0f);
                    DrawSlider("Y オフセット", ref li.OffsetY, -2f, 5f, 1f);
                    DrawSlider("Z オフセット", ref li.OffsetZ, -5f, 5f, 2f);
                }
                else
                {
                    var entry2 = FindEntry(li);
                    float px = li.WorldPosX, py = li.WorldPosY, pz = li.WorldPosZ;
                    DrawSlider("X 座標", ref li.WorldPosX, -20f, 20f, 0f);
                    DrawSlider("Y 座標", ref li.WorldPosY, -5f,  10f, 1f);
                    DrawSlider("Z 座標", ref li.WorldPosZ, -20f, 20f, 2f);
                    if ((li.WorldPosX != px || li.WorldPosY != py || li.WorldPosZ != pz) &&
                        entry2?.Go != null)
                        entry2.Go.transform.position = new Vector3(li.WorldPosX, li.WorldPosY, li.WorldPosZ);
                    DrawSlider("回転X", ref li.RotX, -180f, 180f, 0f);
                    DrawSlider("回転Y", ref li.RotY, -180f, 180f, 0f);
                    DrawSlider("回転Z", ref li.RotZ, -180f, 180f, 0f);
                    DrawSlider("ギズモサイズ", ref li.GizmoSize, 0.2f, 4f, 1f);
                }

                GUILayout.Space(2);

                // 公転（両モード共通）
                bool newRevEnabled = GUILayout.Toggle(li.RevolutionEnabled, "公転");
                if (newRevEnabled != li.RevolutionEnabled)
                {
                    var entryRev = FindEntry(li);
                    if (entryRev != null)
                        SetRevolutionEnabled(entryRev, newRevEnabled);
                    else
                        li.RevolutionEnabled = newRevEnabled;
                }
                if (li.RevolutionEnabled)
                {
                    DrawSlider("  半径",      ref li.RevolutionRadius, 0.1f, 10f,  2f);
                    DrawSlider("  速度(°/s)", ref li.RevolutionSpeed,  0f,   720f, 45f);
                }

                // 自転（両モード共通）
                li.RotationEnabled = GUILayout.Toggle(li.RotationEnabled, "自転");
                if (li.RotationEnabled)
                    DrawSlider("  速度(°/s)", ref li.RotationSpeed, 0f, 720f, 90f);
            }

            // ── セクション2: ライト ──────────────────────────────────────────
            if (DrawSectionHeader(li.Id + "_light", "ライト"))
            {
                // ── パラメータ ──
                DrawSlider("強度",           ref li.Intensity,      0f,  8f,   2f);
                float beforeSpot = li.SpotAngle;
                DrawSlider("スポット角",     ref li.SpotAngle,      1f,  360f, 179f);
                if (!Mathf.Approximately(beforeSpot, li.SpotAngle))
                {
                    li.SpotAnglePinnedByUser = true;
                    li.SpotAngleLoop.Enabled = false;
                    _log.Info($"[SpotAngle] manual-set id={li.Id} value={li.SpotAngle:F1} pin=true loop=false");
                }
                DrawSlider("境界ぼかし(内角)", ref li.InnerSpotAngle, 0f,  360f, 0f);
                DrawSlider("範囲",           ref li.Range,          1f,  30f,  10f);

                GUILayout.Space(4);

                // ── ループ設定 ──
                li.IntensityLoop.Enabled = GUILayout.Toggle(li.IntensityLoop.Enabled, "強度ループ");
                if (li.IntensityLoop.Enabled)
                {
                    li.IntensityLoop.BeatFollow = GUILayout.Toggle(li.IntensityLoop.BeatFollow, "  動画連携(強度追従)");
                    if (!li.IntensityLoop.BeatFollow)
                        li.IntensityLoop.VideoLink = GUILayout.Toggle(li.IntensityLoop.VideoLink, "  動画連携(BPM)");
                    DrawSlider("  最小",      ref li.IntensityLoop.MinValue, 0f,   8f,  0f);
                    DrawSlider("  最大",      ref li.IntensityLoop.MaxValue, 0f,   8f,  2f);
                    if (!li.IntensityLoop.BeatFollow && !li.IntensityLoop.VideoLink)
                        DrawSlider("  速度(Hz)",  ref li.IntensityLoop.SpeedHz,  0.05f, 4f, 0.5f);
                }

                bool newSpotLoopEnabled = GUILayout.Toggle(li.SpotAngleLoop.Enabled, "スポット角ループ");
                if (newSpotLoopEnabled != li.SpotAngleLoop.Enabled)
                {
                    li.SpotAngleLoop.Enabled = newSpotLoopEnabled;
                    if (newSpotLoopEnabled)
                    {
                        li.SpotAnglePinnedByUser = false;
                        _log.Info($"[SpotAngle] loop-enabled id={li.Id} pin=false");
                    }
                }
                if (li.SpotAngleLoop.Enabled)
                {
                    li.SpotAngleLoop.VideoLink = GUILayout.Toggle(li.SpotAngleLoop.VideoLink, "  動画連携");
                    DrawSlider("  最小",      ref li.SpotAngleLoop.MinValue, 1f,   360f, 10f);
                    DrawSlider("  最大",      ref li.SpotAngleLoop.MaxValue, 1f,   360f, 60f);
                    bool prev = GUI.enabled;
                    GUI.enabled = !li.SpotAngleLoop.VideoLink;
                    DrawSlider("  速度(Hz)",  ref li.SpotAngleLoop.SpeedHz,  0.05f, 4f,  0.5f);
                    GUI.enabled = prev;
                }

                li.RangeLoop.Enabled = GUILayout.Toggle(li.RangeLoop.Enabled, "範囲ループ");
                if (li.RangeLoop.Enabled)
                {
                    li.RangeLoop.VideoLink = GUILayout.Toggle(li.RangeLoop.VideoLink, "  動画連携");
                    DrawSlider("  最小",      ref li.RangeLoop.MinValue, 1f,   30f, 1f);
                    DrawSlider("  最大",      ref li.RangeLoop.MaxValue, 1f,   30f, 10f);
                    bool prev = GUI.enabled;
                    GUI.enabled = !li.RangeLoop.VideoLink;
                    DrawSlider("  速度(Hz)",  ref li.RangeLoop.SpeedHz,  0.05f, 4f, 0.5f);
                    GUI.enabled = prev;
                }
            }

            // ── セクション3: 色・エフェクト ──────────────────────────────────
            if (DrawSectionHeader(li.Id + "_color", "色・エフェクト"))
            {
                DrawSlider("R", ref li.ColorR, 0f, 1f, 1f);
                DrawSlider("G", ref li.ColorG, 0f, 1f, 1f);
                DrawSlider("B", ref li.ColorB, 0f, 1f, 1f);

                li.Rainbow.Enabled = GUILayout.Toggle(li.Rainbow.Enabled, "レインボー");
                if (li.Rainbow.Enabled)
                {
                    li.Rainbow.BeatFollow = GUILayout.Toggle(li.Rainbow.BeatFollow, "  動画連携(強度追従)");
                    if (!li.Rainbow.BeatFollow)
                        li.Rainbow.VideoLink = GUILayout.Toggle(li.Rainbow.VideoLink, "  動画連携(BPM)");
                    if (li.Rainbow.BeatFollow || li.Rainbow.VideoLink)
                    {
                        DrawSlider("  最小(Hue/s)", ref li.Rainbow.MinCycleSpeed, 0f, 20f, 0f);
                        DrawSlider("  最大(Hue/s)", ref li.Rainbow.MaxCycleSpeed, 0f, 20f, 2f);
                    }
                    else
                        DrawSlider("  速度(Hue/s)", ref li.Rainbow.CycleSpeed, 0.01f, 20f, 0.2f);
                }

                li.Strobe.Enabled = GUILayout.Toggle(li.Strobe.Enabled, "ストロボ");
                if (li.Strobe.Enabled)
                {
                    li.Strobe.BeatFollow = GUILayout.Toggle(li.Strobe.BeatFollow, "  動画連携(強度追従)");
                    if (!li.Strobe.BeatFollow)
                        li.Strobe.VideoLink = GUILayout.Toggle(li.Strobe.VideoLink, "  動画連携(BPM)");
                    if (li.Strobe.BeatFollow || li.Strobe.VideoLink)
                    {
                        DrawSlider("  最小Hz", ref li.Strobe.MinFrequencyHz, 0f, 30f, 0f);
                        DrawSlider("  最大Hz", ref li.Strobe.MaxFrequencyHz, 0f, 30f, 30f);
                    }
                    else
                        DrawSlider("  Hz",     ref li.Strobe.FrequencyHz, 0.5f, 30f, 4f);

                    // ON比率の連動モード
                    li.Strobe.DutyBeatFollow = GUILayout.Toggle(li.Strobe.DutyBeatFollow, "  ON比率(ゾーン連動)");
                    if (!li.Strobe.DutyBeatFollow)
                        li.Strobe.DutyVideoLink = GUILayout.Toggle(li.Strobe.DutyVideoLink, "  ON比率(BPM波動)");
                    if (li.Strobe.DutyBeatFollow || li.Strobe.DutyVideoLink)
                    {
                        DrawSlider("  最小比率", ref li.Strobe.MinDutyRatio, 0f, 1f, 0.1f);
                        DrawSlider("  最大比率", ref li.Strobe.MaxDutyRatio, 0f, 1f, 0.9f);
                    }
                    else
                        DrawSlider("  ON比率", ref li.Strobe.DutyRatio, 0f, 1f, 0.5f);
                }

                GUILayout.Space(4);
                if (li.Mirrorball == null) li.Mirrorball = new MirrorballCookieSettings();
                li.Mirrorball.Enabled = GUILayout.Toggle(li.Mirrorball.Enabled, "ミラーボールcookie");
                if (li.Mirrorball.Enabled)
                {
                    float cookieRes = li.Mirrorball.Resolution;
                    DrawSlider("  解像度", ref cookieRes, 64f, 1024f, 256f);
                    li.Mirrorball.Resolution = QuantizeCookieResolution(Mathf.RoundToInt(cookieRes));

                    float dotCount = li.Mirrorball.DotCount;
                    DrawSlider("  DOT数", ref dotCount, 1f, 4096f, 220f);
                    li.Mirrorball.DotCount = Mathf.Clamp(Mathf.RoundToInt(dotCount), 1, 4096);

                    DrawSlider("  DOTサイズ", ref li.Mirrorball.DotSize, 0.002f, 0.45f, 0.03f);
                    DrawSlider("  配列ランダム", ref li.Mirrorball.Scatter, 0f, 1f, 0.65f);
                    DrawSlider("  エッジぼかし", ref li.Mirrorball.Softness, 0f, 1f, 0.45f);

                    li.Mirrorball.Animate = GUILayout.Toggle(li.Mirrorball.Animate, "  回転アニメ");
                    if (li.Mirrorball.Animate)
                    {
                        DrawSlider("    回転(回/s)", ref li.Mirrorball.SpinSpeed, -3f, 3f, 0.12f);
                        DrawSlider("    更新Hz", ref li.Mirrorball.UpdateHz, 0.5f, 30f, 8f);
                        DrawSlider("    きらめき", ref li.Mirrorball.Twinkle, 0f, 1f, 0.2f);
                    }
                }
            }

            // ── セクション4: 向き ────────────────────────────────────────────
            if (DrawSectionHeader(li.Id + "_dir", "向き"))
            {
                bool newLookAtFemale = GUILayout.Toggle(li.LookAtFemale, "女を向き続ける");
                if (li.LookAtFemale)
                {
                    DrawSlider("オフセットX", ref li.LookAtOffsetX, -180f, 180f, 0f);
                    DrawSlider("オフセットY", ref li.LookAtOffsetY, -180f, 180f, 0f);
                    DrawSlider("オフセットZ", ref li.LookAtOffsetZ, -180f, 180f, 0f);
                    if (GUILayout.Button("オフセットリセット"))
                    {
                        li.LookAtOffsetX = 0f;
                        li.LookAtOffsetY = 0f;
                        li.LookAtOffsetZ = 0f;
                    }
                }
                if (newLookAtFemale != li.LookAtFemale)
                {
                    if (!newLookAtFemale)
                    {
                        var entryLook = FindEntry(li);
                        if (entryLook?.Go != null)
                        {
                            var euler = entryLook.Go.transform.rotation.eulerAngles;
                            li.RotX = euler.x;
                            li.RotY = euler.y;
                            li.RotZ = euler.z;
                        }
                    }
                    li.LookAtFemale = newLookAtFemale;
                }
            }

            // ── セクション5: マーカー ────────────────────────────────────────
            if (DrawSectionHeader(li.Id + "_marker", "マーカー"))
            {
                li.ShowMarker = GUILayout.Toggle(li.ShowMarker, "マーカー表示");
                li.ShowArrow  = GUILayout.Toggle(li.ShowArrow,  "矢印表示");
                li.ShowGizmo  = GUILayout.Toggle(li.ShowGizmo,  "ギズモ表示");
                if (li.ShowMarker)
                {
                    DrawSlider("マーカーサイズ", ref li.MarkerSize, 0.01f, 0.5f, 0.08f);
                }
                if (li.ShowArrow)
                    DrawSlider("矢印サイズ", ref li.ArrowScale, 0.1f, 5f, 1f);
            }

            // ── セクション6: プリセット ──────────────────────────────────────
            if (DrawSectionHeader(li.Id + "_preset", "プリセット"))
            {
                GUILayout.Label("呼び出し", _boldLabel);
                DrawPresetApply(li);
                DrawBeatPresetAssignment(li);
                GUILayout.Space(4);
                if (!_presetNameBuf.ContainsKey(li.Id)) _presetNameBuf[li.Id] = li.Name;
                GUILayout.BeginHorizontal();
                GUILayout.Label("保存名", GUILayout.Width(60));
                _presetNameBuf[li.Id] = GUILayout.TextField(_presetNameBuf[li.Id], GUILayout.ExpandWidth(true));
                if (GUILayout.Button("保存", GUILayout.Width(40)))
                    SavePresetFromLight(li, _presetNameBuf[li.Id]);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        private string _applyPresetId = "";

        private void DrawPresetApply(LightInstanceSettings li)
        {
            GUILayout.BeginHorizontal();
            string selName = "なし";
            LightPreset sel = FindPreset(_applyPresetId);
            if (sel != null) selName = sel.Name;
            GUILayout.Label(selName, GUILayout.Width(90));
            if (GUILayout.Button("◀", GUILayout.Width(24))) CyclePresetId(ref _applyPresetId, -1);
            if (GUILayout.Button("▶", GUILayout.Width(24))) CyclePresetId(ref _applyPresetId,  1);
            GUI.enabled = sel != null;
            if (GUILayout.Button("適用", GUILayout.Width(40)))
                ApplyPresetToLight(li, _applyPresetId, fromBeatSync: false, reason: "ui-apply");
            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawBeatPresetAssignment(LightInstanceSettings li)
        {
            GUILayout.Label("ビート→プリセット", _boldLabel);

            DrawPresetDropdown("Low",  ref li.Beat.LowPresetId);
            DrawPresetDropdown("Mid",  ref li.Beat.MidPresetId);
            DrawPresetDropdown("High", ref li.Beat.HighPresetId);
        }

        private void DrawPresetDropdown(string label, ref string presetId)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(36));

            string currentName = "なし";
            LightPreset current = FindPreset(presetId);
            if (current != null) currentName = current.Name;

            GUILayout.Label(currentName, GUILayout.Width(80));

            // 簡易選択: 左/右で切替
            if (GUILayout.Button("◀", GUILayout.Width(24)))
                CyclePresetId(ref presetId, -1);
            if (GUILayout.Button("▶", GUILayout.Width(24)))
                CyclePresetId(ref presetId,  1);
            if (GUILayout.Button("×", GUILayout.Width(24)))
                presetId = "";

            GUILayout.EndHorizontal();
        }

        private void CyclePresetId(ref string presetId, int dir)
        {
            var presets = _settings.Presets;
            if (presets.Count == 0) { presetId = ""; return; }

            int current = -1;
            for (int i = 0; i < presets.Count; i++)
                if (presets[i].Id == presetId) { current = i; break; }

            int next = (current + dir + presets.Count + 1) % (presets.Count + 1);
            presetId = next >= presets.Count ? "" : presets[next].Id;
        }

        // ────────────────────────────────────────────────────────────────────
        // プリセット一覧
        // ────────────────────────────────────────────────────────────────────

        private void DrawPresetSection()
        {
            string label = $"━━ プリセット ({_settings.Presets.Count}) ━━  {(_expandedPresets ? "▲" : "▼")}";
            if (GUILayout.Button(label, _boldFoldout))
                _expandedPresets = !_expandedPresets;

            if (!_expandedPresets) return;

            GUILayout.BeginVertical(GUI.skin.box);
            for (int i = 0; i < _settings.Presets.Count; i++)
            {
                var p = _settings.Presets[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label(p.Name, GUILayout.ExpandWidth(true));
                GUILayout.Label($"I:{p.Settings.Intensity:F1} A:{p.Settings.SpotAngle:F0}°", GUILayout.Width(100));
                if (GUILayout.Button("×", GUILayout.Width(24)))
                {
                    DeletePreset(i);
                    break;
                }
                GUILayout.EndHorizontal();
            }
            if (_settings.Presets.Count == 0)
                GUILayout.Label("(プリセットなし)");
            GUILayout.EndVertical();
        }

        // ────────────────────────────────────────────────────────────────────
        // 動画連携
        // ────────────────────────────────────────────────────────────────────

        private void DrawVideoMapSection()
        {
            string label = $"━━ 動画連携 ({_settings.VideoPresetMappings.Count}) ━━  {(_expandedVideoMap ? "▲" : "▼")}";
            if (GUILayout.Button(label, _boldFoldout))
                _expandedVideoMap = !_expandedVideoMap;

            if (!_expandedVideoMap) return;

            GUILayout.BeginVertical(GUI.skin.box);

            // 現在の動画を自動取得
            string currentVideo = "";
            try { currentVideo = MainGameBlankMapAdd.Plugin.GetCurrentVideoPath() ?? ""; }
            catch { }

            if (!string.IsNullOrEmpty(currentVideo))
            {
                GUILayout.Label($"現在: {System.IO.Path.GetFileName(currentVideo)}");
                if (GUILayout.Button("このパスを使用"))
                    _videoMapPathBuf = currentVideo;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("動画パス", GUILayout.Width(60));
            _videoMapPathBuf = GUILayout.TextField(_videoMapPathBuf, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("プリセット", GUILayout.Width(60));
            string selName = "なし";
            LightPreset selPreset = FindPreset(_videoMapPresetId);
            if (selPreset != null) selName = selPreset.Name;
            GUILayout.Label(selName, GUILayout.Width(80));
            if (GUILayout.Button("◀", GUILayout.Width(24))) CyclePresetId(ref _videoMapPresetId, -1);
            if (GUILayout.Button("▶", GUILayout.Width(24))) CyclePresetId(ref _videoMapPresetId,  1);
            GUILayout.EndHorizontal();

            if (GUILayout.Button("マッピング追加") && !string.IsNullOrEmpty(_videoMapPathBuf))
            {
                AddVideoMapping(_videoMapPathBuf, _videoMapPresetId);
                _videoMapPathBuf  = "";
                _videoMapPresetId = "";
            }

            GUILayout.Space(4);

            for (int i = 0; i < _settings.VideoPresetMappings.Count; i++)
            {
                var m = _settings.VideoPresetMappings[i];
                GUILayout.BeginHorizontal();
                string fn   = System.IO.Path.GetFileName(m.VideoPath);
                string pn   = FindPreset(m.PresetId)?.Name ?? "(なし)";
                GUILayout.Label($"{fn} → {pn}", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("×", GUILayout.Width(24)))
                {
                    RemoveVideoMapping(i);
                    break;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }

        // ────────────────────────────────────────────────────────────────────
        // 元ライト制御
        // ────────────────────────────────────────────────────────────────────

        private void DrawNativeLightSection()
        {
            string label = $"━━ 元ライト制御 ━━  {(_expandedNative ? "▲" : "▼")}";
            if (GUILayout.Button(label, _boldFoldout))
                _expandedNative = !_expandedNative;

            if (!_expandedNative) return;

            var nl = _settings.NativeLight;
            GUILayout.BeginVertical(GUI.skin.box);

            bool newOverride = GUILayout.Toggle(nl.OverrideEnabled, "制御有効");
            if (newOverride != nl.OverrideEnabled)
            {
                nl.OverrideEnabled = newOverride;
                if (nl.OverrideEnabled) ApplyNativeLightOverride();
                else ClearNativeLightsOverride();
            }

            if (nl.OverrideEnabled)
            {
                float prevScale = nl.IntensityScale;
                DrawSlider("強度スケール", ref nl.IntensityScale, 0f, 2f, 1f);
                if (nl.IntensityScale != prevScale)
                    ApplyNativeLightOverride();

                // 強度ループ
                GUILayout.Label("強度ループ", _boldLabel);
                nl.IntensityLoop.Enabled    = GUILayout.Toggle(nl.IntensityLoop.Enabled, "有効");
                nl.IntensityLoop.BeatFollow = GUILayout.Toggle(nl.IntensityLoop.BeatFollow, "動画連携(強度追従)");
                if (!nl.IntensityLoop.BeatFollow)
                    nl.IntensityLoop.VideoLink = GUILayout.Toggle(nl.IntensityLoop.VideoLink, "動画連携(BPM)");
                DrawSlider("NL_Min", ref nl.IntensityLoop.MinValue, 0f, 2f, 0f);
                DrawSlider("NL_Max", ref nl.IntensityLoop.MaxValue, 0f, 2f, 1f);
                if (!nl.IntensityLoop.BeatFollow && !nl.IntensityLoop.VideoLink)
                    DrawSlider("NL_Hz", ref nl.IntensityLoop.SpeedHz, 0f, 5f, 0.5f);

                // レインボー
                GUILayout.Label("レインボー", _boldLabel);
                nl.Rainbow.Enabled    = GUILayout.Toggle(nl.Rainbow.Enabled, "有効");
                nl.Rainbow.BeatFollow = GUILayout.Toggle(nl.Rainbow.BeatFollow, "動画連携(強度追従)");
                if (!nl.Rainbow.BeatFollow)
                    nl.Rainbow.VideoLink = GUILayout.Toggle(nl.Rainbow.VideoLink, "動画連携(BPM)");
                if (nl.Rainbow.BeatFollow || nl.Rainbow.VideoLink)
                {
                    DrawSlider("NL_RainbowMin", ref nl.Rainbow.MinCycleSpeed, 0f, 2f, 0f);
                    DrawSlider("NL_RainbowMax", ref nl.Rainbow.MaxCycleSpeed, 0f, 2f, 2f);
                }
                else
                    DrawSlider("NL_RainbowHz", ref nl.Rainbow.CycleSpeed, 0f, 2f, 0.2f);

                // ストロボ
                GUILayout.Label("ストロボ", _boldLabel);
                nl.Strobe.Enabled    = GUILayout.Toggle(nl.Strobe.Enabled, "有効");
                nl.Strobe.BeatFollow = GUILayout.Toggle(nl.Strobe.BeatFollow, "動画連携(強度追従)");
                if (!nl.Strobe.BeatFollow)
                    nl.Strobe.VideoLink = GUILayout.Toggle(nl.Strobe.VideoLink, "動画連携(BPM)");
                if (nl.Strobe.BeatFollow || nl.Strobe.VideoLink)
                {
                    DrawSlider("NL_StrobeMin", ref nl.Strobe.MinFrequencyHz, 0f, 20f, 0f);
                    DrawSlider("NL_StrobeMax", ref nl.Strobe.MaxFrequencyHz, 0f, 20f, 20f);
                }
                else
                    DrawSlider("NL_StrobeHz", ref nl.Strobe.FrequencyHz, 0f, 20f, 4f);

                // ON比率連動
                nl.Strobe.DutyBeatFollow = GUILayout.Toggle(nl.Strobe.DutyBeatFollow, "ON比率(ゾーン連動)");
                if (!nl.Strobe.DutyBeatFollow)
                    nl.Strobe.DutyVideoLink = GUILayout.Toggle(nl.Strobe.DutyVideoLink, "ON比率(BPM波動)");
                if (nl.Strobe.DutyBeatFollow || nl.Strobe.DutyVideoLink)
                {
                    DrawSlider("NL_DutyMin", ref nl.Strobe.MinDutyRatio, 0f, 1f, 0.1f);
                    DrawSlider("NL_DutyMax", ref nl.Strobe.MaxDutyRatio, 0f, 1f, 0.9f);
                }
                else
                    DrawSlider("NL_Duty", ref nl.Strobe.DutyRatio, 0f, 1f, 0.5f);
            }

            GUILayout.Label($"キャッシュ済みライト: {_nativeLights.Count}");
            if (GUILayout.Button("ライト再取得"))
                CacheNativeLights();

            GUILayout.EndVertical();
        }

        // ────────────────────────────────────────────────────────────────────
        // ビート閾値
        // ────────────────────────────────────────────────────────────────────

        private void DrawBeatThresholdSection()
        {
            string label = $"━━ ビート閾値 ━━  {(_expandedBeat ? "▲" : "▼")}";
            if (GUILayout.Button(label, _boldFoldout))
                _expandedBeat = !_expandedBeat;

            if (!_expandedBeat) return;

            GUILayout.BeginVertical(GUI.skin.box);

            float intensity = GetBeatIntensity();

            if (intensity >= 0f)
            {
                string zone = intensity < _settings.BeatLowThreshold  ? "Low"
                            : intensity < _settings.BeatHighThreshold ? "Mid"
                                                                       : "High";
                GUILayout.Label($"現在強度: {intensity:F2}  ゾーン: {zone}");
            }
            else
            {
                GUILayout.Label("BeatSync: 非アクティブ");
            }

            DrawSlider("Low閾値",  ref _settings.BeatLowThreshold,  0f, 1f, 0.4f);
            DrawSlider("High閾値", ref _settings.BeatHighThreshold, 0f, 1f, 0.75f);

            GUILayout.EndVertical();
        }

        // ────────────────────────────────────────────────────────────────────
        // 共通ユーティリティ
        // ────────────────────────────────────────────────────────────────────

        // ラベル → テキストバッファ（テキスト入力中の文字列を保持）
        private readonly Dictionary<string, string> _sliderTextBuf     = new Dictionary<string, string>();
        // 前フレームでフォーカスされていたテキストフィールドを記録
        private readonly HashSet<string>            _textFieldWasFocused = new HashSet<string>();

        private void DrawSlider(string label, ref float value, float min, float max, float? defaultValue = null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90));

            // 範囲外の値はスライダー表示用にクランプするが、実値は破壊しない
            float clampedDisplay = Mathf.Clamp(value, min, max);
            float newVal = GUILayout.HorizontalSlider(clampedDisplay, min, max, GUILayout.ExpandWidth(true));
            if (!Mathf.Approximately(newVal, clampedDisplay))
            {
                value = newVal;
                _sliderTextBuf[label] = value.ToString("F2");
            }

            // テキスト入力フィールド
            bool focused    = GUI.GetNameOfFocusedControl() == label + "_txt";
            bool wasFocused = _textFieldWasFocused.Contains(label);

            // フォーカスなし時はバッファを現在値に同期（ギズモ等の外部変更を反映）
            // ただし「直前フレームまでフォーカスあり → 今フレームで外れた」場合のみテキスト入力を適用
            if (!focused)
            {
                if (wasFocused)
                {
                    // フォーカスが外れた → ユーザーのテキスト入力を適用
                    string buf = _sliderTextBuf.ContainsKey(label) ? _sliderTextBuf[label] : value.ToString("F2");
                    if (float.TryParse(buf, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                        value = parsed;
                }
                // 外部変更（ギズモ移動等）を反映するため、バッファを現在値で上書き
                _sliderTextBuf[label] = value.ToString("F2");
                _textFieldWasFocused.Remove(label);
            }
            else
            {
                // フォーカス中はバッファを初期化のみ（ユーザー入力を壊さない）
                if (!_sliderTextBuf.ContainsKey(label))
                    _sliderTextBuf[label] = value.ToString("F2");
                _textFieldWasFocused.Add(label);
            }

            GUI.SetNextControlName(label + "_txt");
            string edited = GUILayout.TextField(_sliderTextBuf[label], GUILayout.Width(46));
            if (focused) _sliderTextBuf[label] = edited;

            // リセットボタン
            if (defaultValue.HasValue)
            {
                if (GUILayout.Button("R", GUILayout.Width(22)))
                {
                    value = defaultValue.Value;
                    _sliderTextBuf[label] = value.ToString("F2");
                }
            }

            GUILayout.EndHorizontal();
        }
    }
}
