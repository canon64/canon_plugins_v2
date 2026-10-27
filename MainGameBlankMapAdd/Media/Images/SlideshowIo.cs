using System;
using System.IO;

namespace MainGameBlankMapAdd.Media.Images
{
    // This instance is accessed by the serial scan worker only.
    internal sealed class SlideshowFolderScanner
    {
        private readonly SlideshowDateFolder _dateFolder = new SlideshowDateFolder();
        internal SlideshowCatalogSnapshot Scan(string root, bool autoDateFolder, DateTime today, double now,
            string[] extensions, string sort, bool ascending)
        {
            string folder = autoDateFolder && !string.IsNullOrWhiteSpace(root)
                ? _dateFolder.Resolve(root, today, now, Directory.Exists, FindInitialFolder)
                : root;
            if (string.IsNullOrWhiteSpace(folder)) folder = root;
            return SlideshowFileCatalog.Capture(folder, extensions, sort, ascending);
        }

        private static string FindInitialFolder(string root)
        {
            if (!Directory.Exists(root)) return string.Empty;
            string latest = null;
            foreach (string dir in Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly))
            {
                if (latest == null || string.Compare(Path.GetFileName(dir), Path.GetFileName(latest), StringComparison.OrdinalIgnoreCase) > 0)
                    latest = dir;
            }
            return latest ?? string.Empty;
        }
    }

    internal sealed class SlideshowImageBytes
    {
        internal string Path;
        internal DateTime WriteTime;
        internal byte[] Bytes;

        internal static SlideshowImageBytes Read(string path)
        {
            DateTime before = File.GetLastWriteTimeUtc(path);
            byte[] bytes = File.ReadAllBytes(path);
            DateTime after = File.GetLastWriteTimeUtc(path);
            if (before != after) throw new IOException("Image changed while reading; retry next scan.");
            return new SlideshowImageBytes { Path = path, WriteTime = after, Bytes = bytes };
        }
    }
}
