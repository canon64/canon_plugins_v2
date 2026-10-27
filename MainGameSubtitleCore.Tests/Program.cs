using BepInEx.Configuration;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// The config/transport contract is exercised without creating a Unity game object.
namespace UnityEngine { internal static class Time { public static float unscaledTime => 10f; } }

namespace MainGameSubtitleCore
{
    internal sealed partial class Plugin
    {
        private ConfigFile Config;
        private float _nextDirectChangeRetry;
        private bool _directChangeEnabled, _directChangePending, _directChangeTarget;
        private bool _directChangeResultReady, _directChangeResultOk, _directChangeSynced;
        private int _directChangeGeneration;
        private readonly object _directChangeLock = new object();
        private PluginSettings _settings = new PluginSettings { InputPanelForwardEnabled = false };
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private bool _savedDirectChange;
        private int _saveCount;
        private void SaveInputPanelState() { _savedDirectChange = _directChangeEnabled; _saveCount++; }
        private static void LogWarn(string text) { }
        private void UpdateDirectChangeVisual() { }
        private static string EscapeJsonString(string text)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(string)).WriteObject(stream, text);
                string json = Encoding.UTF8.GetString(stream.ToArray());
                return json.Substring(1, json.Length - 2);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void CheckModeAndRulesAreIndependent(string directory)
        {
            var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            int port = ((IPEndPoint)socket.LocalEndpoint).Port;
            socket.Stop();
            using (var server = new HttpListener())
            {
                server.Prefixes.Add("http://127.0.0.1:" + port + "/");
                server.Start();
                var responses = Task.Run(() =>
                {
                    for (int i = 0; i < 2; i++)
                    {
                        var context = server.GetContext();
                        string body;
                        using (var reader = new StreamReader(context.Request.InputStream)) body = reader.ReadToEnd();
                        bool mode = context.Request.Url.AbsolutePath == "/direct-change-state";
                        Check(mode ? body.Contains("\"enabled\":false") && !body.Contains("rules")
                                   : body.Contains("rules") && !body.Contains("enabled"), "separate HTTP payloads");
                        context.Response.StatusCode = mode ? 200 : 404; // old Human_2_kks cannot accept new rules
                        byte[] response = Encoding.UTF8.GetBytes(mode ? "{\"ok\":true,\"enabled\":false,\"protocol\":2}" : "{\"error\":\"not found\"}");
                        context.Response.ContentLength64 = response.Length;
                        context.Response.OutputStream.Write(response, 0, response.Length);
                        context.Response.Close();
                    }
                });
                var plugin = new Plugin { Config = new ConfigFile(Path.Combine(directory, "network.cfg"), false),
                    _directChangeEnabled = true, _directChangeTarget = true,
                    _settings = new PluginSettings { InputPanelForwardEnabled = true, InputPanelForwardHost = "127.0.0.1",
                        InputPanelForwardPort = port, InputPanelForwardTimeoutMs = 2000 } };
                plugin.BindDirectChangeConfig();
                plugin.RequestDirectChangeRules();
                plugin.RequestDirectChangeState(false);
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                while ((plugin._directChangePending || plugin._directChangeRulesPending) && DateTime.UtcNow < deadline)
                {
                    plugin.PollDirectChangeResult();
                    plugin.PollDirectChangeRulesResult();
                    Thread.Sleep(10);
                }
                Check(responses.Wait(1000), "HTTP responses completed");
                Check(!plugin._directChangeEnabled && plugin._directChangeSynced, "OFF confirmed even when rules endpoint rejects settings");
                Check(plugin._directChangeRulesFailed && plugin.DirectChangeLabel() == "直接変更 OFF", "rule error cannot make OFF unsynchronized");
            }
        }

