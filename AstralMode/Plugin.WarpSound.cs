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
        private ConfigEntry<bool> _cfgUseWarpSound;
        private ConfigEntry<string> _cfgWarpSoundFolder;
        private ConfigEntry<string> _cfgWarpSoundFileName;
        private ConfigEntry<float> _cfgWarpSoundVolume;

        private GameObject _warpSoundRoot;
        private AudioSource _warpSoundSource;
        private AudioClip _warpSoundClip;
        private Coroutine _warpSoundLoadCoroutine;
        private string _warpSoundLoadedPath;

        private void InitWarpSoundConfig()
        {
            const string section = "70. Warp Sound";

            _cfgUseWarpSound = Config.Bind(
                section,
                "Use Warp Sound",
                true,
                "ワープ開始時に sound フォルダ内の音声ファイルを再生する。行き/帰り両方で鳴る。");

            _cfgWarpSoundFolder = Config.Bind(
                section,
                "Sound Folder",
                "sound",
                "音声フォルダ。相対パスならAstralMode.dllのあるフォルダ基準。");

            _cfgWarpSoundFileName = Config.Bind(
                section,
                "MP3 File Name",
                "",
                "再生する音声ファイル名。空なら sound フォルダ内の最初の .wav を優先し、なければ .mp3 を使う。既存設定名互換のため MP3 File Name のまま。");

            _cfgWarpSoundVolume = Config.Bind(
                section,
                "Volume",
                1.0f,
                new ConfigDescription("ワープ音量。0.0=無音、1.0=通常、2.0=大きめ。", new AcceptableValueRange<float>(0.0f, 2.0f)));

            SimpleFileLogger.Info($"Warp sound config: Use={_cfgUseWarpSound.Value}, Folder={_cfgWarpSoundFolder.Value}, FileName={_cfgWarpSoundFileName.Value}, Volume={_cfgWarpSoundVolume.Value:F3}");
        }

        private void PlayWarpSound(string phase)
        {
            try
            {
                if (_cfgUseWarpSound == null || !_cfgUseWarpSound.Value)
                {
                    SimpleFileLogger.Info($"Warp sound skipped by config: phase={phase}");
                    return;
                }

                EnsureWarpSoundSource();

                string path = ResolveWarpSoundPath();
                if (string.IsNullOrEmpty(path))
                {
                    SimpleFileLogger.Warn($"Warp sound skipped: no supported audio found. phase={phase}");
                    return;
                }

                if (_warpSoundLoadCoroutine != null)
                {
                    StopCoroutine(_warpSoundLoadCoroutine);
                    _warpSoundLoadCoroutine = null;
                }

                _warpSoundLoadCoroutine = StartCoroutine(LoadAndPlayWarpSound(path, phase));
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"Warp sound play failed: phase={phase}, type={ex.GetType().FullName}, message={ex.Message}");
            }
        }

        private void EnsureWarpSoundSource()
        {
            if (_warpSoundSource != null) return;

            _warpSoundRoot = new GameObject("AstralMode_WarpSound");
            UnityEngine.Object.DontDestroyOnLoad(_warpSoundRoot);
            _warpSoundRoot.hideFlags = HideFlags.DontSave;

            _warpSoundSource = _warpSoundRoot.AddComponent<AudioSource>();
            _warpSoundSource.playOnAwake = false;
            _warpSoundSource.loop = false;
            _warpSoundSource.spatialBlend = 0f;
            _warpSoundSource.ignoreListenerPause = true;
            _warpSoundSource.volume = _cfgWarpSoundVolume != null ? _cfgWarpSoundVolume.Value : 1.0f;

            SimpleFileLogger.Info("Warp sound AudioSource created");
        }

        private string ResolveWarpSoundPath()
        {
            string folder = _cfgWarpSoundFolder != null ? _cfgWarpSoundFolder.Value : "sound";
            if (string.IsNullOrEmpty(folder)) folder = "sound";

            string baseDir = !string.IsNullOrEmpty(_pluginDir) ? _pluginDir : Path.GetDirectoryName(Info.Location);
            if (string.IsNullOrEmpty(baseDir)) baseDir = Directory.GetCurrentDirectory();

            string fullFolder = Path.IsPathRooted(folder)
                ? folder
                : Path.Combine(baseDir, folder);
            fullFolder = Path.GetFullPath(fullFolder);

            if (!Directory.Exists(fullFolder))
            {
                SimpleFileLogger.Warn($"Warp sound folder not found: {fullFolder}");
                return null;
            }

            string fileName = _cfgWarpSoundFileName != null ? _cfgWarpSoundFileName.Value : string.Empty;
            if (!string.IsNullOrEmpty(fileName))
            {
                string configured = Path.IsPathRooted(fileName) ? fileName : Path.Combine(fullFolder, fileName);
                configured = Path.GetFullPath(configured);
                if (File.Exists(configured) && TryGetWarpSoundAudioType(configured, out _)) return configured;

                SimpleFileLogger.Warn($"Warp sound configured audio not found or unsupported: {configured}");
                return null;
            }

            string[] wavFiles = Directory.GetFiles(fullFolder, "*.wav", SearchOption.TopDirectoryOnly);
            Array.Sort(wavFiles, StringComparer.OrdinalIgnoreCase);
            if (wavFiles.Length > 0) return wavFiles[0];

            string[] mp3Files = Directory.GetFiles(fullFolder, "*.mp3", SearchOption.TopDirectoryOnly);
            Array.Sort(mp3Files, StringComparer.OrdinalIgnoreCase);
            if (mp3Files.Length > 0) return mp3Files[0];

            SimpleFileLogger.Warn($"Warp sound folder has no supported audio: {fullFolder}");
            return null;
        }

        private static bool TryGetWarpSoundAudioType(string path, out AudioType audioType)
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

        private IEnumerator LoadAndPlayWarpSound(string path, string phase)
        {
            if (_warpSoundSource == null) yield break;

            if (_warpSoundSource.isPlaying)
                _warpSoundSource.Stop();

            if (!TryGetWarpSoundAudioType(path, out AudioType audioType))
            {
                SimpleFileLogger.Warn($"Warp sound unsupported extension: phase={phase}, path={path}");
                _warpSoundLoadCoroutine = null;
                yield break;
            }

            if (_warpSoundClip == null || !string.Equals(_warpSoundLoadedPath, path, StringComparison.OrdinalIgnoreCase))
            {
                if (_warpSoundClip != null)
                {
                    UnityEngine.Object.Destroy(_warpSoundClip);
                    _warpSoundClip = null;
                    _warpSoundLoadedPath = null;
                }

                string url = new Uri(path).AbsoluteUri;
                SimpleFileLogger.Info($"Warp sound load start: phase={phase}, path={path}, audioType={audioType}, url={url}");

                using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
                {
                    yield return req.SendWebRequest();

                    SimpleFileLogger.Info($"Warp sound request done: phase={phase}, audioType={audioType}, networkError={req.isNetworkError}, httpError={req.isHttpError}, error={req.error}, bytes={req.downloadedBytes}");

                    bool failed = req.isNetworkError || req.isHttpError;
                    if (failed)
                    {
                        SimpleFileLogger.Warn($"Warp sound load failed: phase={phase}, path={path}, audioType={audioType}, error={req.error}");
                        _warpSoundLoadCoroutine = null;
                        yield break;
                    }

                    try
                    {
                        _warpSoundClip = DownloadHandlerAudioClip.GetContent(req);
                    }
                    catch (Exception ex)
                    {
                        SimpleFileLogger.Warn($"Warp sound GetContent failed: phase={phase}, path={path}, audioType={audioType}, type={ex.GetType().FullName}, message={ex.Message}");
                        _warpSoundLoadCoroutine = null;
                        yield break;
                    }

                    _warpSoundLoadedPath = path;
                }

                if (_warpSoundClip == null)
                {
                    SimpleFileLogger.Warn($"Warp sound load returned null clip: phase={phase}, path={path}, audioType={audioType}");
                    _warpSoundLoadCoroutine = null;
                    yield break;
                }

                _warpSoundClip.name = Path.GetFileName(path);
                SimpleFileLogger.Info($"Warp sound loaded: name={_warpSoundClip.name}, length={_warpSoundClip.length:F3}, channels={_warpSoundClip.channels}, frequency={_warpSoundClip.frequency}");
            }

            _warpSoundSource.volume = _cfgWarpSoundVolume != null ? _cfgWarpSoundVolume.Value : 1.0f;
            _warpSoundSource.clip = _warpSoundClip;
            _warpSoundSource.Play();
            SimpleFileLogger.Info($"Warp sound played: phase={phase}, clip={_warpSoundClip.name}, volume={_warpSoundSource.volume:F3}");
            _warpSoundLoadCoroutine = null;
        }

        private void DestroyWarpSound()
        {
            if (_warpSoundLoadCoroutine != null)
            {
                StopCoroutine(_warpSoundLoadCoroutine);
                _warpSoundLoadCoroutine = null;
            }

            if (_warpSoundSource != null && _warpSoundSource.isPlaying)
                _warpSoundSource.Stop();

            if (_warpSoundClip != null)
            {
                UnityEngine.Object.Destroy(_warpSoundClip);
                _warpSoundClip = null;
                _warpSoundLoadedPath = null;
            }

            if (_warpSoundRoot != null)
            {
                UnityEngine.Object.Destroy(_warpSoundRoot);
                _warpSoundRoot = null;
                _warpSoundSource = null;
                SimpleFileLogger.Info("Warp sound disposed");
            }
        }
    }
}
