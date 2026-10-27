using UnityEngine;

namespace MainGameDisplacementMap
{
    public sealed partial class Plugin
    {
        private Texture2D _dispMap;
        private Color32[] _dispMapPixels;
        private float[] _dispMapRadii;
        private float[] _dispMapCosAngles;
        private float[] _dispMapSinAngles;
        private float[] _dispMapDecayBases;
        private float[] _dispMapDecaySteps;
        private float[] _dispMapScrollX;
        private float[] _dispMapScrollY;
        private int[] _dispMapPixelGenerations;
        private int _dispMapGeneration;
        private int _currentMapWaveCount;
        private float _currentMapNoiseAmount;
        private float _currentMapNoiseX1, _currentMapNoiseY1, _currentMapNoiseX2, _currentMapNoiseY2;
        private float _currentMapInvW, _currentMapInvH;
        private readonly float[] _waveCosOffsets = new float[8];
        private readonly float[] _waveSinOffsets = new float[8];
        private readonly float[] _wavePhases = new float[8];
        private readonly float[] _waveFrequencies = new float[8];
        private float _dispMapCachedDamping = float.NaN;
        private int _dispMapVersion = -1;
        private float _lastMapTime;
        private bool _managedMapSamplingChecked;
        private bool _useManagedMapSampling;