        private static int Main(string[] args)
        {
            string directory = Path.Combine(Path.GetTempPath(), "subtitle-direct-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string path = Path.Combine(directory, "settings.cfg");
                var plugin = new Plugin { Config = new ConfigFile(path, true) };
                plugin.BindDirectChangeConfig();
                Check(plugin.Config.Count == 21, "all 21 ConfigManager entries must be bound");
                Check(plugin.Config.ContainsKey(new ConfigDefinition("直接変更 トリガー", "17 巨乳に変更")), "dedicated large breast setting");
                Check(plugin.Config.ContainsKey(new ConfigDefinition("直接変更 トリガー", "18 ペチャパイに変更")), "dedicated small breast setting");
                Check(plugin.Config.ContainsKey(new ConfigDefinition("直接変更 トリガー", "19 元のおっぱいに戻す")), "dedicated breast restore setting");
                foreach (var entry in plugin.Config)
                    Check(entry.Key.Section == "直接変更 トリガー", "ConfigManager section");
                plugin._directChangeRules["morph"].Value = "に変身しろ";
                plugin._directChangeRules["custom"].Value = "変身=>テストAの姿になるね;戻せ=>元の姿に戻るね";
                plugin._directChangeRules["pose"].Value = "のスタイルになれ,の体位になれ";
                plugin._directChangeRules["breast_large"].Value = "胸を大きく";
                plugin._directChangeRules["breast_small"].Value = "胸を小さく";
                plugin._directChangeRules["breast_reset"].Value = "胸を元に";
                Check(plugin._directChangeRulesDirty, "SettingChanged must request synchronization");
                Check(plugin._nextDirectChangeRulesRetry > 0, "SettingChanged debounce");
                var restarted = new Plugin { Config = new ConfigFile(path, false) };
                restarted.BindDirectChangeConfig();
                Check(restarted._directChangeRules["morph"].Value == "に変身しろ", "config persistence");
                Check(restarted._directChangeRules["breast_large"].Value == "胸を大きく" && restarted._directChangeRules["breast_small"].Value == "胸を小さく", "independent breast config persistence");
                Check(restarted._directChangeRules["breast_reset"].Value == "胸を元に", "breast restore config persistence");
                string payload = restarted.BuildDirectChangeRulesJson();
                Check(restarted._directChangeRules["dance"].Value == "を踊って" && payload.Contains("dance"), "dance rule is synchronized");
                Check(!payload.Contains("enabled"), "rules update must not change mode");
                Check(!restarted.BuildDirectChangeStateJson(false).Contains("rules"), "OFF must not carry trigger settings");
                Check(payload.Contains("に変身しろ") && payload.Contains("変身=>テストA"), "edited values in HTTP body");
                Check(payload.Contains("胸を大きく") && payload.Contains("胸を小さく"), "breast inputs in HTTP body");
                Check(payload.Contains("胸を元に"), "breast restore input in HTTP body");
                foreach (string json in new[] { "{\"ok\":true,\"enabled\":true,\"protocol\":2}", "{\"ok\":true,\"enabled\":true}", "{\"ok\":true,\"enabled\":false,\"protocol\":2}" })
                {
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                        Check(ReadDirectChangeAck(stream, true) == json.EndsWith("true,\"protocol\":2}"), "ack must confirm mode and protocol");
                }
                if (args.Length > 0) File.WriteAllText(args[0], payload, new UTF8Encoding(false));
                var offline = new Plugin { _directChangeEnabled = true, _directChangeTarget = true, _directChangeSynced = true };
                offline.ToggleDirectChange();
                Check(!offline._directChangeEnabled && !offline._directChangeTarget, "OFF remains selectable without a pipeline");
                Check(!offline._savedDirectChange && offline._saveCount == 1, "OFF is saved before any acknowledgement");
                Check(offline.DirectChangeLabel().Contains("OFF") && offline.DirectChangeLabel().Contains("切替送信失敗"), "show selected OFF and mode send failure");
                offline.ToggleDirectChange();
                Check(offline._directChangeEnabled && offline._savedDirectChange, "offline toggle remains usable");

                var pending = new Plugin { _directChangeEnabled = true, _directChangeTarget = true, _directChangePending = true, _directChangeSentTarget = true };
                pending.ToggleDirectChange();
                Check(!pending._directChangeEnabled && pending._directChangePending, "OFF can be selected during a request without overlapping it");
                pending._directChangeResultOk = true;
                pending._directChangeResultReady = true;
                pending.PollDirectChangeResult();
                Check(!pending._directChangeEnabled && !pending._savedDirectChange, "late ON acknowledgement must not restore ON");
                Check(!pending._directChangeSynced && pending._nextDirectChangeRetry == 10f, "send latest OFF choice next");
                pending._directChangeSentTarget = false;
                pending._directChangeResultOk = false;
                pending._directChangeResultReady = true;
                pending.PollDirectChangeResult();
                Check(!pending._directChangeEnabled && !pending._directChangeSynced, "timeout keeps user's OFF selection");
                pending._directChangeResultOk = true;
                pending._directChangeResultReady = true;
                pending.PollDirectChangeResult();
                Check(pending._directChangeSynced && pending.DirectChangeLabel() == "直接変更 OFF", "OFF acknowledgement clears pending status");
                pending._directChangeRulesDirty = true;
                pending._directChangeResultReady = true;
                pending.PollDirectChangeResult();
                Check(pending._directChangeSynced && pending.DirectChangeLabel() == "直接変更 OFF", "edited rules must not invalidate mode acknowledgement");
                CheckModeAndRulesAreIndependent(directory);
                Console.WriteLine("PASS: 21 real BepInEx config entries including breast restore, edit notification, persistence, payload, protocol acknowledgement");
                Console.WriteLine("PASS: offline OFF, pending toggle, late ON response, timeout, OFF acknowledgement, rule changes");
                Console.WriteLine("PASS: real mode-only OFF HTTP succeeds while concurrent rules-only HTTP is rejected");
                return 0;
            }
            finally { Directory.Delete(directory, true); }
        }
    }
}
