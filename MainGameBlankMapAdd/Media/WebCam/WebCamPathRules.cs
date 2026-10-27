using System;

namespace MainGameBlankMapAdd.Media.WebCam
{
    internal static class WebCamPathRules
    {
        private const string Scheme = "webcam://";

        internal static bool IsWebCamUrl(string path)
        {
            return path != null && path.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase);
        }

        internal static string ExtractDeviceName(string path)
        {
            return IsWebCamUrl(path) ? path.Substring(Scheme.Length).Trim() : string.Empty;
        }
    }
}