        private void EnsureDisplacementMap()
        {
            int res = Mathf.Clamp(_cfgMapResolution.Value, 32, 512);
            if (_dispMap != null && _dispMap.width == res) return;

            if (_dispMap != null) Destroy(_dispMap);

            _dispMap = new Texture2D(res, res, TextureFormat.ARGB32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _dispMapPixels = new Color32[res * res];
            _dispMapRadii = new float[res * res];
            _dispMapCosAngles = new float[res * res];
            _dispMapSinAngles = new float[res * res];
            _dispMapDecayBases = new float[res * res];
            _dispMapDecaySteps = new float[res * res];
            _dispMapScrollX = new float[res];
            _dispMapScrollY = new float[res];
            _dispMapPixelGenerations = new int[res * res];
            _dispMapGeneration = 0;
            float inv = 1f / (res - 1);
            for (int y = 0; y < res; y++)
            {
                float dy = y * inv - 0.5f;
                for (int x = 0; x < res; x++)
                {
                    float dx = x * inv - 0.5f;
                    int index = y * res + x;
                    _dispMapRadii[index] = Mathf.Sqrt(dx * dx + dy * dy) * 2f;
                    float angle = Mathf.Atan2(dy, dx);
                    _dispMapCosAngles[index] = Mathf.Cos(angle);
                    _dispMapSinAngles[index] = Mathf.Sin(angle);
                }
            }
            _dispMapCachedDamping = float.NaN;
            _dispMapVersion = -1;
            _managedMapSamplingChecked = false;
            _useManagedMapSampling = false;
            if (_fileLog != null && _cfgVerboseLog.Value) _fileLog.Write("INFO", $"Displacement map created {res}x{res}");
        }

        private void ReleaseDisplacementMap()
        {
            if (_dispMap != null) Destroy(_dispMap);
            _dispMap = null;
            _dispMapPixels = null;
            _dispMapRadii = null;
            _dispMapCosAngles = null;
            _dispMapSinAngles = null;
            _dispMapDecayBases = null;
            _dispMapDecaySteps = null;
            _dispMapScrollX = null;
            _dispMapScrollY = null;
            _dispMapPixelGenerations = null;
            _dispMapGeneration = 0;
            _dispMapCachedDamping = float.NaN;
            _dispMapVersion = -1;
            _managedMapSamplingChecked = false;
            _useManagedMapSampling = false;
        }

        private void UpdateDisplacementMapIfNeeded()
        {
            EnsureDisplacementMap();
            float t = Time.time * _cfgTimeScale.Value * 0.6f;
            if (Mathf.Abs(t - _lastMapTime) < 0.033f && _dispMapVersion == _cfgMapResolution.Value) return;

            RegenerateDisplacementMap(t);
            _lastMapTime = t;
            _dispMapVersion = _cfgMapResolution.Value;
        }

        private void RegenerateDisplacementMap(float time)
        {
            if (_dispMap == null) return;

            int w = _dispMap.width;
            int h = _dispMap.height;
            int waveCount = Mathf.Clamp(_cfgMapWaveCount.Value, 1, 8);
            float freq = _cfgMapFrequency.Value;
            float prop = _cfgMapPropagationSpeed.Value;
            float damp = Mathf.Max(0.1f, _cfgMapDamping.Value);
            _currentMapWaveCount = waveCount;
            _currentMapNoiseAmount = Mathf.Clamp01(_cfgMapNoise.Value);
            float scrollX = _cfgMapScrollX.Value;
            float scrollY = _cfgMapScrollY.Value;

            _currentMapInvW = 1f / (w - 1);
            _currentMapInvH = 1f / (h - 1);
            _currentMapNoiseX1 = time * scrollX * 0.3f;
            _currentMapNoiseY1 = time * scrollY * 0.3f;
            _currentMapNoiseX2 = time * 0.4f;
            _currentMapNoiseY2 = time * 0.2f;
            if (_dispMapCachedDamping != damp)
            {
                for (int i = 0; i < _dispMapRadii.Length; i++)
                {
                    float rd = _dispMapRadii[i] * damp;
                    _dispMapDecayBases[i] = Mathf.Exp(-rd * 0.7f);
                    _dispMapDecaySteps[i] = Mathf.Exp(-rd * 0.1f);
                }
                _dispMapCachedDamping = damp;
            }
            for (int wi = 0; wi < waveCount; wi++)
            {
                float angle = wi * 0.4f;
                _waveCosOffsets[wi] = Mathf.Cos(angle);
                _waveSinOffsets[wi] = Mathf.Sin(angle);
                _wavePhases[wi] = time * prop + wi * 1.7f;
                _waveFrequencies[wi] = freq * (1f + wi * 0.08f);
            }

            for (int x = 0; x < w; x++)
                _dispMapScrollY[x] = scrollY != 0f
                    ? Mathf.Cos(time * 0.7f + x * _currentMapInvW * 4f) * scrollY * 0.3f
                    : 0f;

            for (int y = 0; y < h; y++)
                _dispMapScrollX[y] = Mathf.Sin(time * 0.8f + y * _currentMapInvH * 4f) * scrollX * 0.3f;

            if (++_dispMapGeneration == int.MaxValue)
            {
                System.Array.Clear(_dispMapPixelGenerations, 0, _dispMapPixelGenerations.Length);
                _dispMapGeneration = 1;
            }

            if (!_useManagedMapSampling)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                        GenerateDisplacementPixel(x, y);
                _dispMap.SetPixels32(_dispMapPixels);
                if (!_managedMapSamplingChecked)
                    ValidateManagedMapSampling();
            }
        }

        private void GenerateDisplacementPixel(int x, int y)
        {
            int pixelIndex = y * _dispMap.width + x;
            float r = _dispMapRadii[pixelIndex];
            float baseCos = _dispMapCosAngles[pixelIndex];
            float baseSin = _dispMapSinAngles[pixelIndex];
            float decay = _dispMapDecayBases[pixelIndex];
            float decayStep = _dispMapDecaySteps[pixelIndex];
            float dispX = 0f;
            float dispY = 0f;
            for (int wi = 0; wi < _currentMapWaveCount; wi++)
            {
                float wave = Mathf.Sin(r * _waveFrequencies[wi] - _wavePhases[wi]) * decay;
                dispX += (baseCos * _waveCosOffsets[wi] - baseSin * _waveSinOffsets[wi]) * wave;
                dispY += (baseSin * _waveCosOffsets[wi] + baseCos * _waveSinOffsets[wi]) * wave * 0.7f;
                decay *= decayStep;
            }
            if (_currentMapNoiseAmount > 0.001f)
            {
                float nx = x * _currentMapInvW;
                float ny = y * _currentMapInvH;
                float n1 = Mathf.PerlinNoise(nx * 3.7f + _currentMapNoiseX1, ny * 3.7f + _currentMapNoiseY1) - 0.5f;
                float n2 = Mathf.PerlinNoise(nx * 7.1f - _currentMapNoiseX2, ny * 7.1f + _currentMapNoiseY2) - 0.5f;
                dispX += n1 * _currentMapNoiseAmount * 1.6f;
                dispY += n2 * _currentMapNoiseAmount * 1.2f;
            }
            dispX += _dispMapScrollX[y];
            dispY += _dispMapScrollY[x];
            byte rx = (byte)Mathf.Clamp((dispX * 0.5f + 0.5f) * 255f, 0, 255);
            byte ry = (byte)Mathf.Clamp((dispY * 0.5f + 0.5f) * 255f, 0, 255);
            _dispMapPixels[pixelIndex] = new Color32(rx, ry, 128, 255);
            _dispMapPixelGenerations[pixelIndex] = _dispMapGeneration;
        }

