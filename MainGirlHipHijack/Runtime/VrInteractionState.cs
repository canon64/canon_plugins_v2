using System;
using UnityEngine;
using VRGIN.Controls;

namespace MainGirlHipHijack.Runtime
{
    internal sealed class VrInteractionState
    {
        internal bool GrabMode;
        internal Controller.Lock LeftGrabLock;
        internal Controller.Lock RightGrabLock;
        internal int LeftGrabIndex = -1;
        internal int RightGrabIndex = -1;
        internal bool BodyControlLinkEnabled;
        internal Controller.Lock BodyControlLock;
        internal Renderer[] HiddenControllerRenderers = Array.Empty<Renderer>();
        internal bool[] HiddenControllerRendererStates = Array.Empty<bool>();
        internal Collider[] HiddenControllerColliders = Array.Empty<Collider>();
        internal bool[] HiddenControllerColliderStates = Array.Empty<bool>();
        internal bool ControllerHidden;
    }
}
