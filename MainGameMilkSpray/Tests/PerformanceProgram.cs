using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using MainGameMilkSpray;

internal static class PerformanceProgram
{
    private static int _checks;
    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception(label); _checks++; }
    private static void Work()
    {
        long end = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 10000;
        while (Stopwatch.GetTimestamp() < end) { }
    }
    private static void Finish(MilkPerformanceLog log)
    { log.Dispose(); Check(log.Completion.Wait(5000), "writer drains"); Check(log.Error == null, "writer succeeds: " + log.Error); }
    private static List<string> Csv(string text)
    {
        var fields = new List<string>(); var field = new System.Text.StringBuilder(); bool quote = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (quote && i + 1 < text.Length && text[i + 1] == '"') { field.Append(c); i++; }
                else quote = !quote;
            }
            else if (c == ',' && !quote) { fields.Add(field.ToString()); field.Clear(); }
            else field.Append(c);
        }
        fields.Add(field.ToString()); return fields;
    }
    private static List<Dictionary<string, string>> Read(string path)
    {
        var lines = File.ReadAllLines(path); var header = Csv(lines[0]);
        return lines.Skip(1).Select(line => {
            var fields = Csv(line); Check(fields.Count == header.Count, "CSV columns match");
            return header.Select((h, i) => (h, fields[i])).ToDictionary(x => x.h, x => x.Item2);
        }).ToList();
    }
    private static double Number(Dictionary<string, string> row, string name)
        => double.Parse(row[name], CultureInfo.InvariantCulture);
    public static int Main(string[] args)
    {
        string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "milk-perf-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // Nested timings must remain inclusive; cameras are summed into the matching frame.
            var log = new MilkPerformanceLog(Path.Combine(root, "attribution"));
            log.BeginFrame(10, true); log.SetContext(true, true, false); log.SetParticles(226, 0, 226);
            using (MilkPerformanceLog.Measure(log, MilkStage.Late))
            {
                using (MilkPerformanceLog.Measure(log, MilkStage.Detect))
                    using (MilkPerformanceLog.Measure(log, MilkStage.Clear)) Work();
                using (MilkPerformanceLog.Measure(log, MilkStage.Fire)) Work();
            }
            for (int camera = 0; camera < 2; camera++)
            {
                var sample = log.BeginCamera();
                using (MilkPerformanceLog.Measure(log, MilkStage.Sort)) Work();
                using (MilkPerformanceLog.Measure(log, MilkStage.Geometry)) Work();
                using (MilkPerformanceLog.Measure(log, MilkStage.Upload)) Work();
                log.EndCamera(sample, camera + 1, "Camera,\"" + camera + "\"\nname", 226, 20340, 38420);
            }
            log.BeginFrame(11, true); log.SetContext(false, false, false);
            using (MilkPerformanceLog.Measure(log, MilkStage.Clear)) Work();
            log.BeginFrame(12, true);
            log.BeginFrame(12, false);
            Finish(log);
            var rows = Read(log.LogPath);
            var frame = rows.Single(r => r["kind"] == "frame" && r["frame"] == "10");
            var cameras = rows.Where(r => r["kind"] == "camera").ToList();
            Check(cameras.Count == 2 && cameras.All(r => r["frame"] == "10"), "camera frame attribution");
            Check(frame["camera_calls"] == "2" && frame["complete"] == "1" && frame["frame_gap"] == "1", "complete frame");
            Check(frame["draw_particles"] == "226" && frame["vertices"] == "20340", "geometry counts");
            Check(frame["clear_calls"] == "1" && frame["fire_calls"] == "1", "operation counts");
            Check(Number(frame, "late_ms") >= Number(frame, "detect_ms"), "nested timings inclusive");
            foreach (string key in new[] { "camera_ms", "sort_ms", "geometry_ms", "upload_ms" })
                Check(Math.Abs(Number(frame, key) - cameras.Sum(r => Number(r, key))) < .00021, "camera sum " + key);
            Check(cameras[0]["camera_name"] == "Camera,\"0\" name", "escaped camera name");
            Check(cameras.All(r => r["gc0"] == "" && r["frame_span_ms"] == ""), "unmeasured camera fields blank");
            Check(rows.Single(r => r["kind"] == "frame" && r["frame"] == "11")["enabled"] == "0", "OFF frame retained");
            Check(rows.Last()["complete"] == "0", "disable marks partial frame");

            var quiet = new MilkPerformanceLog(Path.Combine(root, "disabled"));
            quiet.BeginFrame(1, false);
            using (MilkPerformanceLog.Measure(quiet, MilkStage.Late)) Work();
            quiet.EndCamera(quiet.BeginCamera(), 1, "ignored", 0, 0, 0);
            Finish(quiet); Check(Read(quiet.LogPath).Count == 0, "disabled produces no samples");

            var rotation = new MilkPerformanceLog(Path.Combine(root, "rotation"), maxBytes: 1500, fileCount: 3);
            for (int i = 0; i < 100; i++) rotation.BeginFrame(i, true);
            Finish(rotation);
            var files = Directory.GetFiles(Path.GetDirectoryName(rotation.LogPath), "*.csv");
            Check(files.Length == 3, "rotation retains only three files");
            foreach (string f in files) { Check(new FileInfo(f).Length <= 1500, "rotation byte limit"); Check(Read(f).Count > 0, "rotated CSV readable"); }

            var pressure = new MilkPerformanceLog(Path.Combine(root, "pressure"), queueCapacity: 1);
            for (int i = 0; i < 30000; i++) pressure.BeginFrame(i, true);
            Thread.Sleep(50);
            pressure.BeginFrame(30000, true);
            Finish(pressure);
            Check(Read(pressure.LogPath).Any(r => long.Parse(r["dropped_rows"]) > 0), "queue drops visible instead of blocking");

            string blocked = Path.Combine(root, "blocked-directory"); File.WriteAllText(blocked, "file");
            var failed = new MilkPerformanceLog(blocked);
            Check(failed.Completion.Wait(5000) && failed.Error != null, "I/O error observable");
            failed.BeginFrame(1, true);
            using (MilkPerformanceLog.Measure(failed, MilkStage.Late)) Work();
            Check(!failed.Capturing, "I/O error stops sampling"); failed.Dispose();

            var allocation = new MilkPerformanceLog(Path.Combine(root, "allocation"));
            allocation.BeginFrame(1, true);
            for (int i = 0; i < 100; i++) using (MilkPerformanceLog.Measure(allocation, MilkStage.Clear)) { }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100000; i++) using (MilkPerformanceLog.Measure(allocation, MilkStage.Clear)) { }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            Check(bytes == 0, "timing scopes allocate zero managed bytes on caller: " + bytes);
            Finish(allocation);

            // Instrumenting ghost updates must not change simulation state or random draws.
            var a = new LiquidSimulation(41729); var b = new LiquidSimulation(41729);
            var pose = new EmitterPose(default, V3.Forward, new V3(1, 0, 0));
            var spec = new LiquidSpec { RandomCount = true, CountMin = 73, CountMax = 156, Duration = .182f,
                Speed = 1.5f, SizeMin = .0005f, SizeMax = .002f, StretchRatio = 10, DropLife = .7f,
                Gravity = 6.867f, Afterimages = true, GhostCount = 4, GhostLife = .1f,
                GhostOpacity = .3f, GhostDistance = .15f };
            var simulationLog = new MilkPerformanceLog(Path.Combine(root, "simulation"));
            a.Fire(spec, pose, pose, true, true); b.Fire(spec, pose, pose, true, true);
            for (int i = 0; i < 60; i++)
            {
                simulationLog.BeginFrame(i, true);
                a.Advance(1.0 / 60, pose, pose);
                b.Advance(1.0 / 60, pose, pose, simulationLog);
                for (int j = 0; j < a.Drops.Length; j++)
                    Check(a.Drops[j].Active == b.Drops[j].Active && a.Drops[j].Id == b.Drops[j].Id
                        && (a.Drops[j].Position(a.Now) - b.Drops[j].Position(b.Now)).Length == 0, "drop unchanged");
                for (int j = 0; j < a.Ghosts.Length; j++)
                    Check(a.Ghosts[j].Active == b.Ghosts[j].Active && a.Ghosts[j].Born == b.Ghosts[j].Born, "ghost unchanged");
            }
            Finish(simulationLog);
            Check(Read(simulationLog.LogPath).Any(r => Number(r, "ghost_update_ms") > 0), "ghost update measured");

            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                string text = MilkPerformanceLog.Format(new MilkPerformanceLog.Row { Span = Stopwatch.Frequency, Utc = DateTime.UtcNow }, "test");
                Check(Csv(text)[5] == "1000.0000", "invariant numeric culture");
            }
            finally { CultureInfo.CurrentCulture = culture; }
            Console.WriteLine("PASS performance instrumentation checks=" + _checks + " output=" + root);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
