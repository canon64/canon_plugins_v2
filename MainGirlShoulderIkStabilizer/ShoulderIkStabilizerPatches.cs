using HarmonyLib;
using CanonPlugins.Shared;

namespace MainGirlShoulderIkStabilizer;

internal static class ShoulderIkStabilizerPatches
{
	[HarmonyPostfix]
	[HarmonyPatch(typeof(HSceneProc), "LateUpdate")]
	private static void Postfix_HSceneProcLateUpdate(HSceneProc __instance)
	{
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
		ShoulderIkStabilizerPlugin.Instance?.OnAfterHSceneLateUpdate(__instance);
	}
}
