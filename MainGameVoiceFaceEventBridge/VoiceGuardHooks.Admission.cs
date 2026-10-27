using System;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using LoadLease = MainGameVoiceFaceEventBridge.Game.GameVoiceLoadLedger<Manager.Voice.Loader>.Lease;

namespace MainGameVoiceFaceEventBridge
{
    internal static partial class VoiceGuardHooks
    {
        private static void ApplyNativeAdmission(Harmony harmony)
        {
            var loaderArgs = new[] { typeof(Manager.Voice.Loader) };
            harmony.Patch(AccessTools.Method(typeof(Manager.Voice), "Play", loaderArgs),
                finalizer: new HarmonyMethod(typeof(VoiceGuardHooks), nameof(NativeLoadFinalizer)));
            harmony.Patch(AccessTools.Method(typeof(Manager.Voice), "PlayAsync", loaderArgs),
                prefix: new HarmonyMethod(typeof(VoiceGuardHooks), nameof(NativeAsyncPrefix)),
                postfix: new HarmonyMethod(typeof(VoiceGuardHooks), nameof(NativeAsyncPostfix)),
                finalizer: new HarmonyMethod(typeof(VoiceGuardHooks), nameof(NativeAsyncFinalizer)));
            foreach (string name in new[] { "OncePlayAsync", "OncePlayCharaAsync" })
                harmony.Patch(AccessTools.Method(typeof(Manager.Voice), name, loaderArgs),
                    prefix: new HarmonyMethod(typeof(VoiceGuardHooks), nameof(NativeAsyncWrapperPrefix)));
            harmony.Patch(AccessTools.Method(typeof(Manager.Voice), "Play_Standby"),
                prefix: new HarmonyMethod(typeof(VoiceGuardHooks), nameof(NativeStandbyPrefix)));
            foreach (var method in typeof(Manager.Voice).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                if (method.Name == "Stop" || method.Name == "StopAll")
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(VoiceGuardHooks), nameof(NativeStopPrefix)));
            _logInfo?.Invoke("[patch] native voice sync/async admission and cancellation installed");
        }

        private static Exception NativeLoadFinalizer(Exception __exception, LoadLease __state)
        {
            Plugin.Instance?.FinishNativeVoiceLoad(__state);
            return __exception;
        }

        private static bool NativeAsyncWrapperPrefix(ref UniTask<AudioSource> __result)
        {
            if (!ShouldBlock()) return true;
            __result = UniTask.FromResult<AudioSource>(null);
            return false; // Also prevent OncePlay* from stopping existing voices.
        }

        private static bool NativeAsyncPrefix(Manager.Voice.Loader loader, ref UniTask<AudioSource> __result, out LoadLease __state)
        {
            __state = null;
            if (!NativeAsyncWrapperPrefix(ref __result)) return false;
            __state = Plugin.Instance?.BeginNativeVoiceLoad(loader);
            return true;
        }

        private static void NativeAsyncPostfix(ref UniTask<AudioSource> __result, LoadLease __state)
        {
            if (__state != null) __result = ObserveNativeLoad(__result, Plugin.Instance, __state);
        }

        private static async UniTask<AudioSource> ObserveNativeLoad(UniTask<AudioSource> task, Plugin owner, LoadLease lease)
        {
            try { return await task; }
            finally { if (owner != null) owner.FinishNativeVoiceLoad(lease); }
        }

        private static Exception NativeAsyncFinalizer(Exception __exception, LoadLease __state)
        {
            if (__exception != null) Plugin.Instance?.FinishNativeVoiceLoad(__state);
            return __exception;
        }

        private static bool NativeStandbyPrefix(AudioSource audioSource, Manager.Voice.Loader loader)
        {
            var plugin = Plugin.Instance;
            if (plugin == null) return true;
            if (!plugin.RejectNativeVoiceStandby(loader))
            {
                plugin.RegisterNativeVoiceStandby(audioSource, loader);
                return true;
            }
            // Reject before the game's deferred AudioSource.Play subscription is installed.
            if (audioSource != null) UnityEngine.Object.Destroy(audioSource.gameObject);
            return false;
        }

        private static void NativeStopPrefix(object[] __args, System.Reflection.MethodBase __originalMethod)
        {
            int? no = null;
            Transform target = null;
            bool hasTarget = false;
            var parameters = __originalMethod.GetParameters();
            for (int i = 0; i < __args.Length; i++)
            {
                object arg = __args[i];
                if (arg is int voiceNo) no = voiceNo;
                if (parameters[i].ParameterType == typeof(Transform)) { target = arg as Transform; hasTarget = true; }
            }
            if (hasTarget && __args.Length == 1 && target == null) return;
            Plugin.Instance?.CancelNativeVoiceLoads(no, target, hasTarget);
        }
    }
}
