using System;
using System.Collections.Generic;

namespace MainGameVoiceFaceEventBridge.Game
{
    // Main-thread reservations survive cancellation until their async completion is observed.
    // This prevents a load from an old scene from playing after its reservation was released.
    internal sealed class GameVoiceLoadLedger<T> where T : class
    {
        internal sealed class Lease
        {
            internal T Owner;
            internal bool Cancelled;
        }
        private readonly List<Lease> _loads = new List<Lease>();
        internal bool HasPending
        {
            get { foreach (Lease load in _loads) if (!load.Cancelled) return true; return false; }
        }
        internal IEnumerable<T> PendingOwners
        {
            get { foreach (Lease load in _loads) if (!load.Cancelled) yield return load.Owner; }
        }
        internal Lease Begin(T owner)
        {
            var load = new Lease { Owner = owner };
            _loads.Add(load);
            return load;
        }
        internal void Finish(Lease load) { if (load != null) _loads.Remove(load); }
        internal void CancelWhere(Predicate<T> predicate)
        {
            foreach (Lease load in _loads) if (predicate(load.Owner)) load.Cancelled = true;
        }
        internal bool IsCancelled(T owner)
        {
            for (int i = _loads.Count - 1; i >= 0; --i)
                if (ReferenceEquals(_loads[i].Owner, owner)) return _loads[i].Cancelled;
            return false;
        }
        internal void Reset() { foreach (Lease load in _loads) load.Cancelled = true; }
    }
}
