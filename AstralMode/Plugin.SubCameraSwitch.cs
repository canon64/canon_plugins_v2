using System;
using HarmonyLib;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private bool _realSubCameraSuspendedForAstral;

        private void SuspendMainGameSubCameraForAstral(string phase)
        {
            if (_realSubCameraSuspendedForAstral)
                return;

            bool ok = InvokeMainGameSubCameraApi("SuspendForAstralMode");
            _realSubCameraSuspendedForAstral = ok;
            SimpleFileLogger.Info($"MainGameSubCamera switch phase={phase} suspend ok={ok}");
        }

        private void ResumeMainGameSubCameraFromAstral(string phase)
        {
            if (!_realSubCameraSuspendedForAstral)
                return;

            bool ok = InvokeMainGameSubCameraApi("ResumeFromAstralMode");
            if (ok)
                _realSubCameraSuspendedForAstral = false;
            SimpleFileLogger.Info($"MainGameSubCamera switch phase={phase} resume ok={ok}");
        }

        private static bool InvokeMainGameSubCameraApi(string methodName)
        {
            try
            {
                Type apiType = AccessTools.TypeByName("MainGameSubCameraDisplayProbe.MainGameSubCameraDisplayProbeApi")
                    ?? AccessTools.TypeByName("MainGameSubCameraDisplayProbe.Plugin");
                if (apiType == null)
                {
                    SimpleFileLogger.Warn("MainGameSubCamera API type not found");
                    return false;
                }

                var method = AccessTools.Method(apiType, methodName);
                if (method == null)
                {
                    SimpleFileLogger.Warn("MainGameSubCamera API method not found: " + methodName);
                    return false;
                }

                object result = method.Invoke(null, null);
                return result is bool b && b;
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn("MainGameSubCamera API invoke failed: " + methodName + " " + ex.Message);
                return false;
            }
        }
    }
}
