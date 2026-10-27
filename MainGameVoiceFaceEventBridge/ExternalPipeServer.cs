using System;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed class ExternalPipeServer : IDisposable
    {
        private static long s_connectionSequence;

        private readonly string _pipeName;
        private readonly Action<string> _onLine;
        private readonly Action<string> _logInfo;
        private readonly Action<string> _logWarn;
        private readonly Action<string> _logError;

        private Thread _worker;
        private volatile bool _stopRequested;

        internal ExternalPipeServer(
            string pipeName,
            Action<string> onLine,
            Action<string> logInfo,
            Action<string> logWarn,
            Action<string> logError)
        {
            _pipeName = string.IsNullOrWhiteSpace(pipeName) ? "kks_voice_face_events" : pipeName.Trim();
            _onLine = onLine;
            _logInfo = logInfo;
            _logWarn = logWarn;
            _logError = logError;
        }

        internal bool IsForPipe(string pipeName)
        {
            return string.Equals(_pipeName, pipeName ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        internal void Start()
        {
            if (_worker != null)
            {
                return;
            }

            _stopRequested = false;
            _worker = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "MainGameVoiceFaceEventBridge.PipeServer"
            };
            _worker.Start();
        }

        internal void Stop()
        {
            if (_worker == null)
            {
                return;
            }

            _stopRequested = true;
            _logInfo?.Invoke("[pipe] stop requested name=" + _pipeName);
            WakeServerWait();

            if (!_worker.Join(2000))
            {
                _logWarn?.Invoke("[pipe] worker stop timeout");
            }

            _worker = null;
            _logInfo?.Invoke("[pipe] stop completed name=" + _pipeName);
        }

        private void RunLoop()
        {
            while (!_stopRequested)
            {
                try
                {
                    _logInfo?.Invoke("[pipe] wait begin name=" + _pipeName);
                    using (var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None))
                    {
                        int workerThreadId = Thread.CurrentThread.ManagedThreadId;
                        var acceptWatch = Stopwatch.StartNew();
                        _logInfo?.Invoke("[pipe] wait server created name=" + _pipeName + " thread=" + workerThreadId);
                        server.WaitForConnection();
                        acceptWatch.Stop();
                        _logInfo?.Invoke(
                            "[pipe] wait returned"
                            + " name=" + _pipeName
                            + " thread=" + workerThreadId
                            + " elapsed_ms=" + acceptWatch.ElapsedMilliseconds
                            + " isConnected=" + server.IsConnected);
                        if (_stopRequested)
                        {
                            _logInfo?.Invoke("[pipe] accepted while stop requested name=" + _pipeName);
                            return;
                        }

                        long connectionId = Interlocked.Increment(ref s_connectionSequence);
                        _logInfo?.Invoke("[pipe] connected id=" + connectionId + " thread=" + workerThreadId + " canRead=" + server.CanRead + " canWrite=" + server.CanWrite);

                        using (var reader = new StreamReader(server, new UTF8Encoding(false), false, 1024, true))
                        {
                            _logInfo?.Invoke("[pipe] reader created id=" + connectionId + " thread=" + workerThreadId + " encoding=utf8-nobom detectEncoding=False buffer=1024");
                            int lineIndex = 0;
                            string disconnectReason = "loop_end";
                            while (!_stopRequested && server.IsConnected)
                            {
                                lineIndex++;
                                _logInfo?.Invoke("[pipe] readline start id=" + connectionId + " line=" + lineIndex);

                                string line = null;
                                try
                                {
                                    line = reader.ReadLine();
                                }
                                catch (IOException ex)
                                {
                                    _logWarn?.Invoke("[pipe] readline io error id=" + connectionId + " line=" + lineIndex + " message=" + ex.Message);
                                    disconnectReason = "readline_io_error";
                                    break;
                                }
                                catch (Exception ex)
                                {
                                    _logError?.Invoke("[pipe] readline error id=" + connectionId + " line=" + lineIndex + " message=" + ex.Message);
                                    disconnectReason = "readline_error";
                                    break;
                                }

                                if (line == null)
                                {
                                    _logInfo?.Invoke("[pipe] readline null id=" + connectionId + " line=" + lineIndex);
                                    disconnectReason = "remote_eof";
                                    break;
                                }

                                _logInfo?.Invoke("[pipe] readline ok id=" + connectionId + " line=" + lineIndex + " chars=" + line.Length);
                                _onLine?.Invoke(line);
                                disconnectReason = "processed";
                            }

                            if (_stopRequested)
                            {
                                disconnectReason = "stop_requested";
                            }

                            _logInfo?.Invoke("[pipe] disconnected id=" + connectionId + " reason=" + disconnectReason + " isConnected=" + server.IsConnected);
                        }
                    }
                }
                catch (IOException ex)
                {
                    if (_stopRequested)
                    {
                        return;
                    }

                    _logWarn?.Invoke("[pipe] io error: " + ex.Message);
                    Thread.Sleep(200);
                }
                catch (Exception ex)
                {
                    if (_stopRequested)
                    {
                        return;
                    }

                    _logError?.Invoke("[pipe] worker error: " + ex.Message);
                    Thread.Sleep(300);
                }
            }
        }

        private void WakeServerWait()
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out))
                {
                    client.Connect(100);
                }
            }
            catch
            {
                // ignore
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
