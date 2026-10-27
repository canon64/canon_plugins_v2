using System;
using System.Collections;
using UnityEngine;

namespace MainGameBlankMapAdd.Application
{
    internal interface ICoroutineHost
    {
        Coroutine Start(IEnumerator routine);
        void Stop(Coroutine coroutine);
    }

    internal sealed class CoroutineHost : ICoroutineHost
    {
        private readonly MonoBehaviour _host;

        internal CoroutineHost(MonoBehaviour host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public Coroutine Start(IEnumerator routine) => _host.StartCoroutine(routine);

        public void Stop(Coroutine coroutine)
        {
            if (coroutine != null) _host.StopCoroutine(coroutine);
        }
    }
}
