using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MainGameCameraControl
{
    // UI_REDESIGN.md 準拠のタブ型 UI（GUILayout）。
    // 旧 DrawUiWindow（手計算 Rect）は Plugin.cs に残置するが OnGUI からは呼ばない。
    internal sealed partial class Plugin
    {
        private static readonly string[] TabLabels = { "呼び出し", "保存", "設定", "操作説明" };
        private int _selectedTab;
        private Vector2 _tabScroll;
        private string _renamingPresetName = string.Empty;
        private string _renameBuffer = string.Empty;
        private readonly HashSet<string> _expandedPresets = new HashSet<string>(StringComparer.Ordinal);
        private bool _captureTabPoseExpanded;
        // 削除は描画後に適用する（描画中に要素数を変えると GUILayout グループ不整合になるため）
        private int _pendingPresetRemove = -1;
        private CameraPreset _pendingPoseRemovePreset;
        private string _pendingPoseRemoveKey;

        private void DrawUiWindowV2(int id)
        {
            GUILayout.BeginArea(new Rect(
                8f,
                WindowDragHandleHeight + 4f,
                WindowWidth - 16f,
                WindowHeight - WindowDragHandleHeight - 12f));

            bool uiChanged = DrawCameraControlBody();

            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0f, 0f, WindowWidth, WindowDragHandleHeight));

            ApplyPostDrawMutations(uiChanged);
        }

        // 窓の中身本体（タブバー＋スクロール＋状態表示）。BeginArea/ドラッグ枠の外側は
        // 呼び出し側が持つ。自前窓と総合UIタブの両方から共用する。
        private bool DrawCameraControlBody()
        {
            bool uiChanged = false;

            int nextTab = GUILayout.Toolbar(Mathf.Clamp(_selectedTab, 0, TabLabels.Length - 1), TabLabels);
            if (nextTab != _selectedTab)
            {
                _selectedTab = nextTab;
                _tabScroll = Vector2.zero;
            }
            GUILayout.Space(4f);

            _tabScroll = GUILayout.BeginScrollView(_tabScroll);
            switch (_selectedTab)
            {
                case 0: uiChanged |= DrawPresetTab(); break;
                case 1: uiChanged |= DrawCaptureTab(); break;
                case 2: uiChanged |= DrawSettingsTab(); break;
                case 3: DrawHelpTab(); break;
            }
            GUILayout.EndScrollView();

            if (!string.IsNullOrWhiteSpace(_statusMessage))
            {
                GUILayout.Space(2f);
                GUILayout.Label(_statusMessage);
            }

            return uiChanged;
        }

        // 描画完了後にまとめて適用（描画中に要素数を変えると GUILayout 不整合エラーになる）。
        private void ApplyPostDrawMutations(bool uiChanged)
        {
            if (_pendingPresetRemove >= 0)
            {
                int idx = _pendingPresetRemove;
                _pendingPresetRemove = -1;
                RemovePresetAt(idx);
            }
            if (_pendingPoseRemovePreset != null)
            {
                CameraPreset p = _pendingPoseRemovePreset;
                string k = _pendingPoseRemoveKey;
                _pendingPoseRemovePreset = null;
                _pendingPoseRemoveKey = null;
                RemovePoseOverride(p, k);
            }

            if (uiChanged)
                SaveSettings();
        }

        // ---- タブ: プリセット ----
        private bool DrawPresetTab()
        {
            bool changed = false;

            GUILayout.Label("プリセット一覧");
            if (_presets.Count == 0)
                GUILayout.Label("（保存済みなし）");

            for (int i = 0; i < _presets.Count; i++)
            {
                CameraPreset preset = _presets[i];
                if (preset == null)
                    continue;

                bool renaming = !string.IsNullOrEmpty(_renamingPresetName)
                    && string.Equals(_renamingPresetName, preset.Name ?? string.Empty, StringComparison.Ordinal);

                GUILayout.BeginHorizontal();
                if (renaming)
                {
                    _renameBuffer = GUILayout.TextField(_renameBuffer ?? string.Empty, GUILayout.ExpandWidth(true));
                    string trimmed = (_renameBuffer ?? string.Empty).Trim();
                    bool canOk = trimmed.Length > 0
                        && !string.Equals(trimmed, preset.Name ?? string.Empty, StringComparison.Ordinal)
                        && FindPresetByName(trimmed) == null;
                    bool prevEnabled = GUI.enabled;
                    GUI.enabled = canOk;
                    if (GUILayout.Button("OK", GUILayout.Width(40f)))
                    {
                        RenamePreset(preset, trimmed);
                        _renamingPresetName = string.Empty;
                        changed = true;
                    }
                    GUI.enabled = prevEnabled;
                    if (GUILayout.Button("Cancel", GUILayout.Width(60f)))
                        _renamingPresetName = string.Empty;
                }
                else
                {
                    string key = preset.Name ?? string.Empty;
                    bool expanded = _expandedPresets.Contains(key);
                    bool quickNext = GUILayout.Toggle(preset.QuickCycleEnabled, string.Empty, GUILayout.Width(20f));
                    if (quickNext != preset.QuickCycleEnabled)
                    {
                        preset.QuickCycleEnabled = quickNext;
                        changed = true;
                    }
                    if (GUILayout.Button(expanded ? "▽" : "▷", GUILayout.Width(24f)))
                    {
                        if (expanded)
                            _expandedPresets.Remove(key);
                        else
                            _expandedPresets.Add(key);
                    }

                    Color prevColor = GUI.backgroundColor;
                    if (IsSelectedPreset(preset))
                        GUI.backgroundColor = Color.red;
                    if (GUILayout.Button(GetPresetDisplayName(preset, i), GUILayout.ExpandWidth(true)))
                        LoadPreset(preset);
                    GUI.backgroundColor = prevColor;

                    if (GUILayout.Button("↻", GUILayout.Width(26f)))
                        OverwritePresetPoseAware(preset);
                    if (GUILayout.Button("✎", GUILayout.Width(26f)))
                    {
                        _renamingPresetName = preset.Name ?? string.Empty;
                        _renameBuffer = preset.Name ?? string.Empty;
                    }
                    if (GUILayout.Button("x", GUILayout.Width(24f)))
                        _pendingPresetRemove = i;
                }
                GUILayout.EndHorizontal();

                if (!renaming && _expandedPresets.Contains(preset.Name ?? string.Empty))
                    changed |= DrawPresetDetail(preset);
            }

            GUILayout.Space(6f);
            GUILayout.Label("▶呼び出し  ↻体位を更新(無ければ追加)  ✎改名  x削除");

            GUILayout.Space(6f);
            changed |= DrawAutoCameraCycleControls();

            GUILayout.Space(6f);
            // 全プリセット一括の「位置を全部使う」。押すと全プリセットの位置データがある行をまとめてON/OFF。
            bool allUseGlobal = AllUseCameraPositionOnAllPresets(out bool anyDataGlobal);
            if (anyDataGlobal)
            {
                bool nextUseGlobal = GUILayout.Toggle(allUseGlobal, "位置を全部使う（全プリセット）");
                if (nextUseGlobal != allUseGlobal)
                    SetAllUseCameraPositionAllPresets(nextUseGlobal);
            }

            bool nextAuto = GUILayout.Toggle(_posePresetAutoApply, "体位で自動切替");
            if (nextAuto != _posePresetAutoApply)
            {
                _posePresetAutoApply = nextAuto;
                changed = true;
            }

            // 拍FOVズーム：ON/OFFトグル＋倍率スライダー。
            changed |= DrawBeatFovToggle();
            if (_cfgBeatFovZoomEnabled.Value)
                changed |= DrawBeatZoomMultiplierSlider();

            GUILayout.Label("現在体位: " + (string.IsNullOrEmpty(_lastPoseDisplayName) ? "(なし)" : _lastPoseDisplayName));

            GUILayout.Space(4f);
            GUILayout.Label("遷移時間");
            float nextTrans = LayoutSliderField(string.Empty, _cfgTransitionSeconds.Value, 0f, 3f, "F2");
            if (!Mathf.Approximately(nextTrans, _cfgTransitionSeconds.Value))
            {
                _cfgTransitionSeconds.Value = nextTrans;
                changed = true;
            }

            GUILayout.Label("イージング");
            int nextEasing = GUILayout.SelectionGrid(
                Mathf.Clamp(_cfgTransitionEasing.Value, 0, EasingLabels.Length - 1), EasingLabels, 2);
            if (nextEasing != _cfgTransitionEasing.Value)
            {
                _cfgTransitionEasing.Value = nextEasing;
                changed = true;
            }

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("解除"))
            {
                ClearBoneLinkActiveState();
            }
            if (GUILayout.Button("Reset"))
                ResetCameraFollowState();
            GUILayout.EndHorizontal();

            return changed;
        }

        private bool DrawAutoCameraCycleControls()
        {
            bool changed = false;
            bool nextAutoCycle = GUILayout.Toggle(_cfgAutoCameraCycleEnabled.Value, "自動カメラ切替");
            if (nextAutoCycle != _cfgAutoCameraCycleEnabled.Value)
            {
                _cfgAutoCameraCycleEnabled.Value = nextAutoCycle;
                ResetAutoCameraCycleTimer();
                changed = true;
            }

            if (_cfgAutoCameraCycleEnabled.Value)
            {
                GUILayout.Label("滞在時間");
                float nextStay = LayoutSliderField(string.Empty, _cfgAutoCameraStaySeconds.Value, 1f, 120f, "F1");
                if (!Mathf.Approximately(nextStay, _cfgAutoCameraStaySeconds.Value))
                {
                    _cfgAutoCameraStaySeconds.Value = nextStay;
                    ResetAutoCameraCycleTimer();
                    changed = true;
                }
            }

            return changed;
        }

        // ---- タブ: 撮影 ----
        private bool DrawCaptureTab()
        {
            bool changed = false;

            // 保存プレビュー（押す前に何が作られるか）
            string modeLabel = SaveModeLabels[Mathf.Clamp(_selectedSaveMode, 0, SaveModeLabels.Length - 1)];
            string poseName = string.IsNullOrEmpty(_lastPoseDisplayName) ? _lastPoseKey : _lastPoseDisplayName;
            string poseInfo = (_saveCameraPoseOverrides && !string.IsNullOrEmpty(_lastPoseKey))
                ? "  体位「" + poseName + "」に記録"
                : string.Empty;
            GUILayout.Label("今撮ると → [" + modeLabel + "] FOV"
                + GetLiveCurrentFov().ToString("0.#", CultureInfo.InvariantCulture) + poseInfo);

            // 現在FOV編集スライダー（演出タブから移設）。
            GUILayout.Space(4f);
            changed |= DrawCurrentFovSlider();

            GUILayout.Space(4f);
            GUILayout.Label("保存モード");
            int nextMode = GUILayout.SelectionGrid(
                Mathf.Clamp(_selectedSaveMode, 0, SaveModeLabels.Length - 1), SaveModeLabels, SaveModeLabels.Length);
            if (nextMode != _selectedSaveMode)
            {
                _selectedSaveMode = nextMode;
                changed = true;
            }

            if (_selectedSaveMode == SaveModeBoneLink)
            {
                GUILayout.Label("対象ボーン");
                int nextBone = GUILayout.SelectionGrid(
                    Mathf.Clamp(_selectedBoneTarget, 0, BoneTargetLabels.Length - 1), BoneTargetLabels, BoneTargetLabels.Length);
                if (nextBone != _selectedBoneTarget)
                {
                    // ボーン連動中なら、今の見え方（注視点・カメラ位置）を維持したまま
                    // 新ボーン基準の相対へ変換して追従対象を即乗り換える。
                    if (_boneLinkActive)
                        RetargetActiveBoneLink(nextBone);
                    else
                        _selectedBoneTarget = nextBone;
                    changed = true;
                }

                // 相対方向: 保存した瞬間の「基準ボーンから見た方向と距離」を焼き込み、
                // 呼び出し中はどんな体位でもその角度と距離を保つ。
                bool nextRelDir = GUILayout.Toggle(_saveRelativeDirection, "相対方向で保存（どの体位でも同じ角度から写す）");
                if (nextRelDir != _saveRelativeDirection)
                {
                    _saveRelativeDirection = nextRelDir;
                    changed = true;
                }

                if (_saveRelativeDirection)
                {
                    // 既定は水平維持。入れるとキャラが寝た時に画面ごと倒れる。
                    bool nextRoll = GUILayout.Toggle(_saveRollWithBone, "　ロールも追従（寝ると画面ごと傾く）");
                    if (nextRoll != _saveRollWithBone)
                    {
                        _saveRollWithBone = nextRoll;
                        changed = true;
                    }
                    GUILayout.Label("　向きの基準は腰。今見えている構図がそのまま「この角度」になります。");
                }
            }

            bool nextPose = GUILayout.Toggle(_saveCameraPoseOverrides, "体位ごとに保存");
            if (nextPose != _saveCameraPoseOverrides)
            {
                _saveCameraPoseOverrides = nextPose;
                changed = true;
            }

            // 保存前に注視点を微調整するためのスライダー（ボーン連動中のみ表示）。
            changed |= DrawLookAtOffsetSliders();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("保存名", GUILayout.Width(48f));
            string nextName = GUILayout.TextField(_presetNameBuffer ?? string.Empty, GUILayout.ExpandWidth(true));
            if (nextName != _presetNameBuffer)
                _presetNameBuffer = nextName;
            GUILayout.EndHorizontal();

            // 柱C: 保存ボタンは保存名欄基準で新規/上書きを切替（ラベルに結果を出す）
            string targetName = (_presetNameBuffer ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(targetName))
            {
                bool prev = GUI.enabled;
                GUI.enabled = false;
                GUILayout.Button("保存できません（名前を入力）");
                GUI.enabled = prev;
            }
            else
            {
                CameraPreset existing = FindPresetByName(targetName);
                string label = existing != null
                    ? "↻ " + targetName + " を上書き保存"
                    : "+ " + targetName + " を新規保存";
                if (GUILayout.Button(label))
                {
                    SaveOrOverwritePreset();
                    changed = true;
                }
            }

            // 柱D: 体位登録ボタン（追加/更新/不可理由）
            GUILayout.Space(6f);
            GUILayout.Label("アクティブプリセットへの体位登録");
            DrawPoseRegisterButton();

            if (GUILayout.Button(_captureTabPoseExpanded ? "▽ このプリセットの中身を隠す" : "▷ このプリセットの中身を見る"))
                _captureTabPoseExpanded = !_captureTabPoseExpanded;
            if (_captureTabPoseExpanded)
            {
                CameraPreset active = FindPresetByName(_activeCameraPresetName);
                if (active != null)
                    changed |= DrawPresetDetail(active);
                else
                    GUILayout.Label("（プリセット未選択）");
            }

            return changed;
        }

        private void DrawPoseRegisterButton()
        {
            bool prev = GUI.enabled;
            if (string.IsNullOrWhiteSpace(_activeCameraPresetName))
            {
                GUI.enabled = false;
                GUILayout.Button("体位を登録できません（プリセット未選択）");
                GUI.enabled = prev;
                return;
            }
            if (string.IsNullOrEmpty(_lastPoseKey))
            {
                GUI.enabled = false;
                GUILayout.Button("体位を登録できません（体位情報なし）");
                GUI.enabled = prev;
                return;
            }

            CameraPreset active = FindPresetByName(_activeCameraPresetName);
            bool registered = active != null && FindCameraPoseOverride(active, _lastPoseKey) != null;
            string poseName = string.IsNullOrEmpty(_lastPoseDisplayName) ? _lastPoseKey : _lastPoseDisplayName;
            string label = registered ? "↻ " + poseName + " を更新" : "+ " + poseName + " を追加";
            if (GUILayout.Button(label))
                CapturePoseOverrideForActivePreset();
        }

        // 現在FOV編集スライダー（保存タブ）。拍ON時はベースFOVを編集、OFF時はライブFOV。
        // ライブ（ズーム後）値を読むと毎フレーム縮んだ値が表示され書き戻して下限張り付きになるため、
        // 拍ON時は ResolveBeatBaseFov を編集対象にする。F2丸めで無操作時の誤検知も防ぐ。
        private bool DrawCurrentFovSlider()
        {
            bool changed = false;
            bool beatOn = IsBeatFovEnabled();
            GUILayout.Label(beatOn ? "現在FOV(拍子ベース)" : "現在FOV");
            GUILayout.BeginHorizontal();
            float source = beatOn ? Mathf.Clamp(ResolveBeatBaseFov(), 5f, 120f) : GetLiveCurrentFov();
            source = Mathf.Round(source * 100f) / 100f;
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && _hasCameraController;
            float nextLive = GUILayout.HorizontalSlider(source, 5f, 120f, GUILayout.ExpandWidth(true));
            string fovTxt = GUILayout.TextField(source.ToString("F2", CultureInfo.InvariantCulture), GUILayout.Width(56f));
            GUI.enabled = prevEnabled;
            GUILayout.EndHorizontal();

            // テキストが実際に編集された時だけスライダー値を上書き（無編集なら parse≈source で素通り）。
            if (float.TryParse(fovTxt, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedFov))
            {
                float pf = Mathf.Clamp(parsedFov, 5f, 120f);
                if (Mathf.Abs(pf - source) > 0.001f)
                    nextLive = pf;
            }

            if (_hasCameraController && Mathf.Abs(nextLive - source) > 0.001f)
            {
                if (!_cfgApplyFov.Value)
                    _cfgApplyFov.Value = true;
                if (beatOn)
                    ApplyBaseFovFromUi(nextLive);
                else
                    ApplyLiveFovFromUi(nextLive);
                changed = true;
            }
            return changed;
        }

        // 拍FOVズームの倍率スライダー（呼び出しタブ）。
        private bool DrawBeatZoomMultiplierSlider()
        {
            GUILayout.Label("ズーム倍率");
            float nextMul = LayoutSliderField(string.Empty, _cfgBeatFovZoomMultiplier.Value, 1f, 3f, "F2");
            if (Mathf.Approximately(nextMul, _cfgBeatFovZoomMultiplier.Value))
                return false;
            _cfgBeatFovZoomMultiplier.Value = nextMul;
            return true;
        }

        // ギズモ表示トグル＋サイズスライダー（設定タブ）。
        private bool DrawGizmoControls()
        {
            bool changed = false;
            bool nextGizmo = GUILayout.Toggle(_gizmoVisible, "ギズモ表示");
            if (nextGizmo != _gizmoVisible)
            {
                _gizmoVisible = nextGizmo;
                if (_cfgGizmoVisible != null)
                    _cfgGizmoVisible.Value = nextGizmo;
                ApplyLookAtAnchorGizmoState();
                changed = true;
            }
            GUILayout.Label("ギズモサイズ");
            float nextSize = LayoutSliderField(string.Empty, _cfgGizmoSize.Value, 0.2f, 1.5f, "F2");
            if (!Mathf.Approximately(nextSize, _cfgGizmoSize.Value))
            {
                _cfgGizmoSize.Value = nextSize;
                ApplyLookAtAnchorGizmoState();
                changed = true;
            }
            return changed;
        }

        // ---- タブ: 設定 ----
        private bool DrawSettingsTab()
        {
            bool changed = false;

            bool nextApplyFov = GUILayout.Toggle(_cfgApplyFov.Value, "FOVも適用");
            if (nextApplyFov != _cfgApplyFov.Value)
            {
                _cfgApplyFov.Value = nextApplyFov;
                if (!nextApplyFov)
                    _lastAppliedFov = -1f;
                changed = true;
            }

            // 相対方向の動作切替。3つとも独立して切れる。
            // どれも「焼いた構図をそのまま再現する」からの逸脱なので、何が効いているかを
            // 1つずつ確かめられるようにしてある。
            GUILayout.Space(4f);
            GUILayout.Label("相対方向の動作");

            bool nextRebake = GUILayout.Toggle(_relRebakeOnManual,
                "　手で動かしたら角度を焼き直す（切ると離した時に戻る）");
            if (nextRebake != _relRebakeOnManual)
            {
                _relRebakeOnManual = nextRebake;
                changed = true;
            }

            bool nextFloor = GUILayout.Toggle(_relFloorGuard,
                "　床下に置かない（切ると焼いた通りの位置に置く）");
            if (nextFloor != _relFloorGuard)
            {
                _relFloorGuard = nextFloor;
                changed = true;
            }

            bool nextYawFix = GUILayout.Toggle(_relYawUpAxisFallback,
                "　腰が倒れた時は別の軸で体の向きを取る");
            if (nextYawFix != _relYawUpAxisFallback)
            {
                _relYawUpAxisFallback = nextYawFix;
                changed = true;
            }

            GUILayout.Space(4f);
            bool nextDetail = GUILayout.Toggle(_cfgDetailLogEnabled.Value, "詳細ログ");
            if (nextDetail != _cfgDetailLogEnabled.Value)
            {
                _cfgDetailLogEnabled.Value = nextDetail;
                changed = true;
            }

            bool nextVoice = GUILayout.Toggle(_cfgVoiceFaceEventCameraTriggerEnabled.Value, "VoiceFaceEventカメラトリガー");
            if (nextVoice != _cfgVoiceFaceEventCameraTriggerEnabled.Value)
            {
                _cfgVoiceFaceEventCameraTriggerEnabled.Value = nextVoice;
                changed = true;
            }

            bool nextVoiceReturn = GUILayout.Toggle(_cfgVoiceFaceEventReturnEnabled.Value, "VoiceFaceEvent後に戻す");
            if (nextVoiceReturn != _cfgVoiceFaceEventReturnEnabled.Value)
            {
                _cfgVoiceFaceEventReturnEnabled.Value = nextVoiceReturn;
                changed = true;
            }

            if (_cfgVoiceFaceEventReturnEnabled.Value)
            {
                GUILayout.Label("VoiceFaceEvent滞在時間");
                float nextVoiceStay = LayoutSliderField(string.Empty, _cfgVoiceFaceEventReturnStaySeconds.Value, 0.5f, 60f, "F1");
                if (!Mathf.Approximately(nextVoiceStay, _cfgVoiceFaceEventReturnStaySeconds.Value))
                {
                    _cfgVoiceFaceEventReturnStaySeconds.Value = nextVoiceStay;
                    changed = true;
                }
            }

            // ギズモ表示トグル＋サイズ（演出タブから移設）。
            GUILayout.Space(4f);
            changed |= DrawGizmoControls();

            GUILayout.Space(4f);
            _uiFoldoutCurrent = GUILayout.Toggle(_uiFoldoutCurrent, "現在値（デバッグ）");
            if (_uiFoldoutCurrent)
            {
                GUILayout.Label(_hasCameraController ? "CameraControl_Ver2: OK" : "CameraControl_Ver2: 未取得");
                GUILayout.Label(FormatVector("Target", _currentData.Pos));
                GUILayout.Label(FormatVector("Dir", _currentData.Dir));
                GUILayout.Label(FormatVector("Rot", _currentData.Rot));
                GUILayout.Label("DataFov: " + GetCurrentDataFov().ToString("0.##", CultureInfo.InvariantCulture));
                GUILayout.Label("LiveFov: " + GetLiveCurrentFov().ToString("0.##", CultureInfo.InvariantCulture));
                GUILayout.Label("PresetFov: " + GetTargetPreviewFov().ToString("0.##", CultureInfo.InvariantCulture));
            }

            return changed;
        }

        // ---- 共通ヘルパ ----
        private float LayoutSliderField(string label, float value, float min, float max, string format = "F2")
        {
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(label))
                GUILayout.Label(label, GUILayout.Width(20f));
            float next = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            string txt = GUILayout.TextField(value.ToString(format, CultureInfo.InvariantCulture), GUILayout.Width(56f));
            GUILayout.EndHorizontal();

            if (float.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                float clamped = Mathf.Clamp(parsed, min, max);
                if (!Mathf.Approximately(clamped, value))
                    return clamped;
            }
            return Mathf.Clamp(next, min, max);
        }

        private bool LayoutBoneOffset(string label, int axis)
        {
            float current = GetBoneLinkOffsetAxis(axis);
            float next = LayoutSliderField(label, current, -2f, 2f, "F2");
            if (Mathf.Approximately(next, current))
                return false;
            SetBoneLinkOffsetAxis(axis, next);
            return true;
        }

        // 拍FOVズームのON/OFFトグル（呼び出しタブ/演出タブ共用）。
        // 有効化時は base をキャプチャ、無効化時は base へ戻す。
        private bool DrawBeatFovToggle()
        {
            bool nextBeat = GUILayout.Toggle(_cfgBeatFovZoomEnabled.Value, "拍FOVズーム");
            if (nextBeat == _cfgBeatFovZoomEnabled.Value)
                return false;
            // Config change owns capture/restore for both this UI and ConfigManager.
            _cfgBeatFovZoomEnabled.Value = nextBeat;
            return true;
        }

        // 注視点オフセットの X/Y/Z スライダー（ボーン連動中のみ表示）。保存タブ/演出タブ共用。
        private bool DrawLookAtOffsetSliders()
        {
            if (!_boneLinkActive)
                return false;
            bool changed = false;
            GUILayout.Space(4f);
            GUILayout.Label("座標系: " + GetGizmoAxisSpaceLabel());
            GUILayout.Label("注視点オフセット");
            changed |= LayoutBoneOffset("X", 0);
            changed |= LayoutBoneOffset("Y", 1);
            changed |= LayoutBoneOffset("Z", 2);
            return changed;
        }

        private void RenamePreset(CameraPreset preset, string newNameRaw)
        {
            string newName = (newNameRaw ?? string.Empty).Trim();
            if (preset == null || newName.Length == 0)
                return;
            string old = preset.Name ?? string.Empty;
            if (string.Equals(old, newName, StringComparison.Ordinal))
                return;
            if (FindPresetByName(newName) != null)
            {
                SetStatus("改名不可: 同名あり " + newName);
                return;
            }

            preset.Name = newName;
            if (string.Equals(_activeCameraPresetName, old, StringComparison.Ordinal))
                _activeCameraPresetName = newName;
            if (string.Equals(_activeBoneLinkPresetName, old, StringComparison.Ordinal))
                _activeBoneLinkPresetName = newName;
            if (string.Equals((_presetNameBuffer ?? string.Empty).Trim(), old, StringComparison.Ordinal))
                _presetNameBuffer = newName;

            CommitPresets("rename:" + old + "->" + newName);
            SetStatus("改名: " + old + " → " + newName);
        }

        // 展開時の中身表示（基底＋体位オーバーライド一覧）。体位行に個別削除ボタン付き。
        // 体位を削除したら true（呼び出し側は列挙を打ち切る）。
        private bool DrawPresetDetail(CameraPreset preset)
        {
            if (preset == null)
                return false;

            bool changed = false;
            GUILayout.BeginVertical(GUI.skin.box);

            changed |= DrawPresetLiveCameraControls(preset);
            GUILayout.Space(4f);

            // 「位置を全部使う」：このプリセットの基底＋全体位のうち位置データがある行を一括ON/OFF。
            // （位置データが無い行は対象外＝触らない）
            bool allOn = AllUseCameraPositionOn(preset, out bool anyData);
            if (anyData)
            {
                bool nextAll = GUILayout.Toggle(allOn, "位置を全部使う");
                if (nextAll != allOn)
                    SetAllUseCameraPosition(preset, nextAll);
            }

            // 基底行：位置データがあれば「位置」トグル、無ければ「位置未保存(↻)」表示
            GUILayout.BeginHorizontal();
            GUILayout.Label("基底  " + DescribePresetBase(preset), GUILayout.ExpandWidth(true));
            if (preset.UseBoneLink && PresetHasStoredOffset(preset))
            {
                bool useNext = GUILayout.Toggle(preset.SaveCameraPosition, "位置", GUILayout.Width(54f));
                if (useNext != preset.SaveCameraPosition)
                {
                    preset.SaveCameraPosition = useNext;
                    CommitPresets("toggle-usepos:" + (preset.Name ?? string.Empty));
                }
            }
            else if (preset.UseBoneLink)
            {
                GUILayout.Label("位置未保存(↻)", GUILayout.Width(96f));
            }
            GUILayout.EndHorizontal();

            CameraPoseOverride[] overrides = preset.PoseOverrides ?? new CameraPoseOverride[0];
            if (overrides.Length == 0)
            {
                GUILayout.Label("（体位オーバーライドなし）");
            }
            else
            {
                for (int i = 0; i < overrides.Length; i++)
                {
                    CameraPoseOverride po = overrides[i];
                    if (po == null)
                        continue;

                    bool current = !string.IsNullOrEmpty(_lastPoseKey)
                        && string.Equals(po.Key, _lastPoseKey, StringComparison.Ordinal);
                    string disp = string.IsNullOrEmpty(po.DisplayName) ? po.Key : po.DisplayName;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label((current ? "▶ " : "　") + disp + "  " + DescribePoseOverride(po), GUILayout.ExpandWidth(true));
                    if (po.UseBoneLink && PoseHasStoredOffset(po))
                    {
                        bool useNext = GUILayout.Toggle(po.SaveCameraPosition, "位置", GUILayout.Width(54f));
                        if (useNext != po.SaveCameraPosition)
                        {
                            po.SaveCameraPosition = useNext;
                            CommitPresets("toggle-usepos-pose:" + po.Key);
                        }
                    }
                    else if (po.UseBoneLink)
                    {
                        GUILayout.Label("位置未保存(↻)", GUILayout.Width(96f));
                    }
                    if (GUILayout.Button("x", GUILayout.Width(24f)))
                    {
                        _pendingPoseRemovePreset = preset;
                        _pendingPoseRemoveKey = po.Key;
                    }
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndVertical();
            return changed;
        }

        private bool DrawPresetLiveCameraControls(CameraPreset preset)
        {
            bool changed = false;

            GUILayout.BeginHorizontal();
            changed |= DrawPresetModeButton("通常", false, preset);
            changed |= DrawPresetModeButton("ボーン", true, preset);
            GUILayout.EndHorizontal();

            bool activePreset = IsSelectedPreset(preset);
            if (activePreset && _boneLinkActive)
            {
                GUILayout.BeginHorizontal();
                changed |= DrawPresetBoneButton("頭", BoneTargetHead, preset);
                changed |= DrawPresetBoneButton("胸", BoneTargetChest, preset);
                changed |= DrawPresetBoneButton("腰", BoneTargetWaist, preset);
                GUILayout.EndHorizontal();

                changed |= DrawLookAtOffsetSliders();
            }

            if (GUILayout.Button("↻ 現在体位を保存"))
            {
                OverwritePresetPoseAware(preset);
                changed = true;
            }

            return changed;
        }

        private bool DrawPresetModeButton(string label, bool boneMode, CameraPreset preset)
        {
            bool activePreset = IsSelectedPreset(preset);
            bool active = activePreset && (boneMode ? _boneLinkActive : !_boneLinkActive);
            Color prev = GUI.backgroundColor;
            if (active)
                GUI.backgroundColor = Color.red;

            bool clicked = GUILayout.Button(label, GUILayout.Width(64f));
            GUI.backgroundColor = prev;

            if (!clicked)
                return false;

            if (boneMode)
            {
                int target = activePreset && _boneLinkActive && _activeBoneLinkTarget >= 0
                    ? _activeBoneLinkTarget
                    : Mathf.Clamp(preset != null && preset.UseBoneLink ? preset.BoneTarget : _selectedBoneTarget, 0, BoneTargetLabels.Length - 1);
                ActivatePresetBoneLinkLive(preset, target);
            }
            else
            {
                ActivatePresetNormalLive(preset);
            }

            return true;
        }

        private bool DrawPresetBoneButton(string label, int target, CameraPreset preset)
        {
            Color prev = GUI.backgroundColor;
            if (IsSelectedPreset(preset) && _boneLinkActive && _activeBoneLinkTarget == target)
                GUI.backgroundColor = Color.red;

            bool clicked = GUILayout.Button(label, GUILayout.Width(52f));
            GUI.backgroundColor = prev;

            if (!clicked)
                return false;

            ActivatePresetBoneLinkLive(preset, target);
            return true;
        }

        private static bool PresetHasStoredOffset(CameraPreset p)
        {
            return (p.CameraOffsetBonePos != null && p.CameraOffsetBonePos.Length >= 3)
                || (p.CameraOffsetLocal != null && p.CameraOffsetLocal.Length >= 3)
                || (p.CameraOffsetWorld != null && p.CameraOffsetWorld.Length >= 3);
        }

        private static bool PoseHasStoredOffset(CameraPoseOverride po)
        {
            return (po.CameraOffsetBonePos != null && po.CameraOffsetBonePos.Length >= 3)
                || (po.CameraOffsetLocal != null && po.CameraOffsetLocal.Length >= 3)
                || (po.CameraOffsetWorld != null && po.CameraOffsetWorld.Length >= 3);
        }

        // 位置データがある行（基底＋体位）が全てON か。anyData=対象行が1つでもあるか。
        private static bool AllUseCameraPositionOn(CameraPreset preset, out bool anyData)
        {
            anyData = false;
            bool all = true;
            if (preset.UseBoneLink && PresetHasStoredOffset(preset))
            {
                anyData = true;
                if (!preset.SaveCameraPosition) all = false;
            }
            if (preset.PoseOverrides != null)
            {
                foreach (CameraPoseOverride po in preset.PoseOverrides)
                {
                    if (po != null && po.UseBoneLink && PoseHasStoredOffset(po))
                    {
                        anyData = true;
                        if (!po.SaveCameraPosition) all = false;
                    }
                }
            }
            return anyData && all;
        }

        // 位置データがある行（基底＋体位）を一括で value に設定して保存。データ無し行は触らない。
        private void SetAllUseCameraPosition(CameraPreset preset, bool value)
        {
            bool any = false;
            if (preset.UseBoneLink && PresetHasStoredOffset(preset))
            {
                preset.SaveCameraPosition = value;
                any = true;
            }
            if (preset.PoseOverrides != null)
            {
                foreach (CameraPoseOverride po in preset.PoseOverrides)
                {
                    if (po != null && po.UseBoneLink && PoseHasStoredOffset(po))
                    {
                        po.SaveCameraPosition = value;
                        any = true;
                    }
                }
            }
            if (any)
                CommitPresets("set-all-usepos:" + (preset.Name ?? string.Empty));
        }

        // 全プリセット横断：位置データがある行が全てONか。
        private bool AllUseCameraPositionOnAllPresets(out bool anyData)
        {
            anyData = false;
            bool all = true;
            foreach (CameraPreset p in _presets)
            {
                if (p == null) continue;
                bool sub = AllUseCameraPositionOn(p, out bool subAny);
                if (subAny)
                {
                    anyData = true;
                    if (!sub) all = false;
                }
            }
            return anyData && all;
        }

        // 全プリセットの位置データがある行を一括で value に設定（1回だけ保存）。
        private void SetAllUseCameraPositionAllPresets(bool value)
        {
            bool any = false;
            foreach (CameraPreset p in _presets)
            {
                if (p == null) continue;
                if (p.UseBoneLink && PresetHasStoredOffset(p))
                {
                    p.SaveCameraPosition = value;
                    any = true;
                }
                if (p.PoseOverrides != null)
                {
                    foreach (CameraPoseOverride po in p.PoseOverrides)
                    {
                        if (po != null && po.UseBoneLink && PoseHasStoredOffset(po))
                        {
                            po.SaveCameraPosition = value;
                            any = true;
                        }
                    }
                }
            }
            if (any)
                CommitPresets("set-all-usepos-global:" + value);
        }

        private static string DescribeModeBone(bool useBone, int boneTarget)
        {
            if (!useBone)
                return "通常";
            string b = (boneTarget >= 0 && boneTarget < BoneTargetLabels.Length) ? BoneTargetLabels[boneTarget] : "?";
            return "ボーン:" + b;
        }

        private static string DescribeTag(bool look, bool pos)
        {
            string s = string.Empty;
            if (look) s += "[注視]";
            if (pos) s += "[位置]";
            return s;
        }

        private static string DescribePresetBase(CameraPreset p)
        {
            bool look, pos;
            if (p.UseBoneLink)
            {
                look = false;
                pos = HasPresetCameraPosition(p);
            }
            else
            {
                look = p.TargetPosition != null && p.TargetPosition.Length >= 3;
                pos = p.CameraDirection != null && p.CameraDirection.Length >= 3;
            }
            return DescribeModeBone(p.UseBoneLink, p.BoneTarget)
                + " FOV" + p.Fov.ToString("0.#", CultureInfo.InvariantCulture)
                + " " + DescribeTag(look, pos)
                + "  " + DescribeRelativeDirection(p);
        }

        /// <summary>
        /// 相対方向の中身を1行で出す。基準点が注視点か腰(旧)かは、焼き直しが要るかの判断に直結する。
        /// 体位行では、その体位に保存した値か、旧データの共通設定継承かを表示する。
        /// </summary>
        private static string DescribeRelativeDirection(CameraPreset p)
        {
            if (!HasRelativeDirection(p))
                return "相対:なし";

            return DescribeRelativeDirection(p.RelativeFromLookAt, p.RelativeDistance, p.RollWithBone, p.FrameBoneName);
        }

        private static string DescribeRelativeDirection(bool fromLookAt, float distance, bool roll, string frameBone)
        {
            string text = "相対:" + (fromLookAt ? "注視点" : "腰(旧/要焼き直し)")
                + " " + distance.ToString("0.00", CultureInfo.InvariantCulture) + "m";

            if (roll)
                text += " ロール追従";

            if (!string.IsNullOrEmpty(frameBone)
                && !string.Equals(frameBone, DefaultFrameBoneName, StringComparison.Ordinal))
                text += " 基準:" + frameBone;

            return text;
        }

        private static string DescribePoseOverride(CameraPoseOverride po)
        {
            bool look, pos;
            if (po.UseBoneLink)
            {
                look = false;
                pos = HasPoseCameraPosition(po);
            }
            else
            {
                look = po.TargetPosition != null && po.TargetPosition.Length >= 3;
                pos = po.CameraDirection != null && po.CameraDirection.Length >= 3;
            }
            string relative = string.Empty;
            if (po.UseBoneLink)
            {
                relative = !po.UseRelativeDirection.HasValue ? "相対:共通"
                    : po.UseRelativeDirection.Value && po.RelativeDirLocal != null
                        && po.RelativeDirLocal.Length >= 3 && po.RelativeDistance > 0.0001f
                    ? DescribeRelativeDirection(po.RelativeFromLookAt, po.RelativeDistance, po.RollWithBone, po.FrameBoneName)
                    : "相対:なし";
            }
            return DescribeModeBone(po.UseBoneLink, po.BoneTarget)
                + " FOV" + po.Fov.ToString("0.#", CultureInfo.InvariantCulture)
                + " " + DescribeTag(look, pos)
                + (string.IsNullOrEmpty(relative) ? string.Empty : "  " + relative);
        }

        private void RemovePoseOverride(CameraPreset preset, string key)
        {
            if (preset == null || preset.PoseOverrides == null || string.IsNullOrEmpty(key))
                return;

            List<CameraPoseOverride> list = new List<CameraPoseOverride>(preset.PoseOverrides);
            int removed = list.RemoveAll(o => o != null && string.Equals(o.Key, key, StringComparison.Ordinal));
            if (removed <= 0)
                return;

            preset.PoseOverrides = list.ToArray();
            if (preset.PoseOverrides.Length == 0)
                preset.UsePoseOverrides = false;

            CommitPresets("pose-remove:" + (preset.Name ?? string.Empty) + ":" + key);
            SetStatus("体位削除: " + key);
        }
    }
}
