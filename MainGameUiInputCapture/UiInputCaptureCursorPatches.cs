using HarmonyLib;

namespace MainGameUiInputCapture
{
    // GameCursor uses Win32 SetCursorPos independently of Unity Cursor.lockState.
    // Prevent the warp at its source: unlocking it afterwards already moves the pointer back.
    internal static class UiInputCaptureCursorPatches
    {
        private static bool Blocked => Plugin.Instance != null
            && Plugin.Instance.Runtime.PreventGameCursorWarp;

        // Block camera input itself, regardless of other plugins replacing NoCtrlCondition.
        // CameraUpdate still runs so tracking and programmed camera motion remain intact.
        [HarmonyPrefix, HarmonyPatch(typeof(BaseCameraControl_Ver2), "InputMouseProc")]
        private static bool BeforeCameraMouse(ref bool __result)
        {
            if (Plugin.Instance == null || !Plugin.Instance.Runtime.BlockCameraPointerInput) return true;
            __result = false;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(BaseCameraControl_Ver2), "InputMouseWheelZoomProc")]
        private static bool BeforeCameraWheel(ref bool __result)
        {
            if (Plugin.Instance == null || !Plugin.Instance.Runtime.BlockCameraPointerInput) return true;
            __result = false;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GameCursor), "SetCursorLock")]
        private static bool SetLock(GameCursor __instance)
        {
            if (!Blocked) return true;
            __instance.UnLockCursor();
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GameCursor), "Update")]
        private static void BeforeUpdate(GameCursor __instance)
        {
            if (Blocked) __instance.UnLockCursor();
        }

        [HarmonyPrefix, HarmonyPatch(typeof(GameCursor), "UpdateCursorLock")]
        private static void BeforeLockUpdate(GameCursor __instance)
        {
            if (Blocked) __instance.UnLockCursor();
        }
    }
}
