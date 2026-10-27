using UnityEngine;
using VRGIN.Core;

namespace SimpleAfterimage
{
    public sealed partial class Plugin
    {
        private enum VrStereoEye
        {
            Mono,
            Left,
            Right
        }

        private sealed class VrStereoSlotBuffer
        {
            internal RenderTexture[] Slots;
            internal readonly TimedAfterimageSlots Timed = new TimedAfterimageSlots();
            internal double NextVmdCapture;
            internal int[] Life;
            internal int[] SlotFadeFrames;
            internal float[] SlotAlphaScale;
            internal bool[] SlotExternal;
            internal RenderTexture[] DrawSlots;
            internal float[] DrawAlpha;
            internal int DrawCount;
            internal int WriteIndex;
            internal int FrameCounter;
            internal int Width;
            internal int Height;

            internal void Clear()
            {
                if (Life != null)
                {
                    for (int i = 0; i < Life.Length; i++)
                        Life[i] = 0;
                }

                Timed.Clear(); NextVmdCapture = 0;
                DrawCount = 0;
                WriteIndex = 0;
                FrameCounter = 0;
            }
        }

        private readonly VrStereoSlotBuffer _vrRightBuffer = new VrStereoSlotBuffer();
        private bool _vrStereoPathLogged;

        internal bool ShouldUseVrStereoRenderImage(Camera cam)
        {
            if (cam == null)
                return false;

            try
            {
                if (!VR.Active)
                    return false;

                Camera steamCam = ResolveSteamCamera();
                if (steamCam != null && ReferenceEquals(cam, steamCam))
                    return true;

                return cam.stereoEnabled || cam.stereoTargetEye != StereoTargetEyeMask.None;
            }
            catch
            {
                return false;
            }
        }

        internal void DrawVrStereoOnRenderImage(Camera cam, RenderTexture src, RenderTexture dst)
        {
            if (src == null || dst == null)
                return;

            if (PauseForExternalGlow())
            {
                Graphics.Blit(src, dst);
                return;
            }

            if (!IsEffectivelyEnabled() || !IsHSceneActive())
            {
                Graphics.Blit(src, dst);
                return;
            }

            EnsureMainVrRenderImageSlots(src.width, src.height);
            if (_slots == null)
            {
                Graphics.Blit(src, dst);
                return;
            }

            VrStereoEye eye = ResolveVrStereoEye(cam);
            bool useRight = eye == VrStereoEye.Right;
            if (useRight)
                EnsureVrStereoBuffer(_vrRightBuffer, src.width, src.height);

            Graphics.Blit(src, dst);

            if (useRight)
            {
                AgeThenBuildVrDrawList(_vrRightBuffer);
                DrawVrTextureList(dst, _vrRightBuffer.DrawSlots, _vrRightBuffer.DrawAlpha, _vrRightBuffer.DrawCount);
                CaptureVrBufferFrame(src, _vrRightBuffer);
            }
            else
            {
                AgeThenBuildDrawList();
                DrawVrTextureList(dst, _drawSlots, _drawAlpha, _drawCount);
                CaptureMainVrFrame(src);
            }

            if (!_vrStereoPathLogged)
            {
                _vrStereoPathLogged = true;
                LogPlugin("INFO", "vr stereo render-image path active cam="
                    + (cam != null ? cam.name : "null")
                    + " eye=" + eye
                    + " size=" + src.width + "x" + src.height);
            }
        }

        private Camera ResolveSteamCamera()
        {
            if (VR.Camera == null || VR.Camera.SteamCam == null)
                return null;

            Component steamComponent = VR.Camera.SteamCam as Component;
            return steamComponent != null ? steamComponent.GetComponent<Camera>() : null;
        }

        private static VrStereoEye ResolveVrStereoEye(Camera cam)
        {
            if (cam == null)
                return VrStereoEye.Mono;

            if (cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Left)
                return VrStereoEye.Left;
            if (cam.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right)
                return VrStereoEye.Right;

            if (cam.stereoTargetEye == StereoTargetEyeMask.Left)
                return VrStereoEye.Left;
            if (cam.stereoTargetEye == StereoTargetEyeMask.Right)
                return VrStereoEye.Right;

            string lower = (cam.name ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("left"))
                return VrStereoEye.Left;
            if (lower.Contains("right"))
                return VrStereoEye.Right;

            return VrStereoEye.Mono;
        }

