using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private static readonly PropertyInfo MotionIkSolver = AccessTools.Property(typeof(MotionIK), "ik");

        // Morph completion recalculates H-scene targets and re-enables FinalIK.
        // Keep that recalculation, but retain VMD ownership of this character's limbs.
        private static void AfterMotionIkRecalculation(MotionIK __instance)
        {
            var p = Instance;
            if (p == null || !p._vmdOwnsCharacter || p._originalIk == null || p._controller == null) return;
            if (!(bool)p.ReadMember(p._controller, "VMDAnimEnabled") || (bool)p.ReadMember(p._controller, "FaceOnly")) return;
            var solver = MotionIkSolver.GetValue(__instance, null) as Behaviour;
            if (solver != p._originalIk || !solver.enabled) return;
            solver.enabled = false;
            p.LogInfo("[VMD] retained limb control after MotionIK recalculation");
        }
    }
}
