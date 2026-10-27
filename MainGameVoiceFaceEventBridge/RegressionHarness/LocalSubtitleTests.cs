using System.Collections.Generic;
using System.Threading;
using MainGameSubtitleCore;
using MainGameVoiceFaceEventBridge.External;

namespace MainGameVoiceFaceEventBridge.RegressionHarness
{
    internal static partial class Program
    {
        private sealed class RecordingSubtitleSink : ISubtitleRequestSink
        {
            public readonly List<SubtitleRequest> Requests = new List<SubtitleRequest>();
            public int ThreadId;
            public bool Accept = true;
            public bool EnqueueSubtitle(SubtitleRequest request, out string reason)
            {
                ThreadId = Thread.CurrentThread.ManagedThreadId;
                reason = null;
                if (!Accept) return false;
                Requests.Add(request);
                return true;
            }
        }

        private static void CharacterizeLocalSubtitleDelivery()
        {
            var client = new SubtitleEventClient(null, null);
            var options = new SubtitleEventClientOptions { Host = "127.0.0.1", Port = 18766, EndpointPath = "/subtitle-event" };
            var message = new SubtitleEventMessage { Text = "first", Backend = "Overlay", DisplayMode = "StackFemale", HoldSeconds = 1.25f };
            Assert(!client.TrySendLocal(options, message), "local subtitle fallback when sink absent");
            var sink = new RecordingSubtitleSink();
            SubtitleApi.Register(sink);
            try
            {
                Assert(client.TrySendLocal(options, message), "local subtitle delivered synchronously");
                Assert(sink.ThreadId == Thread.CurrentThread.ManagedThreadId, "subtitle uses playback caller thread");
                Assert(sink.Requests.Count == 1 && sink.Requests[0].Text == "first", "first subtitle queued before return");
                Assert(sink.Requests[0].Backend == SubtitleBackend.Overlay, "local subtitle backend preserved");
                Assert(sink.Requests[0].DisplayMode == "StackFemale" && sink.Requests[0].HoldSeconds == 1.25f, "local subtitle mode and hold preserved");
                message.Text = "second";
                Assert(client.TrySendLocal(options, message), "next line delivered");
                Assert(sink.Requests[0].Text == "first" && sink.Requests[1].Text == "second", "subtitle order preserved");
                options.Host = "192.0.2.1";
                Assert(!client.TrySendLocal(options, message), "remote subtitle keeps HTTP route");
                options.Host = "localhost";
                options.Port = 18767;
                Assert(!client.TrySendLocal(options, message), "custom local port keeps HTTP route");
                options.Port = 18766;
                options.EndpointPath = "/custom";
                Assert(!client.TrySendLocal(options, message), "custom local endpoint keeps HTTP route");
                options.EndpointPath = "/subtitle-event";
                options.Host = "::1";
                Assert(client.TrySendLocal(options, message), "IPv6 loopback uses local route");
                sink.Accept = false;
                Assert(!client.TrySendLocal(options, message), "rejected local delivery keeps fallback");
            }
            finally { SubtitleApi.Unregister(sink); }
            Assert(!client.TrySendLocal(options, message), "unregistered local sink keeps fallback");
        }
    }
}
