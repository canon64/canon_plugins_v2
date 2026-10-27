using System.Collections.Generic;
using System.Text;
using MainGirlHipHijack.Adapters;
using UnityEngine;
using Valve.VR;

namespace MainGirlHipHijack.Services
{
    internal sealed class SteamVrPoseProvider
    {
        private const float DeviceScanInterval = 3f;

        private readonly TrackedDevicePose_t[] _renderPoses =
            new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly TrackedDevicePose_t[] _gamePoses =
            new TrackedDevicePose_t[OpenVR.k_unMaxTrackedDeviceCount];
        private readonly Dictionary<string, uint> _serialToIndex = new Dictionary<string, uint>();
        private readonly Dictionary<uint, string> _indexToSerial = new Dictionary<uint, string>();
        private readonly SteamVrCoordinateConverter _coordinateConverter;
        private float _nextDeviceScanTime;

        internal SteamVrPoseProvider(SteamVrCoordinateConverter coordinateConverter)
        {
            _coordinateConverter = coordinateConverter;
        }

        internal List<(string serial, uint index)> GetConnectedTrackers()
        {
            var result = new List<(string, uint)>();
            CVRSystem system = OpenVR.System;
            if (system == null) return result;

            for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
            {
                if (system.GetTrackedDeviceClass(i) != ETrackedDeviceClass.GenericTracker
                    || !system.IsTrackedDeviceConnected(i))
                    continue;
                result.Add((GetDeviceSerial(system, i), i));
            }
            return result;
        }

        internal uint GetHmdDeviceIndex()
        {
            CVRSystem system = OpenVR.System;
            if (system == null) return uint.MaxValue;

            for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
            {
                if (system.GetTrackedDeviceClass(i) == ETrackedDeviceClass.HMD
                    && system.IsTrackedDeviceConnected(i))
                    return i;
            }
            return uint.MaxValue;
        }

        internal uint ResolveDeviceIndex(string serial)
        {
            if (string.IsNullOrEmpty(serial)) return uint.MaxValue;
            return _serialToIndex.TryGetValue(serial, out uint index) ? index : uint.MaxValue;
        }

        internal void TickDeviceScan(float unscaledTime)
        {
            if (unscaledTime < _nextDeviceScanTime) return;
            _nextDeviceScanTime = unscaledTime + DeviceScanInterval;

            CVRSystem system = OpenVR.System;
            if (system == null) return;

            _serialToIndex.Clear();
            _indexToSerial.Clear();
            for (uint i = 0; i < OpenVR.k_unMaxTrackedDeviceCount; i++)
            {
                ETrackedDeviceClass deviceClass = system.GetTrackedDeviceClass(i);
                if (deviceClass == ETrackedDeviceClass.Invalid || !system.IsTrackedDeviceConnected(i))
                    continue;

                string serial = GetDeviceSerial(system, i);
                if (string.IsNullOrEmpty(serial)) continue;
                _serialToIndex[serial] = i;
                _indexToSerial[i] = serial;
            }
        }

        internal void FetchPoses()
        {
            CVRCompositor compositor = OpenVR.Compositor;
            if (compositor != null)
                compositor.GetLastPoses(_renderPoses, _gamePoses);
        }

        internal bool TryGetDevicePose(uint deviceIndex, out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;
            if (deviceIndex >= OpenVR.k_unMaxTrackedDeviceCount) return false;

            ref TrackedDevicePose_t pose = ref _renderPoses[deviceIndex];
            if (!pose.bPoseIsValid || !pose.bDeviceIsConnected) return false;

            Matrix4x4 matrix = _coordinateConverter.ToUnityMatrix(pose.mDeviceToAbsoluteTracking);
            worldPosition = matrix.MultiplyPoint3x4(Vector3.zero);
            worldRotation = matrix.rotation;
            return true;
        }

        private static string GetDeviceSerial(CVRSystem system, uint index)
        {
            var buffer = new StringBuilder(64);
            var error = ETrackedPropertyError.TrackedProp_Success;
            system.GetStringTrackedDeviceProperty(
                index,
                ETrackedDeviceProperty.Prop_SerialNumber_String,
                buffer,
                64,
                ref error);
            return error == ETrackedPropertyError.TrackedProp_Success ? buffer.ToString() : string.Empty;
        }
    }
}
