using UnityEngine;

namespace MainGameHandySync
{
    internal sealed partial class Plugin
    {
        private Rect _windowRect = new Rect(20, 120, 420, 280);
        private bool _windowVisible = true;
        private Vector2 _scrollPos;

        private void OnGUI()
        {
            if (!CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom) return;
            if (_cfgShowUI == null || !_cfgShowUI.Value) return;
            if (!InHScene) return;

            if (_windowVisible)
            {
                _windowRect = GUILayout.Window(872341, _windowRect, DrawHandySyncWindow, "MainGameHandySync - Animation Sync");
            }
        }

        private void DrawHandySyncWindow(int id)
        {
            GUILayout.BeginVertical();

            // Status
            GUILayout.Label($"Pose: {_currentPoseKey}");
            GUILayout.Label($"Connection: {(string.IsNullOrWhiteSpace(GetConnectionKey()) ? "No Key" : "Key Set")}");

            GUILayout.Space(8);

            // Phase Offset Slider (main request)
            float currentOffset = _cfgPhaseOffset != null ? _cfgPhaseOffset.Value : 0f;
            GUILayout.Label($"Phase Offset: {currentOffset:F3}");
            float newOffset = GUILayout.HorizontalSlider(currentOffset, 0f, 1f, GUILayout.Width(380));
            if (Mathf.Abs(newOffset - currentOffset) > 0.001f && _cfgPhaseOffset != null)
            {
                _cfgPhaseOffset.Value = newOffset;
            }

            GUILayout.Space(6);

            // Sync Button (main request)
            if (GUILayout.Button("SYNC NOW (Align to Current Animation)", GUILayout.Height(28)))
            {
                PerformSyncNow();
            }

            GUILayout.Space(10);

            // Log toggles
            GUILayout.Label("Logging");
            if (_cfgEnableNormalLog != null)
                _cfgEnableNormalLog.Value = GUILayout.Toggle(_cfgEnableNormalLog.Value, "Normal Log");
            if (_cfgEnableDetailedLog != null)
                _cfgEnableDetailedLog.Value = GUILayout.Toggle(_cfgEnableDetailedLog.Value, "Detailed Log");
            if (_cfgEnableErrorLog != null)
                _cfgEnableErrorLog.Value = GUILayout.Toggle(_cfgEnableErrorLog.Value, "Error Log");

            GUILayout.Space(8);

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void PerformSyncNow()
        {
            if (_animationReader == null || _hFlag == null || _hSceneProc == null) return;

            float currentAnim = _animationReader.GetAnimationPhase(_hSceneProc, _hFlag);
            // Set offset so that (currentAnim + offset) ≈ 0 (or desired position)
            float desired = 0.0f; // or 0.5f for middle, etc.
            float newOffset = (desired - currentAnim + 1f) % 1f;

            if (_cfgPhaseOffset != null)
                _cfgPhaseOffset.Value = newOffset;

            if (ShouldLogNormal())
                SimpleFileLogger.Info($"Sync Now executed. New offset = {newOffset:F3}");
        }
    }
}
