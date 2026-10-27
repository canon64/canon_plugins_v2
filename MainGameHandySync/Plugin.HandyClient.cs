using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace MainGameHandySync
{
    /// <summary>
    /// Handy v3 クライアント（HDSP 位置同期）。
    ///
    /// 実機検証で確定した仕様（HANDY_V3_SPEC_FINDINGS.md 参照）:
    ///   - 認証: 秘密キー(X-Api-Key)→ /auth/token/issue → branca token を Bearer。
    ///   - /hdsp/xpt の xp は 0.0〜1.0（スペックの0〜100は誤り）。絶対位置。
    ///   - stop_on_target:true 必須（無いと端まで突き抜けて位置が効かない）。
    ///   - LoveMachine の LinearCmd(position, timeSecs) ＝ xpt(xp, t=ms)。
    /// </summary>
    internal sealed class HandyClient : IDisposable
    {
        private const string V3Base = "https://www.handyfeeling.com/api/handy-rest/v3";
        private const int TokenTtlSeconds = 86400;
        private const int TokenRenewMarginSeconds = 600;

        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        // v2 fallback
        private string _v2ConnectionKey = "";

        // v3
        private bool _useV3;
        private string _v3AppKey = "";
        private string _v3ConnectionKey = "";
        private string _v3ClientToken = "";
        private DateTime _tokenExpiryUtc = DateTime.MinValue;
        private Task<bool> _ensureTokenTask;
        private bool _hdspModeEnsured;

        // 1本ずつ送る（重複送信を捨てる）ためのガード
        private int _sendInFlight;
        // 送信中に来た最新の指令を1つだけ保持する。
        // 古い指令を待って新しいのを捨てると、端末には常に過去の位置が届く。
        // 捨てるなら古い方を捨てる。
        private float _pendingXp = -1f;
        private float _pendingTimeMs;
        private readonly object _pendingLock = new object();
        // 往復時間の実測(移動平均)。遅延の実態を数字で残すため。
        private double _rttAvgMs = -1;
        private DateTime _lastRttLog = DateTime.MinValue;
        private DateTime _lastDetailLog = DateTime.MinValue;
        private int _lastSentPos = -1;
        private DateTime _lastSendTime = DateTime.MinValue;

        private static readonly Regex TokenRegex = new Regex("\"token\"\\s*:\\s*\"([^\"]+)\"", RegexOptions.Compiled);

        internal HandyClient()
        {
            try
            {
                ServicePointManager.DefaultConnectionLimit = 10;
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                ServicePointManager.Expect100Continue = false;
            }
            catch { }
        }

        internal void ConfigureV2(string connectionKey)
        {
            _v2ConnectionKey = (connectionKey ?? "").Trim();
        }

        internal void ConfigureV3(string appKey, string connectionKey)
        {
            _useV3 = true;
            _v3AppKey = (appKey ?? "").Trim();
            _v3ConnectionKey = (connectionKey ?? "").Trim();
            _v3ClientToken = "";
            _tokenExpiryUtc = DateTime.MinValue;
            _hdspModeEnsured = false;
            if (!string.IsNullOrEmpty(_v3AppKey))
                SimpleFileLogger.Info("HandyClient: v3 HDSP mode enabled");
        }

        internal bool UsesV3 => _useV3 && !string.IsNullOrEmpty(_v3AppKey) && !string.IsNullOrEmpty(_v3ConnectionKey);

        // ==================== 公開: HDSP 位置+時間 送信 ====================

        /// <summary>
        /// LoveMachine の LinearCmd(position, timeSecs) 相当。
        /// xp01: 目標位置 0.0〜1.0 / timeMs: その位置へ到達するのにかける時間(ms)。
        /// </summary>
        internal void SendXpt(float xp01, float timeMs)
        {
            if (!UsesV3) return;
            SendXptAsync(Mathf.Clamp01(xp01), Mathf.Max(1f, timeMs));
        }

        private async void SendXptAsync(float xp01, float timeMs)
        {
            // 送信中なら「最新の指令」として置いておく。捨てない。
            // 飛行中の送信が終わった時点で、その最新値を送り直す。
            if (Interlocked.CompareExchange(ref _sendInFlight, 1, 0) != 0)
            {
                lock (_pendingLock)
                {
                    _pendingXp = xp01;
                    _pendingTimeMs = timeMs;
                }
                return;
            }

            System.Diagnostics.Stopwatch rttWatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!await EnsureTokenAsync()) return;
                await EnsureHdspMode();
                if (!_hdspModeEnsured) return;

                var request = new HttpRequestMessage(HttpMethod.Put, $"{V3Base}/hdsp/xpt");
                // xp は必ず InvariantCulture（スレッドプール上で動くためカンマ小数点事故を防ぐ）
                string jsonBody = "{\"xp\":" + xp01.ToString("0.####", CultureInfo.InvariantCulture)
                                + ",\"t\":" + ((int)timeMs).ToString(CultureInfo.InvariantCulture)
                                + ",\"stop_on_target\":true}";
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                ApplyV3Auth(request);

                var sendTask = _http.SendAsync(request);
                var finished = await Task.WhenAny(sendTask, Task.Delay(2000));
                if (finished != sendTask) return; // ハングは捨てる

                var response = await sendTask;
                if (response.IsSuccessStatusCode)
                {
                    if (Plugin.Instance != null && Plugin.Instance.ShouldLogDetailed() &&
                        (DateTime.Now - _lastDetailLog).TotalSeconds > 1.5)
                    {
                        _lastDetailLog = DateTime.Now;
                        SimpleFileLogger.Debug($"[v3 HDSP] xp={xp01:0.###} t={(int)timeMs}ms status=200");
                    }
                }
                else
                {
                    InvalidateTokenOnUnauthorized(response);
                    if (Plugin.Instance != null && Plugin.Instance.ShouldLogError() &&
                        (DateTime.Now - _lastDetailLog).TotalSeconds > 1.5)
                    {
                        _lastDetailLog = DateTime.Now;
                        string err = await response.Content.ReadAsStringAsync();
                        SimpleFileLogger.Warn($"[v3 HDSP] xpt failed: {(int)response.StatusCode} body={jsonBody} {err}");
                    }
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Instance != null && Plugin.Instance.ShouldLogError() &&
                    (DateTime.Now - _lastDetailLog).TotalSeconds > 1.5)
                {
                    _lastDetailLog = DateTime.Now;
                    SimpleFileLogger.Warn($"[v3 HDSP] xpt exception: {ex.Message}");
                }
            }
            finally
            {
                // 往復時間を記録する。遅延がどこから来ているかを数字で残す。
                rttWatch.Stop();
                double rtt = rttWatch.Elapsed.TotalMilliseconds;
                _rttAvgMs = _rttAvgMs < 0 ? rtt : (_rttAvgMs * 0.8 + rtt * 0.2);
                if ((DateTime.Now - _lastRttLog).TotalSeconds > 5
                    && Plugin.Instance != null && Plugin.Instance.ShouldLogDetailed())
                {
                    _lastRttLog = DateTime.Now;
                    SimpleFileLogger.Debug($"[v3 HDSP] rtt={rtt:0}ms avg={_rttAvgMs:0}ms");
                }

                Interlocked.Exchange(ref _sendInFlight, 0);

                // 送信中に届いていた最新の指令があれば、すぐに送り直す。
                float nextXp, nextT;
                lock (_pendingLock)
                {
                    nextXp = _pendingXp;
                    nextT = _pendingTimeMs;
                    _pendingXp = -1f;
                }
                if (nextXp >= 0f) SendXptAsync(nextXp, nextT);
            }
        }

        // ==================== HAMP（自走往復） ====================
        //
        // 指令1本に約1.5秒かかる。単純な「飛行中は捨てる」ガードだと、mode2の直後に出す
        // hamp/start が捨てられ、play_stateが0のまま速度だけ送る＝全く動かない状態になる。
        // そこでキューにする。start/stop/mode は必ず送り、velocity と stroke は
        // 最新の1件だけ残して古いものを捨てる（送っても無駄なので）。

        private readonly object _qLock = new object();
        private bool _qMode, _qStart, _qStop;
        private float _qVelocity = float.NaN;
        private float _qStrokeMin = float.NaN, _qStrokeMax = float.NaN;
        private bool _pumpRunning;

        internal void EnsureHampMode() { lock (_qLock) { _qMode = true; } Pump(); }
        internal void HampStart() { lock (_qLock) { _qStart = true; _qStop = false; } Pump(); }
        internal void HampStop() { lock (_qLock) { _qStop = true; _qStart = false; } Pump(); }

        internal void HampVelocity(float velocity01)
        {
            lock (_qLock) { _qVelocity = Mathf.Clamp01(velocity01); }
            Pump();
        }

        /// <summary>ストローク範囲。狭いほど同じvelocityで往復が速くなる。</summary>
        internal void SetStrokeRange(float min01, float max01)
        {
            float lo = Mathf.Clamp01(min01), hi = Mathf.Clamp01(max01);
            if (hi < lo) { float t = lo; lo = hi; hi = t; }
            lock (_qLock) { _qStrokeMin = lo; _qStrokeMax = hi; }
            Pump();
        }

        private async void Pump()
        {
            lock (_qLock)
            {
                if (_pumpRunning) return;
                _pumpRunning = true;
            }

            try
            {
                while (true)
                {
                    string path = null, body = null, label = null;

                    // 優先順: モード → 開始/停止 → 速度 → 振幅。
                    // 動くこと(start/velocity)を振幅より先に通す。
                    lock (_qLock)
                    {
                        if (_qMode)
                        {
                            _qMode = false;
                            path = "/mode2"; body = "{\"mode\":0}"; label = "mode2=0";
                        }
                        else if (_qStart)
                        {
                            _qStart = false;
                            path = "/hamp/start"; body = "{}"; label = "hamp/start";
                        }
                        else if (_qStop)
                        {
                            _qStop = false;
                            path = "/hamp/stop"; body = "{}"; label = "hamp/stop";
                        }
                        else if (!float.IsNaN(_qVelocity))
                        {
                            float v = _qVelocity; _qVelocity = float.NaN;
                            path = "/hamp/velocity";
                            body = "{\"velocity\":" + v.ToString("0.####", CultureInfo.InvariantCulture) + "}";
                            label = $"hamp/velocity {v:0.###}";
                        }
                        else if (!float.IsNaN(_qStrokeMin))
                        {
                            float lo = _qStrokeMin, hi = _qStrokeMax;
                            _qStrokeMin = _qStrokeMax = float.NaN;
                            path = "/slider/stroke";
                            body = "{\"min\":" + lo.ToString("0.####", CultureInfo.InvariantCulture)
                                 + ",\"max\":" + hi.ToString("0.####", CultureInfo.InvariantCulture) + "}";
                            label = $"slider/stroke {lo:0.###}-{hi:0.###}";
                        }
                        else
                        {
                            _pumpRunning = false;
                            return;
                        }
                    }

                    await SendOne(path, body, label);
                }
            }
            catch (Exception ex)
            {
                lock (_qLock) { _pumpRunning = false; }
                if (Plugin.Instance != null && Plugin.Instance.ShouldLogError())
                    SimpleFileLogger.Warn($"[hamp] pump exception: {ex.Message}");
            }
        }

        private async Task SendOne(string path, string jsonBody, string label)
        {
            if (!UsesV3) return;
            try
            {
                if (!await EnsureTokenAsync()) return;

                var request = new HttpRequestMessage(HttpMethod.Put, $"{V3Base}{path}");
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                ApplyV3Auth(request);

                var sendTask = _http.SendAsync(request);
                var finished = await Task.WhenAny(sendTask, Task.Delay(6000));
                if (finished != sendTask)
                {
                    if (Plugin.Instance != null && Plugin.Instance.ShouldLogError())
                        SimpleFileLogger.Warn($"[hamp] {label} タイムアウト");
                    return;
                }

                var response = await sendTask;
                if (!response.IsSuccessStatusCode)
                {
                    InvalidateTokenOnUnauthorized(response);
                    if (Plugin.Instance != null && Plugin.Instance.ShouldLogError())
                    {
                        string err = await response.Content.ReadAsStringAsync();
                        SimpleFileLogger.Warn($"[hamp] {label} failed: {(int)response.StatusCode} {err}");
                    }
                }
                else if (Plugin.Instance != null && Plugin.Instance.ShouldLogDetailed())
                {
                    SimpleFileLogger.Debug($"[hamp] {label} ok");
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Instance != null && Plugin.Instance.ShouldLogError())
                    SimpleFileLogger.Warn($"[hamp] {label} exception: {ex.Message}");
            }
        }

        // ==================== v3 認証 ====================

        private Task<bool> EnsureTokenAsync()
        {
            if (!string.IsNullOrEmpty(_v3ClientToken) &&
                DateTime.UtcNow < _tokenExpiryUtc.AddSeconds(-TokenRenewMarginSeconds))
                return Task.FromResult(true);
            if (_ensureTokenTask != null && !_ensureTokenTask.IsCompleted)
                return _ensureTokenTask;
            _ensureTokenTask = IssueTokenAsync();
            return _ensureTokenTask;
        }

        private async Task<bool> IssueTokenAsync()
        {
            if (string.IsNullOrEmpty(_v3AppKey) || string.IsNullOrEmpty(_v3ConnectionKey)) return false;
            try
            {
                string url = $"{V3Base}/auth/token/issue?ttl={TokenTtlSeconds}&to={Uri.EscapeDataString(_v3ConnectionKey)}";
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("X-Api-Key", _v3AppKey);
                var response = await _http.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    if (Plugin.Instance != null && Plugin.Instance.ShouldLogError())
                        SimpleFileLogger.Warn($"[v3] token issue failed: {(int)response.StatusCode} {body}");
                    return false;
                }
                var m = TokenRegex.Match(body);
                if (!m.Success)
                {
                    if (Plugin.Instance != null && Plugin.Instance.ShouldLogError())
                        SimpleFileLogger.Warn($"[v3] token not found: {body}");
                    return false;
                }
                _v3ClientToken = m.Groups[1].Value;
                _tokenExpiryUtc = DateTime.UtcNow.AddSeconds(TokenTtlSeconds);
                _hdspModeEnsured = false;
                SimpleFileLogger.Info("[v3] client token issued");
                return true;
            }
            catch (Exception ex)
            {
                if (Plugin.Instance != null && Plugin.Instance.ShouldLogError())
                    SimpleFileLogger.Warn($"[v3] token issue exception: {ex.Message}");
                return false;
            }
        }

        private void ApplyV3Auth(HttpRequestMessage request)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_v3ClientToken}");
            request.Headers.TryAddWithoutValidation("X-Connection-Key", _v3ConnectionKey);
        }

        private void InvalidateTokenOnUnauthorized(HttpResponseMessage response)
        {
            if ((int)response.StatusCode == 401)
            {
                _v3ClientToken = "";
                _tokenExpiryUtc = DateTime.MinValue;
                _hdspModeEnsured = false;
            }
        }

        /// <summary>HDSP モード(2)に入れる。一度成功すれば以降スキップ。</summary>
        private async Task EnsureHdspMode()
        {
            if (_hdspModeEnsured) return;
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Put, $"{V3Base}/mode2");
                request.Content = new StringContent("{\"mode\":2}", Encoding.UTF8, "application/json");
                ApplyV3Auth(request);
                var response = await _http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    _hdspModeEnsured = true;
                    SimpleFileLogger.Info("[v3] HDSP mode (2) set");
                }
                else
                {
                    InvalidateTokenOnUnauthorized(response);
                    if (Plugin.Instance != null && Plugin.Instance.ShouldLogError())
                        SimpleFileLogger.Warn($"[v3] set HDSP mode failed: {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Instance != null && Plugin.Instance.ShouldLogError())
                    SimpleFileLogger.Warn($"[v3] EnsureHdspMode exception: {ex.Message}");
            }
        }

        // ==================== v2 フォールバック ====================

        internal void SendPositionV2(float phase01, int min, int max, bool invert)
        {
            if (string.IsNullOrWhiteSpace(_v2ConnectionKey)) return;
            int pos = (int)Mathf.Lerp(min, max, invert ? (1f - phase01) : phase01);
            pos = Mathf.Clamp(pos, 0, 100);
            if (pos == _lastSentPos && (DateTime.Now - _lastSendTime).TotalMilliseconds < 60) return;
            _lastSentPos = pos;
            _lastSendTime = DateTime.Now;
            try { _ = _http.GetAsync($"https://www.handyfeeling.com/api/handy/v2/setPosition?k={_v2ConnectionKey}&p={pos}"); }
            catch (Exception ex)
            {
                if (Plugin.Instance != null && Plugin.Instance.ShouldLogError())
                    SimpleFileLogger.Warn($"[v2] send failed: {ex.Message}");
            }
        }

        // ==================== 接続テスト ====================

        internal async Task<bool> TestConnectionAsync()
        {
            if (_useV3 && !string.IsNullOrEmpty(_v3AppKey))
            {
                if (!await EnsureTokenAsync())
                {
                    SimpleFileLogger.Warn("[v3] test: FAILED (token)");
                    return false;
                }
                try
                {
                    var req = new HttpRequestMessage(HttpMethod.Get, $"{V3Base}/connected");
                    ApplyV3Auth(req);
                    var resp = await _http.SendAsync(req);
                    InvalidateTokenOnUnauthorized(resp);
                    string body = await resp.Content.ReadAsStringAsync();
                    bool ok = resp.IsSuccessStatusCode;
                    bool dev = ok && body.Contains("\"connected\":true");
                    SimpleFileLogger.Info($"[v3] test: auth={(ok ? "OK" : "FAIL")} deviceConnected={dev}");
                    return ok;
                }
                catch (Exception ex) { SimpleFileLogger.Warn($"[v3] test exception: {ex.Message}"); return false; }
            }
            if (string.IsNullOrWhiteSpace(_v2ConnectionKey)) return false;
            try
            {
                var resp = await _http.GetAsync($"https://www.handyfeeling.com/api/handy/v2/getStatus?k={_v2ConnectionKey}");
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }

        public void Dispose() => _http?.Dispose();
    }
}
