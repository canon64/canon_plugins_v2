using System.Collections.Generic;
using UnityEngine;

namespace MainGameAgentCamera
{
    /// <summary>
    /// 「指示された角度」を「実際に成立する構図」へ詰める係。
    ///
    /// 撮ってから目視で気づくのでは一発で決まらない。撮る前に2つだけ計算で潰す:
    ///   1. 収まり  … 写したい点が本当に画面へ入る距離まで引く
    ///   2. 遮蔽    … 手前に別のキャラや物があるなら、近い角度で抜けている方向を探す
    /// どちらも当てずっぽうではなく、視錐台と Raycast で決める。
    /// </summary>
    internal static class ShotSolver
    {
        internal sealed class Result
        {
            internal Vector3 CameraPosition;
            internal float Yaw;
            internal float Pitch;
            internal float Distance;
            internal bool DistanceAdjusted;
            internal bool YawAdjusted;
            internal bool StillBlocked;
            internal string Blocker = string.Empty;
        }

        /// <summary>
        /// 写したい点が画面内に収まる最小距離を求める。
        ///
        /// カメラ空間へ落として視錐台の条件を解くだけなので、
        /// 試し撮りの繰り返しは要らない。margin は画面端に残す余白(0〜0.45)。
        /// </summary>
        internal static float SolveFitDistance(
            Vector3 lookAt, Vector3 forward, Vector3 up, Vector3 right,
            IList<Vector3> mustSee, float fovDegrees, float aspect, float margin, float minimum)
        {
            if (mustSee == null || mustSee.Count == 0) return minimum;

            float safe = 1f - Mathf.Clamp(margin, 0f, 0.45f);
            float tanV = Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(fovDegrees, 5f, 120f) * 0.5f) * safe;
            float tanH = tanV * Mathf.Max(0.1f, aspect);

            float required = minimum;
            for (int i = 0; i < mustSee.Count; i++)
            {
                Vector3 o = mustSee[i] - lookAt;
                float depth = Vector3.Dot(o, forward);
                float x = Mathf.Abs(Vector3.Dot(o, right));
                float y = Mathf.Abs(Vector3.Dot(o, up));

                // カメラは lookAt から distance だけ後ろ。その点の奥行きは distance + depth。
                required = Mathf.Max(required, x / tanH - depth);
                required = Mathf.Max(required, y / tanV - depth);
            }

            return Mathf.Clamp(required, minimum, 30f);
        }

        /// <summary>
        /// 被写体とカメラの間に他人や物が挟まっていないか。
        /// 被写体自身の体は当然当たるので除外する。
        /// </summary>
        internal static bool IsBlocked(Transform subjectRoot, Vector3 lookAt, Vector3 cameraPosition, out string blockerName)
        {
            blockerName = string.Empty;
            Vector3 direction = cameraPosition - lookAt;
            float distance = direction.magnitude;
            if (distance <= 0.01f)
            {
                blockerName = "camera too close";
                return true;
            }

            RaycastHit[] hits = Physics.RaycastAll(lookAt, direction / distance, distance, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
            {
                Transform hit = hits[i].transform;
                if (hit == null) continue;
                if (subjectRoot != null && hit.IsChildOf(subjectRoot)) continue;
                blockerName = hit.name;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 指定角度を起点に、収まりと遮蔽を満たす構図を決める。
        /// 遮蔽されていたら yaw を近い順にずらして抜け道を探す。
        /// 見つからなければ元の角度のまま返し、遮蔽されている事実を報告する。
        /// </summary>
        internal static Result Solve(
            SubjectResolver subject,
            Vector3 frameUp, Vector3 frameFront,
            Vector3 lookAt,
            float yaw, float pitch, float distance, float fov, float aspect,
            IList<Vector3> mustSee, float margin,
            bool autoFit, bool avoidOccluders)
        {
            Result result = new Result { Yaw = yaw, Pitch = pitch, Distance = distance };
            Transform root = subject.Female != null ? subject.Female.transform : null;

            float best = distance;
            if (autoFit && mustSee != null && mustSee.Count > 0)
            {
                Vector3 probe = CameraRig.OrbitPosition(lookAt, frameUp, frameFront, yaw, pitch, distance);
                Vector3 forward = (lookAt - probe).normalized;
                Vector3 right = Vector3.Cross(frameUp, forward).normalized;
                if (right.sqrMagnitude < 0.0001f) right = subject.BodyRight;
                Vector3 up = Vector3.Cross(forward, right).normalized;

                float fit = SolveFitDistance(lookAt, forward, up, right, mustSee, fov, aspect, margin, 0.15f);
                if (fit > distance + 0.01f)
                {
                    best = fit;
                    result.DistanceAdjusted = true;
                }
            }

            result.Distance = best;
            result.CameraPosition = CameraRig.OrbitPosition(lookAt, frameUp, frameFront, yaw, pitch, best);

            if (!avoidOccluders) return result;

            string blocker;
            if (!IsBlocked(root, lookAt, result.CameraPosition, out blocker)) return result;

            // 塞がれている。頼まれた角度に近い順に振って、抜けている方向を探す。
            for (int step = 10; step <= 180; step += 10)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    float candidateYaw = yaw + step * sign;
                    Vector3 candidate = CameraRig.OrbitPosition(
                        lookAt, frameUp, frameFront, candidateYaw, pitch, best);

                    string ignored;
                    if (IsBlocked(root, lookAt, candidate, out ignored)) continue;

                    result.Yaw = candidateYaw;
                    result.CameraPosition = candidate;
                    result.YawAdjusted = true;
                    return result;
                }
            }

            // 全周塞がれている。元の角度で返し、塞がれている事実だけ伝える。
            result.StillBlocked = true;
            result.Blocker = blocker;
            return result;
        }
    }
}
