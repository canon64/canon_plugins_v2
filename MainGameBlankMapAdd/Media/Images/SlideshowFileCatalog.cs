using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MainGameBlankMapAdd.Media.Images
{
    internal sealed class SlideshowCatalogSnapshot
    {
        internal string Folder = string.Empty;
        internal bool Available;
        internal string Error = string.Empty;
        internal string[] Files = Array.Empty<string>();
        internal readonly Dictionary<string, DateTime> WriteTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        internal DateTime WriteTime(string path) => path != null && WriteTimes.TryGetValue(path, out DateTime time) ? time : DateTime.MinValue;
    }

    internal static class SlideshowFileCatalog
    {
        internal static string[] Enumerate(
            string folder,
            IEnumerable<string> supportedExtensions,
            string sortMode,
            bool ascending,
            out string error)
        {
            SlideshowCatalogSnapshot snapshot = Capture(folder, supportedExtensions, sortMode, ascending);
            error = snapshot.Error;
            return snapshot.Files;
        }

        internal static SlideshowCatalogSnapshot Capture(string folder, IEnumerable<string> supportedExtensions, string sortMode, bool ascending)
        {
            var snapshot = new SlideshowCatalogSnapshot { Folder = folder ?? string.Empty };
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return snapshot;
                snapshot.Available = true;
                var extensions = new HashSet<string>(supportedExtensions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                var files = new List<string>();
                // FileInfo caches metadata supplied by directory enumeration. Do not issue
                // a separate network stat for every sort key and every latest-image lookup.
                foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
                {
                    if (!extensions.Contains(file.Extension)) continue;
                    files.Add(file.FullName);
                    snapshot.WriteTimes[file.FullName] = file.LastWriteTimeUtc;
                }

                string normalizedSort = NormalizeSortMode(sortMode);
                if (normalizedSort == "Random")
                {
                    CollectionShuffle.Shuffle(files);
                    snapshot.Files = files.ToArray();
                    return snapshot;
                }

                IOrderedEnumerable<string> ordered = normalizedSort == "Date"
                    ? (ascending
                        ? files.OrderBy(snapshot.WriteTime)
                        : files.OrderByDescending(snapshot.WriteTime))
                    : (ascending
                        ? files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                        : files.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase));
                snapshot.Files = ordered.ToArray();
            }
            catch (Exception ex)
            {
                snapshot.Error = ex.Message;
                snapshot.Available = false;
            }
            return snapshot;
        }

        internal static string NormalizeSortMode(string value)
        {
            if (string.Equals(value, "Name", StringComparison.Ordinal)) return "Name";
            if (string.Equals(value, "Random", StringComparison.Ordinal)) return "Random";
            return "Date";
        }

    }
}
