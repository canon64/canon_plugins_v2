using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private string _diagnosticDir;
        private float _nextDiagnostic;
        private string _diagnosticError = "";
        private bool _diagnosticsEnabled;

        [DataContract]
        private sealed class DiagnosticState
        {
            [DataMember] public string utc;
            [DataMember] public string version;
            [DataMember] public bool available;
            [DataMember] public bool enabled;
            [DataMember] public bool hScene;
            [DataMember] public string state;
            [DataMember] public bool controllerAlive;
            [DataMember] public float time;
            [DataMember] public float length;
            [DataMember] public string error;
            [DataMember] public string trial;
            [DataMember] public string controls;
        }

        private void InitDiagnostics()
        {
            _diagnosticsEnabled = Config.Bind("Diagnostics", "Enabled", false,
                "開発用のボーン走査・状態ファイル・診断ログ。有効化には再起動が必要。通常はfalse。").Value;
            if (!_diagnosticsEnabled) return;
            _diagnosticDir = Path.Combine(_pluginDir, "diagnostics");
            Directory.CreateDirectory(_diagnosticDir);
            Application.logMessageReceived += DiagnosticUnityLog;
            DiagnosticLog("SESSION " + Version + " " + Info.Location);
        }

        private void DiagnosticLog(string message)
        {
            if (string.IsNullOrEmpty(_diagnosticDir)) return;
            try { File.AppendAllText(Path.Combine(_diagnosticDir, "session.log"),
                DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine); }
            catch { /* Never recurse into Unity logging from this sink. */ }
        }

        private void DiagnosticUnityLog(string message, string trace, LogType type)
        {
            if ((message ?? "").IndexOf("VMD", StringComparison.OrdinalIgnoreCase) < 0 &&
                (trace ?? "").IndexOf("VMD", StringComparison.OrdinalIgnoreCase) < 0) return;
            DiagnosticLog("UNITY " + type + " " + message + "\n" + trace);
            if (type == LogType.Exception || type == LogType.Error) _diagnosticError = message + "\n" + trace;
        }

        private void TickDiagnostics()
        {
            if (!_diagnosticsEnabled) return;
            if (string.IsNullOrEmpty(_diagnosticDir) || Time.unscaledTime < _nextDiagnostic) return;
            _nextDiagnostic = Time.unscaledTime + 1f;
            try
            {
                string commandPath = Path.Combine(_diagnosticDir, "command.txt");
                if (File.Exists(commandPath))
                {
                    string command = File.ReadAllText(commandPath).Trim();
                    File.Move(commandPath, Path.Combine(_diagnosticDir, "command-" + DateTime.UtcNow.Ticks + ".processed"));
                    DiagnosticLog("COMMAND " + command);
                    var parts = command.Split(new[] { '|' }, 2);
                    switch (parts[0])
                    {
                        case "enable": _cfgEnabled.Value = true; break;
                        case "load":
                            if (!_available || !_insideHScene || parts.Length != 2) throw new InvalidOperationException("Load requires ready HScene and a path");
                            PlaybackLoadAndPlay(parts[1], false); break;
                        case "trial-load": StartDiagnosticLoad(parts[1], false); break;
                        case "trial-cancel": StartDiagnosticLoad(parts[1], true); break;
                        case "trial-release":
                            CancelDiagnosticLoad();
                            HarmonyLib.AccessTools.Property(_controller.GetType(), "VMDAnimEnabled").SetValue(_controller, false, null);
                            LogInfo("[Trial] release " + TrialSnapshot()); break;
                        case "picker": BeginPickVmdFile(); break;
                        case "play": PlaybackPlay(); break;
                        case "pause": PlaybackPause(); break;
                        case "stop": PlaybackStop(); break;
                        default: throw new InvalidOperationException("Unknown diagnostic command");
                    }
                }
            }
            catch (Exception ex) { _diagnosticError = ex.ToString(); DiagnosticLog("COMMAND FAILED " + ex); }
            try
            {
                var value = new DiagnosticState {
                    utc = DateTime.UtcNow.ToString("O"), version = Version, available = _available,
                    enabled = _cfgEnabled != null && _cfgEnabled.Value, hScene = _insideHScene,
                    state = _state.ToString(), controllerAlive = (_controller as UnityEngine.Object) != null,
                    time = _currentAnimTime, length = _animLength, error = _diagnosticError, trial = _trialStatus, controls = TrialSnapshot()
                };
                string path = Path.Combine(_diagnosticDir, "state.json");
                using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
                    new DataContractJsonSerializer(typeof(DiagnosticState)).WriteObject(output, value);
            }
            catch (Exception ex) { DiagnosticLog("STATE FAILED " + ex.Message); }
        }
    }
}
