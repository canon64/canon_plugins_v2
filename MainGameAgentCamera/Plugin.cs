using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameAgentCamera
{
    /// <summary>
    /// 外部のエージェント(Claude / Codex / Grok_build など)にカメラを触らせるための口。
    ///
    /// 受け口は名前付きパイプではなくフォルダにした。
    /// 相手が何であれ「JSONファイルを1個置く」だけで済み、専用クライアントが要らないため。
    ///   投げる: <queue>\in\<好きな名前>.json
    ///   返る  : <queue>\out\<id>.json
    ///
    /// 処理は必ずメインスレッドの Update で行う。Unity の API は他所から触れない。
    /// </summary>
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private bool _runtimeLoggingEnabled = false;
        internal const string GUID = "com.kks.maingame.agentcamera";
        internal const string PluginName = "MainGameAgentCamera";
        internal const string Version = "0.1.0";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private string _pluginDir;
        private string _queueDir;
        private string _inDir;
        private string _outDir;
        private string _shotDir;
        private string _logPath;

        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgLogEnabled;
        private ConfigEntry<int> _cfgFemaleIndex;
        private ConfigEntry<float> _cfgPollSeconds;
        private ConfigEntry<int> _cfgShotWidth;
        private ConfigEntry<int> _cfgShotHeight;
        private ConfigEntry<float> _cfgMaxHoldSeconds;

        private float _nextPollTime;
        private float _nextSafetyPollTime;
        private float _pollIntervalSeconds = 0.1f;
        private FileSystemWatcher _queueWatcher;
        private int _queueDirty = 1;
        private long _firstQueueEventTicks;
        private long _lastQueueEventTicks;
        private volatile bool _watcherFailed;
        /// <summary>直近の look/nudge で使った画角。撮影時に強制するために覚えておく。</summary>
        private float _lastFramingFov;

        // hold 中の固定構図。ゲームや他プラグインに構図を奪われたくないときだけ使う。
        private bool _holdActive;
        private string _holdOwner = string.Empty;
        private float _holdExpireTime;
        private Vector3 _holdLookAt;
        private Vector3 _holdCameraPos;
        private Vector3 _holdUp;
        private float _holdRoll;
        private float _holdFov;

        // hold を掛けた時点の構図。restore で戻す先。
        private bool _hasRestorePoint;
        private BaseCameraControl_Ver2.CameraData _restoreData;
        private float _restoreFov;

        private void Awake()
        {
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            _logPath = Path.Combine(_pluginDir, PluginName + ".log");
            _queueDir = Path.Combine(_pluginDir, "queue");
            _inDir = Path.Combine(_queueDir, "in");
            _outDir = Path.Combine(_queueDir, "out");
            _shotDir = Path.Combine(_pluginDir, "shots");

            _cfgEnabled = Config.Bind("General", "Enabled", true, "エージェントからのコマンド受付。");
            _cfgLogEnabled = Config.Bind("Logging", "Enabled", false, "エージェントカメラの診断ログを出力する。");
            _cfgFemaleIndex = Config.Bind("General", "TargetFemaleIndex", 0, "撮影対象の女性の番号。");
            _cfgPollSeconds = Config.Bind("General", "PollSeconds", 0.1f, "コマンドフォルダを見に行く間隔(秒)。");
            _cfgShotWidth = Config.Bind("Capture", "DefaultWidth", 1080, "capture の既定の幅。");
            _cfgShotHeight = Config.Bind("Capture", "DefaultHeight", 1920, "capture の既定の高さ。");
            _cfgMaxHoldSeconds = Config.Bind("Capture", "MaxHoldSeconds", 120f,
                "hold の最長保持秒数。掴みっぱなしを防ぐため必ず自動解放する。");

            ResetLog();
            TryCreateDirectories();
            StartQueueWatcher();
            Log("initialized. queue=" + _inDir);
        }

        private void OnDestroy()
        {
            _queueWatcher?.Dispose();
            _queueWatcher = null;
        }

        private void StartQueueWatcher()
        {
            try
            {
                if (!Directory.Exists(_inDir)) return;
                var watcher = new FileSystemWatcher(_inDir, "*.json")
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite
                };
                watcher.Created += OnQueueFileChanged;
                watcher.Changed += OnQueueFileChanged;
                watcher.Renamed += OnQueueFileRenamed;
                watcher.Error += OnQueueWatcherError;
                watcher.EnableRaisingEvents = true;
                _queueWatcher = watcher;
            }
            catch (Exception ex)
            {
                Log("queue watcher unavailable: " + ex.Message);
            }
        }

        private void OnQueueFileChanged(object sender, FileSystemEventArgs e) => MarkQueueDirty();
        private void OnQueueFileRenamed(object sender, RenamedEventArgs e) => MarkQueueDirty();
        private void OnQueueWatcherError(object sender, ErrorEventArgs e) => _watcherFailed = true;

        private void MarkQueueDirty()
        {
            long timestamp = Stopwatch.GetTimestamp();
            if (Interlocked.CompareExchange(ref _queueDirty, 1, 0) == 0)
                Interlocked.Exchange(ref _firstQueueEventTicks, timestamp);
            Interlocked.Exchange(ref _lastQueueEventTicks, timestamp);
        }

        private void TryCreateDirectories()
        {
            try
            {
                Directory.CreateDirectory(_inDir);
                Directory.CreateDirectory(_outDir);
                Directory.CreateDirectory(_shotDir);
            }
            catch (Exception ex)
            {
                Log("failed to create queue directories: " + ex.Message);
            }
        }

        /// <summary>JSON設定。保存されていれば自動で読み直される。</summary>
        private AgentCameraSettings Settings => SettingsStore.Current(_pluginDir, Log);

        private void Update()
        {
            if (_cfgEnabled == null || !_cfgEnabled.Value) return;

            float now = Time.unscaledTime;
            if (now < _nextPollTime) return;

            if (_watcherFailed)
            {
                _queueWatcher?.Dispose();
                _queueWatcher = null;
                _watcherFailed = false;
            }
            if (_queueWatcher != null)
            {
                bool dirty = Interlocked.CompareExchange(ref _queueDirty, 0, 0) != 0;
                if (!dirty && now < _nextSafetyPollTime) return;
                if (dirty)
                {
                    long elapsedTicks = Stopwatch.GetTimestamp() - Interlocked.Read(ref _lastQueueEventTicks);
                    long queuedTicks = Stopwatch.GetTimestamp() - Interlocked.Read(ref _firstQueueEventTicks);
                    if (elapsedTicks >= 0 && elapsedTicks < _pollIntervalSeconds * Stopwatch.Frequency
                        && queuedTicks >= 0 && queuedTicks < 0.5f * Stopwatch.Frequency) return;
                }
            }

            _pollIntervalSeconds = Mathf.Max(0.02f, Settings.pollSeconds);
            _nextPollTime = now + _pollIntervalSeconds;
            _nextSafetyPollTime = now + 2f;
            Interlocked.Exchange(ref _queueDirty, 0);

            PollQueue();
        }

        private void LateUpdate()
        {
            if (!_holdActive) return;

            if (Time.unscaledTime > _holdExpireTime)
            {
                _holdActive = false;
                Log("hold expired (owner=" + _holdOwner + ")");
                return;
            }

            CameraControl_Ver2 ctrl = CameraRig.Resolve();
            if (ctrl == null) return;
            CameraRig.ApplyFraming(ctrl, _holdLookAt, _holdCameraPos, _holdUp, _holdRoll, _holdFov);
        }

        private void PollQueue()
        {
            string[] files;
            try
            {
                if (!Directory.Exists(_inDir)) { TryCreateDirectories(); return; }
                files = Directory.GetFiles(_inDir, "*.json");
            }
            catch (Exception ex)
            {
                Log("poll failed: " + ex.Message);
                return;
            }

            if (files.Length == 0) return;
            Array.Sort(files, StringComparer.Ordinal);

            for (int i = 0; i < files.Length; i++)
                ProcessFile(files[i]);
        }

        private void ProcessFile(string file)
        {
            string id = Path.GetFileNameWithoutExtension(file);
            AgentRequest request = null;
            string parseError = string.Empty;
            string requestText = string.Empty;

            try
            {
                requestText = File.ReadAllText(file, Utf8NoBom);
                request = JsonUtility.FromJson<AgentRequest>(requestText);
                if (request != null && string.Equals(request.cmd, "sheet", StringComparison.OrdinalIgnoreCase)
                    && (request.shots == null || request.shots.Length == 0))
                {
                    request.shots = ParseShotSpecs(requestText);
                    if (request.shots.Length > 0)
                        Log("[sheet] recovered " + request.shots.Length + " shots from request json");
                }
            }
            catch (Exception ex)
            {
                parseError = ex.Message;
            }
            finally
            {
                // 読めても読めなくても必ず捨てる。残すと毎周期同じ物を実行し続ける。
                TryDelete(file);
            }

            if (request == null)
            {
                WriteResponse(new AgentResponse
                {
                    id = id,
                    ok = false,
                    error = "invalid json: " + parseError
                });
                return;
            }

            if (string.IsNullOrEmpty(request.id)) request.id = id;

            AgentResponse response;
            try
            {
                response = Execute(request);
            }
            catch (Exception ex)
            {
                response = new AgentResponse
                {
                    id = request.id,
                    cmd = request.cmd,
                    ok = false,
                    error = ex.GetType().Name + ": " + ex.Message
                };
                Log("command failed id=" + request.id + " cmd=" + request.cmd + " " + ex);
            }

            WriteResponse(response);
        }

        // KKSのJsonUtilityが環境によってはカスタム型配列 shots[] をnullにするため、
        // sheetだけは生JSONから各要素を切り出して確実に復元する。
        private static ShotSpec[] ParseShotSpecs(string json)
        {
            List<ShotSpec> shots = new List<ShotSpec>();
            if (string.IsNullOrEmpty(json)) return shots.ToArray();

            int key = json.IndexOf("\"shots\"", StringComparison.Ordinal);
            if (key < 0) return shots.ToArray();
            int arrayStart = json.IndexOf('[', key + 7);
            if (arrayStart < 0) return shots.ToArray();

            bool inString = false;
            bool escaped = false;
            int objectDepth = 0;
            int objectStart = -1;
            for (int i = arrayStart + 1; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == ']' && objectDepth == 0) break;
                if (c == '{')
                {
                    if (objectDepth == 0) objectStart = i;
                    objectDepth++;
                    continue;
                }
                if (c != '}' || objectDepth <= 0) continue;
                objectDepth--;
                if (objectDepth != 0 || objectStart < 0) continue;

                try
                {
                    ShotSpec shot = JsonUtility.FromJson<ShotSpec>(
                        json.Substring(objectStart, i - objectStart + 1));
                    if (shot != null) shots.Add(shot);
                }
                catch
                {
                    // 壊れた要素は追加せず、HandleSheet側で0件なら明示的に失敗させる。
                }
                objectStart = -1;
            }
            return shots.ToArray();
        }

        private AgentResponse Execute(AgentRequest req)
        {
            AgentResponse res = new AgentResponse { id = req.id, cmd = req.cmd, ok = false };
            string cmd = (req.cmd ?? string.Empty).Trim().ToLowerInvariant();

            CameraControl_Ver2 ctrl = CameraRig.Resolve();
            if (ctrl == null && cmd != "targets")
            {
                res.error = "CameraControl_Ver2 not found (Hシーン中か確認)";
                return res;
            }

            switch (cmd)
            {
                case "state":
                    res.camera = BuildCameraState(ctrl);
                    res.subject = BuildSubjectState();
                    res.heldBy = _holdActive ? _holdOwner : string.Empty;
                    res.ok = true;
                    return res;

                case "bones":
                    return DoBones(req, res);

                case "targets":
                    res.subject = BuildSubjectState();
                    res.ok = res.subject.found;
                    if (!res.ok) res.error = res.subject.reason;
                    return res;

                case "place":
                    return DoPlace(req, ctrl, res);

                case "sheet":
                    return DoSheet(req, ctrl, res);

                case "pose":
                    return DoPose(res);

                case "shot":
                {
                    // 構図決めと撮影を同じフレームで行う。
                    // Hシーンは動き続けるので、間が空くと決めた構図と撮れる絵がずれる。
                    AgentResponse framed = DoLook(req, ctrl, res);
                    if (!framed.ok) return framed;
                    return DoCapture(req, ctrl, framed);
                }

                case "look":
                    return DoLook(req, ctrl, res);

                case "nudge":
                    return DoNudge(req, ctrl, res);

                case "set":
                    return DoSet(req, ctrl, res);

                case "capture":
                    return DoCapture(req, ctrl, res);

                case "hold":
                    return DoHold(req, ctrl, res);

                case "release":
                    _holdActive = false;
                    res.ok = true;
                    res.message = "hold released";
                    Log("hold released by " + Describe(req));
                    return res;

                case "restore":
                    return DoRestore(ctrl, res);

                case "unstick":
                    GlobalMethod.SetCameraMoveFlag(ctrl, _bPlay: true);
                    res.ok = true;
                    res.message = "camera control forced back";
                    res.camera = BuildCameraState(ctrl);
                    Log("unstick by " + Describe(req));
                    return res;

                default:
                    res.error = "unknown cmd: " + req.cmd
                        + " (state/targets/look/nudge/set/capture/hold/release/restore/unstick)";
                    return res;
            }
        }

        /// <summary>
        /// 指定できるボーン名を列挙する。target に文字列を入れると絞り込む。
        /// 名前つきの注視点に無い部位を狙いたいときの探索用。
        /// </summary>
        private AgentResponse DoBones(AgentRequest req, AgentResponse res)
        {
            SubjectResolver subject;
            string reason;
            if (!SubjectResolver.TryResolve(Settings.femaleIndex, out subject, out reason))
            {
                res.error = reason;
                return res;
            }

            string filter = (req.target ?? string.Empty).Trim();
            List<string> names = new List<string>();
            for (int i = 0; i < subject.Bones.Length; i++)
            {
                Transform bone = subject.Bones[i];
                if (bone == null) continue;
                string n = bone.name;
                if (filter.Length > 0 && n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;
                names.Add(n);
                if (names.Count >= 400) break;
            }

            res.ok = true;
            res.bones = names.ToArray();
            res.message = names.Count + " bones" + (filter.Length > 0 ? " matching '" + filter + "'" : string.Empty);
            return res;
        }

        private AgentResponse DoLook(AgentRequest req, CameraControl_Ver2 ctrl, AgentResponse res)
        {
            SubjectResolver subject;
            string reason;
            if (!SubjectResolver.TryResolve(Settings.femaleIndex, out subject, out reason))
            {
                res.error = reason;
                return res;
            }

            SubjectResolver.TargetShot shot;
            if (!subject.TryResolveTarget(req.target, Settings, out shot, out reason))
            {
                res.error = reason;
                return res;
            }

            Vector3 lookAt = shot.LookAt;
            lookAt += subject.BodyUp * AgentRequest.Or(req.offsetUp, 0f);
            lookAt += subject.BodyRight * AgentRequest.Or(req.offsetRight, 0f);
            lookAt += subject.BodyFront * AgentRequest.Or(req.offsetForward, 0f);

            // 指定があればそれが勝つ。無ければ部位ごとの既定の撮り方。
            // 角度の基準を決める。うつむいた顔を胴体基準で狙うと後頭部になる。
            bool useHead = shot.UseHeadFrame;
            string frameName = (req.frame ?? string.Empty).Trim().ToLowerInvariant();
            if (frameName == "head") useHead = true;
            else if (frameName == "body" || frameName == "world") useHead = false;

            Vector3 frameFront = useHead ? subject.HeadFront : subject.BodyFront;
            Vector3 frameUp = useHead ? subject.HeadUp : subject.BodyUp;
            if (frameName == "world") { frameFront = subject.BodyFront; frameUp = Vector3.up; }
            if (frameFront.sqrMagnitude < 0.0001f) frameFront = subject.BodyFront;
            if (frameUp.sqrMagnitude < 0.0001f) frameUp = subject.BodyUp;

            float yaw = AgentRequest.Or(req.yaw, shot.Yaw);
            float pitch = AgentRequest.Or(req.pitch, shot.Pitch);
            float roll = AgentRequest.Or(req.roll, shot.Roll);
            float distance = AgentRequest.Or(req.distance, shot.Distance);
            float fov = AgentRequest.Or(req.fov, shot.Fov);

            // 収まりと遮蔽を撮る前に潰す。距離の指定があってもそれで切れるなら引く。
            float aspect = (req.width > 0 ? req.width : Settings.captureWidth)
                / (float)(req.height > 0 ? req.height : Settings.captureHeight);
            ShotSolver.Result solved = ShotSolver.Solve(
                subject, frameUp, frameFront, lookAt, yaw, pitch, distance, fov, aspect,
                shot.MustSee, shot.Margin > 0f ? shot.Margin : 0.06f,
                req.autoFit && !AgentRequest.HasValue(req.distance),
                req.avoidOccluders);

            // 立ち位置は体基準(frameUp)で決めるが、画面の上向きは地面基準にする。
            // ここを体基準にすると、寝た被写体を撮ったときに絵ごと横倒し・逆さになる。
            Vector3 imageUp = req.alignToBody ? frameUp : Vector3.up;

            _lastFramingFov = fov;
            CameraRig.ApplyFraming(ctrl, lookAt, solved.CameraPosition, imageUp, roll, fov);
            RefreshHoldIfActive(lookAt, solved.CameraPosition, imageUp, roll, fov);

            res.ok = true;
            res.hasApplied = true;
            res.appliedYaw = solved.Yaw;
            res.appliedPitch = solved.Pitch;
            res.appliedDistance = solved.Distance;
            res.appliedFov = fov;
            res.distanceAdjusted = solved.DistanceAdjusted;
            res.yawAdjusted = solved.YawAdjusted;
            res.blocked = solved.StillBlocked;
            res.blocker = solved.Blocker;
            res.message = string.Format(
                CultureInfo.InvariantCulture,
                "look target={0} yaw={1:0.#} pitch={2:0.#} roll={3:0.#} distance={4:0.###} fov={5:0.#}{6}{7}{8}",
                string.IsNullOrEmpty(req.target) ? "upper" : req.target,
                solved.Yaw, solved.Pitch, roll, solved.Distance, fov,
                solved.DistanceAdjusted ? " [pulled back to fit]" : string.Empty,
                solved.YawAdjusted ? " [yaw moved to clear occluder]" : string.Empty,
                solved.StillBlocked ? " [BLOCKED by " + solved.Blocker + "]" : string.Empty);
            res.camera = BuildCameraState(ctrl);
            Log(res.message + " by " + Describe(req));
            return res;
        }

        /// <summary>
        /// エージェントが自分で決めたワールド座標へそのまま置く。
        /// 部位や既定角度の解釈を一切挟まないので、こちらで構図を計算しきれる場合はこれが速い。
        /// </summary>
        /// <summary>
        /// 今どんな姿勢なのかを、ボーンの位置と角度から読んで返す。
        ///
        /// 構図はポーズが決まらないと決められない。立っているのか寝ているのか、
        /// 顔がどちらを向いているのか、手足がどこにあるのかを先に知るための口。
        /// 判定結果(ラベル)だけでなく生の角度も返すので、受け取った側で判断し直せる。
        /// </summary>
        private AgentResponse DoPose(AgentResponse res)
        {
            SubjectResolver s;
            string reason;
            if (!SubjectResolver.TryResolve(Settings.femaleIndex, out s, out reason))
            {
                res.error = reason;
                return res;
            }

            // 体の軸が地面に対してどれだけ傾いているか。0=直立, 90=水平, 180=逆さ。
            float bodyTilt = Vector3.Angle(s.BodyUp, Vector3.up);
            // 顔がどこを向いているか。0=水平を見ている, +=上を向く, -=下を向く。
            float headPitch = 90f - Vector3.Angle(s.HeadFront, Vector3.up);
            // 顔が体の正面からどれだけ振れているか。
            float headTurn = Vector3.SignedAngle(
                Vector3.ProjectOnPlane(s.BodyFront, s.BodyUp),
                Vector3.ProjectOnPlane(s.HeadFront, s.BodyUp),
                s.BodyUp);
            // 体の正面が上を向いているか(仰向け)、下か(うつ伏せ)。
            float faceUpness = Vector3.Dot(s.BodyFront, Vector3.up);

            string posture;
            if (bodyTilt < 30f) posture = "upright";
            else if (bodyTilt > 150f) posture = "inverted";
            else if (bodyTilt > 60f && bodyTilt < 120f)
                posture = faceUpness > 0.35f ? "lying_face_up"
                    : faceUpness < -0.35f ? "lying_face_down" : "lying_on_side";
            else posture = bodyTilt < 90f ? "leaning_forward" : "leaning_back";

            Vector3 size = s.BoundsMax - s.BoundsMin;

            res.ok = true;
            res.subject = BuildSubjectState();
            FillFloorInfo(res.subject, s);
            res.message = string.Format(
                CultureInfo.InvariantCulture,
                "posture={0} bodyTilt={1:0.#}deg headPitch={2:0.#}deg headTurn={3:0.#}deg "
                + "bounds={4:0.##}x{5:0.##}x{6:0.##} handsAboveHead={7} kneesBent={8}",
                posture, bodyTilt, headPitch, headTurn,
                size.x, size.y, size.z,
                HandsAboveHead(s), KneesBent(s));
            Log("[pose] " + res.message);
            return res;
        }

        /// <summary>両手が頭より上にあるか。万歳・拘束などの判別に使う。</summary>
        private static bool HandsAboveHead(SubjectResolver s)
        {
            if (s.LeftHand == null || s.RightHand == null || s.Head == null) return false;
            float headUp = Vector3.Dot(s.Head.position, s.BodyUp);
            return Vector3.Dot(s.LeftHand.position, s.BodyUp) > headUp
                && Vector3.Dot(s.RightHand.position, s.BodyUp) > headUp;
        }

        /// <summary>膝が曲がっているか。座り・膝立ち・M字の判別に使う。</summary>
        private static bool KneesBent(SubjectResolver s)
        {
            if (s.Hips == null || s.LeftKnee == null || s.LeftFoot == null) return false;
            Vector3 thigh = s.LeftKnee.position - s.Hips.position;
            Vector3 shin = s.LeftFoot.position - s.LeftKnee.position;
            if (thigh.sqrMagnitude < 0.0001f || shin.sqrMagnitude < 0.0001f) return false;
            return Vector3.Angle(thigh, shin) > 45f;
        }

        /// <summary>
        /// エージェントが選んだ複数の画角を一括で撮り、番号入りの一覧画像にまとめる。
        ///
        /// 全案を同じフレームで撮るので、「同じ瞬間を別角度で撮り比べた」一覧になる。
        /// 1枚ずつ撮ると被写体が動いてしまい、比較にならない。
        /// </summary>
        private AgentResponse DoSheet(AgentRequest req, CameraControl_Ver2 ctrl, AgentResponse res)
        {
            SubjectResolver subject;
            string reason;
            if (!SubjectResolver.TryResolve(Settings.femaleIndex, out subject, out reason))
            {
                res.error = reason;
                return res;
            }

            if (req.shots == null || req.shots.Length == 0)
            {
                res.error = "sheet requires shots[] (撮りたい画角を並べて渡す)";
                return res;
            }

            List<ContactSheet.Candidate> candidates = new List<ContactSheet.Candidate>();
            for (int i = 0; i < req.shots.Length; i++)
            {
                ShotSpec spec = req.shots[i];
                if (spec == null) continue;

                string target = string.IsNullOrEmpty(spec.target) ? req.target : spec.target;
                SubjectResolver.TargetShot baseShot;
                string shotReason;
                if (!subject.TryResolveTarget(target, Settings, out baseShot, out shotReason))
                {
                    res.error = shotReason;
                    return res;
                }

                bool useHead = baseShot.UseHeadFrame;
                string frameName = (string.IsNullOrEmpty(spec.frame) ? req.frame : spec.frame);
                frameName = (frameName ?? string.Empty).Trim().ToLowerInvariant();
                if (frameName == "head") useHead = true;
                else if (frameName == "body" || frameName == "world") useHead = false;

                Vector3 front = useHead ? subject.HeadFront : subject.BodyFront;
                Vector3 up = useHead ? subject.HeadUp : subject.BodyUp;
                if (front.sqrMagnitude < 0.0001f) front = subject.BodyFront;
                if (up.sqrMagnitude < 0.0001f) up = subject.BodyUp;

                Vector3 lookAt = baseShot.LookAt
                    + subject.BodyUp * AgentRequest.Or(spec.offsetUp, 0f)
                    + subject.BodyRight * AgentRequest.Or(spec.offsetRight, 0f);

                candidates.Add(new ContactSheet.Candidate
                {
                    Target = target ?? string.Empty,
                    LookAt = lookAt,
                    FrameUp = up,
                    FrameFront = front,
                    Yaw = AgentRequest.Or(spec.yaw, baseShot.Yaw),
                    Pitch = AgentRequest.Or(spec.pitch, baseShot.Pitch),
                    Roll = AgentRequest.Or(spec.roll, baseShot.Roll),
                    Distance = AgentRequest.Or(spec.distance, baseShot.Distance),
                    Fov = AgentRequest.Or(spec.fov, baseShot.Fov)
                });
            }

            string name = (req.name ?? string.Empty).Trim();
            if (name.Length == 0)
                name = "sheet_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            foreach (char bad in Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');

            int fullW = req.width > 0 ? req.width : Settings.captureWidth;
            int fullH = req.height > 0 ? req.height : Settings.captureHeight;
            int cellW = req.cellWidth > 0 ? req.cellWidth : Mathf.Max(120, fullW / 4);
            int cellH = req.cellHeight > 0 ? req.cellHeight : Mathf.Max(120, fullH / 4);

            ContactSheet.Result built = ContactSheet.Build(
                ctrl, candidates, req.columns > 0 ? req.columns : 5,
                cellW, cellH, fullW, fullH, name, _shotDir, Log);

            if (!built.Ok)
            {
                res.error = "sheet failed: " + built.Error;
                return res;
            }

            res.ok = true;
            res.sheetPath = built.SheetPath;
            res.shotsJson = ContactSheet.CandidatesToJson(built.Candidates);
            res.message = built.Candidates.Count + " shots -> " + Path.GetFileName(built.SheetPath);
            Log("[sheet] " + res.message + " by " + Describe(req));
            return res;
        }

        private AgentResponse DoPlace(AgentRequest req, CameraControl_Ver2 ctrl, AgentResponse res)
        {
            if (req.lookAt == null || req.lookAt.Length < 3 || req.from == null || req.from.Length < 3)
            {
                res.error = "place requires lookAt[3] and from[3] (world space)";
                return res;
            }

            Vector3 lookAt = new Vector3(req.lookAt[0], req.lookAt[1], req.lookAt[2]);
            Vector3 from = new Vector3(req.from[0], req.from[1], req.from[2]);
            float fov = AgentRequest.Or(req.fov, ctrl.GetCameraData().Fov);
            float roll = AgentRequest.Or(req.roll, 0f);

            // 画面の上は地面基準。体の傾きを持ち込まない。
            Vector3 up = Vector3.up;
            if (req.alignToBody)
            {
                SubjectResolver bodySubject;
                string bodyReason;
                if (SubjectResolver.TryResolve(Settings.femaleIndex, out bodySubject, out bodyReason))
                    up = bodySubject.BodyUp;
            }

            CameraRig.ApplyFraming(ctrl, lookAt, from, up, roll, fov);
            RefreshHoldIfActive(lookAt, from, up, roll, fov);

            res.ok = true;
            res.message = "placed";
            res.camera = BuildCameraState(ctrl);
            return res;
        }

        private AgentResponse DoNudge(AgentRequest req, CameraControl_Ver2 ctrl, AgentResponse res)
        {
            SubjectResolver subject;
            string reason;
            if (!SubjectResolver.TryResolve(Settings.femaleIndex, out subject, out reason))
            {
                res.error = reason;
                return res;
            }

            Vector3 lookAt = CameraRig.WorldLookAt(ctrl);
            Vector3 cameraPos = CameraRig.WorldPosition(ctrl);

            float yaw, pitch, distance;
            CameraRig.DecomposeOrbit(lookAt, cameraPos, subject.BodyUp, subject.BodyFront, out yaw, out pitch, out distance);

            yaw += AgentRequest.Or(req.dYaw, 0f);
            pitch = Mathf.Clamp(pitch + AgentRequest.Or(req.dPitch, 0f), -89f, 89f);
            distance = Mathf.Max(0.05f, distance + AgentRequest.Or(req.dDistance, 0f));

            lookAt += subject.BodyUp * AgentRequest.Or(req.dUp, 0f);
            lookAt += subject.BodyRight * AgentRequest.Or(req.dRight, 0f);

            float fov = Mathf.Clamp(ctrl.GetCameraData().Fov + AgentRequest.Or(req.dFov, 0f), 5f, 120f);
            float roll = AgentRequest.Or(req.dRoll, 0f);

            Vector3 newPos = CameraRig.OrbitPosition(lookAt, subject.BodyUp, subject.BodyFront, yaw, pitch, distance);
            Vector3 nudgeUp = req.alignToBody ? subject.BodyUp : Vector3.up;
            CameraRig.ApplyFraming(ctrl, lookAt, newPos, nudgeUp, roll, fov);
            RefreshHoldIfActive(lookAt, newPos, nudgeUp, roll, fov);

            res.ok = true;
            res.message = string.Format(
                CultureInfo.InvariantCulture,
                "nudge -> yaw={0:0.#} pitch={1:0.#} distance={2:0.###} fov={3:0.#}", yaw, pitch, distance, fov);
            res.camera = BuildCameraState(ctrl);
            Log(res.message + " by " + Describe(req));
            return res;
        }

        private AgentResponse DoSet(AgentRequest req, CameraControl_Ver2 ctrl, AgentResponse res)
        {
            if (req.pos == null || req.pos.Length < 3 || req.rot == null || req.rot.Length < 3
                || req.dir == null || req.dir.Length < 3)
            {
                res.error = "set requires pos[3], rot[3], dir[3] (state が返した値をそのまま渡す)";
                return res;
            }

            BaseCameraControl_Ver2.CameraData data = ctrl.GetCameraData();
            data.Pos = new Vector3(req.pos[0], req.pos[1], req.pos[2]);
            data.Rot = new Vector3(req.rot[0], req.rot[1], req.rot[2]);
            data.Dir = new Vector3(req.dir[0], req.dir[1], req.dir[2]);
            data.Fov = Mathf.Clamp(AgentRequest.Or(req.fov, data.Fov), 5f, 120f);
            ctrl.SetCameraData(data);
            CameraRig.ApplyFov(ctrl, data.Fov);

            res.ok = true;
            res.message = "camera data applied";
            res.camera = BuildCameraState(ctrl);
            return res;
        }

        private AgentResponse DoCapture(AgentRequest req, CameraControl_Ver2 ctrl, AgentResponse res)
        {
            int width = req.width > 0 ? req.width : Settings.captureWidth;
            int height = req.height > 0 ? req.height : Settings.captureHeight;

            string name = (req.name ?? string.Empty).Trim();
            if (name.Length == 0)
                name = "shot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                name += ".png";
            foreach (char bad in Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');

            string path = Path.Combine(_shotDir, name);
            // look/nudge で指定された画角を撮影時に強制する。
            // 画面上のFOVは他プラグインに上書きされうるが、撮る絵だけは指示通りにする。
            float fov = AgentRequest.Or(req.fov, _lastFramingFov > 0f ? _lastFramingFov : 0f);

            Camera cam = CameraRig.ResolveCamera(ctrl);
            string error;

            // 本番の絵。背景の映像パネルも含めてそのまま写す。
            if (!ShotCapture.TryCapture(cam, width, height, path, fov, req.hideScreens, out error))
            {
                res.error = "capture failed: " + error;
                return res;
            }

            // 構図判定用の控え。背景の映像を消してある。
            // 完成画像とは別物で、エージェントが構図を見るためだけに使う。
            if (req.cleanPreview && Settings.hideScreensInPreview)
            {
                string cleanPath = Path.Combine(
                    _shotDir, Path.GetFileNameWithoutExtension(name) + "__clean.png");
                string cleanError;
                if (ShotCapture.TryCapture(cam, width, height, cleanPath, fov, true, out cleanError))
                    res.cleanPath = cleanPath;
                else
                    Log("clean preview failed: " + cleanError);
            }

            res.ok = true;
            res.path = path;
            res.message = "captured " + width + "x" + height;
            res.camera = BuildCameraState(ctrl);
            Log("captured " + path + " by " + Describe(req));
            return res;
        }

        private AgentResponse DoHold(AgentRequest req, CameraControl_Ver2 ctrl, AgentResponse res)
        {
            if (!_hasRestorePoint)
            {
                _restoreData = ctrl.GetCameraData();
                _restoreFov = _restoreData.Fov;
                _hasRestorePoint = true;
            }

            Vector3 lookAt = CameraRig.WorldLookAt(ctrl);
            Vector3 cameraPos = CameraRig.WorldPosition(ctrl);

            _holdLookAt = lookAt;
            _holdCameraPos = cameraPos;
            _holdUp = Vector3.up;
            _holdRoll = 0f;
            _holdFov = ctrl.GetCameraData().Fov;
            _holdOwner = string.IsNullOrEmpty(req.agent) ? "unknown" : req.agent;

            float seconds = Mathf.Clamp(
                AgentRequest.Or(req.seconds, _cfgMaxHoldSeconds.Value), 1f, _cfgMaxHoldSeconds.Value);
            _holdExpireTime = Time.unscaledTime + seconds;
            _holdActive = true;

            res.ok = true;
            res.heldBy = _holdOwner;
            res.message = "hold for " + seconds.ToString("0.#", CultureInfo.InvariantCulture) + "s";
            res.camera = BuildCameraState(ctrl);
            Log("hold by " + _holdOwner + " for " + seconds.ToString("0.#", CultureInfo.InvariantCulture) + "s");
            return res;
        }

        private AgentResponse DoRestore(CameraControl_Ver2 ctrl, AgentResponse res)
        {
            if (!_hasRestorePoint)
            {
                res.error = "no restore point (hold していない)";
                return res;
            }

            _holdActive = false;
            CameraRig.RestoreLimits(ctrl);
            ctrl.SetCameraData(_restoreData);
            CameraRig.ApplyFov(ctrl, _restoreFov);
            _hasRestorePoint = false;

            res.ok = true;
            res.message = "camera restored";
            res.camera = BuildCameraState(ctrl);
            Log("restored");
            return res;
        }

        /// <summary>hold 中に構図を変えたら、固定する構図もそこへ更新する。</summary>
        private void RefreshHoldIfActive(Vector3 lookAt, Vector3 cameraPos, Vector3 up, float roll, float fov)
        {
            if (!_holdActive) return;
            _holdLookAt = lookAt;
            _holdCameraPos = cameraPos;
            _holdUp = up;
            _holdRoll = roll;
            _holdFov = fov;
        }

        private CameraStateJson BuildCameraState(CameraControl_Ver2 ctrl)
        {
            CameraStateJson state = new CameraStateJson();
            if (ctrl == null)
            {
                state.hasController = false;
                return state;
            }

            BaseCameraControl_Ver2.CameraData d = ctrl.GetCameraData();
            Vector3 lookAt = CameraRig.WorldLookAt(ctrl);
            Vector3 worldPos = CameraRig.WorldPosition(ctrl);

            state.hasController = true;
            state.pos = new[] { d.Pos.x, d.Pos.y, d.Pos.z };
            state.rot = new[] { d.Rot.x, d.Rot.y, d.Rot.z };
            state.dir = new[] { d.Dir.x, d.Dir.y, d.Dir.z };
            state.fov = d.Fov;
            state.worldPosition = Vec3Json.From(worldPos);
            state.worldLookAt = Vec3Json.From(lookAt);
            state.distance = Vector3.Distance(worldPos, lookAt);
            state.mouseControlBlocked = CameraRig.IsMouseControlBlocked(ctrl);
            state.keyControlBlocked = CameraRig.IsKeyControlBlocked(ctrl);
            state.blockedBy = CameraRig.DescribeBlocker(ctrl);
            return state;
        }

        private SubjectJson BuildSubjectState()
        {
            SubjectResolver subject;
            string reason;
            if (!SubjectResolver.TryResolve(Settings.femaleIndex, out subject, out reason))
                return new SubjectJson { found = false, reason = reason, targets = SubjectResolver.TargetNames };

            return new SubjectJson
            {
                found = true,
                targets = SubjectResolver.TargetNames,
                head = Vec3Json.From(subject.Head.position),
                foundBust = subject.HasBust,
                bust = Vec3Json.From(subject.HasBust ? subject.Bust : subject.Head.position),
                chest = Vec3Json.From(subject.Chest != null ? subject.Chest.position : subject.Head.position),
                waist = Vec3Json.From(subject.Waist != null ? subject.Waist.position : subject.Hips.position),
                hips = Vec3Json.From(subject.Hips.position),
                foundButt = subject.HasButt,
                butt = Vec3Json.From(subject.HasButt ? subject.Butt : subject.Hips.position),
                crotch = Vec3Json.From(subject.Crotch),
                bodyUp = Vec3Json.From(subject.BodyUp),
                bodyRight = Vec3Json.From(subject.BodyRight),
                bodyFront = Vec3Json.From(subject.BodyFront),
                headFront = Vec3Json.From(subject.HeadFront),
                headUp = Vec3Json.From(subject.HeadUp),
                worldUp = Vec3Json.From(Vector3.up),
                boundsMin = Vec3Json.From(subject.BoundsMin),
                boundsMax = Vec3Json.From(subject.BoundsMax),
                boundsSize = Vec3Json.From(subject.BoundsMax - subject.BoundsMin),
                height = subject.Height
            };
        }

        /// <summary>
        /// 真下へレイを飛ばして床を探す。
        /// 「どちらが地面か」はワールドの上向きで決まるが、
        /// 床が実際にどの高さにあるかは撮ってみないと分からないので測る。
        /// </summary>
        private static void FillFloorInfo(SubjectJson json, SubjectResolver subject)
        {
            json.worldUp = Vec3Json.From(Vector3.up);

            Vector3 origin = subject.BoundsCenter + Vector3.up * 0.1f;
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 20f, ~0, QueryTriggerInteraction.Ignore);
            Transform root = subject.Female != null ? subject.Female.transform : null;

            float bestY = float.NegativeInfinity;
            string bestName = string.Empty;
            bool found = false;

            for (int i = 0; i < hits.Length; i++)
            {
                Transform t = hits[i].transform;
                if (t == null) continue;
                if (root != null && t.IsChildOf(root)) continue; // 自分の体は床ではない
                if (hits[i].point.y > bestY)
                {
                    bestY = hits[i].point.y;
                    bestName = t.name;
                    found = true;
                }
            }

            json.foundFloor = found;
            if (found)
            {
                json.floorPoint = Vec3Json.From(new Vector3(subject.BoundsCenter.x, bestY, subject.BoundsCenter.z));
                json.floorName = bestName;
                json.heightAboveFloor = subject.BoundsMin.y - bestY;
            }
            else
            {
                json.floorPoint = Vec3Json.From(Vector3.zero);
                json.heightAboveFloor = 0f;
            }
        }

        private void WriteResponse(AgentResponse response)
        {
            try
            {
                Directory.CreateDirectory(_outDir);
                string path = Path.Combine(_outDir, (string.IsNullOrEmpty(response.id) ? "unknown" : response.id) + ".json");
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, Serialize(response), Utf8NoBom);
                // 中途半端な内容を読ませないため、書き終えてから名前を付け替える。
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Log("failed to write response: " + ex.Message);
            }
        }

        /// <summary>
        /// 応答を JSON 文字列にする。JsonUtility は入れ子のクラスを落とすので使わない。
        /// </summary>
        private static string Serialize(AgentResponse r)
        {
            JsonWriter w = new JsonWriter()
                .Str("id", r.id)
                .Str("cmd", r.cmd)
                .Bool("ok", r.ok)
                .Str("error", r.error)
                .Str("message", r.message)
                .Str("path", r.path)
                .Str("cleanPath", r.cleanPath)
                .Str("sheetPath", r.sheetPath)
                .Str("heldBy", r.heldBy);

            if (r.bones != null)
                w.Strings("bones", r.bones);

            if (!string.IsNullOrEmpty(r.shotsJson))
                w.Raw("shots", r.shotsJson);

            if (r.hasApplied)
            {
                w.Begin("applied")
                    .Num("yaw", r.appliedYaw)
                    .Num("pitch", r.appliedPitch)
                    .Num("distance", r.appliedDistance)
                    .Num("fov", r.appliedFov)
                    .Bool("distanceAdjusted", r.distanceAdjusted)
                    .Bool("yawAdjusted", r.yawAdjusted)
                    .Bool("blocked", r.blocked)
                    .Str("blocker", r.blocker)
                    .End();
            }

            if (r.camera != null)
            {
                CameraStateJson c = r.camera;
                w.Begin("camera")
                    .Bool("hasController", c.hasController)
                    .Num("fov", c.fov)
                    .Num("distance", c.distance)
                    .Bool("mouseControlBlocked", c.mouseControlBlocked)
                    .Bool("keyControlBlocked", c.keyControlBlocked)
                    .Str("blockedBy", c.blockedBy);
                WriteTriple(w, "pos", c.pos);
                WriteTriple(w, "rot", c.rot);
                WriteTriple(w, "dir", c.dir);
                WriteVec(w, "worldPosition", c.worldPosition);
                WriteVec(w, "worldLookAt", c.worldLookAt);
                w.End();
            }

            if (r.subject != null)
            {
                SubjectJson s = r.subject;
                w.Begin("subject")
                    .Bool("found", s.found)
                    .Str("reason", s.reason)
                    .Num("height", s.height)
                    .Bool("foundBust", s.foundBust)
                    .Bool("foundButt", s.foundButt)
                    .Strings("targets", s.targets);
                WriteVec(w, "head", s.head);
                WriteVec(w, "bust", s.bust);
                WriteVec(w, "chest", s.chest);
                WriteVec(w, "waist", s.waist);
                WriteVec(w, "hips", s.hips);
                WriteVec(w, "butt", s.butt);
                WriteVec(w, "crotch", s.crotch);
                WriteVec(w, "bodyUp", s.bodyUp);
                WriteVec(w, "bodyRight", s.bodyRight);
                WriteVec(w, "bodyFront", s.bodyFront);
                WriteVec(w, "headFront", s.headFront);
                WriteVec(w, "headUp", s.headUp);
                WriteVec(w, "worldUp", s.worldUp);
                WriteVec(w, "boundsMin", s.boundsMin);
                WriteVec(w, "boundsMax", s.boundsMax);
                WriteVec(w, "boundsSize", s.boundsSize);
                WriteVec(w, "floorPoint", s.floorPoint);
                w.Bool("foundFloor", s.foundFloor)
                 .Str("floorName", s.floorName)
                 .Num("heightAboveFloor", s.heightAboveFloor);
                w.End();
            }

            return w.Finish();
        }

        private static void WriteTriple(JsonWriter w, string key, float[] v)
        {
            if (v == null || v.Length < 3) return;
            w.Vec(key, new Vector3(v[0], v[1], v[2]));
        }

        private static void WriteVec(JsonWriter w, string key, Vec3Json v)
        {
            if (v == null) return;
            w.Vec(key, new Vector3(v.x, v.y, v.z));
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); } catch { }
        }

        private static string Describe(AgentRequest req)
        {
            return string.IsNullOrEmpty(req.agent) ? "unknown" : req.agent;
        }

        private void ResetLog()
        {
            if (!_runtimeLoggingEnabled) return;
            if (_cfgLogEnabled == null || !_cfgLogEnabled.Value) return;
            try
            {
                File.WriteAllText(_logPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                    + " === session start ===" + Environment.NewLine, Utf8NoBom);
            }
            catch { }
        }

        private void Log(string message)
        {
            if (!_runtimeLoggingEnabled) return;
            if (_cfgLogEnabled == null || !_cfgLogEnabled.Value) return;
            Logger.LogInfo("[" + PluginName + "] " + message);
            try
            {
                File.AppendAllText(_logPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                    + " " + message + Environment.NewLine, Utf8NoBom);
            }
            catch { }
        }
    }
}
