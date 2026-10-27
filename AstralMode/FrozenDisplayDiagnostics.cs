using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace AstralMode
{
    // One snapshot per capture, never polled from Update. Keep only the latest capture.
    internal static class FrozenDisplayDiagnostics
    {
        private static readonly StringBuilder Report = new StringBuilder();
        internal static void Source(ChaControl cha)
        {
            try
            {
                if (Report.Length > 2000000) return;
                Report.AppendLine($"SOURCE frame={Time.frameCount} actor={cha.name} id={cha.GetInstanceID()} sex={cha.sex} head={cha.fileStatus.visibleHeadAlways} body={cha.fileStatus.visibleBodyAlways} simple={cha.fileStatus.visibleSimple} configBody={Manager.Config.HData.VisibleBody}");
                Renderers(cha.gameObject);
            }
            catch (Exception ex) { Report.AppendLine("SOURCE ERROR " + ex); }
        }
        internal static void Finish(Camera camera, System.Collections.Generic.List<CharaFreezeClone.FrozenClone> clones)
        {
            try
            {
                Report.AppendLine($"CAMERA frame={Time.frameCount} mask={camera.cullingMask:X8} near={camera.nearClipPlane} far={camera.farClipPlane} pos={camera.transform.position}");
                foreach (var clone in clones)
                    if (clone?.Root != null) { Report.AppendLine("CLONE " + clone.Root.name); Renderers(clone.Root); }
                string directory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                File.WriteAllText(Path.Combine(directory, "FrozenDisplay.latest.log"), DateTime.Now.ToString("O") + Environment.NewLine + Report);
            }
            catch (Exception ex) { Debug.LogWarning("Astral frozen display diagnostic failed: " + ex.Message); }
            finally { Report.Clear(); }
        }
        private static void Renderers(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                string path = r.name;
                string inactive = "";
                for (Transform t = r.transform; t != null; t = t.parent)
                {
                    if (!t.gameObject.activeSelf) inactive += t.name + ";";
                    if (t != r.transform) path = t.name + "/" + path;
                    if (t == root.transform) break;
                }
                var smr = r as SkinnedMeshRenderer;
                var filter = r.GetComponent<MeshFilter>();
                Mesh mesh = smr != null ? smr.sharedMesh : filter != null ? filter.sharedMesh : null;
                Report.AppendLine($" renderer={path} type={r.GetType().Name} active={r.gameObject.activeInHierarchy} inactiveParents={inactive} enabled={r.enabled} shadow={r.shadowCastingMode} layer={r.gameObject.layer} vertices={(mesh != null ? mesh.vertexCount : 0)} bounds={r.bounds} scale={r.transform.lossyScale}");
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) { Report.AppendLine("  null material"); continue; }
                    Report.Append($"  material={mat.name} shader={mat.shader?.name} queue={mat.renderQueue}");
                    foreach (string key in new[] { "_Color", "_BaseColor" })
                        if (mat.HasProperty(key)) Report.Append($" {key}={mat.GetColor(key)}");
                    foreach (string key in new[] { "_Alpha", "_Cutoff", "_alpha_a", "_alpha_b", "_liquidface", "_liquidftop", "_liquidfbot", "_liquidbtop", "_liquidbbot" })
                        if (mat.HasProperty(key)) Report.Append($" {key}={mat.GetFloat(key)}");
                    foreach (string key in new[] { "_AlphaMask", "_MainTex" })
                        if (mat.HasProperty(key)) {
                            var texture = mat.GetTexture(key);
                            Report.Append($" {key}={(texture == null ? "null" : texture.name + ":" + texture.GetInstanceID() + ":" + texture.GetType().Name)}");
                        }
                    Report.AppendLine();
                }
            }
        }
    }
}
