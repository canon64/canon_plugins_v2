using MainGameBlankMapAdd.Media.WebCam;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private WebCamSessionService _webCamSessions;

        private void InitializeWebCamSessions()
        {
            _webCamSessions = new WebCamSessionService(() => _settings, LogInfo, LogWarn);
        }

        private static bool IsWebCamUrl(string path) => WebCamPathRules.IsWebCamUrl(path);

        private static string ExtractWebCamDeviceName(string path) => WebCamPathRules.ExtractDeviceName(path);

        private void LogAvailableWebCamDevices() => _webCamSessions.WriteAvailableDevices(_paths.WebCamDevicesFile);

        private void TryAttachWebCam(Material material, string deviceName) => _webCamSessions.Attach(material, deviceName);

        private void UpdateWebCamTextures() => _webCamSessions?.Update();

        private void DestroyWebCamTextures() => _webCamSessions?.Clear();
    }
}
