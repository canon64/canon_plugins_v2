using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
namespace CanonPlugins.Shared
{
    internal static class IndependentActorFilter
    {
        internal static bool IsIndependent(ChaControl c) => c != null && c.GetComponent("IndependentActorMarker") != null;
        internal static ChaControl[] FindNonIndependent() {
            var result = new List<ChaControl>();
            foreach (var c in UnityEngine.Object.FindObjectsOfType<ChaControl>()) if (c != null && !IsIndependent(c)) result.Add(c);
            return result.ToArray();
        }
        private static readonly FieldInfo[] ParticipantFields = Array.FindAll(typeof(HSceneProc).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), f => f.Name == "lstFemale" || f.Name == "lstMale" || f.Name == "male" || f.Name == "male1");
        internal static ChaControl[] FindHActors() {
            var scene = UnityEngine.Object.FindObjectOfType<HSceneProc>();
            if (scene == null) return FindNonIndependent();
            var result = new List<ChaControl>();
            foreach (var field in ParticipantFields) {
                object value = field.GetValue(scene);
                if (value is ChaControl actor) { if (actor != null && !IsIndependent(actor) && !result.Contains(actor)) result.Add(actor); }
                else if (value is IEnumerable actors) foreach (object item in actors) {
                    var c = item as ChaControl;
                    if (c != null && !IsIndependent(c) && !result.Contains(c)) result.Add(c);
                }
            }
            return result.ToArray();
        }
    }
}
