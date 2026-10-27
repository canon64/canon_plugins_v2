using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MainGameBlankMapAdd.Media.Playlist
{
    internal sealed class FolderPlaylist
    {
        private readonly HashSet<string> _extensions;
        private string _lastScannedFolder;

        internal FolderPlaylist(IEnumerable<string> extensions)
        {
            _extensions = new HashSet<string>(extensions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            Files = Array.Empty<string>();
            Index = -1;
        }

        internal string[] Files { get; private set; }
        internal int Index { get; set; }

        internal bool ScanIfChanged(string folder, string sortMode, bool ascending, out string error)
        {
            error = string.Empty;
            if (string.Equals(folder, _lastScannedFolder, StringComparison.OrdinalIgnoreCase)) return false;
            return Scan(folder, sortMode, ascending, out error);
        }

        internal bool ForceScan(string folder, string sortMode, bool ascending, out string error)
        {
            _lastScannedFolder = null;
            return Scan(folder, sortMode, ascending, out error);
        }

        internal void Invalidate()
        {
            _lastScannedFolder = null;
        }

        internal bool TrySelect(int index, out string path)
        {
            path = null;
            if (index < 0 || index >= Files.Length) return false;
            Index = index;
            path = Files[index];
            return true;
        }

        private bool Scan(string folder, string sortMode, bool ascending, out string error)
        {
            error = string.Empty;
            _lastScannedFolder = folder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                Files = Array.Empty<string>();
                Index = -1;
                error = "folder not found";
                return true;
            }

            try
            {
                IEnumerable<string> files = Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly)
                    .Where(path => _extensions.Contains(Path.GetExtension(path)));
                string normalizedSort = string.IsNullOrWhiteSpace(sortMode) ? "Name" : sortMode.Trim();
                if (string.Equals(normalizedSort, "Random", StringComparison.OrdinalIgnoreCase))
                {
                    var list = files.ToList();
                    CollectionShuffle.Shuffle(list);
                    Files = list.ToArray();
                }
                else if (string.Equals(normalizedSort, "Date", StringComparison.OrdinalIgnoreCase))
                {
                    Files = (ascending
                            ? files.OrderBy(File.GetLastWriteTime)
                            : files.OrderByDescending(File.GetLastWriteTime))
                        .ToArray();
                }
                else
                {
                    Files = (ascending
                            ? files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                            : files.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
                        .ToArray();
                }
                Index = Files.Length > 0 ? 0 : -1;
                return true;
            }
            catch (Exception ex)
            {
                Files = Array.Empty<string>();
                Index = -1;
                error = ex.Message;
                return true;
            }
        }

    }
}