        private void EnsureMainVrRenderImageSlots(int width, int height)
        {
            int slotCount = Mathf.Clamp(_cfgMaxSlots.Value, 1, 300);
            width = Mathf.Max(16, width);
            height = Mathf.Max(16, height);

            bool needRebuild = _slots == null
                || _slots.Length != slotCount
                || _rtWidth != width
                || _rtHeight != height;

            if (!needRebuild)
                return;

            ReleaseSlots();
            _slots = new RenderTexture[slotCount];
            _life = new int[slotCount];
            _slotFadeFrames = new int[slotCount];
            _slotAlphaScale = new float[slotCount];
            _slotExternal = new bool[slotCount];
            _drawSlots = new RenderTexture[slotCount];
            _drawAlpha = new float[slotCount];

            _rtWidth = width;
            _rtHeight = height;
            _writeIndex = 0;
            _frameCounter = 0;
            _drawCount = 0;
            LogPlugin("INFO", "vr main slots rebuilt: " + width + "x" + height + " slots=" + slotCount);
        }

        private void EnsureVrStereoBuffer(VrStereoSlotBuffer buffer, int width, int height)
        {
            if (buffer == null)
                return;

            int slotCount = Mathf.Clamp(_cfgMaxSlots.Value, 1, 300);
            width = Mathf.Max(16, width);
            height = Mathf.Max(16, height);

            bool needRebuild = buffer.Slots == null
                || buffer.Slots.Length != slotCount
                || buffer.Width != width
                || buffer.Height != height;

            if (!needRebuild)
                return;

            ReleaseVrStereoBuffer(buffer);
            buffer.Slots = new RenderTexture[slotCount];
            buffer.Life = new int[slotCount];
            buffer.SlotFadeFrames = new int[slotCount];
            buffer.SlotAlphaScale = new float[slotCount];
            buffer.SlotExternal = new bool[slotCount];
            buffer.DrawSlots = new RenderTexture[slotCount];
            buffer.DrawAlpha = new float[slotCount];
            buffer.Width = width;
            buffer.Height = height;

            buffer.WriteIndex = 0;
            buffer.FrameCounter = 0;
            buffer.DrawCount = 0;
            LogPlugin("INFO", "vr right slots rebuilt: " + width + "x" + height + " slots=" + slotCount);
        }

        private void AgeThenBuildVrDrawList(VrStereoSlotBuffer buffer)
        {
            if (buffer == null || buffer.Slots == null || buffer.Life == null)
                return;

            InitVrSlotMetadata(buffer);
            float tintA = Mathf.Clamp01(_cfgTintA.Value);

            for (int i = 0; i < buffer.Slots.Length; i++)
            {
                AgeSlot(buffer.Timed, buffer.Life, i);
            }

            buffer.DrawCount = 0;
            int newest = (buffer.WriteIndex - 1 + buffer.Slots.Length) % buffer.Slots.Length;
            for (int i = 0; i < buffer.Slots.Length; i++)
            {
                int slot = (newest - i + buffer.Slots.Length) % buffer.Slots.Length;
                if (buffer.Life[slot] <= 0)
                    continue;

                int fadeFrames = GetVrSlotFadeFrames(buffer, slot);
                float alphaScale = GetVrSlotAlphaScale(buffer, slot);
                float t = buffer.Timed.IsTimed(slot) ? buffer.Timed.Remaining(slot, Time.unscaledTime) : (float)buffer.Life[slot] / fadeFrames;
                t = ApplyCurve(t, _cfgFadeCurve.Value);
                float alpha = tintA * alphaScale * t;
                if (alpha <= 0.0001f)
                    continue;

                buffer.DrawSlots[buffer.DrawCount] = buffer.Slots[slot];
                buffer.DrawAlpha[buffer.DrawCount] = alpha;
                buffer.DrawCount++;
            }
        }

