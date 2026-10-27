using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using MainGameBlankMapAdd.Media.Images;

namespace MainGameBlankMapAdd.Characterization
{
    internal static class SlideshowIoTests
    {
        internal static void Run()
        {
            int caller = Thread.CurrentThread.ManagedThreadId;
            var job = new SlideshowBackgroundJob<int>();
            using (var started = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                job.Request(() => { started.Set(); release.Wait(3000); return Thread.CurrentThread.ManagedThreadId; });
                Assert(started.Wait(2000), "worker started");
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < 10000; i++) Assert(!job.TryTake(out _, out _), "poll never waits for blocked IO");
                watch.Stop();
                Assert(watch.ElapsedMilliseconds < 1000, "poll remains responsive");
                release.Set();
                Assert(Take(job, out Exception error) != caller && error == null, "IO runs off caller thread");
                Console.WriteLine("PASS slideshow polling: 10000 polls while IO blocked in " + watch.Elapsed.TotalMilliseconds.ToString("F2") + " ms");
            }

            using (var started = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                job.Request(() => { started.Set(); release.Wait(3000); return 1; });
                Assert(started.Wait(2000), "stale worker started");
                int skipped = 0;
                job.Request(() => { skipped++; return 2; });
                job.Request(() => 3);
                release.Set();
                Assert(Take(job, out _) == 3 && skipped == 0, "latest request wins without queuing obsolete reads");
            }
            using (var started = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                job.Request(() => { started.Set(); release.Wait(3000); return 4; });
                Assert(started.Wait(2000), "reset worker started");
                job.Reset(); // Room left / source changed while a read was running.
                release.Set();
                var watch = Stopwatch.StartNew();
                while (job.HasWork && watch.ElapsedMilliseconds < 3000)
                {
                    Assert(!job.TryTake(out _, out _), "reset never publishes stale results");
                    Thread.Sleep(1);
                }
                Assert(!job.HasWork, "reset drains worker");
            }
            job.Request(() => throw new IOException("network unavailable"));
            Take(job, out Exception failure);
            Assert(failure is IOException, "IO failure returned without crashing caller");
            job.Request(() => 5);
            Assert(Take(job, out _) == 5, "worker recovers after error");

            string root = Path.Combine(Path.GetTempPath(), "SlideshowIoTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string a = Path.Combine(root, "a.png"), b = Path.Combine(root, "b.jpg");
                File.WriteAllBytes(a, new byte[] { 1, 2, 3 });
                File.WriteAllBytes(b, new byte[] { 4, 5, 6 });
                File.WriteAllText(Path.Combine(root, "ignore.txt"), "ignored");
                DateTime old = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(a, old);
                File.SetLastWriteTimeUtc(b, old.AddHours(1));
                var catalog = SlideshowFileCatalog.Capture(root, new[] { ".png", ".jpg" }, "Date", false);
                Assert(catalog.Available && catalog.Files.Length == 2 && catalog.Files[0] == b, "date sorting and filtering");
                Assert(catalog.WriteTime(a) == old, "snapshot caches dates");
                File.SetLastWriteTimeUtc(a, old.AddHours(2));
                Assert(catalog.WriteTime(a) == old, "lookup never touches changed filesystem");
                var next = SlideshowFileCatalog.Capture(root, new[] { ".png", ".jpg" }, "Date", false);
                Assert(next.Files[0] == a && next.WriteTime(a) > catalog.WriteTime(a), "next scan detects overwrite");
                var bytes = SlideshowImageBytes.Read(a);
                Assert(bytes.Bytes.SequenceEqual(new byte[] { 1, 2, 3 }) && bytes.WriteTime == next.WriteTime(a), "image bytes and version agree");
                File.Delete(a);
                Assert(catalog.WriteTime(a) == old, "deleted file still has immutable snapshot metadata");
                Assert(!SlideshowFileCatalog.Capture(Path.Combine(root, "missing"), new[] { ".png" }, "Date", false).Available, "missing directory handled");

                var scanner = new SlideshowFolderScanner();
                DateTime day = new DateTime(2026, 10, 24);
                string previous = Path.Combine(root, "2026-10-23");
                Directory.CreateDirectory(previous);
                Assert(scanner.Scan(root, true, day, 0, new[] { ".png" }, "Date", false).Folder == previous, "date-folder fallback");
                string today = Path.Combine(root, "2026-10-24");
                Directory.CreateDirectory(today);
                Assert(scanner.Scan(root, true, day, 5, new[] { ".png" }, "Date", false).Folder == today, "delayed today's folder detected");
                Assert(scanner.Scan(root, false, day, 6, new[] { ".jpg" }, "Name", true).Files.Length == 1, "auto folder off uses explicit root");
                Console.WriteLine("PASS slideshow IO: background thread, latest wins, room reset, errors/recovery, metadata, image read, date folders");
            }
            finally { Directory.Delete(root, true); }
        }

        private static int Take(SlideshowBackgroundJob<int> job, out Exception error)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < 3000)
            {
                if (job.TryTake(out int result, out error)) return result;
                Thread.Sleep(1);
            }
            throw new Exception("Background job timed out");
        }
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new Exception("Slideshow IO: " + message);
        }
    }
}
