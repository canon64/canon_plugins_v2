using System;
using System.Collections.Generic;
namespace MainGameVMDPlay
{
    internal sealed class OwnedLoadGeneration<T> where T : class
    {
        private readonly HashSet<T> _objects = new HashSet<T>();
        private readonly Action<T> _destroy;
        internal bool Cancelled { get; private set; }
        private bool _cleaned;
        internal void Invalidate() { Cancelled = true; }
        internal Exception Error { get; private set; }
        internal OwnedLoadGeneration(Action<T> destroy) { _destroy = destroy; }
        internal void Accept(T item, bool ownerAlive, Action<T> apply) {
            if (Cancelled || !ownerAlive) { _destroy(item); return; }
            _objects.Add(item);
            try { apply(item); }
            catch (Exception ex) { Error = ex; _objects.Remove(item); _destroy(item); }
        }
        internal void Cancel() {
            Cancelled = true;
            if (_cleaned) return;
            _cleaned = true;
            foreach (T item in _objects) _destroy(item);
            _objects.Clear();
        }
        internal void Reconcile(Func<T, bool> belongsToOwner) {
            foreach (T item in new List<T>(_objects)) if (!belongsToOwner(item)) { _destroy(item); _objects.Remove(item); }
        }
    }
}
