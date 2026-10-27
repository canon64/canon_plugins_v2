using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        // ── 型キャッシュ ──────────────────────────────────────────────────────
        private static Type _tLogger;
        private static Type _tSettings;
        private static Type _tMgr;
        private static Type _tCtrl;

        // ── メソッド/フィールドキャッシュ ──────────────────────────────────────
        private static MethodInfo _miLoggerInit;
        private static MethodInfo _miSettingsInit;
        private static FieldInfo  _fiSettingsBaseDir;
        private static MethodInfo _miMgrInstall;
        private static MethodInfo _miCtrlInstall;
        private static MethodInfo _miCtrlLoadVmd_1;
        private static MethodInfo _miCtrlLoadVmd_3;
        private static MethodInfo _miCtrlPlay;
        private static MethodInfo _miCtrlPause;
        private static MethodInfo _miCtrlStop;
        private static MethodInfo _miCtrlRestart;
        private static MethodInfo _miCtrlSetAnimPos;
        private static MethodInfo _miCtrlDeleteAnim;
        private static MethodInfo _miCtrlGetAnimTime;
        private static MethodInfo _miCtrlIsActive;
        private static FieldInfo  _fiCtrlLastLoadedVmd;
        private static PropertyInfo _piMgrInstance;
        private static PropertyInfo _piMgrAnimationLength;
        private static PropertyInfo _piSettingsInstance;

        // ── Sound/Music ──────────────────────────────────────────────────────
        private static Type         _tSoundMgr;
        private static FieldInfo    _fiMgrSoundMgr;
        private static MethodInfo   _miSoundSetClip;
        private static MethodInfo   _miSoundPlay;
        private static MethodInfo   _miSoundPause;
        private static MethodInfo   _miSoundStop;
        private static MethodInfo   _miSoundClearClip;
        private static PropertyInfo _piSoundLoop;
        private static PropertyInfo _piSoundLength;
        private static PropertyInfo _piSoundAudioFilePath;
        private static MethodInfo   _miMgrPlayAll;
        private static MethodInfo   _miMgrStopAll;
        private static MethodInfo   _miMgrPauseAll;

        private string _vmdPluginDir;

        private bool VmdBridgeTryResolveTypes()
        {
            // 1) First try Chainloader (future-proof if VMDPlayPlugin ever gets MainGame BepInProcess)
            string dllPath = null;
            if (Chainloader.PluginInfos.TryGetValue("KKS_VMDPlayPlugin", out var info))
            {
                dllPath = info.Location;
                LogInfo($"[Bridge] KKS_VMDPlayPlugin found via Chainloader version={info.Metadata.Version} location={dllPath}");
            }
            else
            {
                // 2) Studio-only plugin — BepInEx didn't auto-load it. Load the DLL manually.
                dllPath = Path.Combine(Paths.PluginPath, "KKS_VMDPlayPlugin", "KKS_VMDPlayPlugin.dll");
                LogInfo($"[Bridge] KKS_VMDPlayPlugin not in Chainloader; try manual load path={dllPath}");
                if (!File.Exists(dllPath))
                {
                    // alternate search: any subdir under BepInEx\plugins
                    string alt = FindDllRecursive(Paths.PluginPath, "KKS_VMDPlayPlugin.dll");
                    if (alt != null) { dllPath = alt; LogInfo($"[Bridge] found via recursive scan path={dllPath}"); }
                    else { LogError($"[Bridge] KKS_VMDPlayPlugin.dll not found under {Paths.PluginPath}"); return false; }
                }
                try
                {
                    var asm = Assembly.LoadFrom(dllPath);
                    LogInfo($"[Bridge] Assembly.LoadFrom ok name={asm.GetName().Name} version={asm.GetName().Version}");
                }
                catch (Exception ex)
                {
                    LogError($"[Bridge] Assembly.LoadFrom FAILED ex={ex.GetType().Name}: {ex.Message}");
                    return false;
                }
            }

            _vmdPluginDir = Path.GetDirectoryName(dllPath);
            LogInfo($"[Bridge] KKS_VMDPlayPlugin dir={_vmdPluginDir}");

            _tLogger   = Type.GetType("VMDPlayPlugin.Core.Logger, KKS_VMDPlayPlugin");
            _tSettings = Type.GetType("VMDPlayPlugin.Core.Settings, KKS_VMDPlayPlugin");
            _tMgr      = Type.GetType("KKVMDPlayPlugin.VMDAnimationMgr, KKS_VMDPlayPlugin");
            _tCtrl     = Type.GetType("KKVMDPlayPlugin.VMDAnimationController, KKS_VMDPlayPlugin");

            LogDebug($"[Bridge] tLogger={_tLogger != null} tSettings={_tSettings != null} tMgr={_tMgr != null} tCtrl={_tCtrl != null}");

            if (_tLogger == null || _tSettings == null || _tMgr == null || _tCtrl == null)
            {
                LogError("[Bridge] resolve FAILED — one or more types not found");
                return false;
            }

            int found = 0, total = 0;

            void Resolve(string label, ref MethodInfo mi, Type t, string name, BindingFlags flags, Type[] paramTypes = null)
            {
                total++;
                try
                {
                    mi = paramTypes != null
                        ? t.GetMethod(name, flags, null, paramTypes, null)
                        : t.GetMethod(name, flags);
                    if (mi != null) { found++; LogDebug($"[Bridge] resolve {label} found"); }
                    else LogError($"[Bridge] resolve {label} NotFound");
                }
                catch (Exception ex) { LogError($"[Bridge] resolve {label} ex={ex.Message}"); }
            }
            void ResolveField(string label, ref FieldInfo fi, Type t, string name, BindingFlags flags)
            {
                total++;
                try
                {
                    fi = t.GetField(name, flags);
                    if (fi != null) { found++; LogDebug($"[Bridge] resolve {label} found"); }
                    else LogError($"[Bridge] resolve {label} NotFound");
                }
                catch (Exception ex) { LogError($"[Bridge] resolve {label} ex={ex.Message}"); }
            }
            void ResolveProp(string label, ref PropertyInfo pi, Type t, string name, BindingFlags flags)
            {
                total++;
                try
                {
                    pi = t.GetProperty(name, flags);
                    if (pi != null) { found++; LogDebug($"[Bridge] resolve {label} found"); }
                    else LogError($"[Bridge] resolve {label} NotFound");
                }
                catch (Exception ex) { LogError($"[Bridge] resolve {label} ex={ex.Message}"); }
            }
            void ResolveOptionalProp(string label, ref PropertyInfo pi, Type t, string name, BindingFlags flags)
            {
                try
                {
                    pi = t.GetProperty(name, flags);
                    if (pi != null) LogDebug($"[Bridge] resolve {label} found");
                    else LogWarn($"[Bridge] resolve {label} NotFound (optional)");
                }
                catch (Exception ex) { LogWarn($"[Bridge] resolve {label} optional ex={ex.Message}"); }
            }

            var pub  = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
            var nonp = BindingFlags.NonPublic | BindingFlags.Static;
            var inst = BindingFlags.Public | BindingFlags.Instance;

            Resolve("Logger.Init",              ref _miLoggerInit,      _tLogger,   "Init",               pub);
            Resolve("Settings.Init",            ref _miSettingsInit,    _tSettings, "Init",               nonp,
                new[] { typeof(ConfigFile), typeof(float) });
            ResolveField("Settings.BaseDir",    ref _fiSettingsBaseDir, _tSettings, "BaseDir",            pub);
            Resolve("VMDAnimationMgr.Install",  ref _miMgrInstall,      _tMgr,      "Install",            pub);
            Resolve("VMDAnimationController.Install", ref _miCtrlInstall, _tCtrl,   "Install",            pub);
            Resolve("LoadVMDAnimation(string)", ref _miCtrlLoadVmd_1,   _tCtrl,     "LoadVMDAnimation",   inst,
                new[] { typeof(string) });
            Resolve("LoadVMDAnimation(string,bool,bool)", ref _miCtrlLoadVmd_3, _tCtrl, "LoadVMDAnimation", inst,
                new[] { typeof(string), typeof(bool), typeof(bool) });
            Resolve("Play",                     ref _miCtrlPlay,        _tCtrl,     "Play",               inst);
            Resolve("Pause",                    ref _miCtrlPause,       _tCtrl,     "Pause",              inst);
            Resolve("Stop",                     ref _miCtrlStop,        _tCtrl,     "Stop",               inst);
            Resolve("Restart",                  ref _miCtrlRestart,     _tCtrl,     "Restart",            inst);
            Resolve("SetAnimPosition",          ref _miCtrlSetAnimPos,  _tCtrl,     "SetAnimPosition",    inst);
            Resolve("DeleteAnim",               ref _miCtrlDeleteAnim,  _tCtrl,     "DeleteAnim",         inst);
            Resolve("GetAnimTime",              ref _miCtrlGetAnimTime, _tCtrl,     "GetAnimTime",        inst);
            Resolve("IsVMDAnimeActive",         ref _miCtrlIsActive,    _tCtrl,     "IsVMDAnimeActive",   inst);
            ResolveField("lastLoadedVMD",       ref _fiCtrlLastLoadedVmd, _tCtrl,   "lastLoadedVMD",      inst);
            ResolveProp("VMDAnimationMgr.Instance",       ref _piMgrInstance,       _tMgr,      "Instance",  pub);
            ResolveOptionalProp("VMDAnimationMgr.AnimationLength", ref _piMgrAnimationLength, _tMgr, "AnimationLength", inst);
            ResolveProp("Settings.Instance",              ref _piSettingsInstance,  _tSettings, "Instance",  pub);

            // Mgr all-control methods (controllers + sound together)
            Resolve("VMDAnimationMgr.PlayAll",   ref _miMgrPlayAll,  _tMgr, "PlayAll",  inst);
            Resolve("VMDAnimationMgr.StopAll",   ref _miMgrStopAll,  _tMgr, "StopAll",  inst);
            Resolve("VMDAnimationMgr.PauseAll",  ref _miMgrPauseAll, _tMgr, "PauseAll", inst);

            // SoundMgr resolution (CoreCustomSoundMgr — abstract base)
            _tSoundMgr = Type.GetType("VMDPlayPlugin.Core.CoreCustomSoundMgr, KKS_VMDPlayPlugin");
            LogDebug($"[Bridge] tSoundMgr={_tSoundMgr != null}");
            if (_tSoundMgr != null)
            {
                total++;
                _fiMgrSoundMgr = _tMgr.GetField("SoundMgr", BindingFlags.Public | BindingFlags.Instance);
                if (_fiMgrSoundMgr != null) { found++; LogDebug("[Bridge] resolve Mgr.SoundMgr found"); }
                else LogError("[Bridge] resolve Mgr.SoundMgr NotFound");

                Resolve("SoundMgr.SetSoundClip",  ref _miSoundSetClip,   _tSoundMgr, "SetSoundClip",  inst, new[] { typeof(string) });
                Resolve("SoundMgr.PlaySound",     ref _miSoundPlay,      _tSoundMgr, "PlaySound",     inst, Type.EmptyTypes);
                Resolve("SoundMgr.PauseSound",    ref _miSoundPause,     _tSoundMgr, "PauseSound",    inst);
                Resolve("SoundMgr.StopSound",     ref _miSoundStop,      _tSoundMgr, "StopSound",     inst);
                Resolve("SoundMgr.ClearClip",     ref _miSoundClearClip, _tSoundMgr, "ClearClip",     inst);
                ResolveProp("SoundMgr.Loop",          ref _piSoundLoop,          _tSoundMgr, "Loop",          inst);
                ResolveProp("SoundMgr.SoundLength",   ref _piSoundLength,        _tSoundMgr, "SoundLength",   inst);
                ResolveProp("SoundMgr.audioFilePath", ref _piSoundAudioFilePath, _tSoundMgr, "audioFilePath", inst);
            }
            else
            {
                LogWarn("[Bridge] CoreCustomSoundMgr type not found — music playback disabled");
            }

            if (found < total)
            {
                LogError($"[Bridge] resolve FAILED ({found}/{total} members found)");
                return false;
            }
            LogInfo($"[Bridge] resolve ok ({found}/{total} members)");
            return true;
        }

        private void VmdBridgeInitLogger(ManualLogSource logSource)
        {
            try
            {
                _miLoggerInit.Invoke(null, new object[] { logSource });
                LogInfo("[Bridge] Logger.Init ok");
            }
            catch (Exception ex) { LogError($"[Bridge] Logger.Init FAILED ex={ex.GetType().Name}: {ex.Message}"); }
        }

        private void VmdBridgeInitSettings(ConfigFile config, float scale)
        {
            try
            {
                _miSettingsInit.Invoke(null, new object[] { config, scale });
                LogInfo("[Bridge] Settings.Init ok");
            }
            catch (Exception ex) { LogError($"[Bridge] Settings.Init FAILED ex={ex.GetType().Name}: {ex.Message}"); }
        }

        private void VmdBridgeSetSettingsBaseDir(string dir)
        {
            try
            {
                _fiSettingsBaseDir.SetValue(null, dir);
                LogInfo($"[Bridge] Settings.BaseDir={dir}");
            }
            catch (Exception ex) { LogError($"[Bridge] Settings.BaseDir FAILED ex={ex.GetType().Name}: {ex.Message}"); }
        }

        private bool VmdBridgeInstallMgr(GameObject container)
        {
            try
            {
                var result = _miMgrInstall.Invoke(null, new object[] { container });
                bool ok = result != null;
                if (ok) LogInfo($"[Bridge] VMDAnimationMgr.Install ok instance={result}");
                else    LogError("[Bridge] VMDAnimationMgr.Install returned null");
                return ok;
            }
            catch (Exception ex) { LogError($"[Bridge] VMDAnimationMgr.Install FAILED ex={ex.GetType().Name}: {ex.Message}"); return false; }
        }

        private object VmdBridgeInstallController(ChaControl chara)
        {
            try
            {
                var before = new System.Collections.Generic.HashSet<Component>(chara.GetComponentsInChildren<Component>(true));
                if (chara.GetComponent(_tCtrl) != null && !_ownedVmdObjects.Contains(chara.GetComponent(_tCtrl))) throw new InvalidOperationException("別のVMDコントローラが既に存在します");
                var result = _miCtrlInstall.Invoke(null, new object[] { chara });
                foreach (var component in chara.GetComponentsInChildren<Component>(true)) if (!before.Contains(component)) {
                    _ownedVmdObjects.Add(component);
                    if (component is Transform) _ownedVmdObjects.Add(component.gameObject);
                }
                string name = chara?.chaFile?.parameter?.fullname ?? "?";
                if (result != null) LogInfo($"[Bridge] Controller.Install chara={name} result=ok");
                else                LogWarn($"[Bridge] Controller.Install chara={name} result=null");
                return result;
            }
            catch (Exception ex) { LogError($"[Bridge] Controller.Install FAILED ex={ex.GetType().Name}: {ex.Message}"); return null; }
        }

        private bool VmdBridgeLoadVmd(object ctrl, string path, bool play, bool enableVMD)
        {
            try
            {
                _miCtrlLoadVmd_3.Invoke(ctrl, new object[] { path, play, enableVMD });
                LogInfo($"[Bridge] LoadVMDAnimation path={System.IO.Path.GetFileName(path)} play={play} enableVMD={enableVMD} ok");
                return true;
            }
            catch (Exception ex) { LogError($"[Bridge] LoadVMDAnimation FAILED ex={ex.GetType().Name}: {ex.Message}"); return false; }
        }

        private bool VmdBridgePlay(object ctrl)
        {
            try { _miCtrlPlay.Invoke(ctrl, null); LogInfo("[Bridge] Play ok"); return true; }
            catch (Exception ex) { LogError($"[Bridge] Play FAILED ex={ex.GetType().Name}: {ex.Message}"); return false; }
        }

        private bool VmdBridgePause(object ctrl)
        {
            try { _miCtrlPause.Invoke(ctrl, null); LogInfo("[Bridge] Pause ok"); return true; }
            catch (Exception ex) { LogError($"[Bridge] Pause FAILED ex={ex.GetType().Name}: {ex.Message}"); return false; }
        }

        private bool VmdBridgeStop(object ctrl)
        {
            try { _miCtrlStop.Invoke(ctrl, null); LogInfo("[Bridge] Stop ok"); return true; }
            catch (Exception ex) { LogError($"[Bridge] Stop FAILED ex={ex.GetType().Name}: {ex.Message}"); return false; }
        }

        private bool VmdBridgeRestart(object ctrl)
        {
            try { _miCtrlRestart.Invoke(ctrl, null); LogInfo("[Bridge] Restart ok"); return true; }
            catch (Exception ex) { LogError($"[Bridge] Restart FAILED ex={ex.GetType().Name}: {ex.Message}"); return false; }
        }

        private bool VmdBridgeSetAnimPosition(object ctrl, float t)
        {
            try { _miCtrlSetAnimPos.Invoke(ctrl, new object[] { t }); return true; }
            catch (Exception ex) { LogError($"[Bridge] SetAnimPosition FAILED ex={ex.GetType().Name}: {ex.Message}"); return false; }
        }

        private bool VmdBridgeDeleteAnim(object ctrl)
        {
            try { _miCtrlDeleteAnim.Invoke(ctrl, null); LogInfo("[Bridge] DeleteAnim ok"); return true; }
            catch (Exception ex) { LogError($"[Bridge] DeleteAnim FAILED ex={ex.GetType().Name}: {ex.Message}"); return false; }
        }

        private float VmdBridgeGetAnimTime(object ctrl)
        {
            try { return (float)_miCtrlGetAnimTime.Invoke(ctrl, null); }
            catch { return 0f; }
        }

        private bool VmdBridgeIsActive(object ctrl)
        {
            try { return (bool)_miCtrlIsActive.Invoke(ctrl, null); }
            catch { return false; }
        }

        private string VmdBridgeGetLastLoadedVmd(object ctrl)
        {
            try { return _fiCtrlLastLoadedVmd.GetValue(ctrl) as string ?? ""; }
            catch { return ""; }
        }

        private float VmdBridgeGetAnimationLength()
        {
            try
            {
                var sequence = _controller == null ? null : ReadMember(_controller, "sequenceSet") as MMD4U.MTSequenceSet;
                return sequence == null ? 0f : sequence.Length;
            }
            catch { return 0f; }
        }

        // ── Sound/Music bridge helpers ───────────────────────────────────────

        private object VmdBridgeGetSoundMgr()
        {
            try
            {
                if (_piMgrInstance == null || _fiMgrSoundMgr == null) return null;
                var mgr = _piMgrInstance.GetValue(null);
                if (mgr == null) { LogWarn("[Bridge] SoundMgr: Mgr.Instance is null"); return null; }
                var sm = _fiMgrSoundMgr.GetValue(mgr);
                if (sm == null) LogWarn("[Bridge] SoundMgr: field returned null");
                return sm;
            }
            catch (Exception ex) { LogError($"[Bridge] GetSoundMgr FAILED ex={ex.GetType().Name}: {ex.Message}"); return null; }
        }

        // ── Recursive DLL finder ─────────────────────────────────────────────

        private static string FindDllRecursive(string root, string fileName)
        {
            try
            {
                if (!Directory.Exists(root)) return null;
                foreach (var f in Directory.GetFiles(root, fileName, SearchOption.AllDirectories))
                    return f;
            }
            catch { }
            return null;
        }

        private void VmdBridgeSetSetting(string key, bool value)
        {
            try
            {
                if (_piSettingsInstance == null) return;
                var settingsInstance = _piSettingsInstance.GetValue(null);
                if (settingsInstance == null) return;

                var cfgField = _tSettings.GetField(key, BindingFlags.Public | BindingFlags.Instance);
                if (cfgField == null) { LogWarn($"[Bridge] SetSetting {key} field not found"); return; }

                var cfgEntry = cfgField.GetValue(settingsInstance);
                if (cfgEntry == null) return;

                var valueProp = cfgEntry.GetType().GetProperty("Value");
                if (valueProp == null) return;

                valueProp.SetValue(cfgEntry, value);
                LogInfo($"[Bridge] SetSetting {key}={value}");
            }
            catch (Exception ex) { LogError($"[Bridge] SetSetting {key} FAILED ex={ex.GetType().Name}: {ex.Message}"); }
        }
    }
}
