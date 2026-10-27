using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MainGameSubCameraDisplayProbe
{
    public sealed partial class Plugin
    {
        private static readonly string[] VideoEncoderLabels = { "h264_nvenc", "hevc_nvenc", "libx264" };

        private Process _videoProcess;
        private Stream _videoInputStream;
        private Texture2D _videoFrameTexture;
        private byte[] _videoRawBuffer;
        private bool _videoRecording;
        private bool _videoCaptureRequested;
        private Coroutine _videoCaptureCoroutine;
        private readonly WaitForEndOfFrame _videoEndOfFrame = new WaitForEndOfFrame();
        private float _nextVideoFrameAt;
        private int _videoWidth;
        private int _videoHeight;
        private int _videoFramesWritten;
        private string _videoOutputPath = string.Empty;

        private void DrawVideoRecordingUi()
        {
            GUILayout.Space(8f);
            GUILayout.Label("動画");
            GUILayout.Label("状態: " + (_videoRecording
                ? "録画中 " + _videoFramesWritten + " frames"
                : "停止"));
            if (!string.IsNullOrWhiteSpace(_videoOutputPath))
                GUILayout.Label("出力: " + Path.GetFileName(_videoOutputPath));

            GUILayout.BeginHorizontal();
            GUI.enabled = !_videoRecording;
            if (GUILayout.Button("録画開始", GUILayout.Height(26f)))
                StartVideoRecording();
            GUI.enabled = _videoRecording;
            if (GUILayout.Button("録画停止", GUILayout.Height(26f)))
                StopVideoRecording("ui-stop");
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            DrawSliderWithText("Video FPS", ref _settings.VideoFps, 1f, 120f, SourceSlider);
            DrawSliderWithText("Bitrate", ref _settings.VideoBitrateKbps, 300f, 50000f, SourceSlider);
            DrawSliderWithText("Video Hold", ref _settings.VideoTriggerHoldSeconds, 0.1f, 2f, SourceSlider);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Encoder", GUILayout.Width(90f));
            for (int i = 0; i < VideoEncoderLabels.Length; i++)
            {
                string encoder = VideoEncoderLabels[i];
                bool selected = string.Equals(_settings.VideoEncoder, encoder, StringComparison.Ordinal);
                if (GUILayout.Button((selected ? "● " : "") + encoder, GUILayout.Height(22f)))
                    _settings.VideoEncoder = encoder;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Video Folder", GUILayout.Width(90f));
            _settings.VideoOutputFolder = GUILayout.TextField(_settings.VideoOutputFolder ?? "video", GUILayout.Width(220f));
            GUILayout.EndHorizontal();
        }

        private void UpdateVideoRecording()
        {
            if (!_videoRecording)
                return;

            if (_subCamera == null || _renderTexture == null || _videoProcess == null || _videoProcess.HasExited)
            {
                StopVideoRecording("runtime-ended");
                return;
            }

            float fps = Mathf.Clamp(_settings.VideoFps, 1f, 120f);
            float interval = 1f / fps;
            float now = Time.unscaledTime;
            if (now < _nextVideoFrameAt)
                return;

            _videoCaptureRequested = true;
            _nextVideoFrameAt += interval;
            if (_nextVideoFrameAt < now - interval)
                _nextVideoFrameAt = now + interval;
        }

        private bool StartVideoRecording()
        {
            EnsureCameraProbe();
            if (_subCamera == null || _renderTexture == null)
            {
                SetStatus("録画開始失敗: サブカメラなし");
                return false;
            }

            if (_videoRecording)
                StopVideoRecording("restart");

            string ffmpegPath = ResolveVideoFfmpegPath();
            if (!File.Exists(ffmpegPath))
            {
                LogWarn("video recording skipped: ffmpeg not found path=" + ffmpegPath);
                SetStatus("録画開始失敗: ffmpegなし");
                return false;
            }

            string outputDir = EnsureVideoOutputDirectory();
            string fileName = "SubCamera_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4";
            string outputPath = Path.Combine(outputDir, fileName);

            _videoWidth = _renderTexture.width;
            _videoHeight = _renderTexture.height;
            _videoFrameTexture = new Texture2D(_videoWidth, _videoHeight, TextureFormat.RGB24, false);
            _videoRawBuffer = null;

            string encoder = SettingsStore.NormalizeVideoEncoder(_settings.VideoEncoder);
            int fps = Mathf.Clamp(Mathf.RoundToInt(_settings.VideoFps), 1, 120);
            int bitrateKbps = Mathf.Clamp(Mathf.RoundToInt(_settings.VideoBitrateKbps), 300, 50000);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = BuildFfmpegArguments(_videoWidth, _videoHeight, fps, bitrateKbps, encoder, outputPath),
                WorkingDirectory = outputDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardError = true
            };

            try
            {
                _videoProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = false };
                _videoProcess.ErrorDataReceived += OnVideoFfmpegErrorData;
                if (!_videoProcess.Start())
                    throw new InvalidOperationException("ffmpeg start returned false");
                _videoProcess.BeginErrorReadLine();
                _videoInputStream = _videoProcess.StandardInput.BaseStream;
                _videoRecording = true;
                _videoCaptureRequested = false;
                _videoCaptureCoroutine = StartCoroutine(CaptureVideoAtEndOfFrame());
                _nextVideoFrameAt = Time.unscaledTime;
                _videoFramesWritten = 0;
                _videoOutputPath = outputPath;
                LogInfo("video recording start path=" + outputPath + " size=" + _videoWidth + "x" + _videoHeight
                    + " fps=" + fps + " bitrateKbps=" + bitrateKbps + " encoder=" + encoder);
                SetStatus("録画開始: " + fileName);
                return true;
            }
            catch (Exception ex)
            {
                LogWarn("video recording start failed: " + ex.Message);
                SetStatus("録画開始失敗: " + ex.Message);
                CleanupVideoRecordingResources(killProcess: true);
                return false;
            }
        }

        private IEnumerator CaptureVideoAtEndOfFrame()
        {
            while (_videoRecording)
            {
                yield return _videoEndOfFrame;
                if (!_videoRecording || !_videoCaptureRequested)
                    continue;

                _videoCaptureRequested = false;
                if (_subCamera == null || !_subCamera.isActiveAndEnabled || _renderTexture == null
                    || _videoFrameTexture == null || _videoInputStream == null)
                {
                    StopVideoRecording("runtime-ended");
                    break;
                }

                CaptureVideoFrame();
            }

            _videoCaptureCoroutine = null;
        }

        private void CaptureVideoFrame()
        {
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                RenderTexture.active = _renderTexture;
                _videoFrameTexture.ReadPixels(new Rect(0f, 0f, _videoWidth, _videoHeight), 0, 0);
                _videoRawBuffer = _videoFrameTexture.GetRawTextureData();
                _videoInputStream.Write(_videoRawBuffer, 0, _videoRawBuffer.Length);
                _videoFramesWritten++;
            }
            catch (Exception ex)
            {
                LogWarn("video frame capture failed: " + ex.Message);
                StopVideoRecording("capture-error");
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }

        private void StopVideoRecording(string reason)
        {
            if (!_videoRecording && _videoProcess == null && _videoFrameTexture == null)
                return;

            string outputPath = _videoOutputPath;
            int frames = _videoFramesWritten;
            _videoRecording = false;
            _videoCaptureRequested = false;
            if (_videoCaptureCoroutine != null)
            {
                StopCoroutine(_videoCaptureCoroutine);
                _videoCaptureCoroutine = null;
            }

            try
            {
                _videoInputStream?.Flush();
            }
            catch
            {
            }

            CleanupVideoRecordingResources(killProcess: false);
            LogInfo("video recording stop reason=" + reason + " frames=" + frames + " path=" + outputPath);
            if (!string.IsNullOrWhiteSpace(outputPath))
                SetStatus("録画停止: " + Path.GetFileName(outputPath));
        }

        private void CleanupVideoRecordingResources(bool killProcess)
        {
            try
            {
                _videoInputStream?.Close();
            }
            catch
            {
            }
            _videoInputStream = null;

            if (_videoProcess != null)
            {
                try
                {
                    _videoProcess.ErrorDataReceived -= OnVideoFfmpegErrorData;
                    if (!_videoProcess.HasExited)
                    {
                        if (!killProcess && !_videoProcess.WaitForExit(3000))
                            killProcess = true;
                        if (killProcess && !_videoProcess.HasExited)
                            _videoProcess.Kill();
                    }
                }
                catch (Exception ex)
                {
                    LogWarn("video process cleanup failed: " + ex.Message);
                }

                try
                {
                    _videoProcess.Dispose();
                }
                catch
                {
                }
                _videoProcess = null;
            }

            if (_videoFrameTexture != null)
            {
                Destroy(_videoFrameTexture);
                _videoFrameTexture = null;
            }

            _videoRawBuffer = null;
            _videoWidth = 0;
            _videoHeight = 0;
            _videoFramesWritten = 0;
        }

        private void OnVideoFfmpegErrorData(object sender, DataReceivedEventArgs args)
        {
            if (string.IsNullOrWhiteSpace(args.Data))
                return;

            LogInfo("[ffmpeg] " + args.Data);
        }

        private string EnsureVideoOutputDirectory()
        {
            string outputDir = ResolveVideoOutputDir();
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);
            return outputDir;
        }

        private string ResolveVideoOutputDir()
        {
            string folder = _settings != null && !string.IsNullOrWhiteSpace(_settings.VideoOutputFolder)
                ? _settings.VideoOutputFolder.Trim()
                : "video";

            if (Path.IsPathRooted(folder))
                return folder;

            return Path.GetFullPath(Path.Combine(_pluginDir, folder));
        }

        private string ResolveVideoFfmpegPath()
        {
            string path = _settings != null && !string.IsNullOrWhiteSpace(_settings.VideoFfmpegPath)
                ? _settings.VideoFfmpegPath.Trim()
                : "..\\..\\_tools\\ffmpeg\\bin\\ffmpeg.exe";

            if (Path.IsPathRooted(path))
                return path;

            return Path.GetFullPath(Path.Combine(_pluginDir, path));
        }

        private static string BuildFfmpegArguments(int width, int height, int fps, int bitrateKbps, string encoder, string outputPath)
        {
            string args = "-y -hide_banner -loglevel warning"
                + " -f rawvideo -pix_fmt rgb24"
                + " -s " + width + "x" + height
                + " -r " + fps.ToString(CultureInfo.InvariantCulture)
                + " -i -"
                + " -vf vflip,format=yuv420p"
                + " -an -c:v " + encoder
                + " -b:v " + bitrateKbps.ToString(CultureInfo.InvariantCulture) + "k";

            if (string.Equals(encoder, "libx264", StringComparison.Ordinal))
                args += " -preset ultrafast";

            args += " -movflags +faststart " + QuoteArgument(outputPath);
            return args;
        }

        private static string QuoteArgument(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
