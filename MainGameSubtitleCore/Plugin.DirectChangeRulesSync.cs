using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Threading;
using UnityEngine;

namespace MainGameSubtitleCore
{
    internal sealed partial class Plugin
    {
        private bool _directChangeRulesPending;
        private bool _directChangeRulesResultReady;
        private bool _directChangeRulesResultOk;
        private bool _directChangeRulesFailed;
        private float _nextDirectChangeRulesRetry;

        private void RequestDirectChangeRules()
        {
            if (_directChangeRulesPending || _settings == null || !_settings.InputPanelForwardEnabled) return;
            _directChangeRulesPending = true;
            _directChangeRulesDirty = false;
            byte[] body = Utf8NoBom.GetBytes(BuildDirectChangeRulesJson());
            string url = "http://" + _settings.InputPanelForwardHost + ":" + _settings.InputPanelForwardPort + "/direct-change-rules";
            string token = _settings.InputPanelForwardToken;
            int timeout = Math.Min(15000, Math.Max(100, _settings.InputPanelForwardTimeoutMs));
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                try
                {
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
                        var ack = new DataContractJsonSerializer(typeof(DirectChangeAck)).ReadObject(stream) as DirectChangeAck;
                        ok = response.StatusCode == HttpStatusCode.OK && ack != null && ack.ok
                            && ack.rules_applied && ack.protocol == DirectChangeProtocol;
                    }
                    if (!ok) LogWarn("[direct-rules] trigger settings were not acknowledged; mode and normal Grok sending are unaffected");
                }
                catch (WebException ex)
                {
                    string detail = ex.Message;
                    if (ex.Response != null)
                    {
                        using (var response = ex.Response)
                        using (var reader = new StreamReader(response.GetResponseStream())) detail += ": " + reader.ReadToEnd();
                    }
                    LogWarn("[direct-rules] trigger settings update failed (restart Human_2_kks if endpoint is missing): " + detail);
                }
                catch (Exception ex) { LogWarn("[direct-rules] trigger settings update failed: " + ex.Message); }
                lock (_directChangeLock)
                {
                    _directChangeRulesResultOk = ok;
                    _directChangeRulesResultReady = true;
                }
            });
        }

        private void PollDirectChangeRulesResult()
        {
            bool ok;
            lock (_directChangeLock)
            {
                if (!_directChangeRulesResultReady) return;
                ok = _directChangeRulesResultOk;
                _directChangeRulesResultReady = false;
            }
            _directChangeRulesPending = false;
            _directChangeRulesFailed = !ok;
            if (!ok)
            {
                _directChangeRulesDirty = true;
                _nextDirectChangeRulesRetry = Time.unscaledTime + 5f;
            }
            // No mode changes here. OFF and ordinary sending do not depend on these settings.
            UpdateDirectChangeVisual();
        }
    }
}
