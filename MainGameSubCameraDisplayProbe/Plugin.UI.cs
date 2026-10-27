using System.Globalization;
using MainGameUiInputCapture;
using UnityEngine;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private void DrawWindow(int id)
        {
            DrawWindowBody(saveWindowLayout: true);

            UpdateWindowDragState();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
            SaveSettings();
        }

        private void DrawWindowBody(bool saveWindowLayout)
        {
            _lastWindowRect = _windowRect;
            GUILayout.BeginVertical();
            if (saveWindowLayout)
                _windowScroll = GUILayout.BeginScrollView(_windowScroll, false, true, GUILayout.Height(_windowRect.height - 50f));
            else
                _windowScroll = GUILayout.BeginScrollView(_windowScroll, false, true);

            _selectedTab = GUILayout.Toolbar(Mathf.Clamp(_selectedTab, 0, TabLabels.Length - 1), TabLabels);
            GUILayout.Space(6f);

            switch (_selectedTab)
            {
                case 0:
                    DrawPresetTab();
                    break;
                case 1:
                    DrawSaveTab();
                    break;
                case 2:
                    DrawPhotoTab();
                    break;
                case 3:
                    DrawDisplayTab();
                    break;
                default:
                    DrawSettingsTab();
                    break;
            }

            if (!string.IsNullOrWhiteSpace(_statusMessage))
            {
                GUILayout.Space(4f);
                GUILayout.Label(_statusMessage);
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            ApplyPostDrawMutations();
        }

        private bool DrawFoldoutHeader(string label, ref bool open)
        {
            string text = (open ? "▼ " : "▶ ") + label;
            if (GUILayout.Button(text, GUILayout.Height(28f)))
                open = !open;
            return open;
        }

        private void DrawPresetTab()
        {
            DrawLiveCameraPositionUi();
            GUILayout.Space(6f);
            GUILayout.Label("プリセット一覧");
            SubCameraPreset[] presets = _settings.Presets ?? new SubCameraPreset[0];
            if (presets.Length == 0)
                GUILayout.Label("（保存済みなし）");

            for (int i = 0; i < presets.Length; i++)
            {
                SubCameraPreset preset = presets[i];
                if (preset == null)
                    continue;

                bool renaming = !string.IsNullOrEmpty(_renamingPresetName)
                    && string.Equals(_renamingPresetName, preset.Name ?? string.Empty, System.StringComparison.Ordinal);

                GUILayout.BeginHorizontal();
                if (renaming)
                {
                    _renameBuffer = GUILayout.TextField(_renameBuffer ?? string.Empty, GUILayout.ExpandWidth(true));
                    string trimmed = (_renameBuffer ?? string.Empty).Trim();
                    bool canOk = trimmed.Length > 0
                        && !string.Equals(trimmed, preset.Name ?? string.Empty, System.StringComparison.Ordinal)
                        && !HasPresetNameConflict(trimmed, preset);
                    bool prevEnabled = GUI.enabled;
                    GUI.enabled = canOk;
                    if (GUILayout.Button("OK", GUILayout.Width(40f)))
                    {
                        RenamePreset(preset, trimmed);
                        _renamingPresetName = string.Empty;
                    }
                    GUI.enabled = prevEnabled;
                    if (GUILayout.Button("Cancel", GUILayout.Width(60f)))
                        _renamingPresetName = string.Empty;
                }
                else
                {
                    string key = preset.Name ?? string.Empty;
                    bool expanded = _expandedPosePresets.Contains(key);
                    bool quickNext = GUILayout.Toggle(preset.AutoCycleInclude, string.Empty, GUILayout.Width(20f));
                    if (quickNext != preset.AutoCycleInclude)
                    {
                        preset.AutoCycleInclude = quickNext;
                        SaveSettings();
                    }
                    if (GUILayout.Button(expanded ? "▽" : "▷", GUILayout.Width(24f)))
                    {
                        if (expanded)
                            _expandedPosePresets.Remove(key);
                        else
                            _expandedPosePresets.Add(key);
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

                if (!renaming && _expandedPosePresets.Contains(preset.Name ?? string.Empty))
                    DrawPresetDetail(preset);
            }

            GUILayout.Space(6f);
            GUILayout.Label("▶呼び出し  ↻現在体位を保存  ✎改名  x削除");

            GUILayout.Space(6f);
            bool allUseGlobal = AllUseCameraPositionOnAllPresets(out bool anyDataGlobal);
            if (anyDataGlobal)
            {
                bool nextUseGlobal = GUILayout.Toggle(allUseGlobal, "位置を全部使う（全プリセット）");
                if (nextUseGlobal != allUseGlobal)
                    SetAllUseCameraPositionAllPresets(nextUseGlobal);
            }

            DrawAutoCycleUi();
            DrawToggleRow("体位で自動切替", ref _settings.PosePresetAutoApply);
            bool hasPose = TryGetCurrentPoseInfo(out string keyNow, out string displayNow);
            GUILayout.Label("現在体位: " + (hasPose ? displayNow + " (" + keyNow + ")" : "(なし)"));

            GUILayout.Space(4f);
            GUILayout.Label("遷移時間");
            DrawSliderWithText("遷移時間", ref _settings.TransitionSeconds, 0f, 3f, SourceSlider);
            DrawTransitionEasingUi();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("解除"))
            {
                ClearBoneFollow();
                SetStatus("ボーン追従解除");
            }
            if (GUILayout.Button("Reset"))
                MoveCameraInFrontOfPlayer();
            GUILayout.EndHorizontal();
        }

        private void DrawSaveTab()
        {
            string modeLabel = SaveModeLabels[Mathf.Clamp(_settings.SelectedSaveMode, 0, SaveModeLabels.Length - 1)];
            bool hasPose = TryGetCurrentPoseInfo(out string poseKey, out string poseDisplayName);
            string poseInfo = (_settings.SaveCameraPoseOverrides && hasPose)
                ? "  体位「" + poseDisplayName + "」に記録"
                : string.Empty;
            GUILayout.Label("今撮ると → [" + modeLabel + "] FOV"
                + _settings.CameraFieldOfView.ToString("0.#", CultureInfo.InvariantCulture) + poseInfo);

            GUILayout.Space(4f);
            if (DrawSliderWithText("現在FOV", ref _settings.CameraFieldOfView, 10f, 170f, SourceSlider))
                SyncActiveFovFromUi();

            GUILayout.Space(4f);
            GUILayout.Label("保存モード");
            int prevMode = _settings.SelectedSaveMode;
            _settings.SelectedSaveMode = GUILayout.SelectionGrid(
                Mathf.Clamp(_settings.SelectedSaveMode, 0, SaveModeLabels.Length - 1),
                SaveModeLabels,
                SaveModeLabels.Length);
            if (prevMode != _settings.SelectedSaveMode && _settings.SelectedSaveMode == SaveModeNormal)
                ClearBoneFollow();
            if (prevMode != _settings.SelectedSaveMode && _actorLease != 0)
                SaveExternalBoneSelection();

            if (_settings.SelectedSaveMode == SaveModeBoneLink)
            {
                GUILayout.Label("対象ボーン");
                int prevBoneTarget = _settings.SelectedBoneTarget;
                _settings.SelectedBoneTarget = DrawBoneTargetButtons(_settings.SelectedBoneTarget);
                if (_settings.SelectedBoneTarget != prevBoneTarget && _boneFollowActive)
                    RetargetActiveBone(_settings.SelectedBoneTarget);
                if (_settings.SelectedBoneTarget != prevBoneTarget && _actorLease != 0)
                    SaveExternalBoneSelection();

                DrawToggleRow("カメラ位置を使う", ref _settings.SaveBoneCameraPosition);
            }

            DrawToggleRow("体位ごとに保存", ref _settings.SaveCameraPoseOverrides);
            DrawLiveCameraPositionUi();
            DrawActiveBoneFollowUi();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("保存名", GUILayout.Width(54f));
            _presetNameBuffer = GUILayout.TextField(_presetNameBuffer ?? string.Empty, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            string targetName = (_presetNameBuffer ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(targetName))
            {
                bool prevEnabled = GUI.enabled;
                GUI.enabled = false;
                GUILayout.Button("保存できません（名前を入力）");
                GUI.enabled = prevEnabled;
            }
            else
            {
                SubCameraPreset existing = FindPresetByName(targetName);
                string label = existing != null
                    ? "↻ " + targetName + " を上書き保存"
                    : "+ " + targetName + " を新規保存";
                if (GUILayout.Button(label))
                    SaveOrOverwritePreset();
            }

            GUILayout.Space(6f);
            GUILayout.Label("アクティブプリセットへの体位登録");
            DrawPoseRegisterButton();

            if (GUILayout.Button(_captureTabPoseExpanded ? "▽ このプリセットの中身を隠す" : "▷ このプリセットの中身を見る"))
                _captureTabPoseExpanded = !_captureTabPoseExpanded;
            if (_captureTabPoseExpanded)
            {
                SubCameraPreset active = FindPresetByName(_activeCameraPresetName);
                if (active != null)
                    DrawPresetDetail(active);
                else
                    GUILayout.Label("（プリセット未選択）");
            }
        }

        private void DrawPhotoTab()
        {
            GUILayout.Label("【撮影】");
            if (GUILayout.Button("写真保存テスト", GUILayout.Height(24f)))
                CaptureSubCameraPhoto("ui-test");

            GUILayout.Space(8f);
            DrawVideoRecordingUi();
        }

        private void DrawDisplayTab()
        {
            DrawDisplaySection();
        }

        private void DrawSettingsTab()
        {
            bool prevEnabled = _settings.Enabled;
            DrawToggleRow("プラグイン全体を有効", ref _settings.Enabled);
            if (prevEnabled != _settings.Enabled)
            {
                if (_cfgEnabled != null)
                    _cfgEnabled.Value = _settings.Enabled;
                else
                    ApplyRuntimeState();
            }
            DrawToggleRow("ログ出力", ref _settings.LoggingEnabled);
            DrawToggleRow("VoiceFaceEventサブカメラトリガー", ref _settings.VoiceFaceEventSubCameraTriggerEnabled);

            GUILayout.Label("【ステータス】");
            GUILayout.Label("Probe: " + (_rootObject != null ? "生成済み" : "未生成"));
            GUILayout.Label("SubCamera: " + (_subCamera != null ? _subCamera.name : "none"));
            GUILayout.Label("RenderTexture: " + (_renderTexture != null ? (_renderTexture.width + "x" + _renderTexture.height) : "none"));
            GUILayout.Label("FOV: " + _settings.CameraFieldOfView.ToString("0.##", CultureInfo.InvariantCulture));
            GUILayout.Label("UI Capture: " + (UiInputCaptureApi.IsAvailable ? UiInputCaptureApi.GetStateSummary() : "unavailable"));

            GUILayout.Space(8f);
            GUILayout.Label("【操作】");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("両方破棄", GUILayout.Height(28f)))
                DestroyProbe();
            if (GUILayout.Button("眼の前に移動", GUILayout.Height(28f)) && _rootObject != null)
                MoveProbeInFrontOfPlayer(initialCreate: false);
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            DrawCameraSettingsSection();

            GUILayout.Space(8f);
            GUILayout.Label("【その他】");
            DrawSliderWithText("Front Dist", ref _settings.SpawnDistance, 0.1f, 2f, SourceSlider);
            DrawSliderWithText("Gizmo Size", ref _settings.GizmoSizeMultiplier, 0.2f, 4f, SourceSlider);
        }

        private void ApplyPostDrawMutations()
        {
            if (_pendingPresetRemove >= 0)
            {
                int index = _pendingPresetRemove;
                _pendingPresetRemove = -1;
                RemovePresetAt(index);
            }
            if (_pendingPoseRemovePreset != null)
            {
                SubCameraPreset preset = _pendingPoseRemovePreset;
                string key = _pendingPoseRemoveKey;
                _pendingPoseRemovePreset = null;
                _pendingPoseRemoveKey = null;
                RemovePoseOverride(preset, key);
            }
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
            if (!TryGetCurrentPoseInfo(out string poseKey, out string poseDisplayName))
            {
                GUI.enabled = false;
                GUILayout.Button("体位を登録できません（体位情報なし）");
                GUI.enabled = prev;
                return;
            }

            SubCameraPreset active = FindPresetByName(_activeCameraPresetName);
            bool registered = active != null && FindCameraPoseOverride(active, poseKey) != null;
            string label = registered ? "↻ " + poseDisplayName + " を更新" : "+ " + poseDisplayName + " を追加";
            if (GUILayout.Button(label) && active != null)
                OverwritePresetPoseAware(active);
        }

        private void DrawPresetDetail(SubCameraPreset preset)
        {
            if (preset == null)
                return;

            GUILayout.BeginVertical(GUI.skin.box);
            DrawPresetLiveCameraControls(preset);
            GUILayout.Space(4f);

            bool allOn = AllUseCameraPositionOn(preset, out bool anyData);
            if (anyData)
            {
                bool nextAll = GUILayout.Toggle(allOn, "位置を全部使う");
                if (nextAll != allOn)
                    SetAllUseCameraPosition(preset, nextAll);
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("基底  " + DescribePresetBase(preset), GUILayout.ExpandWidth(true));
            if (preset.UseBoneLink && PresetHasStoredOffset(preset))
            {
                bool useNext = GUILayout.Toggle(preset.SaveCameraPosition, "位置", GUILayout.Width(54f));
                if (useNext != preset.SaveCameraPosition)
                {
                    preset.SaveCameraPosition = useNext;
                    SaveSettings();
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

                    bool current = TryGetCurrentPoseInfo(out string currentKey, out _)
                        && string.Equals(po.Key, currentKey, System.StringComparison.Ordinal);
                    string disp = string.IsNullOrEmpty(po.DisplayName) ? po.Key : po.DisplayName;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label((current ? "▶ " : "　") + disp + "  " + DescribePoseOverride(po), GUILayout.ExpandWidth(true));
                    if (po.UseBoneLink && PoseHasStoredOffset(po))
                    {
                        bool useNext = GUILayout.Toggle(po.SaveCameraPosition, "位置", GUILayout.Width(54f));
                        if (useNext != po.SaveCameraPosition)
                        {
                            po.SaveCameraPosition = useNext;
                            SaveSettings();
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
        }

        private void DrawPresetLiveCameraControls(SubCameraPreset preset)
        {
            GUILayout.BeginHorizontal();
            DrawPresetModeButton("通常", false, preset);
            DrawPresetModeButton("ボーン", true, preset);
            GUILayout.EndHorizontal();

            bool activePreset = IsSelectedPreset(preset);
            if (activePreset && (_actorLease != 0 ? _externalPreset != null && _externalPreset.UseBoneLink : _boneFollowActive))
            {
                GUILayout.BeginHorizontal();
                DrawPresetBoneButton("頭", 0, preset);
                DrawPresetBoneButton("胸", 1, preset);
                DrawPresetBoneButton("腰", 2, preset);
                GUILayout.EndHorizontal();

                DrawActiveBoneFollowUi();
            }

            if (GUILayout.Button("↻ 現在体位を保存"))
                OverwritePresetPoseAware(preset);
        }

        private void DrawPresetModeButton(string label, bool boneMode, SubCameraPreset preset)
        {
            bool activePreset = IsSelectedPreset(preset);
            bool linked = _actorLease != 0 ? _externalPreset != null && _externalPreset.UseBoneLink : _boneFollowActive;
            bool active = activePreset && (boneMode ? linked : !linked);
            Color prev = GUI.backgroundColor;
            if (active)
                GUI.backgroundColor = Color.red;

            bool clicked = GUILayout.Button(label, GUILayout.Width(64f));
            GUI.backgroundColor = prev;
            if (!clicked)
                return;

            if (boneMode)
            {
                int target = activePreset && _boneFollowActive && _activeBoneTarget >= 0
                    ? _activeBoneTarget
                    : Mathf.Clamp(preset != null && preset.UseBoneLink ? preset.BoneTarget : _settings.SelectedBoneTarget, 0, BoneTargetLabels.Length - 1);
                ActivatePresetBoneLinkLive(preset, target);
            }
            else
            {
                ActivatePresetNormalLive(preset);
            }
        }

        private void DrawPresetBoneButton(string label, int target, SubCameraPreset preset)
        {
            Color prev = GUI.backgroundColor;
            if (IsSelectedPreset(preset) && (_actorLease != 0
                ? _externalPreset != null && _externalPreset.UseBoneLink && _externalPreset.BoneTarget == target
                : _boneFollowActive && _activeBoneTarget == target))
                GUI.backgroundColor = Color.red;

            bool clicked = GUILayout.Button(label, GUILayout.Width(52f));
            GUI.backgroundColor = prev;
            if (clicked)
                ActivatePresetBoneLinkLive(preset, target);
        }

        private static int DrawBoneTargetButtons(int selected)
        {
            selected = Mathf.Clamp(selected, 0, BoneTargetLabels.Length - 1);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < BoneTargetLabels.Length; i++)
            {
                Color previous = GUI.backgroundColor;
                if (i == selected) GUI.backgroundColor = Color.red;
                bool clicked = GUILayout.Button(BoneTargetLabels[i]);
                GUI.backgroundColor = previous;
                if (clicked) selected = i;
            }
            GUILayout.EndHorizontal();
            return selected;
        }

        private void SaveExternalBoneSelection()
        {
            SubCameraPreset selected = FindPresetByName(_activeCameraPresetName);
            if (selected != null)
                SaveExternalCameraPreset(selected);
        }

        private void DrawCameraSettingsSection()
        {
            GUILayout.Label("【カメラ】");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("カメラだけ破棄", GUILayout.Height(28f)))
                DestroyCameraOnly();
            if (GUILayout.Button("カメラを眼の前", GUILayout.Height(28f)))
                MoveCameraInFrontOfPlayer();
            GUILayout.EndHorizontal();

            DrawBeatFovToggle();
            DrawSliderWithText("拍ズーム倍率", ref _settings.BeatFovZoomMultiplier, 1f, 3f, SourceSlider);
            DrawToggleRow("キャラのみ表示（背景非表示）", ref _settings.CharactersOnlyMode);
            DrawRenderResolutionUi();
            GUILayout.Label("Filter: " + _settings.RenderFilterMode);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Point", GUILayout.Height(24f)))
                _settings.RenderFilterMode = "Point";
            if (GUILayout.Button("Bilinear", GUILayout.Height(24f)))
                _settings.RenderFilterMode = "Bilinear";
            if (GUILayout.Button("Trilinear", GUILayout.Height(24f)))
                _settings.RenderFilterMode = "Trilinear";
            GUILayout.EndHorizontal();
            if (GUILayout.Button("現在カメラをサブカメラにする", GUILayout.Height(24f)))
                CopyCurrentCameraToSubCamera();

            DrawToggleRow("カメラギズモ表示", ref _settings.CameraGizmoVisible);
            DrawSliderWithText("Near", ref _settings.CameraNearClip, 0.01f, 1f, SourceSlider);
            DrawSliderWithText("Far", ref _settings.CameraFarClip, 10f, 1000f, SourceSlider);
            bool cameraPosChanged = DrawVector3SlidersZoom("Camera Pos", ref _settings.CameraPosX, ref _settings.CameraPosY, ref _settings.CameraPosZ, -20f, 20f);
            bool cameraRotChanged = DrawVector3SlidersZoom("Camera Rot", ref _settings.CameraRotX, ref _settings.CameraRotY, ref _settings.CameraRotZ, 0f, 360f);
            if (cameraPosChanged || cameraRotChanged)
                SyncCameraControlSliders(cameraPosChanged, cameraRotChanged);

            GUILayout.Space(8f);
            GUILayout.Label("【VR / 掴み操作】");
            DrawSliderWithText("VR Grip Dist", ref _settings.VrGripStartDistance, 0.05f, 1.5f, SourceSlider);
            DrawToggleRow("カメラ掴み中にカメラ付属プレビュー表示", ref _settings.ShowHandPreviewWhileGrabbingCamera);
            DrawToggleRow("カメラ掴み中スティック上下でFOV", ref _settings.VrFovStickEnabled);
            DrawSliderWithText("VR FOV Speed", ref _settings.VrFovStickSpeed, 1f, 120f, SourceSlider);
            DrawSliderWithText("VR FOV Dead", ref _settings.VrFovStickDeadzone, 0f, 0.95f, SourceSlider);
            DrawToggleRow("カメラ掴み中トリガーで写真保存", ref _settings.PhotoCaptureOnTrigger);

            GUILayout.Space(8f);
            GUILayout.Label("【手元プレビュー位置】");
            DrawSliderWithText("カメラ見た目サイズ", ref _settings.CameraVisualScale, 0.1f, 3f, SourceSlider);
            DrawVector3SlidersZoom("Preview Pos", ref _settings.HandPreviewOffsetX, ref _settings.HandPreviewOffsetY, ref _settings.HandPreviewOffsetZ, -1f, 1f);
        }

        private void DrawCameraSection()
        {
            // ───── カメラ
            GUILayout.Label("【カメラ】");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("カメラだけ破棄", GUILayout.Height(28f)))
                DestroyCameraOnly();
            if (GUILayout.Button("カメラを眼の前", GUILayout.Height(28f)))
                MoveCameraInFrontOfPlayer();
            GUILayout.EndHorizontal();
            DrawBeatFovToggle();
            DrawSliderWithText("拍ズーム倍率", ref _settings.BeatFovZoomMultiplier, 1f, 3f, SourceSlider);
            DrawToggleRow("キャラのみ表示（背景非表示）", ref _settings.CharactersOnlyMode);
            DrawRenderResolutionUi();
            GUILayout.Label("Filter: " + _settings.RenderFilterMode);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Point", GUILayout.Height(24f)))
                _settings.RenderFilterMode = "Point";
            if (GUILayout.Button("Bilinear", GUILayout.Height(24f)))
                _settings.RenderFilterMode = "Bilinear";
            if (GUILayout.Button("Trilinear", GUILayout.Height(24f)))
                _settings.RenderFilterMode = "Trilinear";
            GUILayout.EndHorizontal();
            if (GUILayout.Button("現在カメラをサブカメラにする", GUILayout.Height(24f)))
                CopyCurrentCameraToSubCamera();

            DrawToggleRow("カメラギズモ表示", ref _settings.CameraGizmoVisible);
            if (DrawSliderWithText("FOV", ref _settings.CameraFieldOfView, 10f, 170f, SourceSlider))
                SyncActiveFovFromUi();
            DrawSliderWithText("Near", ref _settings.CameraNearClip, 0.01f, 1f, SourceSlider);
            DrawSliderWithText("Far", ref _settings.CameraFarClip, 10f, 1000f, SourceSlider);

            bool cameraPosChanged = DrawVector3SlidersZoom("Camera Pos", ref _settings.CameraPosX, ref _settings.CameraPosY, ref _settings.CameraPosZ, -20f, 20f);
            bool cameraRotChanged = DrawVector3SlidersZoom("Camera Rot", ref _settings.CameraRotX, ref _settings.CameraRotY, ref _settings.CameraRotZ, 0f, 360f);
            if (cameraPosChanged || cameraRotChanged)
                SyncCameraControlSliders(cameraPosChanged, cameraRotChanged);

            // ボーン追従中以外も常に場所を確保する（追従ON/OFFで下の要素が跳ねないように）
            DrawActiveBoneFollowUi();

            // ───── 名前付きプリセット / ボーン追従
            GUILayout.Space(8f);
            DrawPresetUi();

            // ───── 遷移移動
            GUILayout.Space(8f);
            GUILayout.Label("【遷移移動】");
            DrawSliderWithText("遷移時間", ref _settings.TransitionSeconds, 0f, 3f, SourceSlider);
            DrawTransitionEasingUi();

            // ───── VR / 掴み操作
            GUILayout.Space(8f);
            GUILayout.Label("【VR / 掴み操作】");
            DrawSliderWithText("VR Grip Dist", ref _settings.VrGripStartDistance, 0.05f, 1.5f, SourceSlider);
            DrawToggleRow("カメラ掴み中にカメラ付属プレビュー表示", ref _settings.ShowHandPreviewWhileGrabbingCamera);
            DrawToggleRow("カメラ掴み中スティック上下でFOV", ref _settings.VrFovStickEnabled);
            DrawSliderWithText("VR FOV Speed", ref _settings.VrFovStickSpeed, 1f, 120f, SourceSlider);
            DrawSliderWithText("VR FOV Dead", ref _settings.VrFovStickDeadzone, 0f, 0.95f, SourceSlider);
            DrawToggleRow("カメラ掴み中トリガーで写真保存", ref _settings.PhotoCaptureOnTrigger);

            // ───── 写真
            GUILayout.Space(8f);
            GUILayout.Label("【写真】");
            if (GUILayout.Button("写真保存テスト", GUILayout.Height(24f)))
                CaptureSubCameraPhoto("ui-test");

            // ───── 動画録画
            GUILayout.Space(8f);
            DrawVideoRecordingUi();

            // ───── 手元プレビュー位置（カメラ掴み中の小型ビューファインダのカメラ起点オフセット）
            GUILayout.Space(8f);
            GUILayout.Label("【手元プレビュー位置】");
            DrawSliderWithText("カメラ見た目サイズ", ref _settings.CameraVisualScale, 0.1f, 3f, SourceSlider);
            DrawVector3SlidersZoom("Preview Pos", ref _settings.HandPreviewOffsetX, ref _settings.HandPreviewOffsetY, ref _settings.HandPreviewOffsetZ, -1f, 1f);
        }

        private void DrawDisplaySection()
        {
            // ───── ディスプレイ
            GUILayout.Label("【ディスプレイ】");
            if (DrawSliderWithText("映像の明るさ", ref _settings.DisplayBrightness, 0f, 2f, SourceSlider)) {
                ApplyDisplayBrightness(); SaveSettings();
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ディスプレイを表示", GUILayout.Height(28f)))
                EnsureProbe();
            if (GUILayout.Button("ディスプレイだけ破棄", GUILayout.Height(28f)))
                DestroyDisplayOnly();
            if (GUILayout.Button("ディスプレイを眼の前", GUILayout.Height(28f)))
                MoveDisplayInFrontOfPlayer();
            GUILayout.EndHorizontal();
            DrawToggleRow("Overlay Camera", ref _settings.UseDisplayOverlayCamera);
            DrawSliderWithText("Display Layer", ref _displayLayerFloat, 0f, 30f, SourceSlider);
            _settings.DisplayLayer = Mathf.Clamp(Mathf.RoundToInt(_displayLayerFloat), 0, 30);
            _settings.DisplayWidth = SettingsStore.CalculateDisplayWidth(_settings);
            GUILayout.Label("Width: Auto " + _settings.DisplayWidth.ToString("0.##", CultureInfo.InvariantCulture)
                + " (Render " + _settings.RenderWidth + "x" + _settings.RenderHeight + ")");
            DrawSliderWithText("Height", ref _settings.DisplayHeight, 0.1f, 5f, SourceSlider);
            DrawToggleRow("ディスプレイギズモ表示", ref _settings.DisplayGizmoVisible);
            DrawVector3SlidersZoom("Display Pos", ref _settings.DisplayPosX, ref _settings.DisplayPosY, ref _settings.DisplayPosZ, -20f, 20f);
            DrawVector3SlidersZoom("Display Rot", ref _settings.DisplayRotX, ref _settings.DisplayRotY, ref _settings.DisplayRotZ, 0f, 360f);

            // ───── ディスプレイプリセット
            GUILayout.Space(8f);
            DrawDisplayPresetUi();
        }

        private void DrawTransitionEasingUi()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("動き方", GUILayout.Width(90f));
            int current = Mathf.Clamp(_settings.TransitionEasing, 0, TransitionEasingLabels.Length - 1);
            for (int i = 0; i < TransitionEasingLabels.Length; i++)
            {
                Color prevColor = GUI.backgroundColor;
                if (i == current)
                    GUI.backgroundColor = Color.cyan;
                if (GUILayout.Button(TransitionEasingLabels[i], GUILayout.Height(22f)))
                    _settings.TransitionEasing = i;
                GUI.backgroundColor = prevColor;
            }
            GUILayout.EndHorizontal();
        }

        private void DrawAutoCycleUi()
        {
            GUILayout.Label("自動切替");
            DrawToggleRow("自動切替を有効", ref _settings.AutoCycleEnabled);
            DrawSliderWithText("切替間隔(秒)", ref _settings.AutoCycleIntervalSeconds, 1f, 60f, SourceSlider);
            DrawSliderWithText("横槍復帰(秒)", ref _settings.AutoCyclePauseAfterExternalSeconds, 0f, 120f, SourceSlider);
            DrawToggleRow("ランダム順", ref _settings.AutoCycleRandomOrder);
        }

        private void DrawPresetUi()
        {
            GUILayout.Label("【名前付きプリセット / ボーン追従】");
            GUILayout.BeginHorizontal();
            GUILayout.Label("保存名", GUILayout.Width(54f));
            _presetNameBuffer = GUILayout.TextField(_presetNameBuffer ?? string.Empty, GUILayout.Width(220f));
            if (GUILayout.Button("保存", GUILayout.Height(24f)))
                SaveCurrentPreset();
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && !string.IsNullOrWhiteSpace(_presetNameBuffer);
            if (GUILayout.Button("上書き", GUILayout.Height(24f)))
                OverwriteActivePreset();
            GUI.enabled = prevEnabled;
            if (GUILayout.Button("追従解除", GUILayout.Height(24f)))
            {
                ClearBoneFollow();
                SetStatus("ボーン追従解除");
            }
            GUILayout.EndHorizontal();

            int previousSaveMode = _settings.SelectedSaveMode;
            _settings.SelectedSaveMode = GUILayout.SelectionGrid(
                Mathf.Clamp(_settings.SelectedSaveMode, 0, SaveModeLabels.Length - 1),
                SaveModeLabels,
                SaveModeLabels.Length);
            if (previousSaveMode != _settings.SelectedSaveMode && _actorLease != 0)
                SaveExternalBoneSelection();

            if (_settings.SelectedSaveMode == SaveModeBoneLink)
            {
                GUILayout.Label("対象ボーン");
                int prevBoneTarget = _settings.SelectedBoneTarget;
                _settings.SelectedBoneTarget = DrawBoneTargetButtons(_settings.SelectedBoneTarget);
                // 追従中に対象ボーンを変えたら、追従先も切り替えてカメラを移動
                if (_settings.SelectedBoneTarget != prevBoneTarget && _boneFollowActive)
                    RetargetActiveBone(_settings.SelectedBoneTarget);
                if (_settings.SelectedBoneTarget != prevBoneTarget && _actorLease != 0)
                    SaveExternalBoneSelection();
                DrawToggleRow("カメラ位置も保存", ref _settings.SaveBoneCameraPosition);
            }

            DrawToggleRow("体位ごとに上書き保存", ref _settings.SaveCameraPoseOverrides);

            string state = _boneFollowActive
                ? "ボーン追従 " + _activeBonePresetName
                : (string.IsNullOrWhiteSpace(_activeCameraPresetName) ? "通常" : "通常 " + _activeCameraPresetName);
            GUILayout.Label("状態: " + state);

            SubCameraPreset[] presets = _settings.Presets ?? new SubCameraPreset[0];
            for (int i = 0; i < presets.Length; i++)
            {
                SubCameraPreset preset = presets[i];
                if (preset == null)
                    continue;

                bool hasOverrides = preset.UsePoseOverrides && preset.PoseOverrides != null && preset.PoseOverrides.Length > 0;
                string presetKey = preset.Name ?? string.Empty;
                bool expanded = hasOverrides && _expandedPosePresets.Contains(presetKey);

                GUILayout.BeginHorizontal();
                bool nextInclude = GUILayout.Toggle(preset.AutoCycleInclude, GUIContent.none, GUILayout.Width(20f));
                if (nextInclude != preset.AutoCycleInclude)
                {
                    preset.AutoCycleInclude = nextInclude;
                    SaveSettings();
                }
                if (hasOverrides)
                {
                    if (GUILayout.Button(expanded ? "▼" : "▶", GUILayout.Width(24f), GUILayout.Height(24f)))
                    {
                        if (expanded)
                            _expandedPosePresets.Remove(presetKey);
                        else
                            _expandedPosePresets.Add(presetKey);
                        expanded = !expanded;
                    }
                }
                Color prevColor = GUI.backgroundColor;
                if (IsActiveBonePreset(preset))
                    GUI.backgroundColor = Color.red;
                if (GUILayout.Button(GetPresetDisplayName(preset, i), GUILayout.Height(24f)))
                    LoadPreset(preset);
                GUI.backgroundColor = prevColor;
                if (GUILayout.Button("x", GUILayout.Width(28f), GUILayout.Height(24f)))
                {
                    RemovePresetAt(i);
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();

                // 展開時：このプリセットに保存された体位一覧（★=今いる体位、[骨]、個別削除）
                if (expanded)
                {
                    TryGetCurrentPoseInfo(out string curPoseKey, out _);
                    CameraPoseOverride[] overrides = preset.PoseOverrides;
                    for (int j = 0; j < overrides.Length; j++)
                    {
                        CameraPoseOverride o = overrides[j];
                        if (o == null)
                            continue;

                        GUILayout.BeginHorizontal();
                        GUILayout.Space(28f);
                        bool isCurrent = !string.IsNullOrEmpty(curPoseKey)
                            && string.Equals(o.Key, curPoseKey, System.StringComparison.Ordinal);
                        string boneTag = string.IsNullOrEmpty(o.BoneName)
                            ? string.Empty
                            : " [" + BoneTargetLabels[Mathf.Clamp(o.BoneTarget, 0, BoneTargetLabels.Length - 1)] + "]";
                        string poseName = string.IsNullOrWhiteSpace(o.DisplayName) ? o.Key : o.DisplayName;
                        GUILayout.Label((isCurrent ? "★ " : "・") + poseName + boneTag);
                        if (GUILayout.Button("削除", GUILayout.Width(50f), GUILayout.Height(20f)))
                        {
                            RemovePoseOverrideAt(preset, j);
                            GUILayout.EndHorizontal();
                            break;
                        }
                        GUILayout.EndHorizontal();
                    }
                }
            }

            GUILayout.Space(4f);
            DrawAutoCycleUi();

            if (!string.IsNullOrWhiteSpace(_statusMessage))
                GUILayout.Label(_statusMessage);
        }

        private void DrawLiveCameraPositionUi()
        {
            GUILayout.Label("カメラ座標（操作すると即時反映）");
            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && _subCamera != null;
            bool changed = DrawVector3SlidersZoom("Camera Pos", ref _settings.CameraPosX, ref _settings.CameraPosY, ref _settings.CameraPosZ, -20f, 20f);
            GUI.enabled = previousEnabled;
            if (changed) SyncCameraControlSliders(true, false);
        }

        private void DrawActiveBoneFollowUi()
        {
            bool external = _actorLease != 0;
            bool editable = external ? ExternalLookEditable : _boneFollowActive;
            Vector3 currentOffset = external ? _externalLookOffset : _activeLookAtOffsetLocal;
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && editable;

            GUILayout.Label(editable ? "注視点オフセット" : "注視点オフセット（ボーン追従中のみ）");
            float x = currentOffset.x;
            float y = currentOffset.y;
            float z = currentOffset.z;

            DrawSliderWithTextZoom("Look X", ref x, -2f, 2f, SourceSlider);
            DrawSliderWithTextZoom("Look Y", ref y, -2f, 2f, SourceSlider);
            DrawSliderWithTextZoom("Look Z", ref z, -2f, 2f, SourceSlider);

            GUI.enabled = prevEnabled;

            if (!editable)
                return;

            Vector3 next = new Vector3(Round2(x), Round2(y), Round2(z));
            if ((currentOffset - next).sqrMagnitude <= 0.000001f)
                return;

            if (external) { SetExternalLookOffset(next); return; }
            _activeLookAtOffsetLocal = next;
            UpdateBoneFollow();
            SaveActiveBonePresetOffsets();
            SetStatus("注視点オフセット更新");
        }

        private void DrawPosePresetUi()
        {
            GUILayout.Label("【体位連動】");
            DrawToggleRow("体位ごとに自動適用", ref _settings.PosePresetAutoApply);

            bool hasPose = TryGetCurrentPoseInfo(out string key, out string displayName);
            string label = hasPose ? (displayName + " (" + key + ")") : "未取得 (Hシーン外)";
            GUILayout.Label("現在の体位: " + label);

            int cameraTagged = CountPoseAwareCameraPresets();
            int displayTagged = CountPoseAwareDisplayPresets();
            GUILayout.Label("体位属性: カメラ " + cameraTagged + "件 / ディスプレイ " + displayTagged + "件");
        }

        private void DrawDisplayPresetUi()
        {
            GUILayout.Label("【ディスプレイプリセット】");
            GUILayout.BeginHorizontal();
            GUILayout.Label("保存名", GUILayout.Width(54f));
            _displayPresetNameBuffer = GUILayout.TextField(_displayPresetNameBuffer ?? string.Empty, GUILayout.Width(220f));
            if (GUILayout.Button("保存", GUILayout.Height(24f)))
                SaveCurrentDisplayPreset();
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && !string.IsNullOrWhiteSpace(_displayPresetNameBuffer);
            if (GUILayout.Button("上書き", GUILayout.Height(24f)))
                OverwriteActiveDisplayPreset();
            GUI.enabled = prevEnabled;
            GUILayout.EndHorizontal();
            DrawToggleRow("体位ごとに上書き保存", ref _settings.SaveDisplayPoseOverrides);

            DisplayPreset[] presets = _settings.DisplayPresets ?? new DisplayPreset[0];
            for (int i = 0; i < presets.Length; i++)
            {
                DisplayPreset preset = presets[i];
                if (preset == null)
                    continue;

                GUILayout.BeginHorizontal();
                Color prevColor = GUI.backgroundColor;
                if (IsActiveDisplayPreset(preset))
                    GUI.backgroundColor = Color.red;
                if (GUILayout.Button(GetDisplayPresetDisplayName(preset, i), GUILayout.Height(24f)))
                    LoadDisplayPreset(preset);
                GUI.backgroundColor = prevColor;
                if (GUILayout.Button("x", GUILayout.Width(28f), GUILayout.Height(24f)))
                {
                    RemoveDisplayPresetAt(i);
                    GUILayout.EndHorizontal();
                    break;
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawToggleRow(string label, ref bool value)
        {
            bool next = GUILayout.Toggle(value, label);
            if (next != value)
            {
                value = next;
            }
        }

        private bool DrawSliderWithText(string label, ref float value, float min, float max, string sourceKey)
        {
            string key = "slider." + sourceKey + "." + label;
            bool changed = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90f));
            float before = value;
            float sliderValue = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(260f));
            bool sliderChanged = Mathf.Abs(sliderValue - before) > 0.0001f;
            if (sliderChanged)
            {
                value = Round2(sliderValue);
                SetNumericBuffer(key, value);
                _sliderDragging = Input.GetMouseButton(0);
                changed = true;
            }

            string raw = DrawNamedNumericField(key, value, 70f);
            GUILayout.EndHorizontal();

            if (!sliderChanged && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                parsed = Mathf.Clamp(parsed, min, max);
                if (Mathf.Abs(parsed - value) > 0.0001f)
                {
                    value = Round2(parsed);
                    changed = true;
                }
            }

            return changed;
        }

        private bool DrawSliderWithTextZoom(string label, ref float value, float min, float max, string sourceKey)
        {
            string key = "slider." + sourceKey + "." + label;
            string zoomKey = "zoom." + key;
            if (!_sliderZoom.TryGetValue(zoomKey, out float zoom))
                zoom = 1f;

            float sliderMin = min;
            float sliderMax = max;
            if (zoom < 1f)
            {
                float window = (max - min) * zoom;
                sliderMin = value - window * 0.5f;
                sliderMax = value + window * 0.5f;
                if (sliderMin < min)
                {
                    sliderMin = min;
                    sliderMax = Mathf.Min(max, sliderMin + window);
                }
                else if (sliderMax > max)
                {
                    sliderMax = max;
                    sliderMin = Mathf.Max(min, sliderMax - window);
                }
            }

            bool changed = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90f));
            float before = value;
            float sliderValue = GUILayout.HorizontalSlider(value, sliderMin, sliderMax, GUILayout.Width(200f));
            bool sliderChanged = Mathf.Abs(sliderValue - before) > 0.0001f;
            if (sliderChanged)
            {
                value = Round2(Mathf.Clamp(sliderValue, min, max));
                SetNumericBuffer(key, value);
                _sliderDragging = Input.GetMouseButton(0);
                changed = true;
            }

            string raw = DrawNamedNumericField(key, value, 60f);

            if (DrawZoomButton("全", zoom, 1f))
                _sliderZoom[zoomKey] = 1f;
            if (DrawZoomButton("細", zoom, 0.1f))
                _sliderZoom[zoomKey] = 0.1f;
            if (DrawZoomButton("微", zoom, 0.01f))
                _sliderZoom[zoomKey] = 0.01f;
            GUILayout.EndHorizontal();

            if (!sliderChanged && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
            {
                parsed = Mathf.Clamp(parsed, min, max);
                if (Mathf.Abs(parsed - value) > 0.0001f)
                {
                    value = Round2(parsed);
                    changed = true;
                }
            }

            return changed;
        }

        private static bool DrawZoomButton(string label, float currentZoom, float targetZoom)
        {
            Color prev = GUI.backgroundColor;
            if (Mathf.Approximately(currentZoom, targetZoom))
                GUI.backgroundColor = Color.cyan;
            bool clicked = GUILayout.Button(label, GUILayout.Width(28f), GUILayout.Height(20f));
            GUI.backgroundColor = prev;
            return clicked;
        }

        private void DrawVector3Fields(string label, ref float x, ref float y, ref float z)
        {
            GUILayout.Label(label);
            GUILayout.BeginHorizontal();
            x = DrawFloatField(label + ".X", "X", x);
            y = DrawFloatField(label + ".Y", "Y", y);
            z = DrawFloatField(label + ".Z", "Z", z);
            GUILayout.EndHorizontal();
        }

        private bool DrawVector3Sliders(string label, ref float x, ref float y, ref float z, float min, float max)
        {
            GUILayout.Label(label);
            bool changed = false;
            changed |= DrawSliderWithText(label + " X", ref x, min, max, SourceSlider);
            changed |= DrawSliderWithText(label + " Y", ref y, min, max, SourceSlider);
            changed |= DrawSliderWithText(label + " Z", ref z, min, max, SourceSlider);
            return changed;
        }

        private bool DrawVector3SlidersZoom(string label, ref float x, ref float y, ref float z, float min, float max)
        {
            GUILayout.Label(label);
            bool changed = false;
            changed |= DrawSliderWithTextZoom(label + " X", ref x, min, max, SourceSlider);
            changed |= DrawSliderWithTextZoom(label + " Y", ref y, min, max, SourceSlider);
            changed |= DrawSliderWithTextZoom(label + " Z", ref z, min, max, SourceSlider);
            return changed;
        }

        private float DrawFloatField(string key, string label, float value)
        {
            GUILayout.Label(label, GUILayout.Width(14f));
            string raw = DrawNamedNumericField("float." + key, value, 60f);
            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                return Round2(parsed);
            return value;
        }

        private string DrawNamedNumericField(string key, float value, float width)
        {
            string controlName = "MainGameSubCameraDisplayProbe." + key;
            bool focused = GUI.GetNameOfFocusedControl() == controlName;
            if (!_numericFieldBuffers.TryGetValue(key, out string text) || !focused)
            {
                text = value.ToString("0.##", CultureInfo.InvariantCulture);
                _numericFieldBuffers[key] = text;
            }

            GUI.SetNextControlName(controlName);
            string next = GUILayout.TextField(text, GUILayout.Width(width));
            _numericFieldBuffers[key] = next;
            return next;
        }

        private void SetNumericBuffer(string key, float value)
        {
            _numericFieldBuffers[key] = value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void UpdateWindowDragState()
        {
            Rect dragArea = new Rect(0f, 0f, 10000f, 24f);
            Event current = Event.current;
            if (current == null)
                return;

            if (current.type == EventType.MouseDown && dragArea.Contains(current.mousePosition))
            {
                _windowDragging = true;
            }
            else if (current.type == EventType.MouseUp)
            {
                if (_windowDragging)
                    _windowDragging = false;
                _sliderDragging = false;
            }
        }

        private void UpdateInputCapture()
        {
            bool visible = _settings.UiVisible && !IsHostedByUnifiedUi();
            UpdateIdleCursorUnlock(visible);
            bool mouseInWindow = visible && IsMouseInGuiRect(_lastWindowRect);
            bool mouseDown = Input.GetMouseButton(0);
            bool sliderActive = visible && mouseDown && GUIUtility.hotControl != 0 && mouseInWindow;
            bool headerActive = visible && mouseDown && IsMouseInGuiRect(new Rect(_lastWindowRect.x, _lastWindowRect.y, _lastWindowRect.width, 24f));
            bool windowActive = visible && (mouseInWindow || sliderActive || headerActive || _windowDragging);

            UiInputCaptureApi.Sync(InputCaptureOwnerKey, SourceWindow, windowActive);
            UiInputCaptureApi.Sync(InputCaptureOwnerKey, SourceSlider, visible && (sliderActive || _sliderDragging));
            UiInputCaptureApi.Sync(InputCaptureOwnerKey, SourceGizmo, _gizmoDragging);

            _sliderDragging = sliderActive;
        }

        private void UpdateIdleCursorUnlock(bool? standaloneVisible = null)
        {
            bool visible = standaloneVisible ?? (_settings.UiVisible && !IsHostedByUnifiedUi());
            UiInputCaptureApi.SetIdleCursorUnlock(InputCaptureOwnerKey, visible);
        }

        private void ReleaseInputCapture()
        {
            UiInputCaptureApi.SetIdleCursorUnlock(InputCaptureOwnerKey, false);
            UiInputCaptureApi.EndOwner(InputCaptureOwnerKey);
        }

        private void ResetInputCaptureState()
        {
            _windowDragging = false;
            _sliderDragging = false;
            _gizmoDragging = false;
        }

        private static bool IsMouseInGuiRect(Rect guiRect)
        {
            if (guiRect.width <= 0f || guiRect.height <= 0f)
                return false;

            Vector3 mouse = Input.mousePosition;
            Vector2 guiMouse = new Vector2(mouse.x, Screen.height - mouse.y);
            return guiRect.Contains(guiMouse);
        }
    }
}
