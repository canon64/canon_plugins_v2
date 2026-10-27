using MainGirlHipHijack.Runtime;
using UnityEngine;

namespace MainGirlHipHijack.Services
{
    /// <summary>
    /// ObjectComposerのTransformをBodyIK操作点へ接続する責務だけを持つ。
    /// </summary>
    internal sealed class ComposerFollowController
    {
        internal void UpdateTargets(
            BodyIkGizmoSettings settings,
            BodyIkOperationPointState[] operationPoints,
            ObjectComposerGateway gateway,
            int operationPointCount)
        {
            if (settings == null || operationPoints == null || gateway == null)
                return;
            if (settings.FollowComposerEnabled == null || settings.FollowComposerObjectIds == null)
                return;
            if (!gateway.IsAvailable)
                return;

            int length = Mathf.Min(settings.FollowComposerEnabled.Length, operationPointCount);
            for (int i = 0; i < length; i++)
            {
                if (!settings.FollowComposerEnabled[i])
                    continue;

                string id = settings.FollowComposerObjectIds[i];
                if (string.IsNullOrEmpty(id))
                    continue;

                BodyIkOperationPointState state = operationPoints[i];
                if (state == null || !state.Running || state.Proxy == null || state.GizmoDragging)
                    continue;

                if (gateway.TryGetTransform(id, out Transform transform))
                {
                    state.FollowBone = transform;
                    state.FollowBonePositionOffset = Vector3.zero;
                    state.FollowBoneRotationOffset = Quaternion.identity;
                }
            }
        }
    }
}
