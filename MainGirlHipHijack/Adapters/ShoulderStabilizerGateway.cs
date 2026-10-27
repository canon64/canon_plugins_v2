using System;
using System.Reflection;

namespace MainGirlHipHijack.Adapters
{
    /// <summary>
    /// 任意依存の肩補正プラグインを反射で呼び出す境界。
    /// 解決キャッシュと直近呼出状態はこのGatewayが所有する。
    /// </summary>
    internal sealed class ShoulderStabilizerGateway
    {
        private const string PluginTypeName =
            "MainGirlShoulderIkStabilizer.ShoulderIkStabilizerPlugin";
        private const string AssemblyName = "MainGirlShoulderIkStabilizer";

        private Type _pluginType;
        private MethodInfo _setEnabledMethod;
        private bool _resolveFailureLogged;
        private bool _lastRequestedEnabled;
        private bool _lastAppliedEnabled;
        private bool _lastApiResolved;
        private string _lastReason = "none";
        private int _lastFrame = -1;

        internal void SetEnabled(
            bool enabled,
            string reason,
            int frame,
            bool detailLogEnabled,
            bool diagnosticLogEnabled,
            Action<string> logInfo,
            Action<string> logWarning,
            Action<string> logDiagnostic,
            Action<string> logDiagnosticWarning)
        {
            string resolvedReason = string.IsNullOrEmpty(reason) ? "unknown" : reason;
            _lastRequestedEnabled = enabled;
            _lastReason = resolvedReason;
            _lastFrame = frame;

            if (!TryResolve(detailLogEnabled, logWarning))
            {
                _lastApiResolved = false;
                _lastAppliedEnabled = false;
                if (diagnosticLogEnabled)
                    logDiagnostic?.Invoke("[ShoulderLink-DIAG] apiResolved=false req=" + enabled + " reason=" + resolvedReason);
                return;
            }

            try
            {
                _lastApiResolved = true;
                object result = _setEnabledMethod.Invoke(null, new object[] { enabled, resolvedReason });
                bool applied = !(result is bool appliedResult) || appliedResult;
                _lastAppliedEnabled = applied ? enabled : _lastAppliedEnabled;
                if (detailLogEnabled)
                    logInfo?.Invoke("[ShoulderLink] enabled=" + enabled + " reason=" + resolvedReason + " applied=" + applied);
                if (diagnosticLogEnabled)
                    logDiagnostic?.Invoke("[ShoulderLink-DIAG] apiResolved=true req=" + enabled + " applied=" + applied + " reason=" + resolvedReason);
            }
            catch (Exception ex)
            {
                _lastApiResolved = true;
                _lastAppliedEnabled = false;
                if (detailLogEnabled)
                    logWarning?.Invoke("[ShoulderLink] 呼び出し失敗: " + ex.Message);
                if (diagnosticLogEnabled)
                    logDiagnosticWarning?.Invoke("[ShoulderLink-DIAG] invoke failed req=" + enabled + " reason=" + resolvedReason + " error=" + ex.Message);
            }
        }

        internal string BuildDiagnosticStateText()
        {
            return "apiResolved=" + _lastApiResolved
                + ",reqEnabled=" + _lastRequestedEnabled
                + ",appliedEnabled=" + _lastAppliedEnabled
                + ",reason=" + (_lastReason ?? "none")
                + ",frame=" + _lastFrame;
        }

        private bool TryResolve(bool detailLogEnabled, Action<string> logWarning)
        {
            if (_setEnabledMethod != null)
                return true;

            if (_pluginType == null)
            {
                _pluginType = Type.GetType(PluginTypeName + ", " + AssemblyName, throwOnError: false);
                if (_pluginType == null)
                {
                    Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < assemblies.Length; i++)
                    {
                        Type candidate = assemblies[i].GetType(PluginTypeName, throwOnError: false);
                        if (candidate != null)
                        {
                            _pluginType = candidate;
                            break;
                        }
                    }
                }
            }

            if (_pluginType == null)
            {
                LogResolveFailureOnce(detailLogEnabled, logWarning, "[ShoulderLink] 肩補正プラグイン型が見つからない");
                return false;
            }

            _setEnabledMethod = _pluginType.GetMethod(
                "SetEnabledFromHipHijack",
                BindingFlags.Public | BindingFlags.Static);
            if (_setEnabledMethod == null)
            {
                LogResolveFailureOnce(detailLogEnabled, logWarning, "[ShoulderLink] SetEnabledFromHipHijack APIが見つからない");
                return false;
            }

            _resolveFailureLogged = false;
            return true;
        }

        private void LogResolveFailureOnce(bool enabled, Action<string> logWarning, string message)
        {
            if (_resolveFailureLogged || !enabled)
                return;

            _resolveFailureLogged = true;
            logWarning?.Invoke(message);
        }
    }
}
