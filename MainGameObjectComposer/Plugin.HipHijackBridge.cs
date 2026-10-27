using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace MainGameObjectComposer
{
    /// <summary>
    /// MainGirlHipHijack の公開 API へリフレクション経由でアクセス。
    /// HipHijack 未導入時は IsAvailable=false で全 API が無効化される。
    /// </summary>
    internal static class HipHijackBridge
    {
        private static Type _hijackType;
        private static MethodInfo _miGetEffectorKeys;
        private static MethodInfo _miTrySet;
        private static MethodInfo _miTryClear;
        private static MethodInfo _miTryGet;
        private static MethodInfo _miTryGetBodyIkTrackingPosition;
        private static MethodInfo _miTrySetEnabled;
        private static MethodInfo _miTrySetWeight;
        private static MethodInfo _miTrySetSpeedHijack;
        private static MethodInfo _miTrySetCutFemaleAnimSpeed;
        private static MethodInfo _miTryDisableBodyCtrl;
        private static MethodInfo _miTryEnableBodyCtrl;
        private static MethodInfo _miIsBodyCtrlLinkEnabled;
        private static MethodInfo _miIsSpeedHijackEnabled;
        private static MethodInfo _miIsCutFemaleAnimSpeedEnabled;
        private static bool _lookupDone;
        private static bool _lookupOk;

        private static readonly object[] _setArgs = new object[2];
        private static readonly object[] _clearArgs = new object[1];
        private static readonly object[] _getArgs = new object[2];
        private static readonly object[] _trackingArgs = new object[3];
        private static readonly object[] _enabledArgs = new object[2];
        private static readonly object[] _weightArgs = new object[2];
        private static readonly object[] _boolArgs = new object[1];

        internal static bool IsAvailable => Resolve();

        private static bool Resolve()
        {
            if (_lookupDone) return _lookupOk;
            _lookupDone = true;

            _hijackType =
                AccessTools.TypeByName("MainGirlHipHijack.Plugin") ??
                Type.GetType("MainGirlHipHijack.Plugin, MainGirlHipHijack");

            if (_hijackType == null) return false;

            _miGetEffectorKeys = _hijackType.GetMethod(
                "GetEffectorKeys", BindingFlags.Public | BindingFlags.Static);
            _miTrySet = _hijackType.GetMethod(
                "TrySetEffectorFollowComposerObject", BindingFlags.Public | BindingFlags.Static);
            _miTryClear = _hijackType.GetMethod(
                "TryClearEffectorFollowComposerObject", BindingFlags.Public | BindingFlags.Static);
            _miTryGet = _hijackType.GetMethod(
                "TryGetEffectorFollowComposerObject", BindingFlags.Public | BindingFlags.Static);
            _miTryGetBodyIkTrackingPosition = _hijackType.GetMethod(
                "TryGetBodyIkTrackingPosition", BindingFlags.Public | BindingFlags.Static);

            // 新規追加: IK 直接制御API
            _miTrySetEnabled = _hijackType.GetMethod(
                "TrySetEffectorEnabled", BindingFlags.Public | BindingFlags.Static);
            _miTrySetWeight = _hijackType.GetMethod(
                "TrySetEffectorWeight", BindingFlags.Public | BindingFlags.Static);
            _miTrySetSpeedHijack = _hijackType.GetMethod(
                "TrySetSpeedHijackEnabled", BindingFlags.Public | BindingFlags.Static);
            _miTrySetCutFemaleAnimSpeed = _hijackType.GetMethod(
                "TrySetCutFemaleAnimSpeedEnabled", BindingFlags.Public | BindingFlags.Static);
            _miTryDisableBodyCtrl = _hijackType.GetMethod(
                "TryDisableBodyCtrlLink", BindingFlags.Public | BindingFlags.Static);
            _miTryEnableBodyCtrl = _hijackType.GetMethod(
                "TryEnableBodyCtrlLink", BindingFlags.Public | BindingFlags.Static);
            _miIsBodyCtrlLinkEnabled = _hijackType.GetMethod(
                "IsBodyCtrlLinkEnabled", BindingFlags.Public | BindingFlags.Static);
            _miIsSpeedHijackEnabled = _hijackType.GetMethod(
                "IsSpeedHijackEnabled", BindingFlags.Public | BindingFlags.Static);
            _miIsCutFemaleAnimSpeedEnabled = _hijackType.GetMethod(
                "IsCutFemaleAnimSpeedEnabled", BindingFlags.Public | BindingFlags.Static);

            // 旧APIさえあれば BridgeAvailable とする（新APIは無くてもグレースフルに動くように個別チェック）
            _lookupOk = _miGetEffectorKeys != null && _miTrySet != null && _miTryClear != null && _miTryGet != null;
            return _lookupOk;
        }

        internal static IList<string> GetEffectorKeys()
        {
            if (!Resolve()) return null;
            try
            {
                IEnumerable raw = _miGetEffectorKeys.Invoke(null, null) as IEnumerable;
                if (raw == null) return null;
                List<string> result = new List<string>();
                foreach (object o in raw)
                {
                    if (o is string s) result.Add(s);
                }
                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static bool TrySet(string effectorKey, string composerId)
        {
            if (!Resolve()) return false;
            _setArgs[0] = effectorKey;
            _setArgs[1] = composerId;
            try
            {
                return (bool)_miTrySet.Invoke(null, _setArgs);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool TryClear(string effectorKey)
        {
            if (!Resolve()) return false;
            _clearArgs[0] = effectorKey;
            try
            {
                return (bool)_miTryClear.Invoke(null, _clearArgs);
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool TryGet(string effectorKey, out string composerId)
        {
            composerId = null;
            if (!Resolve()) return false;
            _getArgs[0] = effectorKey;
            _getArgs[1] = null;
            bool ok;
            try
            {
                ok = (bool)_miTryGet.Invoke(null, _getArgs);
            }
            catch (Exception)
            {
                return false;
            }
            if (!ok) return false;
            composerId = _getArgs[1] as string;
            return !string.IsNullOrEmpty(composerId);
        }

        // ── IK直接制御 (HipHijack側 V2 API) ────────────────────────

        internal static bool IsBodyIkRunning()
        {
            if (!Resolve() || _miTryGetBodyIkTrackingPosition == null) return false;
            _trackingArgs[0] = UnityEngine.Vector3.zero;
            _trackingArgs[1] = false;
            _trackingArgs[2] = null;
            try
            {
                return (bool)_miTryGetBodyIkTrackingPosition.Invoke(null, _trackingArgs)
                    && _trackingArgs[1] is bool running && running
                    && string.Equals(_trackingArgs[2] as string, "body_proxy", StringComparison.Ordinal);
            }
            catch (Exception) { return false; }
        }

        internal static bool TrySetEffectorEnabled(string effectorKey, bool on)
        {
            if (!Resolve() || _miTrySetEnabled == null) return false;
            _enabledArgs[0] = effectorKey;
            _enabledArgs[1] = on;
            try { return (bool)_miTrySetEnabled.Invoke(null, _enabledArgs); }
            catch (Exception) { return false; }
        }

        internal static bool TrySetEffectorWeight(string effectorKey, float weight)
        {
            if (!Resolve() || _miTrySetWeight == null) return false;
            _weightArgs[0] = effectorKey;
            _weightArgs[1] = weight;
            try { return (bool)_miTrySetWeight.Invoke(null, _weightArgs); }
            catch (Exception) { return false; }
        }

        internal static bool TrySetSpeedHijackEnabled(bool on)
        {
            if (!Resolve() || _miTrySetSpeedHijack == null) return false;
            _boolArgs[0] = on;
            try { return (bool)_miTrySetSpeedHijack.Invoke(null, _boolArgs); }
            catch (Exception) { return false; }
        }

        internal static bool TrySetCutFemaleAnimSpeedEnabled(bool on)
        {
            if (!Resolve() || _miTrySetCutFemaleAnimSpeed == null) return false;
            _boolArgs[0] = on;
            try { return (bool)_miTrySetCutFemaleAnimSpeed.Invoke(null, _boolArgs); }
            catch (Exception) { return false; }
        }

        internal static bool TryDisableBodyCtrlLink()
        {
            if (!Resolve() || _miTryDisableBodyCtrl == null) return false;
            try { return (bool)_miTryDisableBodyCtrl.Invoke(null, null); }
            catch (Exception) { return false; }
        }

        internal static bool TryEnableBodyCtrlLink()
        {
            if (!Resolve() || _miTryEnableBodyCtrl == null) return false;
            try { return (bool)_miTryEnableBodyCtrl.Invoke(null, null); }
            catch (Exception) { return false; }
        }

        internal static bool IsBodyCtrlLinkEnabled()
        {
            if (!Resolve() || _miIsBodyCtrlLinkEnabled == null) return false;
            try { return (bool)_miIsBodyCtrlLinkEnabled.Invoke(null, null); }
            catch (Exception) { return false; }
        }

        internal static bool IsSpeedHijackEnabled()
        {
            if (!Resolve() || _miIsSpeedHijackEnabled == null) return false;
            try { return (bool)_miIsSpeedHijackEnabled.Invoke(null, null); }
            catch (Exception) { return false; }
        }

        internal static bool IsCutFemaleAnimSpeedEnabled()
        {
            if (!Resolve() || _miIsCutFemaleAnimSpeedEnabled == null) return false;
            try { return (bool)_miIsCutFemaleAnimSpeedEnabled.Invoke(null, null); }
            catch (Exception) { return false; }
        }
    }
}
