using System.Reflection;
using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private bool _subCamLineCaptured;
        private Vector3 _subCamLineAWorld;
        private Vector3 _subCamLineBWorld;

        private void CaptureSubCameraTargetLineBeforeWarp()
        {
            _subCamLineCaptured = false;
            _subCamLineAWorld = Vector3.zero;
            _subCamLineBWorld = Vector3.zero;

            ChaControl cha = null;
            var charas = CollectCurrentHSceneCharas();
            for (int i = 0; i < charas.Count; i++)
            {
                ChaControl c = charas[i];
                if (c != null && c.name.StartsWith("chaF_"))
                {
                    cha = c;
                    break;
                }
            }

            if (cha == null && charas.Count > 0)
                cha = charas[0];

            if (cha == null)
            {
                SimpleFileLogger.Warn("SubCameraLine capture failed: cha=null");
                return;
            }

            Transform head = FindFirstTransformByName(
                cha.transform,
                "cf_J_FaceRoot",
                "cf_J_Head",
                "cf_J_Head_s",
                "cf_J_Neck",
                "cf_J_Neck_s"
            );

            Vector3 upperWorld;
            string upperName;

            if (head != null)
            {
                upperWorld = head.position;
                upperName = head.name;
            }
            else
            {
                upperWorld = cha.transform.position + Vector3.up * 1.4f;
                upperName = cha.transform.name + ":root-plus-head-fallback";
            }

            Vector3 lowerWorld;
            string lowerName;

            if (TryGetFbbikBodyPointReflect(cha, out lowerWorld, out lowerName))
            {
                // HipHijack方式: FullBodyBipedIK.solver.bodyEffector.bone を反射で取る
            }
            else if (TryGetFbbikThighMidPointReflect(cha, out lowerWorld, out lowerName))
            {
                // bodyEffector が取れない場合は左右 thigh の中点
            }
            else
            {
                Transform waist = FindFirstTransformByName(
                    cha.transform,
                    "cf_J_Hips",
                    "cf_J_Hips_s",
                    "cf_J_Kosi",
                    "cf_J_Kosi01",
                    "cf_J_Kosi02",
                    "cf_J_Kosi03",
                    "cf_J_Spine01",
                    "cf_J_Spine01_s",
                    "cf_J_Spine02",
                    "cf_J_Spine02_s"
                );

                if (waist != null)
                {
                    lowerWorld = waist.position;
                    lowerName = waist.name;
                }
                else
                {
                    Vector3 root = cha.transform.position;
                    Vector3 dir = root - upperWorld;
                    if (dir.sqrMagnitude < 0.0001f)
                        dir = -cha.transform.forward;

                    lowerWorld = upperWorld + dir.normalized * 0.55f;
                    lowerName = cha.transform.name + ":head-to-root-waist-fallback";
                }
            }

            float lineLen = Vector3.Distance(lowerWorld, upperWorld);
            if (lineLen < 0.15f)
            {
                Vector3 root = cha.transform.position;
                Vector3 dir = root - upperWorld;
                if (dir.sqrMagnitude < 0.0001f)
                    dir = -cha.transform.forward;

                lowerWorld = upperWorld + dir.normalized * 0.55f;
                lowerName += ":too-short-fallback";
                lineLen = Vector3.Distance(lowerWorld, upperWorld);
            }

            _subCamLineAWorld = lowerWorld;
            _subCamLineBWorld = upperWorld;
            _subCamLineCaptured = true;

            bool exactA = lowerName != null && !lowerName.Contains("fallback");

            SimpleFileLogger.Info(
                $"SubCameraLine captured: cha={cha.name}, a={lowerName}:{_subCamLineAWorld}, b={upperName}:{_subCamLineBWorld}, exactA={exactA}, exactB={(head != null)}, length={lineLen:F3}"
            );
        }

        private bool TryGetSubCameraTargetLineOnClone(Vector3 offset, out Vector3 a, out Vector3 b)
        {
            a = Vector3.zero;
            b = Vector3.zero;

            if (!_subCamLineCaptured)
                return false;

            a = _subCamLineAWorld + offset;
            b = _subCamLineBWorld + offset;
            return true;
        }

        private static bool TryGetFbbikBodyPointReflect(ChaControl cha, out Vector3 pos, out string name)
        {
            pos = Vector3.zero;
            name = null;

            object fbbik = FindComponentByTypeName(cha, "RootMotion.FinalIK.FullBodyBipedIK");
            if (fbbik == null)
                return false;

            object solver = GetMemberValue(fbbik, "solver");
            if (solver == null)
                return false;

            object bodyEffector = GetMemberValue(solver, "bodyEffector");
            if (bodyEffector == null)
                return false;

            Transform bone = GetMemberValue(bodyEffector, "bone") as Transform;
            if (bone == null)
                return false;

            pos = bone.position;
            name = "FBBIK.bodyEffector.bone:" + bone.name;
            return true;
        }

        private static bool TryGetFbbikThighMidPointReflect(ChaControl cha, out Vector3 pos, out string name)
        {
            pos = Vector3.zero;
            name = null;

            Transform left = FindFirstTransformByName(
                cha.transform,
                "cf_J_LegUp01_L",
                "cf_J_LegUp00_L",
                "cf_J_Thigh_L",
                "cf_J_Momo_L"
            );

            Transform right = FindFirstTransformByName(
                cha.transform,
                "cf_J_LegUp01_R",
                "cf_J_LegUp00_R",
                "cf_J_Thigh_R",
                "cf_J_Momo_R"
            );

            if (left == null || right == null)
                return false;

            pos = (left.position + right.position) * 0.5f;
            name = "bone.thighMid:" + left.name + "+" + right.name;
            return true;
        }

        private static object FindComponentByTypeName(ChaControl cha, string fullTypeName)
        {
            if (cha == null || cha.transform == null || string.IsNullOrEmpty(fullTypeName))
                return null;

            Component[] components = cha.GetComponentsInChildren<Component>(includeInactive: true);
            for (int i = 0; i < components.Length; i++)
            {
                Component c = components[i];
                if (c == null)
                    continue;

                System.Type t = c.GetType();
                if (t != null && t.FullName == fullTypeName)
                    return c;
            }

            return null;
        }

        private static object GetMemberValue(object obj, string name)
        {
            if (obj == null || string.IsNullOrEmpty(name))
                return null;

            System.Type t = obj.GetType();

            FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
                return f.GetValue(obj);

            PropertyInfo p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null)
                return p.GetValue(obj, null);

            return null;
        }

        private static Transform FindFirstTransformByName(Transform root, params string[] names)
        {
            if (root == null || names == null || names.Length == 0)
                return null;

            Transform[] all = root.GetComponentsInChildren<Transform>(includeInactive: true);

            for (int n = 0; n < names.Length; n++)
            {
                string wanted = names[n];
                for (int i = 0; i < all.Length; i++)
                {
                    Transform t = all[i];
                    if (t != null && t.name == wanted)
                        return t;
                }
            }

            return null;
        }
    }
}
