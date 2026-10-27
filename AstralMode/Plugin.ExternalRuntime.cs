using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private const float ExternalAstralYOffset = -100f;

        private void BeginExternalAstralRuntime()
        {
            InvokeExternalStatic(
                "MainGameBlankMapAdd.Plugin",
                "BeginAstralModeSuppression",
                new Type[] { typeof(float), typeof(float) },
                new object[] { ExternalAstralYOffset, Mathf.Max(0.1f, _cfgWhiteHoldDuration != null ? _cfgWhiteHoldDuration.Value : 0.5f) });

            InvokeExternalStatic(
                "MainGameClubLights.Plugin",
                "BeginAstralModeSuppression",
                Type.EmptyTypes,
                new object[0]);
        }

        private void EndExternalAstralRuntime()
        {
            InvokeExternalStatic(
                "MainGameBlankMapAdd.Plugin",
                "EndAstralModeSuppression",
                Type.EmptyTypes,
                new object[0]);

            InvokeExternalStatic(
                "MainGameClubLights.Plugin",
                "EndAstralModeSuppression",
                Type.EmptyTypes,
                new object[0]);
        }

        private static bool InvokeExternalStatic(string typeName, string methodName, Type[] parameterTypes, object[] args)
        {
            try
            {
                Type type = AccessTools.TypeByName(typeName);
                if (type == null)
                {
                    SimpleFileLogger.Warn("External runtime call skipped: type not found " + typeName + "." + methodName);
                    return false;
                }

                MethodInfo method = AccessTools.Method(type, methodName, parameterTypes);
                if (method == null)
                {
                    SimpleFileLogger.Warn("External runtime call skipped: method not found " + typeName + "." + methodName);
                    return false;
                }

                object result = method.Invoke(null, args);
                SimpleFileLogger.Info("External runtime call " + typeName + "." + methodName + " result=" + (result != null ? result.ToString() : "null"));
                return result is bool ? (bool)result : true;
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn("External runtime call failed: " + typeName + "." + methodName + " error=" + ex.Message);
                return false;
            }
        }
    }
}
