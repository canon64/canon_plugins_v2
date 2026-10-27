using System;
using System.Globalization;
using System.Collections.Generic;
using System.Reflection.Emit;
using Bindings = CanonPlugins.Shared.PluginInput;
using HarmonyLib;
using UnityEngine;

namespace MainGameCameraControl
{
    internal sealed partial class Plugin
    {
        private Harmony _fovInputHarmony;
        private bool FovDiagnosticsEnabled => _cfgDetailLogEnabled != null && _cfgDetailLogEnabled.Value;
        private float _fovDiagnosticsNext;
        private float _fovDiagnosticsUntil;
        private float _fovDiagnosticsNativeNext;
        private string _fovDiagnosticsPreset;
        private bool _fovDiagnosticsTransition;
        private bool _fovDiagnosticsKey;
        private float _fovDiagnosticsRenderNext;
        private bool _quickBoneSwitchMouseHeld;
        private bool _boneReleaseMiddleHeld;
        private static bool _remapNativeFov;

        private void InitializeFovInput()
        {
            _fovInputHarmony = new Harmony(GUID + ".fovInput");
            var target = AccessTools.Method(typeof(BaseCameraControl_Ver2), "InputKeyProc");
            _fovInputHarmony.Patch(target,
                new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(FovInputPrefix))),
                new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(FovInputPostfix))),
                new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(FovBindingTranspiler))));
            _fovInputHarmony.Patch(AccessTools.Method(typeof(BaseCameraControl_Ver2), "InputMouseProc"),
                new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(QuickBoneMousePrefix))));
            Camera.onPreRender += ObserveFovRender;
            LogInfo("[fov-build] mvid=" + typeof(Plugin).Assembly.ManifestModule.ModuleVersionId);
            var patches = Harmony.GetPatchInfo(target);
            if (patches != null)
                LogInfo("[fov-input-patches] owners=" + string.Join(",", patches.Owners));
        }

        private static void FovInputPrefix(BaseCameraControl_Ver2 __instance, out bool __state)
        {
            _remapNativeFov = Instance != null && __instance == Instance._ctrlCamera
                && BlankRoomFovInputAllowed() && !Instance.IsVrFovBlocked() && !Instance.IsExternalFovHoldActive();
            __state = Instance != null && Instance.AcceptsNativeFovInput(__instance);
            if (__state)
            {
                Instance.PrepareNativeFovInput();
                Instance.TraceFovState("native-before");
            }
        }

        private static bool QuickBoneMousePrefix(BaseCameraControl_Ver2 __instance, ref bool __result)
        {
            var plugin = Instance;
            if (plugin == null || __instance != plugin._ctrlCamera || !BlankRoomFovInputAllowed()) return true;
            if (!UnityEngine.Application.isFocused)
            {
                plugin._boneReleaseMiddleHeld = false;
                plugin._quickBoneSwitchMouseHeld = false;
                return true;
            }
            if (!Bindings.MainHeld("MainGameCameraControl.release", "Mouse2 + Ctrl")) plugin._boneReleaseMiddleHeld = false;
            else if (!plugin._boneReleaseMiddleHeld && plugin._boneLinkActive
                && Bindings.Down("MainGameCameraControl.release", "Mouse2 + Ctrl")
                && !plugin._windowPointerActive && !plugin._windowDragging)
                plugin._boneReleaseMiddleHeld = true;
            if (plugin._boneReleaseMiddleHeld)
            {
                __result = false;
                return false;
            }
            if (!UnityEngine.Application.isFocused || !Bindings.MainHeld("MainGameCameraControl.boneCycle", "Mouse1 + Ctrl"))
            {
                plugin._quickBoneSwitchMouseHeld = false;
                return true;
            }
            if (plugin._cfgBoneReleaseByRightClick != null && plugin._cfgBoneReleaseByRightClick.Value
                && Bindings.Held("MainGameCameraControl.boneCycle", "Mouse1 + Ctrl", true))
                plugin._quickBoneSwitchMouseHeld = true;
            if (!plugin._quickBoneSwitchMouseHeld) return true;
            // Consume the complete right-button gesture even if Ctrl is released first.
            __result = false;
            return false;
        }

        private static bool BlankRoomFovInputAllowed()
        {
            return CanonPlugins.Shared.BlankRoomGate.IsInBlankRoom;
        }

        private static void FovInputPostfix(BaseCameraControl_Ver2 __instance, bool __state)
        {
            if (__state && Instance != null)
            {
                // The Japanese ':' key reports Colon; the game's native reset
                // branch only checks Semicolon. Apply the reset ourselves here.
                float fov = Bindings.Down("MainGameCameraControl.fovReset", "Semicolon | Colon")
                    ? Instance._fovResetBase : __instance.CameraFov;
                Instance.ApplyManualBaseFov(fov, activateBeat: false);
                Instance.TraceFovState("native-after");
            }
            _remapNativeFov = false;
        }

        private static IEnumerable<CodeInstruction> FovBindingTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var held = AccessTools.Method(typeof(Input), nameof(Input.GetKey), new[] { typeof(KeyCode) });
            var down = AccessTools.Method(typeof(Input), nameof(Input.GetKeyDown), new[] { typeof(KeyCode) });
            foreach (var instruction in instructions)
            {
                if (instruction.Calls(held)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Plugin), nameof(NativeFovKeyHeld)); }
                else if (instruction.Calls(down)) { instruction.opcode = OpCodes.Call; instruction.operand = AccessTools.Method(typeof(Plugin), nameof(NativeFovKeyDown)); }
                yield return instruction;
            }
        }

        private static bool NativeFovKeyHeld(KeyCode key)
        {
            if (_remapNativeFov && key == KeyCode.Equals) return Bindings.Held("MainGameCameraControl.fovNarrow", "Equals");
            if (_remapNativeFov && key == KeyCode.RightBracket) return Bindings.Held("MainGameCameraControl.fovWide", "RightBracket");
            return Input.GetKey(key);
        }

        private static bool NativeFovKeyDown(KeyCode key)
        {
            if (_remapNativeFov && key == KeyCode.Semicolon) return Bindings.Down("MainGameCameraControl.fovReset", "Semicolon | Colon");
            return Input.GetKeyDown(key);
        }

        private void UpdateFovDiagnostics()
        {
            if (!FovDiagnosticsEnabled || _ctrlCamera == null) return;
            float now = Time.unscaledTime;
            bool key = IsNativeFovKeyActive();
            if (_fovDiagnosticsPreset != _activeCameraPresetName || _fovDiagnosticsTransition != _transitionActive || key != _fovDiagnosticsKey)
            {
                _fovDiagnosticsUntil = now + 2f;
                TraceFovState("state-change", true);
                _fovDiagnosticsPreset = _activeCameraPresetName;
                _fovDiagnosticsTransition = _transitionActive;
                _fovDiagnosticsKey = key;
            }
            if ((_transitionActive || key || now < _fovDiagnosticsUntil) && now >= _fovDiagnosticsNext)
            {
                _fovDiagnosticsNext = now + 0.25f;
                TraceFovState("late", true);
            }
        }

        private void TraceFovState(string stage, bool force = false)
        {
            if (!FovDiagnosticsEnabled || _ctrlCamera == null) return;
            if (!force && Time.unscaledTime < _fovDiagnosticsNativeNext) return;
            // Keep the before/after pair together, at most four pairs per second.
            if (stage == "native-after") _fovDiagnosticsNativeNext = Time.unscaledTime + 0.25f;
            LogInfo("[fov-state] stage=" + stage + " frame=" + Time.frameCount
                + " cam=" + _ctrlCamera.GetInstanceID() + " preset=" + _activeCameraPresetName
                + " base=" + FovNumber(_lastBaseFov) + " beatBase=" + FovNumber(_beatFovBaseFov)
                + " boneBase=" + FovNumber(_activeBoneLinkFov)
                + " from=" + FovNumber(_transitionFrom.Fov) + " target=" + FovNumber(_targetData.Fov)
                + " multiplier=" + FovNumber(_lastBeatCurrentMultiplier)
                + " expected=" + FovNumber(_lastAppliedFov)
                + " control=" + FovNumber(_ctrlCamera.CameraFov) + " reset=" + FovNumber(_ctrlCamera.CameraInitFov)
                + " live=" + FovNumber(_ctrlCamera.thisCmaera != null ? _ctrlCamera.thisCmaera.fieldOfView : -1f)
                + " transition=" + _transitionActive + " bone=" + _boneLinkActive
                + " apply=" + _cfgApplyFov.Value + " beat=" + IsBeatFovEnabled()
                + " external=" + IsExternalFovHoldActive() + " externalReason=" + _externalFovReason);
        }

        private static string FovNumber(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

        private void ObserveFovRender(Camera camera)
        {
            if (!FovDiagnosticsEnabled || _ctrlCamera == null || camera != _ctrlCamera.thisCmaera) return;
            if (Time.unscaledTime < _fovDiagnosticsRenderNext) return;
            bool mismatch = _lastAppliedFov > 0f && Mathf.Abs(camera.fieldOfView - _lastAppliedFov) > 0.02f;
            if (!mismatch && !_transitionActive && !IsNativeFovKeyActive() && Time.unscaledTime >= _fovDiagnosticsUntil) return;
            _fovDiagnosticsRenderNext = Time.unscaledTime + 0.25f;
            TraceFovState(mismatch ? "render-mismatch" : "render", true);
        }
    }
}