        private Color32 GetManagedMapPixel(int x, int y)
        {
            int index = y * _dispMap.width + x;
            if (_dispMapPixelGenerations[index] != _dispMapGeneration)
                GenerateDisplacementPixel(x, y);
            return _dispMapPixels[index];
        }

        private void ValidateManagedMapSampling()
        {
            _managedMapSamplingChecked = true;
            const float tolerance = 2f / 255f;
            for (int i = 0; i < 16; i++)
            {
                float u = (i * 37 % 101) / 100f;
                float v = (i * 71 % 103) / 102f;
                Color native = _dispMap.GetPixelBilinear(u, v);
                Vector2 managed = SampleManagedMap(u, v);
                if (Mathf.Abs(managed.x - (native.r - 0.5f) * 2f) > tolerance
                    || Mathf.Abs(managed.y - (native.g - 0.5f) * 2f) > tolerance)
                    return;
            }
            _useManagedMapSampling = true;
        }

        private Vector2 SampleManagedMap(float u, float v)
        {
            int width = _dispMap.width;
            int height = _dispMap.height;
            float px = Mathf.Clamp01(u) * width - 0.5f;
            float py = Mathf.Clamp01(v) * height - 0.5f;
            int x0 = Mathf.FloorToInt(px);
            int y0 = Mathf.FloorToInt(py);
            float fx = px - x0;
            float fy = py - y0;
            int x1 = Mathf.Clamp(x0 + 1, 0, width - 1);
            int y1 = Mathf.Clamp(y0 + 1, 0, height - 1);
            x0 = Mathf.Clamp(x0, 0, width - 1);
            y0 = Mathf.Clamp(y0, 0, height - 1);

            Color32 a = GetManagedMapPixel(x0, y0);
            Color32 b = GetManagedMapPixel(x1, y0);
            Color32 c = GetManagedMapPixel(x0, y1);
            Color32 d = GetManagedMapPixel(x1, y1);
            float r = Mathf.Lerp(Mathf.Lerp(a.r, b.r, fx), Mathf.Lerp(c.r, d.r, fx), fy) / 255f;
            float g = Mathf.Lerp(Mathf.Lerp(a.g, b.g, fx), Mathf.Lerp(c.g, d.g, fx), fy) / 255f;
            return new Vector2((r - 0.5f) * 2f, (g - 0.5f) * 2f);
        }

        private Vector2 SampleDisplacementMap(Vector2 uv, float sx, float sy)
        {
            if (_dispMap == null) return Vector2.zero;

            // 時間スクロールを考慮したUV
            Vector2 mapUv = new Vector2(uv.x + sx, uv.y + sy);

            if (_useManagedMapSampling)
                return SampleManagedMap(mapUv.x, mapUv.y);

            Color c = _dispMap.GetPixelBilinear(mapUv.x, mapUv.y);
            float dx = (c.r - 0.5f) * 2f;
            float dy = (c.g - 0.5f) * 2f;
            return new Vector2(dx, dy);
        }

    }
}
