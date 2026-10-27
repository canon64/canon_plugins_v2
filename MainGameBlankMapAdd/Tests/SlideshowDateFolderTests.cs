using System;
using System.IO;
using MainGameBlankMapAdd.Media.Images;

namespace MainGameBlankMapAdd.Characterization
{
    internal static class SlideshowDateFolderTests
    {
        internal static void Run()
        {
            var resolver = new SlideshowDateFolder();
            var day = new DateTime(2026, 10, 23);
            string root = @"Z:\images";
            string today = Path.Combine(root, "2026-10-23");
            string tomorrow = Path.Combine(root, "2026-10-24");
            int checks = 0, scans = 0;
            bool available = true;
            Func<string, bool> exists = p => { checks++; return available; };
            Func<string, string> fallback = p => { scans++; return Path.Combine(p, "2026-10-22"); };
            Assert(resolver.Resolve(root, day, 0, exists, fallback) == today, "select today");
            for (int i = 1; i <= 10000; i++) resolver.Resolve(root, day, i, exists, fallback);
            Assert(checks == 1 && scans == 0, "no searching after today found");

            available = false;
            Assert(resolver.Resolve(root, day.AddDays(1), 10001, exists, fallback) == today, "keep previous while waiting");
            int count = checks;
            resolver.Resolve(root, day.AddDays(1), 10005.99, exists, fallback);
            Assert(checks == count, "wait full five seconds");
            resolver.Resolve(root, day.AddDays(1), 10006, exists, fallback);
            Assert(checks == count + 1, "retry at five seconds");
            available = true;
            Assert(resolver.Resolve(root, day.AddDays(1), 10011, exists, fallback) == tomorrow, "find delayed folder");
            count = checks;
            resolver.Resolve(root, day.AddDays(1), 90000, exists, fallback);
            Assert(checks == count && scans == 0, "stop again after found");
            Assert(resolver.Resolve(root, day.AddDays(2), 90001, exists, fallback) == Path.Combine(root, "2026-10-25"), "immediate date rollover selection");

            available = false;
            string other = @"Z:\other";
            Assert(resolver.Resolve(other, day, 90002, exists, fallback) == Path.Combine(other, "2026-10-22"), "new root initial fallback");
            resolver.Resolve(other, day, 90007, exists, fallback);
            Assert(scans == 1, "fallback enumeration happens only on root selection");
            available = true;
            Assert(resolver.Resolve(other, day, 90012, exists, fallback) == Path.Combine(other, "2026-10-23"), "initially missing today recovered");
            Console.WriteLine("PASS date-folder: stop-on-found, rollover, five-second retry, root change");
        }

        private static void Assert(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Date folder: " + message);
        }
    }
}
