using UnityEngine;

namespace MainGirlHipHijack
{
    /// <summary>
    /// BodyIK診断ログの間引きと前フレーム比較値を所有する。
    /// </summary>
    internal sealed class BodyIkDiagnosticState
    {
        internal float NextLogTime { get; set; }
        internal int LastFrame { get; set; } = -1;
        internal int LastIndex { get; set; } = -1;
        internal Vector3 LastPostBonePosition { get; set; }
        internal Vector3 LastPostProxyPosition { get; set; }
        internal bool HasLastPost { get; set; }

        internal void Reset()
        {
            NextLogTime = 0f;
            LastFrame = -1;
            LastIndex = -1;
            LastPostBonePosition = Vector3.zero;
            LastPostProxyPosition = Vector3.zero;
            HasLastPost = false;
        }
    }
}
