using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace MainGameLogRelay
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    [BepInProcess("CharaStudio")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        public const string Guid = "com.kks.maingame.logrelay";
        public const string PluginName = "MainGameLogRelay";
        public const string Version = "1.0.0";

        internal static Plugin Instance { get; private set; }

        private sealed class RuntimeOwnerOverride
        {
            public bool? Enabled;
            public LogRelayOutputMode? OutputMode;
            public LogRelayLevel? MinimumLevel;
            public LogRelayFileLayout? FileLayout;
            public bool HasLogKey;
            public string LogKey = string.Empty;
        }

        private struct EffectiveOwnerConfig
        {
            public bool Enabled;
            public LogRelayOutputMode OutputMode;
            public LogRelayLevel MinimumLevel;
            public LogRelayFileLayout FileLayout;
            public string LogKey;
        }

        private readonly object _sync = new object();
        private readonly Dictionary<string, OwnerRule> _ownerRules = new Dictionary<string, OwnerRule>(StringComparer.Ordinal);
        private readonly Dictionary<string, RuntimeOwnerOverride> _runtimeOverrides = new Dictionary<string, RuntimeOwnerOverride>(StringComparer.Ordinal);

        private string _pluginDir;
        private string _pluginsRootDir;
        private string _logRootDir;
        private MainGameLogRelaySettings _settings;
        private OwnerFileLogger _ownerFileLogger;

        private void Awake()
        {
            Instance = this;
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? string.Empty;
            _pluginsRootDir = Directory.GetParent(_pluginDir)?.FullName ?? _pluginDir;
            _logRootDir = Path.Combine(_pluginDir, "log");
            Directory.CreateDirectory(_logRootDir);

            _settings = SettingsStore.LoadOrCreate(
                _pluginDir,
                msg => Logger.LogInfo("[" + PluginName + "] " + msg),
                msg => Logger.LogWarning("[" + PluginName + "] " + msg),
                msg => Logger.LogError("[" + PluginName + "] " + msg));

            _ownerFileLogger = new OwnerFileLogger();

            lock (_sync)
            {
                RebuildOwnerRulesLocked();
            }

            if (_runtimeLoggingEnabled && (_settings == null || _settings.ResetOwnerLogsOnStartup))
                ResetOwnerLogsOnStartup();

            InternalInfo("loaded");
            InternalInfo("settings=" + Path.Combine(_pluginDir, SettingsStore.FileName));
            InternalInfo(GetRelaySummary());
        }

        private void OnDestroy()
        {
            SaveSettings();
            InternalInfo("destroyed");
            Instance = null;
        }

        internal void Log(string owner, LogRelayLevel level, string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (!TryBuildEmissionContext(owner, level, out string normalizedOwner, out EffectiveOwnerConfig cfg))
                return;

            Emit(normalizedOwner, level, message ?? string.Empty, cfg);
        }

        internal void LogLazy(string owner, LogRelayLevel level, Func<string> messageFactory)
        {
            if (!_runtimeLoggingEnabled) return;
            if (messageFactory == null)
                return;
            if (!TryBuildEmissionContext(owner, level, out string normalizedOwner, out EffectiveOwnerConfig cfg))
                return;

            string message;
            try
            {
                message = messageFactory() ?? string.Empty;
            }
            catch (Exception ex)
            {
                message = "[LogLazyFactoryError] " + ex.GetType().Name + ": " + ex.Message;
                level = LogRelayLevel.Error;
            }

            Emit(normalizedOwner, level, message, cfg);
        }

        internal void SetOwnerEnabled(string owner, bool enabled)
        {
            if (!TryNormalizeOwner(owner, out string normalizedOwner))
                return;

            lock (_sync)
            {
                RuntimeOwnerOverride ov = GetOrCreateRuntimeOverrideLocked(normalizedOwner);
                ov.Enabled = enabled;
            }

            InternalState("runtime override: owner=" + normalizedOwner + " enabled=" + enabled);
        }

        internal void SetOwnerOutputMode(string owner, LogRelayOutputMode mode)
        {
            if (!TryNormalizeOwner(owner, out string normalizedOwner))
                return;

            mode = NormalizeMode(mode);
            lock (_sync)
            {
                RuntimeOwnerOverride ov = GetOrCreateRuntimeOverrideLocked(normalizedOwner);
                ov.OutputMode = mode;
            }

            InternalState("runtime override: owner=" + normalizedOwner + " mode=" + mode);
        }

        internal void SetOwnerMinimumLevel(string owner, LogRelayLevel level)
        {
            if (!TryNormalizeOwner(owner, out string normalizedOwner))
                return;

            level = NormalizeLevel(level);
            lock (_sync)
            {
                RuntimeOwnerOverride ov = GetOrCreateRuntimeOverrideLocked(normalizedOwner);
                ov.MinimumLevel = level;
            }

            InternalState("runtime override: owner=" + normalizedOwner + " minLevel=" + level);
        }

        internal void SetOwnerFileLayout(string owner, LogRelayFileLayout fileLayout)
        {
            if (!TryNormalizeOwner(owner, out string normalizedOwner))
                return;

            fileLayout = NormalizeFileLayout(fileLayout);
            lock (_sync)
            {
                RuntimeOwnerOverride ov = GetOrCreateRuntimeOverrideLocked(normalizedOwner);
                ov.FileLayout = fileLayout;
            }

            InternalState("runtime override: owner=" + normalizedOwner + " fileLayout=" + fileLayout);
        }

        internal void SetOwnerLogKey(string owner, string logKey)
        {
            if (!TryNormalizeOwner(owner, out string normalizedOwner))
                return;

            lock (_sync)
            {
                RuntimeOwnerOverride ov = GetOrCreateRuntimeOverrideLocked(normalizedOwner);
                ov.HasLogKey = true;
                ov.LogKey = logKey ?? string.Empty;
            }

            InternalState("runtime override: owner=" + normalizedOwner + " logKey=" + (logKey ?? string.Empty));
        }

        internal void ClearOwnerRuntimeOverrides(string owner)
        {
            if (!TryNormalizeOwner(owner, out string normalizedOwner))
                return;

            bool removed;
            lock (_sync)
            {
                removed = _runtimeOverrides.Remove(normalizedOwner);
            }

            if (removed)
                InternalState("runtime override cleared: owner=" + normalizedOwner);
        }

        internal string GetOwnerSummary(string owner)
        {
            if (!TryNormalizeOwner(owner, out string normalizedOwner))
                return "invalid-owner";

            EffectiveOwnerConfig cfg;
            bool relayEnabled;
            lock (_sync)
            {
                relayEnabled = _settings != null && _settings.Enabled;
                cfg = ResolveEffectiveConfigLocked(normalizedOwner);
            }

            string path = ResolveOwnerLogPath(normalizedOwner, cfg.LogKey, cfg.FileLayout, logKeyWasFallback: false);
            return "relayEnabled=" + relayEnabled
                + ", owner=" + normalizedOwner
                + ", enabled=" + cfg.Enabled
                + ", mode=" + cfg.OutputMode
                + ", minLevel=" + cfg.MinimumLevel
                + ", fileLayout=" + cfg.FileLayout
                + ", logPath=" + path;
        }

        internal string GetRelaySummary()
        {
            lock (_sync)
            {
                if (_settings == null)
                    return "relay settings=null";

                return "relay enabled=" + _settings.Enabled
                    + ", defaultOwnerEnabled=" + _settings.DefaultOwnerEnabled
                    + ", defaultMode=" + _settings.DefaultOutputMode
                    + ", defaultMinLevel=" + _settings.DefaultMinimumLevel
                    + ", defaultFileLayout=" + NormalizeFileLayout(_settings.FileLayout)
                    + ", ownerRules=" + _ownerRules.Count
                    + ", runtimeOverrides=" + _runtimeOverrides.Count
                    + ", pluginsRoot=" + _pluginsRootDir
                    + ", fallbackLogRoot=" + _logRootDir;
            }
        }

        private bool TryBuildEmissionContext(string ownerRaw, LogRelayLevel level, out string owner, out EffectiveOwnerConfig cfg)
        {
            owner = string.Empty;
            cfg = default(EffectiveOwnerConfig);

            if (!TryNormalizeOwner(ownerRaw, out owner))
                return false;

            bool relayEnabled;
            lock (_sync)
            {
                relayEnabled = _settings != null && _settings.Enabled;
                cfg = ResolveEffectiveConfigLocked(owner);
            }

            if (!relayEnabled)
                return false;
            if (!cfg.Enabled)
                return false;
            if (level < cfg.MinimumLevel)
                return false;
            return true;
        }

        private void Emit(string owner, LogRelayLevel level, string message, EffectiveOwnerConfig cfg)
        {
            LogRelayOutputMode mode = NormalizeMode(cfg.OutputMode);
            string levelText = ToLevelText(level);

            if (mode == LogRelayOutputMode.FileOnly || mode == LogRelayOutputMode.Both)
            {
                string ownerPath = ResolveOwnerLogPath(owner, cfg.LogKey, cfg.FileLayout, logKeyWasFallback: false);
                _ownerFileLogger?.Write(ownerPath, levelText, message);
            }

            if (mode == LogRelayOutputMode.BepInExOnly || mode == LogRelayOutputMode.Both)
            {
                string line = "[" + owner + "] " + message;
                switch (level)
                {
                    case LogRelayLevel.Warning:
                        Logger.LogWarning("[" + PluginName + "] " + line);
                        break;
                    case LogRelayLevel.Error:
                        Logger.LogError("[" + PluginName + "] " + line);
                        break;
                    default:
                        Logger.LogInfo("[" + PluginName + "] " + line);
                        break;
                }
            }
        }

        private string ResolveOwnerLogPath(string owner, string rawLogKey, LogRelayFileLayout fileLayout, bool logKeyWasFallback)
        {
            string key = string.IsNullOrWhiteSpace(rawLogKey) ? owner : rawLogKey;
            if (!TryNormalizeLogKey(key, out string normalized))
            {
                TryNormalizeLogKey(owner, out normalized);
                if (!logKeyWasFallback && ShouldLogInternalState())
                    InternalWarn("invalid logKey rejected. owner=" + owner + " logKey=" + (rawLogKey ?? string.Empty) + " fallback=" + normalized);
            }

            if (string.IsNullOrWhiteSpace(normalized))
                normalized = "unknown_owner";

            if (NormalizeFileLayout(fileLayout) == LogRelayFileLayout.Shared)
            {
                string relative = normalized.EndsWith(".log", StringComparison.OrdinalIgnoreCase)
                    ? normalized
                    : normalized + ".log";

                string combined = Path.Combine(_logRootDir, relative.Replace('/', Path.DirectorySeparatorChar));
                string fullShared = Path.GetFullPath(combined);
                string rootShared = EnsureTrailingSeparator(Path.GetFullPath(_logRootDir));
                if (!fullShared.StartsWith(rootShared, StringComparison.OrdinalIgnoreCase))
                {
                    string fallbackShared = "unknown_owner.log";
                    fullShared = Path.Combine(_logRootDir, fallbackShared);
                    if (ShouldLogInternalState())
                        InternalWarn("resolved shared log path escaped root. owner=" + owner + " key=" + normalized + " fallback=" + fallbackShared);
                }

                return fullShared;
            }

            ResolveOwnerLogRoute(normalized, owner, out string pluginFolder, out string fileName);
            string pluginBase = Path.Combine(_pluginsRootDir, pluginFolder);
            string pluginLogRoot = Path.Combine(pluginBase, "log");
            string full = Path.GetFullPath(Path.Combine(pluginLogRoot, fileName));
            string root = EnsureTrailingSeparator(Path.GetFullPath(pluginLogRoot));

            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                string fallback = "unknown_owner.log";
                full = Path.Combine(_logRootDir, fallback);
                if (ShouldLogInternalState())
                    InternalWarn("resolved log path escaped root. owner=" + owner + " key=" + normalized + " fallback=" + fallback);
            }

            return full;
        }

        private static void ResolveOwnerLogRoute(string normalizedKey, string owner, out string pluginFolder, out string fileName)
        {
            pluginFolder = "unknown_owner";
            fileName = "unknown_owner.log";

            string[] segments = normalizedKey.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return;

            int stemStart = 0;
            string pluginToken = segments[0];
            if (segments.Length >= 2 && IsScopeSegment(segments[0]))
            {
                pluginToken = segments[1];
                stemStart = 1;
            }

            int dot = pluginToken.IndexOf('.');
            if (dot > 0)
                pluginToken = pluginToken.Substring(0, dot);
            if (string.IsNullOrWhiteSpace(pluginToken))
                pluginToken = owner;
            pluginToken = SanitizeFileNameSegment(pluginToken);
            if (string.IsNullOrWhiteSpace(pluginToken))
                pluginToken = "unknown_owner";
            pluginFolder = pluginToken;

            string stem;
            if (stemStart < segments.Length)
            {
                stem = string.Join(".", segments, stemStart, segments.Length - stemStart);
            }
            else
            {
                stem = pluginFolder;
            }

            if (string.IsNullOrWhiteSpace(stem))
                stem = pluginFolder;
            stem = SanitizeFileNameSegment(stem);
            if (string.IsNullOrWhiteSpace(stem))
                stem = pluginFolder;
            if (!stem.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                stem += ".log";
            fileName = stem;
        }

        private static bool IsScopeSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment))
                return false;
            return string.Equals(segment, "main", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "studio", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "vr", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "shared", StringComparison.OrdinalIgnoreCase)
                || string.Equals(segment, "global", StringComparison.OrdinalIgnoreCase);
        }

        private void ResetOwnerLogsOnStartup()
        {
            try
            {
                if (Directory.Exists(_pluginsRootDir))
                {
                    string[] pluginDirs = Directory.GetDirectories(_pluginsRootDir);
                    for (int i = 0; i < pluginDirs.Length; i++)
                    {
                        string pluginLogDir = Path.Combine(pluginDirs[i], "log");
                        if (!Directory.Exists(pluginLogDir))
                            continue;

                        string[] files = Directory.GetFiles(pluginLogDir, "*.log", SearchOption.AllDirectories);
                        for (int j = 0; j < files.Length; j++)
                        {
                            try { File.Delete(files[j]); } catch { }
                        }
                    }
                }

                if (Directory.Exists(_logRootDir))
                {
                    string[] fallbackFiles = Directory.GetFiles(_logRootDir, "*.log", SearchOption.AllDirectories);
                    for (int i = 0; i < fallbackFiles.Length; i++)
                    {
                        try { File.Delete(fallbackFiles[i]); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                InternalWarn("owner log reset failed: " + ex.Message);
            }
        }

        private void SaveSettings()
        {
            lock (_sync)
            {
                SettingsStore.Save(_pluginDir, _settings, msg => Logger.LogWarning("[" + PluginName + "] " + msg));
            }
        }

        private void RebuildOwnerRulesLocked()
        {
            _ownerRules.Clear();
            if (_settings == null || _settings.OwnerRules == null)
                return;

            for (int i = 0; i < _settings.OwnerRules.Length; i++)
            {
                OwnerRule src = _settings.OwnerRules[i];
                if (src == null)
                    continue;
                if (!TryNormalizeOwner(src.Owner, out string owner))
                    continue;

                _ownerRules[owner] = src;
            }
        }

        private EffectiveOwnerConfig ResolveEffectiveConfigLocked(string owner)
        {
            var cfg = new EffectiveOwnerConfig
            {
                Enabled = _settings == null || _settings.DefaultOwnerEnabled,
                OutputMode = _settings == null ? LogRelayOutputMode.Both : NormalizeMode(_settings.DefaultOutputMode),
                MinimumLevel = _settings == null ? LogRelayLevel.Info : NormalizeLevel(_settings.DefaultMinimumLevel),
                FileLayout = _settings == null ? LogRelayFileLayout.PerPlugin : NormalizeFileLayout(_settings.FileLayout),
                LogKey = string.Empty
            };

            if (_ownerRules.TryGetValue(owner, out OwnerRule baseRule))
            {
                if (baseRule.OverrideEnabled)
                    cfg.Enabled = baseRule.Enabled;
                if (baseRule.OverrideOutputMode)
                    cfg.OutputMode = NormalizeMode(baseRule.OutputMode);
                if (baseRule.OverrideMinimumLevel)
                    cfg.MinimumLevel = NormalizeLevel(baseRule.MinimumLevel);
                if (baseRule.OverrideFileLayout)
                    cfg.FileLayout = NormalizeFileLayout(baseRule.FileLayout);
                if (!string.IsNullOrWhiteSpace(baseRule.LogKey))
                    cfg.LogKey = baseRule.LogKey.Trim();
            }

            if (_runtimeOverrides.TryGetValue(owner, out RuntimeOwnerOverride runtime))
            {
                if (runtime.Enabled.HasValue)
                    cfg.Enabled = runtime.Enabled.Value;
                if (runtime.OutputMode.HasValue)
                    cfg.OutputMode = NormalizeMode(runtime.OutputMode.Value);
                if (runtime.MinimumLevel.HasValue)
                    cfg.MinimumLevel = NormalizeLevel(runtime.MinimumLevel.Value);
                if (runtime.FileLayout.HasValue)
                    cfg.FileLayout = NormalizeFileLayout(runtime.FileLayout.Value);
                if (runtime.HasLogKey)
                    cfg.LogKey = runtime.LogKey == null ? string.Empty : runtime.LogKey.Trim();
            }

            return cfg;
        }

        private RuntimeOwnerOverride GetOrCreateRuntimeOverrideLocked(string owner)
        {
            if (_runtimeOverrides.TryGetValue(owner, out RuntimeOwnerOverride ov))
                return ov;

            ov = new RuntimeOwnerOverride();
            _runtimeOverrides.Add(owner, ov);
            return ov;
        }

        private bool ShouldLogInternalState()
        {
            lock (_sync)
            {
                return _settings == null || _settings.LogInternalState;
            }
        }

        private static bool TryNormalizeOwner(string raw, out string owner)
        {
            owner = raw == null ? string.Empty : raw.Trim();
            return owner.Length > 0;
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Path.DirectorySeparatorChar.ToString();
            if (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
                return path;
            return path + Path.DirectorySeparatorChar;
        }

        private static LogRelayLevel NormalizeLevel(LogRelayLevel level)
        {
            if (level < LogRelayLevel.Debug)
                return LogRelayLevel.Debug;
            if (level > LogRelayLevel.Error)
                return LogRelayLevel.Error;
            return level;
        }

        private static LogRelayOutputMode NormalizeMode(LogRelayOutputMode mode)
        {
            if (mode < LogRelayOutputMode.FileOnly)
                return LogRelayOutputMode.FileOnly;
            if (mode > LogRelayOutputMode.Both)
                return LogRelayOutputMode.Both;
            return mode;
        }

        private static LogRelayFileLayout NormalizeFileLayout(LogRelayFileLayout layout)
        {
            if (layout < LogRelayFileLayout.PerPlugin)
                return LogRelayFileLayout.PerPlugin;
            if (layout > LogRelayFileLayout.Shared)
                return LogRelayFileLayout.Shared;
            return layout;
        }

        private static string ToLevelText(LogRelayLevel level)
        {
            switch (level)
            {
                case LogRelayLevel.Debug: return "DEBUG";
                case LogRelayLevel.Info: return "INFO";
                case LogRelayLevel.Warning: return "WARN";
                case LogRelayLevel.Error: return "ERROR";
                default: return "INFO";
            }
        }

        private static bool TryNormalizeLogKey(string raw, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            string candidate = raw.Trim().Replace('\\', '/');
            if (candidate.Length == 0)
                return false;
            if (candidate.Contains(":"))
                return false;
            if (candidate.StartsWith("/", StringComparison.Ordinal))
                return false;
            if (Path.IsPathRooted(candidate))
                return false;

            string[] segments = candidate.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return false;

            var sanitized = new List<string>(segments.Length);
            for (int i = 0; i < segments.Length; i++)
            {
                string seg = segments[i].Trim();
                if (seg.Length == 0)
                    continue;
                if (seg == "." || seg == "..")
                    return false;

                string cleaned = SanitizeFileNameSegment(seg);
                if (cleaned.Length == 0)
                    cleaned = "_";
                sanitized.Add(cleaned);
            }

            if (sanitized.Count == 0)
                return false;

            normalized = string.Join("/", sanitized.ToArray());
            return true;
        }

        private static string SanitizeFileNameSegment(string segment)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var arr = segment.ToCharArray();
            for (int i = 0; i < arr.Length; i++)
            {
                if (Array.IndexOf(invalid, arr[i]) >= 0)
                    arr[i] = '_';
            }
            return new string(arr);
        }

        private void InternalInfo(string message)
        {
            if (!ShouldLogInternalState())
                return;
            Logger.LogInfo("[" + PluginName + "] " + message);
        }

        private void InternalWarn(string message)
        {
            Logger.LogWarning("[" + PluginName + "] " + message);
        }

        private void InternalState(string message)
        {
            if (!ShouldLogInternalState())
                return;
            Logger.LogInfo("[" + PluginName + "] " + message);
        }
    }
}
