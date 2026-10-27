using System;

namespace MainGameVoiceFaceEventBridge
{
    public static class ResponseTextTriggerApi
    {
        public static bool VmdDanceEnabled { get; internal set; } = true;
        public static string VmdDanceSuffix { get; internal set; } = "を踊るね";

        // The scheduler preserves the same line timing and session cancellation as built-in triggers.
        public static event Action<string, Func<int, string, Action, float>> Received;

        internal static void Publish(string text, Func<int, string, Action, float> schedule)
        {
            var listeners = Received;
            if (listeners == null) return;
            foreach (Action<string, Func<int, string, Action, float>> listener in listeners.GetInvocationList())
            {
                try { listener(text, schedule); }
                catch (Exception ex) { UnityEngine.Debug.LogWarning("[VoiceFaceEventBridge] response trigger listener failed: " + ex); }
            }
        }
    }
}