        private void CaptureMainVrFrame(RenderTexture src)
        {
            if (src == null || _slots == null || _slots.Length == 0)
                return;

            _frameCounter++;
            int interval = Mathf.Max(1, GetEffectiveCaptureInterval());
            if (!CaptureDue(ref _nextVmdCapture, _frameCounter, interval))
                return;

            InitWarpSlotMetadata();
            bool warpSlot = ShouldCaptureWarpAfterimage();
            if (IsBaseAfterimageCapturePausedByWarp() && !warpSlot)
                return;

            if (_slots[_writeIndex] == null)
                _slots[_writeIndex] = CreateRT(_rtWidth, _rtHeight);
            Graphics.Blit(src, _slots[_writeIndex]);
            if (warpSlot)
                MarkCurrentSlotAsWarp();
            else
                MarkCurrentSlotAsNormal();

            _writeIndex = (_writeIndex + 1) % _slots.Length;
        }

        private void CaptureVrBufferFrame(RenderTexture src, VrStereoSlotBuffer buffer)
        {
            if (src == null || buffer == null || buffer.Slots == null || buffer.Slots.Length == 0)
                return;

            buffer.FrameCounter++;
            int interval = Mathf.Max(1, GetEffectiveCaptureInterval());
            if (!CaptureDue(ref buffer.NextVmdCapture, buffer.FrameCounter, interval))
                return;

            InitVrSlotMetadata(buffer);
            bool warpSlot = ShouldCaptureWarpAfterimage();
            if (IsBaseAfterimageCapturePausedByWarp() && !warpSlot)
                return;

            if (buffer.Slots[buffer.WriteIndex] == null)
                buffer.Slots[buffer.WriteIndex] = CreateRT(buffer.Width, buffer.Height);
            Graphics.Blit(src, buffer.Slots[buffer.WriteIndex]);
            if (warpSlot)
                MarkVrSlotAsWarp(buffer);
            else
                MarkVrSlotAsNormal(buffer);

            buffer.WriteIndex = (buffer.WriteIndex + 1) % buffer.Slots.Length;
        }

        private void InitVrSlotMetadata(VrStereoSlotBuffer buffer)
        {
            if (buffer == null || buffer.Slots == null)
                return;

            int n = buffer.Slots.Length;
            buffer.Timed.Ensure(n);
            bool initialize = false;
            if (buffer.SlotFadeFrames == null || buffer.SlotFadeFrames.Length != n) { buffer.SlotFadeFrames = new int[n]; initialize = true; }
            if (buffer.SlotAlphaScale == null || buffer.SlotAlphaScale.Length != n) { buffer.SlotAlphaScale = new float[n]; initialize = true; }
            if (buffer.SlotExternal == null || buffer.SlotExternal.Length != n) { buffer.SlotExternal = new bool[n]; initialize = true; }
            if (!initialize) return;

            int fade = GetBaseFadeFrames();
            float alpha = _cfgAlphaScale != null ? Mathf.Clamp01(_cfgAlphaScale.Value) : 1f;
            for (int i = 0; i < n; i++)
            {
                if (buffer.SlotFadeFrames[i] <= 0) buffer.SlotFadeFrames[i] = fade;
                if (buffer.SlotAlphaScale[i] <= 0f) buffer.SlotAlphaScale[i] = alpha;
            }
        }

        private void MarkVrSlotAsNormal(VrStereoSlotBuffer buffer)
        {
            InitVrSlotMetadata(buffer);
            if (buffer == null || buffer.Slots == null || buffer.WriteIndex < 0 || buffer.WriteIndex >= buffer.Slots.Length)
                return;

            buffer.Life[buffer.WriteIndex] = GetBaseFadeFrames();
            buffer.SlotFadeFrames[buffer.WriteIndex] = GetBaseFadeFrames();
            MarkTimedNormal(buffer.Timed, buffer.Slots.Length, buffer.WriteIndex);
            buffer.SlotAlphaScale[buffer.WriteIndex] = _vmdApplied != null ? _vmdApplied.Alpha : Mathf.Clamp01(_cfgAlphaScale.Value);
            buffer.SlotExternal[buffer.WriteIndex] = false;
        }

