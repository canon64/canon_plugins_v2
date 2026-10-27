using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MainGameUiInputCapture;
using MainGameUnifiedUi;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameAutoHVoice
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInDependency(MainGameUiInputCapture.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
    internal sealed class Plugin : BaseUnityPlugin
    {
        private static bool RuntimeLoggingEnabled = false;
        public const string GUID = "com.kks.maingame.autohvoice";
        public const string PluginName = "MainGameAutoHVoice";
        public const string Version = "0.1.0";

        private const string InputOwnerKey = GUID + ".input";
        private const string InputSourceWindow = "window";
        private const float WindowDragHandleHeight = 20f;

        internal static Plugin Instance { get; private set; }
        internal static new ManualLogSource Logger { get; private set; }
        internal static string PluginDir { get; private set; }
        internal static string LogFilePath { get; private set; }
        internal static PluginSettings Settings { get; private set; }
        internal static HSceneProc CurrentProc { get; private set; }

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly object LogLock = new object();

        private Harmony _harmony;
        private Rect _windowRect = new Rect(40f, 40f, 500f, 360f);
        private int _windowId;
        private bool _guiVisible = true;
        private bool _windowPointerActive;
        private bool _windowDragging;
        private int _lastCapturedRaw = -1;
        private int _lastCapturedMain = -1;
        private float _lastCapturedTime = -999f;
        private float _lastAutoTriggerTime = -999f;
        private string _status = string.Empty;
        private string _lastLoggedStatus = string.Empty;
        private string _targetMainIndexBuffer = "0";
        private string _autoIntervalBuffer = "18";
        private string _minimumSpacingBuffer = "6";
        private string _captureExpireBuffer = "45";

        private void Awake()
        {
            Instance = this;
            Logger = base.Logger;
            PluginDir = Path.GetDirectoryName(Info.Location) ?? AppDomain.CurrentDomain.BaseDirectory;
            LogFilePath = Path.Combine(PluginDir, "MainGameAutoHVoice.log");
            _windowId = GUID.GetHashCode();

            Directory.CreateDirectory(PluginDir);
            File.WriteAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss}] === {PluginName} {Version} started ==={Environment.NewLine}", Utf8NoBom);

            Settings = SettingsStore.LoadOrCreate(PluginDir, Log, LogWarn, LogError);
            ApplyRuntimeSettings();

            _harmony = new Harmony(GUID);
            _harmony.PatchAll(typeof(Plugin));
            Log("awake complete");
        }

        private void OnDestroy()
        {
            ReleaseInputCapture();

            try
            {
                _harmony?.UnpatchSelf();
            }
            catch (Exception ex)
            {
                LogWarn("unpatch failed: " + ex.Message);
            }
        }

        private const string UnifiedTabId = "autohvoice";
        private const string UnifiedTabLabel = "AutoHVoice";
        private const int UnifiedTabOrder = 50;

        private bool IsHostedByUnifiedUi()
        {
            return UnifiedUiApi.IsAvailable && UnifiedUiApi.IsHosting(UnifiedTabId);
        }

        private void RegisterUnifiedTabIfAvailable()
        {
            if (UnifiedUiApi.IsAvailable && !UnifiedUiApi.IsHosting(UnifiedTabId))
                UnifiedUiApi.RegisterTab(UnifiedTabId, UnifiedTabLabel, DrawUnifiedContent, UnifiedTabOrder);
        }

        private void DrawUnifiedContent()
        {
            DrawAutoHVoiceBody();
        }

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            RegisterUnifiedTabIfAvailable();
            UpdateWindowDragState();
            UpdateInputCapture();
            TickAutoVoice();
        }

        private void OnGUI()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            // 総合UIがタブ表示する間は自前窓を出さない。
            if (IsHostedByUnifiedUi())
                return;

            if (Settings == null || !_guiVisible)
                return;

            HandleWindowPointer(Event.current);
            _windowRect = GUI.Window(_windowId, _windowRect, DrawWindow, PluginName);
        }

        private void DrawWindow(int id)
        {
            DrawAutoHVoiceBody();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, WindowDragHandleHeight));
        }

        private void DrawAutoHVoiceBody()
        {
            // 絶対座標GUIの実表示範囲をGUILayoutにも知らせ、統合UIが
            // この領域だけを入力遮断対象として測定できるようにする。
            GUILayoutUtility.GetRect(500f, 330f, GUILayout.Width(500f), GUILayout.Height(330f));

            float labelW = 170f;
            float fieldW = 90f;
            float lineH = 22f;
            float y = 24f;

            GUI.Label(new Rect(12f, y, 470f, 20f), "Captured Proc: " + (CurrentProc != null));
            y += lineH;
            GUI.Label(new Rect(12f, y, 470f, 20f), "Last raw: " + FormatLastRawLabel());
            y += lineH;
            GUI.Label(new Rect(12f, y, 470f, 20f), "Capture age: " + FormatAgeLabel(_lastCapturedTime));
            y += lineH;
            GUI.Label(new Rect(12f, y, 470f, 20f), "Since auto: " + FormatAgeLabel(_lastAutoTriggerTime));
            y += 28f;

            bool nextEnabled = GUI.Toggle(new Rect(12f, y, 120f, 22f), Settings.Enabled, "Auto");
            if (nextEnabled != Settings.Enabled)
            {
                Settings.Enabled = nextEnabled;
                PersistSettings();
                SetStatus("enabled=" + Settings.Enabled);
            }

            bool nextVerbose = GUI.Toggle(new Rect(138f, y, 120f, 22f), Settings.VerboseLog, "詳細ログ");
            if (nextVerbose != Settings.VerboseLog)
            {
                Settings.VerboseLog = nextVerbose;
                PersistSettings();
                SetStatus("verboseLog=" + Settings.VerboseLog);
            }

            bool nextRequireModeMatch = GUI.Toggle(new Rect(264f, y, 120f, 22f), Settings.RequireModeMatch, "Mode一致");
            if (nextRequireModeMatch != Settings.RequireModeMatch)
            {
                Settings.RequireModeMatch = nextRequireModeMatch;
                PersistSettings();
                SetStatus("requireModeMatch=" + Settings.RequireModeMatch);
            }

            bool nextAllowManualNoCapture = GUI.Toggle(new Rect(390f, y, 100f, 22f), Settings.AllowManualTriggerWhenNoCapture, "未捕捉許可");
            if (nextAllowManualNoCapture != Settings.AllowManualTriggerWhenNoCapture)
            {
                Settings.AllowManualTriggerWhenNoCapture = nextAllowManualNoCapture;
                PersistSettings();
                SetStatus("allowManualNoCapture=" + Settings.AllowManualTriggerWhenNoCapture);
            }

            y += 30f;
            DrawLabeledTextField("Main Index", ref _targetMainIndexBuffer, ref y, labelW, fieldW, TryApplyTargetMainIndex);
            DrawLabeledTextField("Auto Interval", ref _autoIntervalBuffer, ref y, labelW, fieldW, TryApplyAutoIntervalSeconds);
            DrawLabeledTextField("Min Spacing", ref _minimumSpacingBuffer, ref y, labelW, fieldW, TryApplyMinimumSpacingSeconds);
            DrawLabeledTextField("Capture Expire", ref _captureExpireBuffer, ref y, labelW, fieldW, TryApplyCaptureExpireSeconds);

            y += 8f;
            if (GUI.Button(new Rect(12f, y, 234f, 30f), "Speak Now"))
                TriggerCurrentVoice("gui-manual", ignoreCaptureAge: true);

            if (GUI.Button(new Rect(256f, y, 234f, 30f), "Reload Settings"))
            {
                Settings = SettingsStore.LoadOrCreate(PluginDir, Log, LogWarn, LogError);
                ApplyRuntimeSettings();
                SetStatus("settings reloaded");
            }

            y += 38f;
            if (!string.IsNullOrWhiteSpace(_status))
                GUI.Label(new Rect(12f, y, 470f, 36f), _status);
        }

        private void DrawLabeledTextField(string label, ref string buffer, ref float y, float labelW, float fieldW, Func<string, bool> apply)
        {
            GUI.Label(new Rect(12f, y, labelW, 20f), label);
            string next = GUI.TextField(new Rect(12f + labelW, y - 2f, fieldW, 22f), buffer ?? string.Empty);
            if (!string.Equals(next, buffer, StringComparison.Ordinal))
                buffer = next;

            if (GUI.Button(new Rect(12f + labelW + fieldW + 8f, y - 2f, 60f, 22f), "Apply"))
                apply?.Invoke(buffer);

            y += 26f;
        }

        private void ApplyRuntimeSettings()
        {
            NormalizeSettings();
            _guiVisible = Settings.ShowGui;
            _windowRect.x = Settings.WindowX;
            _windowRect.y = Settings.WindowY;
            SyncBuffersFromSettings();
        }

        private void NormalizeSettings()
        {
            if (Settings == null)
                return;

            Settings.TargetMainIndex = Mathf.Clamp(Settings.TargetMainIndex, 0, 3);
            Settings.AutoIntervalSeconds = Mathf.Clamp(Settings.AutoIntervalSeconds, 0.5f, 600f);
            Settings.MinimumSpacingSeconds = Mathf.Clamp(Settings.MinimumSpacingSeconds, 0.1f, 600f);
            Settings.CaptureExpireSeconds = Mathf.Clamp(Settings.CaptureExpireSeconds, 1f, 3600f);
        }

        private void SyncBuffersFromSettings()
        {
            _targetMainIndexBuffer = Settings.TargetMainIndex.ToString(CultureInfo.InvariantCulture);
            _autoIntervalBuffer = Settings.AutoIntervalSeconds.ToString("0.##", CultureInfo.InvariantCulture);
            _minimumSpacingBuffer = Settings.MinimumSpacingSeconds.ToString("0.##", CultureInfo.InvariantCulture);
            _captureExpireBuffer = Settings.CaptureExpireSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private void TickAutoVoice()
        {
            if (!Settings.Enabled)
                return;

            if (Time.unscaledTime - _lastAutoTriggerTime < Mathf.Max(0.5f, Settings.AutoIntervalSeconds))
                return;

            TriggerCurrentVoice("auto", ignoreCaptureAge: false);
        }

        private bool TriggerCurrentVoice(string source, bool ignoreCaptureAge)
        {
            HSceneProc proc = CurrentProc;
            if (proc == null)
            {
                SetStatus("no HSceneProc", verboseOnly: true);
                return false;
            }

            if (proc.flags == null || proc.flags.voice == null || proc.flags.voice.playVoices == null)
            {
                SetStatus("voice flags not ready", verboseOnly: true);
                return false;
            }

            int main = Mathf.Clamp(Settings.TargetMainIndex, 0, proc.flags.voice.playVoices.Length - 1);
            if (_lastCapturedRaw < 0)
            {
                if (!ignoreCaptureAge || !Settings.AllowManualTriggerWhenNoCapture)
                {
                    SetStatus("no captured voice", verboseOnly: true);
                    return false;
                }
            }

            if (!ignoreCaptureAge)
            {
                float age = Time.unscaledTime - _lastCapturedTime;
                if (_lastCapturedTime <= 0f || age > Mathf.Max(1f, Settings.CaptureExpireSeconds))
                {
                    SetStatus("capture expired", verboseOnly: true);
                    return false;
                }
            }

            if (proc.flags.voice.playVoices[main] != -1)
            {
                SetStatus("playVoices pending", verboseOnly: true);
                return false;
            }

            if (Time.unscaledTime - _lastAutoTriggerTime < Mathf.Max(0.1f, Settings.MinimumSpacingSeconds))
            {
                SetStatus("spacing wait", verboseOnly: true);
                return false;
            }

            if (Settings.RequireModeMatch)
            {
                int currentMode = (int)proc.flags.mode;
                int capturedMode = _lastCapturedRaw / 100;
                if (currentMode != capturedMode)
                {
                    SetStatus($"mode mismatch current={currentMode} captured={capturedMode}", verboseOnly: true);
                    return false;
                }
            }

            proc.flags.voice.playVoices[main] = _lastCapturedRaw;
            _lastAutoTriggerTime = Time.unscaledTime;
            SetStatus($"trigger {source}: raw={_lastCapturedRaw}");
            Log($"[trigger] source={source} main={main} raw={_lastCapturedRaw} mode={_lastCapturedRaw / 100} key={_lastCapturedRaw % 100}");
            return true;
        }

        private void CapturePlayVoiceRaw(HVoiceCtrl voiceCtrl, int main)
        {
            HSceneProc proc = CurrentProc;
            if (proc == null || proc.voice != voiceCtrl)
                return;
            if (proc.flags == null || proc.flags.voice == null || proc.flags.voice.playVoices == null)
                return;
            if (main < 0 || main >= proc.flags.voice.playVoices.Length)
                return;

            int raw = proc.flags.voice.playVoices[main];
            if (raw < 0)
                return;

            _lastCapturedRaw = raw;
            _lastCapturedMain = main;
            _lastCapturedTime = Time.unscaledTime;
            if (Settings.VerboseLog)
                Log($"[capture] main={main} raw={raw} mode={raw / 100} key={raw % 100}");
        }

        private bool TryApplyTargetMainIndex(string text)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) &&
                !int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
                return false;

            Settings.TargetMainIndex = Mathf.Clamp(parsed, 0, 3);
            PersistSettings();
            SyncBuffersFromSettings();
            SetStatus("targetMain=" + Settings.TargetMainIndex);
            return true;
        }

        private bool TryApplyAutoIntervalSeconds(string text)
        {
            if (!TryParseFloat(text, out float parsed))
                return false;

            Settings.AutoIntervalSeconds = Mathf.Clamp(parsed, 0.5f, 600f);
            PersistSettings();
            SyncBuffersFromSettings();
            SetStatus("autoInterval=" + Settings.AutoIntervalSeconds.ToString("0.##", CultureInfo.InvariantCulture));
            return true;
        }

        private bool TryApplyMinimumSpacingSeconds(string text)
        {
            if (!TryParseFloat(text, out float parsed))
                return false;

            Settings.MinimumSpacingSeconds = Mathf.Clamp(parsed, 0.1f, 600f);
            PersistSettings();
            SyncBuffersFromSettings();
            SetStatus("minSpacing=" + Settings.MinimumSpacingSeconds.ToString("0.##", CultureInfo.InvariantCulture));
            return true;
        }

        private bool TryApplyCaptureExpireSeconds(string text)
        {
            if (!TryParseFloat(text, out float parsed))
                return false;

            Settings.CaptureExpireSeconds = Mathf.Clamp(parsed, 1f, 3600f);
            PersistSettings();
            SyncBuffersFromSettings();
            SetStatus("captureExpire=" + Settings.CaptureExpireSeconds.ToString("0.##", CultureInfo.InvariantCulture));
            return true;
        }

        private void PersistSettings()
        {
            NormalizeSettings();
            Settings.ShowGui = _guiVisible;
            Settings.WindowX = _windowRect.x;
            Settings.WindowY = _windowRect.y;
            SettingsStore.Save(Path.Combine(PluginDir, "AutoHVoiceSettings.json"), Settings);
        }

        private void SetStatus(string text, bool verboseOnly = false)
        {
            _status = text;
            if (string.Equals(_lastLoggedStatus, text, StringComparison.Ordinal))
                return;
            if (verboseOnly && !Settings.VerboseLog)
                return;

            _lastLoggedStatus = text;
            Log(text);
        }

        public static bool TryGetUiVisible(out bool visible)
        {
            visible = false;
            if (Instance == null || Settings == null)
                return false;

            visible = Instance._guiVisible;
            return true;
        }

        public static bool TrySetUiVisible(bool visible)
        {
            if (Instance == null || Settings == null)
                return false;

            Instance._guiVisible = visible;
            Settings.ShowGui = visible;
            Instance.PersistSettings();
            Instance.SetStatus("showGui=" + visible);
            return true;
        }

        private void HandleWindowPointer(Event evt)
        {
            if (evt == null || !_guiVisible)
                return;

            bool insideWindow = _windowRect.Contains(evt.mousePosition);
            _windowPointerActive = insideWindow;
            if (!insideWindow)
                return;

            if (evt.isMouse || evt.type == EventType.ScrollWheel)
                evt.Use();
        }

        private void UpdateWindowDragState()
        {
            bool standaloneVisible = _guiVisible && !IsHostedByUnifiedUi();
            if (!standaloneVisible)
            {
                _windowDragging = false;
                _windowPointerActive = false;
                return;
            }

            Vector2 mouseGui = GetMousePositionInGuiSpace();
            _windowPointerActive = _windowRect.Contains(mouseGui);

            if (Input.GetMouseButtonDown(0) && GetTitleBarRect().Contains(mouseGui))
            {
                _windowDragging = true;
                return;
            }

            if (!Input.GetMouseButton(0))
                _windowDragging = false;
        }

        private Rect GetTitleBarRect()
        {
            return new Rect(_windowRect.x, _windowRect.y, _windowRect.width, WindowDragHandleHeight);
        }

        private static Vector2 GetMousePositionInGuiSpace()
        {
            Vector3 mouse = Input.mousePosition;
            return new Vector2(mouse.x, Screen.height - mouse.y);
        }

        private void UpdateInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable)
                return;

            bool standaloneVisible = _guiVisible && !IsHostedByUnifiedUi();
            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, standaloneVisible);
            UiInputCaptureApi.Sync(
                InputOwnerKey,
                InputSourceWindow,
                standaloneVisible && (_windowPointerActive || _windowDragging));
        }

        private void ReleaseInputCapture()
        {
            if (!UiInputCaptureApi.IsAvailable)
                return;

            UiInputCaptureApi.SetIdleCursorUnlock(InputOwnerKey, false);
            UiInputCaptureApi.EndOwner(InputOwnerKey);
            _windowPointerActive = false;
            _windowDragging = false;
        }

        private string FormatLastRawLabel()
        {
            if (_lastCapturedRaw < 0)
                return "none";

            return $"{_lastCapturedRaw} (main={_lastCapturedMain}, mode={_lastCapturedRaw / 100}, key={_lastCapturedRaw % 100})";
        }

        private static string FormatAgeLabel(float timestamp)
        {
            if (timestamp <= 0f)
                return "none";

            return (Time.unscaledTime - timestamp).ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        private static bool TryParseFloat(string text, out float value)
        {
            if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;

            return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
        }

        private static void Log(string message)
        {
            if (!RuntimeLoggingEnabled) return;
            Logger?.LogInfo(message);
            WriteLogLine(message);
        }

        private static void LogWarn(string message)
        {
            if (!RuntimeLoggingEnabled) return;
            Logger?.LogWarning(message);
            WriteLogLine("[warn] " + message);
        }

        private static void LogError(string message)
        {
            if (!RuntimeLoggingEnabled) return;
            Logger?.LogError(message);
            WriteLogLine("[error] " + message);
        }

        private static void WriteLogLine(string message)
        {
            if (string.IsNullOrWhiteSpace(LogFilePath))
                return;

            lock (LogLock)
            {
                File.AppendAllText(LogFilePath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}", Utf8NoBom);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneProc), "Start")]
        private static void HSceneStartPostfix(HSceneProc __instance)
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            CurrentProc = __instance;
            Log("captured HSceneProc.Start");
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(HSceneProc), "OnDestroy")]
        private static void HSceneOnDestroyPostfix(HSceneProc __instance)
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (ReferenceEquals(CurrentProc, __instance))
            {
                CurrentProc = null;
                if (Instance != null)
                    Instance._lastCapturedRaw = -1;
                Log("released HSceneProc");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(HVoiceCtrl), "VoiceProc")]
        private static void HVoiceCtrlVoiceProcPrefix(HVoiceCtrl __instance, int _main)
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            Instance?.CapturePlayVoiceRaw(__instance, _main);
        }
    }
}
