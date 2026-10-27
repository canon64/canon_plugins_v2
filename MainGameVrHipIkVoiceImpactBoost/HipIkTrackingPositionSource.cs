using System;
using System.Reflection;
using UnityEngine;

namespace MainGameVrHipIkVoiceImpactBoost
{
    internal sealed class HipIkTrackingPositionSource
    {
        private const string TargetTypeName = "MainGirlHipHijack.Plugin";
        private const string TargetAssemblyName = "MainGirlHipHijack";
        private const string HipMethodName = "TryGetBodyIkTrackingPosition";
        private const string HeadMethodName = "TryGetFemaleHeadPosition";

        private readonly PluginFileLogger _logger;
        private Type _type;
        private bool _resolvedType;
        private MethodInfo _hipMethod;
        private MethodInfo _headMethod;
        private bool _resolvedHip;
        private bool _resolvedHead;
        private bool _loggedHipMissing;
        private bool _loggedHeadMissing;
        private bool _loggedHipError;
        private bool _loggedHeadError;

        internal HipIkTrackingPositionSource(PluginFileLogger logger)
        {
            _logger = logger;
        }

        internal bool TryGetHip(out Vector3 position, out bool bodyIkRunning, out string source)
        {
            position = Vector3.zero;
            bodyIkRunning = false;
            source = null;

            MethodInfo method = ResolveHipMethod();
            if (method == null)
            {
                return false;
            }

            object[] args = { Vector3.zero, false, null };
            try
            {
                bool ok = (bool)method.Invoke(null, args);
                if (!ok)
                {
                    return false;
                }

                position = args[0] is Vector3 vector ? vector : Vector3.zero;
                bodyIkRunning = args[1] is bool running && running;
                source = args[2] as string;
                return true;
            }
            catch (Exception ex)
            {
                if (!_loggedHipError)
                {
                    _logger.LogWarning("hip ik tracking api call failed: " + ex.Message);
                    _loggedHipError = true;
                }
                return false;
            }
        }

        internal bool TryGetHead(out Vector3 position, out string source)
        {
            position = Vector3.zero;
            source = null;

            MethodInfo method = ResolveHeadMethod();
            if (method == null)
            {
                return false;
            }

            object[] args = { Vector3.zero, null };
            try
            {
                bool ok = (bool)method.Invoke(null, args);
                if (!ok)
                {
                    return false;
                }

                position = args[0] is Vector3 vector ? vector : Vector3.zero;
                source = args[1] as string;
                return true;
            }
            catch (Exception ex)
            {
                if (!_loggedHeadError)
                {
                    _logger.LogWarning("female head position api call failed: " + ex.Message);
                    _loggedHeadError = true;
                }
                return false;
            }
        }

        private Type ResolveType()
        {
            if (_resolvedType)
            {
                return _type;
            }

            _resolvedType = true;
            _type = Type.GetType(TargetTypeName + ", " + TargetAssemblyName, throwOnError: false);
            if (_type == null)
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    _type = assembly.GetType(TargetTypeName, throwOnError: false);
                    if (_type != null)
                    {
                        break;
                    }
                }
            }
            return _type;
        }

        private MethodInfo ResolveHipMethod()
        {
            if (_resolvedHip)
            {
                return _hipMethod;
            }

            _resolvedHip = true;
            Type type = ResolveType();
            if (type != null)
            {
                _hipMethod = type.GetMethod(HipMethodName, BindingFlags.Public | BindingFlags.Static);
            }

            if (_hipMethod == null && !_loggedHipMissing)
            {
                _logger.LogWarning("hip ik tracking api missing: " + TargetTypeName + "." + HipMethodName);
                _loggedHipMissing = true;
            }

            return _hipMethod;
        }

        private MethodInfo ResolveHeadMethod()
        {
            if (_resolvedHead)
            {
                return _headMethod;
            }

            _resolvedHead = true;
            Type type = ResolveType();
            if (type != null)
            {
                _headMethod = type.GetMethod(HeadMethodName, BindingFlags.Public | BindingFlags.Static);
            }

            if (_headMethod == null && !_loggedHeadMissing)
            {
                _logger.LogWarning("female head position api missing: " + TargetTypeName + "." + HeadMethodName);
                _loggedHeadMissing = true;
            }

            return _headMethod;
        }
    }
}
