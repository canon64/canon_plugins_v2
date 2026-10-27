using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using UnityEngine;
using Valve.VR;
using VRGIN.Controls;
using VRGIN.Core;

namespace MainGameClubLights
{
    public sealed partial class Plugin
    {
        // グラブ中のエントリ（null = 未グラブ）
        private LightEntry _vrGrabbedEntry;
        private Controller  _vrGrabbedController;
        private Controller.Lock _vrGrabbedControllerFocusLock = Controller.Lock.Invalid;
        private Vector3    _vrGrabOffset;

        private void UpdateVRLightGrab()
        {
            if (!VR.Active || VR.Mode == null)
            {
                StopVRLightGrab();
                StopFolderVrGrab();
                return;
            }

            var right = VR.Mode.Right;
            var left  = VR.Mode.Left;

            // フォルダ掴み中はそれを優先
            if (_grabbedFolder != null)
            {
                UpdateFolderVrGrab();
                return;
            }

            if (_vrGrabbedEntry != null)
            {
                // グラブ中
                UpdateGrabbedLight();
            }
            else
            {
                // グラブ開始チェック（フォルダハンドル → 個別ライト、右→左の優先順）
                if (right != null && TryBeginFolderGrab(right)) return;
                if (left  != null && TryBeginFolderGrab(left))  return;
                if (right != null && TryBeginGrab(right)) return;
                if (left  != null && TryBeginGrab(left))  return;
            }
        }

        private bool TryBeginGrab(Controller ctrl)
        {
            if (ctrl == null || ctrl.Input == null) return false;
            if (!Bindings.VrDown("MainGameClubLights.grab", ctrl.Input, "Grip")) return false;

            Transform ctrlTf = ((Component)ctrl).transform;
            float minDist    = float.MaxValue;
            LightEntry best  = null;

            foreach (var entry in _lightEntries)
            {
                if (entry.Go == null) continue;
                float dist = Vector3.Distance(ctrlTf.position, entry.Go.transform.position);
                if (dist < 0.3f && dist < minDist)
                {
                    minDist = dist;
                    best    = entry;
                }
            }

            if (best == null) return false;

            if (!ctrl.TryAcquireFocus(out Controller.Lock focusLock) || focusLock == null || !focusLock.IsValid)
            {
                _log.Warn($"[VRGrab] rejected: controller focus acquire failed controller={ctrl.name}");
                return false;
            }

            // グラブ開始 → 自由配置モードに切替
            if (best.Settings.FollowCamera)
                SetLightFollowCamera(best, false);
            if (best.Settings.RevolutionEnabled)
                SetRevolutionEnabled(best, false);

            _vrGrabbedEntry      = best;
            _vrGrabbedController = ctrl;
            _vrGrabbedControllerFocusLock = focusLock;
            _vrGrabOffset        = best.Go.transform.position - ctrlTf.position;
            _log.Info($"[VRGrab] start id={best.Settings.Id} controller={ctrl.name}");
            return true;
        }

        private void UpdateGrabbedLight()
        {
            if (_vrGrabbedEntry?.Go == null || _vrGrabbedController == null || _vrGrabbedController.Input == null)
            {
                StopVRLightGrab();
                return;
            }

            if (!Bindings.VrHeld("MainGameClubLights.grab", _vrGrabbedController.Input, "Grip"))
            {
                StopVRLightGrab();
                SaveCurrentProfile(out _);
                return;
            }

            Transform ctrlTf = ((Component)_vrGrabbedController).transform;
            _vrGrabbedEntry.Go.transform.position = ctrlTf.position + _vrGrabOffset;
        }

        private void StopVRLightGrab()
        {
            if (_vrGrabbedEntry != null)
            {
                var pos = _vrGrabbedEntry.Go.transform.position;
                _vrGrabbedEntry.Settings.WorldPosX = pos.x;
                _vrGrabbedEntry.Settings.WorldPosY = pos.y;
                _vrGrabbedEntry.Settings.WorldPosZ = pos.z;
                _log.Info($"[VRGrab] end id={_vrGrabbedEntry.Settings.Id} pos={pos}");
            }
            ReleaseVRLightGrabFocus();
            _vrGrabbedEntry      = null;
            _vrGrabbedController = null;
        }

        private void ReleaseVRLightGrabFocus()
        {
            if (_vrGrabbedControllerFocusLock == null || !_vrGrabbedControllerFocusLock.IsValid)
            {
                _vrGrabbedControllerFocusLock = Controller.Lock.Invalid;
                return;
            }

            try
            {
                _vrGrabbedControllerFocusLock.SafeRelease();
            }
            catch (Exception ex)
            {
                _log.Warn("[VRGrab] controller focus release failed: " + ex.Message);
            }

            _vrGrabbedControllerFocusLock = Controller.Lock.Invalid;
        }
    }
}
