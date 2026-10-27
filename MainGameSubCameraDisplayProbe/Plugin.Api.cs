using System;
using System.Collections.Generic;

namespace MainGameSubCameraDisplayProbe
{
    public static class MainGameSubCameraDisplayProbeApi
    {
        public static bool TryLoadPresetByName(string presetName, out string reason)
        {
            return Plugin.TryLoadPresetByName(presetName, out reason);
        }

        public static bool TryGetPresetNames(out string[] names, out string reason)
        {
            names = new string[0];
            reason = string.Empty;
            try
            {
                if (!Plugin.TryGetPresetNamesInternalApi(out string[] result, out string innerReason))
                {
                    reason = innerReason;
                    return false;
                }

                names = result ?? new string[0];
                reason = "ok";
                return true;
            }
            catch (Exception ex)
            {
                reason = "exception:" + ex.Message;
                return false;
            }
        }

        public static bool TryGetRenderTexture(out UnityEngine.RenderTexture renderTexture, out string reason)
        {
            renderTexture = null;
            reason = string.Empty;
            try
            {
                return Plugin.TryGetRenderTextureInternalApi(out renderTexture, out reason);
            }
            catch (Exception ex)
            {
                reason = "exception:" + ex.Message;
                return false;
            }
        }

        /// <summary>現在のサブカメラと、呼び出しキャラがその構図を専有しているかを返す。</summary>
        public static bool TryGetKaleidoscopeSource(out UnityEngine.Camera camera, out ChaControl calledActor, out string reason)
        {
            camera = null;
            calledActor = null;
            reason = string.Empty;
            Plugin plugin = Plugin.Instance;
            if (plugin == null) { reason = "instance_null"; return false; }
            return plugin.TryGetKaleidoscopeSource(out camera, out calledActor, out reason);
        }

        public static bool TryGetUiVisible(out bool visible)
        {
            return Plugin.TryGetUiVisible(out visible);
        }

        public static bool TrySetUiVisible(bool visible)
        {
            return Plugin.TrySetUiVisible(visible);
        }

        public static bool SuspendForAstralMode()
        {
            return Plugin.SuspendForAstralMode();
        }

        public static bool ResumeFromAstralMode()
        {
            return Plugin.ResumeFromAstralMode();
        }
    }

    public sealed partial class Plugin
    {
        internal bool TryGetKaleidoscopeSource(out UnityEngine.Camera camera, out ChaControl calledActor, out string reason)
        {
            camera = null;
            calledActor = null;
            if (_subCamera == null || !_subCamera.isActiveAndEnabled || _renderTexture == null)
            { reason = "camera_not_ready"; return false; }
            if (_suspendedForAstralMode) { reason = "camera_suspended"; return false; }
            camera = _subCamera;
            calledActor = GetKaleidoscopeCalledActor();
            reason = "ok";
            return true;
        }
    }
}
