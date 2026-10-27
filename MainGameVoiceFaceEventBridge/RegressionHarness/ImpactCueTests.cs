using System;
using System.Collections.Generic;
using MainGameVoiceFaceEventBridge.Audio;

namespace MainGameVoiceFaceEventBridge.RegressionHarness
{
    internal static partial class Program
    {
        private static void CharacterizeImpactCues()
        {
            var state = new ImpactCueState();
            CueAction action;
            Assert(state.TryBegin(0, false, out action) && action == CueAction.StartCue, "cue without dialogue starts immediately");
            Assert(!state.TryBegin(1, false, out action), "cue retrigger discarded");
            Assert(state.Tick(2, false, true, false, true, 150) == CueAction.FinishCue, "standalone cue completes without resume");
            Assert(!state.TryBegin(2.149, false, out action), "post cue cooldown");
            Assert(state.TryBegin(2.151, true, out action) && action == CueAction.FadeDialogue, "speaking cue fades first");
            Assert(state.Tick(2.2, true, false, false, true, 150) == CueAction.None, "wait for samples to fade");
            action = state.Tick(2.231, true, true, false, true, 150);
            Assert(action == (CueAction.PauseDialogue | CueAction.StartCue) && state.PausedDialogue, "pause before insertion");
            Assert(state.HoldsDialogue, "paused dialogue queue held");
            Assert(state.Tick(3, false, true, true, true, 150) == CueAction.None, "paused isPlaying false does not finish dialogue");
            action = state.Tick(4, false, true, false, true, 150);
            Assert(action == (CueAction.FinishCue | CueAction.ResumeDialogue), "resume only the retained dialogue");
            Assert(!state.TryBegin(4.2, true, out action), "resume fade also prevents overlap");
            state.Tick(4.3, true, true, false, true, 150);
            Assert(!state.Busy, "resume completes");

            state.TryBegin(5, true, out action);
            Assert(state.Disable() == (CueAction.FinishCue | CueAction.ResumeDialogue), "off during prefade restores dialogue");
            state.Tick(6, true, true, false, true, 150);
            state.TryBegin(7, true, out action);
            state.Tick(8, true, true, false, true, 150);
            Assert(state.Disable() == CueAction.FadeCue, "off fades cue");
            Assert(state.Tick(8.01, false, true, true, false, 150) == CueAction.None, "off waits for sample fade");
            Assert(state.Tick(8.06, false, true, true, true, 150) == (CueAction.FinishCue | CueAction.ResumeDialogue), "off resumes retained dialogue");

            state.Abort();
            state.TryBegin(9, true, out action);
            state.Tick(10, true, true, false, true, 150);
            state.Abort();
            Assert(!state.Busy && !state.PausedDialogue, "stop or scene exit removes pause ownership");
            Assert(state.Tick(12, false, true, false, true, 150) == CueAction.None, "stop never resurrects old dialogue");
            state.TryBegin(13, true, out action);
            Assert(state.ForgetDialogue() == CueAction.StartCue, "replacement during prefade preserves cue");
            Assert(state.HoldsDialogue && !state.PausedDialogue, "replacement waits behind cue");
            Assert(state.Tick(15, false, true, false, true, 150) == CueAction.FinishCue, "replacement does not unpause old source");
            state.TryBegin(16, true, out action);
            Assert(state.Tick(16.01, false, false, false, true, 150) == CueAction.StartCue, "dialogue ending during prefade still plays cue");
            Assert(!state.PausedDialogue, "ended dialogue never marked paused");

            var bag = new CueShuffleBag(22, new Random(123));
            int previous = -1;
            for (int round = 0; round < 50; round++)
            {
                var seen = new HashSet<int>();
                for (int n = 0; n < 22; n++)
                {
                    int next = bag.Next();
                    Assert(next != previous, "shuffle prevents adjacent repeat including bag boundary");
                    seen.Add(next); previous = next;
                }
                Assert(seen.Count == 22, "shuffle covers every selected file");
            }

            var envelope = new SampleGainEnvelope();
            envelope.FadeTo(0, 80, 48000);
            float last = 1;
            for (int block = 0; block < 15; block++)
            {
                var samples = new float[512];
                for (int i = 0; i < samples.Length; i++) samples[i] = 1;
                envelope.Process(samples, 2);
                for (int i = 0; i < samples.Length; i += 2)
                {
                    Assert(samples[i] == samples[i + 1], "stereo shares one envelope frame");
                    Assert(samples[i] <= last && last - samples[i] < 0.001f, "sample fade continuous across buffers");
                    last = samples[i];
                }
            }
            Assert(envelope.Complete && last == 0, "80ms is exactly 3840 frames at 48kHz");
            envelope.FadeTo(1, 120, 48000);
            var ramp = new float[5760];
            for (int i = 0; i < ramp.Length; i++) ramp[i] = 1;
            envelope.Process(ramp, 1);
            Assert(envelope.Complete && ramp[0] < .001f && ramp[5759] == 1, "120ms fade resumes from zero to unity");

            var pcm = new float[44100];
            for (int i = 0; i < pcm.Length; i++) pcm[i] = .7f;
            float[] faded = SampleGainEnvelope.FadeClip(pcm, 1, 22050, 80, 80);
            Assert(faded[0] == 0 && faded[faded.Length - 1] == 0 && faded[22050] == .7f, "cue fades both sample endpoints");
            Assert(pcm[0] == .7f, "raw cue PCM remains reusable");
            float[] second = SampleGainEnvelope.FadeClip(pcm, 1, 22050, 80, 80);
            Assert(second[1000] == faded[1000], "repeat cue has identical nonzero waveform");
            Console.WriteLine("IMPACT_CUE_STATE_AND_SAMPLE_TESTS_OK");
        }
    }
}
