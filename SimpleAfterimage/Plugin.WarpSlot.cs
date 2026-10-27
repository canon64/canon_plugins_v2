using UnityEngine;

namespace SimpleAfterimage
{
    public sealed partial class Plugin
    {
        private void InitWarpSlotMetadata()
        {
            if (_slots == null) return;
            int n = _slots.Length;
            _timedSlots.Ensure(n);
            bool initialize = false;
            if (_slotFadeFrames == null || _slotFadeFrames.Length != n) { _slotFadeFrames = new int[n]; initialize = true; }
            if (_slotAlphaScale == null || _slotAlphaScale.Length != n) { _slotAlphaScale = new float[n]; initialize = true; }
            if (_slotExternal == null || _slotExternal.Length != n) { _slotExternal = new bool[n]; initialize = true; }
            if (!initialize) return;

            int fade = GetBaseFadeFrames();
            float alpha = _cfgAlphaScale != null ? Mathf.Clamp01(_cfgAlphaScale.Value) : 1f;
            for (int i = 0; i < n; i++)
            {
                if (_slotFadeFrames[i] <= 0) _slotFadeFrames[i] = fade;
                if (_slotAlphaScale[i] <= 0f) _slotAlphaScale[i] = alpha;
            }
        }

        private void MarkCurrentSlotAsNormal()
        {
            InitWarpSlotMetadata();
            if (_slots == null || _writeIndex < 0 || _writeIndex >= _slots.Length) return;
            _life[_writeIndex] = GetBaseFadeFrames();
            _slotFadeFrames[_writeIndex] = GetBaseFadeFrames();
            MarkTimedNormal(_timedSlots, _slots.Length, _writeIndex);
            _slotAlphaScale[_writeIndex] = _vmdApplied != null ? _vmdApplied.Alpha : Mathf.Clamp01(_cfgAlphaScale.Value);
            _slotExternal[_writeIndex] = false;
        }

        private void MarkCurrentSlotAsWarp()
        {
            InitWarpSlotMetadata();
            if (_slots == null || _writeIndex < 0 || _writeIndex >= _slots.Length) return;
            _timedSlots.Mark(_writeIndex, 0, 0);
            int fade = GetWarpAfterimageFadeFrames();
            _life[_writeIndex] = Mathf.Max(1, fade);
            _slotFadeFrames[_writeIndex] = Mathf.Max(1, fade);
            _slotAlphaScale[_writeIndex] = GetWarpAfterimageAlphaScale();
            _slotExternal[_writeIndex] = true;
        }

        private float GetSlotAlphaScale(int slot)
        {
            // AgeThenBuildDrawList initializes the metadata once before reading slots.
            if (_slotAlphaScale == null || slot < 0 || slot >= _slotAlphaScale.Length) return Mathf.Clamp01(_cfgAlphaScale.Value);
            float a = _slotAlphaScale[slot];
            if (!_slotExternal[slot]) a *= GetBaseAfterimageAlphaMultiplierByWarp();
            return Mathf.Clamp01(a);
        }

        private int GetSlotFadeFrames(int slot)
        {
            // AgeThenBuildDrawList initializes the metadata once before reading slots.
            if (_slotFadeFrames == null || slot < 0 || slot >= _slotFadeFrames.Length) return GetBaseFadeFrames();
            return Mathf.Max(1, _slotFadeFrames[slot]);
        }
    }
}
