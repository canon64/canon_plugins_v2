using System.Collections.Generic;
using MainGameUiInputCapture;

namespace MainGameTransformGizmo
{
    internal static class GizmoInputCaptureRuntime
    {
        private sealed class GizmoCaptureState
        {
            public TransformGizmo Gizmo;
            public string OwnerKey;
            public bool Dragging;
            public System.Action<bool> Handler;
        }

        private static readonly Dictionary<TransformGizmo, GizmoCaptureState> States =
            new Dictionary<TransformGizmo, GizmoCaptureState>();

        private static readonly HashSet<string> ManualOwners = new HashSet<string>(System.StringComparer.Ordinal);

        internal static bool EnableFor(TransformGizmo gizmo)
        {
            if (gizmo == null)
                return false;

            if (States.ContainsKey(gizmo))
                return true;

            var state = new GizmoCaptureState
            {
                Gizmo = gizmo,
                OwnerKey = Plugin.GUID + ".gizmo." + gizmo.GetInstanceID()
            };

            state.Handler = dragging => OnDragStateChanged(state, dragging);
            gizmo.DragStateChanged += state.Handler;
            States[gizmo] = state;
            return true;
        }

        internal static bool DisableFor(TransformGizmo gizmo)
        {
            if (gizmo == null)
                return false;

            if (!States.TryGetValue(gizmo, out GizmoCaptureState state))
                return false;

            if (state.Handler != null)
                gizmo.DragStateChanged -= state.Handler;

            if (UiInputCaptureApi.IsAvailable)
                UiInputCaptureApi.EndOwner(state.OwnerKey);

            States.Remove(gizmo);
            return true;
        }

        internal static bool IsEnabledFor(TransformGizmo gizmo)
        {
            return gizmo != null && States.ContainsKey(gizmo);
        }

        internal static bool SetManualCapture(string ownerKey, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(ownerKey))
                return false;

            if (enabled)
                return ManualOwners.Add(ownerKey);

            if (UiInputCaptureApi.IsAvailable)
                UiInputCaptureApi.EndOwner(ownerKey);

            return ManualOwners.Remove(ownerKey);
        }

        internal static bool IsManualCaptureEnabled(string ownerKey)
        {
            return !string.IsNullOrWhiteSpace(ownerKey) && ManualOwners.Contains(ownerKey);
        }

        internal static void Tick()
        {
            if (!UiInputCaptureApi.IsAvailable)
                return;

            var removeList = default(List<TransformGizmo>);
            foreach (KeyValuePair<TransformGizmo, GizmoCaptureState> pair in States)
            {
                TransformGizmo gizmo = pair.Key;
                GizmoCaptureState state = pair.Value;
                if (gizmo == null)
                {
                    if (removeList == null)
                        removeList = new List<TransformGizmo>();
                    removeList.Add(pair.Key);
                    continue;
                }

                if (state.Dragging)
                    UiInputCaptureApi.Tick(state.OwnerKey, "gizmo-drag");
            }

            if (removeList != null)
            {
                for (int i = 0; i < removeList.Count; i++)
                    DisableFor(removeList[i]);
            }

            foreach (string ownerKey in ManualOwners)
                UiInputCaptureApi.Tick(ownerKey, "manual-gizmo-capture");
        }

        internal static void Shutdown()
        {
            List<TransformGizmo> gizmos = new List<TransformGizmo>(States.Keys);
            for (int i = 0; i < gizmos.Count; i++)
                DisableFor(gizmos[i]);

            if (UiInputCaptureApi.IsAvailable)
            {
                foreach (string ownerKey in ManualOwners)
                    UiInputCaptureApi.EndOwner(ownerKey);
            }

            ManualOwners.Clear();
            States.Clear();
        }

        private static void OnDragStateChanged(GizmoCaptureState state, bool dragging)
        {
            if (state == null)
                return;

            state.Dragging = dragging;
            if (!UiInputCaptureApi.IsAvailable)
                return;

            if (dragging)
                UiInputCaptureApi.Begin(state.OwnerKey, "gizmo-drag");
            else
                UiInputCaptureApi.End(state.OwnerKey, "gizmo-drag");
        }
    }
}
