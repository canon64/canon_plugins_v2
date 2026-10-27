using System;
using System.Collections.Generic;
using System.IO;

namespace MainGameVMDPlay
{
    internal sealed class VmdFileEntry
    {
        public string AbsPath;
        public string RelPath;
        public string DisplayName;
        public long   SizeKB;
        public DateTime MTime;
    }

    public sealed partial class Plugin
    {
        private List<VmdFileEntry> _vmdFiles = new List<VmdFileEntry>();

        private string EffectiveVmdDir
        {
            get
            {
                string s = _settings?.VmdDirectory;
                if (string.IsNullOrEmpty(s)) return Path.Combine(_pluginDir, "vmd");
                // ユーザーがフォルダではなくファイルパスを入れている場合は親フォルダを使う
                try
                {
                    if (File.Exists(s))
                    {
                        string parent = Path.GetDirectoryName(s);
                        if (!string.IsNullOrEmpty(parent)) return parent;
                    }
                }
                catch { }
                return s;
            }
        }

        private void EnsureVmdDirectory()
        {
            string dir = EffectiveVmdDir;
            if (!Directory.Exists(dir))
            {
                try
                {
                    Directory.CreateDirectory(dir);
                    string readme = Path.Combine(dir, "README.txt");
                    File.WriteAllText(readme,
                        "このフォルダに .vmd ファイルを置いてください。\r\n" +
                        "サブフォルダも対応しています。\r\n" +
                        "MainGameVMDPlay プラグインが自動検出します。\r\n",
                        new System.Text.UTF8Encoding(false));
                    LogInfo($"[Files] vmd dir created path={dir}");
                }
                catch (Exception ex) { LogError($"[Files] vmd dir create FAILED ex={ex.Message}"); }
            }
        }

        private void FilesRescan()
        {
            _vmdFiles.Clear();
            string dir = EffectiveVmdDir;

            if (!Directory.Exists(dir))
            {
                LogWarn($"[Files] scan dir not found dir={dir}");
                return;
            }

            try
            {
                foreach (string absPath in Directory.GetFiles(dir, "*.vmd", SearchOption.AllDirectories))
                {
                    var fi = new FileInfo(absPath);
                    string relPath = absPath.Length > dir.Length
                        ? absPath.Substring(dir.Length).TrimStart(Path.DirectorySeparatorChar)
                        : absPath;
                    _vmdFiles.Add(new VmdFileEntry
                    {
                        AbsPath     = absPath,
                        RelPath     = relPath,
                        DisplayName = relPath,
                        SizeKB      = fi.Length / 1024,
                        MTime       = fi.LastWriteTime,
                    });
                }
                LogInfo($"[Files] scan dir={dir} count={_vmdFiles.Count}");
            }
            catch (Exception ex) { LogError($"[Files] scan FAILED ex={ex.GetType().Name}: {ex.Message}"); }
        }
    }
}
