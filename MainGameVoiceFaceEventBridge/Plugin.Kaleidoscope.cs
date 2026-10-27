using System;
using System.Reflection;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed partial class Plugin
    {
        private ConfigEntry<string> _kaleidoscopeThoughtTrigger;
        private bool _kaleidoscopeThoughtActive;
        private bool _thoughtMarkedForSession;

        private void InitializeKaleidoscopeTrigger()
        {
            _kaleidoscopeThoughtTrigger = Config.Bind("Kaleidoscope", "ThoughtTriggerWord", "｛思考中｝",
                "この語句以降を思考区間として扱い、音声終了まで背景の万華鏡を表示する。空欄で自動起動しない。");
        }

        private void ScheduleKaleidoscopeThought(string timedText, string originalText, Func<int, string, Action, float> schedule)
        {
            string trigger = (_kaleidoscopeThoughtTrigger?.Value ?? "｛思考中｝").Trim();
            if (trigger.Length == 0) return;
            int index = (timedText ?? string.Empty).IndexOf(trigger, StringComparison.Ordinal);
            if (index < 0 && (originalText ?? string.Empty).IndexOf(trigger, StringComparison.Ordinal) >= 0)
                index = 0; // TTS line data may omit the control word.
            if (index < 0) return;
            Logger.LogInfo("Thought trigger scheduled at character=" + index);
            schedule(index, "kaleidoscope-thought", () =>
            {
                Logger.LogInfo("Thought trigger fired: voicePlaying="
                    + (_externalVoicePlayer != null && _externalVoicePlayer.IsPlaying));
                if (_externalVoicePlayer != null && _externalVoicePlayer.IsPlaying)
                    SetKaleidoscopeThought(true);
            });
        }

        private void MarkThoughtPlaybackItems(List<ExternalVoicePlaybackItem> items, ExternalVoiceFaceCommand command)
        {
            if (items == null || items.Count == 0) return;
            string trigger = (_kaleidoscopeThoughtTrigger?.Value ?? "｛思考中｝").Trim();
            bool thought = _thoughtMarkedForSession;
            bool foundInLines = false;
            string[] lines = command?.lineTexts;
            for (int i = 0; i < items.Count; i++)
            {
                string line = lines != null && i < lines.Length ? lines[i] : items[i].Subtitle;
                if (trigger.Length > 0 && (line ?? string.Empty).IndexOf(trigger, StringComparison.Ordinal) >= 0)
                {
                    thought = true;
                    foundInLines = true;
                }
                items[i].IsThought = thought;
            }
            if (!foundInLines && !thought && trigger.Length > 0
                && (command?.responseText ?? string.Empty).IndexOf(trigger, StringComparison.Ordinal) >= 0)
            {
                // Sender may strip the control token from spoken lines.
                foreach (var item in items) item.IsThought = true;
                thought = true;
            }
            _thoughtMarkedForSession = thought;
            if (thought) Logger.LogInfo("Thought playback marked: lines=" + items.Count);
        }

        private void SetKaleidoscopeThought(bool active)
        {
            if (_kaleidoscopeThoughtActive == active) return;
            _kaleidoscopeThoughtActive = active;
            Logger.LogInfo("Thought notify " + (active ? "on" : "off"));
            try
            {
                Type api = Type.GetType("MainGameKaleidoscope.KaleidoscopeApi, MainGameKaleidoscope", false);
                MethodInfo method = api?.GetMethod("SetThoughtActive", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(bool) }, null);
                method?.Invoke(null, new object[] { active });
            }
            catch (Exception ex) { LogWarn("[kaleidoscope] thought notify failed: " + ex.Message); }
        }
    }
}
