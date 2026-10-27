using System;

namespace MainGameVoiceFaceEventBridge.Audio
{
    // Lock once per audio buffer, never per sample. No Unity calls or allocations in Process.
    internal sealed class SampleGainEnvelope
    {
        private readonly object _sync = new object();
        private float _gain = 1, _start = 1, _target = 1;
        private int _position, _frames;
        internal bool Complete { get { lock (_sync) return _position >= _frames; } }
        internal void Reset(float gain)
        {
            lock (_sync) { _gain = _start = _target = gain; _position = _frames = 0; }
        }
        internal void FadeTo(float target, int milliseconds, int sampleRate)
        {
            lock (_sync)
            {
                _start = _gain;
                _target = target;
                _position = 0;
                _frames = Math.Max(0, (int)((long)milliseconds * sampleRate / 1000));
                if (_frames == 0) _gain = target;
            }
        }
        internal void Process(float[] data, int channels)
        {
            if (channels <= 0) return;
            lock (_sync)
            {
                if (_position >= _frames && _gain == 1f) return;
                for (int i = 0; i < data.Length; i += channels)
                {
                    if (_position < _frames)
                    {
                        _position++;
                        _gain = _start + (_target - _start) * Ease((double)_position / _frames);
                    }
                    for (int c = 0; c < channels && i + c < data.Length; c++) data[i + c] *= _gain;
                }
            }
        }
        internal static float Ease(double fraction) => (float)(0.5 - 0.5 * Math.Cos(Math.PI * fraction));

        internal static float[] FadeClip(float[] pcm, int channels, int sampleRate, int fadeInMs, int fadeOutMs)
        {
            var result = (float[])pcm.Clone();
            int frames = pcm.Length / channels;
            int fadeIn = Math.Min(frames, Math.Max(0, sampleRate * fadeInMs / 1000));
            int fadeOut = Math.Min(frames, Math.Max(0, sampleRate * fadeOutMs / 1000));
            for (int frame = 0; frame < frames; frame++)
            {
                float gain = 1;
                if (fadeIn > 0 && frame < fadeIn) gain *= Ease((double)frame / Math.Max(1, fadeIn - 1));
                int remaining = frames - 1 - frame;
                if (fadeOut > 0 && remaining < fadeOut) gain *= Ease((double)remaining / Math.Max(1, fadeOut - 1));
                for (int c = 0; c < channels; c++) result[frame * channels + c] *= gain;
            }
            return result;
        }
    }
}
