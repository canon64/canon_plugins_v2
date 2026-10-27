using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace UnityResourceDiagnostics
{
    internal static class BakeMeshTracePatcher
    {
        public static void Apply(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(SkinnedMeshRenderer), "BakeMesh", new[] { typeof(Mesh) });
            MethodInfo postfix = AccessTools.Method(typeof(BakeMeshTracePatcher), nameof(Postfix));
            if (target == null || postfix == null)
                throw new MissingMethodException("SkinnedMeshRenderer.BakeMesh(Mesh) patch target was not found.");

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        }

        private static void Postfix(SkinnedMeshRenderer __instance, Mesh mesh)
        {
            BakeMeshTrace.Record(__instance, mesh);
        }
    }

    internal static class BakeMeshTrace
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object LockObj = new object();

        private static string _logPath;
        private static Func<bool> _enabled;
        private static Func<int> _limit;
        private static int _written;

        public static void Configure(string logPath, Func<bool> enabled, Func<int> limit)
        {
            _logPath = logPath;
            _enabled = enabled;
            _limit = limit;
            _written = 0;
        }

        public static void Record(SkinnedMeshRenderer renderer, Mesh mesh)
        {
            try
            {
                if (_enabled == null || !_enabled())
                    return;

                int limit = _limit != null ? _limit() : 0;
                if (limit <= 0)
                    return;

                lock (LockObj)
                {
                    if (_written >= limit)
                        return;

                    _written++;
                    File.AppendAllText(_logPath, BuildLine(renderer, mesh, _written, limit), Utf8NoBom);
                }
            }
            catch
            {
                // Diagnostics must never break the game path being observed.
            }
        }

        private static string BuildLine(SkinnedMeshRenderer renderer, Mesh mesh, int index, int limit)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine();
            sb.Append("[")
                .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                .Append("] BakeMesh trace ")
                .Append(index)
                .Append("/")
                .Append(limit)
                .Append(" frame=")
                .Append(Time.frameCount)
                .Append(" t=")
                .Append(Time.unscaledTime.ToString("F1"))
                .AppendLine();

            sb.Append("renderer=");
            if (renderer != null)
            {
                sb.Append(PathUtil.GetPath(renderer.gameObject))
                    .Append(" rendererId=")
                    .Append(renderer.GetInstanceID());
            }
            else
            {
                sb.Append("(null)");
            }

            sb.AppendLine();
            sb.Append("mesh=");
            if (mesh != null)
            {
                MeshInfo info = MeshInfo.From(mesh);
                sb.Append("id=")
                    .Append(info.Id)
                    .Append(" name=")
                    .Append(TextUtil.NormalizeName(info.Name))
                    .Append(" verts=")
                    .Append(info.VertexCount)
                    .Append(" tris=")
                    .Append(info.TriangleCount)
                    .Append(" bounds=")
                    .Append(TextUtil.FormatBounds(info.Bounds));
            }
            else
            {
                sb.Append("(null)");
            }

            sb.AppendLine();
            sb.AppendLine("stack:");
            sb.AppendLine(new StackTrace(2, true).ToString());
            return sb.ToString();
        }
    }
}
