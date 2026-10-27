using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace MainGameUiInputCapture
{
    internal static class UiInputCaptureHSceneWheelPatches
    {
        private static readonly MethodInfo InputGetAxis =
            AccessTools.Method(typeof(Input), nameof(Input.GetAxis), new[] { typeof(string) });
        private static readonly MethodInfo InputGetAxisRaw =
            AccessTools.Method(typeof(Input), nameof(Input.GetAxisRaw), new[] { typeof(string) });
        private static readonly MethodInfo InputGetMouseButton =
            AccessTools.Method(typeof(Input), nameof(Input.GetMouseButton), new[] { typeof(int) });
        private static readonly MethodInfo InputGetMouseButtonDown =
            AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonDown), new[] { typeof(int) });
        private static readonly MethodInfo InputGetMouseButtonUp =
            AccessTools.Method(typeof(Input), nameof(Input.GetMouseButtonUp), new[] { typeof(int) });

        private static readonly MethodInfo CaptureGetAxis =
            AccessTools.Method(typeof(UiInputCaptureApi), nameof(UiInputCaptureApi.GetAxis), new[] { typeof(string) });
        private static readonly MethodInfo CaptureGetAxisRaw =
            AccessTools.Method(typeof(UiInputCaptureApi), nameof(UiInputCaptureApi.GetAxisRaw), new[] { typeof(string) });
        private static readonly MethodInfo CaptureGetMouseButton =
            AccessTools.Method(typeof(UiInputCaptureApi), nameof(UiInputCaptureApi.GetMouseButton), new[] { typeof(int) });
        private static readonly MethodInfo CaptureGetMouseButtonDown =
            AccessTools.Method(typeof(UiInputCaptureApi), nameof(UiInputCaptureApi.GetMouseButtonDown), new[] { typeof(int) });
        private static readonly MethodInfo CaptureGetMouseButtonUp =
            AccessTools.Method(typeof(UiInputCaptureApi), nameof(UiInputCaptureApi.GetMouseButtonUp), new[] { typeof(int) });

        internal static IEnumerable<CodeInstruction> ReplaceMouseInputReads(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(InputGetAxis))
                {
                    yield return new CodeInstruction(OpCodes.Call, CaptureGetAxis);
                }
                else if (instruction.Calls(InputGetAxisRaw))
                {
                    yield return new CodeInstruction(OpCodes.Call, CaptureGetAxisRaw);
                }
                else if (instruction.Calls(InputGetMouseButton))
                {
                    yield return new CodeInstruction(OpCodes.Call, CaptureGetMouseButton);
                }
                else if (instruction.Calls(InputGetMouseButtonDown))
                {
                    yield return new CodeInstruction(OpCodes.Call, CaptureGetMouseButtonDown);
                }
                else if (instruction.Calls(InputGetMouseButtonUp))
                {
                    yield return new CodeInstruction(OpCodes.Call, CaptureGetMouseButtonUp);
                }
                else
                {
                    yield return instruction;
                }
            }
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(HSonyu), "LoopProc", new[] { typeof(bool) })]
        private static IEnumerable<CodeInstruction> HSonyu_LoopProc(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(HHoushi), "LoopProc", new[] { typeof(bool) })]
        private static IEnumerable<CodeInstruction> HHoushi_LoopProc(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(H3PSonyu), "LoopProc", new[] { typeof(bool) })]
        private static IEnumerable<CodeInstruction> H3PSonyu_LoopProc(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(H3PHoushi), "LoopProc", new[] { typeof(bool) })]
        private static IEnumerable<CodeInstruction> H3PHoushi_LoopProc(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(H3PDarkSonyu), "LoopProc", new[] { typeof(bool) })]
        private static IEnumerable<CodeInstruction> H3PDarkSonyu_LoopProc(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(H3PDarkHoushi), "LoopProc", new[] { typeof(bool) })]
        private static IEnumerable<CodeInstruction> H3PDarkHoushi_LoopProc(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(HTestSonyu), "LoopProc", new[] { typeof(bool) })]
        private static IEnumerable<CodeInstruction> HTestSonyu_LoopProc(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(HTestHoushi), "LoopProc", new[] { typeof(bool) })]
        private static IEnumerable<CodeInstruction> HTestHoushi_LoopProc(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(HTest3PSonyu), "LoopProc", new[] { typeof(bool) })]
        private static IEnumerable<CodeInstruction> HTest3PSonyu_LoopProc(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(HTest3PHoushi), "LoopProc", new[] { typeof(bool) })]
        private static IEnumerable<CodeInstruction> HTest3PHoushi_LoopProc(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(HTest3PDarkSonyu), "LoopProc", new[] { typeof(bool) })]
        private static IEnumerable<CodeInstruction> HTest3PDarkSonyu_LoopProc(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(HTest3PDarkHoushi), "LoopProc", new[] { typeof(bool) })]
        private static IEnumerable<CodeInstruction> HTest3PDarkHoushi_LoopProc(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(HandCtrl), "SetIconTexture")]
        private static IEnumerable<CodeInstruction> HandCtrl_SetIconTexture(IEnumerable<CodeInstruction> instructions)
        {
            return ReplaceMouseInputReads(instructions);
        }
    }

    [HarmonyPatch]
    internal static class UiInputCaptureOptionalMouseInputPatches
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            System.Type masturbationMenu = AccessTools.TypeByName("MainGameFreeHMasturbationMenu.Plugin");
            MethodInfo method = masturbationMenu == null
                ? null
                : AccessTools.Method(masturbationMenu, "HandleMouseWheelSpeedControl");
            if (method != null)
            {
                yield return method;
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return UiInputCaptureHSceneWheelPatches.ReplaceMouseInputReads(instructions);
        }
    }
}
