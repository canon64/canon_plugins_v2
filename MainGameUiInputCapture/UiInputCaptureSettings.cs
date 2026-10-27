using System;

namespace MainGameUiInputCapture
{
    [Serializable]
    internal sealed class UiInputCaptureSettings
    {
        public bool VerboseLog = false; // legacy (unused)
        public bool DetailLogEnabled = false;
        public bool LogStateOnTransition = false;
    }
}
