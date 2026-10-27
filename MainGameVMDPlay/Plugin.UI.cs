using System.IO;
using MainGameUnifiedUi;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private const string UnifiedTabId = "vmdplay";

        private bool   _seekDragging    = false;
        private bool _seekRequested;
        private float  _seekDragValue   = 0f;

        private void InitUI()
        {
            _presetName = Path.GetFileNameWithoutExtension(_settings.LastVmdAbsPath ?? "");
            RefreshPresets();
            UnifiedUiApi.RegisterTab(UnifiedTabId, "VMD", DrawUnifiedContent, 45);
        }

        private void DrawUnifiedContent()
        {
            if (AstralBlocked) { GUILayout.Label("アストラル中：現実のVMD・呼出キャラは凍結中"); return; }
            if (!_available) { GUILayout.Label("VMDの初期化に失敗しました。ログを確認してください。"); return; }
            if (!_cfgEnabled.Value) { GUILayout.Label("VMDプラグインは無効です。設定のEnabledを有効にしてください。"); return; }
            GUILayout.BeginVertical();
            DrawIndependentActor();
            DrawStatusSection();
            GUILayout.Space(4f);
            DrawPlaybackControlSection();
            GUILayout.Space(4f);
            DrawMusicSection();
            DrawPresetSection();
            DrawMyLists();
            GUILayout.Space(4f);
            DrawAdjustments();
            DrawOptionsSection();

            GUILayout.EndVertical();
        }

        // ── ステータス ─────────────────────────────────────────────────────────

        private void DrawStatusSection()
        {
            string stateLabel;
            switch (_state)
            {
                case PluginState.Idle:          stateLabel = "待機中"; break;
                case PluginState.HSceneEntered: stateLabel = "キャラ準備待ち..."; break;
                case PluginState.CharaReady:    stateLabel = "準備完了"; break;
                case PluginState.Playing:       stateLabel = "再生中"; break;
                case PluginState.Paused:        stateLabel = "一時停止"; break;
                default:                        stateLabel = "?"; break;
            }

            if (!_available)
                GUILayout.Label("⚠ KKS_VMDPlayPlugin が見つかりません");
            else
                GUILayout.Label($"状態: {stateLabel}");

            string loaded = (_controller != null) ? VmdBridgeGetLastLoadedVmd(_controller) : "";
            string displayName = string.IsNullOrEmpty(loaded) ? "—" : Path.GetFileName(loaded);
            GUILayout.Label($"VMD: {displayName}");
        }

        // ── 再生制御 ──────────────────────────────────────────────────────────

        private void DrawPlaybackControlSection()
        {
            bool hasCtrl = _controller != null;

            GUILayout.BeginHorizontal();

            GUI.enabled = hasCtrl && _state != PluginState.Playing && _loadRoutine == null && !_musicLoading;
            if (GUILayout.Button("再生"))
            {
                LogInfo("[UI] btn=再生");
                PlaybackPlay();
            }

            GUI.enabled = hasCtrl && _state == PluginState.Playing;
            if (GUILayout.Button("一時停止"))
            {
                LogInfo("[UI] btn=一時停止");
                PlaybackPause();
            }

            GUI.enabled = hasCtrl;
            if (GUILayout.Button("停止"))
            {
                LogInfo("[UI] btn=停止");
                PlaybackStop();
            }

            GUI.enabled = hasCtrl && _loadRoutine == null && !_musicLoading;
            if (GUILayout.Button("最初から"))
            {
                LogInfo("[UI] btn=最初から");
                PlaybackRestart();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUI.enabled = hasCtrl;
            if (GUILayout.Button("VMDを終了して本編に戻す")) ReleaseCharacter();
            GUI.enabled = true;

            GUI.enabled = hasCtrl && !_pickerBusy;
            if (GUILayout.Button("VMDファイルを選ぶ")) BeginPickVmdFile();
            if (GUILayout.Button("音源を選ぶ（音楽・動画）")) BeginPickAudioFile();
            GUI.enabled = true;

            // シークバー
            if (hasCtrl && _animLength > 0f)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("時刻", GUILayout.Width(30f));

                float displayTime = _seekDragging ? _seekDragValue : _currentAnimTime;
                bool released = Event.current != null && Event.current.rawType == EventType.MouseUp;
                float newVal = GUILayout.HorizontalSlider(displayTime, 0f, _animLength);

                if (Mathf.Abs(newVal - displayTime) > 0.001f)
                {
                    _seekDragging  = true;
                    _seekDragValue = newVal;
                    _currentAnimTime = newVal;
                    _seekRequested = true;
                }

                // Release only ends scrubbing; changed values are consumed every Update.
                if (_seekDragging && released)
                {
                    _seekDragging = false;
                }

                GUILayout.Label($"{(_seekDragging || _seekRequested ? _seekDragValue : _currentAnimTime):F1}/{_animLength:F1}s", GUILayout.Width(80f));
                GUILayout.EndHorizontal();
            }
        }

        // ── オプション ─────────────────────────────────────────────────────────

        private void DrawOptionsSection()
        {
            GUILayout.Label("── オプション ──");

            bool autoLoad = GUILayout.Toggle(_settings.AutoLoadLast, " 前回VMDを自動ロード");
            if (autoLoad != _settings.AutoLoadLast)
            {
                _settings.AutoLoadLast = autoLoad;
                SaveSettings("toggle");
                LogInfo($"[UI] toggle AutoLoadLast={autoLoad}");
            }

            bool autoPlay = GUILayout.Toggle(_settings.AutoPlayOnLoad, " ロード時に自動再生");
            if (autoPlay != _settings.AutoPlayOnLoad)
            {
                _settings.AutoPlayOnLoad = autoPlay;
                SaveSettings("toggle");
                LogInfo($"[UI] toggle AutoPlayOnLoad={autoPlay}");
            }

            bool ik = DrawBinaryControl("option.UseIKDataInVMD", " IKデータ使用 (UseIKDataInVMD)", _settings.UseIKDataInVMD);
            if (ik != _settings.UseIKDataInVMD)
            {
                _settings.UseIKDataInVMD = ik;
                SaveSettings("toggle");
                VmdBridgeSetSetting("UseIKDataInVMD", ik);
                ApplyRuntimeOptions();
                LogInfo($"[UI] toggle UseIKDataInVMD={ik}");
            }

            bool smooth = DrawBinaryControl("option.SmoothArmTwist", " 腕ひねり補正 (SmoothArmTwist)", _settings.SmoothArmTwist);
            if (smooth != _settings.SmoothArmTwist)
            {
                _settings.SmoothArmTwist = smooth;
                SaveSettings("toggle");
                VmdBridgeSetSetting("SmoothArmTwist", smooth);
                ApplyRuntimeOptions();
                LogInfo($"[UI] toggle SmoothArmTwist={smooth}");
            }

            bool bone = DrawBinaryControl("option.UseBoneTransform", " ボーン変形使用 (UseBoneTransform)", _settings.UseBoneTransform);
            if (bone != _settings.UseBoneTransform)
            {
                _settings.UseBoneTransform = bone;
                SaveSettings("toggle");
                VmdBridgeSetSetting("UseBoneTransform", bone);
                ApplyRuntimeOptions();
                LogInfo($"[UI] toggle UseBoneTransform={bone}");
            }

            bool verbose = GUILayout.Toggle(_settings.VerboseLog, " 詳細ログ");
            if (verbose != _settings.VerboseLog)
            {
                _settings.VerboseLog = verbose;
                SaveSettings("toggle");
                LogInfo($"[UI] toggle VerboseLog={verbose}");
            }
        }
    }
}
