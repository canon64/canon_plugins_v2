using System;
using System.Diagnostics;
using System.IO;

namespace MainGameBeatSyncSpeed
{
    public partial class Plugin
    {
        /// <summary>
        /// WAVファイルを読み込み、拍ごとのエネルギー強度配列を返す。
        /// </summary>
        private bool TryAnalyzeWav(string path, float bpm, float lowPassHz,
                                   out float[] beatIntensities, out float beatDurationSec)
        {
            beatIntensities  = null;
            beatDurationSec  = 0f;

            try
            {
                if (!TryReadWavPcm(path, out float[] samples, out int sampleRate))
                {
                    LogWarn($"WAV読み込み失敗: {path}");
                    return false;
                }

                // IIRローパスフィルタ（1次RC）
                float[] filtered = ApplyLowPass(samples, sampleRate, lowPassHz);

                // 拍ごとRMS
                float beatSec      = 60f / bpm;
                int   beatSamples  = (int)(beatSec * sampleRate);
                if (beatSamples <= 0)
                {
                    LogWarn("beatSamples<=0: BPMまたはsampleRateが不正");
                    return false;
                }

                int beatCount = filtered.Length / beatSamples;
                if (beatCount <= 0)
                {
                    LogWarn("WAVが短すぎて拍が取れない");
                    return false;
                }

                float[] rms = new float[beatCount];
                for (int b = 0; b < beatCount; b++)
                {
                    int start = b * beatSamples;
                    int end   = Math.Min(start + beatSamples, filtered.Length);
                    double sum = 0;
                    for (int i = start; i < end; i++)
                        sum += filtered[i] * filtered[i];
                    rms[b] = (float)Math.Sqrt(sum / (end - start));
                }

                // 移動平均スムージング（ウィンドウ3拍）
                float[] smoothed = Smooth(rms, 3);

                // 正規化 [0, 1]
                float max = 0f;
                foreach (float v in smoothed)
                    if (v > max) max = v;

                float[] normalized = new float[beatCount];
                if (max > 0f)
                    for (int i = 0; i < beatCount; i++)
                        normalized[i] = smoothed[i] / max;

                beatIntensities = normalized;
                beatDurationSec = beatSec;
                return true;
            }
            catch (Exception ex)
            {
                LogError("TryAnalyzeWav exception: " + ex.Message);
                return false;
            }
        }

        // ── WAV PCM 読み込み ─────────────────────────────────────────
        private bool TryReadWavPcm(string path, out float[] samples, out int sampleRate)
        {
            samples    = null;
            sampleRate = 0;

            using (var br = new BinaryReader(File.OpenRead(path)))
            {
                // RIFF header
                string riff = new string(br.ReadChars(4));
                if (riff != "RIFF") return false;
                br.ReadInt32(); // chunk size
                string wave = new string(br.ReadChars(4));
                if (wave != "WAVE") return false;

                // サブチャンク探索
                short audioFormat = 0, numChannels = 0, bitsPerSample = 0;
                sampleRate = 0;
                int dataSize = 0;
                byte[] dataBytes = null;

                while (br.BaseStream.Position < br.BaseStream.Length - 8)
                {
                    string id   = new string(br.ReadChars(4));
                    int    size = br.ReadInt32();

                    if (id == "fmt ")
                    {
                        audioFormat  = br.ReadInt16();
                        numChannels  = br.ReadInt16();
                        sampleRate   = br.ReadInt32();
                        br.ReadInt32(); // byteRate
                        br.ReadInt16(); // blockAlign
                        bitsPerSample = br.ReadInt16();
                        // fmt に追加バイトがある場合スキップ
                        int extra = size - 16;
                        if (extra > 0) br.ReadBytes(extra);
                    }
                    else if (id == "data")
                    {
                        dataBytes = br.ReadBytes(size);
                        break;
                    }
                    else
                    {
                        br.ReadBytes(size);
                    }
                }

                if (dataBytes == null || sampleRate <= 0) return false;
                // PCM only (audioFormat==1) または IEEE float (audioFormat==3)
                if (audioFormat != 1 && audioFormat != 3) return false;

                int bytesPerSample = bitsPerSample / 8;
                int totalSamples   = dataBytes.Length / (bytesPerSample * numChannels);
                samples = new float[totalSamples];

                for (int i = 0; i < totalSamples; i++)
                {
                    float mix = 0f;
                    for (int ch = 0; ch < numChannels; ch++)
                    {
                        int offset = (i * numChannels + ch) * bytesPerSample;
                        float s;
                        if (audioFormat == 3 && bitsPerSample == 32)
                        {
                            s = BitConverter.ToSingle(dataBytes, offset);
                        }
                        else if (bitsPerSample == 16)
                        {
                            s = BitConverter.ToInt16(dataBytes, offset) / 32768f;
                        }
                        else if (bitsPerSample == 24)
                        {
                            int v = dataBytes[offset]
                                  | (dataBytes[offset + 1] << 8)
                                  | ((sbyte)dataBytes[offset + 2] << 16);
                            s = v / 8388608f;
                        }
                        else if (bitsPerSample == 32 && audioFormat == 1)
                        {
                            s = BitConverter.ToInt32(dataBytes, offset) / 2147483648f;
                        }
                        else
                        {
                            s = 0f;
                        }
                        mix += s;
                    }
                    samples[i] = mix / numChannels;
                }
                return true;
            }
        }

