using UnityEngine;
using System.Collections.Generic;

namespace MainGirlHipHijack
{
    public sealed partial class Plugin
    {
        private void DrawFemaleHandPoseSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("女性手ポーズ");
            DrawFemaleHandPoseRow(FemaleHandLeft, "左手");
            DrawFemaleHandPoseRow(FemaleHandRight, "右手");
        }

        private void DrawFemaleHandPoseRow(int hand, string label)
        {
            if (_settings == null)
                return;

            ChaControl target = _femaleSession.Character;
            int maxCount = target != null ? Mathf.Max(0, target.GetShapeIndexCount()) : 0;
            bool enabled = GetFemaleHandPoseEnabled(hand);
            int index = GetFemaleHandPoseIndex(hand);
            float weight = GetFemaleHandPoseWeight(hand);
            string text = hand == FemaleHandLeft ? _femaleLeftHandPoseIndexText : _femaleRightHandPoseIndexText;

            GUILayout.BeginHorizontal();
            bool nextEnabled = GUILayout.Toggle(enabled, label, GUILayout.Width(64f));
            if (nextEnabled != enabled)
                SetFemaleHandPoseEnabled(hand, nextEnabled, apply: true, saveSettings: true);

            GUILayout.Label("index", GUILayout.Width(38f));
            GUI.SetNextControlName(hand == FemaleHandLeft ? "FemaleLeftHandPoseIndex" : "FemaleRightHandPoseIndex");
            string nextText = GUILayout.TextField(text ?? string.Empty, GUILayout.Width(54f));
            if (!string.Equals(nextText, text, System.StringComparison.Ordinal))
                SetFemaleHandPoseIndexText(hand, nextText, applyWhenValid: true);

            GUI.enabled = target != null;
            if (GUILayout.Button("前", GUILayout.Width(36f)))
                StepFemaleHandPoseIndex(hand, -1);
            if (GUILayout.Button("次", GUILayout.Width(36f)))
                StepFemaleHandPoseIndex(hand, 1);
            if (GUILayout.Button("現在", GUILayout.Width(52f)))
                CaptureCurrentFemaleHandPose(hand);
            if (GUILayout.Button("0", GUILayout.Width(32f)))
                SetFemaleHandPoseIndex(hand, 0, apply: true, saveSettings: true);
            GUI.enabled = true;

            GUILayout.Label("W", GUILayout.Width(18f));
            float nextWeight = GUILayout.HorizontalSlider(weight, 0f, 1f, GUILayout.Width(100f));
            GUILayout.Label(nextWeight.ToString("F2"), GUILayout.Width(36f));
            if (!Mathf.Approximately(nextWeight, weight))
                SetFemaleHandPoseWeight(hand, nextWeight, apply: true, saveSettings: true);

            GUILayout.Label("cur=" + GetCurrentFemaleHandPoseLabel(target, hand, maxCount), GUILayout.Width(120f));
            GUILayout.EndHorizontal();
        }

        private string GetCurrentFemaleHandPoseLabel(ChaControl target, int hand, int maxCount)
        {
            if (target == null)
                return "-";

            int index = 0;
            bool enabled = false;
            try
            {
                index = target.GetShapeHandIndex(hand, 0);
                enabled = target.GetEnableShapeHand(hand);
            }
            catch
            {
                return "err";
            }

            return index + "/" + maxCount + (enabled ? " on" : " off");
        }

        private void ApplyFemaleHandPoseIndexText(int hand)
        {
            string text = hand == FemaleHandLeft ? _femaleLeftHandPoseIndexText : _femaleRightHandPoseIndexText;
            if (!int.TryParse(text, out int parsed))
            {
                LogWarn("[FemaleHandPose] invalid index hand=" + hand + " text=" + text);
                return;
            }

            SetFemaleHandPoseIndex(hand, parsed, apply: true, saveSettings: true);
            SetFemaleHandPoseEnabled(hand, true, apply: true, saveSettings: true);
        }

        private void SetFemaleHandPoseIndexText(int hand, string text, bool applyWhenValid)
        {
            if (hand == FemaleHandLeft)
                _femaleLeftHandPoseIndexText = text;
            else
                _femaleRightHandPoseIndexText = text;

            if (!applyWhenValid || !int.TryParse(text, out int parsed))
                return;

            SetFemaleHandPoseIndex(hand, parsed, apply: true, saveSettings: true);
            SetFemaleHandPoseEnabled(hand, parsed != 0, apply: true, saveSettings: true);
        }

        private void StepFemaleHandPoseIndex(int hand, int delta)
        {
            int maxCount = GetFemaleHandPoseMaxCount();
            int current = GetFemaleHandPoseIndex(hand);
            int next = current + delta;
            if (maxCount > 0)
            {
                if (next < 0)
                    next = maxCount - 1;
                else if (next >= maxCount)
                    next = 0;
            }
            else
            {
                next = Mathf.Max(0, next);
            }

            SetFemaleHandPoseIndex(hand, next, apply: true, saveSettings: true);
            SetFemaleHandPoseEnabled(hand, next != 0, apply: true, saveSettings: true);
        }

