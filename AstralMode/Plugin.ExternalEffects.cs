using System;
using System.Collections.Generic;
using System.Reflection;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private readonly HashSet<string> _warnedExternalEffects = new HashSet<string>();

        private bool _simpleAfterimageLookupDone;
        private MethodInfo _simpleAfterimageSetWarpMethod;
        private MethodInfo _simpleAfterimageSetWarpFullMethod;
        private MethodInfo _simpleAfterimageClearWarpMethod;

        private bool _circularDisplacementLookupDone;
        private MethodInfo _circularDisplacementSetWarpMethod;
        private MethodInfo _circularDisplacementSetWarpFullMethod;
        private MethodInfo _circularDisplacementClearWarpMethod;

        private void WarnExternalOnce(string key, string message)
        {
            if (_warnedExternalEffects.Contains(key)) return;
            _warnedExternalEffects.Add(key);
            SimpleFileLogger.Warn(message);
        }

        private static Type FindType(string typeName, string assemblyName)
        {
            Type t = Type.GetType(typeName + ", " + assemblyName);
            if (t != null) return t;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    t = assemblies[i].GetType(typeName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        private bool EnsureSimpleAfterimageApi()
        {
            if (_simpleAfterimageLookupDone)
                return _simpleAfterimageSetWarpMethod != null;

            _simpleAfterimageLookupDone = true;

            Type t = FindType("SimpleAfterimage.Plugin", "SimpleAfterimage");
            if (t == null)
            {
                WarnExternalOnce("SimpleAfterimage.Missing", "SimpleAfterimage API not found");
                return false;
            }

            _simpleAfterimageSetWarpMethod = t.GetMethod("SetWarpAfterimage", BindingFlags.Public | BindingFlags.Static, null,
                new Type[] { typeof(float), typeof(float), typeof(int), typeof(int), typeof(bool), typeof(float), typeof(float) },
                null);
            _simpleAfterimageSetWarpFullMethod = t.GetMethod("SetWarpAfterimage", BindingFlags.Public | BindingFlags.Static, null,
                new Type[] { typeof(float), typeof(float), typeof(int), typeof(int), typeof(bool), typeof(float), typeof(float), typeof(bool) },
                null);
            _simpleAfterimageClearWarpMethod = t.GetMethod("ClearWarpAfterimage", BindingFlags.Public | BindingFlags.Static);

            if (_simpleAfterimageSetWarpMethod == null)
            {
                WarnExternalOnce("SimpleAfterimage.SetWarpAfterimage.Missing", "SimpleAfterimage.SetWarpAfterimage API not found");
                return false;
            }

            if (_simpleAfterimageClearWarpMethod == null)
                WarnExternalOnce("SimpleAfterimage.ClearWarpAfterimage.Missing", "SimpleAfterimage.ClearWarpAfterimage API not found");

            return true;
        }

        private bool EnsureCircularDisplacementApi()
        {
            if (_circularDisplacementLookupDone)
                return _circularDisplacementSetWarpMethod != null;

            _circularDisplacementLookupDone = true;

            Type t = FindType("MainGameCircularDisplacement.Plugin", "MainGameCircularDisplacement");
            if (t == null)
            {
                WarnExternalOnce("CircularDisplacement.Missing", "MainGameCircularDisplacement API not found");
                return false;
            }

            _circularDisplacementSetWarpMethod = t.GetMethod("SetExternalWarp", BindingFlags.Public | BindingFlags.Static, null,
                new Type[] { typeof(float), typeof(string), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float) },
                null);
            _circularDisplacementSetWarpFullMethod = t.GetMethod("SetExternalWarp", BindingFlags.Public | BindingFlags.Static, null,
                new Type[] { typeof(float), typeof(string), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(float), typeof(bool) },
                null);
            _circularDisplacementClearWarpMethod = t.GetMethod("ClearExternalWarp", BindingFlags.Public | BindingFlags.Static);

            if (_circularDisplacementSetWarpMethod == null)
            {
                WarnExternalOnce("CircularDisplacement.SetExternalWarp.Missing", "MainGameCircularDisplacement.SetExternalWarp API not found");
                return false;
            }

            if (_circularDisplacementClearWarpMethod == null)
                WarnExternalOnce("CircularDisplacement.ClearExternalWarp.Missing", "MainGameCircularDisplacement.ClearExternalWarp API not found");

            return true;
        }

        private bool TrySetWarpAfterimage(
            float intensity,
            float alphaScale,
            int fadeFrames,
            int captureInterval,
            bool pauseBaseCapture,
            float baseAlphaMultiplier,
            float holdSeconds)
        {
            if (!EnsureSimpleAfterimageApi()) return false;

            try
            {
                _simpleAfterimageSetWarpMethod.Invoke(null, new object[]
                {
                    intensity,
                    alphaScale,
                    fadeFrames,
                    captureInterval,
                    pauseBaseCapture,
                    baseAlphaMultiplier,
                    holdSeconds
                });
                return true;
            }
            catch (Exception ex)
            {
                WarnExternalOnce("SimpleAfterimage.SetWarpAfterimage.Failed", "SimpleAfterimage.SetWarpAfterimage failed: " + ex.Message);
                return false;
            }
        }

        private bool TrySetWarpAfterimageFull(
            float intensity,
            float alphaScale,
            int fadeFrames,
            int captureInterval,
            bool pauseBaseCapture,
            float baseAlphaMultiplier,
            float holdSeconds,
            bool fullScreen)
        {
            if (!EnsureSimpleAfterimageApi()) return false;

            // Fall back to the legacy (Chara-only) overload when the full-screen API is missing.
            if (_simpleAfterimageSetWarpFullMethod == null)
                return TrySetWarpAfterimage(intensity, alphaScale, fadeFrames, captureInterval, pauseBaseCapture, baseAlphaMultiplier, holdSeconds);

            try
            {
                _simpleAfterimageSetWarpFullMethod.Invoke(null, new object[]
                {
                    intensity,
                    alphaScale,
                    fadeFrames,
                    captureInterval,
                    pauseBaseCapture,
                    baseAlphaMultiplier,
                    holdSeconds,
                    fullScreen
                });
                return true;
            }
            catch (Exception ex)
            {
                WarnExternalOnce("SimpleAfterimage.SetWarpAfterimageFull.Failed", "SimpleAfterimage.SetWarpAfterimage(full) failed: " + ex.Message);
                return false;
            }
        }

        private void TryClearWarpAfterimage()
        {
            if (!EnsureSimpleAfterimageApi()) return;
            if (_simpleAfterimageClearWarpMethod == null) return;

            try
            {
                _simpleAfterimageClearWarpMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                WarnExternalOnce("SimpleAfterimage.ClearWarpAfterimage.Failed", "SimpleAfterimage.ClearWarpAfterimage failed: " + ex.Message);
            }
        }

        private bool TrySetCircularDisplacement(
            float intensity,
            string mode,
            float opacity,
            float strength,
            float radius,
            float innerRadius,
            float centerX,
            float centerY,
            float holdSeconds)
        {
            if (!EnsureCircularDisplacementApi()) return false;

            try
            {
                _circularDisplacementSetWarpMethod.Invoke(null, new object[]
                {
                    intensity,
                    mode,
                    opacity,
                    strength,
                    radius,
                    innerRadius,
                    centerX,
                    centerY,
                    holdSeconds
                });
                return true;
            }
            catch (Exception ex)
            {
                WarnExternalOnce("CircularDisplacement.SetExternalWarp.Failed", "MainGameCircularDisplacement.SetExternalWarp failed: " + ex.Message);
                return false;
            }
        }

        private bool TrySetCircularDisplacementFull(
            float intensity,
            string mode,
            float opacity,
            float strength,
            float radius,
            float innerRadius,
            float centerX,
            float centerY,
            float holdSeconds,
            bool fullScreen)
        {
            if (!EnsureCircularDisplacementApi()) return false;

            // Fall back to the legacy (Chara-only) overload when the full-screen API is missing.
            if (_circularDisplacementSetWarpFullMethod == null)
                return TrySetCircularDisplacement(intensity, mode, opacity, strength, radius, innerRadius, centerX, centerY, holdSeconds);

            try
            {
                _circularDisplacementSetWarpFullMethod.Invoke(null, new object[]
                {
                    intensity,
                    mode,
                    opacity,
                    strength,
                    radius,
                    innerRadius,
                    centerX,
                    centerY,
                    holdSeconds,
                    fullScreen
                });
                return true;
            }
            catch (Exception ex)
            {
                WarnExternalOnce("CircularDisplacement.SetExternalWarpFull.Failed", "MainGameCircularDisplacement.SetExternalWarp(full) failed: " + ex.Message);
                return false;
            }
        }

        private void TryClearCircularDisplacement()
        {
            if (!EnsureCircularDisplacementApi()) return;
            if (_circularDisplacementClearWarpMethod == null) return;

            try
            {
                _circularDisplacementClearWarpMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                WarnExternalOnce("CircularDisplacement.ClearExternalWarp.Failed", "MainGameCircularDisplacement.ClearExternalWarp failed: " + ex.Message);
            }
        }

        private void ClearExternalWarpEffects()
        {
            TryClearWarpAfterimage();
            TryClearCircularDisplacement();
        }
    }
}