        // ── IIR 1次ローパス ──────────────────────────────────────────
        private static float[] ApplyLowPass(float[] src, int sampleRate, float cutHz)
        {
            float dt  = 1f / sampleRate;
            float rc  = 1f / (2f * (float)Math.PI * cutHz);
            float alpha = dt / (rc + dt);

            float[] dst = new float[src.Length];
            float prev = 0f;
            for (int i = 0; i < src.Length; i++)
            {
                prev   = alpha * src[i] + (1f - alpha) * prev;
                dst[i] = prev;
            }
            return dst;
        }

        // ── 移動平均スムージング ──────────────────────────────────────
        private static float[] Smooth(float[] src, int window)
        {
            float[] dst = new float[src.Length];
            int half = window / 2;
            for (int i = 0; i < src.Length; i++)
            {
                float sum = 0f;
                int   cnt = 0;
                for (int j = i - half; j <= i + half; j++)
                {
                    if (j < 0 || j >= src.Length) continue;
                    sum += src[j];
                    cnt++;
                }
                dst[i] = cnt > 0 ? sum / cnt : 0f;
            }
            return dst;
        }

        // ── ffmpeg による音声抽出 ─────────────────────────────────────
        /// <summary>
        /// ffmpeg.exe を使って動画ファイルから WAV を抽出する。
        /// 成功すれば true。ffmpegPath が存在しない場合は false。
        /// </summary>
        internal bool TryExtractWavFromVideo(string videoPath, string outWavPath, string ffmpegPath)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outWavPath));

                // -vn: 映像なし  -ar 44100: サンプルレート  -ac 1: モノ
                string args = $"-y -i \"{videoPath}\" -vn -ar 44100 -ac 1 -f wav \"{outWavPath}\"";
                LogInfo($"[ffmpeg] 抽出開始: {Path.GetFileName(videoPath)}");

                var psi = new ProcessStartInfo(ffmpegPath, args)
                {
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardError  = true,
                    RedirectStandardOutput = true,
                };

                using (var proc = Process.Start(psi))
                {
                    // ffmpegはstderrにログを出す。読み捨てないとデッドロックする
                    string stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(120_000); // 最大2分

                    if (proc.ExitCode != 0)
                    {
                        LogWarn($"[ffmpeg] 失敗 exit={proc.ExitCode}");
                        // stderrの末尾だけログに残す
                        if (stderr.Length > 300)
                            stderr = "..." + stderr.Substring(stderr.Length - 300);
                        LogWarn("[ffmpeg] " + stderr.Trim());
                        return false;
                    }
                }

                LogInfo($"[ffmpeg] 完了: {outWavPath}");
                return true;
            }
            catch (Exception ex)
            {
                LogError("[ffmpeg] exception: " + ex.Message);
                return false;
            }
        }

        internal string FindFfmpegPath()
        {
            // 1) Bundled ffmpeg beside plugins (preferred for release zip use).
            try
            {
                string pluginDir = _pluginDir ?? string.Empty;
                string parentDir = string.IsNullOrWhiteSpace(pluginDir)
                    ? string.Empty
                    : Path.GetDirectoryName(pluginDir) ?? string.Empty;

                string[] bundledCandidates =
                {
                    Path.Combine(pluginDir, "ffmpeg.exe"),
                    Path.Combine(pluginDir, "_tools", "ffmpeg", "ffmpeg.exe"),
                    Path.Combine(pluginDir, "_tools", "ffmpeg", "bin", "ffmpeg.exe"),
                    Path.Combine(parentDir, "_tools", "ffmpeg", "ffmpeg.exe"),
                    Path.Combine(parentDir, "_tools", "ffmpeg", "bin", "ffmpeg.exe")
                };

                for (int i = 0; i < bundledCandidates.Length; i++)
                {
                    string candidate = bundledCandidates[i];
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                        return candidate;
                }
            }
            catch
            {
                // Fall back to PATH lookup.
            }

            // 2) PATH lookup fallback.
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }
            return null;
        }
    }
}
