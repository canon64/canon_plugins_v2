using UnityEngine;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private void DrawPosePresetSection()
        {
            EnsurePosePresetsLoaded();
            RefreshPosePresetThumbCacheIfNeeded();

            GUILayout.Space(8f);
            GUILayout.Label("── ポーズ保存/読込（スクショ付き） ──");
            GUILayout.Label("現在体位: " + BuildCurrentPostureHint());
            GUILayout.Label("サムネを同体位でダブルクリックすると読込");

            GUILayout.BeginHorizontal();
            GUILayout.Label("名前", GUILayout.Width(38f));
            _posePresetNameDraft = GUILayout.TextField(_posePresetNameDraft ?? string.Empty, GUILayout.Width(160f));
            if (GUILayout.Button("保存+撮影", GUILayout.Width(92f)))
                SaveCurrentPosePresetWithScreenshot(_posePresetNameDraft);
            if (GUILayout.Button("再読込", GUILayout.Width(70f)))
                ReloadPosePresetIndex();
            GUILayout.EndHorizontal();

            bool autoEnabled = GUILayout.Toggle(_settings.AutoPoseEnabled, "自動呼び出し（体位+モーション一致 + autoチェック済み候補からランダム）");
            if (autoEnabled != _settings.AutoPoseEnabled)
            {
                _settings.AutoPoseEnabled = autoEnabled;
                _autoPosePendingApply = autoEnabled;
                _autoPoseLoopReady = false;
                _autoPoseLoopCountSinceSwitch = 0;
                SaveSettings();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("切替アニメ回数", GUILayout.Width(100f));
            int loopCount = Mathf.Max(1, _settings.AutoPoseSwitchAnimationLoops);
            int nextLoopCount = Mathf.RoundToInt(GUILayout.HorizontalSlider(loopCount, 1f, 100f, GUILayout.Width(160f)));
            GUILayout.Label(nextLoopCount.ToString(), GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            if (nextLoopCount != _settings.AutoPoseSwitchAnimationLoops)
            {
                _settings.AutoPoseSwitchAnimationLoops = nextLoopCount;
                SaveSettings();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("遷移秒数", GUILayout.Width(100f));
            float transitionSec = GetPoseTransitionSeconds();
            float nextTransitionSec = GUILayout.HorizontalSlider(transitionSec, 0f, 2f, GUILayout.Width(160f));
            GUILayout.Label(nextTransitionSec.ToString("F2"), GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(nextTransitionSec, transitionSec))
            {
                _settings.PoseTransitionSeconds = nextTransitionSec;
                SaveSettings();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("遷移イージング", GUILayout.Width(100f));
            PoseTransitionEasing easing = GetPoseTransitionEasing();
            int easingIndex = Mathf.Clamp((int)easing, 0, 2);
            int nextEasingIndex = GUILayout.SelectionGrid(
                easingIndex,
                new[] { "リニア", "加減速", "EaseOut" },
                3,
                GUILayout.Width(220f));
            GUILayout.EndHorizontal();
            if (nextEasingIndex != easingIndex)
            {
                _settings.PoseTransitionEasing = (PoseTransitionEasing)Mathf.Clamp(nextEasingIndex, 0, 2);
                SaveSettings();
            }

            bool anyVisible = false;
            for (int i = 0; i < _posePresets.Count; i++)
            {
                PosePresetRuntime preset = _posePresets[i];
                if (preset == null)
                    continue;
                bool exactMotion = IsCurrentPostureMatch(preset);
                bool legacyMotion = IsLegacyPosePresetForCurrentPosture(preset);
                if (!exactMotion && !legacyMotion)
                    continue;

                anyVisible = true;

                GUILayout.BeginHorizontal("box");

                Texture2D thumb = GetPosePresetThumbnail(preset);
                GUIContent thumbContent = thumb != null ? new GUIContent(thumb) : new GUIContent("No\nImage");
                if (GUILayout.Button(thumbContent, GUILayout.Width(96f), GUILayout.Height(54f)))
                    HandlePosePresetThumbnailClick(preset);

                GUILayout.BeginVertical(GUILayout.Width(230f));
                GUILayout.Label(string.IsNullOrEmpty(preset.name) ? "<no name>" : preset.name);
                GUILayout.Label(string.IsNullOrEmpty(preset.createdAt) ? "<no time>" : preset.createdAt);
                GUILayout.Label("体位: " + BuildPosePostureHint(preset));
                if (legacyMotion)
                    GUILayout.Label("未紐付け: 現在モーションに紐付け可");
                GUILayout.EndVertical();

                GUILayout.BeginVertical(GUILayout.Width(72f));
                bool auto = GUILayout.Toggle(preset.autoApply, "auto");
                if (auto != preset.autoApply)
                {
                    preset.autoApply = auto;
                    SavePosePresetIndex();
                }
                if (GUILayout.Button("読込", GUILayout.Width(60f)))
                    ApplyPosePreset(preset, requireCurrentPosture: exactMotion, reason: legacyMotion ? "ui-load-legacy" : "ui-load");
                if (legacyMotion && GUILayout.Button("紐付け", GUILayout.Width(60f)))
                    BindPosePresetToCurrentMotion(preset);
                if (GUILayout.Button("上書き", GUILayout.Width(60f)))
                    OverwritePosePresetById(preset.id);
                if (GUILayout.Button("削除", GUILayout.Width(60f)))
                    DeletePosePresetById(preset.id);
                GUILayout.EndVertical();

                GUILayout.EndHorizontal();
            }

            if (!anyVisible)
                GUILayout.Label("この体位+モーションの保存済みポーズなし");
        }
    }
}

