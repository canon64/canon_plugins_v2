using System;
using System.IO;
using System.Net;
using System.Threading;
using UnityEngine;

namespace MainGameSubtitleCore
{
    internal sealed partial class Plugin
    {
        private bool _directChangeSentTarget;

        private string DirectChangeLabel()
        {
            string label = _directChangeEnabled ? "直接変更 ON" : "直接変更 OFF";
            if (_directChangePending) return label + " (切替中)";
            if (!_directChangeSynced) return label + " (切替送信失敗)";
            if (_directChangeEnabled && _directChangeRulesFailed) return label + " (設定更新失敗)";
            return label;
        }

        private void ToggleDirectChange()
        {
            RequestDirectChangeState(!_directChangeEnabled);
        }

        private void RequestDirectChangeState(bool enabled)
        {
            _directChangeTarget = enabled;
            if (_directChangeEnabled != enabled)
            {
                // Persist the user's choice even while the pipeline is unavailable.
                _directChangeEnabled = enabled;
                _directChangeSynced = false;
                SaveInputPanelState();
            }
            UpdateDirectChangeVisual();
            // Keep requests in order; the latest choice is sent after this one completes.
            if (_directChangePending) return;
            if (_settings == null || !_settings.InputPanelForwardEnabled)
            {
                _directChangeSynced = false;
                UpdateDirectChangeVisual();
                LogWarn("[direct] pipeline forwarding is disabled");
                return;
            }
            _directChangePending = true;
            _directChangeSentTarget = enabled;
            _directChangeSynced = false;
            int generation;
            lock (_directChangeLock)
            {
                generation = ++_directChangeGeneration;
                _directChangeResultReady = false;
            }
            byte[] body = Utf8NoBom.GetBytes(BuildDirectChangeStateJson(enabled));
            UpdateDirectChangeVisual();
            string host = _settings.InputPanelForwardHost;
            int port = _settings.InputPanelForwardPort;
            string token = _settings.InputPanelForwardToken;
            int timeout = Math.Min(15000, Math.Max(100, _settings.InputPanelForwardTimeoutMs));
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                try
                {
                    string url = "http://" + host + ":" + port + "/direct-change-state";
                    var request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "POST";
                    request.ContentType = "application/json; charset=utf-8";
                    request.Timeout = timeout;
                    request.ReadWriteTimeout = timeout;
                    request.Proxy = null;
                    if (!string.IsNullOrWhiteSpace(token)) request.Headers["X-Auth-Token"] = token.Trim();
                    request.ContentLength = body.Length;
                    using (Stream stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
                    using (var response = (HttpWebResponse)request.GetResponse())
                    using (Stream stream = response.GetResponseStream())
                    {
                        ok = response.StatusCode == HttpStatusCode.OK && ReadDirectChangeAck(stream, enabled);
                        if (!ok) LogWarn("[direct] pipeline response did not confirm settings; restart Human_2_kks with the updated code");
                    }
                }
                catch (WebException ex)
                {
                    string detail = ex.Message;
                    if (ex.Response != null)
                    {
                        using (var response = ex.Response)
                        using (var reader = new StreamReader(response.GetResponseStream())) detail += ": " + reader.ReadToEnd();
                    }
                    LogWarn("[direct] state sync failed: " + detail);
                }
                catch (Exception ex)
                {
                    LogWarn("[direct] state sync failed: " + ex.Message);
                }
                lock (_directChangeLock)
                {
                    if (generation != _directChangeGeneration) return;
                    _directChangeResultOk = ok;
                    _directChangeResultReady = true;
                }
            });
        }

        private void PollDirectChangeResult()
        {
            bool ready;
            bool ok;
            lock (_directChangeLock)
            {
                ready = _directChangeResultReady;
                ok = _directChangeResultOk;
                _directChangeResultReady = false;
            }
            if (!ready) return;
            _directChangePending = false;
            bool matchesCurrentChoice = _directChangeSentTarget == _directChangeTarget;
            _directChangeSynced = ok && matchesCurrentChoice;
            if (!_directChangeSynced)
                _nextDirectChangeRetry = Time.unscaledTime + (matchesCurrentChoice ? 5f : 0f);
            UpdateDirectChangeVisual();
        }

    }
}
