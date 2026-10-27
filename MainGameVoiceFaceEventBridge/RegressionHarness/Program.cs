using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using MainGameVoiceFaceEventBridge.External;
using MainGameVoiceFaceEventBridge.Audio;
using MainGameVoiceFaceEventBridge.Game;
using MainGameVoiceFaceEventBridge.Inbound;
using MainGameVoiceFaceEventBridge.Infrastructure;
using MainGameVoiceFaceEventBridge.ResponseText;
using MainGameVoiceFaceEventBridge.Scheduling;

namespace MainGameVoiceFaceEventBridge.RegressionHarness
{
    internal static partial class Program
    {
        private static int _assertions;

        private static int Main()
        {
            try
            {
                CheckGameVoiceLoadAdmission();
                CharacterizeCommandParser();
                CharacterizeMotionIntensityText();
                CharacterizeMovementMetadata();
                CharacterizeLegacyPipeNormalization();
                CharacterizeInboundJsonValidation();
                CharacterizeInboundQueueOrderingAndOverflow();
                CharacterizeStableScheduler();
                CharacterizeSequenceSessionCoordinator();
                CharacterizeSubtitleEventContract();
                CharacterizeLocalSubtitleDelivery();
                CharacterizeResponseTextQueue();
                CharacterizeGameVoiceGuardState();
                CharacterizePipeServerLifecycle();
                CharacterizeAudioSequenceState();
                CharacterizeImpactCues();
                CharacterizeAnonymizedFixtureRoundTrip();
                CharacterizeAstralMethodCache();
                CharacterizeNamedPosePrefilter();
                CharacterizeReceiveTiming();
                Characterize49PartSequence();
                Console.WriteLine("REGRESSION_HARNESS_OK assertions=" + _assertions);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("REGRESSION_HARNESS_FAILED " + ex);
                return 1;
            }
        }

        private static void CharacterizeMotionIntensityText()
        {
            string[] targets = { "腰の動き", "腰を" };
            string[][] levels =
            {
                new[] { "とてもおとなしく" },
                new[] { "おとなしく" },
                new[] { "普通に" },
                new[] { "激しく" },
                new[] { "すごく激しく" }
            };
            var hits = MotionIntensityTextMatcher.FindHits(
                "腰の動きをすごく激しくするね。腰をおとなしく動かすね。", targets, levels);
            Assert(hits.Count == 2 && hits[0].Level == 4 && hits[1].Level == 1, "motion intensity longest match and order");
            Assert(hits[0].Index < hits[1].Index, "motion intensity match timing index");
            Assert(MotionIntensityTextMatcher.FindHits("激しくするね。腰の動きはどう？", targets, levels).Count == 0,
                "motion intensity same sentence target required");
            Assert(MotionIntensityTextMatcher.FindHits("腰の動きを普通に戻すね", targets, levels)[0].Level == 2,
                "motion intensity normal");
        }

        private static void CharacterizeMovementMetadata()
        {
            const string json = "{\"type\":\"speak_sequence\",\"sessionId\":\"movement-test\",\"lineSourceIndices\":[0,0,1],\"lineSourceTexts\":[\"first\",\"first\",\"second\"],\"lineMovementIntensities\":[80,80,-1]}";
            // Unity's native JsonUtility is unavailable in this console harness.
            var command = new System.Web.Script.Serialization.JavaScriptSerializer()
                .Deserialize<ExternalVoiceFaceCommand>(json);
            Assert(command != null, "movement metadata schema deserializes");
            Assert(command.lineSourceIndices.Length == 3 && command.lineSourceIndices[1] == 0,
                "original line identity survives serialized audio splitting");
            Assert(command.lineMovementIntensities[0] == 80 && command.lineMovementIntensities[2] == -1,
                "movement score and absent sentinel survive deserialization");
            Assert(command.lineSourceTexts[2] == "second", "source text arrives for spoken command detection");
        }

