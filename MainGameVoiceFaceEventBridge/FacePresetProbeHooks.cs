using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace MainGameVoiceFaceEventBridge
{
    internal static class FacePresetProbeHooks
    {
        private static Action<string> _logInfo;
        private static Action<string> _logWarn;
        private static Action<string> _logError;

        internal static void Apply(
            Harmony harmony,
            Action<string> logInfo,
            Action<string> logWarn,
            Action<string> logError)
        {
            _logInfo = logInfo;
            _logWarn = logWarn;
            _logError = logError;

            PatchChaControlMethodByName(
                harmony,
                "ChangeEyebrowPtn",
                m => HasFirstParameterType(m, typeof(int)),
                nameof(ChaControlMutatorPrefix));

            PatchChaControlMethodByName(
                harmony,
                "ChangeEyesPtn",
                m => HasFirstParameterType(m, typeof(int)),
                nameof(ChaControlMutatorPrefix));

            PatchChaControlMethodByName(
                harmony,
                "ChangeMouthPtn",
                m => HasFirstParameterType(m, typeof(int)),
                nameof(ChaControlMutatorPrefix));

            PatchChaControlMethodByName(
                harmony,
                "ChangeEyesOpenMax",
                m => HasFirstParameterType(m, typeof(float)),
                nameof(ChaControlMutatorPrefix));

            PatchChaControlMethodByName(
                harmony,
                "ChangeMouthOpenMax",
                m => HasFirstParameterType(m, typeof(float)),
                nameof(ChaControlMutatorPrefix));

            PatchChaControlMethodByName(
                harmony,
                "ChangeHohoAkaRate",
                m => HasFirstParameterType(m, typeof(float)),
                nameof(ChaControlMutatorPrefix));

            PatchPrefix(
                harmony,
                AccessTools.PropertySetter(typeof(ChaControl), "tearsLv"),
                nameof(ChaControlMutatorPrefix),
                "ChaControl.set_tearsLv");
        }

        private static bool HasFirstParameterType(MethodInfo method, Type type)
        {
            if (method == null)
            {
                return false;
            }

            var parameters = method.GetParameters();
            return parameters != null
                && parameters.Length > 0
                && parameters[0].ParameterType == type;
        }

        private static void PatchChaControlMethodByName(
            Harmony harmony,
            string methodName,
            Func<MethodInfo, bool> predicate,
            string prefixMethodName)
        {
            var methods = new List<MethodInfo>();
            foreach (var method in typeof(ChaControl).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (predicate != null && !predicate(method))
                {
                    continue;
                }

                methods.Add(method);
            }

            if (methods.Count == 0)
            {
                _logWarn?.Invoke("[patch][face-probe] target not found: ChaControl." + methodName);
                return;
            }

            foreach (var method in methods)
            {
                string signature = method.Name + "(" + BuildMethodParameterSignature(method) + ")";
                PatchPrefix(
                    harmony,
                    method,
                    prefixMethodName,
                    "ChaControl." + signature);
            }
        }

        private static string BuildMethodParameterSignature(MethodInfo method)
        {
            if (method == null)
            {
                return string.Empty;
            }

            var parameters = method.GetParameters();
            if (parameters == null || parameters.Length == 0)
            {
                return string.Empty;
            }

            var names = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                names[i] = p?.ParameterType != null ? p.ParameterType.Name : "?";
            }

            return string.Join(", ", names);
        }

        private static void PatchPrefix(Harmony harmony, MethodInfo target, string prefixMethodName, string label)
        {
            if (target == null)
            {
                _logWarn?.Invoke("[patch][face-probe] target not found: " + label);
                return;
            }

            var prefix = AccessTools.Method(typeof(FacePresetProbeHooks), prefixMethodName);
            if (prefix == null)
            {
                _logError?.Invoke("[patch][face-probe] prefix method not found: " + prefixMethodName);
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            _logInfo?.Invoke("[patch][face-probe] prefix ok: " + label);
        }

        private static void ChaControlMutatorPrefix(ChaControl __instance, object[] __args, MethodBase __originalMethod)
        {
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                return;
            }

            string methodName = __originalMethod != null
                ? "ChaControl." + __originalMethod.Name
                : "ChaControl.(unknown)";

            string arg0 = "n/a";
            if (__args != null && __args.Length > 0)
            {
                object value = __args[0];
                arg0 = value != null ? value.ToString() : "null";
            }

            plugin.OnFacePresetProbeMutatorCalled(__instance, methodName, arg0);
        }
    }
}
