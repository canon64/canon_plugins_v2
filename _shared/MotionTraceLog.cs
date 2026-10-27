using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CanonPlugins.Shared
{
    // Opt-in, bounded diagnostic output. No file or string work at sample sites when off.
    internal sealed class MotionTraceLog : IDisposable
    {
        private const long Limit = 8 * 1024 * 1024;
        private readonly string _path;
        private readonly Action<string> _warning;
        private readonly string _session = System.Guid.NewGuid().ToString("N");
        private StreamWriter _writer;
        private long _bytes;
        internal bool Enabled => _writer != null;

        internal MotionTraceLog(string directory, Assembly assembly, Action<string> warning)
        {
            _path = Path.Combine(directory, "MotionDiagnostics.log");
            _warning = warning;
            if (!File.Exists(Path.Combine(directory, "MotionDiagnostics.enabled"))) return;
            try
            {
                _bytes = File.Exists(_path) ? new FileInfo(_path).Length : 0;
                Open();
                string hash;
                using (var sha = SHA256.Create())
                using (var input = File.OpenRead(assembly.Location))
                    hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                Write("START", 0, 0f, "pid=" + Process.GetCurrentProcess().Id
                    + " assembly=" + assembly.Location + " sha256=" + hash
                    + " mvid=" + assembly.ManifestModule.ModuleVersionId);
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void Open()
        {
            _writer = new StreamWriter(new FileStream(_path, FileMode.Append,
                FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
        }

        internal static string DescribeAssembly(Assembly assembly)
        {
            try
            {
                string hash;
                using (var sha = SHA256.Create())
                using (var input = File.OpenRead(assembly.Location))
                    hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                return "name=" + assembly.GetName().Name + " path=" + assembly.Location
                    + " sha256=" + hash + " mvid=" + assembly.ManifestModule.ModuleVersionId;
            }
            catch (Exception ex) { return "name=" + assembly.FullName + " identityError=" + ex.Message; }
        }

        internal void Write(string stage, int frame, float realtime, string detail)
        {
            if (!Enabled) return;
            try
            {
                if (_bytes >= Limit)
                {
                    _writer.Dispose();
                    _writer = null;
                    string previous = _path + ".previous";
                    if (File.Exists(previous)) File.Delete(previous);
                    File.Move(_path, previous);
                    _bytes = 0;
                    Open();
                }
                string line = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)
                    + " session=" + _session + " frame=" + frame + " rt="
                    + realtime.ToString("F3", CultureInfo.InvariantCulture) + " " + stage
                    + " " + (detail ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
                _writer.WriteLine(line);
                _bytes += Encoding.UTF8.GetByteCount(line) + 2;
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void Fail(Exception ex)
        {
            Dispose();
            _warning?.Invoke("MotionDiagnostics disabled: " + ex.Message);
        }

        public void Dispose()
        {
            try { _writer?.Dispose(); } catch { }
            _writer = null;
        }
    }
}
