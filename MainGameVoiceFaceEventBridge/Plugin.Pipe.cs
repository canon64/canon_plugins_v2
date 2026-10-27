using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    // Plugin.Pipe.cs
    //
    // 責務: NamedPipe サーバの開閉、受信1行のパース→キュー投入、毎フレームの
    //       コマンド消化、response_text キューの遅延実行コルーチン、診断用の
    //       ランタイム state ダンプ。
    //       「外部からコマンドが入る経路」の配線。
    internal sealed partial class Plugin
    {
        private void StartOrRestartPipeServer(bool forceRestart, string reason = "")
        {
            PluginSettings s = Settings;
            string reasonText = string.IsNullOrWhiteSpace(reason) ? "(none)" : reason;
            if (s == null || !s.Enabled || !s.EnablePipeServer)
            {
                Log("[pipe-life] start skipped reason=" + reasonText + " enabled=" + (s != null && s.Enabled) + " pipeEnabled=" + (s != null && s.EnablePipeServer));
                StopPipeServer("start_skipped:" + reasonText);
                return;
            }

            string pipeName = CommandParser.NormalizePipeName(s.PipeName);
            Log("[pipe-life] start requested reason=" + reasonText + " forceRestart=" + forceRestart + " targetPipe=" + pipeName + " currentState=" + (_pipeServer == null ? "stopped" : "running"));
            if (_pipeServer != null && !forceRestart && _pipeServer.IsForPipe(pipeName))
            {
                Log("[pipe-life] start skipped reason=same_pipe_running pipe=" + pipeName + " trigger=" + reasonText);
                return;
            }

            StopPipeServer("restart:" + reasonText);

            _pipeServer = new Infrastructure.PipeServerHost(
                pipeName,
                OnPipeLineReceived,
                Log,
                LogWarn,
                LogError);

            _pipeServer.Start();
            Log("[pipe] listening name=" + pipeName);
            Log("[pipe-life] start completed reason=" + reasonText + " pipe=" + pipeName);
        }

        private void StopPipeServer(string reason = "")
        {
            if (_pipeServer == null)
            {
                if (!string.IsNullOrWhiteSpace(reason))
                {
                    Log("[pipe-life] stop skipped reason=" + reason + " state=already_stopped");
                }
                return;
            }

            Log("[pipe-life] stop requested reason=" + (string.IsNullOrWhiteSpace(reason) ? "(none)" : reason));
            _pipeServer.Stop();
            _pipeServer = null;
            Log("[pipe-life] stop completed reason=" + (string.IsNullOrWhiteSpace(reason) ? "(none)" : reason));
        }

        private void OnPipeLineReceived(string line)
        {
            string preview = line ?? string.Empty;
            if (preview.Length > 180)
            {
                preview = preview.Substring(0, 180);
            }
            preview = preview.Replace("\r", "\\r").Replace("\n", "\\n");
            Log("[pipe] recv bytes=" + Encoding.UTF8.GetByteCount(line ?? string.Empty) + " chars=" + (line ?? string.Empty).Length + " preview=" + preview);

            if (!_inboundCommandDecoder.TryDecode(line, Settings, out var command, out var reason))
            {
                LogWarn("[pipe] parse failed reason=" + reason + " preview=" + preview);
                return;
            }

            if (command != null)
            {
                string traceId = string.IsNullOrWhiteSpace(command.traceId) ? "(none)" : command.traceId;
                string type = string.IsNullOrWhiteSpace(command.type) ? "(empty)" : command.type.Trim();
                Log("[pipe] parsed type=" + type + " trace=" + traceId + " main=" + command.main + " delay=" + command.delaySeconds.ToString("F3"));
            }

            EnqueueCommand(command);
        }

        private void EnqueueCommand(ExternalVoiceFaceCommand command)
        {
            int capacity = Math.Max(1, Settings?.MaxQueuedCommands ?? 1);
            Inbound.InboundCommandEnvelope envelope = _inboundCommandQueue.Enqueue(
                command,
                capacity,
                DateTime.UtcNow,
                out int droppedCount);
            if (droppedCount > 0 && Settings != null && Settings.VerboseLog)
            {
                LogWarn("[pipe] queue overflow, oldest command dropped count=" + droppedCount);
            }
            if (Settings != null && Settings.VerboseLog)
            {
                Log("[pipe] enqueued sequence=" + envelope.SequenceNo + " pending=" + _inboundCommandQueue.Count);
            }
        }

        private void DrainIncomingCommands(int maxPerFrame)
        {
            for (int i = 0; i < maxPerFrame; i++)
            {
                if (!_inboundCommandQueue.TryDequeue(out Inbound.InboundCommandEnvelope envelope))
                {
                    return;
                }

                HandleTimedCommand(envelope.Command);
            }
        }

        private void EnqueueResponseTextCommand(ExternalVoiceFaceCommand command)
        {
            if (command == null)
            {
                return;
            }

            int capacity = Math.Max(1, Settings?.MaxQueuedCommands ?? 1);
            int dropped = _responseTextQueue.Enqueue(command, capacity);
            for (int i = 0; i < dropped; i++)
            {
                LogWarn("[response_text] queue overflow, oldest command dropped");
            }
            string traceId = string.IsNullOrWhiteSpace(command.traceId) ? "(none)" : command.traceId.Trim();
            _lastResponseTraceId = traceId;
            _lastResponsePhase = "queued";
            _lastResponseTextLength = string.IsNullOrWhiteSpace(command.text) ? 0 : command.text.Length;
            _lastResponseElapsedMs = -1f;
            Log("[response_text] queued trace=" + traceId + " pending=" + _responseTextQueue.Count);
            DumpRuntimeState("response_text_queued:" + traceId);
        }

        private void PumpResponseTextQueue()
        {
            if (_responseTextCoroutine != null)
            {
                return;
            }

            if (!_responseTextQueue.TryDequeue(out ExternalVoiceFaceCommand command))
            {
                return;
            }

            _responseTextCoroutine = StartCoroutine(RunResponseTextCommand(command));
        }

        private IEnumerator RunResponseTextCommand(ExternalVoiceFaceCommand command)
        {
            // Spread heavy text parsing away from the same frame that drained pipe commands.
            yield return null;

            string traceId = command == null || string.IsNullOrWhiteSpace(command.traceId) ? "(none)" : command.traceId.Trim();
            _lastResponseTraceId = traceId;
            _lastResponsePhase = "running";
            _lastResponseStartedAt = Time.realtimeSinceStartup;
            _lastResponseTextLength = command == null || string.IsNullOrWhiteSpace(command.text) ? 0 : command.text.Length;
            try
            {
                DumpRuntimeState("response_text_start:" + traceId);
                HandleResponseTextCommand(command);
                _lastResponsePhase = "done";
                _lastResponseElapsedMs = (Time.realtimeSinceStartup - _lastResponseStartedAt) * 1000f;
                DumpRuntimeState("response_text_done:" + traceId);
            }
            catch (Exception ex)
            {
                _lastResponsePhase = "failed";
                _lastResponseElapsedMs = (Time.realtimeSinceStartup - _lastResponseStartedAt) * 1000f;
                DumpRuntimeState("response_text_failed:" + traceId);
                LogError("[response_text] fatal trace=" + traceId + " message=" + ex.Message + "\n" + ex.StackTrace);
            }
            finally
            {
                _responseTextCoroutine = null;
            }
        }

        private void ResetResponseTextQueue(string reason)
        {
            if (_responseTextCoroutine != null)
            {
                StopCoroutine(_responseTextCoroutine);
                _responseTextCoroutine = null;
            }

            int dropped = _responseTextQueue.Clear();
            _lastResponsePhase = "reset:" + reason;
            if (dropped > 0)
            {
                LogWarn("[response_text] queue reset reason=" + reason + " dropped=" + dropped);
                DumpRuntimeState("response_text_reset:" + reason);
            }
        }

        private void DumpRuntimeState(string reason)
        {
            if (!RuntimeLoggingEnabled || (Settings != null && !Settings.VerboseLog))
            {
                return;
            }

            float now = Time.unscaledTime;
            bool extPlaying = _externalVoicePlayer != null && _externalVoicePlayer.IsPlaying;
            float blockRemain = Mathf.Max(0f, _voiceGuardState.BlockUntil - now);
            bool procExists = CurrentProc != null || FindCurrentProc() != null;
            string dump =
                "[state-dump]"
                + " reason=" + reason
                + " now=" + now.ToString("F3")
                + " blockRemain=" + blockRemain.ToString("F3")
                + " blockGameVoice=" + (ShouldBlockGameVoiceEvents() ? 1 : 0)
                + " blockKiss=" + (ShouldBlockKissActions() ? 1 : 0)
                + " extPlaying=" + (extPlaying ? 1 : 0)
                + " voiceProcStopOverridden=" + (_voiceGuardState.ProcStopOverridden ? 1 : 0)
                + " delayedActions=" + _actionScheduler.Count
                + " commandQueue=" + _inboundCommandQueue.Count
                + " responseQueue=" + _responseTextQueue.Count
                + " responseRunning=" + (_responseTextCoroutine != null ? 1 : 0)
                + " lastResponseTrace=" + _lastResponseTraceId
                + " lastResponsePhase=" + _lastResponsePhase
                + " lastResponseLen=" + _lastResponseTextLength
                + " lastResponseElapsedMs=" + _lastResponseElapsedMs.ToString("F1")
                + " procExists=" + (procExists ? 1 : 0)
                + " frameStep=" + _updateLastStep;
            Log(dump);
        }
    }
}
