using UnityEngine;
using Valve.VR;

namespace MainGirlHipHijack.Adapters
{
    internal sealed class SteamVrCoordinateConverter
    {
        internal Matrix4x4 ToUnityMatrix(HmdMatrix34_t source)
        {
            return new Matrix4x4
            {
                m00 = source.m0,  m01 = source.m1,  m02 = -source.m2,  m03 = source.m3,
                m10 = source.m4,  m11 = source.m5,  m12 = -source.m6,  m13 = source.m7,
                m20 = -source.m8, m21 = -source.m9, m22 = source.m10, m23 = -source.m11,
                m30 = 0f,         m31 = 0f,         m32 = 0f,          m33 = 1f
            };
        }
    }
}
