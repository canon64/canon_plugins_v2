using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MainGameObjectComposer
{
    // One BP agent owns one chain of bones. Never adjust that chain twice after
    // deferring it, including when its target or the follow toggle changes mid-frame.
    internal sealed class DeferredTargetQueue<TContext>
    {
        internal sealed class Call
        {
            internal object Agent;
            internal object Target;
            internal bool TwoDans;
            internal TContext Context;
            internal bool Pending;
            internal bool Deferred;
        }

        private sealed class IdentityComparer : IEqualityComparer<object>
        {
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private readonly Dictionary<object, Call> _calls = new Dictionary<object, Call>(new IdentityComparer());
        private readonly List<Call> _order = new List<Call>();
        private int _frame = -1;
        private bool _drained;
        private Call _replay;
        private bool _replayAllowed;

        internal bool ShouldRunOriginal(int frame, object agent, object target, bool twoDans,
            TContext context, bool defer)
        {
            BeginFrame(frame);
            if (_replayAllowed && _replay != null && ReferenceEquals(_replay.Agent, agent)
                && ReferenceEquals(_replay.Target, target))
            {
                _replayAllowed = false;
                return true;
            }

            if (!_calls.TryGetValue(agent, out Call call))
            {
                call = new Call { Agent = agent };
                _calls.Add(agent, call);
                _order.Add(call);
            }
            else if (!call.Pending)
            {
                // Preserve ordinary BP calls outside the integration. If BP has
                // already run normally, enabling follow cannot run it again here.
                return !defer && !call.Deferred;
            }

            call.Target = target;
            call.TwoDans = twoDans;
            call.Context = context;
            if (!defer || _drained)
            {
                // Turning follow off cancels a queued call before passing through.
                // Calls arriving after LateUpdate run now, never on the next frame.
                call.Pending = false;
                call.Deferred |= defer;
                return true;
            }

            call.Pending = true;
            call.Deferred = true;
            return false;
        }

        internal void Drain(int frame, Func<Call, bool> isCurrent, Action<Call> invoke)
        {
            BeginFrame(frame);
            if (_drained) return;
            _drained = true;
            for (int i = 0; i < _order.Count; i++)
            {
                Call call = _order[i];
                if (!call.Pending) continue;
                call.Pending = false;
                if (!isCurrent(call)) continue;
                _replay = call;
                _replayAllowed = true;
                try { invoke(call); }
                finally
                {
                    _replayAllowed = false;
                    _replay = null;
                }
            }
        }

        internal void Clear()
        {
            _calls.Clear();
            _order.Clear();
            _replay = null;
            _replayAllowed = false;
        }

        private void BeginFrame(int frame)
        {
            if (_frame == frame) return;
            Clear();
            _frame = frame;
            _drained = false;
        }
    }
}
