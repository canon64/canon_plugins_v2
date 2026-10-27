using System;
using System.Collections;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Networking;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> _cfgUseAstralBgm;
        private ConfigEntry<string> _cfgAstralBgmFolder;
        private ConfigEntry<string> _cfgAstralBgmFileName;
        private ConfigEntry<float> _cfgAstralBgmVolume;

        private GameObject _astralBgmRoot;
        private AudioSource _astralBgmSource;
        private AudioClip _astralBgmClip;
        private Coroutine _astralBgmLoadCoroutine;
        private string _astralBgmLoadedPath;

        private void InitAstralBgmConfig()
        {
            const string section = "75. Astral BGM";

            _cfgUseAstralBgm = Config.Bind(
                section,
                "Use Astral BGM",
                true,
                "アストラルモード中に bgm フォルダ内の音声をループ再生する。");

            _cfgAstralBgmFolder = Config.Bind(
                section,
                "BGM Folder",
                "bgm",
                "BGMフォルダ。相対パスならAstralMode.dllのあるフォルダ基準。");

            _cfgAstralBgmFileName = Config.Bind(
                section,
                "BGM File Name",
                "",
                "再生するBGMファイル名。空なら bgm フォルダ内の最初の .wav を優先し、なければ .mp3 を使う。");

            _cfgAstralBgmVolume = Config.Bind(
                section,
                "Volume",
                2f,
                new ConfigDescription("アストラルBGM音量。0.0=無音、1.0=通常、2.0=大きめ。", new AcceptableValueRange<float>(0.0f, 2.0f)));

            SimpleFileLogger.Info($"Astral BGM config: Use={_cfgUseAstralBgm.Value}, Folder={_cfgAstralBgmFolder.Value}, FileName={_cfgAstralBgmFileName.Value}, Volume={_cfgAstralBgmVolume.Value:F3}");
        }

        internal void StartAstralBgm()
        {
            try
            {
                if (_cfgUseAstralBgm == null || !_cfgUseAstralBgm.Value)
                {
                    SimpleFileLogger.Info("Astral BGM skipped by config");
                    return;
                }

                EnsureAstralBgmSource();

                string path = ResolveAstralBgmPath();
                if (string.IsNullOrEmpty(path))
                {
                    SimpleFileLogger.Warn("Astral BGM skipped: no supported audio found in bgm folder");
                    return;
                }

                if (_astralBgmLoadCoroutine != null)
                {
                    StopCoroutine(_astralBgmLoadCoroutine);
                    _astralBgmLoadCoroutine = null;
                }

                _astralBgmLoadCoroutine = StartCoroutine(LoadAndPlayAstralBgm(path));
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"Astral BGM start failed: {ex.Message}");
            }
        }

        internal void StopAstralBgm()
        {
            try
            {
                if (_astralBgmLoadCoroutine != null)
                {
                    StopCoroutine(_astralBgmLoadCoroutine);
                    _astralBgmLoadCoroutine = null;
                }

                if (_astralBgmSource != null && _astralBgmSource.isPlaying)
                {
                    _astralBgmSource.Stop();
                    SimpleFileLogger.Info("Astral BGM stopped");
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"Astral BGM stop failed: {ex.Message}");
            }
        }

        private void EnsureAstralBgmSource()
        {
            if (_astralBgmSource != null) return;

            _astralBgmRoot = new GameObject("AstralMode_BGM");
            UnityEngine.Object.DontDestroyOnLoad(_astralBgmRoot);
            _astralBgmRoot.hideFlags = HideFlags.DontSave;

            _astralBgmSource = _astralBgmRoot.AddComponent<AudioSource>();
            _astralBgmSource.playOnAwake = false;
            _astralBgmSource.loop = true;
            _astralBgmSource.spatialBlend = 0f;
            _astralBgmSource.ignoreListenerPause = true;
            _astralBgmSource.volume = _cfgAstralBgmVolume != null ? _cfgAstralBgmVolume.Value : 0.6f;

            SimpleFileLogger.Info("Astral BGM AudioSource created (loop=true)");
        }

        private string ResolveAstralBgmPath()
        {
            string folder = _cfgAstralBgmFolder != null ? _cfgAstralBgmFolder.Value : "bgm";
            if (string.IsNullOrEmpty(folder)) folder = "bgm";

            string baseDir = !string.IsNullOrEmpty(_pluginDir) ? _pluginDir : Path.GetDirectoryName(Info.Location);
            if (string.IsNullOrEmpty(baseDir)) baseDir = Directory.GetCurrentDirectory();

            string fullFolder = Path.IsPathRooted(folder)
                ? folder
                : Path.Combine(baseDir, folder);
            fullFolder = Path.GetFullPath(fullFolder);

            if (!Directory.Exists(fullFolder))
            {
                SimpleFileLogger.Warn($"Astral BGM folder not found: {fullFolder}");
                return null;
            }

            string fileName = _cfgAstralBgmFileName != null ? _cfgAstralBgmFileName.Value : string.Empty;
            if (!string.IsNullOrEmpty(fileName))
            {
                string configured = Path.IsPathRooted(fileName) ? fileName : Path.Combine(fullFolder, fileName);
                configured = Path.GetFullPath(configured);
                if (File.Exists(configured) && TryGetAstralBgmAudioType(configured, out _)) return configured;

                SimpleFileLogger.Warn($"Astral BGM configured file not found or unsupported: {configured}");
                return null;
            }

            string[] wavFiles = Directory.GetFiles(fullFolder, "*.wav", SearchOption.TopDirectoryOnly);
            Array.Sort(wavFiles, StringComparer.OrdinalIgnoreCase);
            if (wavFiles.Length > 0) return wavFiles[0];

            string[] mp3Files = Directory.GetFiles(fullFolder, "*.mp3", SearchOption.TopDirectoryOnly);
            Array.Sort(mp3Files, StringComparer.OrdinalIgnoreCase);
            if (mp3Files.Length > 0) return mp3Files[0];

            SimpleFileLogger.Warn($"Astral BGM folder has no supported audio (.wav/.mp3): {fullFolder}");
            return null;
        }

        private static bool TryGetAstralBgmAudioType(string path, out AudioType audioType)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".wav")
            {
                audioType = AudioType.WAV;
                return true;
            }
            if (ext == ".mp3")
            {
                audioType = AudioType.MPEG;
                return true;
            }
            audioType = AudioType.UNKNOWN;
            return false;
        }

        private IEnumerator LoadAndPlayAstralBgm(string path)
        {
            if (_astralBgmSource == null) yield break;

            if (_astralBgmSource.isPlaying)
                _astralBgmSource.Stop();

            if (!TryGetAstralBgmAudioType(path, out AudioType audioType))
            {
                SimpleFileLogger.Warn($"Astral BGM unsupported extension: {path}");
                _astralBgmLoadCoroutine = null;
                yield break;
            }

            if (_astralBgmClip == null || !string.Equals(_astralBgmLoadedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                if (_astralBgmClip != null)
                {
                    UnityEngine.Object.Destroy(_astralBgmClip);
                    _astralBgmClip = null;
                    _astralBgmLoadedPath = null;
                }

                string url = new Uri(path).AbsoluteUri;
                SimpleFileLogger.Info($"Astral BGM load start: path={path}, audioType={audioType}");

                using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
                {
                    yield return req.SendWebRequest();

                    bool failed = req.isNetworkError || req.isHttpError;
                    if (failed)
                    {
                        SimpleFileLogger.Warn($"Astral BGM load failed: {path}, error={req.error}");
                        _astralBgmLoadCoroutine = null;
                        yield break;
                    }

                    try
                    {
                        _astralBgmClip = DownloadHandlerAudioClip.GetContent(req);
                    }
                    catch (Exception ex)
                    {
                        SimpleFileLogger.Warn($"Astral BGM GetContent failed: {ex.Message}");
                        _astralBgmLoadCoroutine = null;
                        yield break;
                    }

                    _astralBgmLoadedPath = path;
                }

                if (_astralBgmClip == null)
                {
                    SimpleFileLogger.Warn($"Astral BGM load returned null clip: {path}");
                    _astralBgmLoadCoroutine = null;
                    yield break;
                }

                _astralBgmClip.name = Path.GetFileName(path);
                SimpleFileLogger.Info($"Astral BGM loaded: {_astralBgmClip.name}, length={_astralBgmClip.length:F1}s");
            }

            _astralBgmSource.volume = _cfgAstralBgmVolume != null ? _cfgAstralBgmVolume.Value : 0.6f;
            _astralBgmSource.clip = _astralBgmClip;
            _astralBgmSource.Play();
            SimpleFileLogger.Info($"Astral BGM started: {_astralBgmClip.name}, vol={_astralBgmSource.volume:F2}, loop=true");
            _astralBgmLoadCoroutine = null;
        }

        private void DestroyAstralBgm()
        {
            if (_astralBgmLoadCoroutine != null)
            {
                StopCoroutine(_astralBgmLoadCoroutine);
                _astralBgmLoadCoroutine = null;
            }

            if (_astralBgmSource != null && _astralBgmSource.isPlaying)
                _astralBgmSource.Stop();

            if (_astralBgmClip != null)
            {
                UnityEngine.Object.Destroy(_astralBgmClip);
                _astralBgmClip = null;
                _astralBgmLoadedPath = null;
            }

            if (_astralBgmRoot != null)
            {
                UnityEngine.Object.Destroy(_astralBgmRoot);
                _astralBgmRoot = null;
                _astralBgmSource = null;
                SimpleFileLogger.Info("Astral BGM fully destroyed");
            }
        }

        /// <summary>
        /// コンフィグのBGM音量を現在再生中のソースに即時反映する。
        /// </summary>
        internal void SyncAstralBgmVolume()
        {
            if (_astralBgmSource != null && _cfgAstralBgmVolume != null)
            {
                _astralBgmSource.volume = _cfgAstralBgmVolume.Value;
            }
        }
    }
}
 
