using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace MainGameAgentCamera
{
    /// <summary>
    /// 「誰を撮るか」と「その体の向き」を求める。
    ///
    /// 構図指定を体基準にするための土台。ワールド座標の東西南北で角度を言われても
    /// 被写体が寝ていたら意味を成さないので、体の上方向・右方向・正面を毎回作り直し、
    /// yaw/pitch はその体基準の角度として扱う。
    /// </summary>
    internal sealed class SubjectResolver
    {
        private static readonly string[] HipNames = { "cf_j_hips", "cf_J_Hips", "cf_j_waist01", "cf_J_Waist01" };
        private static readonly string[] HeadNames = { "cf_j_head", "cf_J_Head" };
        private static readonly string[] NeckNames = { "cf_j_neck", "cf_J_Neck" };
        private static readonly string[] ChestNames = { "cf_j_spine03", "cf_J_Spine03", "cf_j_spine02", "cf_J_Spine02" };
        private static readonly string[] LeftShoulderNames = { "cf_j_shoulder_L", "cf_J_Shoulder_L", "cf_j_arm00_L" };
        private static readonly string[] RightShoulderNames = { "cf_j_shoulder_R", "cf_J_Shoulder_R", "cf_j_arm00_R" };
        private static readonly string[] LeftThighNames = { "cf_j_thigh00_L", "cf_J_Thigh00_L", "cf_J_Thigh_L" };
        private static readonly string[] RightThighNames = { "cf_j_thigh00_R", "cf_J_Thigh00_R", "cf_J_Thigh_R" };
        // 胸は背骨ではなくバストのボーンを使う。spine03 だと寄ったとき被写体がずれる。
        private static readonly string[] LeftBustNames = { "cf_j_bust01_L", "cf_j_bust00_L", "cf_d_bust00_L" };
        private static readonly string[] RightBustNames = { "cf_j_bust01_R", "cf_j_bust00_R", "cf_d_bust00_R" };
        private static readonly string[] LeftButtNames = { "cf_j_siri_L", "cf_J_Siri_L" };
        private static readonly string[] RightButtNames = { "cf_j_siri_R", "cf_J_Siri_R" };
        private static readonly string[] WaistNames = { "cf_j_waist02", "cf_j_waist01", "cf_J_Waist01" };
        private static readonly string[] LeftKneeNames = { "cf_j_leg01_L", "cf_J_LegLow01_L" };
        private static readonly string[] RightKneeNames = { "cf_j_leg01_R", "cf_J_LegLow01_R" };
        private static readonly string[] LeftFootNames = { "cf_j_leg03_L", "cf_J_Foot01_L", "cf_j_foot_L" };
        private static readonly string[] RightFootNames = { "cf_j_leg03_R", "cf_J_Foot01_R", "cf_j_foot_R" };
        private static readonly string[] LeftHandNames = { "cf_j_hand_L", "cf_J_Hand_L" };
        private static readonly string[] RightHandNames = { "cf_j_hand_R", "cf_J_Hand_R" };
        private static readonly string[] LeftElbowNames = { "cf_j_forearm01_L", "cf_J_ArmLow01_L" };
        private static readonly string[] RightElbowNames = { "cf_j_forearm01_R", "cf_J_ArmLow01_R" };

        private static FieldInfo _lstFemaleField;

        internal ChaControl Female;
        internal Transform Head;
        internal Transform Neck;
        internal Transform Chest;
        internal Transform Hips;
        internal Transform Waist;
        internal Vector3 Crotch;
        internal bool HasBust;
        internal Vector3 Bust;
        internal bool HasButt;
        internal Vector3 Butt;
        internal Vector3 BodyUp;
        internal Vector3 BodyRight;
        internal Vector3 BodyFront;
        /// <summary>顔が向いている方向。うつむき・仰向けでも顔を正面から撮るために使う。</summary>
        internal Vector3 HeadFront;
        internal Vector3 HeadUp;
        internal float Height;
        internal Transform[] Bones;

        internal Transform LeftKnee, RightKnee, LeftFoot, RightFoot;
        internal Transform LeftHand, RightHand, LeftElbow, RightElbow;
        internal Transform LeftShoulder, RightShoulder;

        /// <summary>体全体を包む箱（ワールド）。姿勢に関係なく「どこまでが体か」を示す。</summary>
        internal Vector3 BoundsMin;
        internal Vector3 BoundsMax;
        internal Vector3 BoundsCenter;

        internal static bool TryResolve(int femaleIndex, out SubjectResolver subject, out string reason)
        {
            subject = null;
            reason = string.Empty;

            HSceneProc proc = UnityEngine.Object.FindObjectOfType<HSceneProc>();
            if (proc == null)
            {
                reason = "HSceneProc not found (Hシーン中ではない)";
                return false;
            }

            if (_lstFemaleField == null)
            {
                _lstFemaleField = typeof(HSceneProc).GetField(
                    "lstFemale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            IList females = _lstFemaleField != null ? _lstFemaleField.GetValue(proc) as IList : null;
            if (females == null || femaleIndex < 0 || femaleIndex >= females.Count)
            {
                reason = "female index out of range: " + femaleIndex;
                return false;
            }

            ChaControl female = females[femaleIndex] as ChaControl;
            if (female == null)
            {
                reason = "female is null: " + femaleIndex;
                return false;
            }

            Transform root = female.animBody != null ? female.animBody.transform : female.transform;
            Transform[] bones = root.GetComponentsInChildren<Transform>(true);
            Transform hips = FindBone(bones, HipNames);
            Transform head = FindBone(bones, HeadNames);
            if (hips == null || head == null)
            {
                reason = "required bones missing (hips/head)";
                return false;
            }

            Transform left = FindBone(bones, LeftShoulderNames);
            Transform right = FindBone(bones, RightShoulderNames);
            Transform leftThigh = FindBone(bones, LeftThighNames);
            Transform rightThigh = FindBone(bones, RightThighNames);
            Transform leftBust = FindBone(bones, LeftBustNames);
            Transform rightBust = FindBone(bones, RightBustNames);
            Transform leftButt = FindBone(bones, LeftButtNames);
            Transform rightButt = FindBone(bones, RightButtNames);

            Vector3 axis = head.position - hips.position;
            float length = Mathf.Max(axis.magnitude, 0.2f);
            Vector3 up = axis.normalized;

            Vector3 crotch = leftThigh != null && rightThigh != null
                ? (leftThigh.position + rightThigh.position) * 0.5f
                : hips.position - up * (length * 0.12f);

            Vector3 bodyRight = left != null && right != null
                ? right.position - left.position
                : female.transform.right;
            bodyRight -= up * Vector3.Dot(bodyRight, up);
            if (bodyRight.sqrMagnitude < 0.0001f)
            {
                bodyRight = Vector3.Cross(up, female.transform.forward);
                if (bodyRight.sqrMagnitude < 0.0001f) bodyRight = Vector3.Cross(up, Vector3.up);
                if (bodyRight.sqrMagnitude < 0.0001f) bodyRight = Vector3.Cross(up, Vector3.right);
            }
            bodyRight.Normalize();

            subject = new SubjectResolver
            {
                Female = female,
                Head = head,
                Neck = FindBone(bones, NeckNames),
                Chest = FindBone(bones, ChestNames),
                Hips = hips,
                Waist = FindBone(bones, WaistNames),
                HasBust = leftBust != null && rightBust != null,
                Bust = leftBust != null && rightBust != null
                    ? (leftBust.position + rightBust.position) * 0.5f
                    : Vector3.zero,
                HasButt = leftButt != null && rightButt != null,
                Butt = leftButt != null && rightButt != null
                    ? (leftButt.position + rightButt.position) * 0.5f
                    : Vector3.zero,
                Crotch = crotch,
                BodyUp = up,
                BodyRight = bodyRight,
                BodyFront = Vector3.Cross(bodyRight, up).normalized,
                HeadFront = head.forward,
                HeadUp = head.up,
                Height = Vector3.Distance(head.position, crotch),
                Bones = bones,
                LeftShoulder = left,
                RightShoulder = right,
                LeftKnee = FindBone(bones, LeftKneeNames),
                RightKnee = FindBone(bones, RightKneeNames),
                LeftFoot = FindBone(bones, LeftFootNames),
                RightFoot = FindBone(bones, RightFootNames),
                LeftHand = FindBone(bones, LeftHandNames),
                RightHand = FindBone(bones, RightHandNames),
                LeftElbow = FindBone(bones, LeftElbowNames),
                RightElbow = FindBone(bones, RightElbowNames)
            };

            subject.ComputeBounds();
            return true;
        }

        /// <summary>
        /// 体の要所を全部含む箱をワールドで求める。
        /// 「頭から股まで」という決め打ちでは、寝た姿勢や手足を伸ばした姿勢で
        /// 体の端がはみ出す。実際のボーン位置から取るのが唯一確実。
        /// </summary>
        internal void ComputeBounds()
        {
            Transform[] key =
            {
                Head, Neck, Chest, Hips, Waist,
                LeftShoulder, RightShoulder,
                LeftElbow, RightElbow, LeftHand, RightHand,
                LeftKnee, RightKnee, LeftFoot, RightFoot
            };

            bool any = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            for (int i = 0; i < key.Length; i++)
            {
                if (key[i] == null) continue;
                Vector3 p = key[i].position;
                if (!any) { min = p; max = p; any = true; continue; }
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            if (!any) { min = Head.position; max = Head.position; }

            // 頭は骨の位置より上に、足は下に肉がある。少しだけ膨らませる。
            Vector3 pad = new Vector3(0.06f, 0.06f, 0.06f);
            BoundsMin = min - pad;
            BoundsMax = max + pad;
            BoundsCenter = (BoundsMin + BoundsMax) * 0.5f;
        }

        /// <summary>箱の8隅。収まり計算にはこれを使う。</summary>
        internal Vector3[] BoundsCorners()
        {
            Vector3 a = BoundsMin, b = BoundsMax;
            return new[]
            {
                new Vector3(a.x, a.y, a.z), new Vector3(b.x, a.y, a.z),
                new Vector3(a.x, b.y, a.z), new Vector3(b.x, b.y, a.z),
                new Vector3(a.x, a.y, b.z), new Vector3(b.x, a.y, b.z),
                new Vector3(a.x, b.y, b.z), new Vector3(b.x, b.y, b.z)
            };
        }

        /// <summary>構図の名前つき注視点。ここに無い名前は生のボーン名として探す。</summary>
        internal static readonly string[] TargetNames =
        {
            "face", "head", "neck", "bust", "chest", "belly", "waist", "hips", "butt", "crotch", "upper", "whole"
        };

        /// <summary>
        /// 部位ごとの既定の撮り方。注視点だけでなく「どの角度から、どれだけ寄って、どんな画角で」
        /// までを持つ。指定が無いときに真正面・目線の高さで撮ると、どの部位でも一番退屈な絵になるため。
        /// 呼び出し側が yaw/pitch/roll を明示すればそちらが勝つ。
        /// </summary>
        internal sealed class TargetShot
        {
            internal Vector3 LookAt;
            internal float Distance;
            internal float Fov;
            internal float Yaw;
            internal float Pitch;
            internal float Roll;
            /// <summary>必ず画面に入れたい点。これを使って距離を詰める。</summary>
            internal Vector3[] MustSee;
            /// <summary>画面端に残す余白の割合。</summary>
            internal float Margin;
            /// <summary>顔の向きを基準に角度を取るか。頭まわりの部位は既定でこちら。</summary>
            internal bool UseHeadFrame;
        }

        /// <summary>注視点のまわりに、収まりを判定するための箱の代表点を作る。</summary>
        private Vector3[] Box(Vector3 center, float halfWidth, float halfUp, float halfDown)
        {
            return new[]
            {
                center + BodyRight * halfWidth,
                center - BodyRight * halfWidth,
                center + BodyUp * halfUp,
                center - BodyUp * halfDown
            };
        }

        /// <summary>
        /// 部位ごとの撮り方を決める。数値は JSON の設定から取る。
        /// ここに数値を書くと、変更のたびにビルドとゲーム再起動が要る。
        /// </summary>
        internal bool TryResolveTarget(
            string name, AgentCameraSettings settings, out TargetShot shot, out string reason)
        {
            reason = string.Empty;
            shot = new TargetShot { Distance = 1.2f, Fov = 35f, Margin = 0.06f };

            string key = (name ?? string.Empty).Trim();
            if (key.Length == 0)
                key = settings != null ? (settings.defaultTarget ?? "whole") : "whole";
            key = key.ToLowerInvariant();
            if (key == "breast") key = "bust";
            if (key == "siri") key = "butt";

            // 全身は箱そのものを使う。部位ごとの箱の指定は要らない。
            if (key == "whole")
            {
                float margin = settings != null ? settings.wholeMargin : 0.03f;
                shot.LookAt = BoundsCenter;
                shot.Fov = settings != null ? settings.wholeFov : 40f;
                shot.Yaw = settings != null ? settings.wholeYaw : 25f;
                shot.Pitch = settings != null ? settings.wholePitch : 5f;
                shot.Distance = FitDistance((BoundsMax - BoundsMin).magnitude * 0.6f, shot.Fov);
                shot.MustSee = BoundsCorners();
                shot.Margin = margin;
                return true;
            }

            Vector3 anchor;
            if (!TryResolveAnchor(key, out anchor, out reason))
                return false;

            TargetTuning tuning = settings != null ? settings.Find(key) : null;
            if (tuning == null)
            {
                // 設定に無い部位（生のボーン名など）。寄りすぎない無難な値で撮る。
                shot.LookAt = anchor;
                shot.Yaw = 25f;
                shot.Pitch = 10f;
                shot.Distance = 0.8f;
                shot.Fov = 35f;
                shot.Margin = 0.06f;
                shot.MustSee = Box(anchor, 0.15f, 0.15f, 0.15f);
                return true;
            }

            shot.LookAt = anchor + BodyUp * tuning.lookAtRaise;
            shot.Yaw = tuning.yaw;
            shot.Pitch = tuning.pitch;
            shot.Roll = tuning.roll;
            shot.Distance = tuning.distance;
            shot.Fov = tuning.fov;
            shot.Margin = tuning.margin;
            shot.UseHeadFrame = tuning.useHeadFrame;
            shot.MustSee = Box(shot.LookAt, tuning.boxWidth, tuning.boxUp, tuning.boxDown);
            return true;
        }

        /// <summary>部位名から注視点のワールド座標を求める。</summary>
        private bool TryResolveAnchor(string key, out Vector3 anchor, out string reason)
        {
            reason = string.Empty;
            float h = Mathf.Max(0.2f, Height);

            switch (key)
            {
                case "face":
                case "head":
                    anchor = Head.position;
                    return true;
                case "neck":
                    anchor = Neck != null ? Neck.position : Head.position - BodyUp * (h * 0.12f);
                    return true;
                case "bust":
                    anchor = HasBust ? Bust : Vector3.Lerp(Hips.position, Head.position, 0.7f);
                    return true;
                case "chest":
                    anchor = Chest != null ? Chest.position : Vector3.Lerp(Hips.position, Head.position, 0.65f);
                    return true;
                case "waist":
                    anchor = Waist != null ? Waist.position : Vector3.Lerp(Hips.position, Head.position, 0.25f);
                    return true;
                case "belly":
                    anchor = Vector3.Lerp(Hips.position, Head.position, 0.35f);
                    return true;
                case "hips":
                    anchor = Hips.position;
                    return true;
                case "butt":
                    anchor = HasButt ? Butt : Hips.position - BodyFront * (h * 0.08f);
                    return true;
                case "crotch":
                    anchor = Crotch;
                    return true;
                case "upper":
                    anchor = Vector3.Lerp(Hips.position, Head.position, 0.6f);
                    return true;
            }

            Transform bone = FindBoneByName(Bones, key);
            if (bone == null)
            {
                anchor = Vector3.zero;
                reason = "unknown target: " + key;
                return false;
            }

            anchor = bone.position;
            return true;
        }

        /// <summary>指定の高さが画面に収まる距離。</summary>
        private static float FitDistance(float subjectHeight, float fovDegrees)
        {
            float half = Mathf.Max(0.05f, subjectHeight * 0.5f);
            float tan = Mathf.Tan(Mathf.Deg2Rad * Mathf.Clamp(fovDegrees, 5f, 120f) * 0.5f);
            return Mathf.Clamp(half / Mathf.Max(0.01f, tan), 0.2f, 12f);
        }

        private static Transform FindBone(Transform[] bones, string[] names)
        {
            for (int n = 0; n < names.Length; n++)
            {
                Transform found = FindBoneByName(bones, names[n]);
                if (found != null) return found;
            }
            return null;
        }

        private static Transform FindBoneByName(Transform[] bones, string name)
        {
            if (bones == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null && string.Equals(bones[i].name, name, StringComparison.OrdinalIgnoreCase))
                    return bones[i];
            }
            return null;
        }
    }
}