        private static void CharacterizeCommandParser()
        {
            const string json = "{\"type\":\"speak_sequence\",\"sessionId\":\"session-a\",\"main\":1,\"items\":[{\"index\":1,\"audioPath\":\"C:\\\\voice\\\\line_001.wav\",\"subtitle\":\"first\",\"durationSeconds\":1.25},{\"index\":2,\"path\":\"C:\\\\voice\\\\line_002.wav\",\"text\":\"second\",\"holdSeconds\":2.5}]}";
            var settings = new PluginSettings { AcceptJsonCommand = false };
            Assert(!CommandParser.TryParseIncoming(json, settings, out ExternalVoiceFaceCommand command, out string reason), "json disable");
            Assert(reason == "json command disabled", "json disable reason");

            settings.AcceptPlainAudioPath = true;
            Assert(CommandParser.TryParseIncoming("C:\\voice\\plain.wav", settings, out command, out reason), reason);
            Assert(command.ResolveAudioPath() == "C:\\voice\\plain.wav", "plain path");

            MethodInfo recover = typeof(CommandParser).GetMethod(
                "TryRecoverSequenceItems",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert(recover != null, "sequence recovery method");
            object[] args = { json, null };
            bool recovered = (bool)recover.Invoke(null, args);
            var items = args[1] as ExternalVoiceSequenceItem[];
            Assert(recovered && items != null && items.Length == 2, "sequence recovery count");
            Assert(items[0].ResolveAudioPath().EndsWith("line_001.wav", StringComparison.Ordinal), "audioPath alias");
            Assert(items[1].ResolveSubtitle() == "second", "subtitle alias");
        }

        private static void CharacterizeLegacyPipeNormalization()
        {
            Assert(CommandParser.NormalizePipeName(@"\\.\pipe\kks_voice_face_events") == "kks_voice_face_events", "pipe prefix");
            Assert(CommandParser.NormalizePipeName("kks_voice_face_events_diag_0423") == "kks_voice_face_events", "legacy pipe");
        }

        private static void CharacterizeInboundJsonValidation()
        {
            Assert(InboundJsonValidator.TryValidateObject("{\"type\":\"stop\"}", out string reason), reason);
            Assert(!InboundJsonValidator.TryValidateObject("[1,2]", out reason) && reason == "json object required", "json root object");
            Assert(!InboundJsonValidator.TryValidateObject("{\"type\":", out reason) && reason.StartsWith("invalid json:"), "malformed json rejected");

            var decoder = new InboundCommandDecoder();
            var settings = new PluginSettings { AcceptJsonCommand = true };
            Assert(!decoder.TryDecode("{\"type\":", settings, out ExternalVoiceFaceCommand command, out reason), "decoder rejects malformed json");
            Assert(command == null && reason.StartsWith("invalid json:"), "decoder malformed reason");
            settings.AcceptJsonCommand = false;
            Assert(!decoder.TryDecode("{\"type\":", settings, out command, out reason), "json disabled still rejected");
            Assert(reason == "json command disabled", "json disabled reason preserved");
        }

        private static void CharacterizeInboundQueueOrderingAndOverflow()
        {
            var queue = new InboundCommandQueue();
            var a = new ExternalVoiceFaceCommand { type = "speak", traceId = "a" };
            var b = new ExternalVoiceFaceCommand { type = "speak", traceId = "b" };
            var c = new ExternalVoiceFaceCommand { type = "stop", traceId = "c" };
            InboundCommandEnvelope ea = queue.Enqueue(a, 2, DateTime.UtcNow, out int droppedA);
            InboundCommandEnvelope eb = queue.Enqueue(b, 2, DateTime.UtcNow, out int droppedB);
            InboundCommandEnvelope ec = queue.Enqueue(c, 2, DateTime.UtcNow, out int droppedC);

            Assert(droppedA == 0 && droppedB == 0 && droppedC == 1, "bounded overflow");
            Assert(ea.SequenceNo < eb.SequenceNo && eb.SequenceNo < ec.SequenceNo, "monotonic sequence");
            Assert(queue.Count == 2, "bounded count");
            Assert(queue.TryDequeue(out InboundCommandEnvelope first) && first.Command.traceId == "b", "oldest dropped");
            Assert(queue.TryDequeue(out InboundCommandEnvelope second) && second.Command.traceId == "c", "fifo order");
            Assert(!queue.TryDequeue(out _), "queue empty");
        }

        private static void CharacterizeAnonymizedFixtureRoundTrip()
        {
            const string sensitiveText = "private spoken sentence";
            const string sensitivePath = @"D:\private\line.wav";
            const string sensitiveSession = "user-session-123";
            string raw = "{\"type\":\"speak_sequence\",\"sessionId\":\"" + sensitiveSession + "\",\"responseText\":\"" + sensitiveText + "\",\"items\":[{\"audioPath\":\"D:\\\\private\\\\line.wav\",\"subtitle\":\"" + sensitiveText + "\"}]}";
            RecordedInboundEvent second = InboundEventFixtureCodec.Record(2, 100, raw);
            RecordedInboundEvent first = InboundEventFixtureCodec.Record(1, 100, raw.Replace("line.wav", "first.wav"));

            Assert(!second.AnonymizedJson.Contains(sensitiveText), "text redacted");
            Assert(!second.AnonymizedJson.Contains(sensitivePath), "path redacted");
            Assert(!second.AnonymizedJson.Contains(sensitiveSession), "identity redacted");
            Assert(second.AnonymizedJson.Contains(".wav"), "extension retained");

            string jsonl;
            using (var writer = new StringWriter())
            {
                InboundEventFixtureCodec.WriteJsonLines(writer, new[] { second, first });
                jsonl = writer.ToString();
            }

            List<RecordedInboundEvent> replay;
            using (var reader = new StringReader(jsonl)) replay = InboundEventFixtureCodec.ReadJsonLines(reader);
            Assert(replay.Count == 2, "fixture count");
            Assert(replay[0].SequenceNo == 1 && replay[1].SequenceNo == 2, "stable replay order");
            Assert(replay[1].PayloadSha256 == second.PayloadSha256, "fixture hash");
        }

        private static void CharacterizeStableScheduler()
        {
            var scheduler = new StableActionScheduler();
            var calls = new List<string>();
            scheduler.Schedule(5f, () => calls.Add("first-at-five"));
            scheduler.Schedule(3f, () => calls.Add("at-three"));
            scheduler.Schedule(5f, () => throw new InvalidOperationException("expected"));
            scheduler.Schedule(5f, () => calls.Add("last-at-five"));

            int errors = 0;
            Assert(scheduler.DrainDue(4f, _ => errors++) == 1, "scheduler first drain count");
            Assert(calls.Count == 1 && calls[0] == "at-three", "scheduler due order");
            Assert(scheduler.DrainDue(5f, _ => errors++) == 3, "scheduler second drain count");
            Assert(calls.Count == 3, "scheduler continues after exception");
            Assert(calls[1] == "first-at-five" && calls[2] == "last-at-five", "stable same-time order");
            Assert(errors == 1, "scheduler error callback");
            scheduler.Schedule(9f, () => calls.Add("cleared"));
            Assert(scheduler.Clear() == 1 && scheduler.Count == 0, "scheduler clear");
        }

        private static void CharacterizeSequenceSessionCoordinator()
        {
            var scheduler = new StableActionScheduler();
            var sessions = new SequenceSessionCoordinator(scheduler);
            var calls = new List<string>();
            var stale = new List<string>();

            sessions.Activate(" session-a ");
            Assert(sessions.ActiveSessionId == "session-a", "session normalize");
            Assert(sessions.IsCurrent("session-a") && !sessions.IsCurrent("session-b"), "stale session detection");
            Assert(sessions.IsCurrent(string.Empty), "blank session compatibility");

            sessions.MarkTriggerRegistered("session-a");
            sessions.MarkTriggerRegistered("session-a");
            Assert(sessions.IsTriggerRegistered("session-a"), "trigger registration");
            Assert(sessions.RegisteredTriggerSessionCount == 1, "duplicate trigger suppression");

            Assert(!sessions.ScheduleLineAction(
                "session-a", 0, 0.5f, "first", () => calls.Add("first"),
                (session, label) => stale.Add(session + ":" + label), out _), "line action pending");
            Assert(!sessions.ScheduleLineAction(
                "session-a", 0, 0.5f, "second", () => calls.Add("second"),
                (session, label) => stale.Add(session + ":" + label), out _), "second line action pending");
            IReadOnlyList<ScheduledLineActionInfo> scheduled = sessions.RecordLineStart(
                "session-a", 0, 10f, (session, label) => stale.Add(session + ":" + label));
            Assert(scheduled.Count == 2 && scheduled[0].Label == "first" && scheduled[1].Label == "second", "pending insertion order");
            scheduler.DrainDue(10.5f, _ => { });
            Assert(calls.Count == 2 && calls[0] == "first" && calls[1] == "second", "line action execution order");

            sessions.ScheduleLineAction(
                "session-a", 1, 0f, "stale", () => calls.Add("must-not-run"),
                (session, label) => stale.Add(session + ":" + label), out _);
            sessions.RecordLineStart("session-a", 1, 20f, (session, label) => stale.Add(session + ":" + label));
            sessions.Activate("session-b");
            scheduler.DrainDue(20f, _ => { });
            Assert(!calls.Contains("must-not-run") && stale.Count == 1, "generation switch cancels stale action");

            SequenceTransientClearResult cleared = sessions.ClearTransientState();
            Assert(cleared.TriggerSessionCount == 1 && sessions.RegisteredTriggerSessionCount == 0, "transient state clear");
            sessions.Deactivate();
            Assert(string.IsNullOrEmpty(sessions.ActiveSessionId), "session deactivate");
        }

        private static void CharacterizeSubtitleEventContract()
        {
            Assert(
                SubtitleEventPayloadBuilder.BuildUrl("", 0, "") == "http://127.0.0.1:18766/subtitle-event",
                "subtitle url defaults");
            Assert(SubtitleEventPayloadBuilder.NormalizeSendMode("fulltextonce") == "FullTextOnce", "subtitle send mode");
            Assert(SubtitleEventPayloadBuilder.BuildProgressText("line", 2, 3) == "[2/3] line", "subtitle progress");
            Assert(
                SubtitleEventPayloadBuilder.BuildDisplayText("line", "StackFemale") == "<color=#FF7ACDFF>line</color>",
                "subtitle female color");

            var message = new SubtitleEventMessage
            {
                Text = "line \"one\"\nnext",
                WavName = "line_001.wav",
                Backend = "Overlay",
                DisplayMode = "StackFemale",
                HoldSeconds = 1.25f,
                SessionId = "session-a",
                Index = 1
            };
            string expectedPayload = SubtitleEventPayloadBuilder.BuildPayload(message);
            Assert(expectedPayload.Contains("\"source\":\"voiceface_sequence\""), "subtitle source contract");
            Assert(expectedPayload.Contains("\"backend\":\"Overlay\""), "subtitle backend contract");
            Assert(expectedPayload.Contains("line \\\"one\\\"\\nnext"), "subtitle json escaping");

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string receivedHeader = null;
            string receivedBody = null;
            Exception serverError = null;
            var server = new Thread(() =>
            {
                try
                {
                    using (TcpClient socket = listener.AcceptTcpClient())
                    using (NetworkStream stream = socket.GetStream())
                    {
                        byte[] headerBytes = ReadUntilHeaderEnd(stream);
                        receivedHeader = Encoding.ASCII.GetString(headerBytes);
                        int contentLength = ParseContentLength(receivedHeader);
                        byte[] body = new byte[contentLength];
                        int offset = 0;
                        while (offset < body.Length)
                        {
                            int read = stream.Read(body, offset, body.Length - offset);
                            if (read <= 0) throw new EndOfStreamException("subtitle request body ended early");
                            offset += read;
                        }
                        receivedBody = Encoding.UTF8.GetString(body);
                        byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n{}");
                        stream.Write(response, 0, response.Length);
                    }
                }
                catch (Exception ex) { serverError = ex; }
            });
            server.IsBackground = true;
            server.Start();

            var logs = new List<string>();
            var warnings = new List<string>();
            try
            {
                new SubtitleEventClient(logs.Add, warnings.Add).Send(
                    new SubtitleEventClientOptions
                    {
                        Host = "127.0.0.1",
                        Port = port,
                        EndpointPath = "subtitle-event",
                        Token = "token-a",
                        VerboseLog = true
                    },
                    message);
                Assert(server.Join(3000), "subtitle server completed");
            }
            finally
            {
                listener.Stop();
            }

            if (serverError != null) throw serverError;
            Assert(receivedHeader.StartsWith("POST /subtitle-event HTTP/", StringComparison.Ordinal), "subtitle http endpoint");
            Assert(receivedHeader.IndexOf("X-Auth-Token: token-a", StringComparison.OrdinalIgnoreCase) >= 0, "subtitle auth header");
            Assert(receivedBody == expectedPayload, "subtitle payload over http");
            Assert(warnings.Count == 0 && logs.Exists(v => v.Contains("sent status=200")), "subtitle response log");
        }

        private static void CharacterizeResponseTextQueue()
        {
            var queue = new ResponseTextCommandQueue();
            Assert(queue.Enqueue(new ExternalVoiceFaceCommand { traceId = "a" }, 2) == 0, "response enqueue a");
            Assert(queue.Enqueue(new ExternalVoiceFaceCommand { traceId = "b" }, 2) == 0, "response enqueue b");
            Assert(queue.Enqueue(new ExternalVoiceFaceCommand { traceId = "c" }, 2) == 1, "response overflow");
            Assert(queue.Count == 2, "response bounded count");
            Assert(queue.TryDequeue(out ExternalVoiceFaceCommand first) && first.traceId == "b", "response oldest dropped");
            Assert(queue.TryDequeue(out ExternalVoiceFaceCommand second) && second.traceId == "c", "response fifo");
            queue.Enqueue(new ExternalVoiceFaceCommand { traceId = "d" }, 0);
            Assert(queue.Clear() == 1 && queue.Count == 0, "response clear");
        }

        private static void CharacterizeGameVoiceGuardState()
        {
            var state = new GameVoiceGuardState();
            state.ExtendBlockUntil(5f);
            state.ExtendBlockUntil(3f);
            Assert(state.BlockUntil == 5f && state.IsWithinBlock(4.9f), "voice block monotonic");
            Assert(!state.IsWithinBlock(5f), "voice block expiry");

            Assert(state.CaptureProcStop(true, true), "voice proc capture");
            Assert(!state.CaptureProcStop(false, false), "voice proc capture once");
            Assert(!state.ResolveRestoreValue(false, out bool staleAstral) && staleAstral, "astral stale true correction");
            Assert(state.ResolveRestoreValue(true, out staleAstral) && !staleAstral, "astral active restore");
            Assert(state.ShouldLogRestorePending(1f), "restore log first");
            Assert(!state.ShouldLogRestorePending(1.5f), "restore log throttled");
            Assert(state.ShouldLogRestorePending(2f), "restore log resumes");
            state.CompleteRestore();
            Assert(!state.ProcStopOverridden && state.NextRestorePendingLogTime == 0f, "restore state cleared");
            state.ClearBlock();
            Assert(state.BlockUntil == 0f, "voice block clear");
        }

        private static void CharacterizePipeServerLifecycle()
        {
            string pipeName = "voiceface_regression_" + Guid.NewGuid().ToString("N");
            string received = null;
            var delivered = new ManualResetEventSlim(false);
            var logs = new List<string>();
            var warnings = new List<string>();
            var errors = new List<string>();
            using (var host = new PipeServerHost(
                pipeName,
                line => { received = line; delivered.Set(); },
                logs.Add,
                warnings.Add,
                errors.Add))
            {
                Assert(host.IsForPipe(pipeName.ToUpperInvariant()), "pipe name comparison");
                host.Start();
                using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out))
                {
                    client.Connect(2000);
                    using (var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true))
                    {
                        writer.WriteLine("{\"type\":\"stop\"}");
                        writer.Flush();
                    }
                }
                Assert(delivered.Wait(2000), "pipe line delivered");
                host.Stop();
                host.Stop();
            }
            delivered.Dispose();
            Assert(received == "{\"type\":\"stop\"}", "pipe payload unchanged");
            Assert(errors.Count == 0, "pipe no worker errors");
            Assert(logs.Exists(v => v.Contains("stop completed")), "pipe stop completion");
        }

        private static void CharacterizeAudioSequenceState()
        {
            var state = new AudioSequenceState();
            var first = new ExternalVoicePlaybackItem { Path = "first.wav" };
            var second = new ExternalVoicePlaybackItem { Path = "second.wav" };
            Assert(state.Begin(new[] { first, second }, "session-a") == 2, "audio sequence accepted");
            Assert(first.Index == 1 && first.Total == 2 && first.SequencePosition == 1, "audio first metadata");
            Assert(second.Index == 2 && second.Total == 2 && second.SequencePosition == 2, "audio second metadata");
            Assert(state.TryBeginNextLoad(out ExternalVoicePlaybackItem loading) && loading == first, "audio first pending");
            Assert(state.HasPending && state.HasQueuedOrPending, "audio pending state");

            var appended = new ExternalVoicePlaybackItem { Path = "third.wav", Index = 3, Total = 3 };
            Assert(state.Append(new[] { appended }, "session-a", out string active) == 1 && active == "session-a", "audio append same session");
            Assert(state.Append(new[] { new ExternalVoicePlaybackItem { Path = "wrong.wav" } }, "session-b", out active) == -1, "audio append stale rejected");
            Assert(state.TakePending() == first && !state.HasPending, "audio playback start consumes pending");
            Assert(state.TryBeginNextLoad(out loading) && loading == second, "audio completion advances second");
            Assert(state.TakePending() == second, "audio second playback start");
            Assert(state.TryBeginNextLoad(out loading) && loading == appended, "audio appended order");
            Assert(state.TakePending() == appended && !state.HasQueuedOrPending, "audio queue exhausted");

            state.Begin(new[] { new ExternalVoicePlaybackItem { Path = "cancel.wav" } }, "session-c");
            state.TryBeginNextLoad(out _);
            state.Clear();
            Assert(!state.HasQueuedOrPending && string.IsNullOrEmpty(state.SessionId), "audio stop cancels queue and pending");
        }

        private static byte[] ReadUntilHeaderEnd(Stream stream)
        {
            using (var buffer = new MemoryStream())
            {
                int matched = 0;
                byte[] marker = { 13, 10, 13, 10 };
                while (buffer.Length < 32768)
                {
                    int value = stream.ReadByte();
                    if (value < 0) throw new EndOfStreamException("subtitle request headers ended early");
                    buffer.WriteByte((byte)value);
                    matched = value == marker[matched] ? matched + 1 : (value == marker[0] ? 1 : 0);
                    if (matched == marker.Length) return buffer.ToArray();
                }
                throw new InvalidDataException("subtitle request headers too large");
            }
        }

        private static int ParseContentLength(string headers)
        {
            foreach (string line in (headers ?? string.Empty).Split(new[] { "\r\n" }, StringSplitOptions.None))
            {
                if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(line.Substring("Content-Length:".Length).Trim(), out int length)) return length;
            }
            throw new InvalidDataException("Content-Length missing");
        }

        private static void Assert(bool condition, string message)
        {
            _assertions++;
            if (!condition) throw new InvalidOperationException("assertion failed: " + message);
        }
    }
}
