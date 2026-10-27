using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private string _monoVideoCacheDir;
        private readonly Dictionary<string, string> _monoVideoPathCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _monoVideoCacheLock = new object();

        private void EnsureMonoVideoCacheDir()
        {
            if (!string.IsNullOrWhiteSpace(_monoVideoCacheDir))
                return;

            _monoVideoCacheDir = Path.Combine(_pluginDir ?? string.Empty, "_mono_cache");
            if (!string.IsNullOrWhiteSpace(_monoVideoCacheDir))
                Directory.CreateDirectory(_monoVideoCacheDir);
        }

        private string ResolveMonoVideoPathIfNeeded(string sourcePath)
        {
            // Requested behavior: do not generate converted mono MP4 cache files.
            // Keep existing call sites and simply play source as-is.
            return sourcePath;
        }

        private static bool TryConvertMp4ToMono(string inputPath, string outputPath, out string error)
        {
            error = string.Empty;

            string ffmpegArgs =
                "-y -hide_banner -loglevel error " +
                "-i " + QuoteProcessArg(inputPath) + " " +
                "-map 0:v:0 -map 0:a:0? " +
                "-c:v copy -c:a aac -ac 1 -b:a 192k " +
                QuoteProcessArg(outputPath);

            try
            {
                using (var proc = new Process())
                {
                    proc.StartInfo = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = ffmpegArgs,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    proc.Start();
                    string stdErr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    if (proc.ExitCode != 0)
                    {
                        error = string.IsNullOrWhiteSpace(stdErr)
                            ? $"ffmpeg exit={proc.ExitCode}"
                            : stdErr.Trim();
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            try
            {
                var fi = new FileInfo(outputPath);
                if (!fi.Exists || fi.Length <= 0)
                {
                    error = "output file missing or empty";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            return true;
        }

        private static string QuoteProcessArg(string value)
        {
            if (value == null)
                return "\"\"";
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static void TryDeleteFileQuiet(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Ignore cleanup failures.
            }
        }

        private static string ComputeSha1Hex(string value)
        {
            using (var sha1 = SHA1.Create())
            {
                byte[] input = Encoding.UTF8.GetBytes(value ?? string.Empty);
                byte[] hash = sha1.ComputeHash(input);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static string SanitizePathPart(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "video";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool bad = false;
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (c != invalid[j])
                        continue;
                    bad = true;
                    break;
                }

                sb.Append(bad ? '_' : c);
            }

            return sb.ToString().Trim();
        }
    }
}