        private void MarkVrSlotAsWarp(VrStereoSlotBuffer buffer)
        {
            InitVrSlotMetadata(buffer);
            if (buffer == null || buffer.Slots == null || buffer.WriteIndex < 0 || buffer.WriteIndex >= buffer.Slots.Length)
                return;

            buffer.Timed.Mark(buffer.WriteIndex, 0, 0);
            int fade = GetWarpAfterimageFadeFrames();
            buffer.Life[buffer.WriteIndex] = Mathf.Max(1, fade);
            buffer.SlotFadeFrames[buffer.WriteIndex] = Mathf.Max(1, fade);
            buffer.SlotAlphaScale[buffer.WriteIndex] = GetWarpAfterimageAlphaScale();
            buffer.SlotExternal[buffer.WriteIndex] = true;
        }

        private float GetVrSlotAlphaScale(VrStereoSlotBuffer buffer, int slot)
        {
            // AgeThenBuildVrDrawList initializes the metadata once before reading slots.
            if (buffer == null || buffer.SlotAlphaScale == null || slot < 0 || slot >= buffer.SlotAlphaScale.Length)
                return Mathf.Clamp01(_cfgAlphaScale.Value);

            float a = buffer.SlotAlphaScale[slot];
            if (!buffer.SlotExternal[slot])
                a *= GetBaseAfterimageAlphaMultiplierByWarp();
            return Mathf.Clamp01(a);
        }

        private int GetVrSlotFadeFrames(VrStereoSlotBuffer buffer, int slot)
        {
            // AgeThenBuildVrDrawList initializes the metadata once before reading slots.
            if (buffer == null || buffer.SlotFadeFrames == null || slot < 0 || slot >= buffer.SlotFadeFrames.Length)
                return GetBaseFadeFrames();

            return Mathf.Max(1, buffer.SlotFadeFrames[slot]);
        }

        private void DrawVrTextureList(RenderTexture target, RenderTexture[] slots, float[] alphaList, int count)
        {
            if (target == null || slots == null || alphaList == null || count <= 0)
                return;

            float r = Mathf.Clamp01(_cfgTintR.Value);
            float g = Mathf.Clamp01(_cfgTintG.Value);
            float b = Mathf.Clamp01(_cfgTintB.Value);
            Rect rect = new Rect(0f, 0f, target.width, target.height);
            RenderTexture previous = RenderTexture.active;

            try
            {
                RenderTexture.active = target;
                GL.PushMatrix();
                GL.LoadPixelMatrix(0f, target.width, target.height, 0f);
                for (int i = 0; i < count; i++)
                {
                    if (slots[i] == null)
                        continue;

                    float alpha = alphaList[i];
                    if (alpha <= 0.0001f)
                        continue;

                    Graphics.DrawTexture(rect, slots[i], new Rect(0f, 0f, 1f, 1f), 0, 0, 0, 0,
                        new Color(r, g, b, alpha));
                }
            }
            finally
            {
                GL.PopMatrix();
                RenderTexture.active = previous;
            }
        }

        private void ClearVrStereoSlots()
        {
            _vrRightBuffer.Clear();
        }

        private void ReleaseVrStereoSlots()
        {
            ReleaseVrStereoBuffer(_vrRightBuffer);
            _vrStereoPathLogged = false;
        }

        private void ReleaseVrStereoBuffer(VrStereoSlotBuffer buffer)
        {
            if (buffer == null)
                return;

            if (buffer.Slots != null)
            {
                for (int i = 0; i < buffer.Slots.Length; i++)
                {
                    if (buffer.Slots[i] == null)
                        continue;

                    buffer.Slots[i].Release();
                    Destroy(buffer.Slots[i]);
                    buffer.Slots[i] = null;
                }
            }

            buffer.Slots = null;
            buffer.Life = null;
            buffer.SlotFadeFrames = null;
            buffer.SlotAlphaScale = null;
            buffer.SlotExternal = null;
            buffer.DrawSlots = null;
            buffer.DrawAlpha = null;
            buffer.DrawCount = 0;
            buffer.WriteIndex = 0;
            buffer.FrameCounter = 0;
            buffer.Width = 0;
            buffer.Height = 0;
        }
    }
}
