using System;
using System.Globalization;
using System.Collections.Generic;
using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private readonly Dictionary<string, string> _singleSliderEdits = new Dictionary<string, string>();
        private int _singleSliderIndex;
        private void OnGUI()
        {
            if (!CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom)
                return;

            // 総合UIがタブとして表示する間は自前窓を出さない（二重表示防止）。
            if (IsHostedByUnifiedUi())
                return;

            if (_settings.UiVisible)
            {
                ClampWindowRectToScreen(ref _mainWindowRect, 360f, 320f);
                _lastMainWindowRect = _mainWindowRect;
                _mainWindowRect = GUI.Window(MainWindowId, _mainWindowRect, DrawMainWindow, "オブジェクト管理");
            }
        }

        private void DrawMainWindow(int id)
        {
            GUILayout.BeginVertical();
            float scrollHeight = Mathf.Max(160f, _mainWindowRect.height - 52f);
            _mainScroll = GUILayout.BeginScrollView(_mainScroll, false, true, GUILayout.Height(scrollHeight));

            DrawMainBody();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            Rect dragRect = new Rect(0f, 0f, 10000f, 20f);
            UpdateWindowDragHint(dragRect, ref _mainWindowDragging);
            GUI.DragWindow(dragRect);
        }

        // 窓の中身本体。自前窓（スクロール＋ドラッグ枠つき）と総合UIタブの両方から共用する。
        private void DrawMainBody()
        {
            _singleSliderIndex = 0;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("円柱を追加", GUILayout.Height(36f)))
            {
                CreateCylinder();
            }
            if (GUILayout.Button("回転オブジェクト追加", GUILayout.Height(36f)))
            {
                CreateRotationObject();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ピストン追加", GUILayout.Height(36f)))
            {
                CreatePistonObject();
            }
            if (GUILayout.Button("アングル追加", GUILayout.Height(36f)))
            {
                CreateAngleObject();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            bool nextFemaleGhost = GUILayout.Toggle(_femaleGhostEnabled, "配置用: 女キャラを半透明にする");
            if (nextFemaleGhost != _femaleGhostEnabled)
            {
                _femaleGhostEnabled = nextFemaleGhost;
                if (!nextFemaleGhost) RestoreFemaleGhost();
                else _nextFemaleGhostScan = 0f;
            }

            bool nextGizmoVis = GUILayout.Toggle(_settings.EnableSelectedGizmo, "ギズモ表示");
            if (nextGizmoVis != _settings.EnableSelectedGizmo)
            {
                _settings.EnableSelectedGizmo = nextGizmoVis;
                if (_cfgEnableSelectedGizmo != null) _cfgEnableSelectedGizmo.Value = nextGizmoVis;
                if (!nextGizmoVis) DetachSelectedGizmo();
                SaveSettings();
            }
            GUILayout.Label("ギズモサイズ (0.2〜4.0)");
            float gizmoSize = _settings.GizmoSizeMultiplier;
            DrawSingleSlider("Gz", ref gizmoSize, 0.2f, 4f, "F2");
            if (!Mathf.Approximately(gizmoSize, _settings.GizmoSizeMultiplier))
            {
                _settings.GizmoSizeMultiplier = gizmoSize;
                SaveSettings();
            }

            bool nextDbg = GUILayout.Toggle(_settings.DebugLogEnabled, "デバッグログ (右クリック等)");
            if (nextDbg != _settings.DebugLogEnabled)
            {
                _settings.DebugLogEnabled = nextDbg;
                SaveSettings();
            }

            GUILayout.Space(6f);
            DrawPresetSection();

            GUILayout.Space(8f);
            GUILayout.Label("オブジェクト");
            DrawObjectTree();

            GUILayout.Space(8f);
            DrawSelectedSection();
        }

        private void DrawObjectTree()
        {
            // ルート (= 内部ルート) または外部親 (キャラ等) のオブジェクトをトップレベルに表示
            for (int i = 0; i < _objects.Count; i++)
            {
                ManagedObjectData obj = _objects[i];
                if (obj == null) continue;
                bool isTopLevel = string.Equals(obj.parentKind, ParentKindRoot, StringComparison.Ordinal)
                                || string.Equals(obj.parentKind, ParentKindExternal, StringComparison.Ordinal);
                if (!isTopLevel) continue;
                DrawObjectTreeNode(obj, 0);
            }
        }

        private void DrawObjectTreeNode(ManagedObjectData obj, int depth)
        {
            GUILayout.BeginHorizontal();
            if (depth > 0) GUILayout.Space(depth * 16f);

            bool selected = string.Equals(_selectedId, obj.id, StringComparison.Ordinal);
            string prefix = obj.isRotationObject ? "[回] " : string.Empty;
            if (string.Equals(obj.parentKind, ParentKindExternal, StringComparison.Ordinal))
            {
                string extLabel = string.Equals(obj.parentRefId, "char:root", StringComparison.Ordinal)
                    ? "[キャラ] " : "[外部] ";
                prefix = extLabel + prefix;
            }
            string label = prefix + obj.name;
            bool nextSelected = GUILayout.Toggle(selected, label, GUILayout.ExpandWidth(true));
            if (nextSelected && !selected)
            {
                _selectedId = obj.id;
                _selectionDirty = true;
            }

            // 「子化」: このオブジェクトを現在選択中の子にする
            // 無効条件: 選択なし / 自分が選択中 / 選択中がこのオブジェクトの子孫（サイクル）
            bool canBecomeChild = !string.IsNullOrEmpty(_selectedId)
                                  && !string.Equals(_selectedId, obj.id, StringComparison.Ordinal)
                                  && !IsDescendantOrSelf(_selectedId, obj.id);
            bool prevEnabled = GUI.enabled;
            GUI.enabled = canBecomeChild;
            if (GUILayout.Button("子化", GUILayout.Width(42f)))
            {
                SetObjectAsChildOfSelected(obj.id);
            }
            GUI.enabled = prevEnabled;

            // 「根」: ルート直下に戻す。既にルートなら無効
            bool isRoot = string.Equals(obj.parentKind, ParentKindRoot, StringComparison.Ordinal);
            GUI.enabled = !isRoot;
            if (GUILayout.Button("根", GUILayout.Width(32f)))
            {
                SetObjectAsRoot(obj.id);
            }
            GUI.enabled = prevEnabled;

            // 「キャラ」: 女キャラ本体 (char:root) の子にする
            // キャラの位置移動には追従するが、腰アニメ振動には連動しない (相対固定位置を保つ)
            bool charAvailable = TryGetExternalParentTarget("char:root", out _);
            bool alreadyOnChar = string.Equals(obj.parentKind, ParentKindExternal, StringComparison.Ordinal)
                              && string.Equals(obj.parentRefId, "char:root", StringComparison.Ordinal);
            GUI.enabled = charAvailable && !alreadyOnChar;
            if (GUILayout.Button("キャラ", GUILayout.Width(44f)))
            {
                SetObjectParentToExternal(obj.id, "char:root");
            }
            GUI.enabled = prevEnabled;

            GUILayout.EndHorizontal();

            for (int i = 0; i < _objects.Count; i++)
            {
                ManagedObjectData child = _objects[i];
                if (child == null) continue;
                if (string.Equals(child.parentKind, ParentKindManaged, StringComparison.Ordinal)
                    && string.Equals(child.parentRefId, obj.id, StringComparison.Ordinal))
                {
                    DrawObjectTreeNode(child, depth + 1);
                }
            }
        }

        private void DrawSelectedSection()
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                GUILayout.Label("（オブジェクトを選択してください）");
                return;
            }

            if (_selectionDirty)
            {
                RefreshSelectedEditorFields();
            }

            GUILayout.BeginHorizontal();
            string kindLabel = selected.isRotationObject ? "[回転] "
                : selected.isPistonObject ? "[ピストン] "
                : selected.isAngleObject  ? "[アングル] " : "";
            GUILayout.Label("選択中: " + kindLabel + selected.name);
            if (GUILayout.Button("削除", GUILayout.Width(60f)))
            {
                DeleteSelectedObject();
                return;
            }
            GUILayout.EndHorizontal();

            // 表示/非表示トグル（全オブジェクト共通）
            bool nextVisible = GUILayout.Toggle(selected.visible, "表示");
            if (nextVisible != selected.visible)
            {
                selected.visible = nextVisible;
                RuntimeObjectRef rr = FindRuntimeById(selected.id);
                if (rr != null) ApplyVisibility(rr);
                SaveLayoutIfNeeded();
                // アクティブプリセットがあれば L1/L2 にも反映（再アクティブ化や自動切替で復元される）
                SyncVisibleToActivePreset(selected.id, nextVisible);
            }

            // ドライバ専用パラメータ
            if (selected.isRotationObject)
            {
                DrawRotationObjectParams(selected);
            }
            else if (selected.isPistonObject)
            {
                DrawPistonObjectParams(selected);
            }
            else if (selected.isAngleObject)
            {
                DrawAngleObjectParams(selected);
            }

            // 親がドライバの子: 位相スライダー
            if (string.Equals(selected.parentKind, ParentKindManaged, System.StringComparison.Ordinal))
            {
                ManagedObjectData parent = FindDataById(selected.parentRefId);
                if (parent != null && parent.isRotationObject)
                {
                    DrawOrbitChildParams(selected);
                }
                else if (parent != null && (parent.isPistonObject || parent.isAngleObject))
                {
                    DrawDriverChildParams(selected);
                }
            }

            GUILayout.Space(4f);
            GUILayout.Label("位置 (m, スライダーは±0.5、テキストは無制限)");
            DrawAxisControl("X", ref _sliderPosX, ref _editPosX, -0.5f, 0.5f);
            DrawAxisControl("Y", ref _sliderPosY, ref _editPosY, -0.5f, 0.5f);
            DrawAxisControl("Z", ref _sliderPosZ, ref _editPosZ, -0.5f, 0.5f);

            GUILayout.Space(4f);
            GUILayout.Label("角度 (°)");
            DrawAxisControl("X", ref _sliderRotX, ref _editRotX, -180f, 180f);
            DrawAxisControl("Y", ref _sliderRotY, ref _editRotY, -180f, 180f);
            DrawAxisControl("Z", ref _sliderRotZ, ref _editRotZ, -180f, 180f);

            GUILayout.Space(4f);
            GUILayout.Label("サイズ (スライダーは0.001〜1、テキストは無制限)");
            DrawAxisControl("X", ref _sliderScaleX, ref _editScaleX, 0.001f, 1f);
            DrawAxisControl("Y", ref _sliderScaleY, ref _editScaleY, 0.001f, 1f);
            DrawAxisControl("Z", ref _sliderScaleZ, ref _editScaleZ, 0.001f, 1f);

            DrawHipHijackLinkSection();
        }

        private void DrawRotationObjectParams(ManagedObjectData selected)
        {
            ManagedObjectData before = CloneObject(selected);
            bool alignInsertion = GUILayout.Toggle(selected.vibeInsertionAlignEnabled,
                "Vibe の挿入方向に軌道を合わせる (+Z)");
            if (alignInsertion != selected.vibeInsertionAlignEnabled)
            {
                selected.vibeInsertionAlignEnabled = alignInsertion;
                SaveLayoutIfNeeded();
            }
            if (!string.IsNullOrEmpty(selected.parentId))
            {
                ManagedObjectData parentData = FindDataById(selected.parentId);
                if (parentData != null && parentData.isRotationObject)
                {
                    GUILayout.Space(4f);
                    GUILayout.Label("Vibe のゆっくり動作で親の横回転を混ぜる");
                    bool enableSlowParent = GUILayout.Toggle(selected.vibeSlowParentEnabled, "ゆっくり時だけ親を反映");
                    if (enableSlowParent != selected.vibeSlowParentEnabled)
                    {
                        selected.vibeSlowParentEnabled = enableSlowParent;
                        SaveLayoutIfNeeded();
                    }
                    DrawSingleSlider("親の影響", ref selected.vibeSlowParentWeight, 0f, 1f, "F2");
                    DrawSingleSlider("巻込 秒", ref selected.vibeSlowRiseSeconds, 0.05f, 5f, "F2");
                    DrawSingleSlider("戻り 秒", ref selected.vibeSlowFallSeconds, 0.05f, 5f, "F2");
                }
            }
            GUILayout.Space(4f);
            GUILayout.Label("ドーナツ形状（X/Z半径 = 楕円軌道）");
            DrawSingleSlider("Rx", ref selected.orbitRadiusX, 0.05f, 2f, "F3");
            DrawSingleSlider("Rz", ref selected.orbitRadiusZ, 0.05f, 2f, "F3");
            GUILayout.Label("ドーナツの細さ");
            DrawSingleSlider("管", ref selected.tubeRadius, 0.001f, 0.1f, "F4");
            GUILayout.Label("公転速度 (Hz, アニメ非同期時のみ有効)");
            float prevSpeed = selected.orbitSpeedHz;
            float speedRef = selected.orbitSpeedHz;
            DrawSingleSlider("速", ref speedRef, -5f, 5f, "F3");
            if (!Mathf.Approximately(speedRef, prevSpeed))
            {
                RebasePhaseContinuity(selected, () => { selected.orbitSpeedHz = speedRef; });
                SaveLayoutIfNeeded();
            }

            GUILayout.Space(2f);
            bool nextSync = GUILayout.Toggle(selected.animSync, "アニメ同期 (HSceneアニメに厳密追従)");
            if (nextSync != selected.animSync)
            {
                selected.animSync = nextSync;
                SaveLayoutIfNeeded();
            }
            GUILayout.Label("同期倍率 (1.0=アニメ1周で1公転, 2.0=2公転, 0.5=半公転)");
            DrawSingleSlider("倍", ref selected.animSpeedMultiplier, 0.1f, 10f, "F2");

            if (selected.animSync)
            {
                GUILayout.Label("位相シフト (0〜1, アニメ同期に対するズラし量)");
                DrawSingleSlider("位", ref selected.animSyncPhaseShift, -1f, 1f, "F3");
            }

            GUILayout.Space(2f);
            bool nextOrient = GUILayout.Toggle(selected.orientChildrenToTangent, "子の接線追従 (新規子のデフォルト)");
            if (nextOrient != selected.orientChildrenToTangent)
            {
                selected.orientChildrenToTangent = nextOrient;
                SaveLayoutIfNeeded();
            }
            SyncRotationParamsToActivePreset(selected.id, before, selected);
        }

        private void DrawPistonObjectParams(ManagedObjectData selected)
        {
            GUILayout.Space(4f);
            GUILayout.Label("ピストンレール（全長 = 振幅×2）");
            DrawSingleSlider("振", ref selected.pistonAmplitude, 0f, 10f, "F3");

            GUILayout.Label("ロッド太さ (m)");
            DrawSingleSlider("太", ref selected.pistonRodRadius, 0.0005f, 0.1f, "F4");

            GUILayout.Label("速度 (Hz)");
            DrawSingleSlider("速", ref selected.pistonSpeedHz, 0.01f, 20f, "F2");

            GUILayout.Label("位相 (0〜1)");
            if (DrawSingleSlider("位", ref selected.pistonPhaseTurns, 0f, 1f, "F3"))
            {
                SyncPhaseToActivePreset(selected.id, selected);
            }

            GUILayout.Label("軸方向");
            DrawSingleSlider("X", ref selected.pistonAxis.x, -1f, 1f, "F2");
            DrawSingleSlider("Y", ref selected.pistonAxis.y, -1f, 1f, "F2");
            DrawSingleSlider("Z", ref selected.pistonAxis.z, -1f, 1f, "F2");

            bool nextLocal = GUILayout.Toggle(selected.pistonLocalSpace, "ローカル空間");
            if (nextLocal != selected.pistonLocalSpace)
            {
                selected.pistonLocalSpace = nextLocal;
                SaveLayoutIfNeeded();
            }
        }

        private void DrawAngleObjectParams(ManagedObjectData selected)
        {
            GUILayout.Space(4f);
            GUILayout.Label("アングル扇形（開き = 角度×2）");
            DrawSingleSlider("角", ref selected.angleAmplitudeDeg, 0f, 180f, "F1");

            GUILayout.Label("扇の半径 (m)");
            DrawSingleSlider("半", ref selected.angleFanRadius, 0.01f, 2f, "F3");

            GUILayout.Label("速度 (Hz)");
            DrawSingleSlider("速", ref selected.angleSpeedHz, 0.01f, 20f, "F2");

            GUILayout.Label("位相 (0〜1)");
            if (DrawSingleSlider("位", ref selected.anglePhaseTurns, 0f, 1f, "F3"))
            {
                SyncPhaseToActivePreset(selected.id, selected);
            }

            GUILayout.Label("軸方向");
            DrawSingleSlider("X", ref selected.angleAxis.x, -1f, 1f, "F2");
            DrawSingleSlider("Y", ref selected.angleAxis.y, -1f, 1f, "F2");
            DrawSingleSlider("Z", ref selected.angleAxis.z, -1f, 1f, "F2");

            bool nextLocal = GUILayout.Toggle(selected.angleLocalSpace, "ローカル空間");
            if (nextLocal != selected.angleLocalSpace)
            {
                selected.angleLocalSpace = nextLocal;
                SaveLayoutIfNeeded();
            }
        }

        private void DrawDriverChildParams(ManagedObjectData selected)
        {
            GUILayout.Space(4f);
            GUILayout.Label("ドライバ子: 個別位相オフセット (0〜1)");
            if (DrawSingleSlider("位", ref selected.orbitPhaseTurns, 0f, 1f, "F3"))
            {
                SyncPhaseToActivePreset(selected.id, selected);
            }
        }

        private void DrawOrbitChildParams(ManagedObjectData selected)
        {
            GUILayout.Space(4f);
            GUILayout.Label("親の軌道上の位相 (0〜1)");
            if (DrawSingleSlider("位", ref selected.orbitPhaseTurns, 0f, 1f, "F3"))
            {
                SyncPhaseToActivePreset(selected.id, selected);
            }

            bool nextOrient = GUILayout.Toggle(selected.orientToTangent, "軌道接線方向を向く (OFFで向き固定)");
            if (nextOrient != selected.orientToTangent)
            {
                selected.orientToTangent = nextOrient;
                SaveLayoutIfNeeded();
            }

            GUILayout.Label("オフセット軸: 位置X=半径方向 / Y=縦 / Z=軌道接線方向");
            if (GUILayout.Button("初期接続点に戻す (オフセット 0,0,0)", GUILayout.Width(260f)))
            {
                ResetOrbitChildOffset(selected);
            }
        }

        /// <summary>
        /// 子の orbit offset (localPosition) を (0,0,0) に戻す。
        /// アクティブプリセット中なら L1/L2 両方の該当エントリも 0 にする。
        /// </summary>
        private void ResetOrbitChildOffset(ManagedObjectData selected)
        {
            if (selected == null) return;
            Vector3 oldPos = selected.localPosition;
            selected.localPosition = Vector3.zero;
            RuntimeObjectRef rr = FindRuntimeById(selected.id);
            if (rr != null && rr.GameObject != null)
            {
                rr.GameObject.transform.localPosition = Vector3.zero;
            }
            RefreshSelectedEditorFields();
            // アクティブプリセットの L1/L2 にも反映 (delta = -oldPos)
            if (_activePreset != null)
            {
                ShiftActivePresetEntryDelta(selected.id, -oldPos, Vector3.zero, Vector3.zero);
            }
            SaveLayoutIfNeeded();
            LogInfo("orbit child offset reset: id=" + selected.id);
        }

        private bool DrawSingleSlider(string label, ref float value, float min, float max, string fmt)
        {
            bool changed = false;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(24f));
            float displayVal = Mathf.Clamp(value, min, max);
            float newSlider = GUILayout.HorizontalSlider(displayVal, min, max, GUILayout.ExpandWidth(true));
            if (!Mathf.Approximately(newSlider, displayVal))
            {
                value = newSlider;
                changed = true;
                SaveLayoutIfNeeded();
            }
            string control = "ComposerSlider_" + (_singleSliderIndex++).ToString(CultureInfo.InvariantCulture);
            GUI.SetNextControlName(control);
            bool focused = GUI.GetNameOfFocusedControl() == control;
            string text = focused && _singleSliderEdits.TryGetValue(control, out string edit)
                ? edit : value.ToString(fmt, CultureInfo.InvariantCulture);
            string newText = GUILayout.TextField(text, GUILayout.Width(72f));
            if (GUI.GetNameOfFocusedControl() == control)
            {
                _singleSliderEdits[control] = newText;
                if (newText != text && float.TryParse(newText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    value = parsed;
                    changed = true;
                    SaveLayoutIfNeeded();
                }
            }
            else _singleSliderEdits.Remove(control);
            GUILayout.EndHorizontal();
            return changed;
        }

        private void DrawAxisControl(string label, ref float sliderVal, ref string textVal, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(16f));

            // スライダー表示用に値をクランプ（実値はクランプしない＝範囲外でもテキストで保持可能）
            float displaySlider = Mathf.Clamp(sliderVal, min, max);
            float newSlider = GUILayout.HorizontalSlider(displaySlider, min, max, GUILayout.ExpandWidth(true));

            // 値変化を「ユーザードラッグ」と判定するのはマウス押下中のみ。
            // クランプ差分や初期値が範囲外のケースで誤発火しないようにする。
            // UI入力キャプチャは UpdateUiInputCapture が毎フレーム面倒を見るのでここでは何もしない。
            if (GetOwnerMouseButton(0) && !Mathf.Approximately(newSlider, displaySlider))
            {
                sliderVal = newSlider;
                textVal = newSlider.ToString("F3", CultureInfo.InvariantCulture);
                CommitLiveTransform();
            }

            string newText = GUILayout.TextField(textVal, GUILayout.Width(72f));
            if (newText != textVal)
            {
                textVal = newText;
                if (float.TryParse(newText, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                {
                    sliderVal = parsed; // テキスト入力はクランプしない
                    CommitLiveTransform();
                }
            }

            GUILayout.EndHorizontal();
        }

        private void CommitLiveTransform()
        {
            if (!float.TryParse(_editPosX, NumberStyles.Float, CultureInfo.InvariantCulture, out float px)) return;
            if (!float.TryParse(_editPosY, NumberStyles.Float, CultureInfo.InvariantCulture, out float py)) return;
            if (!float.TryParse(_editPosZ, NumberStyles.Float, CultureInfo.InvariantCulture, out float pz)) return;
            if (!float.TryParse(_editRotX, NumberStyles.Float, CultureInfo.InvariantCulture, out float rx)) return;
            if (!float.TryParse(_editRotY, NumberStyles.Float, CultureInfo.InvariantCulture, out float ry)) return;
            if (!float.TryParse(_editRotZ, NumberStyles.Float, CultureInfo.InvariantCulture, out float rz)) return;
            if (!float.TryParse(_editScaleX, NumberStyles.Float, CultureInfo.InvariantCulture, out float sx)) return;
            if (!float.TryParse(_editScaleY, NumberStyles.Float, CultureInfo.InvariantCulture, out float sy)) return;
            if (!float.TryParse(_editScaleZ, NumberStyles.Float, CultureInfo.InvariantCulture, out float sz)) return;

            ApplyTransformLive(
                new Vector3(px, py, pz),
                new Vector3(rx, ry, rz),
                new Vector3(sx, sy, sz));
        }

        private void RefreshSelectedEditorFields()
        {
            ManagedObjectData selected = GetSelectedData();
            if (selected == null)
            {
                _selectionDirty = false;
                return;
            }

            _sliderPosX = selected.localPosition.x;
            _sliderPosY = selected.localPosition.y;
            _sliderPosZ = selected.localPosition.z;
            _editPosX = _sliderPosX.ToString("F3", CultureInfo.InvariantCulture);
            _editPosY = _sliderPosY.ToString("F3", CultureInfo.InvariantCulture);
            _editPosZ = _sliderPosZ.ToString("F3", CultureInfo.InvariantCulture);

            _sliderRotX = selected.localEulerAngles.x;
            _sliderRotY = selected.localEulerAngles.y;
            _sliderRotZ = selected.localEulerAngles.z;
            _editRotX = _sliderRotX.ToString("F3", CultureInfo.InvariantCulture);
            _editRotY = _sliderRotY.ToString("F3", CultureInfo.InvariantCulture);
            _editRotZ = _sliderRotZ.ToString("F3", CultureInfo.InvariantCulture);

            _sliderScaleX = selected.localScale.x;
            _sliderScaleY = selected.localScale.y;
            _sliderScaleZ = selected.localScale.z;
            _editScaleX = _sliderScaleX.ToString("F3", CultureInfo.InvariantCulture);
            _editScaleY = _sliderScaleY.ToString("F3", CultureInfo.InvariantCulture);
            _editScaleZ = _sliderScaleZ.ToString("F3", CultureInfo.InvariantCulture);

            _selectionDirty = false;
        }

        private static void ClampWindowRectToScreen(ref Rect rect, float minWidth, float minHeight)
        {
            float maxWidth = Mathf.Max(minWidth, Screen.width - 8f);
            float maxHeight = Mathf.Max(minHeight, Screen.height - 8f);
            rect.width = Mathf.Clamp(rect.width, minWidth, maxWidth);
            rect.height = Mathf.Clamp(rect.height, minHeight, maxHeight);
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, Screen.width - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, Screen.height - rect.height));
        }

        // Plugin.Motion.UI.cs から参照されるためここに残す
        private static void DrawVec3Editors(ref string x, ref string y, ref string z)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("X", GUILayout.Width(14f));
            x = GUILayout.TextField(x, GUILayout.Width(74f));
            GUILayout.Label("Y", GUILayout.Width(14f));
            y = GUILayout.TextField(y, GUILayout.Width(74f));
            GUILayout.Label("Z", GUILayout.Width(14f));
            z = GUILayout.TextField(z, GUILayout.Width(74f));
            GUILayout.EndHorizontal();
        }

        private static bool TryParseFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseVec3(string x, string y, string z, out Vector3 value)
        {
            value = Vector3.zero;
            if (!TryParseFloat(x, out float vx)) return false;
            if (!TryParseFloat(y, out float vy)) return false;
            if (!TryParseFloat(z, out float vz)) return false;
            value = new Vector3(vx, vy, vz);
            return true;
        }
    }
}
