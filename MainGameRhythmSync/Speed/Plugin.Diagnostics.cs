using System;
using UnityEngine;

namespace MainGameSpeedLimitBreak
{
    public partial class Plugin
    {
        private bool IsDiagnosticTraceEnabled()
        {
            var s = Settings;
            return s != null && s.EnablePerFrameTrace;
        }

        internal void TracePatchCall(string patchName, HFlag flags)
        {
            if (!IsDiagnosticTraceEnabled())
            {
                return;
            }

            if (flags == null)
            {
                LogDiag($"patch:{patchName}:null", $"[patch] {patchName} flags=null", 1f);
                return;
            }

            LogDiag(
                $"patch:{patchName}:mode:{(int)flags.mode}",
                $"[patch] {patchName} mode={flags.mode} speedCalc={flags.speedCalc:0.###} speed={flags.speed:0.###}",
                0.5f);
        }

        internal void TracePatchCall(string patchName)
        {
            if (!IsDiagnosticTraceEnabled())
            {
                return;
            }

            LogDiag($"patch:{patchName}", $"[patch] {patchName}", 0.5f);
        }

        internal void TraceTimelineSkip(string sourceTag, string reason)
        {
            if (!IsDiagnosticTraceEnabled())
            {
                return;
            }

            LogDiag($"timeline-skip:{sourceTag}:{reason}", $"[timeline] skip src={sourceTag} reason={reason}", 1f);
        }

        internal void TraceTimelineApply(string sourceTag, HFlag flags, float gauge01)
        {
            if (!IsDiagnosticTraceEnabled())
            {
                return;
            }

            if (flags == null)
            {
                LogDiag($"timeline-apply:{sourceTag}:null", $"[timeline] apply src={sourceTag} flags=null", 1f);
                return;
            }

            LogDiag(
                $"timeline-apply:{sourceTag}:mode:{(int)flags.mode}",
                $"[timeline] apply src={sourceTag} mode={flags.mode} gauge={gauge01:0.###} speedCalc={flags.speedCalc:0.###} speed={flags.speed:0.###}",
                0.5f);
        }

        internal void TraceSetAnimatorFloat(string param, float value)
        {
            if (!IsDiagnosticTraceEnabled())
            {
                return;
            }

            if (!_insideHScene)
            {
                return;
            }

            LogDiag(
                $"setAnimFloat:{param}",
                $"[setAnimFloat] param={param} value={value:0.####} insideH={_insideHScene} mastSrc={_masturbationSourceRangeActive}",
                0.5f);
        }

        internal void TraceLateProcAnimatorSpeed()
        {
            if (!IsDiagnosticTraceEnabled() || !_masturbationSourceRangeActive)
                return;

            if (!TryGetPrimaryFemale(out ChaControl female) || female?.animBody == null)
                return;

            float speed = female.animBody.speed;
            LogDiag("lateProc:animSpeed", $"[lateProc] animBody.speed={speed:0.####} mastSrc={_masturbationSourceRangeActive}", 0.3f);
        }

        internal void TraceMasturbationAnimators()
        {
            if (!IsDiagnosticTraceEnabled() || !_masturbationSourceRangeActive)
                return;

            if (!TryGetPrimaryFemale(out ChaControl female) || female == null)
                return;

            var allAnimators = UnityEngine.Object.FindObjectsOfType<Animator>();
            if (allAnimators == null) return;

            foreach (var anim in allAnimators)
            {
                if (anim == null) continue;
                if (anim == female.animBody) continue; // animBodyは既知なのでスキップ

                string key = $"mast-anim:{anim.GetInstanceID()}";
                string clipName = string.Empty;
                try
                {
                    var clips = anim.GetCurrentAnimatorClipInfo(0);
                    if (clips != null && clips.Length > 0 && clips[0].clip != null)
                        clipName = clips[0].clip.name;
                }
                catch { }

                LogDiag(key,
                    $"[mast-anim] name={anim.gameObject.name} path={GetGameObjectPath(anim.gameObject)} speed={anim.speed:0.####} clip={clipName}",
                    2f);
            }
        }

        private static string GetGameObjectPath(UnityEngine.GameObject obj)
        {
            if (obj == null) return string.Empty;
            var path = obj.name;
            var t = obj.transform.parent;
            int depth = 0;
            while (t != null && depth < 5)
            {
                path = t.name + "/" + path;
                t = t.parent;
                depth++;
            }
            return path;
        }

        private void LogDiag(string key, string message, float intervalSec)
        {
            float now = Time.unscaledTime;
            float interval = Mathf.Max(0.1f, intervalSec);
            if (_diagNextLogTime.TryGetValue(key, out float next) && now < next)
            {
                return;
            }

            _diagNextLogTime[key] = now + interval;
            LogInfo(message);
        }
    }
}
