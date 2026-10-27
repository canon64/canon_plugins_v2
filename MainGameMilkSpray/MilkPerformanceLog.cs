using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MainGameMilkSpray
{
    internal enum MilkStage { Late, Detect, Init, Simulation, Ghosts, Fire, Prepare, Clear, Sort, Geometry, Upload }

    // The Unity thread only copies value types into a bounded queue. Formatting, flushing
    // and rotation all run on the writer thread. Durations are elapsed CPU-side time, not GPU time.
    internal sealed class MilkPerformanceLog : IDisposable
    {
        internal struct Times
        {
            internal long Late, Detect, Init, Simulation, Ghosts, Fire, Prepare, Clear, Camera, Sort, Geometry, Upload;
            internal int ClearCalls, FireCalls;
            internal void Add(MilkStage stage, long ticks)
            {
                switch (stage)
                {
                    case MilkStage.Late: Late += ticks; break;
                    case MilkStage.Detect: Detect += ticks; break;
                    case MilkStage.Init: Init += ticks; break;
                    case MilkStage.Simulation: Simulation += ticks; break;
                    case MilkStage.Ghosts: Ghosts += ticks; break;
                    case MilkStage.Fire: Fire += ticks; FireCalls++; break;
                    case MilkStage.Prepare: Prepare += ticks; break;
                    case MilkStage.Clear: Clear += ticks; ClearCalls++; break;
                    case MilkStage.Sort: Sort += ticks; break;
                    case MilkStage.Geometry: Geometry += ticks; break;
                    case MilkStage.Upload: Upload += ticks; break;
                }
            }
        }

        internal struct Row
        {
            internal DateTime Utc;
            internal int Frame, FrameGap, Drops, Ghosts, Particles, CameraId, CameraCalls, Vertices, Triangles;
            internal int Gc0, Gc1, Gc2;
            internal bool CameraRow, Complete, Enabled, ContextValid, Paused;
            internal string CameraName;
            internal long Span, Dropped;
            internal Times Times;
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly MilkPerformanceLog _owner;
            private readonly MilkStage _stage;
            private readonly long _start;
            internal Scope(MilkPerformanceLog owner, MilkStage stage)
            { _owner = owner; _stage = stage; _start = Stopwatch.GetTimestamp(); }
            public void Dispose() { _owner?.Record(_stage, Stopwatch.GetTimestamp() - _start); }
        }

        internal readonly struct CameraSample
        {
            internal readonly bool Active;
            internal readonly long Start;
            internal readonly Times Before;
            internal CameraSample(Times before)
            { Active = true; Before = before; Start = Stopwatch.GetTimestamp(); }
        }

        internal const string Header = "schema,session,utc,kind,frame,frame_span_ms,frame_gap,complete,enabled,context_valid,paused,drops,ghosts,draw_particles,camera_id,camera_name,camera_calls,vertices,triangles,clear_calls,fire_calls,gc0,gc1,gc2,dropped_rows,late_ms,detect_ms,init_ms,simulation_ms,ghost_update_ms,fire_ms,prepare_ms,clear_ms,camera_ms,sort_ms,geometry_ms,upload_ms";
        private readonly BlockingCollection<Row> _queue;
        private readonly string _session;
        private readonly long _maxBytes;
        private readonly int _fileCount;
        private Row _frame;
        private long _frameStart, _dropped;
        private int _gc0, _gc1, _gc2;
        private bool _requested, _active, _disposed;
        private string _error;
        internal string LogPath { get; }
        internal string Error => Volatile.Read(ref _error);
        internal Task Completion { get; }
        internal bool Capturing => _active && !_disposed && Error == null;

        internal MilkPerformanceLog(string directory, int queueCapacity = 4096, long maxBytes = 16 * 1024 * 1024, int fileCount = 3)
        {
            LogPath = Path.Combine(directory, "milk-performance.csv");
            _session = DateTime.UtcNow.ToString("yyyyMMddTHHmmss.fffffffZ", CultureInfo.InvariantCulture)
                + "-" + Process.GetCurrentProcess().Id;
            _maxBytes = maxBytes;
            _fileCount = Math.Max(1, fileCount);
            _queue = new BlockingCollection<Row>(queueCapacity);
            Completion = Task.Factory.StartNew(WriteLoop, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        internal static Scope Measure(MilkPerformanceLog owner, MilkStage stage)
            => owner != null && owner.Capturing ? new Scope(owner, stage) : default;

        internal void BeginFrame(int frame, bool enabled)
        {
            _requested = enabled;
            TouchFrame(frame);
        }

        // Called from Update AND camera callbacks: a manual Camera.Render before LateUpdate
        // still belongs to the correct Time.frameCount, rather than to the previous frame.
        internal void TouchFrame(int frame)
        {
            if (_disposed) return;
            if (!_requested || Error != null)
            {
                if (_active) FinishFrame(Stopwatch.GetTimestamp(), frame, false);
                return;
            }
            if (_active && _frame.Frame == frame) return;
            long now = Stopwatch.GetTimestamp();
            if (_active) FinishFrame(now, frame, true);
            _frame = new Row { Frame = frame, Utc = DateTime.UtcNow };
            _frameStart = now;
            _gc0 = GC.CollectionCount(0); _gc1 = GC.CollectionCount(1); _gc2 = GC.CollectionCount(2);
            _active = true;
        }

        internal void SetContext(bool enabled, bool valid, bool paused)
        { _frame.Enabled = enabled; _frame.ContextValid = valid; _frame.Paused = paused; }
        internal void SetParticles(int drops, int ghosts, int particles)
        { _frame.Drops = drops; _frame.Ghosts = ghosts; _frame.Particles = particles; }

        private void Record(MilkStage stage, long ticks)
        { if (Capturing) _frame.Times.Add(stage, ticks); }

        internal CameraSample BeginCamera() => Capturing ? new CameraSample(_frame.Times) : default;

        internal void EndCamera(CameraSample sample, int id, string name, int particles, int vertices, int triangles)
        {
            if (!sample.Active || !Capturing) return;
            long elapsed = Stopwatch.GetTimestamp() - sample.Start;
            Times after = _frame.Times;
            _frame.Times.Camera += elapsed;
            _frame.CameraCalls++;
            _frame.Vertices = Math.Max(_frame.Vertices, vertices);
            _frame.Triangles = Math.Max(_frame.Triangles, triangles);
            Row row = _frame;
            row.Utc = DateTime.UtcNow;
            row.CameraRow = true; row.CameraId = id; row.CameraName = name; row.CameraCalls = 1;
            row.Particles = particles; row.Vertices = vertices; row.Triangles = triangles;
            row.Times = new Times { Camera = elapsed, Sort = after.Sort - sample.Before.Sort,
                Geometry = after.Geometry - sample.Before.Geometry, Upload = after.Upload - sample.Before.Upload };
            Enqueue(row);
        }

        private void FinishFrame(long now, int nextFrame, bool complete)
        {
            _frame.Span = now - _frameStart;
            _frame.FrameGap = nextFrame - _frame.Frame;
            _frame.Complete = complete;
            _frame.Gc0 = GC.CollectionCount(0) - _gc0;
            _frame.Gc1 = GC.CollectionCount(1) - _gc1;
            _frame.Gc2 = GC.CollectionCount(2) - _gc2;
            Enqueue(_frame);
            _active = false;
        }

        private void Enqueue(Row row)
        {
            if (_disposed || Error != null) return;
            row.Dropped = Interlocked.Read(ref _dropped);
            // A slow disk must drop diagnostic rows, never stall the Unity thread.
            if (!_queue.TryAdd(row)) Interlocked.Increment(ref _dropped);
        }

        private void WriteLoop()
        {
            StreamWriter writer = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                long bytes;
                writer = OpenWriter(out bytes);
                long flushed = Stopwatch.GetTimestamp();
                while (!_queue.IsCompleted)
                {
                    if (_queue.TryTake(out Row row, 250))
                    {
                        string text = Format(row, _session);
                        int size = Encoding.UTF8.GetByteCount(text) + 2;
                        if (bytes + size > _maxBytes && bytes > Header.Length + 2)
                        {
                            writer.Dispose(); writer = null;
                            Rotate();
                            writer = OpenWriter(out bytes);
                        }
                        writer.WriteLine(text);
                        bytes += size;
                    }
                    long now = Stopwatch.GetTimestamp();
                    if (now - flushed >= Stopwatch.Frequency)
                    { writer.Flush(); flushed = now; }
                }
            }
            catch (Exception ex) { Volatile.Write(ref _error, ex.GetType().Name + ": " + ex.Message); }
            finally
            {
                try { writer?.Dispose(); }
                catch (Exception ex) { Volatile.Write(ref _error, ex.GetType().Name + ": " + ex.Message); }
            }
        }

        private StreamWriter OpenWriter(out long bytes)
        {
            var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 65536);
            bytes = stream.Length;
            var writer = new StreamWriter(stream, new UTF8Encoding(false), 65536) { NewLine = "\r\n" };
            if (bytes == 0) { writer.WriteLine(Header); bytes = Encoding.UTF8.GetByteCount(Header) + 2; }
            writer.Flush();
            return writer;
        }

        private string Rotated(int index) => index == 0 ? LogPath : Path.Combine(Path.GetDirectoryName(LogPath), "milk-performance." + index + ".csv");
        private void Rotate()
        {
            string oldest = Rotated(_fileCount - 1);
            if (File.Exists(oldest)) File.Delete(oldest);
            for (int i = _fileCount - 2; i >= 0; i--)
                if (File.Exists(Rotated(i))) File.Move(Rotated(i), Rotated(i + 1));
        }

        internal static string Format(Row r, string session)
        {
            var s = new StringBuilder(480);
            s.Append("1,").Append(session).Append(',').Append(r.Utc.ToString("O", CultureInfo.InvariantCulture))
                .Append(',').Append(r.CameraRow ? "camera" : "frame").Append(',').Append(r.Frame).Append(',');
            if (!r.CameraRow) s.Append(Ms(r.Span));
            s.Append(','); if (!r.CameraRow) s.Append(r.FrameGap);
            s.Append(','); if (!r.CameraRow) s.Append(r.Complete ? 1 : 0);
            s.Append(',').Append(r.Enabled ? 1 : 0).Append(',').Append(r.ContextValid ? 1 : 0).Append(',').Append(r.Paused ? 1 : 0)
                .Append(',').Append(r.Drops).Append(',').Append(r.Ghosts).Append(',').Append(r.Particles).Append(',');
            if (r.CameraRow) s.Append(r.CameraId);
            s.Append(',').Append('"').Append((r.CameraName ?? "").Replace("\"", "\"\"").Replace('\r', ' ').Replace('\n', ' ')).Append('"')
                .Append(',').Append(r.CameraCalls).Append(',').Append(r.Vertices).Append(',').Append(r.Triangles)
                .Append(',').Append(r.Times.ClearCalls).Append(',').Append(r.Times.FireCalls).Append(',');
            if (!r.CameraRow) s.Append(r.Gc0);
            s.Append(','); if (!r.CameraRow) s.Append(r.Gc1);
            s.Append(','); if (!r.CameraRow) s.Append(r.Gc2);
            s.Append(',').Append(r.Dropped);
            Times t = r.Times;
            s.Append(',').Append(Ms(t.Late)).Append(',').Append(Ms(t.Detect)).Append(',').Append(Ms(t.Init))
                .Append(',').Append(Ms(t.Simulation)).Append(',').Append(Ms(t.Ghosts)).Append(',').Append(Ms(t.Fire))
                .Append(',').Append(Ms(t.Prepare)).Append(',').Append(Ms(t.Clear)).Append(',').Append(Ms(t.Camera))
                .Append(',').Append(Ms(t.Sort)).Append(',').Append(Ms(t.Geometry)).Append(',').Append(Ms(t.Upload));
            return s.ToString();
        }
        private static string Ms(long ticks) => (ticks * (1000.0 / Stopwatch.Frequency)).ToString("F4", CultureInfo.InvariantCulture);

        public void Dispose()
        {
            if (_disposed) return;
            if (_active) FinishFrame(Stopwatch.GetTimestamp(), _frame.Frame, false);
            _disposed = true;
            _queue.CompleteAdding(); // No Join/Wait on the Unity thread.
        }
    }
}
