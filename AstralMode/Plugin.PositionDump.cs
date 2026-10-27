using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private static string Vec(Vector3 v)
        {
            return string.Format("({0:F3},{1:F3},{2:F3})", v.x, v.y, v.z);
        }

        private static string Rot(Quaternion q)
        {
            Vector3 e = q.eulerAngles;
            return string.Format("({0:F1},{1:F1},{2:F1})", e.x, e.y, e.z);
        }

        private static string ParentName(Transform t)
        {
            if (t == null || t.parent == null) return "null";
            return t.parent.name + "#" + t.parent.gameObject.GetInstanceID();
        }

        private static void DumpTransformLine(string label, Transform t)
        {
            if (t == null)
            {
                SimpleFileLogger.Info($"[POS] {label}: transform=null");
                return;
            }

            GameObject go = t.gameObject;
            SimpleFileLogger.Info(
                $"[POS] {label}: name={go.name} id={go.GetInstanceID()} active={go.activeInHierarchy} layer={go.layer} worldPos={Vec(t.position)} localPos={Vec(t.localPosition)} worldRot={Rot(t.rotation)} localRot={Rot(t.localRotation)} scale={Vec(t.lossyScale)} parent={ParentName(t)}"
            );
        }

        private static void DumpRendererBoundsSummary(string label, Renderer[] renderers)
        {
            int total = renderers != null ? renderers.Length : 0;
            int active = 0;
            int enabled = 0;
            int visible = 0;
            bool hasBounds = false;
            Bounds totalBounds = new Bounds(Vector3.zero, Vector3.zero);

            if (renderers != null)
            {
                foreach (Renderer r in renderers)
                {
                    if (r == null) continue;
                    if (r.gameObject.activeInHierarchy) active++;
                    if (r.enabled) enabled++;
                    if (r.enabled && r.gameObject.activeInHierarchy)
                    {
                        visible++;
                        if (!hasBounds)
                        {
                            totalBounds = r.bounds;
                            hasBounds = true;
                        }
                        else
                        {
                            totalBounds.Encapsulate(r.bounds);
                        }
                    }
                }
            }

            SimpleFileLogger.Info(
                $"[POS] {label}: rendererTotal={total} active={active} enabled={enabled} visible={visible} boundsCenter={(hasBounds ? Vec(totalBounds.center) : "none")} boundsSize={(hasBounds ? Vec(totalBounds.size) : "none")}"
            );
        }

        private static List<ChaControl> CollectHSceneCharas(HSceneProc proc)
        {
            var result = new List<ChaControl>();
            if (proc == null) return result;

            var lstFemale = AccessTools.Field(typeof(HSceneProc), "lstFemale")?.GetValue(proc) as System.Collections.IEnumerable;
            var male = AccessTools.Field(typeof(HSceneProc), "male")?.GetValue(proc) as ChaControl;
            var male1 = AccessTools.Field(typeof(HSceneProc), "male1")?.GetValue(proc) as ChaControl;

            if (lstFemale != null)
                foreach (object o in lstFemale)
                    if (o is ChaControl c && c != null) result.Add(c);
            if (male != null) result.Add(male);
            if (male1 != null) result.Add(male1);
            return result;
        }

        internal void DumpAllAstralPositions(string reason)
        {
            try
            {
                SimpleFileLogger.Info($"========== [POS] Dump begin reason={reason} state={_state} inHScene={_inHScene} time={Time.unscaledTime:F3} ==========");
                SimpleFileLogger.Info($"[POS] Centers: realWorldCharaCenter={Vec(_realWorldCharaCenter)} astralCharaCenter={Vec(_astralCharaCenter)} astralWorldOrigin={Vec(GetAstralWorldOrigin())}");

                if (_hSceneProc != null) DumpTransformLine("HSceneProc", _hSceneProc.transform);
                else SimpleFileLogger.Info("[POS] HSceneProc=null");

                var charas = CollectHSceneCharas(_hSceneProc);
                SimpleFileLogger.Info($"[POS] HScene chara count={charas.Count}");
                for (int i = 0; i < charas.Count; i++)
                {
                    ChaControl c = charas[i];
                    if (c == null)
                    {
                        SimpleFileLogger.Info($"[POS] Chara[{i}]=null");
                        continue;
                    }

                    DumpTransformLine($"Chara[{i}] ChaControl {c.name}", c.transform);
                    if (c.objTop != null) DumpTransformLine($"Chara[{i}] objTop", c.objTop.transform);
                    if (c.objBody != null) DumpTransformLine($"Chara[{i}] objBody", c.objBody.transform);
                    DumpRendererBoundsSummary($"Chara[{i}] renderers", c.GetComponentsInChildren<Renderer>(includeInactive: true));
                }

                SimpleFileLogger.Info($"[POS] FrozenClone count={_frozenClones.Count}");
                for (int i = 0; i < _frozenClones.Count; i++)
                {
                    var fc = _frozenClones[i];
                    if (fc == null || fc.Root == null)
                    {
                        SimpleFileLogger.Info($"[POS] FrozenClone[{i}]=null/rootNull");
                        continue;
                    }

                    SimpleFileLogger.Info($"[POS] FrozenClone[{i}] savedWorldPos={Vec(fc.WorldPos)} savedWorldRot={Rot(fc.WorldRot)}");
                    DumpTransformLine($"FrozenClone[{i}] Root", fc.Root.transform);
                    DumpRendererBoundsSummary($"FrozenClone[{i}] renderers", fc.Root.GetComponentsInChildren<Renderer>(includeInactive: true));
                }

                Camera main = Camera.main;
                SimpleFileLogger.Info($"[POS] Camera.main={(main != null ? main.name + "#" + main.gameObject.GetInstanceID() : "null")}");
                Camera[] cams = Camera.allCameras;
                SimpleFileLogger.Info($"[POS] Camera.allCameras count={cams.Length}");
                for (int i = 0; i < cams.Length; i++)
                {
                    Camera cam = cams[i];
                    if (cam == null) continue;
                    DumpTransformLine($"Camera[{i}] {cam.name}", cam.transform);
                    SimpleFileLogger.Info($"[POS] Camera[{i}] props: enabled={cam.enabled} tag={cam.tag} depth={cam.depth} fov={cam.fieldOfView:F2} near={cam.nearClipPlane:F3} far={cam.farClipPlane:F3} cullingMask=0x{cam.cullingMask:X8} targetTexture={(cam.targetTexture != null ? cam.targetTexture.width + "x" + cam.targetTexture.height : "null")}");
                }

                if (_trackedMainCamera != null) DumpTransformLine("TrackedMainCamera", _trackedMainCamera.transform);
                else SimpleFileLogger.Info("[POS] TrackedMainCamera=null");

                if (_realWorldSubCameraRoot != null) DumpTransformLine("RealWorldSubCameraRoot", _realWorldSubCameraRoot.transform);
                else SimpleFileLogger.Info("[POS] RealWorldSubCameraRoot=null");
                if (_realWorldSubCamera != null) DumpTransformLine("RealWorldSubCamera", _realWorldSubCamera.transform);
                else SimpleFileLogger.Info("[POS] RealWorldSubCamera=null");

                SimpleFileLogger.Info($"[POS] DisplayQuad count={_displayQuads.Count}");
                for (int i = 0; i < _displayQuads.Count; i++)
                {
                    GameObject q = _displayQuads[i];
                    if (q == null)
                    {
                        SimpleFileLogger.Info($"[POS] DisplayQuad[{i}]=null");
                        continue;
                    }
                    DumpTransformLine($"DisplayQuad[{i}] {q.name}", q.transform);
                    DumpRendererBoundsSummary($"DisplayQuad[{i}] renderers", q.GetComponentsInChildren<Renderer>(includeInactive: true));
                }

                if (_roomRoot != null)
                {
                    DumpTransformLine("WhiteRoomRoot", _roomRoot.transform);
                    DumpRendererBoundsSummary("WhiteRoom renderers", _roomRoot.GetComponentsInChildren<Renderer>(includeInactive: true));
                }
                else
                {
                    SimpleFileLogger.Info("[POS] WhiteRoomRoot=null");
                }

                GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
                int astralNamed = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    GameObject go = all[i];
                    if (go == null || string.IsNullOrEmpty(go.name)) continue;
                    if (go.name.IndexOf("AstralMode") >= 0 || go.name.IndexOf("FrozenClone") >= 0 || go.name.IndexOf("Display_") >= 0)
                        astralNamed++;
                }
                SimpleFileLogger.Info($"[POS] NamedObject totalMatched={astralNamed}");
                SimpleFileLogger.Info($"========== [POS] Dump end reason={reason} ==========");
            }
            catch (System.Exception ex)
            {
                SimpleFileLogger.Warn($"[POS] DumpAllAstralPositions failed: {ex}");
            }
        }
    }
}
