using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Mono.Data.Sqlite;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace MainGameSemanticPoseBridge
{
    [BepInPlugin(GUID, Name, Version)]
    [BepInDependency(PoseInjectorGuid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInProcess("KoikatsuSunshine")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        public const string GUID = "canon.maingame.semanticposebridge";
        public const string Name = "MainGameSemanticPoseBridge";
        public const string Version = "0.1.0";
        private const string PoseInjectorGuid = "canon.maingame.hscene.poseinjector";

        private static Plugin _instance;
        private readonly ConcurrentQueue<Action> _mainThreadActions = new ConcurrentQueue<Action>();
        private readonly List<PoseVector> _poses = new List<PoseVector>();
        private ConfigEntry<bool> _enabled;
        private ConfigEntry<string> _databasePath;
        private ConfigEntry<string> _ollamaUrl;
        private ConfigEntry<float> _minimumScore;
        private ConfigEntry<float> _captureDelayAfterPose;
        private bool _requestRunning;
        private string _model;
        private string _lastResult = "not requested";
        private string _diagnosticLogPath;

        private void Awake()
        {
            _instance = this;
            _diagnosticLogPath = Path.Combine(Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath, "semantic_pose_diagnostic.log");
            if (_runtimeLoggingEnabled) { try { File.WriteAllText(_diagnosticLogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " awake" + Environment.NewLine, Encoding.UTF8); } catch { } }
            _enabled = Config.Bind("00. General", "Enabled", true, "文章から近いポーズを検索して適用する機能を有効にします。");
            _databasePath = Config.Bind("10. Search", "Database Path",
                Path.Combine(Paths.PluginPath, "canon_plugins", "MainGamePoseCatalogCapture", "pose_catalog_captures", "pose_index.sqlite"),
                "bge-m3で作成したポーズ索引SQLiteのパスです。");
            _ollamaUrl = Config.Bind("10. Search", "Ollama URL", "http://127.0.0.1:11434", "Ollama APIのURLです。");
            _minimumScore = Config.Bind("10. Search", "Minimum Score", 0.00f,
                new ConfigDescription("0なら常に一番近いポーズを適用します。数値を上げた場合だけ、この類似度未満の候補を拒否します。", new AcceptableValueRange<float>(0f, 1f)));
            _captureDelayAfterPose = Config.Bind("20. Capture", "Capture Delay After Pose", 0.8f,
                new ConfigDescription("ポーズ適用後、ボーンが安定してから撮影構図を計算し始めるまでの待機秒数です。", new AcceptableValueRange<float>(0f, 5f)));
            try
            {
                LoadIndex();
                Diagnostic("index_loaded count=" + _poses.Count + " model=" + _model + " database=" + _databasePath.Value);
                Logger.LogInfo("loaded pose index count=" + _poses.Count + " model=" + _model);
            }
            catch (Exception ex)
            {
                Diagnostic("index_load_failed " + ex);
                Logger.LogError("pose index load failed: " + ex);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            Action action;
            while (_mainThreadActions.TryDequeue(out action))
            {
                try { action(); }
                catch (Exception ex) { Logger.LogError("main thread action failed: " + ex); }
            }
        }

        public static bool TryRequestPose(string posePrompt, out string reason)
        {
            Plugin instance = _instance;
            if (instance == null) { reason = "SemanticPoseBridge is not loaded"; return false; }
            return instance.RequestPose(posePrompt, false, out reason);
        }

        public static bool TryRequestPose(string posePrompt, bool captureAfterApply, out string reason)
        {
            Plugin instance = _instance;
            if (instance == null) { reason = "SemanticPoseBridge is not loaded"; return false; }
            return instance.RequestPose(posePrompt, captureAfterApply, out reason);
        }

        public static bool TryRequestPose(string posePrompt, int captureCount, out string reason)
        {
            Plugin instance = _instance;
            if (instance == null) { reason = "SemanticPoseBridge is not loaded"; return false; }
            return instance.RequestPose(posePrompt, Mathf.Clamp(captureCount, 0, 10), out reason);
        }

        public static bool TryGetLastResult(out string result)
        {
            Plugin instance = _instance;
            result = instance != null ? instance._lastResult : "SemanticPoseBridge is not loaded";
            return instance != null;
        }

        public static bool TryApplyNamedPoseByName(string customName, bool captureAfterApply, out string reason)
        {
            return TryApplyNamedPoseByName(customName, captureAfterApply ? 1 : 0, out reason);
        }

        public static bool TryApplyNamedPoseByName(string customName, int captureCount, out string reason)
        {
            Plugin instance = _instance;
            if (instance == null) { reason = "SemanticPoseBridge is not loaded"; return false; }
            string name = (customName ?? string.Empty).Trim();
            PoseVector pose = instance._poses.FirstOrDefault(item => string.Equals(item.CustomName, name, StringComparison.Ordinal));
            if (pose == null) { reason = "custom_name not found: " + name; return false; }

            bool applied = TryApplyPose(pose.Id, out reason);
            int requestedCaptures = Mathf.Clamp(captureCount, 0, 10);
            if (applied && requestedCaptures > 0)
            {
                float delay = Mathf.Max(0f, instance._captureDelayAfterPose.Value);
                instance.StartCoroutine(instance.CaptureAfterPoseDelay(delay, requestedCaptures));
                reason += " capture=scheduled count=" + requestedCaptures + " delay=" + delay.ToString("F2");
            }
            instance._lastResult = (applied ? "applied" : "failed") + " id=" + pose.Id
                + " custom_name=" + pose.CustomName + " reason=" + reason;
            instance.Diagnostic("named_apply_result " + instance._lastResult);
            return applied;
        }

        public static bool TryResolveNamedPoseFromText(
            string text,
            string triggerSuffix,
            out string customName,
            out int matchIndex,
            out string reason)
        {
            customName = string.Empty;
            matchIndex = -1;
            Plugin instance = _instance;
            if (instance == null) { reason = "SemanticPoseBridge is not loaded"; return false; }

            string source = text ?? string.Empty;
            string suffix = (triggerSuffix ?? string.Empty).Trim();
            if (source.Length == 0) { reason = "response text is empty"; return false; }
            if (suffix.Length == 0) { reason = "trigger suffix is empty"; return false; }
            if (instance._poses.Count == 0) { reason = "pose index is empty"; return false; }

            int bestDistance = int.MaxValue;
            int bestLength = -1;
            int bestStart = -1;
            int suffixSearchStart = 0;
            while (suffixSearchStart < source.Length)
            {
                int suffixIndex = source.IndexOf(suffix, suffixSearchStart, StringComparison.Ordinal);
                if (suffixIndex < 0) break;
                suffixSearchStart = suffixIndex + Math.Max(1, suffix.Length);
                if (suffixIndex <= 0) continue;

                foreach (PoseVector pose in instance._poses)
                {
                    string name = (pose?.CustomName ?? string.Empty).Trim();
                    if (name.Length == 0) continue;

                    int start = source.LastIndexOf(name, suffixIndex - 1, StringComparison.Ordinal);
                    if (start < 0) continue;
                    int distance = suffixIndex - (start + name.Length);
                    if (distance < 0) continue;

                    if (bestStart < 0
                        || distance < bestDistance
                        || (distance == bestDistance && name.Length > bestLength)
                        || (distance == bestDistance && name.Length == bestLength && start > bestStart))
                    {
                        customName = name;
                        matchIndex = start;
                        bestDistance = distance;
                        bestLength = name.Length;
                        bestStart = start;
                    }
                }
            }

            if (bestStart < 0)
            {
                reason = "no registered custom_name before trigger suffix";
                instance.Diagnostic("named_resolve_miss suffix=" + suffix + " source_len=" + source.Length);
                return false;
            }

            reason = "matched distance=" + bestDistance + " length=" + bestLength;
            instance.Diagnostic("named_resolve_match custom_name=" + customName
                + " index=" + matchIndex + " distance=" + bestDistance + " length=" + bestLength);
            return true;
        }

        private bool RequestPose(string posePrompt, bool captureAfterApply, out string reason)
        {
            return RequestPose(posePrompt, captureAfterApply ? 1 : 0, out reason);
        }

        private bool RequestPose(string posePrompt, int captureCount, out string reason)
        {
            string query = (posePrompt ?? string.Empty).Trim();
            int requestedCaptures = Mathf.Clamp(captureCount, 0, 10);
            Diagnostic("request query_len=" + query.Length + " capture_count=" + requestedCaptures);
            if (!_enabled.Value) { reason = "disabled"; Diagnostic("request_rejected " + reason); return false; }
            if (query.Length == 0) { reason = "pose prompt is empty"; Diagnostic("request_rejected " + reason); return false; }
            if (_poses.Count == 0) { reason = "pose index is empty"; Diagnostic("request_rejected " + reason); return false; }
            if (_requestRunning) { reason = "another pose search is running"; Diagnostic("request_rejected " + reason); return false; }
            _requestRunning = true;
            _lastResult = "searching: " + query;
            Task.Run(async () =>
            {
                try
                {
                    Diagnostic("embedding_start model=" + _model + " url=" + _ollamaUrl.Value);
                    float[] queryVector = await EmbedAsync(query).ConfigureAwait(false);
                    Diagnostic("embedding_done dimensions=" + queryVector.Length);
                    PoseMatch[] top = _poses
                        .Select(pose => new PoseMatch { Pose = pose, Score = Dot(queryVector, pose.Vector) })
                        .OrderByDescending(match => match.Score)
                        .Take(5)
                        .ToArray();
                    PoseMatch best = top[0];
                    Diagnostic("match_best id=" + best.Pose.Id + " name=" + best.Pose.Name + " score=" + best.Score.ToString("F4"));
                    string ranking = string.Join(" | ", top.Select(match => match.Pose.Id + ":" + match.Pose.Name + "=" + match.Score.ToString("F4")).ToArray());
                    _mainThreadActions.Enqueue(() =>
                    {
                        try
                        {
                            if (best.Score < _minimumScore.Value)
                            {
                                _lastResult = "rejected score=" + best.Score.ToString("F4") + " ranking=" + ranking;
                                Diagnostic(_lastResult);
                                Logger.LogWarning(_lastResult);
                                return;
                            }
                            string applyReason;
                            bool applied = TryApplyPose(best.Pose.Id, out applyReason);
                            if (applied && requestedCaptures > 0)
                            {
                                float delay = Mathf.Max(0f, _captureDelayAfterPose.Value);
                                StartCoroutine(CaptureAfterPoseDelay(delay, requestedCaptures));
                                applyReason += " capture=scheduled count=" + requestedCaptures + " delay=" + delay.ToString("F2");
                            }
                            _lastResult = (applied ? "applied" : "failed") + " id=" + best.Pose.Id + " name=" + best.Pose.Name +
                                " score=" + best.Score.ToString("F4") + " query=" + query + " ranking=" + ranking + " reason=" + applyReason;
                            Diagnostic("apply_result " + _lastResult);
                            if (applied) Logger.LogInfo(_lastResult); else Logger.LogError(_lastResult);
                        }
                        finally { _requestRunning = false; }
                    });
                }
                catch (Exception ex)
                {
                    Diagnostic("search_exception " + ex);
                    _mainThreadActions.Enqueue(() =>
                    {
                        _lastResult = "search failed: " + ex.Message;
                        Logger.LogError(_lastResult);
                        _requestRunning = false;
                    });
                }
            });
            reason = "accepted";
            Diagnostic("request_accepted");
            return true;
        }

        private IEnumerator CaptureAfterPoseDelay(float delaySeconds, int captureCount)
        {
            Diagnostic("capture_wait_start seconds=" + delaySeconds.ToString("F2"));
            if (delaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(delaySeconds);
            }
            yield return new WaitForEndOfFrame();
            string reason;
            bool accepted = TryCapturePortrait(captureCount, out reason);
            Diagnostic("capture_request count=" + captureCount + " accepted=" + accepted + " reason=" + reason);
        }

        private void Diagnostic(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            try
            {
                File.AppendAllText(
                    _diagnosticLogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch { }
        }

        private static bool TryCapturePortrait(int count, out string reason)
        {
            try
            {
                Type type = Type.GetType("MainGamePortraitCapture.Plugin, MainGamePortraitCapture", false);
                MethodInfo method = type != null
                    ? type.GetMethod("TryCaptureMany", BindingFlags.Public | BindingFlags.Static, null,
                        new[] { typeof(int), typeof(string).MakeByRefType() }, null)
                    : null;
                if (method == null) { reason = "MainGamePortraitCapture is not loaded"; return false; }
                object[] args = { Mathf.Clamp(count, 1, 10), string.Empty };
                object result = method.Invoke(null, args);
                reason = args[1] as string ?? string.Empty;
                return result is bool && (bool)result;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private void LoadIndex()
        {
            _poses.Clear();
            string path = Environment.ExpandEnvironmentVariables(_databasePath.Value);
            if (!File.Exists(path)) throw new FileNotFoundException("pose index not found", path);
            using (SqliteConnection connection = new SqliteConnection("URI=file:" + path))
            {
                connection.Open();
                using (IDbCommand modelCommand = connection.CreateCommand())
                {
                    modelCommand.CommandText = "SELECT value FROM metadata WHERE key='model'";
                    _model = Convert.ToString(modelCommand.ExecuteScalar());
                }
                using (IDbCommand command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT pose_id, pose_name, custom_name, description, embedding, embedding_dim FROM poses ORDER BY pose_id";
                    using (IDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            byte[] bytes = (byte[])reader[4];
                            int dimensions = Convert.ToInt32(reader[5]);
                            float[] vector = new float[dimensions];
                            Buffer.BlockCopy(bytes, 0, vector, 0, Math.Min(bytes.Length, dimensions * sizeof(float)));
                            _poses.Add(new PoseVector
                            {
                                Id = Convert.ToInt32(reader[0]), Name = Convert.ToString(reader[1]),
                                CustomName = Convert.ToString(reader[2]), Description = Convert.ToString(reader[3]), Vector = vector,
                            });
                        }
                    }
                }
            }
            if (_poses.Count == 0 || string.IsNullOrEmpty(_model)) throw new InvalidDataException("pose index contains no usable rows");
        }

        private async Task<float[]> EmbedAsync(string text)
        {
            EmbedRequest request = new EmbedRequest { model = _model, input = new[] { text } };
            byte[] requestBytes = SerializeJson(request);
            HttpWebRequest webRequest = (HttpWebRequest)WebRequest.Create(_ollamaUrl.Value.TrimEnd('/') + "/api/embed");
            webRequest.Method = "POST";
            webRequest.ContentType = "application/json";
            webRequest.Timeout = 60000;
            webRequest.ReadWriteTimeout = 60000;
            using (Stream stream = await webRequest.GetRequestStreamAsync().ConfigureAwait(false))
                await stream.WriteAsync(requestBytes, 0, requestBytes.Length).ConfigureAwait(false);
            using (WebResponse response = await webRequest.GetResponseAsync().ConfigureAwait(false))
            using (Stream responseStream = response.GetResponseStream())
            {
                EmbedResponse payload = DeserializeJson<EmbedResponse>(responseStream);
                if (payload.embeddings == null || payload.embeddings.Length == 0 || payload.embeddings[0] == null)
                    throw new InvalidDataException("Ollama returned no embedding");
                return payload.embeddings[0];
            }
        }

        private static bool TryApplyPose(int poseId, out string reason)
        {
            PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue(PoseInjectorGuid, out info) || info == null || info.Instance == null)
            { reason = "PoseInjector is not loaded"; return false; }
            object injector = info.Instance;
            Type type = injector.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo getList = type.GetMethod("GetPoseList", flags);
            MethodInfo apply = type.GetMethod("ApplyPose", flags);
            IEnumerable poses = getList != null ? getList.Invoke(injector, null) as IEnumerable : null;
            if (poses == null || apply == null) { reason = "PoseInjector API unavailable"; return false; }
            foreach (object pose in poses)
            {
                FieldInfo idField = pose.GetType().GetField("Id", flags);
                if (idField != null && Convert.ToInt32(idField.GetValue(pose)) == poseId)
                {
                    bool ok = Convert.ToBoolean(apply.Invoke(injector, new[] { pose }));
                    reason = ok ? "ok" : "ApplyPose returned false";
                    return ok;
                }
            }
            reason = "pose_id not found";
            return false;
        }

        private static float Dot(float[] left, float[] right)
        {
            int count = Math.Min(left.Length, right.Length);
            double sum = 0d;
            for (int i = 0; i < count; i++) sum += left[i] * right[i];
            return (float)sum;
        }

        private static byte[] SerializeJson<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return stream.ToArray();
            }
        }

        private static T DeserializeJson<T>(Stream stream)
        {
            return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }

        private sealed class PoseVector { public int Id; public string Name; public string CustomName; public string Description; public float[] Vector; }
        private sealed class PoseMatch { public PoseVector Pose; public float Score; }

        [DataContract]
        private sealed class EmbedRequest
        {
            [DataMember] public string model;
            [DataMember] public string[] input;
        }

        [DataContract]
        private sealed class EmbedResponse
        {
            [DataMember] public float[][] embeddings { get; set; }
        }
    }
}