        private void CaptureCurrentFemaleHandPose(int hand)
        {
            ChaControl target = _femaleSession.Character;
            if (target == null)
                return;

            try
            {
                SetFemaleHandPoseIndex(hand, target.GetShapeHandIndex(hand, 0), apply: false, saveSettings: false);
                SetFemaleHandPoseEnabled(hand, target.GetEnableShapeHand(hand), apply: false, saveSettings: false);
                SaveSettings();
                SyncFemaleHandPoseTextFromSettings();
                LogInfo("[FemaleHandPose] captured hand=" + hand + " index=" + GetFemaleHandPoseIndex(hand));
            }
            catch (System.Exception ex)
            {
                LogWarn("[FemaleHandPose] capture failed hand=" + hand + " error=" + ex.Message);
            }
        }

        private void SyncFemaleHandPoseTextFromSettings()
        {
            if (_settings == null)
                return;

            _femaleLeftHandPoseIndexText = _settings.FemaleLeftHandPoseIndex.ToString();
            _femaleRightHandPoseIndexText = _settings.FemaleRightHandPoseIndex.ToString();
        }

        private bool GetFemaleHandPoseEnabled(int hand)
        {
            if (_settings == null)
                return false;
            return hand == FemaleHandLeft ? _settings.FemaleLeftHandPoseEnabled : _settings.FemaleRightHandPoseEnabled;
        }

        private int GetFemaleHandPoseIndex(int hand)
        {
            if (_settings == null)
                return 0;
            return hand == FemaleHandLeft ? _settings.FemaleLeftHandPoseIndex : _settings.FemaleRightHandPoseIndex;
        }

        private float GetFemaleHandPoseWeight(int hand)
        {
            if (_settings == null)
                return 0f;
            return hand == FemaleHandLeft ? _settings.FemaleLeftHandPoseWeight : _settings.FemaleRightHandPoseWeight;
        }

        private void SetFemaleHandPoseEnabled(int hand, bool enabled, bool apply, bool saveSettings)
        {
            if (_settings == null)
                return;

            if (hand == FemaleHandLeft)
                _settings.FemaleLeftHandPoseEnabled = enabled;
            else
                _settings.FemaleRightHandPoseEnabled = enabled;

            if (apply)
                ApplyFemaleHandPose(hand);
            if (saveSettings)
                SaveSettings();
        }

        private void SetFemaleHandPoseIndex(int hand, int index, bool apply, bool saveSettings)
        {
            if (_settings == null)
                return;

            int maxCount = GetFemaleHandPoseMaxCount();
            int clamped = maxCount > 0 ? Mathf.Clamp(index, 0, maxCount - 1) : Mathf.Max(0, index);
            if (hand == FemaleHandLeft)
            {
                _settings.FemaleLeftHandPoseIndex = clamped;
                _femaleLeftHandPoseIndexText = clamped.ToString();
            }
            else
            {
                _settings.FemaleRightHandPoseIndex = clamped;
                _femaleRightHandPoseIndexText = clamped.ToString();
            }

            if (apply)
                ApplyFemaleHandPose(hand);
            if (saveSettings)
                SaveSettings();
        }

        private void SetFemaleHandPoseWeight(int hand, float weight, bool apply, bool saveSettings)
        {
            if (_settings == null)
                return;

            float clamped = Mathf.Clamp01(weight);
            if (hand == FemaleHandLeft)
                _settings.FemaleLeftHandPoseWeight = clamped;
            else
                _settings.FemaleRightHandPoseWeight = clamped;

            if (apply)
                ApplyFemaleHandPose(hand);
            if (saveSettings)
                SaveSettings();
        }

        private int GetFemaleHandPoseMaxCount()
        {
            ChaControl target = _femaleSession.Character;
            if (target == null)
                return 0;

            try
            {
                return Mathf.Max(0, target.GetShapeIndexCount());
            }
            catch
            {
                return 0;
            }
        }

        private void ApplyConfiguredFemaleHandPoses()
        {
            ApplyFemaleHandPose(FemaleHandLeft);
            ApplyFemaleHandPose(FemaleHandRight);
        }

        private void ApplyFemaleHandPose(int hand)
        {
            ChaControl target = _femaleSession.Character;
            if (target == null)
                return;
            FemaleHandFkSettings fk = GetFemaleHandFkSettings(hand);
            if (fk != null && fk.Enabled)
                return;

            bool enabled = GetFemaleHandPoseEnabled(hand);
            int maxCount = Mathf.Max(0, target.GetShapeIndexCount());
            int index = maxCount > 0 ? Mathf.Clamp(GetFemaleHandPoseIndex(hand), 0, maxCount - 1) : 0;
            float weight = Mathf.Clamp01(GetFemaleHandPoseWeight(hand));

            try
            {
                if (!enabled || weight <= 0.0001f || index == 0)
                {
                    target.SetEnableShapeHand(hand, false);
                    return;
                }

                target.SetShapeHandValue(hand, index, 0, 1f - weight);
                target.SetEnableShapeHand(hand, true);
            }
            catch (System.Exception ex)
            {
                LogWarn("[FemaleHandPose] apply failed hand=" + hand + " index=" + index + " error=" + ex.Message);
            }
        }

