using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private bool _cursorStateCaptured;
        private CursorLockMode _cursorOrigLockState;
        private bool _cursorOrigVisible;
        private float _nextCursorLogTime;

        private void UpdateAstralCursorState()
        {
            bool astralActive = _state != AstralState.Normal;

            if (astralActive)
            {
                if (!_cursorStateCaptured)
                {
                    _cursorOrigLockState = Cursor.lockState;
                    _cursorOrigVisible = Cursor.visible;
                    _cursorStateCaptured = true;
                    SimpleFileLogger.Info($"Cursor captured: lockState={_cursorOrigLockState}, visible={_cursorOrigVisible}");
                }

                if (Cursor.lockState != CursorLockMode.None || !Cursor.visible)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                    if (Time.unscaledTime >= _nextCursorLogTime)
                    {
                        _nextCursorLogTime = Time.unscaledTime + 1f;
                        SimpleFileLogger.Info($"Cursor forced unlocked during astral: lockState={Cursor.lockState}, visible={Cursor.visible}");
                    }
                }
                return;
            }

            if (_cursorStateCaptured)
            {
                // A cursor lock captured at entry may belong to a long-finished drag.
                // Let normal gameplay acquire its own lock on the next input.
                if (Singleton<GameCursor>.IsInstance()) Singleton<GameCursor>.Instance.UnLockCursor();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                SimpleFileLogger.Info($"Cursor restored: lockState={Cursor.lockState}, visible={Cursor.visible}");
                _cursorStateCaptured = false;
            }
        }
    }
}
