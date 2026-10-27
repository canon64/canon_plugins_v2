using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed class SampleFadeFilter : MonoBehaviour
    {
        internal readonly Audio.SampleGainEnvelope Envelope = new Audio.SampleGainEnvelope();
        private void OnAudioFilterRead(float[] data, int channels) => Envelope.Process(data, channels);
        internal void FadeTo(float gain, int milliseconds) => Envelope.FadeTo(gain, milliseconds, AudioSettings.outputSampleRate);
    }
}