        private void ResetFemaleHandPoseRuntime()
        {
            ResetFemaleHandPoseRuntime(resetSettings: false, saveSettings: false);
        }

        private void ResetFemaleHandPoseRuntimeAndSettings(bool saveSettings)
        {
            ResetFemaleHandPoseRuntime(resetSettings: true, saveSettings: saveSettings);
        }

        private void ResetFemaleHandPoseRuntime(bool resetSettings, bool saveSettings)
        {
            ChaControl target = _femaleSession.Character;
            if (resetSettings && _settings != null)
            {
                _settings.FemaleLeftHandPoseEnabled = false;
                _settings.FemaleRightHandPoseEnabled = false;
                _settings.FemaleLeftHandPoseIndex = 0;
                _settings.FemaleRightHandPoseIndex = 0;
                _settings.FemaleLeftHandPoseWeight = 0f;
                _settings.FemaleRightHandPoseWeight = 0f;
                SyncFemaleHandPoseTextFromSettings();
                if (saveSettings)
                    SaveSettings();
            }

            if (target != null)
            {
                try
                {
                    target.SetEnableShapeHand(FemaleHandLeft, false);
                    target.SetEnableShapeHand(FemaleHandRight, false);
                }
                catch
                {
                    // ignore cleanup failures
                }
            }

            ResetFemaleHandFkRuntimeAll();
        }

        private List<HandPoseTransitionPoint> BuildFemaleHandPoseTransitionPoints(PosePresetRuntime preset)
        {
            var points = new List<HandPoseTransitionPoint>(2);
            if (_settings == null || preset == null)
                return points;

            points.Add(new HandPoseTransitionPoint
            {
                Hand = FemaleHandLeft,
                StartEnabled = _settings.FemaleLeftHandPoseEnabled,
                StartIndex = _settings.FemaleLeftHandPoseIndex,
                StartWeight = _settings.FemaleLeftHandPoseWeight,
                TargetEnabled = preset.hasFemaleHandPose && preset.femaleLeftHandPoseEnabled,
                TargetIndex = preset.hasFemaleHandPose ? preset.femaleLeftHandPoseIndex : 0,
                TargetWeight = preset.hasFemaleHandPose ? preset.femaleLeftHandPoseWeight : 0f
            });
            points.Add(new HandPoseTransitionPoint
            {
                Hand = FemaleHandRight,
                StartEnabled = _settings.FemaleRightHandPoseEnabled,
                StartIndex = _settings.FemaleRightHandPoseIndex,
                StartWeight = _settings.FemaleRightHandPoseWeight,
                TargetEnabled = preset.hasFemaleHandPose && preset.femaleRightHandPoseEnabled,
                TargetIndex = preset.hasFemaleHandPose ? preset.femaleRightHandPoseIndex : 0,
                TargetWeight = preset.hasFemaleHandPose ? preset.femaleRightHandPoseWeight : 0f
            });

            return points;
        }

        private void ApplyFemaleHandPoseTransitionPoints(List<HandPoseTransitionPoint> points, float t)
        {
            if (points == null || _settings == null)
                return;

            for (int i = 0; i < points.Count; i++)
            {
                HandPoseTransitionPoint point = points[i];
                if (point == null)
                    continue;

                float w = Mathf.Lerp(
                    point.StartEnabled ? point.StartWeight : 0f,
                    point.TargetEnabled ? point.TargetWeight : 0f,
                    Mathf.Clamp01(t));
                bool useTarget = t >= 0.5f;
                bool enabled = useTarget ? point.TargetEnabled : point.StartEnabled;
                int index = useTarget ? point.TargetIndex : point.StartIndex;

                if (point.Hand == FemaleHandLeft)
                {
                    _settings.FemaleLeftHandPoseEnabled = enabled;
                    _settings.FemaleLeftHandPoseIndex = Mathf.Max(0, index);
                    _settings.FemaleLeftHandPoseWeight = Mathf.Clamp01(w);
                    _femaleLeftHandPoseIndexText = _settings.FemaleLeftHandPoseIndex.ToString();
                }
                else
                {
                    _settings.FemaleRightHandPoseEnabled = enabled;
                    _settings.FemaleRightHandPoseIndex = Mathf.Max(0, index);
                    _settings.FemaleRightHandPoseWeight = Mathf.Clamp01(w);
                    _femaleRightHandPoseIndexText = _settings.FemaleRightHandPoseIndex.ToString();
                }

                ApplyFemaleHandPose(point.Hand);
            }
        }
    }
}
