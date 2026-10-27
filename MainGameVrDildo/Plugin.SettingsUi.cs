using System;
using System.Globalization;
using UnityEngine;

namespace MainGameVrDildo
{
    public sealed partial class Plugin
    {
        private void OnGUI()
        {
            DrawOralSubtitleFallback();
            if (!_showSettings) return;
            int previousHotControl = GUIUtility.hotControl;
            try { _window = GUILayout.Window(0x56524444, _window, DrawSettingsWindow, "VR バイブ 設定"); }
            finally
            {
                int currentHotControl = GUIUtility.hotControl;
                if (currentHotControl != 0 && currentHotControl != previousHotControl)
                    _settingsHotControl = currentHotControl;
                if (currentHotControl == 0) _settingsHotControl = 0;
                SyncStandaloneInputCapture();
            }
        }

        private void DrawSettingsWindow(int id)
        {
            DrawSettingsHeader(true);
            _scroll = GUILayout.BeginScrollView(_scroll);
            DrawDesktopGizmoControls();
            DrawSettingsControls();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, _window.width, 22));
        }

        private void DrawTestTab()
        {
            DrawSettingsHeader(false);
            DrawDesktopGizmoControls();
            DrawSettingsControls();
        }

        private void DrawSettingsHeader(bool showClose)
        {
            if (GUILayout.Button(_model == null ? "呼び出す" : "削除する")) ToggleSpawn();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("保存")) SaveSettings();
            if (GUILayout.Button("再読込")) { LoadSettings(); RebuildColliders(); _edits.Clear(); }
            if (GUILayout.Button("初期値")) { _settings = DildoSettings.CreateDefault(); RebuildColliders(); _edits.Clear(); }
            if (showClose && GUILayout.Button("閉じる")) _showSettings = false;
            GUILayout.EndHorizontal();
        }

        private void DrawSettingsControls()
        {
            Toggle("有効", ref _settings.Enabled);
            Toggle("詳細ログ", ref _settings.DiagnosticLogEnabled);
            Toggle("フリーHで自動呼出", ref _settings.AutoSpawnOnH);
            Toggle("近い入口へ自動追従", ref _settings.AutoTarget);
            Toggle("口ターゲットを使う", ref _settings.OralTargetEnabled);
            Toggle("VRで掴む", ref _settings.EnableVrGrab);
            Toggle("コライダー表示", ref _settings.ShowColliders);
            Float("モデル倍率", "scale", ref _settings.ModelScale, 0.25f, 3f);
            Float("呼出距離", "spawn", ref _settings.SpawnDistance, 0.1f, 3f);
            Float("掴む距離", "gripradius", ref _settings.GripRadius, 0.02f, 0.5f);
            Section("持ち手位置と向き (モデル座標)");
            Float("位置 X", "gx", ref _settings.GripOffsetX, -1f, 1f);
            Float("位置 Y", "gy", ref _settings.GripOffsetY, -1f, 1f);
            Float("位置 Z", "gz", ref _settings.GripOffsetZ, -1f, 1f);
            Float("回転 X", "grx", ref _settings.GripRotationX, -180f, 180f);
            Float("回転 Y", "gry", ref _settings.GripRotationY, -180f, 180f);
            Float("回転 Z", "grz", ref _settings.GripRotationZ, -180f, 180f);
            Section("ターゲットと曲げ");
            Float("追従開始距離", "engage", ref _settings.EngageDistance, 0.02f, 0.5f);
            Float("追従解除距離", "release", ref _settings.ReleaseDistance, 0.02f, 0.8f);
            Float("入口 X", "ex", ref _settings.EntryOffsetX, -0.3f, 0.3f);
            Float("入口 Y", "ey", ref _settings.EntryOffsetY, -0.3f, 0.3f);
            Float("入口 Z", "ez", ref _settings.EntryOffsetZ, -0.3f, 0.3f);
            Float("奥 X", "dx", ref _settings.DeepOffsetX, -0.3f, 0.3f);
            Float("奥 Y", "dy", ref _settings.DeepOffsetY, -0.3f, 0.3f);
            Float("奥 Z", "dz", ref _settings.DeepOffsetZ, -0.3f, 0.3f);
            Section("口ターゲットと開口");
            Float("口入口 X", "mex", ref _settings.MouthEntryOffsetX, -0.3f, 0.3f);
            Float("口入口 Y", "mey", ref _settings.MouthEntryOffsetY, -0.3f, 0.3f);
            Float("口入口 Z", "mez", ref _settings.MouthEntryOffsetZ, -0.3f, 0.3f);
            Float("口奥 X", "mdx", ref _settings.MouthDeepOffsetX, -0.3f, 0.3f);
            Float("口奥 Y", "mdy", ref _settings.MouthDeepOffsetY, -0.3f, 0.3f);
            Float("口奥 Z", "mdz", ref _settings.MouthDeepOffsetZ, -0.3f, 0.3f);
            GUILayout.Label("距離はバイブ先端から口入口まで。21=舐め / 22=咥え");
            Integer("接近中の口形ID", "mouth_approach_pattern", ref _settings.MouthApproachPattern, 0, 40);
            Integer("咥え中の口形ID", "mouth_contact_pattern", ref _settings.MouthContactPattern, 0, 40);
            Float("開き始める距離 m", "mouth_open_start", ref _settings.MouthOpenStartDistance, 0.01f, 0.5f);
            Float("最大になる距離 m", "mouth_open_full", ref _settings.MouthOpenFullDistance, 0f, 0.5f);
            Float("開口解除距離 m", "mouth_open_release", ref _settings.MouthOpenReleaseDistance, 0.01f, 0.8f);
            Float("接近中の最大開き度", "mouth_open_amount", ref _settings.MouthOpenAmount, 0f, 1f);
            Float("咥え中の開き度", "mouth_contact_amount", ref _settings.MouthContactOpenAmount, 0f, 1f);
            Float("開き度の追従時間 秒", "mouth_open_seconds", ref _settings.MouthOpenSeconds, 0.02f, 2f);
            Float("口接触半径 m", "oral_contact_radius", ref _settings.OralContactRadius, 0.005f, 0.2f);
            Section("フェラ音声");
            Toggle("口接触中は専用音声のみ", ref _settings.OralAudioEnabled);
            GUILayout.Label("選択JSON (空欄なら VoiceFaceEventBridge/ImpactCueAudio/Oral/manifest.json)");
            string oralPath = GUILayout.TextField(_settings.OralSelectionPath ?? "");
            if (oralPath != _settings.OralSelectionPath) { _settings.OralSelectionPath = oralPath; _settingsChanged = true; }
            Float("専用音声の音量", "oral_volume", ref _settings.OralAudioVolume, 0f, 1f);
            Float("台詞の間隔 秒", "oral_interval", ref _settings.OralVoiceIntervalSeconds, 2f, 60f);
            if (GUILayout.Button("選択JSONと音声を再読込")) ReloadOralAudio();
            GUILayout.Label(_oralAudioStatus);
            GUILayout.Label("選択先: " + (_selectedTarget == TargetKind.Oral ? "口" :
                _selectedTarget == TargetKind.Vaginal ? "膣" : "なし") +
                " / 口接触: " + (_oralContact ? "あり" : "なし"));
            Float("曲げ強さ", "bend", ref _settings.BendStrength, 0f, 1f);
            Float("曲げ速度", "speed", ref _settings.BendSpeed, 0.1f, 60f);
            Float("節ごとの最大角度", "angle", ref _settings.MaxAngle, 0f, 80f);
            Integer("反復回数", "iter", ref _settings.SolverIterations, 1, 12);
            Section("愛撫中の出入り距離");
            Toggle("距離でゲージ加算", ref _settings.AibuGaugeEnabled);
            Toggle("VRグリップ/ギズモ中のみ", ref _settings.AibuRequireGrip);
            Float("膣軸からの許容距離", "aibu_radius", ref _settings.AibuContactRadius, 0.01f, 0.2f);
            Float("最小移動速度 m/s", "aibu_speed", ref _settings.AibuMinMotionSpeed, 0f, 1f);
            Float("声判定の集計時間 秒", "aibu_window", ref _settings.AibuWindowSeconds, 0.2f, 5f);
            Float("1m移動あたりの加算", "aibu_rate", ref _settings.AibuPointsPerMetre, 0f, 300f);
            Float("1秒あたりの上限", "aibu_cap", ref _settings.AibuMaxPointsPerSecond, 0f, 100f);
            Section("ゲーム標準ボイスの強さ");
            Toggle("激しい動きで強い声", ref _settings.AibuStrongVoiceEnabled);
            Float("強い声に切替 cm", "aibu_voice_on", ref _settings.AibuStrongVoiceOnCm, 1f, 100f);
            Float("通常の声に戻す cm", "aibu_voice_off", ref _settings.AibuStrongVoiceOffCm, 0f, 100f);
            Section("出し入れによる体の反応");
            Toggle("腰・上体の反応を有効", ref _settings.BodyReactionEnabled);
            Float("ゆっくり上限 m/s", "slow_max", ref _settings.SlowMaxSpeed, 0.02f, 1f);
            Float("ゆっくり確定 秒", "slow_wait", ref _settings.SlowConfirmSeconds, 0.05f, 2f);
            Float("高速往復しきい値 m/s", "fast_min", ref _settings.FastMinSpeed, 0.02f, 2f);
            Section("連続ピストン音声判定");
            Float("最短ストローク cm", "piston_stroke", ref _settings.PistonMinStrokeCm, 0.1f, 30f);
            Float("ストローク最大時間 秒", "piston_stroke_time", ref _settings.PistonMaxStrokeSeconds, 0.05f, 3f);
            Float("折返し判定窓 秒", "piston_window", ref _settings.PistonReversalWindowSeconds, 0.1f, 5f);
            Integer("必要な折返し回数", "piston_reversals", ref _settings.PistonMinReversals, 1, 12);
            Float("再試行間隔 成功 秒", "piston_retry_ok", ref _settings.PistonAcceptedRetrySeconds, 0f, 30f);
            Float("再試行間隔 失敗 秒", "piston_retry_no", ref _settings.PistonRejectedRetrySeconds, 0.05f, 10f);
            Float("音声音量", "piston_volume", ref _settings.PistonVoiceVolume, 0f, 1f);
            Float("再生後の待機 秒", "piston_cooldown", ref _settings.PistonVoiceCooldownSeconds, 0f, 30f);
            Section("連続ハードピストン音声判定");
            Float("集計時間 秒", "hard_window", ref _settings.HardPistonWindowSeconds, 0.1f, 5f);
            Float("集計移動距離 cm", "hard_distance", ref _settings.HardPistonDistanceCm, 1f, 100f);
            Float("最低速度 m/s", "hard_speed", ref _settings.HardPistonMinSpeed, 0.02f, 2f);
            Integer("必要な折返し回数", "hard_reversals", ref _settings.HardPistonMinReversals, 1, 12);
            Float("判定解除まで 秒", "hard_release", ref _settings.HardPistonReleaseSeconds, 0.05f, 5f);
            Float("再試行間隔 成功 秒", "hard_retry_ok", ref _settings.HardPistonAcceptedRetrySeconds, 0f, 30f);
            Float("再試行間隔 失敗 秒", "hard_retry_no", ref _settings.HardPistonRejectedRetrySeconds, 0.05f, 10f);
            Float("音声音量", "hard_volume", ref _settings.HardPistonVoiceVolume, 0f, 1f);
            Float("再生後の待機 秒", "hard_cooldown", ref _settings.HardPistonVoiceCooldownSeconds, 0f, 30f);
            Float("深い一撃の距離 cm", "deep_cm", ref _settings.DeepStrokeCm, 2f, 100f);
            Float("深い一撃 距離上限の挿入路割合", "deep_path_cap", ref _settings.DeepPathCapFraction, 0.1f, 1f);
            Float("深い一撃 速度 m/s", "deep_speed", ref _settings.DeepMinSpeed, 0.02f, 2f);
            Float("深い一撃の到達率", "deep_fraction", ref _settings.DeepEndFraction, 0f, 1f);
            Toggle("深い一撃で専用の声を再生", ref _settings.DeepVoiceEnabled);
            Float("通常の腰移動 m", "body_follow", ref _settings.BodyFollowMetres, 0f, 0.08f);
            Float("高速往復の腰反応 m", "body_fast", ref _settings.FastReactionMetres, 0f, 0.08f);
            Float("高速往復 引き抜き反応比", "body_fast_pull", ref _settings.FastPullReactionRatio, 0f, 2f);
            Float("高速往復 反応の消える速さ /秒", "body_fast_return", ref _settings.FastReactionReturnPerSecond, 0.1f, 30f);
            Float("ゆっくり時の腰追従率", "body_slow_follow", ref _settings.SlowBodyFollowRatio, 0f, 2f);
            Float("深い一撃の腰反応 m", "body_deep", ref _settings.DeepReactionMetres, 0f, 0.1f);
            Float("深い一撃後の戻り時間 秒", "body_deep_return", ref _settings.DeepReturnSeconds, 0.05f, 5f);
            Float("深い一撃後 ゆっくり判定停止 秒", "body_deep_hold", ref _settings.DeepHoldSeconds, 0f, 5f);
            Float("通常時の腰の戻り速度 m/秒", "body_return_speed", ref _settings.BodyReturnSpeed, 0.005f, 2f);
            Float("腰の追従速度 m/秒", "body_follow_speed", ref _settings.BodyFollowSpeed, 0.005f, 2f);
            Float("腰移動の上限 m", "body_offset_limit", ref _settings.BodyOffsetLimitMetres, 0.01f, 0.3f);
            Float("上体の追従率", "upper_ratio", ref _settings.UpperBodyRatio, 0f, 1f);
            Float("肩の追従速度 m/秒", "upper_follow_speed", ref _settings.UpperBodyFollowSpeed, 0.01f, 1f);
            Float("肩の戻り速度 m/秒", "upper_return_speed", ref _settings.UpperBodyReturnSpeed, 0.01f, 1f);
            Float("押出方向 左右限界 °", "body_angle", ref _settings.BodyMaxAngleDeg, 0f, 45f);
            Section("移動距離ゲージ");
            Toggle("移動距離を計測", ref _settings.MotionGaugeEnabled);
            Float("満タンの移動距離 cm", "motion_full", ref _settings.MotionGaugeFullCm, 1f, 3000f);
            Float("減衰速度 cm/秒", "motion_decay", ref _settings.MotionGaugeDecayCmPerSecond, 0f, 100f);
            Float("アニメ強度への反映", "motion_anim_strength", ref _settings.MotionGaugeAnimationStrengthWeight, 0f, 1f);
            Section("ゲージ連動の表情・汗");
            Float("表情開始 ゲージ割合", "face_start", ref _settings.FaceStartGaugeFraction, 0f, 1f);
            Integer("目パターンID", "heart_eye_pattern", ref _settings.HeartEyesPattern, 0, 38);
            Float("口表情の切替間隔 秒", "mouth_change", ref _settings.MouthChangeSeconds, 1f, 120f);
            Toggle("舌出し口開け 24", ref _settings.MouthPattern24Enabled);
            Toggle("Ahegao1 43", ref _settings.MouthPattern43Enabled);
            Toggle("Ahegao2 44", ref _settings.MouthPattern44Enabled);
            Toggle("Ahegao3 45", ref _settings.MouthPattern45Enabled);
            Toggle("ゲージ連動の汗", ref _settings.SweatEnabled);
            Float("汗1 開始割合", "sweat_start1", ref _settings.SweatStage1GaugeFraction, 0f, 1f);
            Integer("汗1 レベル", "sweat_level1", ref _settings.SweatStage1Level, 0, 10);
            Float("汗2 開始割合", "sweat_start2", ref _settings.SweatStage2GaugeFraction, 0f, 1f);
            Integer("汗2 レベル", "sweat_level2", ref _settings.SweatStage2Level, 0, 10);
            Float("汗3 開始割合", "sweat_start3", ref _settings.SweatStage3GaugeFraction, 0f, 1f);
            Integer("汗3 レベル", "sweat_level3", ref _settings.SweatStage3Level, 0, 10);
            Section("口挿入で増える涙・よだれ (H終了まで維持)");
            Toggle("涙・よだれを蓄積", ref _settings.OralFluidEnabled);
            Integer("涙の最大レベル", "oral_tears", ref _settings.OralTearLevel, 0, 10);
            Float("涙 1段階ごとの秒数", "oral_tear_step", ref _settings.OralTearStepSeconds, 0.5f, 120f);
            Integer("よだれの最大レベル", "oral_drool", ref _settings.OralDroolLevel, 0, 10);
            Float("よだれ 1段階ごとの秒数", "oral_drool_step", ref _settings.OralDroolStepSeconds, 0.5f, 120f);
            Section("全愛撫クローンの体アニメ速度");
            Float("ゲージ空の倍率", "clone_min", ref _settings.CloneAnimationMinMultiplier, 0.1f, 4f);
            Float("ゲージ満タンの倍率", "clone_max", ref _settings.CloneAnimationMaxMultiplier, 1f, 4f);
            Float("ゲージ反映量", "clone_weight", ref _settings.CloneAnimationGaugeWeight, 0f, 1f);
            Section("Ogazm 視線 (通常の視線制御より優先)");
            Toggle("ゲージ連動のより目", ref _settings.OgazmEyesEnabled);
            float eyeStartPercent = _settings.OgazmEyeStartGaugeFraction * 100f;
            Float("より目変化開始 %", "ogazm_eye_start_pct", ref eyeStartPercent, 0f, 99f);
            _settings.OgazmEyeStartGaugeFraction = eyeStartPercent * 0.01f;
            float eyeEndPercent = _settings.OgazmEyeFullGaugeFraction * 100f;
            Float("より目最大到達 %", "ogazm_eye_end_pct", ref eyeEndPercent,
                eyeStartPercent + 1f, 100f);
            _settings.OgazmEyeFullGaugeFraction = Mathf.Max(
                _settings.OgazmEyeStartGaugeFraction + 0.01f, eyeEndPercent * 0.01f);
            GUILayout.Label("開始で寄せ角0°、最大到達で設定した角度になります。", GUILayout.Width(430f));
            Float("最大時の寄せ角 °", "ogazm_inward", ref _settings.OgazmEyeInwardDegrees, 0f, 30f);
            Float("白目・上向き角 °", "ogazm_vertical", ref _settings.OgazmEyeVerticalDegrees, -60f, 60f);
            Float("虹彩を内側へ動かす量", "ogazm_iris_inward", ref _settings.OgazmIrisInwardUv, -0.3f, 0.3f);
            Float("虹彩を上へ動かす量", "ogazm_iris_up", ref _settings.OgazmIrisUpUv, -0.3f, 0.3f);
            GUILayout.Label("正の値で上、負の値で下。より目と同じゲージ範囲で変化します。",
                GUILayout.Width(430f));
            GUILayout.Label("より目状態: " + _ogazmEyeStatus, GUILayout.Width(430f));
            Toggle("目を寄せる左右を反転", ref _settings.OgazmInvertEyeDirection);
            Toggle("Ogazm 専用音声を再生", ref _settings.OgazmVoiceEnabled);
            Float("音声開始 ゲージ割合", "ogazm_enter", ref _settings.OgazmEnterGaugeFraction, 0.05f, 1f);
            Float("音声の再発火解除割合", "ogazm_exit", ref _settings.OgazmExitGaugeFraction, 0f, 1f);
            Float("Ogazm 音声音量", "ogazm_volume", ref _settings.OgazmVoiceVolume, 0f, 1f);
            Float("Ogazm 再生後の待機 秒", "ogazm_cooldown", ref _settings.OgazmVoiceCooldownSeconds, 0f, 60f);
            Float("満タン時の反応増幅", "motion_boost", ref _settings.MotionGaugeBoost, 0f, 2f);
            GUILayout.Label("現在: 距離 " + _motionGaugeCm.ToString("F1") + " cm / 判定 " +
                (_deepPulse > 0.01f ? "深い一撃" : Mathf.Abs(_fastPulse) > 0.01f ? "高速往復" :
                 _motionSlowActive > 0f ? "ゆっくり" : "通常") + " / 腰 " +
                (_motionBodyOffset * 100f).ToString("F1") + " cm");
            Section("節ごとの DynamicBoneCollider");
            _lastColliderEditorDrawAt = Time.unscaledTime;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀")) _selectedSegment = (_selectedSegment + _settings.Segments.Length - 1) % _settings.Segments.Length;
            GUILayout.Label((_selectedSegment + 1).ToString("00", CultureInfo.InvariantCulture) + "/" +
                _settings.Segments.Length.ToString("00", CultureInfo.InvariantCulture) + "  " +
                _settings.Segments[_selectedSegment].Bone, GUILayout.Width(190f));
            if (GUILayout.Button("▶")) _selectedSegment = (_selectedSegment + 1) % _settings.Segments.Length;
            GUILayout.EndHorizontal();
            var segment = _settings.Segments[_selectedSegment];
            GUILayout.Label(segment.Enabled ? "選択中のコライダー: 黄色" : "選択中のコライダー: 橙色（無効）");
            UpdateColliderPreviewAppearance();
            if (_selectedSegment > 0 && _selectedSegment < _settings.Segments.Length - 1)
            {
                Section("手動FK (自動追従OFFで使用)");
                Float("曲げ X", _selectedSegment + "fkx", ref segment.FkX, -80f, 80f);
                Float("曲げ Z", _selectedSegment + "fkz", ref segment.FkZ, -80f, 80f);
            }
            Toggle("この節を使用", ref segment.Enabled);
            string prefix = _selectedSegment.ToString(CultureInfo.InvariantCulture);
            float radiusCm = segment.Radius * 100f;
            Float("半径 cm", prefix + "radius_cm", ref radiusCm, 0.1f, 10f);
            segment.Radius = radiusCm * 0.01f;
            float lengthCm = GetSegmentLengthMetres(_selectedSegment) * 100f;
            float priorLengthCm = lengthCm;
            float minimumLengthCm = Mathf.Max(0.2f, radiusCm * 2f);
            Float("長さ cm", prefix + "length_cm", ref lengthCm, minimumLengthCm, 30f);
            if (Mathf.Abs(priorLengthCm - lengthCm) > 0.0001f)
                segment.LengthMetres = lengthCm * 0.01f;
            if (segment.LengthMetres > 0f && GUILayout.Button("長さを骨間の自動値に戻す"))
            {
                segment.LengthMetres = 0f;
                _settingsChanged = true;
                _edits.Remove(prefix + "length_cm");
            }
            Float("中心 X", prefix + "cx", ref segment.OffsetX, -0.2f, 0.2f);
            Float("中心 Y", prefix + "cy", ref segment.OffsetY, -0.2f, 0.2f);
            Float("中心 Z", prefix + "cz", ref segment.OffsetZ, -0.2f, 0.2f);
            Float("向き X", prefix + "rx", ref segment.RotationX, -180f, 180f);
            Float("向き Y", prefix + "ry", ref segment.RotationY, -180f, 180f);
            Float("向き Z", prefix + "rz", ref segment.RotationZ, -180f, 180f);
        }

        private float GetSegmentLengthMetres(int index)
        {
            SegmentSettings segment = _settings.Segments[index];
            if (segment.LengthMetres > 0f) return segment.LengthMetres;
            if (_colliderBones != null && index < _colliderBones.Length && _colliderBones[index] != null)
            {
                Transform bone = _colliderBones[index];
                Transform next = index + 1 < _colliderBones.Length ? _colliderBones[index + 1] : null;
                Transform tf = index < _colliders.Count && _colliders[index] != null
                    ? _colliders[index].transform : bone;
                float scaleY = Mathf.Max(0.0001f, Mathf.Abs(tf.lossyScale.y));
                float scaleZ = Mathf.Max(0.0001f, Mathf.Abs(tf.lossyScale.z));
                float span = next != null ? Vector3.Distance(bone.position, next.position) : segment.Radius;
                return (span + segment.Radius * scaleY / scaleZ) * segment.LengthScale;
            }
            float defaultSpan = index + 1 < BoneY.Length
                ? (BoneY[index + 1] - BoneY[index]) * _settings.ModelScale : segment.Radius;
            return (defaultSpan + segment.Radius) * segment.LengthScale;
        }

        private static void Section(string title)
        {
            GUILayout.Space(8);
            GUILayout.Label(title);
        }

        private void Toggle(string label, ref bool value)
        {
            bool next = GUILayout.Toggle(value, label);
            if (next != value) { value = next; _settingsChanged = true; }
        }

        private void Float(string label, string key, ref float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(160));
            float next = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(190));
            if (!Mathf.Approximately(next, value)) { value = next; _settingsChanged = true; _edits.Remove(key); }
            GUI.SetNextControlName(key);
            string display = GUI.GetNameOfFocusedControl() == key && _edits.TryGetValue(key, out string edit)
                ? edit : value.ToString("0.####", CultureInfo.InvariantCulture);
            string typed = GUILayout.TextField(display, GUILayout.Width(80));
            if (GUI.GetNameOfFocusedControl() == key)
            {
                _edits[key] = typed;
                if (float.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
                    && !float.IsNaN(parsed) && !float.IsInfinity(parsed))
                {
                    parsed = Mathf.Clamp(parsed, min, max);
                    if (!Mathf.Approximately(parsed, value)) { value = parsed; _settingsChanged = true; }
                }
            }
            else _edits.Remove(key);
            GUILayout.EndHorizontal();
        }

        private void Integer(string label, string key, ref int value, int min, int max)
        {
            float current = value;
            Float(label, key, ref current, min, max);
            value = Mathf.Clamp(Mathf.RoundToInt(current), min, max);
        }
    }
}
