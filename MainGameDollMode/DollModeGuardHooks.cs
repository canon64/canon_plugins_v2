using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MainGameDollMode
{
    internal static class DollModeGuardHooks
    {
        private static Action<string> _logInfo;
        private static Action<string> _logWarn;
        private static Action<string> _logError;
        private static readonly List<Type> _fixationalTypes = new List<Type>();
        private static bool _ahegaoPatchReady;
        private static bool _ahegaoMissingLogged;
        private static bool _ahegaoFoundButUnreadyLogged;

        internal static void Apply(
            Harmony harmony,
            Action<string> logInfo,
            Action<string> logWarn,
            Action<string> logError)
        {
            _logInfo = logInfo;
            _logWarn = logWarn;
            _logError = logError;
            _ahegaoPatchReady = false;
            _ahegaoMissingLogged = false;
            _ahegaoFoundButUnreadyLogged = false;

            PatchPrefix(harmony, FindHVoiceCtrlProcMethod(), nameof(HVoiceCtrlProcPrefix), "HVoiceCtrl.Proc");
            PatchPrefix(harmony, FindHVoiceCtrlPlayVoiceMethod(), nameof(HVoiceCtrlPlayVoicePrefix), "HVoiceCtrl.PlayVoice");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(Manager.Voice), "Play", new[] { typeof(Manager.Voice.Loader) }),
                nameof(ManagerVoicePlayPrefix),
                "Manager.Voice.Play");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(Manager.Voice), "OncePlay", new[] { typeof(Manager.Voice.Loader) }),
                nameof(ManagerVoiceOncePlayPrefix),
                "Manager.Voice.OncePlay");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(Manager.Voice), "OncePlayChara", new[] { typeof(Manager.Voice.Loader) }),
                nameof(ManagerVoiceOncePlayCharaPrefix),
                "Manager.Voice.OncePlayChara");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(HandCtrl), "IsKissAction", Type.EmptyTypes),
                nameof(HandCtrlIsKissActionPrefix),
                "HandCtrl.IsKissAction");
            PatchPostfix(
                harmony,
                AccessTools.Method(typeof(HandCtrl), "GetOnMouseAibuCollider", Type.EmptyTypes),
                nameof(HandCtrlGetOnMouseAibuColliderPostfix),
                "HandCtrl.GetOnMouseAibuCollider");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(EyeLookController), "LateUpdate", Type.EmptyTypes),
                nameof(EyeLookControllerLateUpdatePrefix),
                "EyeLookController.LateUpdate");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(EyeLookController), "ForceLateUpdate", Type.EmptyTypes),
                nameof(EyeLookControllerForceLateUpdatePrefix),
                "EyeLookController.ForceLateUpdate");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(EyeLookCalc), "EyeUpdateCalc", new[] { typeof(Vector3), typeof(int) }),
                nameof(EyeLookCalcEyeUpdateCalcPrefix),
                "EyeLookCalc.EyeUpdateCalc");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(EyeLookMaterialControll), "Update", Type.EmptyTypes),
                nameof(EyeLookMaterialControllUpdatePrefix),
                "EyeLookMaterialControll.Update");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(ChaControl), "ChangeEyebrowPtn", new[] { typeof(int), typeof(bool) }),
                nameof(ChaControlChangeEyebrowPtnPrefix),
                "ChaControl.ChangeEyebrowPtn");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(ChaControl), "ChangeEyesPtn", new[] { typeof(int), typeof(bool) }),
                nameof(ChaControlChangeEyesPtnPrefix),
                "ChaControl.ChangeEyesPtn");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(ChaControl), "ChangeMouthPtn", new[] { typeof(int), typeof(bool) }),
                nameof(ChaControlChangeMouthPtnPrefix),
                "ChaControl.ChangeMouthPtn");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(ChaControl), "ChangeEyesOpenMax", new[] { typeof(float) }),
                nameof(ChaControlChangeEyesOpenMaxPrefix),
                "ChaControl.ChangeEyesOpenMax");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(ChaControl), "ChangeMouthOpenMax", new[] { typeof(float) }),
                nameof(ChaControlChangeMouthOpenMaxPrefix),
                "ChaControl.ChangeMouthOpenMax");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(ChaControl), "ChangeMouthFixed", new[] { typeof(bool) }),
                nameof(ChaControlChangeMouthFixedPrefix),
                "ChaControl.ChangeMouthFixed");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(ChaControl), "ChangeHohoAkaRate", new[] { typeof(float) }),
                nameof(ChaControlChangeHohoAkaRatePrefix),
                "ChaControl.ChangeHohoAkaRate");
            PatchPrefix(
                harmony,
                AccessTools.PropertySetter(typeof(ChaControl), "tearsLv"),
                nameof(ChaControlSetTearsLvPrefix),
                "ChaControl.set_tearsLv");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(ChaControl), "SetSiruFlags", new[] { typeof(ChaFileDefine.SiruParts), typeof(byte) }),
                nameof(ChaControlSetSiruFlagsPrefix),
                "ChaControl.SetSiruFlags");
            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(ChaControl), "ChangeLookEyesPtn", new[] { typeof(int) }),
                nameof(ChaControlChangeLookEyesPtnPrefix),
                "ChaControl.ChangeLookEyesPtn");
            List<MethodInfo> changeLookEyesTargetMethods = FindChaControlChangeLookEyesTargetMethods();
            if (changeLookEyesTargetMethods.Count == 0)
            {
                _logWarn?.Invoke("[guard][patch] prefix target not found: ChaControl.ChangeLookEyesTarget");
            }
            else
            {
                for (int i = 0; i < changeLookEyesTargetMethods.Count; i++)
                {
                    MethodInfo target = changeLookEyesTargetMethods[i];
                    string label = "ChaControl.ChangeLookEyesTarget(" + target.GetParameters().Length + ")";
                    PatchPrefix(harmony, target, nameof(ChaControlChangeLookEyesTargetPrefix), label);
                }
            }

            PatchPrefix(
                harmony,
                AccessTools.Method(typeof(HActionBase), "SetDefaultAnimatorFloat", Type.EmptyTypes),
                nameof(HActionBaseSetDefaultAnimatorFloatPrefix),
                "HActionBase.SetDefaultAnimatorFloat");

            ApplyFixationalEyeMovementBlock(harmony);
            ApplyMainGameAhegaoBlock(harmony, fromRetry: false);
        }

        internal static Type[] GetFixationalTypesSnapshot()
        {
            return _fixationalTypes.ToArray();
        }

        internal static bool EnsureMainGameAhegaoBlock(Harmony harmony)
        {
            return ApplyMainGameAhegaoBlock(harmony, fromRetry: true);
        }

        private static MethodInfo FindHVoiceCtrlProcMethod()
        {
            foreach (MethodInfo method in typeof(HVoiceCtrl).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != "Proc" || method.ReturnType != typeof(bool))
                {
                    continue;
                }

                ParameterInfo[] p = method.GetParameters();
                if (p.Length == 2
                    && p[0].ParameterType != null
                    && p[0].ParameterType.FullName == "UnityEngine.AnimatorStateInfo")
                {
                    return method;
                }
            }

            return null;
        }

        private static MethodInfo FindHVoiceCtrlPlayVoiceMethod()
        {
            foreach (MethodInfo method in typeof(HVoiceCtrl).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method.Name != "PlayVoice" || method.ReturnType != typeof(bool))
                {
                    continue;
                }

                ParameterInfo[] p = method.GetParameters();
                if (p.Length == 7 && p[0].ParameterType == typeof(ChaControl))
                {
                    return method;
                }
            }

            return null;
        }

        private static List<MethodInfo> FindChaControlChangeLookEyesTargetMethods()
        {
            var methods = new List<MethodInfo>();
            foreach (MethodInfo method in typeof(ChaControl).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method == null || method.Name != "ChangeLookEyesTarget")
                {
                    continue;
                }

                methods.Add(method);
            }

            return methods;
        }

        private static void PatchPrefix(Harmony harmony, MethodInfo target, string prefixMethodName, string label)
        {
            if (target == null)
            {
                _logWarn?.Invoke("[guard][patch] prefix target not found: " + label);
                return;
            }

            if (IsAlreadyPatchedByOwner(target, harmony))
            {
                return;
            }

            MethodInfo prefix = AccessTools.Method(typeof(DollModeGuardHooks), prefixMethodName);
            if (prefix == null)
            {
                _logError?.Invoke("[guard][patch] prefix method not found: " + prefixMethodName);
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            _logInfo?.Invoke("[guard][patch] prefix ok: " + label);
        }

        private static void PatchPostfix(Harmony harmony, MethodInfo target, string postfixMethodName, string label)
        {
            if (target == null)
            {
                _logWarn?.Invoke("[guard][patch] postfix target not found: " + label);
                return;
            }

            if (IsAlreadyPatchedByOwner(target, harmony))
            {
                return;
            }

            MethodInfo postfix = AccessTools.Method(typeof(DollModeGuardHooks), postfixMethodName);
            if (postfix == null)
            {
                _logError?.Invoke("[guard][patch] postfix method not found: " + postfixMethodName);
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            _logInfo?.Invoke("[guard][patch] postfix ok: " + label);
        }

        private static bool IsAlreadyPatchedByOwner(MethodInfo target, Harmony harmony)
        {
            if (target == null || harmony == null)
            {
                return false;
            }

            try
            {
                Patches info = Harmony.GetPatchInfo(target);
                if (info == null || info.Owners == null)
                {
                    return false;
                }

                string owner = harmony.Id;
                if (string.IsNullOrWhiteSpace(owner))
                {
                    return false;
                }

                for (int i = 0; i < info.Owners.Count; i++)
                {
                    if (string.Equals(info.Owners[i], owner, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // ignore patch info read failure
            }

            return false;
        }

        private static bool ShouldBlock()
        {
            Plugin plugin = Plugin.Instance;
            return plugin != null && plugin.ShouldBlockGameEvents();
        }

        private static bool HVoiceCtrlProcPrefix(ref bool __result)
        {
            if (!ShouldBlock())
            {
                return true;
            }

            __result = true;
            Plugin.Instance?.OnGameEventBlocked("HVoiceCtrl.Proc");
            return false;
        }

        private static bool HVoiceCtrlPlayVoicePrefix(ref bool __result)
        {
            if (!ShouldBlock())
            {
                return true;
            }

            __result = false;
            Plugin.Instance?.OnGameEventBlocked("HVoiceCtrl.PlayVoice");
            return false;
        }

        private static bool ManagerVoicePlayPrefix(Manager.Voice.Loader loader, ref AudioSource __result)
        {
            if (!ShouldBlock())
            {
                return true;
            }

            __result = null;
            Plugin.Instance?.OnGameEventBlocked("Manager.Voice.Play");
            return false;
        }

        private static bool ManagerVoiceOncePlayPrefix(Manager.Voice.Loader loader, ref AudioSource __result)
        {
            if (!ShouldBlock())
            {
                return true;
            }

            __result = null;
            Plugin.Instance?.OnGameEventBlocked("Manager.Voice.OncePlay");
            return false;
        }

        private static bool ManagerVoiceOncePlayCharaPrefix(Manager.Voice.Loader loader, ref AudioSource __result)
        {
            if (!ShouldBlock())
            {
                return true;
            }

            __result = null;
            Plugin.Instance?.OnGameEventBlocked("Manager.Voice.OncePlayChara");
            return false;
        }

        private static bool HandCtrlIsKissActionPrefix(ref bool __result)
        {
            if (!ShouldBlock())
            {
                return true;
            }

            __result = false;
            Plugin.Instance?.OnKissEventBlocked("HandCtrl.IsKissAction");
            return false;
        }

        private static void HandCtrlGetOnMouseAibuColliderPostfix(ref HandCtrl.AibuColliderKind __result)
        {
            if (!ShouldBlock())
            {
                return;
            }

            if (__result != HandCtrl.AibuColliderKind.mouth)
            {
                return;
            }

            __result = HandCtrl.AibuColliderKind.none;
            Plugin.Instance?.OnKissEventBlocked("HandCtrl.GetOnMouseAibuCollider.mouth");
        }

        private static bool EyeLookControllerLateUpdatePrefix(EyeLookController __instance)
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockEyeLook(__instance);
            plugin?.OnCauseProbe("EyeLookController.LateUpdate", blocked, null, blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            plugin.OnGameEventBlocked("EyeLookController.LateUpdate");
            return false;
        }

        private static bool EyeLookControllerForceLateUpdatePrefix(EyeLookController __instance)
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockEyeLook(__instance);
            plugin?.OnCauseProbe("EyeLookController.ForceLateUpdate", blocked, null, blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            plugin.OnGameEventBlocked("EyeLookController.ForceLateUpdate");
            return false;
        }

        private static bool EyeLookCalcEyeUpdateCalcPrefix(EyeLookCalc __instance)
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockEyeLookCalc(__instance);
            plugin?.OnCauseProbe("EyeLookCalc.EyeUpdateCalc", blocked, null, blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            plugin.OnGameEventBlocked("EyeLookCalc.EyeUpdateCalc");
            return false;
        }

        private static bool EyeLookMaterialControllUpdatePrefix(EyeLookMaterialControll __instance)
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockEyeLookMaterial(__instance);
            plugin?.OnCauseProbe("EyeLookMaterialControll.Update", blocked, null, blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("EyeLookMaterialControll.Update");
            return false;
        }

        private static bool ChaControlChangeEyebrowPtnPrefix(ChaControl __instance)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.ShouldBlockFaceExpressionApi(__instance))
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("ChaControl.ChangeEyebrowPtn");
            return false;
        }

        private static bool ChaControlChangeEyesPtnPrefix(ChaControl __instance)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.ShouldBlockFaceExpressionApi(__instance))
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("ChaControl.ChangeEyesPtn");
            return false;
        }

        private static bool ChaControlChangeMouthPtnPrefix(ChaControl __instance)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.ShouldBlockFaceExpressionApi(__instance))
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("ChaControl.ChangeMouthPtn");
            return false;
        }

        private static bool ChaControlChangeEyesOpenMaxPrefix(ChaControl __instance)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.ShouldBlockFaceExpressionApi(__instance))
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("ChaControl.ChangeEyesOpenMax");
            return false;
        }

        private static bool ChaControlChangeMouthOpenMaxPrefix(ChaControl __instance)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.ShouldBlockFaceExpressionApi(__instance))
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("ChaControl.ChangeMouthOpenMax");
            return false;
        }

        private static bool ChaControlChangeMouthFixedPrefix(ChaControl __instance)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.ShouldBlockFaceExpressionApi(__instance))
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("ChaControl.ChangeMouthFixed");
            return false;
        }

        private static bool ChaControlChangeHohoAkaRatePrefix(ChaControl __instance)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.ShouldBlockFaceExpressionApi(__instance))
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("ChaControl.ChangeHohoAkaRate");
            return false;
        }

        private static bool ChaControlSetTearsLvPrefix(ChaControl __instance)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || !plugin.ShouldBlockFaceExpressionApi(__instance))
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("ChaControl.set_tearsLv");
            return false;
        }

        private static bool ChaControlSetSiruFlagsPrefix(ChaControl __instance, ChaFileDefine.SiruParts parts)
        {
            if (parts != ChaFileDefine.SiruParts.SiruKao)
            {
                return true;
            }

            Plugin plugin = Plugin.Instance;
            bool modeOn = plugin != null && plugin.ShouldBlockGameEvents();
            plugin?.OnCauseProbe(
                "ChaControl.SetSiruFlags(SiruKao)",
                blocked: false,
                cha: __instance,
                note: modeOn ? "allow_doll_mode_face_siru" : "pass");
            return true;
        }

        private static bool ChaControlChangeLookEyesPtnPrefix(ChaControl __instance)
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockLookTargetApi(__instance);
            plugin?.OnCauseProbe("ChaControl.ChangeLookEyesPtn", blocked, __instance, blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("ChaControl.ChangeLookEyesPtn");
            return false;
        }

        private static bool ChaControlChangeLookEyesTargetPrefix(ChaControl __instance)
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockLookTargetApi(__instance);
            plugin?.OnCauseProbe("ChaControl.ChangeLookEyesTarget", blocked, __instance, blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("ChaControl.ChangeLookEyesTarget");
            return false;
        }

        private static void HActionBaseSetDefaultAnimatorFloatPrefix(HActionBase __instance, HFlag ___flags)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null || ___flags == null)
            {
                return;
            }

            string ownerType = __instance != null
                ? (__instance.GetType().FullName ?? __instance.GetType().Name ?? "HActionBase")
                : "HActionBase";
            plugin.TryApplyMotionLock(___flags, "HActionBase.SetDefaultAnimatorFloat", ownerType);
        }

        private static void ApplyFixationalEyeMovementBlock(Harmony harmony)
        {
            List<Type> fixTypes = FindFixationalEyeMovementTypes();
            _fixationalTypes.Clear();
            if (fixTypes.Count == 0)
            {
                _logInfo?.Invoke("[guard][patch] optional target not found: *FixationalEyeMovement*");
                return;
            }

            _fixationalTypes.AddRange(fixTypes);
            _logInfo?.Invoke("[guard][patch] fixational candidates found=" + fixTypes.Count);
            for (int i = 0; i < fixTypes.Count; i++)
            {
                Type fixType = fixTypes[i];
                string typeLabel = fixType.FullName ?? fixType.Name ?? "(unknown)";

                MethodInfo update = AccessTools.Method(fixType, "Update", Type.EmptyTypes);
                PatchPrefix(harmony, update, nameof(FixationalEyeMovementUpdatePrefix), typeLabel + ".Update");

                MethodInfo ex = AccessTools.Method(fixType, "EX", Type.EmptyTypes);
                PatchPrefix(harmony, ex, nameof(FixationalEyeMovementExPrefix), typeLabel + ".EX");
            }
        }

        private static List<Type> FindFixationalEyeMovementTypes()
        {
            var results = new List<Type>();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly asm = assemblies[i];
                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types;
                }
                catch (Exception ex)
                {
                    _logWarn?.Invoke("[guard][patch] fixational scan failed asm=" + asm.FullName + " error=" + ex.Message);
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                for (int t = 0; t < types.Length; t++)
                {
                    Type type = types[t];
                    if (type == null)
                    {
                        continue;
                    }

                    string fullName = type.FullName ?? type.Name ?? string.Empty;
                    if (fullName.IndexOf("FixationalEyeMovement", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    MethodInfo update = AccessTools.Method(type, "Update", Type.EmptyTypes);
                    MethodInfo ex = AccessTools.Method(type, "EX", Type.EmptyTypes);
                    if (update == null && ex == null)
                    {
                        continue;
                    }

                    if (unique.Add(fullName))
                    {
                        results.Add(type);
                    }
                }
            }

            return results;
        }

        private static bool FixationalEyeMovementUpdatePrefix(object __instance)
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockFixationalInstance(__instance);
            plugin?.OnCauseProbe("FixationalEyeMovement.Update", blocked, null, blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            string typeLabel = __instance?.GetType().FullName ?? "FixationalEyeMovement";
            plugin.OnExternalPluginBlocked(typeLabel + ".Update");
            return false;
        }

        private static bool FixationalEyeMovementExPrefix(object __instance)
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockFixationalInstance(__instance);
            plugin?.OnCauseProbe("FixationalEyeMovement.EX", blocked, null, blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            string typeLabel = __instance?.GetType().FullName ?? "FixationalEyeMovement";
            plugin.OnExternalPluginBlocked(typeLabel + ".EX");
            return false;
        }

        private static bool ApplyMainGameAhegaoBlock(Harmony harmony, bool fromRetry)
        {
            if (harmony == null)
            {
                return false;
            }

            Type ahegaoType = TryFindMainGameAhegaoType();
            if (ahegaoType == null)
            {
                if (!_ahegaoMissingLogged)
                {
                    _logInfo?.Invoke("[guard][patch] optional target not found: MainGameAhegao.AhegaoPlugin");
                    _ahegaoMissingLogged = true;
                }

                _ahegaoFoundButUnreadyLogged = false;
                _ahegaoPatchReady = false;
                return false;
            }

            _ahegaoMissingLogged = false;

            MethodInfo isMasterEnabled = AccessTools.Method(ahegaoType, "IsMasterEnabled", Type.EmptyTypes);
            PatchPrefix(harmony, isMasterEnabled, nameof(AhegaoPluginIsMasterEnabledPrefix), "MainGameAhegao.AhegaoPlugin.IsMasterEnabled");

            MethodInfo shouldProc = AccessTools.Method(ahegaoType, "ShouldProc", Type.EmptyTypes);
            PatchPrefix(harmony, shouldProc, nameof(AhegaoPluginShouldProcPrefix), "MainGameAhegao.AhegaoPlugin.ShouldProc");

            MethodInfo lateUpdate = AccessTools.Method(ahegaoType, "LateUpdate", Type.EmptyTypes);
            PatchPrefix(harmony, lateUpdate, nameof(AhegaoPluginLateUpdatePrefix), "MainGameAhegao.AhegaoPlugin.LateUpdate");

            MethodInfo roll = AccessTools.Method(ahegaoType, "Roll", new[] { typeof(ChaControl), typeof(bool) });
            PatchPrefix(harmony, roll, nameof(AhegaoPluginRollPrefix), "MainGameAhegao.AhegaoPlugin.Roll");

            MethodInfo refreshFace = AccessTools.Method(ahegaoType, "RefreshFace", Type.EmptyTypes);
            PatchPrefix(harmony, refreshFace, nameof(AhegaoPluginRefreshFacePrefix), "MainGameAhegao.AhegaoPlugin.RefreshFace");

            MethodInfo resetAhegao = AccessTools.Method(ahegaoType, "ResetAhegao", Type.EmptyTypes);
            PatchPrefix(harmony, resetAhegao, nameof(AhegaoPluginResetAhegaoPrefix), "MainGameAhegao.AhegaoPlugin.ResetAhegao");

            MethodInfo applyTearsBlush = AccessTools.Method(ahegaoType, "ApplyTearsBlush", Type.EmptyTypes);
            PatchPrefix(harmony, applyTearsBlush, nameof(AhegaoPluginApplyTearsBlushPrefix), "MainGameAhegao.AhegaoPlugin.ApplyTearsBlush");

            Type ahegaoHooksType = TryFindMainGameAhegaoHooksType();
            MethodInfo faceListCtrlSetFaceHook = ahegaoHooksType != null
                ? AccessTools.Method(ahegaoHooksType, "FaceListCtrl_SetFace", new[] { typeof(bool) })
                : null;
            PatchPrefix(
                harmony,
                faceListCtrlSetFaceHook,
                nameof(AhegaoHooksFaceListCtrlSetFacePrefix),
                "MainGameAhegao.AhegaoHooks.FaceListCtrl_SetFace");

            bool ready =
                IsAlreadyPatchedByOwner(lateUpdate, harmony)
                || IsAlreadyPatchedByOwner(roll, harmony)
                || IsAlreadyPatchedByOwner(faceListCtrlSetFaceHook, harmony);

            if (!ready)
            {
                if (fromRetry && !_ahegaoFoundButUnreadyLogged)
                {
                    _logWarn?.Invoke("[guard][patch] optional target found but key methods not patched: MainGameAhegao.AhegaoPlugin");
                    _ahegaoFoundButUnreadyLogged = true;
                }

                _ahegaoPatchReady = false;
                return false;
            }

            _ahegaoFoundButUnreadyLogged = false;
            if (!_ahegaoPatchReady)
            {
                _logInfo?.Invoke("[guard][patch] optional target ready: MainGameAhegao.AhegaoPlugin");
            }

            _ahegaoPatchReady = true;
            return true;
        }

        private static Type TryFindMainGameAhegaoType()
        {
            Type found = TryFindTypeByFullNameFast("MainGameAhegao.AhegaoPlugin");
            if (found != null)
            {
                return found;
            }

            return TryFindTypeByFullNameFast("AhegaoPlugin");
        }

        private static Type TryFindMainGameAhegaoHooksType()
        {
            Type found = TryFindTypeByFullNameFast("MainGameAhegao.AhegaoHooks");
            if (found != null)
            {
                return found;
            }

            return TryFindTypeByFullNameFast("AhegaoHooks");
        }

        private static Type TryFindTypeByFullNameFast(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return null;
            }

            Assembly[] assemblies;
            try
            {
                assemblies = AppDomain.CurrentDomain.GetAssemblies();
            }
            catch
            {
                return null;
            }

            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly asm = assemblies[i];
                if (asm == null)
                {
                    continue;
                }

                try
                {
                    Type type = asm.GetType(fullName, throwOnError: false, ignoreCase: false);
                    if (type != null)
                    {
                        return type;
                    }
                }
                catch
                {
                    // ignore per-assembly lookup failures and continue
                }
            }

            return null;
        }

        private static bool AhegaoPluginIsMasterEnabledPrefix(ref bool __result)
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockGameEvents();
            plugin?.OnCauseProbe(
                "MainGameAhegao.AhegaoPlugin.IsMasterEnabled",
                blocked,
                null,
                blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            __result = false;
            plugin.OnExternalPluginBlocked("MainGameAhegao.AhegaoPlugin.IsMasterEnabled");
            return false;
        }

        private static bool AhegaoPluginShouldProcPrefix(ref bool __result)
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockGameEvents();
            plugin?.OnCauseProbe(
                "MainGameAhegao.AhegaoPlugin.ShouldProc",
                blocked,
                null,
                blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            __result = false;
            plugin.OnExternalPluginBlocked("MainGameAhegao.AhegaoPlugin.ShouldProc");
            return false;
        }

        private static bool AhegaoPluginLateUpdatePrefix(object __instance)
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockGameEvents();
            plugin?.OnCauseProbe(
                "MainGameAhegao.AhegaoPlugin.LateUpdate",
                blocked,
                null,
                blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("MainGameAhegao.AhegaoPlugin.LateUpdate");
            return false;
        }

        private static bool AhegaoPluginRollPrefix(ChaControl target)
        {
            Plugin plugin = Plugin.Instance;
            if (plugin == null)
            {
                return true;
            }

            if (!plugin.ShouldBlockGameEvents())
            {
                plugin.OnCauseProbe(
                    "MainGameAhegao.AhegaoPlugin.Roll",
                    false,
                    target,
                    "pass_mode_off");
                return true;
            }

            if (target != null && !plugin.IsTargetCharacterPublic(target))
            {
                plugin.OnCauseProbe(
                    "MainGameAhegao.AhegaoPlugin.Roll",
                    false,
                    target,
                    "pass_non_target");
                return true;
            }

            plugin.OnCauseProbe(
                "MainGameAhegao.AhegaoPlugin.Roll",
                true,
                target,
                "blocked");
            plugin.OnExternalPluginBlocked("MainGameAhegao.AhegaoPlugin.Roll");
            return false;
        }

        private static bool AhegaoPluginRefreshFacePrefix()
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockGameEvents();
            plugin?.OnCauseProbe("MainGameAhegao.AhegaoPlugin.RefreshFace", blocked, null, blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("MainGameAhegao.AhegaoPlugin.RefreshFace");
            return false;
        }

        private static bool AhegaoPluginResetAhegaoPrefix()
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockGameEvents();
            plugin?.OnCauseProbe("MainGameAhegao.AhegaoPlugin.ResetAhegao", blocked, null, blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("MainGameAhegao.AhegaoPlugin.ResetAhegao");
            return false;
        }

        private static bool AhegaoPluginApplyTearsBlushPrefix()
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockGameEvents();
            plugin?.OnCauseProbe("MainGameAhegao.AhegaoPlugin.ApplyTearsBlush", blocked, null, blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("MainGameAhegao.AhegaoPlugin.ApplyTearsBlush");
            return false;
        }

        private static bool AhegaoHooksFaceListCtrlSetFacePrefix()
        {
            Plugin plugin = Plugin.Instance;
            bool blocked = plugin != null && plugin.ShouldBlockGameEvents();
            plugin?.OnCauseProbe("MainGameAhegao.AhegaoHooks.FaceListCtrl_SetFace", blocked, null, blocked ? "blocked" : "pass");
            if (!blocked)
            {
                return true;
            }

            plugin.OnExternalPluginBlocked("MainGameAhegao.AhegaoHooks.FaceListCtrl_SetFace");
            return false;
        }
    }
}
