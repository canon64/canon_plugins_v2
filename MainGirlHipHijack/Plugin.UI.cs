using UnityEngine;
using MainGameTransformGizmo;
using VRGIN.Core;
using MainGirlHipHijack.Runtime;
using CanonPlugins.Shared;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private void OnGUI()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (!IsPluginEnabled())
                return;

            DrawGuiNotify();

            // 総合UIがタブ表示する間は自前窓を出さない（ボーン選択オーバーレイは残す）。
            if (_unifiedUiGateway.IsHosting())
            {
                DrawCandidateBoneOverlay();
                return;
            }

            if (!_settings.UiVisible)
                return;

            ClampWindowRectToScreen();
            _windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, PluginName);
            DrawCandidateBoneOverlay();
        }

        private void DrawUnifiedContent()
        {
            DrawHipHijackBody();
            DrawTooltip();
        }

        private void DrawGuiNotify()
        {
            if (string.IsNullOrEmpty(_guiNotifyText))
                return;
            if (Time.unscaledTime >= _guiNotifyEndTime)
            {
                _guiNotifyText = null;
                return;
            }

            float alpha = Mathf.Clamp01((_guiNotifyEndTime - Time.unscaledTime) / 0.5f);
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = new Color(1f, 1f, 0.2f, alpha);

            float w = 400f;
            float h = 50f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height * 0.15f;

            // 影
            var shadowStyle = new GUIStyle(style);
            shadowStyle.normal.textColor = new Color(0f, 0f, 0f, alpha * 0.8f);
            GUI.Label(new Rect(x + 2f, y + 2f, w, h), _guiNotifyText, shadowStyle);
            GUI.Label(new Rect(x, y, w, h), _guiNotifyText, style);
        }

        private void DrawCandidateBoneOverlay()
        {
            var cam = Camera.main;
            if (cam == null) return;

            var shadowStyle = new GUIStyle(GUI.skin.label);
            shadowStyle.normal.textColor = Color.black;
            var textStyle = new GUIStyle(GUI.skin.label);
            textStyle.normal.textColor = new Color(0.4f, 0.8f, 1f);

            for (int i = 0; i < BIK_TOTAL; i++)
            {
                BodyIkOperationPointState state = _bikEff[i];
                if (state == null || !state.GizmoDragging || state.CandidateBone == null)
                    continue;

                Vector3 screenPos = cam.WorldToScreenPoint(state.CandidateBone.position);
                if (screenPos.z <= 0f) continue;

                float gx = screenPos.x + 14f;
                float gy = Screen.height - screenPos.y - 10f;
                string label = state.CandidateBone.name;

                // 影
                GUI.Label(new Rect(gx + 1f, gy + 1f, 240f, 20f), label, shadowStyle);
                // 本文
                GUI.Label(new Rect(gx, gy, 240f, 20f), label, textStyle);
            }
        }

        private void DrawWindow(int id)
        {
            Event ev = Event.current;
            HandleWindowDragFlag(ev);
            HandleSliderDragFlag(ev);
            HandleScrollDragFlag(ev);
            HandleWindowScrollWheel();

            GUILayout.BeginVertical();
            float scrollHeight = Mathf.Max(160f, _windowRect.height - 48f);
            _scroll = GUILayout.BeginScrollView(_scroll, false, true, GUILayout.Height(scrollHeight));

            DrawHipHijackBody();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
            DrawTooltip();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 20f));
        }

        // 窓の中身本体。スクロール/ドラッグ枠の外側は呼び出し側が持つ。総合UIタブと共用。
        private void DrawHipHijackBody()
        {
            Event ev = Event.current;

            // ━━━ ステータス ━━━
            _secStatusExpanded = GUILayout.Toggle(_secStatusExpanded, _secStatusExpanded ? "━━━ ステータス ▼" : "━━━ ステータス ▲");
            if (_secStatusExpanded)
            {
                GUILayout.Label("対象キャラ: " + (_femaleSession.Character != null ? GetFemaleName(_femaleSession.Character) : "<未検出>"));
                GUILayout.Label("FBBIK: " + (_femaleSession.FullBodyIk != null ? "検出済み" : "未検出"));
                GUILayout.Label("入力奪取状態: " + (IsInputCaptureActive() ? "ON" : "OFF"));
                GUILayout.Label("UI表示切替: ConfigManager > MainGirlHipHijack > UI > Visible");

                bool detailLog = GUILayout.Toggle(_settings.DetailLogEnabled,
                    new GUIContent("詳細ログ", "入力キャプチャ/状態監視などの詳細ログを出力する"));
                if (detailLog != _settings.DetailLogEnabled)
                {
                    SetDetailLoggingEnabled(detailLog, "ui-toggle");
                }

                bool bodyIkDiag = GUILayout.Toggle(_settings.BodyIkDiagnosticLog,
                    new GUIContent("BodyIK診断ログ", "IK適用の前後状態を比較する診断ログを出力する（MainGirlHipHijack.log）"));
                if (bodyIkDiag != _settings.BodyIkDiagnosticLog)
                {
                    _settings.BodyIkDiagnosticLog = bodyIkDiag;
                    SaveSettings();
                }

                float bodyDiagInterval = DrawSliderWithField(
                    "BodyIK診断間隔",
                    _settings.BodyIkDiagnosticLogInterval,
                    0.05f,
                    2f,
                    "F2",
                    tooltip: "BodyIK診断ログの出力間隔（秒）");
                if (!Mathf.Approximately(bodyDiagInterval, _settings.BodyIkDiagnosticLogInterval))
                {
                    _settings.BodyIkDiagnosticLogInterval = bodyDiagInterval;
                    SaveSettings();
                }
            }

            // ━━━ VR ━━━
            if (VR.Active)
            {
                _secVrExpanded = GUILayout.Toggle(_secVrExpanded, _secVrExpanded ? "━━━ VR ▼" : "━━━ VR ▲");
                if (_secVrExpanded)
                {
                    string vrLabel = _vrGrabMode ? "VR掴みモード: ON" : "VR掴みモード: OFF";
                    if (GUILayout.Button(new GUIContent(vrLabel, "VRコントローラーのグリップでIKプロキシを掴んで移動できるモード"), GUILayout.Width(200f)))
                        ToggleVRGrabMode();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(new GUIContent("VR掴み距離", "VRコントローラーがIKプロキシ球に反応する距離閾値（メートル）"), GUILayout.Width(96f));
                    float grabDist = _settings.VRGrabDistance;
                    float nextGrabDist = GUILayout.HorizontalSlider(grabDist, 0.02f, 0.6f, GUILayout.Width(160f));
                    GUILayout.Label(nextGrabDist.ToString("F2"), GUILayout.Width(40f));
                    GUILayout.EndHorizontal();
                    if (!Mathf.Approximately(grabDist, nextGrabDist))
                    {
                        _settings.VRGrabDistance = nextGrabDist;
                        SaveSettings();
                    }

                    float vrMarkerSize = DrawSliderWithField(
                        "VRマーカーサイズ",
                        _settings.VRMarkerSize, 0.01f, 0.1f, "F2",
                        tooltip: "VR掴み用マーカー球のサイズ（メートル）");
                    if (!Mathf.Approximately(vrMarkerSize, _settings.VRMarkerSize))
                    {
                        _settings.VRMarkerSize = vrMarkerSize;
                        ApplyVrMarkerSizeToAll();
                        SaveSettings();
                    }

                    float headGrabDist = DrawSliderWithField(
                        (_femaleHeadInRange ? "女頭距離[範囲内]" : "女頭距離[範囲外]"),
                        _settings.FemaleHeadGrabDistance, 0.02f, 0.6f, "F2",
                        tooltip: "VRで女キャラの頭をグラブできる距離閾値（メートル）");
                    if (!Mathf.Approximately(headGrabDist, _settings.FemaleHeadGrabDistance))
                    {
                        _settings.FemaleHeadGrabDistance = headGrabDist;
                        SaveSettings();
                    }
                    if (GUILayout.Button(new GUIContent("頭回転リセット", "女キャラの頭の追加回転オフセットをリセットする"), GUILayout.Width(100f)))
                        ResetFemaleHeadAdditiveRot();
                }
            }

            // ━━━ 腰 ━━━
            _secHipExpanded = GUILayout.Toggle(_secHipExpanded, _secHipExpanded ? "━━━ 腰 ▼" : "━━━ 腰 ▲");
            if (_secHipExpanded)
            {
                DrawBodyIkRow(BIK_BODY, ev);
                {
                    var boldToggle = new GUIStyle(GUI.skin.toggle) { fontStyle = FontStyle.Bold };
                    bool prevEnabled = GUI.enabled;
                    GUI.enabled = VR.Active && _bikEff[BIK_BODY].Running;
                    GUILayout.BeginHorizontal();
                    bool linkNext = GUILayout.Toggle(_bodyCtrlLinkEnabled,
                        new GUIContent("左コントローラー腰リンク", "VRの左コントローラーの動きを腰IKに連動させる"), boldToggle);
                    if (linkNext != _bodyCtrlLinkEnabled)
                        ToggleBodyCtrlLink();
                    GUI.enabled = VR.Active && _bodyCtrlLinkEnabled;
                    if (GUILayout.Button(new GUIContent("腰位置リセット", "腰リンクのベースライン位置を現在位置に合わせてリセット"), GUILayout.Width(100f)))
                        RebaselineBodyCtrlLink();
                    GUILayout.EndHorizontal();
                    GUI.enabled = prevEnabled;
                }

                {
                    var boldToggle = new GUIStyle(GUI.skin.toggle) { fontStyle = FontStyle.Bold };
                    bool speedHijack = GUILayout.Toggle(_settings.SpeedHijackEnabled,
                        new GUIContent("速さゲージ乗っ取り（腰動きで速さ駆動）", "腰の動きでHシーンの速さゲージを駆動する"), boldToggle);
                    if (speedHijack != _settings.SpeedHijackEnabled)
                    {
                        _settings.SpeedHijackEnabled = speedHijack;
                        SaveSettings();
                    }
                }

                {
                    var boldToggle = new GUIStyle(GUI.skin.toggle) { fontStyle = FontStyle.Bold };
                    bool cutAnimSpeed = GUILayout.Toggle(_settings.CutFemaleAnimSpeedEnabled,
                        new GUIContent("女性アニメ速度切断", "女性側のアニメーション速度を0に固定する（速さゲージ乗っ取りと組み合わせて使用）"), boldToggle);
                    if (cutAnimSpeed != _settings.CutFemaleAnimSpeedEnabled)
                    {
                        _settings.CutFemaleAnimSpeedEnabled = cutAnimSpeed;
                        SaveSettings();
                    }
                }

                {
                    var boldToggle = new GUIStyle(GUI.skin.toggle) { fontStyle = FontStyle.Bold };
                    bool autoInsert = GUILayout.Toggle(_settings.AutoInsertOnMoveEnabled,
                        new GUIContent("待機中の動きで自動挿入", "待機中に腰の動きを検知すると自動で挿入→ピストンへ移行する"), boldToggle);
                    if (autoInsert != _settings.AutoInsertOnMoveEnabled)
                    {
                        _settings.AutoInsertOnMoveEnabled = autoInsert;
                        SaveSettings();
                    }
                }

                string paramsLabel = _hSceneParamsExpanded ? "各種パラメータ ▼" : "各種パラメータ ▲";
                _hSceneParamsExpanded = GUILayout.Toggle(_hSceneParamsExpanded, paramsLabel);
                if (_hSceneParamsExpanded)
                {
                    GUILayout.Label("── 腰リンク ──");
                    float nbcfx = DrawSliderWithField("移動倍率X", _settings.BodyCtrlChangeFactorX, 0f, 20f, "F1", tooltip: "腰リンクのX軸移動倍率");
                    if (!Mathf.Approximately(nbcfx, _settings.BodyCtrlChangeFactorX)) { _settings.BodyCtrlChangeFactorX = nbcfx; SaveSettings(); }

                    float nbcfy = DrawSliderWithField("移動倍率Y", _settings.BodyCtrlChangeFactorY, 0f, 20f, "F1", tooltip: "腰リンクのY軸移動倍率");
                    if (!Mathf.Approximately(nbcfy, _settings.BodyCtrlChangeFactorY)) { _settings.BodyCtrlChangeFactorY = nbcfy; SaveSettings(); }

                    float nbcfz = DrawSliderWithField("移動倍率Z", _settings.BodyCtrlChangeFactorZ, 0f, 20f, "F1", tooltip: "腰リンクのZ軸移動倍率");
                    if (!Mathf.Approximately(nbcfz, _settings.BodyCtrlChangeFactorZ)) { _settings.BodyCtrlChangeFactorZ = nbcfz; SaveSettings(); }

                    float nbcd = DrawSliderWithField("ダンペン", _settings.BodyCtrlDampen, 0f, 1f, "F3", tooltip: "腰リンクの動きの滑らかさ（0に近いほど追従が鋭く、1に近いほど遅い）");
                    if (!Mathf.Approximately(nbcd, _settings.BodyCtrlDampen)) { _settings.BodyCtrlDampen = nbcd; SaveSettings(); }

                    GUILayout.Label("── 速さゲージ ──");
                    float nmas = DrawSliderWithField("移動中増加量/秒", _settings.SpeedMoveAddPerSecond, 0.001f, 20f, "F3", tooltip: "腰が動いている間に速さゲージが増加する量（毎秒）");
                    if (!Mathf.Approximately(nmas, _settings.SpeedMoveAddPerSecond)) { _settings.SpeedMoveAddPerSecond = nmas; SaveSettings(); }

                    float ndps = DrawSliderWithField("停止時減衰量/秒", _settings.SpeedDecayPerSecond, 0.001f, 20f, "F3", tooltip: "腰が止まっているときに速さゲージが減衰する量（毎秒）");
                    if (!Mathf.Approximately(ndps, _settings.SpeedDecayPerSecond)) { _settings.SpeedDecayPerSecond = ndps; SaveSettings(); }

                    float nmt = DrawSliderWithField("移動検出閾値", _settings.SpeedMovementThreshold, 0.0001f, 0.1f, "F4", tooltip: "腰の動きを「動いている」と判定する移動量の閾値");
                    if (!Mathf.Approximately(nmt, _settings.SpeedMovementThreshold)) { _settings.SpeedMovementThreshold = nmt; SaveSettings(); }

                    float nid = DrawSliderWithField("停止判定遅延(秒)", _settings.SpeedIdleDelay, 0f, 5f, "F2", tooltip: "腰が止まってから速さゲージ減衰を開始するまでの遅延時間（秒）");
                    if (!Mathf.Approximately(nid, _settings.SpeedIdleDelay)) { _settings.SpeedIdleDelay = nid; SaveSettings(); }
                }
            }

            // ━━━ BodyIK ━━━
            _secBodyIkExpanded = GUILayout.Toggle(_secBodyIkExpanded, _secBodyIkExpanded ? "━━━ BodyIK ▼" : "━━━ BodyIK ▲");
            if (_secBodyIkExpanded)
            {
                GUILayout.Label("中央クリックで Move/Rotate 切替");

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent("Gizmoサイズ", "Gizmoの表示サイズ倍率"), GUILayout.Width(96f));
                float gizmoSize = GetGizmoSizeMultiplier();
                float nextGizmoSize = GUILayout.HorizontalSlider(
                    gizmoSize,
                    TransformGizmo.MinSizeMultiplier,
                    TransformGizmo.MaxSizeMultiplier,
                    GUILayout.Width(200f));
                GUILayout.Label(nextGizmoSize.ToString("F2"), GUILayout.Width(40f));
                GUILayout.EndHorizontal();
                if (!Mathf.Approximately(gizmoSize, nextGizmoSize))
                    SetGizmoSizeMultiplier(nextGizmoSize);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("全ON", "全IK部位を有効にする"), GUILayout.Width(90f)))
                    SetAllBodyIK(true);
                if (GUILayout.Button(new GUIContent("全OFF", "全IK部位を無効にする"), GUILayout.Width(90f)))
                    SetAllBodyIK(false);
                if (GUILayout.Button(new GUIContent("IK全リセット", "全IK部位のプロキシ位置をアニメーションの現在ポーズに戻す"), GUILayout.Width(120f)))
                    CompleteResetToAnimationPose();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Gizmo全表示", "全部位のGizmoを個別表示ONにする"), GUILayout.Width(140f)))
                    SetAllGizmoVisible(true);
                if (GUILayout.Button(new GUIContent("Gizmo全非表示", "全部位のGizmoを個別表示OFFにする"), GUILayout.Width(140f)))
                    SetAllGizmoVisible(false);
                GUILayout.EndHorizontal();

                bool autoEnable = GUILayout.Toggle(_settings.AutoEnableAllOnResolve,
                    new GUIContent("HScene解決時に全IK自動ON", "HSceneに入った瞬間に全IK部位を自動でONにする"));
                if (autoEnable != _settings.AutoEnableAllOnResolve)
                {
                    _settings.AutoEnableAllOnResolve = autoEnable;
                    SaveSettings();
                }

                GUILayout.Space(4f);
                GUILayout.Label("各行: IK有効 | 個別表示 | ウェイト | 部位リセット");
                for (int i = 0; i < BIK_TOTAL; i++)
                {
                    if (i == BIK_BODY) continue;
                    DrawBodyIkRow(i, ev);
                }
                DrawFemaleHandPoseSection();
                DrawBodyIkFollowSection();
                DrawFemaleHeadAngleSection();
            }

            // ━━━ ポーズ ━━━
            _secPoseExpanded = GUILayout.Toggle(_secPoseExpanded, _secPoseExpanded ? "━━━ ポーズ ▼" : "━━━ ポーズ ▲");
            if (_secPoseExpanded)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(12f);
                GUILayout.BeginVertical();
                DrawPosePresetSection();
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }

            if (!MaleFeaturesTemporarilySealed)
            {
                // ━━━ 男キャラ ━━━
                _secMaleExpanded = GUILayout.Toggle(_secMaleExpanded, _secMaleExpanded ? "━━━ 男キャラ ▼" : "━━━ 男キャラ ▲");
                if (_secMaleExpanded)
                {
                string maleHmdButtonLabel = _settings.MaleHmdEnabled
                    ? "男頭IK追従: ON"
                    : "男頭IK追従: OFF";
                if (GUILayout.Button(new GUIContent(maleHmdButtonLabel, "男頭IK追従を有効化する（VR-HMD または男頭ギズモを入力源に使う）"), GUILayout.Width(220f)))
                {
                    bool next = !_settings.MaleHmdEnabled;
                    _settings.MaleHmdEnabled = next;
                    if (!next)
                        ClearMaleRefs();
                    else
                        _maleSession.HasMaleHmdBaseline = false;
                    LogInfo("[MaleHMD] " + (next ? "ON" : "OFF"));
                    SaveSettings();
                }

                string maleHeadGizmoLabel = _settings.MaleHeadIkGizmoEnabled
                    ? "男頭IKギズモ操作: ON"
                    : "男頭IKギズモ操作: OFF";
                if (GUILayout.Button(new GUIContent(maleHeadGizmoLabel, "男頭ターゲットをギズモで直接操作する"), GUILayout.Width(220f)))
                {
                    SetMaleHeadIkGizmoEnabled(!_settings.MaleHeadIkGizmoEnabled);
                }

                bool headTargetGizmoVisible = GUILayout.Toggle(
                    _settings.MaleHeadIkGizmoVisible,
                    new GUIContent("頭IKギズモ表示", "頭IKターゲットギズモの表示のみ切り替える（操作ON/OFFとは別）"));
                if (headTargetGizmoVisible != _settings.MaleHeadIkGizmoVisible)
                {
                    _settings.MaleHeadIkGizmoVisible = headTargetGizmoVisible;
                    UpdateMaleHeadTargetGizmoVisibility();
                    SaveSettings();
                }

                string maleHmdParamsLabel = _maleHmdParamsExpanded ? "男HMD パラメータ ▼" : "男HMD パラメータ ▲";
                _maleHmdParamsExpanded = GUILayout.Toggle(_maleHmdParamsExpanded, maleHmdParamsLabel);
                if (_maleHmdParamsExpanded)
                {
                    GUILayout.Label("入力源: " + (_settings.MaleHeadIkGizmoEnabled ? "男頭ギズモ" : "VR-HMD"));
                    string maleStatus = _maleSession.TargetMaleCha != null
                        ? ("男キャラ: " + (_maleSession.TargetMaleCha.name ?? "(unnamed)"))
                        : "男キャラ: 未検出";
                    GUILayout.Label(maleStatus);
                    GUILayout.Label("waist: " + (_maleSession.MaleWaistBone != null ? "OK" : "未検出")
                        + "  spine01: " + (_maleSession.MaleSpine1Bone != null ? "OK" : "未検出")
                        + "  spine02: " + (_maleSession.MaleSpine2Bone != null ? "OK" : "未検出"));
                    GUILayout.Label("spine03: " + (_maleSession.MaleSpineBone != null ? "OK" : "未検出")
                        + "  neck: " + (_maleSession.MaleNeckBone != null ? "OK" : "未検出")
                        + "  head: " + (_maleSession.MaleHeadBone != null ? "OK" : "未検出"));
                    GUILayout.Label("headBone selected: " + (_maleSession.MaleHeadBoneName ?? "未検出"));
                    GUILayout.Label("leftHand: " + (_maleSession.MaleLeftHandBone != null ? "OK" : "未検出")
                        + "  rightHand: " + (_maleSession.MaleRightHandBone != null ? "OK" : "未検出"));

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("頭ボーン", GUILayout.Width(60f));
                    int selectedHeadBone = Mathf.Clamp((int)_settings.MaleHeadBoneSelection, 0, MaleHeadBoneSelectionLabels.Length - 1);
                    for (int i = 0; i < MaleHeadBoneSelectionLabels.Length; i++)
                    {
                        bool selected = GUILayout.Toggle(selectedHeadBone == i, MaleHeadBoneSelectionLabels[i], GUI.skin.button, GUILayout.Width(90f));
                        if (selected && selectedHeadBone != i)
                        {
                            SetMaleHeadBoneSelection((MaleHeadBoneSelectionMode)i);
                            selectedHeadBone = i;
                        }
                    }
                    GUILayout.EndHorizontal();

                    if (_settings.MaleHeadIkGizmoEnabled)
                    {
                        if (GUILayout.Button(new GUIContent("男頭ターゲットを現在頭へ", "男頭ギズモターゲットを現在の頭位置へスナップする"), GUILayout.Width(200f)))
                        {
                            SnapMaleHeadTargetToCurrentHead();
                            _maleSession.HasMaleHmdBaseline = false;
                            _maleSession.HasMaleHmdLocalDelta = false;
                            LogInfo("[MaleHMD] head target snapped to current head");
                        }
                    }

                    bool headIk = GUILayout.Toggle(_settings.MaleHeadIkEnabled,
                        new GUIContent("頭IKを使う", "頭をIK的にHMD位置へ寄せてから回転追従を行う"));
                    if (headIk != _settings.MaleHeadIkEnabled)
                    {
                        _settings.MaleHeadIkEnabled = headIk;
                        _maleSession.HasMaleHmdBaseline = false;
                        SaveSettings();
                    }

                    float nmhrw = DrawSliderWithField("頭回転Weight", _settings.MaleHmdHeadRotationWeight, 0f, 1f, "F2",
                        tooltip: "男キャラ頭部のHMD回転追従ウェイト（0=無効, 1=完全追従）");
                    if (!Mathf.Approximately(nmhrw, _settings.MaleHmdHeadRotationWeight)) { _settings.MaleHmdHeadRotationWeight = nmhrw; SaveSettings(); }

                    float nhpw = DrawSliderWithField("頭IK位置Weight", _settings.MaleHeadIkPositionWeight, 0f, 1f, "F2",
                        tooltip: "頭をHMD位置へ寄せる強さ（0=無効, 1=強い）");
                    if (!Mathf.Approximately(nhpw, _settings.MaleHeadIkPositionWeight)) { _settings.MaleHeadIkPositionWeight = nhpw; SaveSettings(); }

                    float nhnw = DrawSliderWithField("頭IK首寄与", _settings.MaleHeadIkNeckWeight, 0f, 1f, "F2",
                        tooltip: "頭IK位置追従で首ボーンを使う割合（0=頭のみ, 1=首を強く使う）");
                    if (!Mathf.Approximately(nhnw, _settings.MaleHeadIkNeckWeight)) { _settings.MaleHeadIkNeckWeight = nhnw; SaveSettings(); }

                    float nIter = DrawSliderWithField("頭IK反復", _settings.MaleHeadIkSolveIterations, 1f, 8f, "F0",
                        tooltip: "頭IKの反復回数（高いほど目標へ寄るが、過剰だと不自然になりやすい）");
                    int iterInt = Mathf.Clamp(Mathf.RoundToInt(nIter), 1, 8);
                    if (iterInt != _settings.MaleHeadIkSolveIterations)
                    {
                        _settings.MaleHeadIkSolveIterations = iterInt;
                        SaveSettings();
                    }

                    float nearDist = DrawSliderWithField("頭IK近距離閾値", _settings.MaleHeadIkNearDistance, 0.05f, 2f, "F2",
                        tooltip: "頭ターゲットが近い判定の距離。近いほど背中を丸める寄りになる");
                    float farDist = DrawSliderWithField("頭IK遠距離閾値", _settings.MaleHeadIkFarDistance, 0.05f, 2f, "F2",
                        tooltip: "頭ターゲットが遠い判定の距離。遠いほど腰主導で直線化する寄りになる");
                    bool curveChanged = false;
                    if (!Mathf.Approximately(nearDist, _settings.MaleHeadIkNearDistance))
                    {
                        _settings.MaleHeadIkNearDistance = nearDist;
                        curveChanged = true;
                    }
                    if (!Mathf.Approximately(farDist, _settings.MaleHeadIkFarDistance))
                    {
                        _settings.MaleHeadIkFarDistance = farDist;
                        curveChanged = true;
                    }
                    if (_settings.MaleHeadIkFarDistance < _settings.MaleHeadIkNearDistance + 0.01f)
                    {
                        _settings.MaleHeadIkFarDistance = _settings.MaleHeadIkNearDistance + 0.01f;
                        curveChanged = true;
                    }
                    if (curveChanged)
                        SaveSettings();

                    bool neckShoulderFollow = GUILayout.Toggle(_settings.MaleNeckShoulderFollowEnabled,
                        new GUIContent("首から両肩IKを追従", "首の姿勢に合わせて左右肩IKターゲットを追従させる"));
                    if (neckShoulderFollow != _settings.MaleNeckShoulderFollowEnabled)
                    {
                        _settings.MaleNeckShoulderFollowEnabled = neckShoulderFollow;
                        _maleSession.HasMaleNeckShoulderPrevPose = false;
                        SaveSettings();
                    }

                    float neckShoulderPos = DrawSliderWithField("首->肩 位置追従", _settings.MaleNeckShoulderFollowPositionWeight, 0f, 1f, "F2",
                        tooltip: "首から左右肩IKへの位置追従強度");
                    if (!Mathf.Approximately(neckShoulderPos, _settings.MaleNeckShoulderFollowPositionWeight))
                    {
                        _settings.MaleNeckShoulderFollowPositionWeight = neckShoulderPos;
                        SaveSettings();
                    }

                    float neckShoulderRot = DrawSliderWithField("首->肩 回転追従", _settings.MaleNeckShoulderFollowRotationWeight, 0f, 1f, "F2",
                        tooltip: "首から左右肩IKへの回転追従強度");
                    if (!Mathf.Approximately(neckShoulderRot, _settings.MaleNeckShoulderFollowRotationWeight))
                    {
                        _settings.MaleNeckShoulderFollowRotationWeight = neckShoulderRot;
                        SaveSettings();
                    }

                    bool bodyPullEnabled = GUILayout.Toggle(_settings.MaleHeadIkBodyPullEnabled,
                        new GUIContent("腰中央を頭で引っぱる", "頭ターゲットとの差分を使って、男の腰中央(Body)を自動追従させる"));
                    if (bodyPullEnabled != _settings.MaleHeadIkBodyPullEnabled)
                    {
                        _settings.MaleHeadIkBodyPullEnabled = bodyPullEnabled;
                        SaveSettings();
                    }

                    float bodyPullPos = DrawSliderWithField("腰中央 位置追従", _settings.MaleHeadIkBodyPullPositionWeight, 0f, 1f, "F2",
                        tooltip: "頭ターゲットに対する腰中央の位置追従量（0=無効, 1=強い）");
                    if (!Mathf.Approximately(bodyPullPos, _settings.MaleHeadIkBodyPullPositionWeight))
                    {
                        _settings.MaleHeadIkBodyPullPositionWeight = bodyPullPos;
                        SaveSettings();
                    }

                    float bodyPullRot = DrawSliderWithField("腰中央 回転追従", _settings.MaleHeadIkBodyPullRotationWeight, 0f, 1f, "F2",
                        tooltip: "頭ターゲットに対する腰中央の回転追従量（0=無効, 1=強い）");
                    if (!Mathf.Approximately(bodyPullRot, _settings.MaleHeadIkBodyPullRotationWeight))
                    {
                        _settings.MaleHeadIkBodyPullRotationWeight = bodyPullRot;
                        SaveSettings();
                    }

                    float bodyPullMaxStep = DrawSliderWithField("腰中央 1frame移動上限", _settings.MaleHeadIkBodyPullMaxStep, 0f, 0.5f, "F3",
                        tooltip: "頭差分で腰中央を動かすときの1フレーム最大移動量");
                    if (!Mathf.Approximately(bodyPullMaxStep, _settings.MaleHeadIkBodyPullMaxStep))
                    {
                        _settings.MaleHeadIkBodyPullMaxStep = bodyPullMaxStep;
                        SaveSettings();
                    }

                    bool compensateBodyOffset = GUILayout.Toggle(_settings.MaleHeadIkCompensateBodyOffset,
                        new GUIContent("腰中央移動を頭目標へ反映", "腰中央(Body)を動かした差分を頭IK目標へ加算して体幹の圧縮を減らす"));
                    if (compensateBodyOffset != _settings.MaleHeadIkCompensateBodyOffset)
                    {
                        _settings.MaleHeadIkCompensateBodyOffset = compensateBodyOffset;
                        SaveSettings();
                    }

                    float compensateBodyWeight = DrawSliderWithField("頭目標への腰差分比率", _settings.MaleHeadIkCompensateBodyOffsetWeight, 0f, 2f, "F2",
                        tooltip: "腰中央の移動差分を頭目標へどれだけ反映するか（1.0=等倍）");
                    if (!Mathf.Approximately(compensateBodyWeight, _settings.MaleHeadIkCompensateBodyOffsetWeight))
                    {
                        _settings.MaleHeadIkCompensateBodyOffsetWeight = compensateBodyWeight;
                        SaveSettings();
                    }

                    float compensateBodyMax = DrawSliderWithField("頭目標への腰差分上限", _settings.MaleHeadIkCompensateBodyOffsetMax, 0f, 3f, "F2",
                        tooltip: "腰差分を頭目標へ反映する最大距離");
                    if (!Mathf.Approximately(compensateBodyMax, _settings.MaleHeadIkCompensateBodyOffsetMax))
                    {
                        _settings.MaleHeadIkCompensateBodyOffsetMax = compensateBodyMax;
                        SaveSettings();
                    }

                    bool spineBodyOffset = GUILayout.Toggle(_settings.MaleHeadIkSpineBodyOffsetEnabled,
                        new GUIContent("腰差分をspine位置へ分配", "腰中央(Body)の移動差分をspine01/02/03の位置へ分配して体幹圧縮を減らす"));
                    if (spineBodyOffset != _settings.MaleHeadIkSpineBodyOffsetEnabled)
                    {
                        _settings.MaleHeadIkSpineBodyOffsetEnabled = spineBodyOffset;
                        SaveSettings();
                    }

                    float spine1Pos = DrawSliderWithField("spine01 位置寄与", _settings.MaleHeadIkSpineBodyOffsetSpine1Weight, 0f, 1f, "F2",
                        tooltip: "腰差分をspine01位置へ反映する比率");
                    if (!Mathf.Approximately(spine1Pos, _settings.MaleHeadIkSpineBodyOffsetSpine1Weight))
                    {
                        _settings.MaleHeadIkSpineBodyOffsetSpine1Weight = spine1Pos;
                        SaveSettings();
                    }

                    float spine2Pos = DrawSliderWithField("spine02 位置寄与", _settings.MaleHeadIkSpineBodyOffsetSpine2Weight, 0f, 1f, "F2",
                        tooltip: "腰差分をspine02位置へ反映する比率");
                    if (!Mathf.Approximately(spine2Pos, _settings.MaleHeadIkSpineBodyOffsetSpine2Weight))
                    {
                        _settings.MaleHeadIkSpineBodyOffsetSpine2Weight = spine2Pos;
                        SaveSettings();
                    }

                    float spine3Pos = DrawSliderWithField("spine03 位置寄与", _settings.MaleHeadIkSpineBodyOffsetSpine3Weight, 0f, 1f, "F2",
                        tooltip: "腰差分をspine03位置へ反映する比率");
                    if (!Mathf.Approximately(spine3Pos, _settings.MaleHeadIkSpineBodyOffsetSpine3Weight))
                    {
                        _settings.MaleHeadIkSpineBodyOffsetSpine3Weight = spine3Pos;
                        SaveSettings();
                    }

                    float spinePosMax = DrawSliderWithField("spine位置差分上限", _settings.MaleHeadIkSpineBodyOffsetMax, 0f, 2f, "F2",
                        tooltip: "腰差分をspine位置へ反映するときの最大距離");
                    if (!Mathf.Approximately(spinePosMax, _settings.MaleHeadIkSpineBodyOffsetMax))
                    {
                        _settings.MaleHeadIkSpineBodyOffsetMax = spinePosMax;
                        SaveSettings();
                    }

                    bool maleDebug = GUILayout.Toggle(_settings.MaleIkDebugVisible,
                        new GUIContent("男IKデバッグ表示", "男IKチェーン骨のデバッグキューブを表示する"));
                    if (maleDebug != _settings.MaleIkDebugVisible)
                    {
                        _settings.MaleIkDebugVisible = maleDebug;
                        SaveSettings();
                    }

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(new GUIContent("上半身IK表示", "腰/spine01/spine02/spine03/首/頭 のデバッグ表示をON"), GUILayout.Width(110f)))
                    {
                        if (!_settings.MaleIkDebugVisible)
                        {
                            _settings.MaleIkDebugVisible = true;
                            SaveSettings();
                        }
                    }
                    if (GUILayout.Button(new GUIContent("上半身IK非表示", "腰/spine01/spine02/spine03/首/頭 のデバッグ表示をOFF"), GUILayout.Width(110f)))
                    {
                        if (_settings.MaleIkDebugVisible)
                        {
                            _settings.MaleIkDebugVisible = false;
                            SaveSettings();
                        }
                    }
                    GUILayout.EndHorizontal();

                    bool maleDiagLog = GUILayout.Toggle(_settings.MaleHmdDiagnosticLog,
                        new GUIContent("男HMD診断ログ", "男HMD追従の入力/適用/結果をログへ出力する"));
                    if (maleDiagLog != _settings.MaleHmdDiagnosticLog)
                    {
                        _settings.MaleHmdDiagnosticLog = maleDiagLog;
                        SaveSettings();
                    }

                    float maleDiagInterval = DrawSliderWithField("男HMDログ間隔", _settings.MaleHmdDiagnosticLogInterval, 0.05f, 2f, "F2",
                        tooltip: "男HMD診断ログの出力間隔（秒）");
                    if (!Mathf.Approximately(maleDiagInterval, _settings.MaleHmdDiagnosticLogInterval))
                    {
                        _settings.MaleHmdDiagnosticLogInterval = maleDiagInterval;
                        SaveSettings();
                    }

                    GUILayout.Label("男IK 有効 / Weight");
                    for (int i = 0; i < MALE_IK_BONE_TOTAL; i++)
                        DrawMaleIkRow(i);

                    GUILayout.Space(6f);
                    GUILayout.Label("男全身操作（手/足/肩/腰/肘/膝）");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(new GUIContent("Gizmo全表示", "男全身操作ギズモを一括表示"), GUILayout.Width(110f)))
                        SetAllMaleControlGizmoVisible(true);
                    if (GUILayout.Button(new GUIContent("Gizmo全非表示", "男全身操作ギズモを一括非表示"), GUILayout.Width(110f)))
                        SetAllMaleControlGizmoVisible(false);
                    GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(new GUIContent("全有効", "男全身操作を一括で有効化する"), GUILayout.Width(90f)))
                        SetAllMaleControlEnabled(true);
                    if (GUILayout.Button(new GUIContent("全無効", "男全身操作を一括で無効化する"), GUILayout.Width(90f)))
                        SetAllMaleControlEnabled(false);
                    if (GUILayout.Button(new GUIContent("全リセット", "男全身操作の全プロキシを現在アニメ姿勢へ戻す"), GUILayout.Width(90f)))
                        ResetAllMaleControlPartsToAnimationPose();
                    GUILayout.EndHorizontal();
                    for (int i = 0; i < BIK_TOTAL; i++)
                        DrawMaleControlRow(i);
                    DrawMalePosePresetSection();

                    float nmps = DrawSliderWithField("位置スケール", _settings.MaleHmdPositionScale, 0f, 5f, "F2",
                        tooltip: "男キャラのHMD位置追従スケール");
                    if (!Mathf.Approximately(nmps, _settings.MaleHmdPositionScale)) { _settings.MaleHmdPositionScale = nmps; SaveSettings(); }

                    bool useLocalDelta = GUILayout.Toggle(_settings.MaleHmdUseLocalDelta,
                        new GUIContent("ローカル差分で位置追従", "男キャラ基準座標でHMD位置差分を計算してから追従する"));
                    if (useLocalDelta != _settings.MaleHmdUseLocalDelta)
                    {
                        _settings.MaleHmdUseLocalDelta = useLocalDelta;
                        _maleSession.HasMaleHmdBaseline = false;
                        _maleSession.HasMaleHmdLocalDelta = false;
                        SaveSettings();
                    }

                    bool swapHorizontal = GUILayout.Toggle(_settings.MaleHmdSwapHorizontalAxes,
                        new GUIContent("水平X/Zを入れ替える", "前後と左右の追従軸を入れ替える"));
                    if (swapHorizontal != _settings.MaleHmdSwapHorizontalAxes)
                    {
                        _settings.MaleHmdSwapHorizontalAxes = swapHorizontal;
                        _maleSession.HasMaleHmdBaseline = false;
                        _maleSession.HasMaleHmdLocalDelta = false;
                        SaveSettings();
                    }

                    GUILayout.BeginHorizontal();
                    bool invertX = GUILayout.Toggle(_settings.MaleHmdInvertHorizontalX, "水平X反転", GUILayout.Width(100f));
                    bool invertZ = GUILayout.Toggle(_settings.MaleHmdInvertHorizontalZ, "水平Z反転", GUILayout.Width(100f));
                    GUILayout.EndHorizontal();
                    if (invertX != _settings.MaleHmdInvertHorizontalX || invertZ != _settings.MaleHmdInvertHorizontalZ)
                    {
                        _settings.MaleHmdInvertHorizontalX = invertX;
                        _settings.MaleHmdInvertHorizontalZ = invertZ;
                        _maleSession.HasMaleHmdBaseline = false;
                        _maleSession.HasMaleHmdLocalDelta = false;
                        SaveSettings();
                    }

                    float localResponse = DrawSliderWithField("位置追従応答", _settings.MaleHmdLocalDeltaSmoothing, 0f, 1f, "F2",
                        tooltip: "ローカル差分の平滑化応答（0=強平滑, 1=ほぼ生値）");
                    if (!Mathf.Approximately(localResponse, _settings.MaleHmdLocalDeltaSmoothing))
                    {
                        _settings.MaleHmdLocalDeltaSmoothing = localResponse;
                        SaveSettings();
                    }

                    float handSnap = DrawSliderWithField("男手追従スナップ距離", _settings.MaleHandFollowSnapDistance, 0.02f, 0.8f, "F2",
                        tooltip: "男手の近傍追従セット時に女ボーンを選ぶ最大距離");
                    if (!Mathf.Approximately(handSnap, _settings.MaleHandFollowSnapDistance))
                    {
                        _settings.MaleHandFollowSnapDistance = handSnap;
                        SaveSettings();
                    }

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(new GUIContent("左手 近傍追従セット", "左手の現在位置に近い女ボーンへ追従を設定する"), GUILayout.Width(140f)))
                    {
                        if (TrySetMaleHandFollow(left: true))
                        {
                            SaveSettings();
                            LogInfo("[MaleHandFollow] left set: " + (_maleSession.MaleLeftHandFollowBone != null ? _maleSession.MaleLeftHandFollowBone.name : "(null)"));
                        }
                        else
                        {
                            LogWarn("[MaleHandFollow] left set failed: target not found");
                        }
                    }
                    if (GUILayout.Button(new GUIContent("左手 解除", "左手の女ボーン追従を解除する"), GUILayout.Width(80f)))
                    {
                        ClearMaleHandFollow(left: true);
                        SaveSettings();
                    }
                    GUILayout.Label(_maleSession.MaleLeftHandFollowBone != null ? _maleSession.MaleLeftHandFollowBone.name : "-");
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button(new GUIContent("右手 近傍追従セット", "右手の現在位置に近い女ボーンへ追従を設定する"), GUILayout.Width(140f)))
                    {
                        if (TrySetMaleHandFollow(left: false))
                        {
                            SaveSettings();
                            LogInfo("[MaleHandFollow] right set: " + (_maleSession.MaleRightHandFollowBone != null ? _maleSession.MaleRightHandFollowBone.name : "(null)"));
                        }
                        else
                        {
                            LogWarn("[MaleHandFollow] right set failed: target not found");
                        }
                    }
                    if (GUILayout.Button(new GUIContent("右手 解除", "右手の女ボーン追従を解除する"), GUILayout.Width(80f)))
                    {
                        ClearMaleHandFollow(left: false);
                        SaveSettings();
                    }
                    GUILayout.Label(_maleSession.MaleRightHandFollowBone != null ? _maleSession.MaleRightHandFollowBone.name : "-");
                    GUILayout.EndHorizontal();

                    if (GUILayout.Button(new GUIContent("ベースライン再取得", "男キャラのHMD追従基準位置を現在の位置に再設定する"), GUILayout.Width(160f)))
                    {
                        _maleSession.HasMaleHmdBaseline = false;
                        LogInfo("[MaleHMD] baseline reset by UI");
                    }
                }
            }
            }

        }

        private void DrawMaleIkRow(int idx)
        {
            if (_settings == null || _settings.MaleIkEnabled == null || _settings.MaleIkWeights == null)
                return;
            if (idx < 0 || idx >= _settings.MaleIkEnabled.Length || idx >= _settings.MaleIkWeights.Length || idx >= MALE_IK_BONE_TOTAL)
                return;

            GUILayout.BeginHorizontal();
            bool enabled = GUILayout.Toggle(_settings.MaleIkEnabled[idx], MaleIkLabels[idx], GUILayout.Width(88f));
            if (enabled != _settings.MaleIkEnabled[idx])
            {
                _settings.MaleIkEnabled[idx] = enabled;
                SaveSettings();
            }

            float weight = _settings.MaleIkWeights[idx];
            float nextWeight = GUILayout.HorizontalSlider(weight, 0f, 1f, GUILayout.Width(140f));
            GUILayout.Label(nextWeight.ToString("F2"), GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(nextWeight, weight))
            {
                _settings.MaleIkWeights[idx] = nextWeight;
                SaveSettings();
            }
        }

        private void DrawMaleControlRow(int idx)
        {
            if (_settings == null || _settings.MaleControlEnabled == null || _settings.MaleControlWeights == null || _settings.MaleControlGizmoVisible == null)
                return;
            if (idx < 0 || idx >= BIK_TOTAL
                || idx >= _settings.MaleControlEnabled.Length
                || idx >= _settings.MaleControlWeights.Length
                || idx >= _settings.MaleControlGizmoVisible.Length)
                return;

            GUILayout.BeginHorizontal();

            bool enabled = GUILayout.Toggle(GetMaleControlEnabled(idx), BIK_Labels[idx], GUILayout.Width(120f));
            if (enabled != GetMaleControlEnabled(idx))
                SetMaleControlEnabled(idx, enabled);

            bool show = GUILayout.Toggle(GetMaleControlGizmoVisible(idx), "表示", GUILayout.Width(52f));
            if (show != GetMaleControlGizmoVisible(idx))
                SetMaleControlGizmoVisible(idx, show);

            float weight = GetMaleControlWeight(idx);
            float nextWeight = GUILayout.HorizontalSlider(weight, 0f, 1f, GUILayout.Width(140f));
            GUILayout.Label(nextWeight.ToString("F2"), GUILayout.Width(40f));
            if (!Mathf.Approximately(nextWeight, weight))
                SetMaleControlWeight(idx, nextWeight);

            if (GUILayout.Button("戻す", GUILayout.Width(58f)))
                ResetMaleControlPartToAnimationPose(idx);

            Transform bone = GetMaleControlBoneByIndex(idx);
            GUILayout.Label(bone != null ? "OK" : "未検出", GUILayout.Width(56f));

            GUILayout.EndHorizontal();
        }

        private void DrawTooltip()
        {
            string tip = GUI.tooltip;
            if (string.IsNullOrEmpty(tip))
                return;

            GUIContent content = new GUIContent(tip);
            GUIStyle style = new GUIStyle(GUI.skin.box);
            style.wordWrap = true;
            style.alignment = TextAnchor.UpperLeft;

            float maxWidth = Mathf.Min(_windowRect.width - 8f, 300f);
            float height = style.CalcHeight(content, maxWidth);

            Vector2 mouse = Event.current.mousePosition;
            float x = Mathf.Clamp(mouse.x + 12f, 2f, _windowRect.width - maxWidth - 4f);
            float y = Mathf.Clamp(mouse.y + 18f, 2f, _windowRect.height - height - 4f);

            GUI.Box(new Rect(x, y, maxWidth, height), content, style);
        }

        private void DrawBodyIkRow(int idx, Event ev)
        {
            const float rowHeight = 22f;
            const float gap = 6f;
            const float minIkToggleWidth = 150f;
            const float maxIkToggleWidth = 220f;
            const float gizmoToggleWidth = 58f;
            const float valueWidth = 46f;
            const float resetButtonWidth = 58f;
            const float minSliderWidth = 100f;

            Rect rowRect = GUILayoutUtility.GetRect(1f, rowHeight, GUILayout.ExpandWidth(true));
            float ikToggleWidth = Mathf.Clamp(rowRect.width * 0.45f, minIkToggleWidth, maxIkToggleWidth);
            float sliderWidth = rowRect.width - ikToggleWidth - gizmoToggleWidth - valueWidth - resetButtonWidth - (gap * 4f);

            if (sliderWidth < minSliderWidth)
            {
                float deficit = minSliderWidth - sliderWidth;
                ikToggleWidth = Mathf.Max(minIkToggleWidth, ikToggleWidth - deficit);
                sliderWidth = rowRect.width - ikToggleWidth - gizmoToggleWidth - valueWidth - resetButtonWidth - (gap * 4f);
            }
            sliderWidth = Mathf.Max(minSliderWidth, sliderWidth);

            Rect ikRect = new Rect(rowRect.x, rowRect.y + 1f, ikToggleWidth, rowHeight);
            Rect gizmoRect = new Rect(ikRect.xMax + gap, rowRect.y + 1f, gizmoToggleWidth, rowHeight);
            Rect sliderRect = new Rect(gizmoRect.xMax + gap, rowRect.y + 4f, sliderWidth, 16f);
            Rect valueRect = new Rect(sliderRect.xMax + gap, rowRect.y + 1f, valueWidth, rowHeight);
            Rect resetRect = new Rect(valueRect.xMax + gap, rowRect.y + 1f, resetButtonWidth, rowHeight);

            GUIStyle ikToggleStyle = idx == BIK_BODY
                ? new GUIStyle(GUI.skin.toggle) { fontStyle = FontStyle.Bold }
                : GUI.skin.toggle;
            bool wanted = GUI.Toggle(ikRect, _bikWant[idx], BIK_Labels[idx], ikToggleStyle);
            if (wanted != _bikWant[idx])
                SetBodyIK(idx, wanted, reason: "ui-row");

            bool gizmoVisible = GetGizmoVisible(idx);
            bool nextGizmoVisible = GUI.Toggle(gizmoRect, gizmoVisible, "表示");
            if (nextGizmoVisible != gizmoVisible)
                SetGizmoVisible(idx, nextGizmoVisible);

            float current = GetBodyIKWeight(idx);
            float next = GUI.HorizontalSlider(sliderRect, current, 0f, 1f);
            GUI.Label(valueRect, next.ToString("F2"));
            if (!Mathf.Approximately(current, next))
                SetBodyIKWeight(idx, next);

            if (GUI.Button(resetRect, "戻す"))
                ResetBodyIKPartToAnimationPose(idx);

            DrawBodyIkFollowRow(idx);
        }

        private void HandleSliderDragFlag(Event ev)
        {
            if (ev == null)
                return;

            Rect bodyArea = new Rect(0f, 20f, _windowRect.width - 18f, _windowRect.height - 20f);
            if (ev.rawType == EventType.MouseDown && ev.button == 0 && bodyArea.Contains(ev.mousePosition))
            {
                SetSliderDragging(true, -1, "slider-area-down");
            }
            else if (ev.rawType == EventType.MouseUp && ev.button == 0 && _sliderDragging)
            {
                SetSliderDragging(false, -1, "slider-area-up");
            }
        }

        private void HandleScrollDragFlag(Event ev)
        {
            if (ev == null)
                return;

            Rect scrollbarCol = new Rect(_windowRect.width - 18f, 20f, 18f, _windowRect.height - 20f);
            if (ev.rawType == EventType.MouseDown && ev.button == 0 && scrollbarCol.Contains(ev.mousePosition))
            {
                SetScrollDragging(true, "scroll-down");
            }
            else if (ev.rawType == EventType.MouseUp && ev.button == 0 && _scrollDragging)
            {
                SetScrollDragging(false, "scroll-up");
            }
        }

        private void ClampWindowRectToScreen()
        {
            float maxW = Mathf.Max(360f, Screen.width - 8f);
            float maxH = Mathf.Max(260f, Screen.height - 8f);
            Rect rect = _windowRect;
            rect.width = Mathf.Clamp(rect.width, 360f, maxW);
            rect.height = Mathf.Clamp(rect.height, 260f, maxH);
            rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, Screen.width - rect.width));
            rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, Screen.height - rect.height));
            _windowRect = rect;
        }

        private void HandleWindowScrollWheel()
        {
            Event ev = Event.current;
            if (ev == null || ev.type != EventType.ScrollWheel)
                return;

            Rect scrollArea = new Rect(0f, 20f, _windowRect.width, _windowRect.height - 20f);
            if (!scrollArea.Contains(ev.mousePosition))
                return;

            Vector2 scroll = _scroll;
            scroll.y = Mathf.Max(0f, scroll.y + ev.delta.y * 24f);
            _scroll = scroll;
            ev.Use();
        }

        private bool _secStatusExpanded { get => _uiState.StatusExpanded; set => _uiState.StatusExpanded = value; }
        private bool _secVrExpanded { get => _uiState.VrExpanded; set => _uiState.VrExpanded = value; }
        private bool _secHipExpanded { get => _uiState.HipExpanded; set => _uiState.HipExpanded = value; }
        private bool _secBodyIkExpanded { get => _uiState.BodyIkExpanded; set => _uiState.BodyIkExpanded = value; }
        private bool _secPoseExpanded { get => _uiState.PoseExpanded; set => _uiState.PoseExpanded = value; }
        private bool _secMaleExpanded { get => _uiState.MaleExpanded; set => _uiState.MaleExpanded = value; }
        private bool _hSceneParamsExpanded { get => _uiState.HSceneParametersExpanded; set => _uiState.HSceneParametersExpanded = value; }
        private bool _maleHmdParamsExpanded { get => _uiState.MaleHmdParametersExpanded; set => _uiState.MaleHmdParametersExpanded = value; }

        // label幅・スライダー幅・テキスト幅を指定。戻り値が変化した場合は新しい値。
        private float DrawSliderWithField(string label, float value, float min, float max,
            string format = "F2", float labelWidth = 130f, float sliderWidth = 140f, string tooltip = null)
        {
            GUILayout.BeginHorizontal();
            if (string.IsNullOrEmpty(tooltip))
                GUILayout.Label(label, GUILayout.Width(labelWidth));
            else
                GUILayout.Label(new GUIContent(label, tooltip), GUILayout.Width(labelWidth));
            float next = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(sliderWidth));
            string txt = GUILayout.TextField(value.ToString(format), GUILayout.Width(52f));
            GUILayout.EndHorizontal();
            if (float.TryParse(txt, out float parsed))
            {
                float clamped = Mathf.Clamp(parsed, min, max);
                if (!Mathf.Approximately(clamped, value))
                    return clamped;
            }
            return next;
        }

        private void HandleWindowDragFlag(Event ev)
        {
            if (ev == null)
                return;

            Rect titleBar = new Rect(0f, 0f, _windowRect.width, 20f);
            if (ev.type == EventType.MouseDown && ev.button == 0 && titleBar.Contains(ev.mousePosition))
            {
                SetWindowDragging(true, "window-title-down");
            }
            else if (ev.rawType == EventType.MouseUp && ev.button == 0)
            {
                SetWindowDragging(false, "window-title-up");
            }
        }
    }
}
