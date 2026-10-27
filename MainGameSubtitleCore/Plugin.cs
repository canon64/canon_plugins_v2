using Bindings = CanonPlugins.Shared.PluginInput;
using ActionGame;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using CanonPlugins.Shared;

namespace MainGameSubtitleCore
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    internal sealed partial class Plugin : BaseUnityPlugin, ISubtitleRequestSink, ITextForwardSink
    {
        private static bool RuntimeLoggingEnabled = false;
        private enum DisplayBackendMode
        {
            Auto,
            InformationUI,
            Overlay
        }

        private enum InputPanelSection
        {
            None,
            History,
            Preset
        }

        private struct InputPanelSkin
        {
            public Font Font;
            public Sprite PanelSprite;
            public Image.Type PanelType;
            public Color PanelColor;
            public Material PanelMaterial;
            public Sprite ButtonSprite;
            public Image.Type ButtonType;
            public Color ButtonColor;
            public Material ButtonMaterial;
            public ColorBlock ButtonColors;
        }

        [Serializable]
        private sealed class ForwardTextPayload
        {
            public string text;
            public string source;
            public string event_id;
            public long sent_at_unix_ms;
            public bool direct_change;
        }

        public const string GUID = "com.kks.maingame.subtitlecore";
        public const string PluginName = "MainGameSubtitleCore";
        public const string Version = "0.4.5";

        internal static new ManualLogSource Logger { get; private set; }

        private static readonly object FileLogLock = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo WaitTimeField = typeof(InformationUI).GetField("_waitTime", PrivateInstance);
        private static readonly Regex OverlayColorValueTagRegex =
            new Regex(@"<color\s*=\s*['""]?(?<value>[^>""']+)['""]?\s*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Type HSceneProcType = Type.GetType("HSceneProc, Assembly-CSharp");
        private static readonly Type VRHSceneType = Type.GetType("VRHScene, Assembly-CSharp");

        private static string _pluginDir;
        private static string _logFilePath;
        private static string _inputPanelStatePath;
        private static PluginSettings _settings;
        private static Plugin _instance;
        private static DisplayBackendMode _displayBackend = DisplayBackendMode.Auto;
        private static float _overlayUntil;
        private static float _overlayStartedAt;
        private static string _overlayText = string.Empty;
        private static string _overlayUpperText = string.Empty;
        private static string _overlayShadowText = string.Empty;
        private static string _overlayUpperShadowText = string.Empty;
        private static readonly GUIContent OverlayContent = new GUIContent(string.Empty);
        private static readonly GUIContent OverlayUpperContent = new GUIContent(string.Empty);
        private static float _overlayUpperUntil;
        private static GUIStyle _overlayStyle;
        private static GUIStyle _overlayShadowStyle;
        private static Color _overlayTextColor = Color.white;
        private static Color _overlayShadowColor = Color.black;
        private static Color _overlayIntroHighlightColor = Color.white;
        private static Font _overlayResolvedFont;
        private static string _overlayResolvedFontSource = string.Empty;
        private static float _nextOverlayFontResolveTime;
        private static bool _femaleKeepVisibleUntilNext = false;
        private static float _femaleMaxHoldSeconds = 30f;
        // Template clone often carries scene-specific components and breaks click routing.
        // Keep skin sampling from templates, but build runtime controls explicitly.
        private const bool EnableTemplateControlClone = false;
        private static bool _inputPanelVisible;
        private ConfigEntry<bool> _cfgKeepOpenOnOutsideClick;
        private ConfigEntry<bool> _cfgKeepOpenAfterSend;
        private ConfigEntry<float> _cfgCancelSubtitleClearSeconds;
        private ConfigEntry<float> _cfgFinishSubtitleClearSeconds;
        private Button _voiceCancelButton;
        private Button _micToggleButton;
        private Button _directChangeButton;
        private bool _directChangeEnabled;
        private bool _directChangePending;
        private bool _directChangeTarget;
        private bool _directChangeResultReady;
        private bool _directChangeResultOk;
        private bool _directChangeSynced;
        private float _nextDirectChangeRetry;
        private int _directChangeGeneration;
        private readonly object _directChangeLock = new object();
        private static bool _micSendEnabled = true;
        private bool _micStatePending;
        private bool _micStateSynced;
        private bool _micStateSentTarget;
        private bool _micStateResultReady;
        private bool _micStateResultOk;
        private float _nextMicStateRetry;
        private readonly object _micStateLock = new object();
        // ドラッグで動かした位置を実行中ずっと保持する。
        // 設定の保存値を見ていると、離すまで保存されないので毎回中央へ戻ってしまう。
        private Vector2 _inputPanelPos;
        private bool _inputPanelMoved;
        private static bool _focusInputNextFrame;
        private static bool _isHSceneCached;
        private static float _hSceneCachedUntil;

        private readonly Queue<SubtitleRequest> _externalRequests = new Queue<SubtitleRequest>();
        private readonly object _externalRequestsLock = new object();
        private readonly List<string> _inputHistory = new List<string>();
        private readonly List<string> _inputPresets = new List<string>();
        private Canvas _inputCanvas;
        private RectTransform _inputPanelRect;
        // Update監視と onEndEdit の二重発火を抑えるための直近送信時刻。
        private float _lastInputSendTime;
        private const float InputSendDebounceSeconds = 0.25f;
        // クリックでフォーカスが外れた onEndEdit を送信と区別するための直近クリック時刻。
        private float _lastInputPanelMouseDownTime = -10f;
        private const float EndEditMouseGraceSeconds = 0.3f;
        // パネルを閉じる等、こちらの都合で外した時の onEndEdit を送信にしないための抑止。
        private bool _suppressEndEditSend;
        private static readonly List<RaycastResult> _uiRaycastResults = new List<RaycastResult>();
        private InputField _inputField;
        private RectTransform _inputPanelTemplate;
        private RectTransform _inputSectionRoot;
        private RectTransform _historySectionRoot;
        private RectTransform _presetSectionRoot;
        private LayoutElement _historySectionLayoutElement;
        private LayoutElement _presetSectionLayoutElement;
        private RectTransform _historyContent;
        private RectTransform _presetContent;
        private Text _historyHeaderText;
        private Text _presetHeaderText;
        private Button _historyToggleButton;
        private Button _presetToggleButton;
        private Button _buttonTemplate;
        private Vector2 _buttonTemplateSize = new Vector2(176f, 60f);
        private InputField _inputFieldTemplate;
        private Vector2 _inputFieldTemplateSize = new Vector2(192f, 40f);
        private InputPanelSection _activeInputSection;
        private bool _inputPanelSkinResolved;
        private bool _inputPanelTemplateReady;
        private InputPanelSkin _inputPanelSkin;
        private EventSystem _eventSystem;
        private ConfigEntry<int> _cfgOverlayFontSize;
        private ConfigEntry<float> _cfgDefaultHoldSeconds;
        private ConfigEntry<bool> _cfgFemaleKeepVisibleUntilNext;
        private ConfigEntry<float> _cfgFemaleMaxHoldSeconds;

        private void Awake()
        {
            _instance = this;
            Logger = base.Logger;
            _pluginDir = Path.GetDirectoryName(Info.Location);
            _logFilePath = Path.Combine(_pluginDir, PluginName + ".log");
            _inputPanelStatePath = Path.Combine(_pluginDir, "SubtitleInputPanelState.json");

            Directory.CreateDirectory(_pluginDir);
            ReloadSettings();
            BindRuntimeConfig();
            ApplyRuntimeConfigOverrides();
            ReloadInputPanelState();
            NotifyMicSendState();
            RequestDirectChangeState(_directChangeEnabled);
            SubtitleApi.Register(this);
            SubtitleApi.RegisterForwardSink(this);
            Log("awake complete");
        }

        private void OnDestroy()
        {
            SaveInputPanelState();
            if (_inputCanvas != null)
            {
                UnityEngine.Object.Destroy(_inputCanvas.gameObject);
                _inputCanvas = null;
            }
            if (_eventSystem != null &&
                _eventSystem.gameObject != null &&
                _eventSystem.gameObject.transform.parent == transform &&
                string.Equals(_eventSystem.gameObject.name, "MainGameSubtitleEventSystem", StringComparison.Ordinal))
            {
                UnityEngine.Object.Destroy(_eventSystem.gameObject);
                _eventSystem = null;
            }
            SubtitleApi.UnregisterForwardSink(this);
            SubtitleApi.Unregister(this);
        }

        private void Update()
        {
            PollMicSendState();
            if (!_micStatePending && Time.unscaledTime >= _nextMicStateRetry)
            {
                _nextMicStateRetry = Time.unscaledTime + 5f;
                NotifyMicSendState();
            }
            PollDirectChangeResult();
            if (!_directChangePending && !_directChangeSynced && Time.unscaledTime >= _nextDirectChangeRetry)
            {
                _nextDirectChangeRetry = Time.unscaledTime + 5f;
                RequestDirectChangeState(_directChangeTarget);
            }
            PollDirectChangeRulesResult();
            if (!_directChangeRulesPending && _directChangeRulesDirty && Time.unscaledTime >= _nextDirectChangeRulesRetry)
            {
                _nextDirectChangeRulesRetry = Time.unscaledTime + 5f;
                RequestDirectChangeRules();
            }
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (_settings != null && _settings.EnableCtrlRReload && IsCtrlRDown())
            {
                ReloadSettings();
                ReloadInputPanelState();
                NotifyMicSendState();
                RequestDirectChangeState(_directChangeEnabled);
                Log("settings reloaded by Ctrl+R");
            }

            if (_settings == null || !_settings.Enabled)
            {
                _inputPanelVisible = false;
                SetInputPanelVisible(false);
                return;
            }

            HandleInputPanelHotkey();
            UpdateInputPanelRuntime();
            DrainExternalRequests(8);

            if (Bindings.Down("MainGameSubtitleCore.test", _settings.TriggerKey))
            {
                ShowSubtitleCore(
                    _settings.TestText,
                    _settings.HoldSeconds,
                    _displayBackend,
                    _settings.DisplayMode,
                    "hotkey");
            }
        }

        private void OnGUI()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (_settings == null || !_settings.Enabled)
            {
                return;
            }

            bool hasOverlay = Time.unscaledTime <= _overlayUntil || Time.unscaledTime <= _overlayUpperUntil;
            if (!hasOverlay)
            {
                return;
            }

            if (hasOverlay)
            {
                EnsureOverlayResources();

                float drawScale = 1f;
                float drawYOffset = 0f;
                float drawAlphaMul = 1f;
                Color drawTextColorBase = _overlayTextColor;
                if (_settings.OverlayIntroEnabled && _settings.OverlayIntroDuration > 0f)
                {
                    float elapsed = Mathf.Max(0f, Time.unscaledTime - _overlayStartedAt);
                    float duration = Mathf.Max(0.01f, _settings.OverlayIntroDuration);
                    if (elapsed < duration)
                    {
                        float t = Mathf.Clamp01(elapsed / duration);
                        float eased = EaseOutBack01(t, _settings.OverlayIntroBackOvershoot);
                        drawScale = Mathf.Lerp(_settings.OverlayIntroScaleFrom, 1f, eased);
                        drawYOffset = Mathf.Lerp(_settings.OverlayIntroYOffset, 0f, t);
                        drawAlphaMul = Mathf.Clamp01(t);

                        float hiDur = Mathf.Max(0f, _settings.OverlayIntroHighlightDuration);
                        if (hiDur > 0f && elapsed < hiDur)
                        {
                            float hiT = Mathf.Clamp01(elapsed / hiDur);
                            drawTextColorBase = Color.Lerp(_overlayIntroHighlightColor, _overlayTextColor, hiT);
                        }
                    }
                }

                int baseFontSize = _settings.OverlayFontSize;
                int drawFontSize = Mathf.Clamp(Mathf.RoundToInt(baseFontSize * Mathf.Max(1f, drawScale)), 12, 160);
                _overlayStyle.fontSize = drawFontSize;
                _overlayShadowStyle.fontSize = drawFontSize;

                Color drawTextColor = drawTextColorBase;
                drawTextColor.a *= drawAlphaMul;
                _overlayStyle.normal.textColor = drawTextColor;

                Color drawShadowColor = _overlayShadowColor;
                drawShadowColor.a *= drawAlphaMul;
                _overlayShadowStyle.normal.textColor = drawShadowColor;

                float marginX = Mathf.Clamp(_settings.OverlayHorizontalMargin, 0f, Screen.width * 0.45f);
                float width = Mathf.Max(200f, Screen.width - (marginX * 2f));
                float mainTextHeight = _overlayStyle.CalcHeight(OverlayContent, width);
                bool hasUpper = !string.IsNullOrEmpty(_overlayUpperText) && Time.unscaledTime <= _overlayUpperUntil;
                float upperTextHeight = hasUpper ? _overlayStyle.CalcHeight(OverlayUpperContent, width) : 0f;
                float lineGap = hasUpper ? Mathf.Clamp(_settings.OverlayLineSpacing, 0f, 80f) : 0f;
                float totalHeight = Mathf.Max(32f, mainTextHeight + upperTextHeight + lineGap + 2f);
                float y = Mathf.Max(0f, Screen.height - _settings.OverlayBottomMargin - totalHeight + drawYOffset);
                float x = (Screen.width - width) * 0.5f;

                if (hasUpper)
                {
                    Rect upperRect = new Rect(x, y, width, Mathf.Max(24f, upperTextHeight + 2f));
                    DrawOverlayLine(upperRect, _overlayUpperText, _overlayUpperShadowText);
                    y += upperRect.height + lineGap;
                }

                Rect textRect = new Rect(x, y, width, Mathf.Max(24f, mainTextHeight + 2f));
                DrawOverlayLine(textRect, _overlayText, _overlayShadowText);
            }
        }

        private static readonly Regex ColorTagRegex = new Regex(@"<color=[^>]*>|</color>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static string StripColorTags(string text)
        {
            return text == null ? string.Empty : ColorTagRegex.Replace(text, string.Empty);
        }

        private static void DrawOverlayLine(Rect textRect, string text, string shadowText)
        {
            if (_settings == null)
            {
                return;
            }

            if (_settings.OverlayShadowEnabled)
            {
                Rect shadowRect = new Rect(
                    textRect.x + _settings.OverlayShadowOffsetX,
                    textRect.y + _settings.OverlayShadowOffsetY,
                    textRect.width,
                    textRect.height);
                GUI.Label(shadowRect, shadowText, _overlayShadowStyle);
            }

            GUI.Label(textRect, text, _overlayStyle);
        }

        private static bool IsCtrlRDown()
        {
            return Bindings.Down("MainGameSubtitleCore.reload", "R + Ctrl");
        }

        private void HandleInputPanelHotkey()
        {
            if (_settings == null || !_settings.InputPanelEnabled)
            {
                SetInputPanelVisible(false);
                return;
            }

            if (!_settings.InputPanelKeepOpen &&
                _settings.InputPanelCloseWhenHExits &&
                _inputPanelVisible &&
                _settings.InputPanelShowOnlyInH &&
                !IsHSceneActiveCached())
            {
                SetInputPanelVisible(false);
            }

            bool toggleRequested = Bindings.Down("MainGameSubtitleCore.toggle", _settings.InputPanelToggleKey);
            if (!toggleRequested)
            {
                return;
            }

            if (!_settings.InputPanelKeepOpen &&
                _settings.InputPanelShowOnlyInH && !IsHSceneActiveCached())
            {
                if (_settings.VerboseLog)
                {
                    Log("input panel toggle ignored: h scene is not active");
                }
                return;
            }

            SetInputPanelVisible(!_inputPanelVisible);
        }

        private void UpdateInputPanelRuntime()
        {
            if (!_inputPanelVisible)
            {
                return;
            }

            if (_settings == null || !_settings.Enabled || !_settings.InputPanelEnabled)
            {
                SetInputPanelVisible(false);
                return;
            }

            if (!_settings.InputPanelKeepOpen &&
                _settings.InputPanelCloseWhenHExits &&
                _settings.InputPanelShowOnlyInH &&
                !IsHSceneActiveCached())
            {
                SetInputPanelVisible(false);
                return;
            }

            EnsureInputPanelUi();
            if (_inputCanvas == null)
            {
                return;
            }

            if (!_inputCanvas.enabled)
            {
                _inputCanvas.enabled = true;
            }

            // パネルが開いているあいだ、キーボードは入力欄のもの。
            // 他プラグインのホットキー（Enterのトグル等）を黙らせるための表札。
            TextInputState.MarkActive();

            UpdateInputPanelLayout();

            if (Bindings.Down("MainGameSubtitleCore.close", "Escape"))
            {
                SetInputPanelVisible(false);
                return;
            }

            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2))
            {
                _lastInputPanelMouseDownTime = Time.unscaledTime;
            }

            // パネルの外を左クリックしたら閉じる（入力は捨てる）。
            // 外クリックは onEndEdit も呼ぶが、直近クリックの判定で弾かれるため送信されない。
            // KeepOpenOnOutsideClick がONなら閉じない（置きっぱなしで使いたい時用）。
            bool keepOnOutside = _cfgKeepOpenOnOutsideClick != null && _cfgKeepOpenOnOutsideClick.Value;
            if (!keepOnOutside && Bindings.Down("MainGameSubtitleCore.outsideClose", "Mouse0") && !IsPointerOverInputPanel())
            {
                SetInputPanelVisible(false);
                return;
            }

            if (_settings.InputPanelSendOnEnter &&
                Bindings.Down("MainGameSubtitleCore.send", "Return | KeypadEnter"))
            {
                SendInputDraft();
                return;
            }

            if (_focusInputNextFrame)
            {
                FocusInputField(keepNextFrame: false);
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void SetInputPanelVisible(bool visible)
        {
            bool changed = _inputPanelVisible != visible;
            if (!changed && !visible && (_inputCanvas == null || !_inputCanvas.enabled))
                return;
            _inputPanelVisible = visible;

            if (!visible)
            {
                _focusInputNextFrame = false;

                // 選択解除は InputField の onEndEdit をその場で呼ぶ。
                // 閉じる操作を送信と取り違えないよう抑止しておく。
                bool previousSuppress = _suppressEndEditSend;
                _suppressEndEditSend = true;
                try
                {
                    if (_inputCanvas != null)
                    {
                        _inputCanvas.enabled = false;
                    }

                    EventSystem current = EventSystem.current;
                    if (current != null &&
                        _inputField != null &&
                        current.currentSelectedGameObject == _inputField.gameObject)
                    {
                        current.SetSelectedGameObject(null);
                    }
                }
                finally
                {
                    _suppressEndEditSend = previousSuppress;
                }
            }
            else
            {
                if (_settings == null || !_settings.Enabled || !_settings.InputPanelEnabled)
                {
                    _inputPanelVisible = false;
                    return;
                }

                if (!_settings.InputPanelKeepOpen &&
                _settings.InputPanelShowOnlyInH && !IsHSceneActiveCached())
                {
                    _inputPanelVisible = false;
                    return;
                }

                EnsureInputPanelUi();
                if (_inputCanvas == null)
                {
                    _inputPanelVisible = false;
                    return;
                }

                UpdateInputPanelLayout();
                RebuildInputLists();
                _inputCanvas.enabled = true;
                SetActiveInputSection(InputPanelSection.None, focusInput: false);
                FocusInputField(keepNextFrame: true);
            }

            if (_settings != null && _settings.VerboseLog && changed)
            {
                Log("input panel visible=" + _inputPanelVisible);
            }
        }

        private void FocusInputField(bool keepNextFrame)
        {
            _focusInputNextFrame = keepNextFrame;
            if (_inputField == null)
            {
                return;
            }

            EventSystem current = EventSystem.current;
            if (current != null)
            {
                current.SetSelectedGameObject(_inputField.gameObject);
            }

            _inputField.Select();
            _inputField.ActivateInputField();
        }

        private void EnsureInputPanelUi()
        {
            if (_inputCanvas != null)
            {
                return;
            }

            EnsureInputPanelSkin();
            EnsureEventSystem();

            GameObject canvasGo = new GameObject(
                "MainGameSubtitleInputCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            _inputCanvas = canvasGo.GetComponent<Canvas>();
            _inputCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _inputCanvas.sortingOrder = 4500;

            CanvasScaler scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;

            _inputPanelRect = CreateUiRect(
                canvasRect,
                "InputPanel",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                new Vector2(760f, 140f));

            ApplyInputPanelFrameTemplate();

            VerticalLayoutGroup panelLayout = _inputPanelRect.gameObject.AddComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(10, 10, 10, 10);
            panelLayout.spacing = 8f;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            _inputSectionRoot = CreateUiRect(
                _inputPanelRect,
                "InputSection",
                Vector2.zero,
                Vector2.zero,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            LayoutElement inputSectionElement = _inputSectionRoot.gameObject.AddComponent<LayoutElement>();
            inputSectionElement.preferredHeight = 44f;

            HorizontalLayoutGroup inputRowLayout = _inputSectionRoot.gameObject.AddComponent<HorizontalLayoutGroup>();
            inputRowLayout.spacing = 8f;
            inputRowLayout.padding = new RectOffset(0, 0, 0, 0);
            inputRowLayout.childAlignment = TextAnchor.MiddleLeft;
            inputRowLayout.childControlWidth = true;
            inputRowLayout.childControlHeight = true;
            inputRowLayout.childForceExpandWidth = false;
            inputRowLayout.childForceExpandHeight = true;

            _inputField = CreateInputField(_inputSectionRoot, "InputField");
            LayoutElement inputFieldElement = GetOrAddLayoutElement(_inputField.gameObject);
            inputFieldElement.flexibleWidth = 1f;
            inputFieldElement.minWidth = 260f;

            // Enter送信は Update の Input.GetKeyDown では拾えない。
            // 編集中のキーは InputField が消費するため監視側へ届かないので、
            // InputField 自身の「編集完了」通知に繋ぐ。
            // IME使用時はこの通知が変換確定の後に発火するため、
            // 「1回目=変換確定 / 2回目=送信」が自然に成立する。
            _inputField.onEndEdit.AddListener(OnInputFieldEndEdit);

            Button sendButton = CreateButton(_inputSectionRoot, "SendButton", "送信", SendInputDraft);
            LayoutElement sendButtonLayout = GetOrAddLayoutElement(sendButton.gameObject);
            sendButtonLayout.preferredWidth = 100f;
            sendButtonLayout.preferredHeight = 36f;

            Button presetRegisterButton = CreateButton(_inputSectionRoot, "PresetRegisterButton", "プリセット登録", AddPresetFromDraft);
            LayoutElement presetRegisterLayout = GetOrAddLayoutElement(presetRegisterButton.gameObject);
            presetRegisterLayout.preferredWidth = 156f;
            presetRegisterLayout.preferredHeight = 36f;

            RectTransform toggleRow = CreateUiRect(
                _inputPanelRect,
                "ToggleRow",
                Vector2.zero,
                Vector2.zero,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            LayoutElement toggleRowElement = toggleRow.gameObject.AddComponent<LayoutElement>();
            toggleRowElement.preferredHeight = 36f;

            HorizontalLayoutGroup toggleLayout = toggleRow.gameObject.AddComponent<HorizontalLayoutGroup>();
            toggleLayout.spacing = 8f;
            toggleLayout.padding = new RectOffset(0, 0, 0, 0);
            toggleLayout.childAlignment = TextAnchor.MiddleLeft;
            toggleLayout.childControlWidth = true;
            toggleLayout.childControlHeight = true;
            toggleLayout.childForceExpandWidth = false;
            toggleLayout.childForceExpandHeight = true;

            _historyToggleButton = CreateButton(toggleRow, "HistoryToggle", "履歴", () => ToggleInputSection(InputPanelSection.History));
            LayoutElement historyToggleLayout = GetOrAddLayoutElement(_historyToggleButton.gameObject);
            historyToggleLayout.preferredWidth = 130f;
            historyToggleLayout.preferredHeight = 34f;

            _presetToggleButton = CreateButton(toggleRow, "PresetToggle", "プリセット", () => ToggleInputSection(InputPanelSection.Preset));
            LayoutElement presetToggleLayout = GetOrAddLayoutElement(_presetToggleButton.gameObject);
            presetToggleLayout.preferredWidth = 130f;
            presetToggleLayout.preferredHeight = 34f;

            // 女性の発話を止める。VoiceFaceEventBridge の StopFemaleVoice を叩く。
            _voiceCancelButton = CreateButton(toggleRow, "VoiceCancel", "喋りキャンセル", CancelFemaleVoice);
            LayoutElement voiceCancelLayout = GetOrAddLayoutElement(_voiceCancelButton.gameObject);
            voiceCancelLayout.preferredWidth = 150f;
            voiceCancelLayout.preferredHeight = 34f;

            // 自分の音声をAIへ送るかどうか。ここでは状態を持ち、停止信号を外へ送るだけ。
            // 実際に送信を止めるのは Human_2_kks 側（この信号を受けている間は送らない）。
            _micToggleButton = CreateButton(toggleRow, "MicToggle", MicToggleLabel(), ToggleMicSend);
            LayoutElement micToggleLayout = GetOrAddLayoutElement(_micToggleButton.gameObject);
            micToggleLayout.preferredWidth = 170f;
            micToggleLayout.preferredHeight = 34f;

            _directChangeButton = CreateButton(toggleRow, "DirectChangeToggle", DirectChangeLabel(), ToggleDirectChange);
            _directChangeButtonVisual = new DirectChangeButtonVisual(_directChangeButton);
            UpdateDirectChangeVisual();
            LayoutElement directLayout = GetOrAddLayoutElement(_directChangeButton.gameObject);
            directLayout.preferredWidth = 170f;
            directLayout.preferredHeight = 34f;

            _historySectionRoot = CreateUiRect(
                canvasRect,
                "HistorySection",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -80f),
                new Vector2(760f, 300f));
            LayoutElement historySectionElement = _historySectionRoot.gameObject.AddComponent<LayoutElement>();
            historySectionElement.preferredHeight = 300f;
            _historySectionLayoutElement = historySectionElement;
            VerticalLayoutGroup historySectionLayout = _historySectionRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            historySectionLayout.padding = new RectOffset(0, 0, 0, 0);
            historySectionLayout.spacing = 6f;
            historySectionLayout.childControlWidth = true;
            historySectionLayout.childControlHeight = true;
            historySectionLayout.childForceExpandWidth = true;
            historySectionLayout.childForceExpandHeight = false;
            BuildListColumn(_historySectionRoot, "履歴", out _historyHeaderText, out _historyContent);

            _presetSectionRoot = CreateUiRect(
                canvasRect,
                "PresetSection",
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -80f),
                new Vector2(760f, 300f));
            LayoutElement presetSectionElement = _presetSectionRoot.gameObject.AddComponent<LayoutElement>();
            presetSectionElement.preferredHeight = 300f;
            _presetSectionLayoutElement = presetSectionElement;
            VerticalLayoutGroup presetSectionLayout = _presetSectionRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            presetSectionLayout.padding = new RectOffset(0, 0, 0, 0);
            presetSectionLayout.spacing = 6f;
            presetSectionLayout.childControlWidth = true;
            presetSectionLayout.childControlHeight = true;
            presetSectionLayout.childForceExpandWidth = true;
            presetSectionLayout.childForceExpandHeight = false;
            BuildListColumn(_presetSectionRoot, "プリセット", out _presetHeaderText, out _presetContent);

            UpdateInputPanelLayout();
            RebuildInputLists();
            AttachInputPanelDragger();
            SetActiveInputSection(InputPanelSection.None, focusInput: false);
            if (_inputPanelRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_inputPanelRect);
                ReapplyInputPanelPositionAfterRebuild();
            }
            _inputCanvas.enabled = false;
        }

        /// <summary>動かした位置を実際のRectTransformへ入れる。動かしていなければ何もしない。</summary>
        private void ApplyInputPanelPosition()
        {
            if (_inputPanelRect == null || !_inputPanelMoved)
                return;
            _inputPanelPos = InputPanelDragger.ClampToCanvas(_inputPanelPos, _inputPanelRect, _inputCanvas);
            if (_inputPanelRect.anchoredPosition != _inputPanelPos)
                _inputPanelRect.anchoredPosition = _inputPanelPos;
        }

        /// <summary>
        /// レイアウト再構築の直後に呼ぶ。ForceRebuildLayoutImmediate は
        /// anchoredPosition を書き戻すので、その後でこちらの位置を入れ直す必要がある。
        /// </summary>
        private void ReapplyInputPanelPositionAfterRebuild()
        {
            ApplyInputPanelPosition();
        }

        /// <summary>
        /// 上端に重ねるドラッグ用のつまみ帯。レイアウトの外(親直下の子)に置き、
        /// 幅いっぱい・高さ固定で一番手前に出す。ここだけがドラッグを受ける。
        /// </summary>
        private RectTransform EnsureDragHandle()
        {
            const string handleName = "SubtitleInputDragHandle";
            Transform found = _inputPanelRect.Find(handleName);
            if (found != null)
                return found as RectTransform;

            var go = new GameObject(handleName, typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(_inputPanelRect, false);

            // パネル全面を覆う。位置ではなく重なり順で棲み分ける。
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var img = go.GetComponent<Image>();
            // alpha 0 でも raycastTarget が true ならクリックは拾える（見えないが掴める）。
            // 掴める範囲を目で確認したい時は InputPanelDragHandleAlpha を上げる。
            img.color = new Color(1f, 1f, 1f, Mathf.Clamp01(_settings.InputPanelDragHandleAlpha));
            img.raycastTarget = true;

            // レイアウトに詰められないよう除外する
            var ignore = go.AddComponent<LayoutElement>();
            ignore.ignoreLayout = true;

            // 最背面へ。uGUIのレイキャストは手前の要素が優先されるので、
            // ボタンや入力欄の上ではそちらが取り、何も無い余白だけがこの帯に届く。
            rect.SetAsFirstSibling();
            return rect;
        }

        /// <summary>
        /// パネルにドラッグ機能を付け、保存位置を復元する。
        /// パネル本体に付けるので、どこを掴んでも動く（入力欄の中は除く）。
        /// </summary>
        private void AttachInputPanelDragger()
        {
            if (_inputPanelRect == null || _settings == null)
                return;

            // パネル直下は VerticalLayoutGroup で入力欄とボタンが全面を覆っていて、
            // 掴める余白が無い。上端に専用のつまみ帯を重ねて、そこでドラッグを受ける。
            RectTransform handle = EnsureDragHandle();

            var dragger = handle.gameObject.GetComponent<InputPanelDragger>()
                       ?? handle.gameObject.AddComponent<InputPanelDragger>();
            dragger.Bind(_inputPanelRect, _inputCanvas);
            dragger.IsEnabled = () => _settings != null && _settings.InputPanelDraggable;
            // ドラッグ中: 実行中の位置として即座に保持する（レイアウトに負けないため）
            dragger.OnMoving = pos =>
            {
                _inputPanelPos = pos;
                _inputPanelMoved = true;
            };
            // 離した時: 設定へ書き出す
            dragger.OnMoved = pos =>
            {
                _inputPanelPos = pos;
                _inputPanelMoved = true;
                if (_settings == null || !_settings.InputPanelRememberPosition) return;
                _settings.InputPanelPosX = pos.x;
                _settings.InputPanelPosY = pos.y;
                try
                {
                    SettingsStore.Save(
                        System.IO.Path.Combine(_pluginDir, "SubtitleCoreSettings.json"), _settings);
                }
                catch (Exception ex)
                {
                    LogWarn("input panel position save failed: " + ex.Message);
                }
            };

            if (_settings.InputPanelRememberPosition
                && (Mathf.Abs(_settings.InputPanelPosX) > 0.01f || Mathf.Abs(_settings.InputPanelPosY) > 0.01f))
            {
                _inputPanelPos = new Vector2(_settings.InputPanelPosX, _settings.InputPanelPosY);
                _inputPanelMoved = true;
                ApplyInputPanelPosition();
            }

            // 取り付けが本当に走ったかを必ず残す。ここが出なければ経路自体が通っていない。
            Log(string.Format(
                "[drag] 取付 dragger={0} handle={1} handleSize={2} canvas={3} panel={4} draggable={5} 復元位置={6}",
                dragger != null,
                handle != null,
                handle != null ? handle.rect.size.ToString("0") : "-",
                _inputCanvas != null,
                _inputPanelRect != null,
                _settings.InputPanelDraggable,
                _inputPanelMoved ? _inputPanelPos.ToString("0") : "なし"));
        }

        private void UpdateInputPanelLayout()
        {
            if (_settings == null || _inputPanelRect == null)
            {
                return;
            }

            RectTransform canvasRect = _inputCanvas != null ? _inputCanvas.transform as RectTransform : null;
            float canvasWidth = canvasRect != null && canvasRect.rect.width > 0f ? canvasRect.rect.width : Screen.width;
            float canvasHeight = canvasRect != null && canvasRect.rect.height > 0f ? canvasRect.rect.height : Screen.height;
            float width = Mathf.Min(Mathf.Clamp(_settings.InputPanelWidth, 420f, 1400f),
                Mathf.Max(1f, canvasWidth - 16f));
            if (_activeInputSection == InputPanelSection.None)
            {
                width = Mathf.Min(width, 760f);
            }

            float sectionHeight = 0f;
            switch (_activeInputSection)
            {
                case InputPanelSection.History:
                case InputPanelSection.Preset:
                    sectionHeight = Mathf.Clamp(
                        _settings.InputPanelListHeight + 20f,
                        160f,
                        Mathf.Max(160f, canvasHeight - 220f));
                    break;
                default:
                    sectionHeight = 0f;
                    break;
            }

            if (_historySectionLayoutElement != null)
            {
                float preferredHeight = _activeInputSection == InputPanelSection.History
                    ? Mathf.Max(120f, sectionHeight)
                    : 0f;
                if (_historySectionLayoutElement.preferredHeight != preferredHeight)
                    _historySectionLayoutElement.preferredHeight = preferredHeight;
            }

            if (_presetSectionLayoutElement != null)
            {
                float preferredHeight = _activeInputSection == InputPanelSection.Preset
                    ? Mathf.Max(120f, sectionHeight)
                    : 0f;
                if (_presetSectionLayoutElement.preferredHeight != preferredHeight)
                    _presetSectionLayoutElement.preferredHeight = preferredHeight;
            }

            float baseHeight = 96f;
            float height = Mathf.Min(baseHeight, Mathf.Max(1f, canvasHeight - 16f));

            if (!Mathf.Approximately(_inputPanelRect.rect.width, width))
                _inputPanelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            if (!Mathf.Approximately(_inputPanelRect.rect.height, height))
                _inputPanelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);

            // 大きさを変えると anchoredPosition が中央へ戻るので、動かした位置を書き戻す。
            // 保存値ではなく実行中の値を使う（ドラッグ中はまだ保存されていない）。
            ApplyInputPanelPosition();

            float listWidth = width;
            float listHeight = Mathf.Max(120f, sectionHeight);

            // 履歴/プリセットはキャンバス直下に作られていてパネルの子ではない。
            // 中央固定の式のままだと、パネルをドラッグで動かした瞬間に離れてしまう。
            // パネルの現在位置を基準に、その真下へ置く。
            Vector2 panelPos = _inputPanelRect.anchoredPosition;
            const float listGap = 8f;
            float listY = panelPos.y - (height * 0.5f + listGap);
            float listX = panelPos.x;

            // 下にはみ出すなら上側へ回す（pivotは(0.5,1)なのでYは上辺）。
            RectTransform canvasRectForList = _inputCanvas != null
                ? _inputCanvas.transform as RectTransform
                : null;
            if (canvasRectForList != null && listHeight > 0f)
            {
                float halfCanvasH = canvasRectForList.rect.height * 0.5f;
                float bottomEdge = listY - listHeight;
                if (bottomEdge < -halfCanvasH)
                {
                    // 上側: パネル上辺の少し上に下辺が来るよう、上辺を持ち上げる
                    listY = panelPos.y + (height * 0.5f + listGap) + listHeight;
                }
            }

            if (_historySectionRoot != null)
            {
                Vector2 targetPosition = new Vector2(listX, listY);
                if (_historySectionRoot.anchoredPosition != targetPosition)
                    _historySectionRoot.anchoredPosition = targetPosition;
                if (!Mathf.Approximately(_historySectionRoot.rect.width, listWidth))
                    _historySectionRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, listWidth);
                if (!Mathf.Approximately(_historySectionRoot.rect.height, listHeight))
                    _historySectionRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, listHeight);
            }

            if (_presetSectionRoot != null)
            {
                Vector2 targetPosition = new Vector2(listX, listY);
                if (_presetSectionRoot.anchoredPosition != targetPosition)
                    _presetSectionRoot.anchoredPosition = targetPosition;
                if (!Mathf.Approximately(_presetSectionRoot.rect.width, listWidth))
                    _presetSectionRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, listWidth);
                if (!Mathf.Approximately(_presetSectionRoot.rect.height, listHeight))
                    _presetSectionRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, listHeight);
            }

            int fontSize = Mathf.Clamp(_settings.InputPanelInputFontSize, 14, 48);
            if (_inputField != null)
            {
                if (_inputField.textComponent != null)
                {
                    if (_inputField.textComponent.fontSize != fontSize)
                        _inputField.textComponent.fontSize = fontSize;
                }

                Text placeholder = _inputField.placeholder as Text;
                if (placeholder != null)
                {
                    int placeholderFontSize = Mathf.Max(12, fontSize - 2);
                    if (placeholder.fontSize != placeholderFontSize)
                        placeholder.fontSize = placeholderFontSize;
                }
            }
        }

        private void ToggleInputSection(InputPanelSection section)
        {
            if (_activeInputSection == section)
            {
                SetActiveInputSection(InputPanelSection.None, focusInput: false);
                if (_settings != null && _settings.VerboseLog)
                {
                    Log("input panel section=none");
                }
                return;
            }

            SetActiveInputSection(section, focusInput: false);
            if (_settings != null && _settings.VerboseLog)
            {
                Log("input panel section=" + section);
            }
        }

        private void SetActiveInputSection(InputPanelSection section, bool focusInput)
        {
            _activeInputSection = section;

            if (_inputSectionRoot != null)
            {
                _inputSectionRoot.gameObject.SetActive(true);
            }

            if (_historySectionRoot != null)
            {
                _historySectionRoot.gameObject.SetActive(section == InputPanelSection.History);
            }

            if (_presetSectionRoot != null)
            {
                _presetSectionRoot.gameObject.SetActive(section == InputPanelSection.Preset);
            }

            UpdateTabButtonVisuals();
            UpdateInputPanelLayout();
            if (_inputPanelRect != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(_inputPanelRect);
                ReapplyInputPanelPositionAfterRebuild();
            }

            if (section == InputPanelSection.History || section == InputPanelSection.Preset)
            {
                RebuildInputLists();
                if (_inputPanelRect != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_inputPanelRect);
                ReapplyInputPanelPositionAfterRebuild();
                }
            }

            _focusInputNextFrame = focusInput;
        }

        private void UpdateTabButtonVisuals()
        {
            ApplyButtonVisual(_historyToggleButton, _activeInputSection == InputPanelSection.History);
            ApplyButtonVisual(_presetToggleButton, _activeInputSection == InputPanelSection.Preset);
        }

        private void EnsureEventSystem()
        {
            EventSystem current = EventSystem.current;
            if (current != null)
            {
                _eventSystem = current;
                return;
            }

            GameObject eventSystemGo = new GameObject(
                "MainGameSubtitleEventSystem",
                typeof(EventSystem),
                typeof(StandaloneInputModule));
            eventSystemGo.transform.SetParent(transform, false);
            _eventSystem = eventSystemGo.GetComponent<EventSystem>();
        }

        private void EnsureInputPanelSkin()
        {
            if (_inputPanelSkinResolved)
            {
                return;
            }

            _inputPanelSkin = CreateFallbackInputPanelSkin();
            _inputPanelTemplateReady = false;
            _inputPanelTemplate = null;
            _buttonTemplate = null;
            _inputFieldTemplate = null;
            RectTransform[] rects = Resources.FindObjectsOfTypeAll<RectTransform>();
            RectTransform panelCandidate = null;
            Button buttonCandidate = null;
            InputField fieldCandidate = null;

            // 1) Strict: use InputName panel that has both btnYes and InputLastName.
            for (int i = 0; i < rects.Length; i++)
            {
                RectTransform rect = rects[i];
                if (rect == null || !rect.gameObject.scene.IsValid())
                {
                    continue;
                }

                if (IsOwnInputUiObject(rect))
                {
                    continue;
                }

                string nameText = rect.name ?? string.Empty;
                if (nameText.IndexOf("InputName", StringComparison.OrdinalIgnoreCase) < 0 ||
                    nameText.IndexOf("InputLastName", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nameText.IndexOf("InputFirstName", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                Button[] buttonsInPanel = rect.GetComponentsInChildren<Button>(true);
                InputField[] fieldsInPanel = rect.GetComponentsInChildren<InputField>(true);
                Button yesButton = null;
                InputField lastNameField = null;

                for (int j = 0; j < buttonsInPanel.Length; j++)
                {
                    Button b = buttonsInPanel[j];
                    if (b == null)
                    {
                        continue;
                    }

                    string bn = b.name ?? string.Empty;
                    if (bn.IndexOf("btnYes", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        string.Equals(bn, "Yes", StringComparison.OrdinalIgnoreCase))
                    {
                        yesButton = b;
                        break;
                    }
                }

                for (int j = 0; j < fieldsInPanel.Length; j++)
                {
                    InputField f = fieldsInPanel[j];
                    if (f == null)
                    {
                        continue;
                    }

                    string fn = f.name ?? string.Empty;
                    if (fn.IndexOf("InputLastName", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        lastNameField = f;
                        break;
                    }
                }

                if (yesButton == null || lastNameField == null)
                {
                    continue;
                }

                panelCandidate = rect;
                buttonCandidate = yesButton;
                fieldCandidate = lastNameField;
                break;
            }

            // 2) Fallback: resolve each template by explicit object names only.
            if (buttonCandidate == null)
            {
                Button[] buttons = Resources.FindObjectsOfTypeAll<Button>();
                int bestScore = int.MinValue;
                for (int i = 0; i < buttons.Length; i++)
                {
                    Button button = buttons[i];
                    if (button == null ||
                        !button.gameObject.scene.IsValid() ||
                        IsOwnInputUiObject(button.transform))
                    {
                        continue;
                    }

                    string nameText = button.name ?? string.Empty;
                    bool isBtnYes = string.Equals(nameText, "btnYes", StringComparison.OrdinalIgnoreCase) ||
                                    nameText.IndexOf("btnYes", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool isYes = string.Equals(nameText, "Yes", StringComparison.OrdinalIgnoreCase);
                    if (!isBtnYes && !isYes)
                    {
                        continue;
                    }

                    int score = isBtnYes ? 300 : 260;
                    string path = GetTransformPath(button.transform);
                    if (path.IndexOf("InputName", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 60;
                    }
                    if (path.IndexOf("ExitDialog", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 40;
                    }

                    Image image = button.GetComponent<Image>();
                    if (image != null && image.sprite != null)
                    {
                        score += 10;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        buttonCandidate = button;
                    }
                }
            }

            if (fieldCandidate == null)
            {
                InputField[] fields = Resources.FindObjectsOfTypeAll<InputField>();
                int bestScore = int.MinValue;
                for (int i = 0; i < fields.Length; i++)
                {
                    InputField field = fields[i];
                    if (field == null ||
                        !field.gameObject.scene.IsValid() ||
                        IsOwnInputUiObject(field.transform))
                    {
                        continue;
                    }

                    string nameText = field.name ?? string.Empty;
                    int score = int.MinValue;
                    if (string.Equals(nameText, "InputLastName", StringComparison.OrdinalIgnoreCase))
                    {
                        score = 300;
                    }
                    else if (nameText.IndexOf("InputLastName", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score = 260;
                    }
                    else if (string.Equals(nameText, "InputName", StringComparison.OrdinalIgnoreCase))
                    {
                        score = 220;
                    }
                    else if (nameText.IndexOf("InputName", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score = 180;
                    }
                    else
                    {
                        continue;
                    }

                    string path = GetTransformPath(field.transform);
                    if (path.IndexOf("InputName", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score += 30;
                    }

                    if (field.textComponent != null)
                    {
                        score += 10;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        fieldCandidate = field;
                    }
                }
            }

            if (panelCandidate == null)
            {
                panelCandidate = FindTemplatePanelFromAncestor(fieldCandidate != null ? fieldCandidate.transform : null);
            }
            if (panelCandidate == null)
            {
                panelCandidate = FindTemplatePanelFromAncestor(buttonCandidate != null ? buttonCandidate.transform : null);
            }
            if (panelCandidate == null)
            {
                for (int i = 0; i < rects.Length; i++)
                {
                    RectTransform rect = rects[i];
                    if (rect == null ||
                        !rect.gameObject.scene.IsValid() ||
                        IsOwnInputUiObject(rect))
                    {
                        continue;
                    }

                    string nameText = rect.name ?? string.Empty;
                    if (nameText.IndexOf("InputName", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                    if (nameText.IndexOf("InputLastName", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        nameText.IndexOf("InputFirstName", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }
                    if (rect.GetComponent<Image>() == null)
                    {
                        continue;
                    }

                    panelCandidate = rect;
                    break;
                }
            }

            if (panelCandidate != null)
            {
                _inputPanelTemplate = panelCandidate;
                if (_settings != null && _settings.VerboseLog)
                {
                    Log("input panel frame template: " + GetTransformPath(panelCandidate));
                }
            }
            else if (_settings != null && _settings.VerboseLog)
            {
                LogWarn("input panel frame template not found (expected: InputName or ancestor panel)");
            }

            if (buttonCandidate != null)
            {
                _buttonTemplate = buttonCandidate;
                Text candidateLabel = GetPreferredButtonText(buttonCandidate.transform);
                RectTransform candidateRect = buttonCandidate.GetComponent<RectTransform>();
                if (candidateRect != null)
                {
                    Vector2 size = candidateRect.rect.size;
                    if (size.x > 1f && size.y > 1f)
                    {
                        _buttonTemplateSize = size;
                    }
                }

                Image candidateImage = buttonCandidate.GetComponent<Image>();
                if (candidateImage != null && candidateImage.sprite != null)
                {
                    _inputPanelSkin.ButtonSprite = candidateImage.sprite;
                    _inputPanelSkin.ButtonType = candidateImage.type;
                    _inputPanelSkin.ButtonMaterial = candidateImage.material;
                }

                _inputPanelSkin.ButtonColors = buttonCandidate.colors;
                ColorBlock colors = _inputPanelSkin.ButtonColors;
                _inputPanelSkin.ButtonColor = colors.normalColor;

                if (candidateLabel != null && candidateLabel.font != null)
                {
                    _inputPanelSkin.Font = candidateLabel.font;
                }

                if (_settings != null && _settings.VerboseLog)
                {
                    string spriteName = candidateImage != null && candidateImage.sprite != null
                        ? candidateImage.sprite.name
                        : "none";
                    Log("input panel skin source button: " + GetTransformPath(buttonCandidate.transform) + " sprite=" + spriteName);
                }
            }
            else if (_settings != null && _settings.VerboseLog)
            {
                LogWarn("input panel button template not found (expected: btnYes/Yes)");
            }

            if (fieldCandidate != null)
            {
                _inputFieldTemplate = fieldCandidate;
                RectTransform fieldRect = fieldCandidate.GetComponent<RectTransform>();
                if (fieldRect != null)
                {
                    Vector2 size = fieldRect.rect.size;
                    if (size.x > 1f && size.y > 1f)
                    {
                        _inputFieldTemplateSize = size;
                    }
                }

                if (fieldCandidate.textComponent != null && fieldCandidate.textComponent.font != null)
                {
                    _inputPanelSkin.Font = fieldCandidate.textComponent.font;
                }

                if (_settings != null && _settings.VerboseLog)
                {
                    Log("input panel input template: " + GetTransformPath(fieldCandidate.transform));
                }
            }
            else if (_settings != null && _settings.VerboseLog)
            {
                LogWarn("input panel input template not found (expected: InputLastName under InputName)");
            }

            _inputPanelTemplateReady = EnableTemplateControlClone
                ? (_buttonTemplate != null && _inputFieldTemplate != null)
                : true;
            _inputPanelSkinResolved = true;
            if (EnableTemplateControlClone && !_inputPanelTemplateReady && _settings != null && _settings.VerboseLog)
            {
                LogWarn("input panel templates incomplete; retry on next open.");
            }
            else if (!EnableTemplateControlClone && _settings != null && _settings.VerboseLog)
            {
                Log("input panel controls use runtime build (template clone disabled)");
            }
        }

        private static RectTransform FindTemplatePanelFromAncestor(Transform start)
        {
            if (start == null)
            {
                return null;
            }

            RectTransform firstWithImage = null;
            Transform cursor = start;
            while (cursor != null)
            {
                RectTransform rect = cursor as RectTransform;
                if (rect != null && !IsOwnInputUiObject(rect))
                {
                    Image image = rect.GetComponent<Image>();
                    if (image != null)
                    {
                        string nameText = rect.name ?? string.Empty;
                        if (nameText.IndexOf("InputName", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return rect;
                        }

                        if (firstWithImage == null)
                        {
                            firstWithImage = rect;
                        }
                    }
                }

                cursor = cursor.parent;
            }

            return firstWithImage;
        }

        private void ApplyInputPanelFrameTemplate()
        {
            if (_inputPanelRect == null)
            {
                return;
            }

            Image image = _inputPanelRect.GetComponent<Image>();
            if (image == null)
            {
                image = _inputPanelRect.gameObject.AddComponent<Image>();
            }

            if (_inputPanelTemplate != null)
            {
                Image templateImage = _inputPanelTemplate.GetComponent<Image>();
                if (templateImage != null)
                {
                    image.sprite = templateImage.sprite;
                    image.type = templateImage.type;
                    image.material = templateImage.material;
                    image.color = templateImage.color;
                    return;
                }
            }

            image.sprite = _inputPanelSkin.PanelSprite;
            image.type = _inputPanelSkin.PanelType;
            image.material = _inputPanelSkin.PanelMaterial;
            image.color = _inputPanelSkin.PanelColor;
        }

        private static InputPanelSkin CreateFallbackInputPanelSkin()
        {
            var skin = new InputPanelSkin
            {
                Font = Resources.GetBuiltinResource<Font>("Arial.ttf"),
                PanelSprite = null,
                PanelType = Image.Type.Sliced,
                PanelColor = new Color(0.02f, 0.28f, 0.44f, 0.88f),
                PanelMaterial = null,
                ButtonSprite = null,
                ButtonType = Image.Type.Sliced,
                ButtonColor = new Color(0.18f, 0.58f, 0.93f, 0.98f),
                ButtonMaterial = null,
                ButtonColors = ColorBlock.defaultColorBlock
            };

            ColorBlock colors = skin.ButtonColors;
            colors.normalColor = skin.ButtonColor;
            colors.highlightedColor = new Color(0.32f, 0.71f, 0.98f, 1f);
            colors.pressedColor = new Color(0.12f, 0.46f, 0.76f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.25f, 0.25f, 0.25f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            skin.ButtonColors = colors;

            return skin;
        }

        private static bool IsOwnInputUiObject(Transform tr)
        {
            Transform cursor = tr;
            while (cursor != null)
            {
                if (string.Equals(cursor.name, "MainGameSubtitleInputCanvas", StringComparison.Ordinal))
                {
                    return true;
                }

                cursor = cursor.parent;
            }

            return false;
        }

        private static string GetTransformPath(Transform tr)
        {
            if (tr == null)
            {
                return "(null)";
            }

            var names = new List<string>();
            Transform cursor = tr;
            while (cursor != null)
            {
                names.Add(cursor.name ?? string.Empty);
                cursor = cursor.parent;
            }

            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private static RectTransform CreateUiRect(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            return rect;
        }

        private static LayoutElement GetOrAddLayoutElement(GameObject go)
        {
            if (go == null)
            {
                return null;
            }

            LayoutElement element = go.GetComponent<LayoutElement>();
            if (element == null)
            {
                element = go.AddComponent<LayoutElement>();
            }

            return element;
        }

        private static Text GetPreferredButtonText(Transform root)
        {
            if (root == null)
            {
                return null;
            }

            Text[] texts = root.GetComponentsInChildren<Text>(true);
            Text preferred = null;
            int preferredScore = int.MinValue;
            for (int i = 0; i < texts.Length; i++)
            {
                Text text = texts[i];
                if (text == null)
                {
                    continue;
                }

                string nameText = text.name ?? string.Empty;
                int score = 0;
                if (string.Equals(nameText, "Text", StringComparison.OrdinalIgnoreCase) ||
                    nameText.IndexOf("label", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 20;
                }

                score += text.fontSize;
                if (text.alignment == TextAnchor.MiddleCenter || text.alignment == TextAnchor.MiddleLeft)
                {
                    score += 10;
                }

                if (score > preferredScore)
                {
                    preferredScore = score;
                    preferred = text;
                }
            }

            return preferred;
        }

        private Text CreateSimpleText(
            Transform parent,
            string name,
            string value,
            int fontSize,
            TextAnchor alignment,
            Color color)
        {
            RectTransform rect = CreateUiRect(
                parent,
                name,
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);

            Text text = rect.gameObject.AddComponent<Text>();
            text.text = value ?? string.Empty;
            text.font = _inputPanelSkin.Font != null
                ? _inputPanelSkin.Font
                : Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = Mathf.Clamp(fontSize, 10, 64);
            text.alignment = alignment;
            text.color = color;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(Transform parent, string name, string label, Action onClick)
        {
            if (EnableTemplateControlClone && _buttonTemplate != null && _buttonTemplate.gameObject != null)
            {
                GameObject clone = UnityEngine.Object.Instantiate(_buttonTemplate.gameObject);
                clone.name = name;
                clone.transform.SetParent(parent, false);
                clone.transform.localScale = Vector3.one;
                clone.transform.localRotation = Quaternion.identity;

                RectTransform cloneRect = clone.GetComponent<RectTransform>();
                if (cloneRect != null)
                {
                    cloneRect.anchorMin = new Vector2(0.5f, 0.5f);
                    cloneRect.anchorMax = new Vector2(0.5f, 0.5f);
                    cloneRect.pivot = new Vector2(0.5f, 0.5f);
                    cloneRect.anchoredPosition = Vector2.zero;
                    cloneRect.sizeDelta = _buttonTemplateSize;
                }

                Button clonedButton = clone.GetComponent<Button>();
                if (clonedButton != null)
                {
                    clonedButton.onClick = new Button.ButtonClickedEvent();
                    if (onClick != null)
                    {
                        clonedButton.onClick.AddListener(() => onClick());
                    }
                }

                Text preferred = GetPreferredButtonText(clone.transform);
                ApplyButtonLabel(clone.transform, label, preferred);

                if (clonedButton != null)
                {
                    ApplyButtonVisual(clonedButton, false);
                    return clonedButton;
                }

                UnityEngine.Object.Destroy(clone);
            }

            RectTransform rect = CreateUiRect(
                parent,
                name,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                _buttonTemplateSize);

            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = _inputPanelSkin.ButtonSprite;
            image.type = _inputPanelSkin.ButtonType;
            image.color = _inputPanelSkin.ButtonColor;
            image.material = _inputPanelSkin.ButtonMaterial;

            Button button = rect.gameObject.AddComponent<Button>();
            button.colors = _inputPanelSkin.ButtonColors;

            Text labelText = CreateSimpleText(
                rect,
                "Label",
                label,
                20,
                TextAnchor.MiddleCenter,
                new Color(0.94f, 0.96f, 1f, 1f));
            labelText.raycastTarget = false;
            labelText.resizeTextForBestFit = false;
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            labelText.verticalOverflow = VerticalWrapMode.Truncate;

            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }

            ApplyButtonVisual(button, false);
            return button;
        }

        private void ApplyButtonLabel(Transform buttonRoot, string label, Text preferredUnityText)
        {
            if (buttonRoot == null)
            {
                return;
            }

            string value = label ?? string.Empty;

            Text[] unityTexts = buttonRoot.GetComponentsInChildren<Text>(true);
            Text unityTarget = preferredUnityText;
            if (unityTarget == null && unityTexts.Length > 0)
            {
                unityTarget = unityTexts[0];
            }

            for (int i = 0; i < unityTexts.Length; i++)
            {
                Text t = unityTexts[i];
                if (t == null)
                {
                    continue;
                }

                t.text = t == unityTarget ? value : string.Empty;
                t.raycastTarget = false;
                if (t == unityTarget && _inputPanelSkin.Font != null)
                {
                    t.font = _inputPanelSkin.Font;
                }
            }

            Component[] tmpTexts = GetTmpTextComponents(buttonRoot);
            Component tmpTarget = null;
            if (unityTarget == null && tmpTexts.Length > 0)
            {
                tmpTarget = SelectPreferredTmpText(tmpTexts);
            }
            for (int i = 0; i < tmpTexts.Length; i++)
            {
                Component component = tmpTexts[i];
                if (component == null)
                {
                    continue;
                }

                SetTmpText(component, component == tmpTarget ? value : string.Empty);
                SetTmpRaycast(component, false);
            }

            if (unityTarget == null && tmpTarget == null)
            {
                Text runtimeLabel = CreateSimpleText(
                    buttonRoot,
                    "MainGameSubtitleRuntimeLabel",
                    value,
                    20,
                    TextAnchor.MiddleCenter,
                    new Color(0.94f, 0.96f, 1f, 1f));
                runtimeLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
                runtimeLabel.verticalOverflow = VerticalWrapMode.Truncate;
                runtimeLabel.supportRichText = false;
                runtimeLabel.resizeTextForBestFit = false;
                runtimeLabel.raycastTarget = false;
            }
        }

        private static Component[] GetTmpTextComponents(Transform root)
        {
            if (root == null)
            {
                return Array.Empty<Component>();
            }

            Component[] components = root.GetComponentsInChildren<Component>(true);
            var matches = new List<Component>(4);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || component is Text)
                {
                    continue;
                }

                string fullName = component.GetType().FullName ?? string.Empty;
                if (fullName.IndexOf("TMPro.", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                matches.Add(component);
            }

            return matches.ToArray();
        }

        private static Component SelectPreferredTmpText(Component[] tmpTexts)
        {
            if (tmpTexts == null || tmpTexts.Length == 0)
            {
                return null;
            }

            Component preferred = null;
            int bestScore = int.MinValue;
            for (int i = 0; i < tmpTexts.Length; i++)
            {
                Component component = tmpTexts[i];
                if (component == null)
                {
                    continue;
                }

                int score = 0;
                string nameText = component.name ?? string.Empty;
                if (string.Equals(nameText, "Text", StringComparison.OrdinalIgnoreCase))
                {
                    score += 40;
                }
                if (nameText.IndexOf("label", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    nameText.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 20;
                }
                if (component.gameObject.activeInHierarchy)
                {
                    score += 10;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    preferred = component;
                }
            }

            return preferred ?? tmpTexts[0];
        }

        private static void SetTmpText(Component component, string value)
        {
            if (component == null)
            {
                return;
            }

            PropertyInfo textProperty = component.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
            if (textProperty != null &&
                textProperty.CanWrite &&
                textProperty.PropertyType == typeof(string))
            {
                textProperty.SetValue(component, value ?? string.Empty, null);
            }
        }

        private static void SetTmpRaycast(Component component, bool enabled)
        {
            if (component == null)
            {
                return;
            }

            PropertyInfo raycastProperty = component.GetType().GetProperty("raycastTarget", BindingFlags.Instance | BindingFlags.Public);
            if (raycastProperty != null &&
                raycastProperty.CanWrite &&
                raycastProperty.PropertyType == typeof(bool))
            {
                raycastProperty.SetValue(component, enabled, null);
            }
        }

        private void ApplyButtonVisual(Button button, bool highlighted)
        {
            if (button == null)
            {
                return;
            }

            bool usingTemplateClone = EnableTemplateControlClone && _buttonTemplate != null;
            if (!usingTemplateClone)
            {
                Image image = button.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = _inputPanelSkin.ButtonSprite;
                    image.type = _inputPanelSkin.ButtonType;
                    image.material = _inputPanelSkin.ButtonMaterial;
                    image.color = highlighted
                        ? Color.Lerp(_inputPanelSkin.ButtonColor, Color.white, 0.22f)
                        : _inputPanelSkin.ButtonColor;
                }
            }

            ColorBlock colors = button.colors;
            if (highlighted)
            {
                colors.normalColor = Color.Lerp(colors.normalColor, Color.white, 0.18f);
                colors.selectedColor = colors.normalColor;
            }
            else if (!usingTemplateClone)
            {
                colors = _inputPanelSkin.ButtonColors;
            }
            button.colors = colors;
        }

        private InputField CreateInputField(Transform parent, string name)
        {
            if (EnableTemplateControlClone && _inputFieldTemplate != null && _inputFieldTemplate.gameObject != null)
            {
                GameObject clone = UnityEngine.Object.Instantiate(_inputFieldTemplate.gameObject);
                clone.name = name;
                clone.transform.SetParent(parent, false);
                clone.transform.localScale = Vector3.one;
                clone.transform.localRotation = Quaternion.identity;

                RectTransform cloneRect = clone.GetComponent<RectTransform>();
                if (cloneRect != null)
                {
                    cloneRect.anchorMin = new Vector2(0.5f, 0.5f);
                    cloneRect.anchorMax = new Vector2(0.5f, 0.5f);
                    cloneRect.pivot = new Vector2(0.5f, 0.5f);
                    cloneRect.anchoredPosition = Vector2.zero;
                    cloneRect.sizeDelta = _inputFieldTemplateSize;
                }

                InputField templateField = clone.GetComponent<InputField>();
                if (templateField != null)
                {
                    templateField.text = string.Empty;
                    templateField.onValueChanged = new InputField.OnChangeEvent();
                    templateField.onEndEdit = new InputField.SubmitEvent();
                    templateField.lineType = InputField.LineType.SingleLine;
                    templateField.contentType = InputField.ContentType.Standard;

                    int templateFontSize = _settings != null ? Mathf.Clamp(_settings.InputPanelInputFontSize, 14, 48) : 22;
                    if (templateField.textComponent != null)
                    {
                        if (_inputPanelSkin.Font != null)
                        {
                            templateField.textComponent.font = _inputPanelSkin.Font;
                        }

                        templateField.textComponent.fontSize = templateFontSize;
                        templateField.textComponent.alignment = TextAnchor.MiddleLeft;
                        templateField.textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
                        templateField.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
                    }

                    Text templatePlaceholder = templateField.placeholder as Text;
                    if (templatePlaceholder != null)
                    {
                        templatePlaceholder.text = "字幕を入力...";
                        templatePlaceholder.fontSize = Mathf.Max(12, templateFontSize - 2);
                        templatePlaceholder.alignment = TextAnchor.MiddleLeft;
                        if (_inputPanelSkin.Font != null)
                        {
                            templatePlaceholder.font = _inputPanelSkin.Font;
                        }
                    }

                    return templateField;
                }

                UnityEngine.Object.Destroy(clone);
            }

            RectTransform rect = CreateUiRect(
                parent,
                name,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                _inputFieldTemplateSize);

            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = _inputPanelSkin.ButtonSprite;
            image.type = _inputPanelSkin.ButtonType;
            image.material = _inputPanelSkin.ButtonMaterial;
            image.color = new Color(0f, 0f, 0f, 0.48f);
            Outline frame = rect.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(0.4f, 0.9f, 1f, 0.9f);
            frame.effectDistance = new Vector2(1f, -1f);

            InputField field = rect.gameObject.AddComponent<InputField>();
            field.lineType = InputField.LineType.SingleLine;
            field.contentType = InputField.ContentType.Standard;

            RectTransform textArea = CreateUiRect(
                rect,
                "TextArea",
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            textArea.offsetMin = new Vector2(10f, 5f);
            textArea.offsetMax = new Vector2(-10f, -5f);

            int fontSize = _settings != null ? Mathf.Clamp(_settings.InputPanelInputFontSize, 14, 48) : 22;

            Text text = CreateSimpleText(
                textArea,
                "Text",
                string.Empty,
                fontSize,
                TextAnchor.MiddleLeft,
                new Color(1f, 1f, 1f, 1f));
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;

            Text placeholder = CreateSimpleText(
                textArea,
                "Placeholder",
                "字幕を入力...",
                Mathf.Max(12, fontSize - 2),
                TextAnchor.MiddleLeft,
                new Color(1f, 1f, 1f, 0.35f));
            placeholder.raycastTarget = false;
            placeholder.fontStyle = FontStyle.Italic;

            field.textComponent = text;
            field.placeholder = placeholder;
            return field;
        }

        private void BuildListColumn(
            Transform parent,
            string title,
            out Text headerText,
            out RectTransform content)
        {
            RectTransform column = CreateUiRect(
                parent,
                title + "Column",
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);

            LayoutElement columnElement = column.gameObject.AddComponent<LayoutElement>();
            columnElement.flexibleWidth = 1f;
            columnElement.flexibleHeight = 1f;
            columnElement.minWidth = 180f;

            VerticalLayoutGroup columnLayout = column.gameObject.AddComponent<VerticalLayoutGroup>();
            columnLayout.padding = new RectOffset(0, 0, 0, 0);
            columnLayout.spacing = 6f;
            columnLayout.childControlWidth = true;
            columnLayout.childControlHeight = true;
            columnLayout.childForceExpandWidth = true;
            columnLayout.childForceExpandHeight = false;

            headerText = CreateSimpleText(
                column,
                title + "Header",
                title,
                17,
                TextAnchor.MiddleLeft,
                new Color(0.94f, 0.96f, 1f, 0.98f));
            LayoutElement headerElement = headerText.gameObject.AddComponent<LayoutElement>();
            headerElement.preferredHeight = 26f;

            RectTransform listRoot = CreateUiRect(
                column,
                title + "ListRoot",
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero);
            LayoutElement listRootElement = listRoot.gameObject.AddComponent<LayoutElement>();
            listRootElement.flexibleHeight = 1f;
            listRootElement.minHeight = 180f;
            Image viewportImage = listRoot.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(0f, 0f, 0f, 0.16f);
            Mask viewportMask = listRoot.gameObject.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;

            ScrollRect scroll = listRoot.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            scroll.inertia = true;

            content = CreateUiRect(
                listRoot,
                "Content",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                Vector2.zero,
                new Vector2(0f, 0f));

            VerticalLayoutGroup contentLayout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(4, 4, 4, 4);
            contentLayout.spacing = 6f;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;

            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = listRoot;
            scroll.content = content;
            scroll.verticalNormalizedPosition = 1f;
        }

        private void RebuildInputLists()
        {
            if (_historyHeaderText != null)
            {
                _historyHeaderText.text = $"履歴 ({_inputHistory.Count})";
            }

            if (_presetHeaderText != null)
            {
                _presetHeaderText.text = $"プリセット ({_inputPresets.Count})";
            }

            if (_historyContent != null)
            {
                RebuildListContent(_historyContent, _inputHistory, false);
            }

            if (_presetContent != null)
            {
                RebuildListContent(_presetContent, _inputPresets, true);
            }
        }

        private void RebuildListContent(RectTransform content, List<string> values, bool isPreset)
        {
            if (content == null)
            {
                return;
            }

            for (int i = content.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(content.GetChild(i).gameObject);
            }

            if (values == null)
            {
                return;
            }

            bool hasAny = false;
            for (int i = 0; i < values.Count; i++)
            {
                string item = values[i];
                if (string.IsNullOrWhiteSpace(item))
                {
                    continue;
                }
                hasAny = true;

                string captured = item;

                RectTransform row = CreateUiRect(
                    content,
                    "Row_" + i,
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    Vector2.zero);
                LayoutElement rowElement = row.gameObject.AddComponent<LayoutElement>();
                float rowHeight = Mathf.Clamp(_buttonTemplateSize.y > 1f ? _buttonTemplateSize.y * 0.65f : 38f, 34f, 56f);
                rowElement.preferredHeight = rowHeight;

                HorizontalLayoutGroup rowLayout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 5f;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = false;
                rowLayout.childForceExpandHeight = true;

                Button valueButton = CreateButton(row, "ValueButton", captured, () =>
                {
                    if (_inputField != null)
                    {
                        _inputField.text = captured;
                        _focusInputNextFrame = true;
                        SetActiveInputSection(InputPanelSection.None, focusInput: true);
                    }
                });
                LayoutElement valueButtonElement = GetOrAddLayoutElement(valueButton.gameObject);
                valueButtonElement.flexibleWidth = 1f;
                valueButtonElement.minWidth = 120f;
                Text valueText = valueButton.GetComponentInChildren<Text>();
                if (valueText != null)
                {
                    valueText.alignment = TextAnchor.MiddleLeft;
                    valueText.horizontalOverflow = HorizontalWrapMode.Overflow;
                    valueText.verticalOverflow = VerticalWrapMode.Truncate;
                }

                Button sendButton = CreateButton(row, "SendButton", "送信", () => SendTextFromInputPanel(captured));
                LayoutElement rowSendLayout = GetOrAddLayoutElement(sendButton.gameObject);
                rowSendLayout.preferredWidth = 88f;
                rowSendLayout.preferredHeight = 32f;

                Button removeButton = CreateButton(row, "RemoveButton", "削除", () =>
                {
                    if (isPreset)
                    {
                        _inputPresets.RemoveAll(x => string.Equals(x, captured, StringComparison.Ordinal));
                    }
                    else
                    {
                        _inputHistory.RemoveAll(x => string.Equals(x, captured, StringComparison.Ordinal));
                    }

                    SaveInputPanelState();
                    RebuildInputLists();
                });
                LayoutElement rowRemoveLayout = GetOrAddLayoutElement(removeButton.gameObject);
                rowRemoveLayout.preferredWidth = 88f;
                rowRemoveLayout.preferredHeight = 32f;
            }

            if (!hasAny)
            {
                RectTransform emptyRow = CreateUiRect(
                    content,
                    isPreset ? "PresetEmpty" : "HistoryEmpty",
                    Vector2.zero,
                    Vector2.one,
                    new Vector2(0.5f, 0.5f),
                    Vector2.zero,
                    Vector2.zero);

                LayoutElement emptyRowElement = emptyRow.gameObject.AddComponent<LayoutElement>();
                emptyRowElement.preferredHeight = 30f;

                Text emptyText = CreateSimpleText(
                    emptyRow,
                    "EmptyLabel",
                    isPreset ? "（プリセットなし）" : "（履歴なし）",
                    15,
                    TextAnchor.MiddleLeft,
                    new Color(0.9f, 0.95f, 1f, 0.85f));
                emptyText.raycastTarget = false;
            }
        }

        private static bool IsConfiguredKeyDown(string keyText)
        {
            if (string.IsNullOrWhiteSpace(keyText))
            {
                return false;
            }

            if (!Enum.TryParse(keyText.Trim(), ignoreCase: true, out KeyCode key))
            {
                return false;
            }

            if (key == KeyCode.None)
            {
                return false;
            }

            return Input.GetKeyDown(key);
        }

        private static bool IsHSceneActiveCached()
        {
            float now = Time.unscaledTime;
            if (now < _hSceneCachedUntil)
            {
                return _isHSceneCached;
            }

            _isHSceneCached = IsHSceneActiveNow();
            _hSceneCachedUntil = now + 0.25f;
            return _isHSceneCached;
        }

        private static bool IsHSceneActiveNow()
        {
            if (HSceneProcType != null && UnityEngine.Object.FindObjectOfType(HSceneProcType) != null)
            {
                return true;
            }

            if (VRHSceneType != null && UnityEngine.Object.FindObjectOfType(VRHSceneType) != null)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// マウス位置が入力パネルの矩形内にあるか。
        /// 判定できない場合は true を返し、誤って閉じないようにする。
        /// </summary>
        private bool IsPointerOverInputPanel()
        {
            if (_inputPanelRect == null)
            {
                return true;
            }

            // 履歴とプリセットのパネルは _inputPanelRect の子ではなくキャンバス直下に
            // 置かれ、入力パネルの矩形の外に張り出す。矩形だけで見ると
            // それらをクリックした瞬間に「外」と判定されて閉じてしまうため、
            // 自前キャンバスのどこかに当たったかどうかで判定する。
            EventSystem current = EventSystem.current;
            if (current != null && _inputCanvas != null)
            {
                PointerEventData pointer = new PointerEventData(current);
                pointer.position = Input.mousePosition;

                _uiRaycastResults.Clear();
                current.RaycastAll(pointer, _uiRaycastResults);

                Transform canvasTransform = _inputCanvas.transform;
                bool hitOwnUi = false;
                for (int i = 0; i < _uiRaycastResults.Count; i++)
                {
                    GameObject hit = _uiRaycastResults[i].gameObject;
                    if (hit != null && hit.transform.IsChildOf(canvasTransform))
                    {
                        hitOwnUi = true;
                        break;
                    }
                }
                _uiRaycastResults.Clear();

                if (hitOwnUi)
                {
                    return true;
                }
            }

            // EventSystem が取れない場合の保険。表示中の矩形をすべて見る。
            return IsPointerInsideRect(_inputPanelRect) ||
                IsPointerInsideRect(_historySectionRoot) ||
                IsPointerInsideRect(_presetSectionRoot);
        }

        private bool IsPointerInsideRect(RectTransform rect)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy)
            {
                return false;
            }

            Camera camera = null;
            if (_inputCanvas != null && _inputCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                camera = _inputCanvas.worldCamera;
            }

            return RectTransformUtility.RectangleContainsScreenPoint(rect, Input.mousePosition, camera);
        }

        /// <summary>
        /// 入力欄の編集完了。Enterで確定された時だけ送信する。
        /// フォーカスを失っただけでも呼ばれるので、そちらを弾いて振り分ける。
        /// </summary>
        private void OnInputFieldEndEdit(string _)
        {
            if (_settings == null || !_settings.InputPanelSendOnEnter)
            {
                return;
            }

            if (_suppressEndEditSend)
            {
                return; // こちらの都合で閉じた
            }

            if (!MainGameUiInputCapture.InputBindingsApi.AllowsTextSubmit("MainGameSubtitleCore.send", "Return | KeypadEnter"))
                return;

            // Enterの押下は当てにできない。IMEが有効な間、WindowsはEnterを
            // VK_PROCESSKEY として渡すため Unity の Input には現れない。
            // 「Enterである証拠」ではなく「Enter以外が原因である証拠」で弾く。
            float now = Time.unscaledTime;
            bool mouseInvolved =
                now - _lastInputPanelMouseDownTime < EndEditMouseGraceSeconds ||
                Input.GetMouseButton(0) ||
                Input.GetMouseButton(1) ||
                Input.GetMouseButton(2);
            if (mouseInvolved)
            {
                if (_settings.VerboseLog)
                {
                    Log("input panel end edit ignored: pointer click");
                }
                return;
            }

            if (Bindings.Held("MainGameSubtitleCore.close", "Escape") || Input.GetKey(KeyCode.Escape) || Input.GetKey(KeyCode.Tab))
            {
                if (_settings.VerboseLog)
                {
                    Log("input panel end edit ignored: cancel key");
                }
                return;
            }

            if (_settings.VerboseLog)
            {
                Log("input panel end edit: treated as enter submit");
            }
            SendInputDraft();
        }

        private void SendInputDraft()
        {
            // Update監視と onEndEdit の双方から来るため、同じEnterで二度送らないよう抑える。
            float now = Time.unscaledTime;
            if (now - _lastInputSendTime < InputSendDebounceSeconds)
            {
                return;
            }
            _lastInputSendTime = now;

            if (_inputField == null)
            {
                if (_settings != null && _settings.VerboseLog)
                {
                    LogWarn("input panel send ignored: input field is null");
                }
                return;
            }

            string text = (_inputField.text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                if (_settings != null && _settings.VerboseLog)
                {
                    Log("input panel send ignored: empty text");
                }
                return;
            }

            if (_settings != null && _settings.VerboseLog)
            {
                Log("input panel send clicked: len=" + text.Length);
            }

            // 送信すると SendTextFromInputPanel がパネルを閉じ、その選択解除で
            // onEndEdit が再び走る。先に空にしておけば二重送信の芽が残らない。
            ClearInputDraft();
            SendTextFromInputPanel(text);
        }

        private static string MicToggleLabel()
        {
            return _micSendEnabled ? "音声送信 ON" : "音声送信 OFF";
        }

        /// <summary>
        /// 女性の発話を止める。MainGameVoiceFaceEventBridge.Plugin.StopFemaleVoice を
        /// リフレクションで呼ぶ。DLL参照はしないので未導入でも落ちない。
        /// </summary>
        private void CancelFemaleVoice()
        {
            MethodInfo stop = null;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType("MainGameVoiceFaceEventBridge.Plugin", false);
                    if (t == null)
                    {
                        continue;
                    }

                    stop = t.GetMethod(
                        "StopFemaleVoice",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                        null,
                        new[] { typeof(string) },
                        null);
                    break;
                }
                catch
                {
                }
            }

            if (stop == null)
            {
                LogWarn("[cancel] VoiceFaceEventBridge が見つからない");
                return;
            }

            try
            {
                object ok = stop.Invoke(null, new object[] { "input-panel-cancel" });
                Log("[cancel] 発話停止 result=" + ok);
            }
            catch (Exception ex)
            {
                LogWarn("[cancel] 発話停止に失敗: " + ex.Message);
            }

            ScheduleSubtitleClearAfterCancel();
        }

        /// <summary>
        /// キャンセル後に字幕を消す。
        /// KeepVisibleUntilNext がONだと次の字幕まで残り続けるので、
        /// 表示期限(_overlayUntil)を今から指定秒後へ切り詰める。
        /// 0以下なら即座に消す。
        /// </summary>
        private void ScheduleSubtitleClearAfterCancel()
        {
            float seconds = _cfgCancelSubtitleClearSeconds != null
                ? _cfgCancelSubtitleClearSeconds.Value
                : 5f;

            ClearSubtitleAfter(seconds, "cancel");
        }

        /// <summary>
        /// 返事を最後まで読み終えた時に外部（VoiceFaceEventBridge）から呼ばれる。
        /// キャンセル時と同じく表示期限を切り詰めるだけで、消し方は共通。
        /// リフレクションで呼ばれるので名前と引数を変えないこと。
        /// </summary>
        internal static bool NotifyFemaleVoiceFinished(string reason)
        {
            Plugin instance = _instance;
            if (instance == null)
                return false;

            float seconds = instance._cfgFinishSubtitleClearSeconds != null
                ? instance._cfgFinishSubtitleClearSeconds.Value
                : 2f;

            instance.ClearSubtitleAfter(seconds, string.IsNullOrWhiteSpace(reason) ? "finished" : reason);
            return true;
        }

        private void ClearSubtitleAfter(float seconds, string reason)
        {

            if (seconds < 0f)
            {
                Log("[" + reason + "] 字幕はそのまま残す(設定が負値)");
                return;
            }

            float until = Time.unscaledTime + seconds;

            // 元の期限より延ばさない。既に短いならそのまま。
            if (_overlayUntil > until)
            {
                _overlayUntil = until;
            }

            if (_overlayUpperUntil > until)
            {
                _overlayUpperUntil = until;
            }

            Log("[" + reason + "] 字幕を" + seconds.ToString("0.0") + "秒後に消す");
        }

        /// <summary>
        /// 音声送信のON/OFFを切り替え、その状態を外部へ通知する。
        /// 受け取り側(Human_2_kks)がまだ無くても、送信自体は失敗しても構わない設計にする。
        /// </summary>
        private void ToggleMicSend()
        {
            _micSendEnabled = !_micSendEnabled;
            _micStateSynced = false;
            SaveInputPanelState();
            UpdateMicToggleVisual();
            NotifyMicSendState();
        }

        private void UpdateMicToggleVisual()
        {
            if (_micToggleButton == null)
            {
                return;
            }

            Text label = _micToggleButton.GetComponentInChildren<Text>();
            if (label != null)
            {
                label.text = MicToggleLabel() + (_micStatePending ? " (切替中)" : !_micStateSynced ? " (同期失敗)" : "");
            }

            ApplyButtonVisual(_micToggleButton, !_micSendEnabled);
        }

        /// <summary>
        /// 停止信号を外部へ送る。既存の転送設定(Host/Port/Token)をそのまま使い、
        /// エンドポイントだけ /mic-state にする。受け側が無ければ黙って諦める。
        /// </summary>
        private void NotifyMicSendState()
        {
            if (_micStatePending) return;
            if (_settings == null || !_settings.InputPanelForwardEnabled)
            {
                _micStateSynced = false;
                UpdateMicToggleVisual();
                return;
            }
            _micStatePending = true;
            _micStateSynced = false;
            _micStateSentTarget = _micSendEnabled;
            bool sentTarget = _micStateSentTarget;
            string json = "{\"send_voice\":" + (_micSendEnabled ? "true" : "false")
                        + ",\"source\":\"" + EscapeJsonString(_settings.InputPanelForwardSource ?? "subtitle-ui") + "\"}";
            string host = _settings.InputPanelForwardHost;
            int port = _settings.InputPanelForwardPort;
            string token = _settings.InputPanelForwardToken;
            int timeout = Mathf.Clamp(_settings.InputPanelForwardTimeoutMs, 200, 5000);
            UpdateMicToggleVisual();
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok = false;
                try
                {
                    string url = "http://" + host + ":" + port + "/mic-state";
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                    req.Method = "POST";
                    req.ContentType = "application/json";
                    req.Timeout = timeout;
                    req.ReadWriteTimeout = timeout;
                    req.Proxy = null;
                    if (!string.IsNullOrEmpty(token)) req.Headers["X-Token"] = token;
                    byte[] body = Encoding.UTF8.GetBytes(json);
                    req.ContentLength = body.Length;
                    using (var st = req.GetRequestStream()) st.Write(body, 0, body.Length);
                    using (var res = (System.Net.HttpWebResponse)req.GetResponse())
                    using (var stream = res.GetResponseStream())
                    {
                        var ack = ReadMicStateAck(stream);
                        ok = res.StatusCode == System.Net.HttpStatusCode.OK && ack.HasValue && ack.Value == sentTarget;
                    }
                }
                catch (Exception ex)
                {
                    LogWarn("[mic] state sync failed: " + ex.Message);
                }
                lock (_micStateLock)
                {
                    _micStateResultOk = ok;
                    _micStateResultReady = true;
                }
            });
        }

        private static bool? ReadMicStateAck(System.IO.Stream stream)
        {
            if (stream == null) return null;
            using (var reader = new System.IO.StreamReader(stream))
            {
                string response = reader.ReadToEnd();
                if (!response.Contains("\"ok\": true") && !response.Contains("\"ok\":true")) return null;
                if (response.Contains("\"send_voice\": true") || response.Contains("\"send_voice\":true")) return true;
                if (response.Contains("\"send_voice\": false") || response.Contains("\"send_voice\":false")) return false;
                return null;
            }
        }

        private void PollMicSendState()
        {
            bool ready;
            bool ok;
            lock (_micStateLock)
            {
                ready = _micStateResultReady;
                ok = _micStateResultOk;
                _micStateResultReady = false;
            }
            if (!ready) return;
            _micStatePending = false;
            bool current = _micStateSentTarget == _micSendEnabled;
            _micStateSynced = ok && current;
            _nextMicStateRetry = Time.unscaledTime + (_micStateSynced ? 15f : current ? 5f : 0f);
            UpdateMicToggleVisual();
        }

        private void ClearInputDraft()
        {
            if (_inputField == null)
            {
                return;
            }

            _inputField.text = string.Empty;
            _focusInputNextFrame = true;
        }

        private void SendTextFromInputPanel(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string normalized = text.Trim();
            try
            {
                DisplayBackendMode backend = ParseDisplayBackend(_settings.InputPanelSendBackend);
                string modeText = string.IsNullOrWhiteSpace(_settings.InputPanelSendDisplayMode)
                    ? _settings.DisplayMode
                    : _settings.InputPanelSendDisplayMode;
                float holdSeconds = Mathf.Clamp(_settings.InputPanelSendHoldSeconds, 0.1f, 600f);

                if (_settings != null && _settings.VerboseLog)
                {
                    Log(
                        "input panel dispatch: backend=" + backend +
                        ", mode=" + modeText +
                        ", hold=" + holdSeconds.ToString("0.00") +
                        ", len=" + normalized.Length);
                }
                ShowSubtitleCore(normalized, holdSeconds, backend, modeText, "input-panel");
                TryForwardInputText(normalized);
                PushUniqueFirst(_inputHistory, normalized, _settings.InputPanelHistoryMax);
                SaveInputPanelState();
                RebuildInputLists();
            }
            catch (Exception ex)
            {
                LogError("input panel send failed: " + ex.Message);
            }
            finally
            {
                // KeepOpenAfterSend がONなら閉じずに残し、続けて打てるようにする。
                // 入力欄は SendInputDraft 側で既に空にしてあるので、そのまま次を打てる。
                if (_cfgKeepOpenAfterSend != null && _cfgKeepOpenAfterSend.Value)
                {
                    _focusInputNextFrame = true;
                }
                else
                {
                    SetInputPanelVisible(false);
                }
            }
        }

        private bool TryForwardInputText(string text)
        {
            string reason;
            return TryForwardInputText(text, null, out reason);
        }

        private bool TryForwardInputText(string text, string sourceOverride, out string reason)
        {
            if (_settings == null || !_settings.InputPanelForwardEnabled)
            {
                if (_settings != null && _settings.VerboseLog)
                {
                    Log("input panel forward skipped: disabled");
                }
                reason = "input panel forward is disabled";
                return false;
            }

            string normalized = (text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(normalized))
            {
                reason = "text is empty";
                return false;
            }

            string host = string.IsNullOrWhiteSpace(_settings.InputPanelForwardHost)
                ? "127.0.0.1"
                : _settings.InputPanelForwardHost.Trim();
            int port = Mathf.Clamp(_settings.InputPanelForwardPort, 1, 65535);
            string endpoint = NormalizeHttpEndpoint(_settings.InputPanelForwardEndpoint, "/manual-text");
            int timeoutMs = Mathf.Clamp(_settings.InputPanelForwardTimeoutMs, 100, 15000);
            string token = _settings.InputPanelForwardToken ?? string.Empty;
            string source = !string.IsNullOrWhiteSpace(sourceOverride)
                ? sourceOverride.Trim()
                : (string.IsNullOrWhiteSpace(_settings.InputPanelForwardSource)
                    ? "subtitle-ui"
                    : _settings.InputPanelForwardSource.Trim());
            if (_settings != null && _settings.VerboseLog)
            {
                Log("input panel forward begin: http://" + host + ":" + port + endpoint + ", source=" + source + ", len=" + normalized.Length);
            }

            var payload = new ForwardTextPayload
            {
                text = normalized,
                source = source,
                event_id = Guid.NewGuid().ToString("N"),
                sent_at_unix_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                direct_change = _directChangeEnabled
            };

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string url = $"http://{host}:{port}{endpoint}";
                    string json = BuildForwardTextJson(payload);
                    byte[] body = Utf8NoBom.GetBytes(json);

                    HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "POST";
                    request.ContentType = "application/json; charset=utf-8";
                    request.Timeout = timeoutMs;
                    request.ReadWriteTimeout = timeoutMs;
                    request.ContentLength = body.Length;
                    request.Proxy = null;
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        request.Headers["X-Auth-Token"] = token.Trim();
                    }

                    using (Stream reqStream = request.GetRequestStream())
                    {
                        reqStream.Write(body, 0, body.Length);
                    }

                    using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                    {
                        if (_settings != null && _settings.VerboseLog)
                        {
                            Log("input panel forward ok: status=" + (int)response.StatusCode);
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogWarn("input panel forward failed: " + ex.Message);
                }
            });

            reason = null;
            return true;
        }

        private static string NormalizeHttpEndpoint(string endpoint, string fallback)
        {
            string value = string.IsNullOrWhiteSpace(endpoint) ? fallback : endpoint.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                value = fallback;
            }

            if (!value.StartsWith("/"))
            {
                value = "/" + value;
            }

            return value;
        }

        private static string BuildForwardTextJson(ForwardTextPayload payload)
        {
            if (payload == null)
            {
                return "{\"text\":\"\",\"source\":\"\",\"event_id\":\"\",\"sent_at_unix_ms\":0}";
            }

            string text = EscapeJsonString(payload.text ?? string.Empty);
            string source = EscapeJsonString(payload.source ?? string.Empty);
            string eventId = EscapeJsonString(payload.event_id ?? string.Empty);
            return "{\"text\":\"" + text +
                   "\",\"source\":\"" + source +
                   "\",\"event_id\":\"" + eventId +
                   "\",\"sent_at_unix_ms\":" + payload.sent_at_unix_ms.ToString() +
                   ",\"direct_change\":" + (payload.direct_change ? "true" : "false") + "}";
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                switch (ch)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (ch < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)ch).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(ch);
                        }
                        break;
                }
            }

            return sb.ToString();
        }

        private void AddPresetFromDraft()
        {
            if (_inputField == null)
            {
                return;
            }

            string text = (_inputField.text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            PushUniqueFirst(_inputPresets, text, _settings.InputPanelPresetMax);
            SaveInputPanelState();
            RebuildInputLists();
            _focusInputNextFrame = true;
        }

        private static void PushUniqueFirst(List<string> list, string text, int max)
        {
            if (list == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string normalized = text.Trim();
            list.RemoveAll(x => string.Equals(x, normalized, StringComparison.Ordinal));
            list.Insert(0, normalized);
            while (list.Count > Mathf.Max(1, max))
            {
                list.RemoveAt(list.Count - 1);
            }
        }

        private void ReloadInputPanelState()
        {
            if (string.IsNullOrEmpty(_inputPanelStatePath) || _settings == null)
            {
                return;
            }

            InputPanelState state = InputPanelStateStore.LoadOrCreate(
                _inputPanelStatePath,
                _settings.InputPanelHistoryMax,
                _settings.InputPanelPresetMax,
                Log,
                LogWarn,
                LogError);

            _inputHistory.Clear();
            _inputPresets.Clear();
            if (state != null)
            {
                if (state.History != null)
                {
                    _inputHistory.AddRange(state.History);
                }

                if (state.Presets != null)
                {
                    _inputPresets.AddRange(state.Presets);
                }
            }
            _directChangeEnabled = state != null && state.DirectChangeEnabled;
            _micSendEnabled = state == null || state.MicSendEnabled != false;

            RebuildInputLists();
        }

        private void SaveInputPanelState()
        {
            if (string.IsNullOrEmpty(_inputPanelStatePath) || _settings == null)
            {
                return;
            }

            var state = new InputPanelState
            {
                History = new List<string>(_inputHistory),
                Presets = new List<string>(_inputPresets),
                DirectChangeEnabled = _directChangeEnabled
                ,MicSendEnabled = _micSendEnabled
            };
            state.Normalize(_settings.InputPanelHistoryMax, _settings.InputPanelPresetMax);
            InputPanelStateStore.Save(_inputPanelStatePath, state);
        }

        private void BindRuntimeConfig()
        {
            BindDirectChangeConfig();
            int defaultFontSize = _settings?.OverlayFontSize ?? 36;
            float defaultHoldSeconds = _settings?.HoldSeconds ?? 10f;

            _cfgOverlayFontSize = Config.Bind(
                "Overlay",
                "FontSize",
                defaultFontSize,
                new ConfigDescription(
                    "字幕フォントサイズ（Overlay）。",
                    new AcceptableValueRange<int>(12, 96)));

            _cfgDefaultHoldSeconds = Config.Bind(
                "Subtitle",
                "HoldSeconds",
                defaultHoldSeconds,
                new ConfigDescription(
                    "字幕の表示秒数。",
                    new AcceptableValueRange<float>(0.1f, 600f)));

            _cfgCancelSubtitleClearSeconds = Config.Bind(
                "InputPanel",
                "CancelSubtitleClearSeconds",
                5f,
                new ConfigDescription(
                    "喋りキャンセル後、字幕を消すまでの秒数。0で即消す。負値で消さない。"
                    + "KeepVisibleUntilNext がONだと通常は次の字幕まで残り続ける。",
                    new AcceptableValueRange<float>(-1f, 60f)));

            _cfgFinishSubtitleClearSeconds = Config.Bind(
                "FemaleSubtitle",
                "FinishSubtitleClearSeconds",
                2f,
                new ConfigDescription(
                    "返事を最後まで読み終えてから字幕を消すまでの秒数。0で即消す。負値で消さない。"
                    + "KeepVisibleUntilNext がONでも、読み終わりで消えるようになる。",
                    new AcceptableValueRange<float>(-1f, 60f)));

            _cfgKeepOpenAfterSend = Config.Bind(
                "InputPanel",
                "KeepOpenAfterSend",
                false,
                "ONにすると、送信してもパネルが閉じない。続けて打ちたい時用。"
                + "入力欄は送信時に空になり、そのまま次を打てる。");

            _cfgKeepOpenOnOutsideClick = Config.Bind(
                "InputPanel",
                "KeepOpenOnOutsideClick",
                false,
                "ONにすると、パネルの外をクリックしても閉じない。置いたまま使いたい時用。"
                + "閉じるのは F7 と Escape のみになる。");

            _cfgFemaleKeepVisibleUntilNext = Config.Bind(
                "FemaleSubtitle",
                "KeepVisibleUntilNext",
                false,
                "女性字幕を長めに保持する。次字幕まで残したい場合はON。");

            _cfgFemaleMaxHoldSeconds = Config.Bind(
                "FemaleSubtitle",
                "MaxHoldSeconds",
                30f,
                new ConfigDescription(
                    "女性字幕の最大保持秒数。",
                    new AcceptableValueRange<float>(1f, 600f)));

            _cfgOverlayFontSize.SettingChanged += (_, __) => ApplyRuntimeConfigOverrides();
            _cfgDefaultHoldSeconds.SettingChanged += (_, __) => ApplyRuntimeConfigOverrides();
            _cfgFemaleKeepVisibleUntilNext.SettingChanged += (_, __) => ApplyRuntimeConfigOverrides();
            _cfgFemaleMaxHoldSeconds.SettingChanged += (_, __) => ApplyRuntimeConfigOverrides();
        }

        private void ApplyRuntimeConfigOverrides()
        {
            if (_settings == null)
            {
                return;
            }

            if (_cfgOverlayFontSize != null)
            {
                _settings.OverlayFontSize = Mathf.Clamp(_cfgOverlayFontSize.Value, 12, 96);
            }

            if (_cfgDefaultHoldSeconds != null)
            {
                _settings.HoldSeconds = Mathf.Clamp(_cfgDefaultHoldSeconds.Value, 0.1f, 600f);
            }

            if (_cfgFemaleKeepVisibleUntilNext != null)
            {
                _femaleKeepVisibleUntilNext = _cfgFemaleKeepVisibleUntilNext.Value;
            }

            if (_cfgFemaleMaxHoldSeconds != null)
            {
                _femaleMaxHoldSeconds = Mathf.Clamp(_cfgFemaleMaxHoldSeconds.Value, 1f, 600f);
            }
        }

        private void ReloadSettings()
        {
            _settings = SettingsStore.LoadOrCreate(_pluginDir, Log, LogWarn, LogError);
            _displayBackend = ParseDisplayBackend(_settings?.DisplayBackend);
            _overlayTextColor = ParseColor(_settings?.OverlayTextColor, Color.white, "OverlayTextColor");
            _overlayShadowColor = ParseColor(_settings?.OverlayShadowColor, Color.black, "OverlayShadowColor");
            _overlayIntroHighlightColor = ParseColor(_settings?.OverlayIntroHighlightColor, _overlayTextColor, "OverlayIntroHighlightColor");
            UpdateDirectChangeVisual();
            _overlayStyle = null;
            _overlayShadowStyle = null;
            _overlayResolvedFont = null;
            _overlayResolvedFontSource = string.Empty;

            ApplyRuntimeConfigOverrides();

            Log(
                $"settings loaded: enabled={_settings?.Enabled}, " +
                $"backend={_displayBackend}, mode={_settings?.DisplayMode}, hold={_settings?.HoldSeconds:0.00}s");
        }

        private static DisplayBackendMode ParseDisplayBackend(string backendText)
        {
            if (Enum.TryParse(backendText, ignoreCase: true, out DisplayBackendMode parsed))
            {
                return parsed;
            }

            if (!string.IsNullOrWhiteSpace(backendText))
            {
                LogWarn("invalid DisplayBackend, fallback to Auto: " + backendText);
            }
            return DisplayBackendMode.Auto;
        }

        private static Color ParseColor(string text, Color fallback, string settingName)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            if (ColorUtility.TryParseHtmlString(text, out Color parsed))
            {
                return parsed;
            }

            LogWarn("invalid " + settingName + ", fallback used: " + text);
            return fallback;
        }

        public bool EnqueueSubtitle(SubtitleRequest request, out string reason)
        {
            if (request == null)
            {
                reason = "request is null";
                return false;
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                reason = "text is empty";
                return false;
            }

            SubtitleRequest clone = request.Clone();
            lock (_externalRequestsLock)
            {
                if (_externalRequests.Count >= 128)
                {
                    _externalRequests.Dequeue();
                }

                _externalRequests.Enqueue(clone);
            }

            if (_settings != null && _settings.VerboseLog)
            {
                Log($"external enqueue: text={clone.Text}");
            }

            reason = null;
            return true;
        }

        public bool ForwardText(string text, string source, out string reason)
        {
            return TryForwardInputText(text, source, out reason);
        }

        private void DrainExternalRequests(int maxPerFrame)
        {
            for (int i = 0; i < maxPerFrame; i++)
            {
                SubtitleRequest req = null;
                lock (_externalRequestsLock)
                {
                    if (_externalRequests.Count > 0)
                    {
                        req = _externalRequests.Dequeue();
                    }
                }

                if (req == null)
                {
                    return;
                }

                string text = req.Text;
                float hold = req.HoldSeconds ?? _settings.HoldSeconds;
                string modeText = string.IsNullOrWhiteSpace(req.DisplayMode) ? _settings.DisplayMode : req.DisplayMode;
                DisplayBackendMode backend = req.Backend.HasValue ? ToInternalBackend(req.Backend.Value) : _displayBackend;

                ShowSubtitleCore(text, hold, backend, modeText, "external-api");
            }
        }

        private static DisplayBackendMode ToInternalBackend(SubtitleBackend backend)
        {
            switch (backend)
            {
                case SubtitleBackend.InformationUI:
                    return DisplayBackendMode.InformationUI;
                case SubtitleBackend.Overlay:
                    return DisplayBackendMode.Overlay;
                default:
                    return DisplayBackendMode.Auto;
            }
        }

        private static void ShowSubtitleCore(
            string text,
            float holdSeconds,
            DisplayBackendMode backend,
            string displayModeText,
            string source)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                LogWarn("subtitle text is empty; source=" + source);
                return;
            }

            if (_femaleKeepVisibleUntilNext && IsFemaleSubtitle(displayModeText, text))
            {
                holdSeconds = Mathf.Max(holdSeconds, _femaleMaxHoldSeconds);
            }

            holdSeconds = Mathf.Clamp(holdSeconds, 0.1f, 600f);

            if (backend == DisplayBackendMode.Overlay)
            {
                ShowOverlay(text, holdSeconds, displayModeText, source + ":overlay");
                return;
            }

            if (TryShowWithInformationUI(text, holdSeconds, displayModeText, logMissing: backend == DisplayBackendMode.InformationUI))
            {
                return;
            }

            if (backend == DisplayBackendMode.Auto)
            {
                ShowOverlay(text, holdSeconds, displayModeText, source + ":auto-fallback");
                return;
            }

            LogWarn("InformationUI unavailable and backend=InformationUI; no fallback");
        }

        private static bool TryShowWithInformationUI(
            string text,
            float holdSeconds,
            string displayModeText,
            bool logMissing)
        {
            if (!SingletonInitializer<InformationUI>.initialized)
            {
                if (logMissing)
                {
                    LogWarn("InformationUI not initialized yet");
                }
                return false;
            }

            InformationUI ui = SingletonInitializer<InformationUI>.instance;
            if (ui == null)
            {
                if (logMissing)
                {
                    LogWarn("InformationUI instance is null");
                }
                return false;
            }

            if (WaitTimeField != null)
            {
                WaitTimeField.SetValue(ui, holdSeconds);
            }

            InformationUI.Mode mode = ParseMode(displayModeText);
            InformationUI.Set(text, mode);

            if (_settings.VerboseLog)
            {
                Log($"subtitle shown by InformationUI: mode={mode}, hold={holdSeconds:0.00}, text={text}");
            }

            return true;
        }

        private static bool IsFemaleSubtitle(string displayModeText, string text)
        {
            string mode = (displayModeText ?? string.Empty).Trim();
            if (string.Equals(mode, "StackFemale", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "Female", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (TryExtractFirstColorToken(text, out string colorToken) && IsFemaleColorToken(colorToken))
            {
                return true;
            }

            return false;
        }

        private static bool TryExtractFirstColorToken(string text, out string colorToken)
        {
            colorToken = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            Match match = OverlayColorValueTagRegex.Match(text);
            if (match == null || !match.Success)
            {
                return false;
            }

            Group valueGroup = match.Groups["value"];
            if (valueGroup == null || !valueGroup.Success)
            {
                return false;
            }

            colorToken = NormalizeColorToken(valueGroup.Value);
            return !string.IsNullOrWhiteSpace(colorToken);
        }

        private static string NormalizeColorToken(string token)
        {
            string value = (token ?? string.Empty).Trim().Trim('"', '\'').ToLowerInvariant();
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            if (!value.StartsWith("#"))
            {
                return value;
            }

            if (value.Length == 9)
            {
                return value.Substring(0, 7);
            }

            if (value.Length == 4 || value.Length == 5)
            {
                char r = value[1];
                char g = value[2];
                char b = value[3];
                return "#" + r + r + g + g + b + b;
            }

            return value;
        }

        private static bool IsFemaleColorToken(string token)
        {
            return token == "pink" ||
                token == "hotpink" ||
                token == "#ffc0cb" ||
                token == "#ff69b4" ||
                token == "#ffb6c1";
        }

        private static void ShowOverlay(string text, float holdSeconds, string displayModeText, string source)
        {
            float now = Time.unscaledTime;
            float until = now + holdSeconds;
            string mode = (displayModeText ?? string.Empty).Trim();
            bool stackMale = string.Equals(mode, "StackMale", StringComparison.OrdinalIgnoreCase);
            bool stackFemale = string.Equals(mode, "StackFemale", StringComparison.OrdinalIgnoreCase);

            if (stackFemale)
            {
                if (!string.IsNullOrWhiteSpace(_overlayText) && _overlayUntil > now)
                {
                    _overlayUpperText = _overlayText;
                    _overlayUpperUntil = Mathf.Max(_overlayUntil, until);
                }
                else
                {
                    _overlayUpperText = string.Empty;
                    _overlayUpperUntil = 0f;
                }

                _overlayText = text ?? string.Empty;
                _overlayUntil = until;
            }
            else
            {
                // Normal and StackMale both reset to a single active line.
                _overlayUpperText = string.Empty;
                _overlayUpperUntil = 0f;
                _overlayText = text ?? string.Empty;
                _overlayUntil = until;
            }

            OverlayContent.text = _overlayText;
            OverlayUpperContent.text = _overlayUpperText;
            _overlayShadowText = StripColorTags(_overlayText);
            _overlayUpperShadowText = StripColorTags(_overlayUpperText);

            _overlayStartedAt = now;
            EnsureOverlayResources();

            if (_settings != null && _settings.VerboseLog)
            {
                Log(
                    $"subtitle shown by overlay ({source}): hold={holdSeconds:0.00}, " +
                    $"mode={mode}, text={_overlayText}");
            }
        }

        private static void EnsureOverlayResources()
        {
            if (_overlayStyle == null)
            {
                _overlayStyle = new GUIStyle(GUI.skin.label);
            }

            if (_overlayShadowStyle == null)
            {
                _overlayShadowStyle = new GUIStyle(GUI.skin.label);
            }

            _overlayStyle.alignment = TextAnchor.MiddleCenter;
            _overlayStyle.wordWrap = true;
            _overlayStyle.richText = true;
            _overlayStyle.fontSize = _settings?.OverlayFontSize ?? 28;
            _overlayStyle.normal.textColor = _overlayTextColor;
            _overlayStyle.fontStyle = FontStyle.Normal;

            _overlayShadowStyle.alignment = _overlayStyle.alignment;
            _overlayShadowStyle.wordWrap = _overlayStyle.wordWrap;
            _overlayShadowStyle.richText = _overlayStyle.richText;
            _overlayShadowStyle.fontSize = _overlayStyle.fontSize;
            _overlayShadowStyle.fontStyle = _overlayStyle.fontStyle;
            _overlayShadowStyle.normal.textColor = _overlayShadowColor;
            _overlayShadowStyle.font = _overlayStyle.font;

            if (_settings != null && _settings.OverlayUseConversationFont)
            {
                Font font = ResolveConversationFont();
                if (font != null)
                {
                    _overlayStyle.font = font;
                    _overlayShadowStyle.font = font;
                }
            }
        }

        private static Font ResolveConversationFont()
        {
            if (_overlayResolvedFont != null)
            {
                if (!string.IsNullOrWhiteSpace(_overlayResolvedFontSource) &&
                    _overlayResolvedFontSource.IndexOf("MainGameSubtitleInputCanvas", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _overlayResolvedFont = null;
                    _overlayResolvedFontSource = string.Empty;
                }
                else
                {
                    return _overlayResolvedFont;
                }
            }

            if (_overlayResolvedFont != null)
            {
                return _overlayResolvedFont;
            }

            float now = Time.unscaledTime;
            if (now < _nextOverlayFontResolveTime)
                return null;
            _nextOverlayFontResolveTime = now + 1f;

            TextController[] controllers = Resources.FindObjectsOfTypeAll<TextController>();
            for (int i = 0; i < controllers.Length; i++)
            {
                TextController controller = controllers[i];
                if (controller == null)
                {
                    continue;
                }

                Text message = controller.MessageWindow;
                if (message != null && message.font != null)
                {
                    CacheResolvedFont(message.font, "TextController.MessageWindow");
                    return _overlayResolvedFont;
                }
            }

            Text[] activeTexts = UnityEngine.Object.FindObjectsOfType<Text>();
            Text best = ChooseBestConversationText(activeTexts);
            if (best != null && best.font != null)
            {
                CacheResolvedFont(best.font, "SceneActiveText:" + BuildObjectPath(best.transform));
                return _overlayResolvedFont;
            }

            Text[] allTexts = Resources.FindObjectsOfTypeAll<Text>();
            best = ChooseBestConversationText(allTexts);
            if (best != null && best.font != null)
            {
                CacheResolvedFont(best.font, "SceneAllText:" + BuildObjectPath(best.transform));
                return _overlayResolvedFont;
            }

            return null;
        }

        private static Text ChooseBestConversationText(Text[] texts)
        {
            Text best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < texts.Length; i++)
            {
                Text t = texts[i];
                if (t == null || t.font == null)
                {
                    continue;
                }

                if (IsOwnInputUiObject(t.transform))
                {
                    continue;
                }

                int score = ScoreTextCandidate(t);
                if (best == null || score > bestScore)
                {
                    best = t;
                    bestScore = score;
                }
            }

            // 起動直後は会話画面がまだ存在せず、確認ダイアログ等の一般UIが
            // 最高点になることがある。それを会話フォントとして永続キャッシュ
            // すると、以後本物のMessageWindowが現れてもフォントが戻らない。
            return bestScore >= 300 ? best : null;
        }

        private static int ScoreTextCandidate(Text t)
        {
            int score = 0;
            if (t.gameObject.activeInHierarchy)
            {
                score += 100;
            }

            string path = BuildObjectPath(t.transform).ToLowerInvariant();
            score += ContainsScore(path, "message", 500);
            score += ContainsScore(path, "talk", 400);
            score += ContainsScore(path, "name", 220);
            score += ContainsScore(path, "window", 200);
            score += ContainsScore(path, "subtitle", 350);
            score += ContainsScore(path, "textscenario", 450);
            score += ContainsScore(path, "adv", 120);
            score += ContainsScore(path, "menu", -120);
            score += ContainsScore(path, "button", -160);

            score += Mathf.Clamp(t.fontSize, 0, 120);

            return score;
        }

        private static int ContainsScore(string text, string token, int score)
        {
            if (text.IndexOf(token, StringComparison.Ordinal) >= 0)
            {
                return score;
            }

            return 0;
        }

        private static string BuildObjectPath(Transform tr)
        {
            if (tr == null)
            {
                return "null";
            }

            StringBuilder sb = new StringBuilder(128);
            Transform cur = tr;
            int guard = 0;
            while (cur != null && guard < 64)
            {
                if (sb.Length > 0)
                {
                    sb.Insert(0, "/");
                }

                sb.Insert(0, cur.name);
                cur = cur.parent;
                guard++;
            }

            return sb.ToString();
        }

        private static void CacheResolvedFont(Font font, string source)
        {
            _overlayResolvedFont = font;
            _overlayResolvedFontSource = source ?? string.Empty;
            Log("overlay font resolved: " + font.name + " source=" + _overlayResolvedFontSource);
        }

        private static float EaseOutBack01(float t, float overshoot)
        {
            float x = Mathf.Clamp01(t) - 1f;
            float c = Mathf.Max(0f, overshoot);
            return 1f + (x * x * ((c + 1f) * x + c));
        }

        private static InformationUI.Mode ParseMode(string modeText)
        {
            if (Enum.TryParse(modeText, ignoreCase: true, out InformationUI.Mode mode))
            {
                return mode;
            }

            LogWarn("invalid DisplayMode, fallback to Normal: " + modeText);
            return InformationUI.Mode.Normal;
        }

        /// <summary>他クラス(ドラッガー)から使うためのログ入口。</summary>
        internal static void LogStatic(string message) => Log(message);

        private static void Log(string message)
        {
            if (!RuntimeLoggingEnabled) return;
            if (_settings == null || !_settings.VerboseLog) return;
            Logger?.LogInfo(message);
            WriteFile("INFO", message);
        }

        private static void LogWarn(string message)
        {
            if (!RuntimeLoggingEnabled) return;
            Logger?.LogWarning(message);
            WriteFile("WARN", message);
        }

        private static void LogError(string message)
        {
            if (!RuntimeLoggingEnabled) return;
            Logger?.LogError(message);
            WriteFile("ERROR", message);
        }

        private static void WriteFile(string level, string message)
        {
            if (!RuntimeLoggingEnabled) return;
            try
            {
                lock (FileLogLock)
                {
                    File.AppendAllText(
                        _logFilePath,
                        $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}{Environment.NewLine}",
                        Utf8NoBom);
                }
            }
            catch
            {
            }
        }
    }
}
