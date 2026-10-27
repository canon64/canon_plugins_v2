using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace MainGamePortraitCapture
{
    public sealed partial class Plugin
    {
        private static readonly string[] HipNames = { "cf_j_hips", "cf_J_Hips", "cf_j_waist01", "cf_J_Waist01" };
        private static readonly string[] HeadNames = { "cf_j_head", "cf_J_Head" };
        private static readonly string[] LeftShoulderNames = { "cf_j_shoulder_L", "cf_J_Shoulder_L", "cf_j_arm00_L", "cf_J_Arm00_L" };
        private static readonly string[] RightShoulderNames = { "cf_j_shoulder_R", "cf_J_Shoulder_R", "cf_j_arm00_R", "cf_J_Arm00_R" };
        private static readonly string[] LeftThighNames = { "cf_j_thigh00_L", "cf_J_Thigh00_L", "cf_J_Thigh_L" };
        private static readonly string[] RightThighNames = { "cf_j_thigh00_R", "cf_J_Thigh00_R", "cf_J_Thigh_R" };
        private static FieldInfo _lstFemaleField;

        private sealed class SubjectPoints
        {
            internal ChaControl Female;
            internal Transform Head;
            internal Vector3 Center;
            internal Vector3[] FitPoints;
            internal Vector3 BodyUp;
            internal Vector3 BodyRight;
            internal Vector3 CameraOut;
            internal Vector3 HeadForward;
            internal Vector3 HipForward;
        }

        private bool TryResolveSubject(out SubjectPoints subject, out string reason)
        {
            subject = null;
            reason = string.Empty;
            HSceneProc proc = UnityEngine.Object.FindObjectOfType<HSceneProc>();
            if (proc == null) { reason = "HSceneProc not found"; return false; }
            if (_lstFemaleField == null) _lstFemaleField = typeof(HSceneProc).GetField("lstFemale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            IList females = _lstFemaleField != null ? _lstFemaleField.GetValue(proc) as IList : null;
            int index = _cfgTargetFemaleIndex.Value;
            if (females == null || index < 0 || index >= females.Count) { reason = "female index not found: " + index; return false; }
            ChaControl female = females[index] as ChaControl;
            if (female == null) { reason = "female is null: " + index; return false; }

            Transform root = female.animBody != null ? female.animBody.transform : female.transform;
            Transform[] bones = root.GetComponentsInChildren<Transform>(true);
            Transform hips = FindBone(bones, HipNames);
            Transform head = FindBone(bones, HeadNames);
            Transform left = FindBone(bones, LeftShoulderNames);
            Transform right = FindBone(bones, RightShoulderNames);
            Transform leftThigh = FindBone(bones, LeftThighNames);
            Transform rightThigh = FindBone(bones, RightThighNames);
            if (hips == null || head == null) { reason = "required bones missing hips=" + NameOf(hips) + " head=" + NameOf(head); return false; }

            Vector3 axis = head.position - hips.position;
            float length = Mathf.Max(axis.magnitude, 0.2f);
            Vector3 up = axis.normalized;
            Vector3 headTop = head.position + up * (length * Mathf.Max(0f, _cfgHeadUpperMargin.Value));
            Vector3 crotchPosition;
            if (leftThigh != null && rightThigh != null)
                crotchPosition = (leftThigh.position + rightThigh.position) * 0.5f;
            else
                crotchPosition = hips.position - up * (length * 0.12f);
            Vector3 crotchBottom = crotchPosition - up * (length * Mathf.Max(0f, _cfgCrotchLowerMargin.Value));
            Vector3 center = (headTop + crotchBottom) * 0.5f;

            // Build a pose-local frame. It follows the torso in standing, sitting,
            // lying and rolled poses instead of inheriting the current camera angle.
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

            Vector3 cameraOut = Vector3.Cross(bodyRight, up).normalized;

            subject = new SubjectPoints
            {
                Female = female,
                Head = head,
                Center = center,
                FitPoints = new[]
                {
                    headTop,
                    crotchBottom,
                    left != null ? left.position : head.position,
                    right != null ? right.position : head.position,
                    leftThigh != null ? leftThigh.position : crotchPosition,
                    rightThigh != null ? rightThigh.position : crotchPosition
                },
                BodyUp = up,
                BodyRight = bodyRight,
                CameraOut = cameraOut,
                HeadForward = head.forward,
                HipForward = hips.forward
            };
            LogDebug("bones hips=" + hips.name + " head=" + head.name + " left=" + NameOf(left) + " right=" + NameOf(right));
            LogDebug("crotchSource=thigh midpoint leftThigh=" + NameOf(leftThigh) + " rightThigh=" + NameOf(rightThigh) + " headMargin=" + _cfgHeadUpperMargin.Value.ToString("0.00") + " lowerMargin=" + _cfgCrotchLowerMargin.Value.ToString("0.00"));
            LogDebug("body frame up=" + up + " right=" + bodyRight + " cameraOut=" + cameraOut);
            return true;
        }

        private static Transform FindBone(Transform[] bones, string[] names)
        {
            for (int n = 0; n < names.Length; n++)
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i] != null && string.Equals(bones[i].name, names[n], StringComparison.Ordinal)) return bones[i];
            return null;
        }

        private static string NameOf(Transform value)
        {
            return value != null ? value.name : "null";
        }
    }
}
