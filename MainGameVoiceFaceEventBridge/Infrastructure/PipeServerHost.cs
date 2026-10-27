using System;

namespace MainGameVoiceFaceEventBridge.Infrastructure
{
    internal sealed class PipeServerHost : IDisposable
    {
        private readonly ExternalPipeServer _server;
        private bool _stopped;

        public PipeServerHost(
            string pipeName,
            Action<string> onLine,
            Action<string> logInfo,
            Action<string> logWarn,
            Action<string> logError)
        {
            _server = new ExternalPipeServer(pipeName, onLine, logInfo, logWarn, logError);
        }

        public bool IsForPipe(string pipeName)
        {
            return _server.IsForPipe(pipeName);
        }

        public void Start()
        {
            if (_stopped) throw new ObjectDisposedException(nameof(PipeServerHost));
            _server.Start();
        }

        public void Stop()
        {
            if (_stopped) return;
            _stopped = true;
            _server.Stop();
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
