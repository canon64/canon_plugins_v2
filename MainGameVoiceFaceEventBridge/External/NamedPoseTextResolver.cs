using System;
using System.Reflection;

namespace MainGameVoiceFaceEventBridge.External
{
    internal sealed class NamedPoseTextResolver
    {
        private readonly Action<string> _log, _warn;
        private readonly Func<Type> _resolveType;

        internal NamedPoseTextResolver(Action<string> log, Action<string> warn, Func<Type> resolveType = null)
        {
            _log = log;
            _warn = warn;
            _resolveType = resolveType ?? (() => Type.GetType("MainGameSemanticPoseBridge.Plugin, MainGameSemanticPoseBridge", false));
        }

        internal bool TryResolve(string text, string triggerSuffix, bool verbose, out string customName, out int matchIndex)
        {
            customName = string.Empty;
            matchIndex = -1;
            string source = text ?? string.Empty;
            string suffix = (triggerSuffix ?? string.Empty).Trim();
            if (source.Length == 0 || suffix.Length == 0 || source.IndexOf(suffix, StringComparison.Ordinal) < 0)
                return false;

            if (verbose)
                _log($"[named-pose][diagnostic] extract begin source='{source.Replace("\r", "\\r").Replace("\n", "\\n")}' suffix='{suffix}' source_len={source.Length} suffix_len={suffix.Length}");
            try
            {
                Type bridgeType = _resolveType();
                MethodInfo method = bridgeType?.GetMethod("TryResolveNamedPoseFromText", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), typeof(string), typeof(string).MakeByRefType(), typeof(int).MakeByRefType(), typeof(string).MakeByRefType() }, null);
                if (method == null)
                {
                    _warn("[named-pose] MainGameSemanticPoseBridge.TryResolveNamedPoseFromText is unavailable");
                    return false;
                }

                object[] args = { source, suffix, string.Empty, -1, string.Empty };
                object result = method.Invoke(null, args);
                bool matched = result is bool && (bool)result;
                customName = args[2] as string ?? string.Empty;
                matchIndex = args[3] is int ? (int)args[3] : -1;
                if (verbose)
                {
                    string reason = args[4] as string ?? string.Empty;
                    if (matched) _log($"[named-pose][diagnostic] resolved name='{customName}' pos={matchIndex} reason={reason}");
                    else _log($"[named-pose][diagnostic] resolve miss reason={reason}");
                }
                return matched;
            }
            catch (Exception ex)
            {
                _warn("[named-pose] resolve failed: " + ex.Message);
                return false;
            }
        }
    }
}
