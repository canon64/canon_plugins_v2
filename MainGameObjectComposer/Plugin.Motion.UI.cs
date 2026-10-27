using UnityEngine;

namespace MainGameObjectComposer
{
    public sealed partial class Plugin
    {
        private static readonly string[] MotionModeLabels = new[] { "なし", "回転", "アングル", "ピストン" };

        private void DrawMotionSection()
        {
            GUILayout.Label("動き");
            int prevMode = Mathf.Clamp(_editMotionMode, 0, MotionModeLabels.Length - 1);
            int nextMode = GUILayout.SelectionGrid(prevMode, MotionModeLabels, MotionModeLabels.Length);
            if (nextMode != prevMode)
            {
                _editMotionMode = nextMode;
                SetSelectedMotionMode(nextMode);
            }

            switch (_editMotionMode)
            {
                case 1:
                    DrawAutoRotateEditor();
                    break;
                case 2:
                    DrawAngleEditor();
                    break;
                case 3:
                    DrawPistonEditor();
                    break;
                default:
                    GUILayout.Label("(動きなし)");
                    break;
            }
        }

        private void DrawAutoRotateEditor()
        {
            _editAutoRotateLocalSpace = GUILayout.Toggle(_editAutoRotateLocalSpace, "ローカル空間");
            GUILayout.Label("回転軸");
            DrawVec3Editors(ref _editAxisX, ref _editAxisY, ref _editAxisZ);
            GUILayout.BeginHorizontal();
            GUILayout.Label("速度 (度/秒)", GUILayout.Width(90f));
            _editAutoRotateSpeed = GUILayout.TextField(_editAutoRotateSpeed, GUILayout.Width(90f));
            if (GUILayout.Button("回転を適用", GUILayout.Width(120f)))
            {
                if (!TryParseVec3(_editAxisX, _editAxisY, _editAxisZ, out Vector3 axis)
                    || !TryParseFloat(_editAutoRotateSpeed, out float speed))
                {
                    LogWarn("auto rotate parse failed");
                }
                else
                {
                    SetSelectedAutoRotate(true, axis, speed, _editAutoRotateLocalSpace);
                }
            }
            GUILayout.EndHorizontal();
        }

        private void DrawAngleEditor()
        {
            _editAngleLocalSpace = GUILayout.Toggle(_editAngleLocalSpace, "ローカル空間");
            GUILayout.Label("軸");
            DrawVec3Editors(ref _editAngleAxisX, ref _editAngleAxisY, ref _editAngleAxisZ);
            GUILayout.BeginHorizontal();
            GUILayout.Label("振幅 (度)", GUILayout.Width(90f));
            _editAngleAmplitudeDeg = GUILayout.TextField(_editAngleAmplitudeDeg, GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("速度 (往復/秒)", GUILayout.Width(90f));
            _editAngleSpeedHz = GUILayout.TextField(_editAngleSpeedHz, GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("位相 (0〜1)", GUILayout.Width(90f));
            _editAnglePhaseTurns = GUILayout.TextField(_editAnglePhaseTurns, GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            if (GUILayout.Button("アングルを適用", GUILayout.Width(130f)))
            {
                if (!TryParseVec3(_editAngleAxisX, _editAngleAxisY, _editAngleAxisZ, out Vector3 axis)
                    || !TryParseFloat(_editAngleAmplitudeDeg, out float amp)
                    || !TryParseFloat(_editAngleSpeedHz, out float speed)
                    || !TryParseFloat(_editAnglePhaseTurns, out float phase))
                {
                    LogWarn("angle parse failed");
                }
                else
                {
                    SetSelectedAngle(axis, amp, speed, phase, _editAngleLocalSpace);
                }
            }
        }

        private void DrawPistonEditor()
        {
            _editPistonLocalSpace = GUILayout.Toggle(_editPistonLocalSpace, "ローカル空間");
            GUILayout.Label("軸");
            DrawVec3Editors(ref _editPistonAxisX, ref _editPistonAxisY, ref _editPistonAxisZ);
            GUILayout.BeginHorizontal();
            GUILayout.Label("振幅 (m)", GUILayout.Width(90f));
            _editPistonAmplitude = GUILayout.TextField(_editPistonAmplitude, GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("速度 (往復/秒)", GUILayout.Width(90f));
            _editPistonSpeedHz = GUILayout.TextField(_editPistonSpeedHz, GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("位相 (0〜1)", GUILayout.Width(90f));
            _editPistonPhaseTurns = GUILayout.TextField(_editPistonPhaseTurns, GUILayout.Width(90f));
            GUILayout.EndHorizontal();
            if (GUILayout.Button("ピストンを適用", GUILayout.Width(130f)))
            {
                if (!TryParseVec3(_editPistonAxisX, _editPistonAxisY, _editPistonAxisZ, out Vector3 axis)
                    || !TryParseFloat(_editPistonAmplitude, out float amp)
                    || !TryParseFloat(_editPistonSpeedHz, out float speed)
                    || !TryParseFloat(_editPistonPhaseTurns, out float phase))
                {
                    LogWarn("piston parse failed");
                }
                else
                {
                    SetSelectedPiston(axis, amp, speed, phase, _editPistonLocalSpace);
                }
            }
        }
    }
}
