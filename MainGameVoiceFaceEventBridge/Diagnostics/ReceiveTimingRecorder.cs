using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace MainGameVoiceFaceEventBridge.Diagnostics
{
    internal enum ReceiveTimingStage { Guard, Astral, Text }

    // All recording happens on Unity's main thread. Only detached batches reach the writer.
    internal sealed class ReceiveTimingRecorder
    {
        internal struct Sample
        {
            public bool IsCommand;
            public int Frame, Index;
            public string Type, Session;
            public double TotalMs, GuardMs, AstralMs, TextMs, FrameMs;
            public int AstralCalls;
            public bool VoiceBefore, VoiceAfter;
        }

        internal sealed class Batch
        {
            public Sample[] Samples;
            public int Dropped;
        }

        private readonly Sample[] _samples;
        private readonly Func<double> _clock;
        private readonly Action<Batch> _flush;
        private int _count, _dropped, _active = -1;
        private double _lastUpdate, _lastCommand;
        private bool _hasUpdate, _enabled;

        internal ReceiveTimingRecorder(Action<Batch> flush, Func<double> clock = null, int capacity = 8192)
        {
            _flush = flush;
            _clock = clock ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
            _samples = new Sample[Math.Max(1, capacity)];
        }

        internal void Tick(bool enabled, int frame)
        {
            if (!enabled && !_enabled && _count == 0) return;
            double now = _clock();
            if (_count > 0 && (!enabled || now - _lastCommand >= 2.0)) Flush();
            _enabled = enabled;
            if (enabled && _count > 0 && _hasUpdate)
                Add(new Sample { Frame = frame, FrameMs = (now - _lastUpdate) * 1000.0 });
            _lastUpdate = now;
            _hasUpdate = true;
        }

        internal Scope BeginCommand(string type, string session, int index, int frame, bool voiceBefore)
        {
            if (!_enabled) return default(Scope);
            _lastCommand = _clock();
            _active = Add(new Sample { IsCommand = true, Type = type, Session = session,
                Index = index, Frame = frame, VoiceBefore = voiceBefore });
            return new Scope(this, _lastCommand, null, _active);
        }

        internal Scope Measure(ReceiveTimingStage stage)
        {
            return _active < 0 ? default(Scope) : new Scope(this, _clock(), stage, _active);
        }

        internal void SetVoiceAfter(bool value)
        {
            if (_active >= 0) _samples[_active].VoiceAfter = value;
        }

        private int Add(Sample sample)
        {
            if (_count == _samples.Length) { _dropped++; return -1; }
            _samples[_count] = sample;
            return _count++;
        }

        private void End(double start, ReceiveTimingStage? stage, int index)
        {
            if (index < 0) return;
            double ms = (_clock() - start) * 1000.0;
            if (!stage.HasValue) { _samples[index].TotalMs = ms; _active = -1; }
            else if (stage == ReceiveTimingStage.Guard) _samples[index].GuardMs += ms;
            else if (stage == ReceiveTimingStage.Text) _samples[index].TextMs += ms;
            else { _samples[index].AstralMs += ms; _samples[index].AstralCalls++; }
        }

        internal void Flush()
        {
            if (_count == 0 && _dropped == 0) return;
            var copy = new Sample[_count];
            Array.Copy(_samples, copy, _count);
            Array.Clear(_samples, 0, _count);
            var batch = new Batch { Samples = copy, Dropped = _dropped };
            _count = _dropped = 0;
            _active = -1;
            _flush?.Invoke(batch);
        }

        internal readonly struct Scope : IDisposable
        {
            private readonly ReceiveTimingRecorder _owner;
            private readonly double _start;
            private readonly ReceiveTimingStage? _stage;
            private readonly int _index;
            internal Scope(ReceiveTimingRecorder owner, double start, ReceiveTimingStage? stage, int index)
            { _owner = owner; _start = start; _stage = stage; _index = index; }
            public void Dispose() { _owner?.End(_start, _stage, _index); }
        }

        internal static void WriteAsync(Batch batch, string directory, string buildId)
        {
            // Resolve names before enqueueing; no Unity or BepInEx API on the worker.
            string name = "receive-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
                + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".csv";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    using (var writer = new StreamWriter(Path.Combine(directory, name), false, new UTF8Encoding(false)))
                    {
                        writer.WriteLine("# build_mvid=" + buildId + ",dropped=" + batch.Dropped);
                        writer.WriteLine("kind,frame,type,session,index,total_ms,guard_ms,astral_ms,text_ms,astral_calls,frame_ms,voice_before,voice_after");
                        foreach (Sample s in batch.Samples)
                            writer.WriteLine(string.Join(",", s.IsCommand ? "command" : "frame", s.Frame.ToString(CultureInfo.InvariantCulture),
                                Quote(s.Type), Quote(s.Session), s.Index.ToString(CultureInfo.InvariantCulture),
                                F(s.TotalMs), F(s.GuardMs), F(s.AstralMs), F(s.TextMs), s.AstralCalls.ToString(CultureInfo.InvariantCulture),
                                F(s.FrameMs), s.VoiceBefore ? "1" : "0", s.VoiceAfter ? "1" : "0"));
                    }
                }
                catch (Exception ex) { Trace.TraceError("Receive timing export failed: " + ex.Message); }
            });
        }

        private static string F(double value) => value.ToString("F6", CultureInfo.InvariantCulture);
        private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
