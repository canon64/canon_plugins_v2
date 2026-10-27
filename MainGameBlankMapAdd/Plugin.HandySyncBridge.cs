using System;
using System.Reflection;
using HarmonyLib;

namespace MainGameBlankMapAdd
{
    /// <summary>
    /// 再生バーから MainGameHandySync の動作を開始/停止するための橋渡し。
    ///
    /// DLL参照ではなくリフレクションで繋ぐので、HandySyncが入っていない環境では
    /// ボタン自体を出さずに黙って無視する。
    /// </summary>
    public sealed partial class Plugin
    {
        private const string HandySyncApiTypeName = "MainGameHandySync.MainGameHandySyncApi";

        private static bool _handySyncApiResolved;
        private static MethodInfo _handySyncGetEnabled;
        private static MethodInfo _handySyncSetEnabled;

        private static void ResolveHandySyncApi()
        {
            if (_handySyncApiResolved)
                return;

            _handySyncApiResolved = true;

            Type type = AccessTools.TypeByName(HandySyncApiTypeName);
            if (type == null)
                return;

            _handySyncGetEnabled = AccessTools.Method(type, "TryGetEnabled");
            _handySyncSetEnabled = AccessTools.Method(type, "TrySetEnabled", new[] { typeof(bool) });
        }

        /// <summary>HandySyncが居るか。居なければバーにボタンを出さない。</summary>
        internal bool IsHandySyncAvailable()
        {
            ResolveHandySyncApi();
            return _handySyncGetEnabled != null && _handySyncSetEnabled != null;
        }

        /// <summary>今動く状態か。取得できなければ false。</summary>
        internal bool IsHandySyncEnabled()
        {
            if (!IsHandySyncAvailable())
                return false;

            try
            {
                object[] args = { false };
                bool ok = (bool)_handySyncGetEnabled.Invoke(null, args);
                return ok && (bool)args[0];
            }
            catch (Exception ex)
            {
                LogWarn("[handy] state read failed: " + ex.Message);
                return false;
            }
        }

        internal void SetHandySyncEnabled(bool enabled)
        {
            if (!IsHandySyncAvailable())
                return;

            try
            {
                _handySyncSetEnabled.Invoke(null, new object[] { enabled });
                LogInfo("[handy] " + (enabled ? "start" : "stop") + " (bar)");
            }
            catch (Exception ex)
            {
                LogWarn("[handy] set failed: " + ex.Message);
            }
        }
    }
}
