using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed class IndependentActorMarker : MonoBehaviour { }
    public static class IndependentActorApi
    {
        public static ChaControl GetReadyActor() => Plugin.Instance == null ? null : Plugin.Instance.ReadyIndependentActor();
        public static bool IsIndependentActor(ChaControl actor) => actor != null && actor.GetComponent<IndependentActorMarker>() != null;
    }
    internal sealed class ActorLoadSession
    {
        internal ChaControl Actor;
        internal readonly OwnedLoadGeneration<GameObject> Generation = new OwnedLoadGeneration<GameObject>(obj => { if (obj != null) UnityEngine.Object.Destroy(obj); });
        internal Exception Error => Generation.Error;
        internal void Accept(GameObject obj, Action<GameObject> action) => Generation.Accept(obj, Actor != null, action);
        internal void Cancel() {
            Generation.Cancel();
            if (Actor != null) Actor.StopAllCoroutines();
        }
    }

    internal static class OwnedCharacterLoader
    {
        internal static ActorLoadSession Creating;
        internal static readonly Dictionary<ChaControl, ActorLoadSession> Sessions = new Dictionary<ChaControl, ActorLoadSession>();
        internal static void Initialize(Harmony harmony) {
            var targets = new List<MethodInfo>();
            foreach (string name in new[] { "LoadAsync", "LoadCharaFbxDataAsync" }) {
                var method = AccessTools.Method(typeof(ChaControl), name);
                var state = method.GetCustomAttribute<IteratorStateMachineAttribute>().StateMachineType;
                if (AccessTools.Field(state, "<>4__this")?.FieldType != typeof(ChaControl)) throw new InvalidOperationException("ロードowner非対応: " + name);
                targets.Add(AccessTools.Method(state, "MoveNext"));
            }
            harmony.Patch(AccessTools.Method(typeof(ChaControl), "Initialize"), prefix: new HarmonyMethod(typeof(OwnedCharacterLoader), nameof(BeforeInitialize)));
            foreach (var target in targets) harmony.Patch(target, transpiler: new HarmonyMethod(typeof(OwnedCharacterLoader), nameof(Transpile)));
        }
        private static void BeforeInitialize(ChaControl __instance) {
            if (Creating == null || !ReferenceEquals(Creating.Actor, null)) return;
            Creating.Actor = __instance;
            __instance.gameObject.AddComponent<IndependentActorMarker>();
            Sessions.Add(__instance, Creating);
        }
        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod) {
            var result = new List<CodeInstruction>();
            int count = 0;
            foreach (var instruction in instructions) {
                var call = instruction.operand as MethodInfo;
                if (call != null && call.DeclaringType == typeof(ChaControl) && call.Name == "Load_Coroutine") {
                    if (!call.IsGenericMethod || call.GetGenericArguments()[0] != typeof(GameObject) || call.GetParameters().Length != 5) throw new InvalidOperationException("ロード契約不一致");
                    var owner = new CodeInstruction(OpCodes.Ldarg_0); owner.labels.AddRange(instruction.labels); instruction.labels.Clear();
                    owner.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                    result.Add(owner);
                    result.Add(new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(__originalMethod.DeclaringType, "<>4__this")));
                    result.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(OwnedCharacterLoader), nameof(Load))));
                    count++;
                } else result.Add(instruction);
            }
            int expected = __originalMethod.DeclaringType.Name.StartsWith("<LoadAsync>") ? 7 : 1;
            if (count != expected) throw new InvalidOperationException("ロード差替え件数不一致: " + count);
            return result;
        }
        private static IEnumerator Load(string bundle, string asset, Action<GameObject> action, bool xxx, string manifest, ChaControl owner) {
            if (!Sessions.TryGetValue(owner, out var session)) return ChaControl.Load_Coroutine(bundle, asset, action, xxx, manifest);
            return ChaControl.Load_Coroutine<GameObject>(bundle, asset, obj => session.Accept(obj, action), xxx, manifest);
        }
    }
}
