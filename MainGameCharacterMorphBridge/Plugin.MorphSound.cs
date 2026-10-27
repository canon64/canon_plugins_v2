using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace MainGameCharacterMorphBridge
{
    /// <summary>
    /// 変身(Blend遷移)の開始に合わせて効果音を鳴らす。
    ///
    /// ファイルは一度読んだらAudioClipを保持して使い回す。パスを変えた時だけ読み直す。
    /// 読み込みに失敗しても変身自体は止めない。音が鳴らないだけにする。
    /// </summary>
    public sealed partial class Plugin
    {
        private AudioSource _morphAudioSource;
        private AudioClip _morphClip;
        private string _morphClipPath = string.Empty;
        private Coroutine _morphClipLoadCoroutine;
        private bool _morphClipLoadFailed;

        /// <summary>
        /// 通常の音声診断は EnableLogs に従う。失敗だけは常時警告する。
        /// </summary>
        private void LogSound(string message)
        {
            if (message.StartsWith("FAILED", StringComparison.Ordinal)
                || message.StartsWith("WARNING", StringComparison.Ordinal)
                || message.StartsWith("skipped: settings null", StringComparison.Ordinal)
                || message.StartsWith("skipped: path empty", StringComparison.Ordinal)
                || message.StartsWith("play skipped:", StringComparison.Ordinal))
                LogWarnAlways("morph sound " + message);
            else
                LogExecution("morph sound " + message);
        }

        /// <summary>
        /// テスト再生用。一度失敗すると同じパスでは再試行しない作りなので、
        /// ここだけは失敗の記憶を捨ててから鳴らす。ファイルを差し替えた直後にも効く。
        /// </summary>
        internal void ForcePlayMorphSound()
        {
            _morphClip = null;
            _morphClipPath = string.Empty;
            _morphClipLoadFailed = false;
            PlayMorphSound();
        }

        internal void PlayMorphSound()
        {
            if (_settings == null)
            {
                LogSound("skipped: settings null");
                return;
            }

            if (!_settings.MorphSoundEnabled)
            {
                LogSound("skipped: disabled");
                return;
            }

            string path = PluginSettings.NormalizePath(_settings.MorphSoundPath);
            if (path.Length <= 0)
            {
                LogSound("skipped: path empty");
                return;
            }

            // パスが変わったら読み直す。失敗の記憶もそこで捨てる。
            if (!string.Equals(path, _morphClipPath, StringComparison.OrdinalIgnoreCase))
            {
                _morphClip = null;
                _morphClipPath = path;
                _morphClipLoadFailed = false;
            }

            if (_morphClip != null)
            {
                PlayLoadedMorphClip();
                return;
            }

            if (_morphClipLoadFailed)
            {
                LogSound("skipped: previous load failed for " + path);
                return;
            }

            if (_morphClipLoadCoroutine != null)
            {
                LogSound("skipped: load already running");
                return;
            }

            path = ResolvePlayablePath(path);
            LogSound("loading: " + path);
            _morphClipLoadCoroutine = StartCoroutine(LoadMorphClipRoutine(path, playWhenReady: true));
        }

        private void PlayLoadedMorphClip()
        {
            if (_morphClip == null)
            {
                LogSound("play skipped: clip null");
                return;
            }

            EnsureMorphAudioSource();
            if (_morphAudioSource == null)
            {
                LogSound("play skipped: audio source null");
                return;
            }

            // AudioSource.volume は 1.0 で頭打ちになる。それ以上はゲインで持ち上げる。
            float volume = PluginSettings.Clamp01(_settings.MorphSoundVolume);
            float gain = PluginSettings.ClampFloat(_settings.MorphSoundGain, 0f, 8f);

            _morphAudioSource.Stop();
            _morphAudioSource.volume = volume;
            _morphAudioSource.PlayOneShot(_morphClip, volume * gain);

            if (_runtimeLoggingEnabled && _settings != null && _settings.EnableLogs)
                LogSound("played: volume=" + volume.ToString("0.00")
                    + " gain=" + gain.ToString("0.00")
                    + " length=" + _morphClip.length.ToString("0.00") + "s"
                    + " listeners=" + FindObjectsOfType<AudioListener>().Length
                    + " path=" + _morphClipPath);
        }

        private void EnsureMorphAudioSource()
        {
            if (_morphAudioSource != null)
                return;

            // シーン内の音源に紛れないよう、自前のGameObjectに置く。
            // 3D減衰を切って、カメラ位置に関係なく一定の音量で鳴らす。
            var go = new GameObject(PluginName + "Audio");
            go.transform.SetParent(transform, false);
            _morphAudioSource = go.AddComponent<AudioSource>();
            _morphAudioSource.playOnAwake = false;
            _morphAudioSource.loop = false;
            _morphAudioSource.spatialBlend = 0f;
            _morphAudioSource.bypassEffects = true;
            _morphAudioSource.bypassListenerEffects = true;
            _morphAudioSource.ignoreListenerPause = true;
        }

        private IEnumerator LoadMorphClipRoutine(string path, bool playWhenReady)
        {
            if (!File.Exists(path))
            {
                _morphClipLoadFailed = true;
                _morphClipLoadCoroutine = null;
                LogSound("FAILED not found: " + path);
                yield break;
            }

            string url = new Uri(path).AbsoluteUri;
            AudioType audioType = ResolveAudioType(path);

            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
            {
                yield return request.SendWebRequest();

                if (request.isNetworkError || request.isHttpError)
                {
                    _morphClipLoadFailed = true;
                    _morphClipLoadCoroutine = null;
                    LogSound("FAILED request: " + request.error + " type=" + audioType + " path=" + path);
                    yield break;
                }

                AudioClip clip = null;
                try
                {
                    clip = DownloadHandlerAudioClip.GetContent(request);
                }
                catch (Exception ex)
                {
                    LogSound("FAILED decode: " + ex.Message + " type=" + audioType + " path=" + path);
                }

                if (clip == null)
                {
                    _morphClipLoadFailed = true;
                    _morphClipLoadCoroutine = null;
                    LogSound("FAILED decode: clip null type=" + audioType + " path=" + path);
                    yield break;
                }

                clip.name = Path.GetFileName(path);
                _morphClip = clip;
                _morphClipPath = path;
                _morphClipLoadFailed = false;
                LogSound("loaded: " + path
                    + " length=" + clip.length.ToString("0.00") + "s"
                    + " channels=" + clip.channels
                    + " frequency=" + clip.frequency
                    + " state=" + clip.loadState);
            }

            _morphClipLoadCoroutine = null;

            if (playWhenReady)
                PlayLoadedMorphClip();
        }

        internal const string SoundFolderName = "sound";

        /// <summary>
        /// 実際に読むファイルを決める。
        ///
        /// 相対パスはプラグインフォルダ基準。他ドライブを見に行かせないため、既定は sound\ 配下。
        /// UnityはMP3をランタイムでデコードできない（standaloneの制約）ので、mp3が指定されていたら
        /// 同名の wav / ogg を探して黙って差し替える。
        /// </summary>
        private string ResolvePlayablePath(string path)
        {
            string soundDir = Path.Combine(_pluginDir ?? string.Empty, SoundFolderName);

            // 相対パスはプラグインフォルダ基準に直す。
            if (!Path.IsPathRooted(path))
                path = Path.Combine(_pluginDir ?? string.Empty, path);

            string baseName = Path.GetFileNameWithoutExtension(path) ?? string.Empty;
            string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();

            // sound\ に同名の再生できる形式があれば最優先。設定が古い絶対パスのままでも拾える。
            foreach (string candidateExt in new[] { ".wav", ".ogg" })
            {
                string candidate = Path.Combine(soundDir, baseName + candidateExt);
                if (File.Exists(candidate))
                {
                    if (!string.Equals(candidate, path, StringComparison.OrdinalIgnoreCase))
                        LogSound("using plugin sound folder: " + candidate);
                    return candidate;
                }
            }

            if (ext == ".mp3")
            {
                // 指定された場所の隣に変換済みがあるならそれを使う。
                string dir = Path.GetDirectoryName(path) ?? string.Empty;
                foreach (string candidateExt in new[] { ".wav", ".ogg" })
                {
                    string candidate = Path.Combine(dir, baseName + candidateExt);
                    if (File.Exists(candidate))
                    {
                        LogSound("using " + candidateExt + " instead of mp3 (unity cannot decode mp3): " + candidate);
                        return candidate;
                    }
                }

                LogSound("WARNING mp3 specified and no wav/ogg found; unity cannot decode mp3");
            }

            return path;
        }

        private static AudioType ResolveAudioType(string path)
        {
            string ext = (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
            switch (ext)
            {
                case ".wav": return AudioType.WAV;
                case ".ogg": return AudioType.OGGVORBIS;
                case ".it": return AudioType.IT;
                case ".mod": return AudioType.MOD;
                case ".s3m": return AudioType.S3M;
                case ".xm": return AudioType.XM;
                default: return AudioType.MPEG;
            }
        }

        private void StopMorphSound()
        {
            if (_morphAudioSource != null && _morphAudioSource.isPlaying)
                _morphAudioSource.Stop();
        }
    }
}
