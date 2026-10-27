using Bindings = CanonPlugins.Shared.PluginInput;
using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using CanonPlugins.Shared;

namespace MainGameFreeHMasturbationMenu
{
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    public sealed class Plugin : BaseUnityPlugin
    {
        private static bool SuppressRuntimeLogs => true;
        private const string Guid = "com.kks.maingame.freehmasturbationmenu";
        private const string PluginName = "MainGameFreeHMasturbationMenu";
        private const string Version = "0.1.0";
        private const string SettingsFileName = "MainGameFreeHMasturbationMenuSettings.json";
        private const string CustomButtonObjectName = "__MasturbationActionButton";
        private const string PosePanelObjectName = "__MasturbationPosePanel";

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly BindingFlags InstanceAny =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo HSpriteMenuActionField =
            typeof(HSprite).GetField("menuAction", InstanceAny);
        private static readonly FieldInfo HSceneLstUseAnimInfoField =
            typeof(HSceneProc).GetField("lstUseAnimInfo", InstanceAny);
        private static readonly FieldInfo HSceneLstFemaleField =
            typeof(HSceneProc).GetField("lstFemale", InstanceAny);
        private static readonly FieldInfo HSceneLstMaleField =
            typeof(HSceneProc).GetField("lstMale", InstanceAny);
        private static readonly FieldInfo HSceneItemField =
            typeof(HSceneProc).GetField("item", InstanceAny);
        private static readonly MethodInfo HSceneChangeAnimatorMethod =
            typeof(HSceneProc).GetMethod(
                "ChangeAnimator",
                InstanceAny,
                null,
                new[] { typeof(HSceneProc.AnimationListInfo), typeof(bool) },
                null);

        internal static Plugin Instance { get; private set; }

        private readonly object _logLock = new object();

        private Harmony _harmony;
        private PluginSettings _settings;
        private string _pluginDir;
        private string _logPath;

        private HSceneProc _hSceneProc;
        private HSprite _hSprite;
        private int _boundSpriteInstanceId = -1;
        private HFlag.EMode _lastKnownMode = HFlag.EMode.none;
        private float _nextScanTime;
        private float _nextRecoverLogTime;
        private float _nextWheelLogTime;
        private float _nextSpeedLinkLogTime;
        private float _lastLoggedSpeedLinkGauge = float.NaN;
        private float _lastLoggedSpeedLinkAnimSpeed = float.NaN;
        private float _lastLoggedSpeedLinkSpeed = float.NaN;
        private float _lastLoggedSpeedLinkSpeedCalc = float.NaN;
        private HFlag.EMode _lastLoggedSpeedLinkMode = HFlag.EMode.none;
        private string _lastLoggedSpeedLinkSource = string.Empty;
        private int _masturbationCycleIndex;
        private bool _masturbationSpeedApplied;
        private bool _maleGaugeLockActive;
        private float _maleGaugeLockValue = -1f;

        private GameObject _customButton;
        private TMP_Text _customButtonTmpText;
        private Text _customButtonUiText;
        private bool _customButtonLayoutApplied;
        private GameObject _cachedTemplateButton;
        private GameObject _cachedAnchorButton;
        private int _cachedTemplateIndex = -1;
        private int _cachedAnchorIndex = -1;
        private float _nextMenuButtonLookupAt;
        private GameObject _posePanel;
        private RectTransform _posePanelRect;
        private RectTransform _posePanelContentRect;
        private readonly List<Button> _posePanelButtons = new List<Button>();
        private readonly List<HSceneProc.AnimationListInfo> _posePanelInfos = new List<HSceneProc.AnimationListInfo>();

        private void Awake()
        {
            Instance = this;
            _pluginDir = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
            Directory.CreateDirectory(_pluginDir);

            _logPath = Path.Combine(_pluginDir, "MainGameFreeHMasturbationMenu.log");
            if (!SuppressRuntimeLogs) File.WriteAllText(
                _logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] === {PluginName} {Version} started ==={Environment.NewLine}",
                Utf8NoBom);

            _settings = SettingsStore.LoadOrCreate(_pluginDir, SettingsFileName, LogInfoFile, LogWarnFile, LogErrorFile);
            LogInfoFile(
                $"settings loaded enabled={_settings.Enabled} freeHOnly={_settings.FreeHOnly} " +
                $"buttonText={_settings.ButtonText} cycle={_settings.CycleMasturbationPoses}");

            try
            {
                _harmony = new Harmony(Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                LogInfoFile("harmony patches applied");
            }
            catch (Exception ex)
            {
                LogErrorFile("failed to apply harmony patches: " + ex);
            }
        }

        private void OnDestroy()
        {
            try
            {
                _harmony?.UnpatchSelf();
            }
            catch (Exception ex)
            {
                LogWarnFile("unpatch failed: " + ex.Message);
            }

            DestroyCustomButton();
            _hSceneProc = null;
            _hSprite = null;
            Instance = null;
        }

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            if (Bindings.Down("MainGameFreeHMasturbationMenu.reload", "R + LeftControl"))
            {
                ReloadSettings();
                return;
            }

            if (Time.unscaledTime >= _nextScanTime)
            {
                _nextScanTime = Time.unscaledTime + 0.5f;
                ScanSceneObjects();
            }

            if (!CanRun())
            {
                HandleModeChange(HFlag.EMode.none);
                ClearMaleGaugeLock();
                if (_customButton != null && _customButton.activeSelf)
                {
                    _customButton.SetActive(value: false);
                }
                if (_posePanel != null && _posePanel.activeSelf)
                {
                    _posePanel.SetActive(value: false);
                }
                return;
            }

            if (_settings.ForceMasturbationModeContext)
            {
                ForceMasturbationModeContext();
            }

            HandleModeChange(_hSceneProc.flags.mode);

            if (_settings.EnableMouseWheelSpeedControl)
            {
                HandleMouseWheelSpeedControl();
            }

            bool linkedMasturbationSpeed = false;
            if (_settings.EnableMasturbationGaugeSpeedLink)
            {
                linkedMasturbationSpeed = LinkMasturbationPlaybackSpeedToGauge("update");
            }
            if (!linkedMasturbationSpeed)
            {
                ResetMasturbationPlaybackSpeed("update");
            }

            EnsureCustomButton();

            if (_posePanel != null && _posePanel.activeSelf)
            {
                UpdatePosePanelHighlight();
            }

            if (_settings.KeepActionMenuVisibleInMasturbation)
            {
                KeepActionMenuVisibleInMasturbation();
            }

            if (_settings.KeepSpeedGaugeVisibleInMasturbation)
            {
                KeepSpeedGaugeVisibleForMasturbationPose();
            }

            if (_settings.LockMaleGaugeDuringMasturbation)
            {
                ApplyMaleGaugeLockForMasturbationPose();
            }
            else
            {
                ClearMaleGaugeLock();
            }

            if (_settings.AutoRecoverTransitionFromMasturbation)
            {
                RecoverPendingSelectionFromMasturbation();
            }
        }

        private void ReloadSettings()
        {
            _settings = SettingsStore.LoadOrCreate(_pluginDir, SettingsFileName, LogInfoFile, LogWarnFile, LogErrorFile);
            LogInfoFile(
                $"settings reloaded enabled={_settings.Enabled} freeHOnly={_settings.FreeHOnly} " +
                $"buttonText={_settings.ButtonText} cycle={_settings.CycleMasturbationPoses}");

            _customButtonLayoutApplied = false;
            if (_posePanel != null)
            {
                _posePanel.SetActive(value: false);
            }
            UpdateCustomButtonText();
        }

        private void ScanSceneObjects()
        {
            HSceneProc foundProc = FindObjectOfType<HSceneProc>();
            HSprite foundSprite = FindObjectOfType<HSprite>();

            bool changed =
                !ReferenceEquals(_hSceneProc, foundProc) ||
                !ReferenceEquals(_hSprite, foundSprite);

            _hSceneProc = foundProc;
            _hSprite = foundSprite;

            int spriteId = _hSprite != null ? _hSprite.GetInstanceID() : -1;
            if (changed || spriteId != _boundSpriteInstanceId)
            {
                _boundSpriteInstanceId = spriteId;
                _masturbationCycleIndex = 0;
                DestroyCustomButton();

                if (_hSprite != null && _settings.VerboseLog)
                {
                    LogInfoFile("HSprite detected; custom button will be rebuilt");
                }
            }
        }

        private bool CanRun()
        {
            if (_settings == null || !_settings.Enabled)
            {
                return false;
            }

            if (_hSceneProc == null || _hSceneProc.flags == null)
            {
                return false;
            }

            if (_hSprite == null)
            {
                return false;
            }

            if (_settings.FreeHOnly && !_hSceneProc.flags.isFreeH)
            {
                return false;
            }

            return true;
        }

        private void EnsureCustomButton()
        {
            int templateIndex = _settings.TemplateButtonIndex;
            int anchorIndex = _settings.AnchorButtonIndex;
            if (_cachedTemplateButton == null || _cachedTemplateIndex != templateIndex
                || _cachedAnchorIndex != anchorIndex || Time.unscaledTime >= _nextMenuButtonLookupAt)
            {
                _cachedTemplateButton = GetMenuActionObject(templateIndex);
                _cachedAnchorButton = GetMenuActionObject(anchorIndex);
                _cachedTemplateIndex = templateIndex;
                _cachedAnchorIndex = anchorIndex;
                _nextMenuButtonLookupAt = Time.unscaledTime + 0.25f;
            }

            GameObject template = _cachedTemplateButton;
            if (template == null)
            {
                SetCustomUiVisible(visible: false);
                return;
            }

            GameObject anchor = _cachedAnchorButton ?? template;
            if (_customButton == null)
            {
                _customButton = CreateButtonFromTemplate(template);
                if (_customButton == null)
                {
                    return;
                }
            }

            RectTransform customRect = _customButton.GetComponent<RectTransform>();
            RectTransform anchorRect = anchor.GetComponent<RectTransform>();
            RectTransform templateRect = template.GetComponent<RectTransform>();
            if (customRect == null || templateRect == null)
            {
                SetCustomUiVisible(visible: false);
                return;
            }

            RectTransform sourceRect = anchorRect ?? templateRect;
            if (!_customButtonLayoutApplied)
            {
                customRect.SetParent(templateRect.parent, worldPositionStays: false);
                customRect.anchoredPosition = sourceRect.anchoredPosition + new Vector2(_settings.ButtonOffsetX, _settings.ButtonOffsetY);
                customRect.localScale = sourceRect.localScale;
                customRect.localRotation = sourceRect.localRotation;
                customRect.sizeDelta = sourceRect.sizeDelta;
                customRect.SetSiblingIndex(Mathf.Min(sourceRect.GetSiblingIndex() + 1, customRect.parent.childCount - 1));
                _customButtonLayoutApplied = true;
            }

            UpdateCustomButtonText();
            EnsurePosePanel(templateRect, sourceRect);
            KeepCustomButtonAbovePosePanel(customRect);

            bool sourceVisible =
                template.activeSelf &&
                template.activeInHierarchy &&
                sourceRect.gameObject.activeSelf &&
                sourceRect.gameObject.activeInHierarchy;
            SetCustomUiVisible(_settings.Enabled && sourceVisible);
        }

        private void SetCustomUiVisible(bool visible)
        {
            if (_customButton != null)
            {
                if (_customButton.activeSelf != visible)
                    _customButton.SetActive(visible);
            }

            if (!visible && _posePanel != null && _posePanel.activeSelf)
            {
                _posePanel.SetActive(value: false);
            }
        }

        private GameObject CreateButtonFromTemplate(GameObject template)
        {
            RectTransform templateRect = template.GetComponent<RectTransform>();
            if (templateRect == null)
            {
                LogWarnFile("template button has no RectTransform");
                return null;
            }

            GameObject buttonObject = new GameObject(
                CustomButtonObjectName,
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));

            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.anchorMin = templateRect.anchorMin;
            rect.anchorMax = templateRect.anchorMax;
            rect.pivot = templateRect.pivot;
            rect.sizeDelta = templateRect.sizeDelta;
            rect.localScale = templateRect.localScale;

            Image srcImage = template.GetComponent<Image>();
            Image dstImage = buttonObject.GetComponent<Image>();
            if (srcImage != null && dstImage != null)
            {
                dstImage.sprite = srcImage.sprite;
                dstImage.type = srcImage.type;
                dstImage.color = srcImage.color;
                dstImage.material = srcImage.material;
                dstImage.preserveAspect = srcImage.preserveAspect;
                dstImage.raycastTarget = srcImage.raycastTarget;
                dstImage.fillCenter = srcImage.fillCenter;
                dstImage.fillMethod = srcImage.fillMethod;
                dstImage.fillOrigin = srcImage.fillOrigin;
                dstImage.fillAmount = srcImage.fillAmount;
                dstImage.fillClockwise = srcImage.fillClockwise;
            }

            Button srcButton = template.GetComponent<Button>();
            Button dstButton = buttonObject.GetComponent<Button>();
            if (srcButton != null && dstButton != null)
            {
                dstButton.transition = srcButton.transition;
                dstButton.colors = srcButton.colors;
                dstButton.spriteState = srcButton.spriteState;
                dstButton.navigation = srcButton.navigation;
                dstButton.targetGraphic = dstImage;
            }
            dstButton.onClick.AddListener(OnMasturbationButtonClick);

            LayoutElement layout = buttonObject.GetComponent<LayoutElement>();
            if (layout != null)
            {
                layout.ignoreLayout = true;
            }

            TMP_Text srcTmp = template.GetComponentInChildren<TMP_Text>(includeInactive: true);
            Text srcText = template.GetComponentInChildren<Text>(includeInactive: true);
            if (srcTmp != null)
            {
                _customButtonTmpText = Instantiate(srcTmp, buttonObject.transform, worldPositionStays: false);
                _customButtonTmpText.name = "Text";
                _customButtonUiText = null;
            }
            else if (srcText != null)
            {
                _customButtonUiText = Instantiate(srcText, buttonObject.transform, worldPositionStays: false);
                _customButtonUiText.name = "Text";
                _customButtonTmpText = null;
            }
            else
            {
                GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
                textObject.transform.SetParent(buttonObject.transform, worldPositionStays: false);
                RectTransform textRect = textObject.GetComponent<RectTransform>();
                textRect.anchorMin = new Vector2(0f, 0f);
                textRect.anchorMax = new Vector2(1f, 1f);
                textRect.offsetMin = Vector2.zero;
                textRect.offsetMax = Vector2.zero;

                _customButtonUiText = textObject.GetComponent<Text>();
                _customButtonUiText.alignment = TextAnchor.MiddleCenter;
                _customButtonUiText.color = Color.white;
                _customButtonUiText.fontSize = 20;
                _customButtonUiText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                _customButtonTmpText = null;
            }

            UpdateCustomButtonText();
            return buttonObject;
        }

        private void OnMasturbationButtonClick()
        {
            try
            {
                if (!CanRun())
                {
                    return;
                }

                if (_settings.EnablePoseSelectionMenu)
                {
                    TogglePosePanel();
                    return;
                }

                List<HSceneProc.AnimationListInfo> masturbationList = GetMasturbationPoseList();
                if (masturbationList == null || masturbationList.Count == 0)
                {
                    LogWarnFile("masturbation button clicked, but no masturbation poses in lstUseAnimInfo[3]");
                    return;
                }

                int index = ResolveMasturbationIndex(masturbationList);
                HSceneProc.AnimationListInfo selected = masturbationList[index];
                if (selected == null)
                {
                    LogWarnFile("selected masturbation pose is null");
                    return;
                }

                ApplyAnimationSelection(selected, "button");
                _masturbationCycleIndex = (_masturbationCycleIndex + 1) % masturbationList.Count;
            }
            catch (Exception ex)
            {
                LogErrorFile("masturbation button click failed: " + ex);
            }
        }

        private void TogglePosePanel()
        {
            if (_posePanel == null)
            {
                return;
            }

            bool open = !_posePanel.activeSelf;
            _posePanel.SetActive(open);
            if (open)
            {
                RebuildPosePanelItems();
                UpdatePosePanelHighlight();
            }
        }

        private void EnsurePosePanel(RectTransform templateRect, RectTransform anchorRect)
        {
            if (!_settings.EnablePoseSelectionMenu)
            {
                if (_posePanel != null && _posePanel.activeSelf)
                {
                    _posePanel.SetActive(value: false);
                }
                return;
            }

            if (_posePanel == null)
            {
                _posePanel = new GameObject(
                    PosePanelObjectName,
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(ScrollRect),
                    typeof(LayoutElement));
                _posePanelRect = _posePanel.GetComponent<RectTransform>();
                Image panelImage = _posePanel.GetComponent<Image>();
                panelImage.color = new Color(0f, 0f, 0f, 0.72f);
                panelImage.raycastTarget = true;

                LayoutElement panelLayout = _posePanel.GetComponent<LayoutElement>();
                if (panelLayout != null)
                {
                    panelLayout.ignoreLayout = true;
                }

                GameObject viewport = new GameObject(
                    "Viewport",
                    typeof(RectTransform),
                    typeof(Image),
                    typeof(Mask));
                RectTransform viewportRect = viewport.GetComponent<RectTransform>();
                viewportRect.SetParent(_posePanelRect, worldPositionStays: false);
                viewportRect.anchorMin = new Vector2(0f, 0f);
                viewportRect.anchorMax = new Vector2(1f, 1f);
                viewportRect.offsetMin = new Vector2(6f, 6f);
                viewportRect.offsetMax = new Vector2(-6f, -6f);
                Image viewportImage = viewport.GetComponent<Image>();
                viewportImage.color = new Color(0f, 0f, 0f, 0.01f);
                Mask viewportMask = viewport.GetComponent<Mask>();
                viewportMask.showMaskGraphic = false;

                GameObject content = new GameObject(
                    "Content",
                    typeof(RectTransform),
                    typeof(VerticalLayoutGroup),
                    typeof(ContentSizeFitter));
                _posePanelContentRect = content.GetComponent<RectTransform>();
                _posePanelContentRect.SetParent(viewportRect, worldPositionStays: false);
                _posePanelContentRect.anchorMin = new Vector2(0f, 1f);
                _posePanelContentRect.anchorMax = new Vector2(1f, 1f);
                _posePanelContentRect.pivot = new Vector2(0.5f, 1f);
                _posePanelContentRect.offsetMin = new Vector2(0f, 0f);
                _posePanelContentRect.offsetMax = new Vector2(0f, 0f);
                _posePanelContentRect.anchoredPosition = Vector2.zero;

                VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.childControlWidth = true;
                vlg.childControlHeight = false;
                vlg.childForceExpandWidth = true;
                vlg.childForceExpandHeight = false;
                vlg.spacing = 4f;
                vlg.padding = new RectOffset(2, 2, 2, 2);

                ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

                ScrollRect scroll = _posePanel.GetComponent<ScrollRect>();
                scroll.viewport = viewportRect;
                scroll.content = _posePanelContentRect;
                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.scrollSensitivity = 30f;
                scroll.movementType = ScrollRect.MovementType.Clamped;

                _posePanel.SetActive(value: false);
            }

            if (_posePanelRect == null)
            {
                return;
            }

            if (_posePanelRect.parent != templateRect.parent)
                _posePanelRect.SetParent(templateRect.parent, worldPositionStays: false);
            if (_posePanelRect.anchorMin != templateRect.anchorMin) _posePanelRect.anchorMin = templateRect.anchorMin;
            if (_posePanelRect.anchorMax != templateRect.anchorMax) _posePanelRect.anchorMax = templateRect.anchorMax;
            if (_posePanelRect.pivot != templateRect.pivot) _posePanelRect.pivot = templateRect.pivot;
            if (_posePanelRect.localScale != templateRect.localScale) _posePanelRect.localScale = templateRect.localScale;
            if (_posePanelRect.localRotation != templateRect.localRotation) _posePanelRect.localRotation = templateRect.localRotation;
            float panelWidth = Mathf.Max(120f, _settings.PosePanelWidth);
            float panelHeight = Mathf.Max(80f, _settings.PosePanelHeight);
            Vector2 panelSize = new Vector2(panelWidth, panelHeight);
            if (_posePanelRect.sizeDelta != panelSize) _posePanelRect.sizeDelta = panelSize;

            Vector2 panelPosition = anchorRect.anchoredPosition + new Vector2(
                _settings.PosePanelOffsetX,
                _settings.PosePanelOffsetY);
            if (_settings.PosePanelPlaceLeftOfIcon)
            {
                float anchorWidth = Mathf.Max(0f, anchorRect.rect.width * Mathf.Abs(anchorRect.localScale.x));
                float sideGap = Mathf.Max(0f, _settings.PosePanelSideGap);
                panelPosition.x -= (anchorWidth * 0.5f) + panelWidth + sideGap;
            }

            if (_posePanelRect.anchoredPosition != panelPosition)
                _posePanelRect.anchoredPosition = panelPosition;
            int panelSiblingIndex = Mathf.Min(templateRect.GetSiblingIndex() + 2, _posePanelRect.parent.childCount - 1);
            if (_posePanelRect.GetSiblingIndex() != panelSiblingIndex)
                _posePanelRect.SetSiblingIndex(panelSiblingIndex);
        }

        private void KeepCustomButtonAbovePosePanel(RectTransform customRect)
        {
            if (customRect == null || _posePanelRect == null || customRect.parent != _posePanelRect.parent)
            {
                return;
            }

            int panelIndex = _posePanelRect.GetSiblingIndex();
            int buttonIndex = customRect.GetSiblingIndex();
            if (buttonIndex > panelIndex)
            {
                return;
            }

            int maxIndex = customRect.parent.childCount - 1;
            customRect.SetSiblingIndex(Mathf.Min(panelIndex + 1, maxIndex));
        }

        private void RebuildPosePanelItems()
        {
            if (_posePanelContentRect == null)
            {
                return;
            }

            for (int i = _posePanelContentRect.childCount - 1; i >= 0; i--)
            {
                Destroy(_posePanelContentRect.GetChild(i).gameObject);
            }
            _posePanelButtons.Clear();
            _posePanelInfos.Clear();

            List<HSceneProc.AnimationListInfo> masturbationList = GetMasturbationPoseList();
            if (masturbationList == null || masturbationList.Count == 0)
            {
                CreatePoseListItem(null, -1);
                return;
            }

            for (int i = 0; i < masturbationList.Count; i++)
            {
                CreatePoseListItem(masturbationList[i], i);
            }
        }

        private void CreatePoseListItem(HSceneProc.AnimationListInfo info, int index)
        {
            GameObject line = new GameObject(
                index >= 0 ? $"Pose_{index:D2}" : "Pose_Empty",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            RectTransform lineRect = line.GetComponent<RectTransform>();
            lineRect.SetParent(_posePanelContentRect, worldPositionStays: false);
            lineRect.anchorMin = new Vector2(0f, 1f);
            lineRect.anchorMax = new Vector2(1f, 1f);
            lineRect.pivot = new Vector2(0.5f, 1f);
            lineRect.sizeDelta = new Vector2(0f, Mathf.Max(22f, _settings.PoseButtonHeight));

            Image lineImage = line.GetComponent<Image>();
            lineImage.color = (index >= 0)
                ? new Color(1f, 1f, 1f, 0.12f)
                : new Color(1f, 1f, 1f, 0.06f);

            LayoutElement lineLayout = line.GetComponent<LayoutElement>();
            lineLayout.preferredHeight = Mathf.Max(22f, _settings.PoseButtonHeight);

            Button lineButton = line.GetComponent<Button>();
            if (index < 0)
            {
                lineButton.interactable = false;
            }

            GameObject textObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(lineRect, worldPositionStays: false);
            textRect.anchorMin = new Vector2(0f, 0f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.offsetMin = new Vector2(8f, 2f);
            textRect.offsetMax = new Vector2(-8f, -2f);
            Text text = textObject.GetComponent<Text>();
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.fontSize = 18;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");

            if (index < 0 || info == null)
            {
                text.text = "オナニー体位が見つかりません";
                return;
            }

            string poseName = string.IsNullOrWhiteSpace(info.nameAnimation)
                ? $"ID:{info.id}"
                : info.nameAnimation;
            text.text = $"{index + 1:00}. {poseName}";
            _posePanelButtons.Add(lineButton);
            _posePanelInfos.Add(info);

            HSceneProc.AnimationListInfo capturedInfo = info;
            lineButton.onClick.AddListener(delegate
            {
                if (capturedInfo == null)
                {
                    return;
                }

                ApplyAnimationSelection(capturedInfo, $"panel:id={capturedInfo.id}");
                if (_settings.ClosePosePanelAfterSelect && _posePanel != null)
                {
                    _posePanel.SetActive(value: false);
                }
            });
        }

        private void UpdatePosePanelHighlight()
        {
            if (_hSceneProc == null || _hSceneProc.flags == null)
            {
                return;
            }

            int currentId = (_hSceneProc.flags.nowAnimationInfo != null) ? _hSceneProc.flags.nowAnimationInfo.id : -1;
            bool inMasturbation = _hSceneProc.flags.mode == HFlag.EMode.masturbation;
            for (int i = 0; i < _posePanelButtons.Count && i < _posePanelInfos.Count; i++)
            {
                Button button = _posePanelButtons[i];
                HSceneProc.AnimationListInfo info = _posePanelInfos[i];
                if (button == null || info == null)
                {
                    continue;
                }

                bool active = inMasturbation && info.id == currentId;
                Image image = button.GetComponent<Image>();
                if (image != null)
                {
                    image.color = active
                        ? new Color(0.25f, 0.75f, 0.30f, 0.75f)
                        : new Color(1f, 1f, 1f, 0.12f);
                }
            }
        }

        private List<HSceneProc.AnimationListInfo> GetMasturbationPoseList()
        {
            if (_hSceneProc == null || HSceneLstUseAnimInfoField == null)
            {
                return null;
            }

            List<HSceneProc.AnimationListInfo>[] useAnimInfo =
                HSceneLstUseAnimInfoField.GetValue(_hSceneProc) as List<HSceneProc.AnimationListInfo>[];
            if (useAnimInfo == null || useAnimInfo.Length <= 3)
            {
                return null;
            }

            return useAnimInfo[3];
        }

        private int ResolveMasturbationIndex(List<HSceneProc.AnimationListInfo> list)
        {
            if (list == null || list.Count == 0)
            {
                return 0;
            }

            if (!_settings.CycleMasturbationPoses)
            {
                return 0;
            }

            if (_settings.StartFromCurrentWhenInMasturbation &&
                _hSceneProc != null &&
                _hSceneProc.flags != null &&
                _hSceneProc.flags.mode == HFlag.EMode.masturbation &&
                _hSceneProc.flags.nowAnimationInfo != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    HSceneProc.AnimationListInfo info = list[i];
                    if (info != null && info.id == _hSceneProc.flags.nowAnimationInfo.id)
                    {
                        return (i + 1) % list.Count;
                    }
                }
            }

            if (_masturbationCycleIndex < 0)
            {
                _masturbationCycleIndex = 0;
            }
            return _masturbationCycleIndex % list.Count;
        }

        private void ApplyAnimationSelection(HSceneProc.AnimationListInfo info, string reason)
        {
            if (_hSceneProc == null || _hSceneProc.flags == null || info == null)
            {
                return;
            }

            HFlag flags = _hSceneProc.flags;
            // Use the same route as vanilla UI to keep mode/name/state transitions consistent.
            flags.selectAnimationListInfo = info;
            flags.voiceWait = false;
            flags.click = HFlag.ClickKind.actionChange;

            if (_settings.KeepActionMenuVisibleInMasturbation)
            {
                KeepActionMenuVisibleInMasturbation();
            }

            LogInfoFile(
                $"animation selection queued ({reason}) mode={info.mode} id={info.id} " +
                $"name={info.nameAnimation}");
        }

        private void HandleModeChange(HFlag.EMode nextMode)
        {
            if (_lastKnownMode == nextMode)
            {
                return;
            }

            HFlag.EMode prev = _lastKnownMode;
            _lastKnownMode = nextMode;

            if (_settings == null)
            {
                return;
            }

            bool leavingMasturbation = prev == HFlag.EMode.masturbation && nextMode != HFlag.EMode.masturbation;
            if (leavingMasturbation)
            {
                ResetMasturbationPlaybackSpeed("mode-change");
                ClearMaleGaugeLock();
            }
        }

        private void ApplyMaleGaugeLockForMasturbationPose()
        {
            if (_hSceneProc == null || _hSceneProc.flags == null)
            {
                return;
            }

            HFlag flags = _hSceneProc.flags;
            if (!IsMasturbationPoseActive(flags))
            {
                ClearMaleGaugeLock();
                return;
            }

            if (!_maleGaugeLockActive)
            {
                _maleGaugeLockValue = Mathf.Clamp(flags.gaugeMale, 0f, 100f);
                _maleGaugeLockActive = true;
                if (_settings != null && _settings.VerboseLog)
                {
                    LogInfoFile($"male gauge lock start value={_maleGaugeLockValue:0.000}");
                }
            }

            flags.gaugeMale = _maleGaugeLockValue;
        }

        private void ForceMasturbationModeContext()
        {
            if (_hSceneProc == null || _hSceneProc.flags == null)
            {
                return;
            }

            HFlag flags = _hSceneProc.flags;
            if (flags.nowAnimationInfo == null || flags.nowAnimationInfo.mode != HFlag.EMode.masturbation)
            {
                return;
            }

            if (flags.mode == HFlag.EMode.masturbation)
            {
                return;
            }

            HFlag.EMode previous = flags.mode;
            flags.mode = HFlag.EMode.masturbation;
            if (_settings != null && _settings.VerboseLog)
            {
                LogInfoFile($"force masturbation mode context prev={previous} now={flags.mode} id={flags.nowAnimationInfo.id}");
            }
        }

        private void ClearMaleGaugeLock()
        {
            _maleGaugeLockActive = false;
            _maleGaugeLockValue = -1f;
        }

        internal bool OnMaleGaugeUpPrefix()
        {
            if (_settings == null || !_settings.LockMaleGaugeDuringMasturbation)
            {
                return true;
            }

            if (_hSceneProc == null || _hSceneProc.flags == null)
            {
                return true;
            }

            if (!IsMasturbationPoseActive(_hSceneProc.flags))
            {
                return true;
            }

            return false;
        }

        private static bool IsMasturbationPoseActive(HFlag flags)
        {
            if (flags == null)
            {
                return false;
            }

            if (flags.mode == HFlag.EMode.masturbation)
            {
                return true;
            }

            if (flags.nowAnimationInfo != null && flags.nowAnimationInfo.mode == HFlag.EMode.masturbation)
            {
                return true;
            }

            return flags.selectAnimationListInfo != null && flags.selectAnimationListInfo.mode == HFlag.EMode.masturbation;
        }

        private static float ResolveSpeedGaugeNormalized(HFlag flags)
        {
            if (flags == null)
            {
                return 0f;
            }

            if (flags.mode == HFlag.EMode.aibu)
            {
                float max = flags.speedMaxAibuBody > 0f ? flags.speedMaxAibuBody : 1.5f;
                return Mathf.Clamp01(Mathf.InverseLerp(0f, max, flags.speed));
            }

            return Mathf.Clamp01(flags.speedCalc);
        }

        private bool LinkMasturbationPlaybackSpeedToGauge(string source)
        {
            if (_settings == null ||
                !_settings.EnableMasturbationGaugeSpeedLink ||
                _hSceneProc == null ||
                _hSceneProc.flags == null)
            {
                return false;
            }

            HFlag flags = _hSceneProc.flags;
            if (!IsMasturbationPoseActive(flags))
            {
                return false;
            }

            float gaugeNormalized = ResolveSpeedGaugeNormalized(flags);
            float min = Mathf.Max(0.05f, _settings.MasturbationAnimatorSpeedMin);
            float max = Mathf.Max(min, _settings.MasturbationAnimatorSpeedMax);
            float playbackSpeed = Mathf.Lerp(min, max, gaugeNormalized);

            ApplyMasturbationPlaybackSpeed(playbackSpeed);
            _masturbationSpeedApplied = true;

            if (_settings.VerboseLog)
            {
                float currentSpeed = _hSceneProc.flags.speed;
                float currentSpeedCalc = _hSceneProc.flags.speedCalc;
                HFlag.EMode currentMode = _hSceneProc.flags.mode;
                bool valueChanged =
                    HasLogValueChanged(gaugeNormalized, _lastLoggedSpeedLinkGauge) ||
                    HasLogValueChanged(playbackSpeed, _lastLoggedSpeedLinkAnimSpeed) ||
                    HasLogValueChanged(currentSpeed, _lastLoggedSpeedLinkSpeed) ||
                    HasLogValueChanged(currentSpeedCalc, _lastLoggedSpeedLinkSpeedCalc) ||
                    currentMode != _lastLoggedSpeedLinkMode ||
                    !string.Equals(source ?? string.Empty, _lastLoggedSpeedLinkSource, StringComparison.Ordinal);

                if (valueChanged && Time.unscaledTime >= _nextSpeedLinkLogTime)
                {
                    _nextSpeedLinkLogTime = Time.unscaledTime + 1.0f;
                    LogInfoFile(
                        $"masturbation speed link src={source} gauge={gaugeNormalized:0.000} animSpeed={playbackSpeed:0.000} " +
                        $"speed={currentSpeed:0.000} speedCalc={currentSpeedCalc:0.000} " +
                        $"mode={currentMode}");

                    _lastLoggedSpeedLinkGauge = gaugeNormalized;
                    _lastLoggedSpeedLinkAnimSpeed = playbackSpeed;
                    _lastLoggedSpeedLinkSpeed = currentSpeed;
                    _lastLoggedSpeedLinkSpeedCalc = currentSpeedCalc;
                    _lastLoggedSpeedLinkMode = currentMode;
                    _lastLoggedSpeedLinkSource = source ?? string.Empty;
                }
            }

            return true;
        }

        private void ResetMasturbationPlaybackSpeed(string reason)
        {
            if (!_masturbationSpeedApplied)
            {
                return;
            }

            ApplyMasturbationPlaybackSpeed(1f);
            _masturbationSpeedApplied = false;

            if (_settings != null && _settings.VerboseLog)
            {
                LogInfoFile("masturbation speed link reset (" + reason + ")");
            }

            _lastLoggedSpeedLinkGauge = float.NaN;
            _lastLoggedSpeedLinkAnimSpeed = float.NaN;
            _lastLoggedSpeedLinkSpeed = float.NaN;
            _lastLoggedSpeedLinkSpeedCalc = float.NaN;
            _lastLoggedSpeedLinkMode = HFlag.EMode.none;
            _lastLoggedSpeedLinkSource = string.Empty;
        }

        private static bool HasLogValueChanged(float current, float previous)
        {
            if (float.IsNaN(previous))
            {
                return true;
            }

            return Mathf.Abs(current - previous) >= 0.01f;
        }

        private void ApplyMasturbationPlaybackSpeed(float playbackSpeed)
        {
            if (_hSceneProc == null)
            {
                return;
            }

            ApplyPlaybackSpeedToList(HSceneLstFemaleField?.GetValue(_hSceneProc), playbackSpeed);
            ApplyPlaybackSpeedToItem(HSceneItemField?.GetValue(_hSceneProc), playbackSpeed);
        }

        private static void ApplyPlaybackSpeedToList(object listObject, float playbackSpeed)
        {
            if (!(listObject is IList<ChaControl> list))
            {
                return;
            }

            float safeSpeed = Mathf.Max(0f, playbackSpeed);
            for (int i = 0; i < list.Count; i++)
            {
                ChaControl cha = list[i];
                if (cha == null)
                {
                    continue;
                }

                Animator animator = cha.animBody;
                if (animator != null)
                {
                    animator.speed = safeSpeed;
                }
            }
        }

        private static void ApplyPlaybackSpeedToItem(object itemObject, float playbackSpeed)
        {
            if (!(itemObject is ItemObject item))
            {
                return;
            }

            item.SetAnimationSpeed(Mathf.Max(0f, playbackSpeed));
        }

        internal void OnMasturbationProcPostfix()
        {
            bool linked = LinkMasturbationPlaybackSpeedToGauge("harmony-postfix");
            if (!linked)
            {
                ResetMasturbationPlaybackSpeed("harmony-postfix");
            }

            if (_settings != null && _settings.KeepSpeedGaugeVisibleInMasturbation)
            {
                KeepSpeedGaugeVisibleForMasturbationPose();
            }
        }

        internal void OnHSpriteUpdatePostfix()
        {
            if (_settings != null && _settings.KeepSpeedGaugeVisibleInMasturbation)
            {
                KeepSpeedGaugeVisibleForMasturbationPose();
            }
        }

        private void HandleMouseWheelSpeedControl()
        {
            if (_hSceneProc == null || _hSceneProc.flags == null || _settings == null)
            {
                return;
            }

            float wheel = Bindings.Axis("MainGameFreeHMasturbationMenu.speed", _settings.WheelRequireCtrl ? "Wheel + Ctrl" : "Wheel");
            if (Mathf.Abs(wheel) < 0.0001f)
            {
                return;
            }

            float step = Mathf.Abs(_settings.WheelSpeedStep);
            if (step <= 0f)
            {
                return;
            }

            HFlag flags = _hSceneProc.flags;
            float delta = wheel > 0f ? step : (0f - step);

            switch (flags.mode)
            {
            case HFlag.EMode.aibu:
            {
                float bodyMax = (flags.speedMaxAibuBody > 0f) ? flags.speedMaxAibuBody : 1.5f;
                float itemMax = (flags.speedMaxAibuItem > 0f) ? flags.speedMaxAibuItem : 1.5f;
                flags.SpeedUpClickAibu(delta, bodyMax, _drag: false);
                float itemDelta = delta;
                if (Mathf.Abs(flags.rateSpeedUpAibu) > 0.0001f)
                {
                    itemDelta = delta * (flags.rateSpeedUpItem / flags.rateSpeedUpAibu);
                }
                flags.SpeedUpClickItemAibu(itemDelta, itemMax, _drag: false);
                break;
            }
            case HFlag.EMode.masturbation:
            {
                flags.speedCalc = Mathf.Clamp01(flags.speedCalc + delta);
                flags.speed = flags.speedCalc;
                break;
            }
            case HFlag.EMode.lesbian:
            {
                float localMax = Mathf.Max(0.1f, flags.speedLesbian);
                flags.speed = Mathf.Clamp(flags.speed + delta, 0f, localMax);
                break;
            }
            default:
                flags.SpeedUpClick(delta, 1f);
                break;
            }

            if (_settings.VerboseLog && Time.unscaledTime >= _nextWheelLogTime)
            {
                _nextWheelLogTime = Time.unscaledTime + 0.4f;
                LogInfoFile(
                    $"wheel speed mode={flags.mode} wheel={wheel:0.000} speed={flags.speed:0.000} " +
                    $"speedCalc={flags.speedCalc:0.000} motion={flags.motion:0.000}");
            }
        }

        private void KeepActionMenuVisibleInMasturbation()
        {
            if (_hSceneProc == null || _hSceneProc.flags == null)
            {
                return;
            }

            if (!_hSceneProc.flags.isFreeH || _hSceneProc.flags.mode != HFlag.EMode.masturbation)
            {
                return;
            }

            GameObject menuActionRoot = GetMenuActionRoot();
            if (menuActionRoot != null && !menuActionRoot.activeSelf)
            {
                menuActionRoot.SetActive(value: true);
            }
        }

        private void KeepSpeedGaugeVisibleForMasturbationPose()
        {
            if (_hSceneProc == null || _hSceneProc.flags == null || _hSprite == null)
            {
                return;
            }

            if (!_hSceneProc.flags.isFreeH || !IsMasturbationPoseActive(_hSceneProc.flags))
            {
                return;
            }

            if (_hSprite.objCommonAibuIcon != null && !_hSprite.objCommonAibuIcon.activeSelf)
            {
                _hSprite.objCommonAibuIcon.SetActive(value: true);
            }
        }

        private void RecoverPendingSelectionFromMasturbation()
        {
            if (_hSceneProc == null || _hSceneProc.flags == null)
            {
                return;
            }

            HFlag flags = _hSceneProc.flags;
            if (flags.selectAnimationListInfo == null)
            {
                return;
            }

            if (flags.mode != HFlag.EMode.masturbation)
            {
                return;
            }

            if (flags.selectAnimationListInfo.mode == HFlag.EMode.masturbation)
            {
                return;
            }

            bool recovered = false;
            if (flags.voiceWait)
            {
                flags.voiceWait = false;
                recovered = true;
            }

            if (flags.click == HFlag.ClickKind.none)
            {
                flags.click = HFlag.ClickKind.actionChange;
                recovered = true;
            }

            if (recovered && _settings.VerboseLog && Time.unscaledTime >= _nextRecoverLogTime)
            {
                _nextRecoverLogTime = Time.unscaledTime + 1f;
                LogInfoFile(
                    $"recovered pending selection in mode={flags.mode} " +
                    $"targetMode={flags.selectAnimationListInfo.mode} targetId={flags.selectAnimationListInfo.id}");
            }
        }

        private GameObject GetMenuActionRoot()
        {
            if (_hSprite == null || HSpriteMenuActionField == null)
            {
                return null;
            }

            object menuAction = HSpriteMenuActionField.GetValue(_hSprite);
            if (menuAction == null)
            {
                return null;
            }

            Component component = menuAction as Component;
            if (component != null)
            {
                return component.gameObject;
            }

            PropertyInfo prop = menuAction.GetType().GetProperty("gameObject", InstanceAny);
            if (prop != null)
            {
                return prop.GetValue(menuAction, null) as GameObject;
            }

            return null;
        }

        private GameObject GetMenuActionObject(int index)
        {
            if (_hSprite == null || HSpriteMenuActionField == null)
            {
                return null;
            }

            object menuAction = HSpriteMenuActionField.GetValue(_hSprite);
            if (menuAction == null)
            {
                return null;
            }

            MethodInfo getObjectMethod = menuAction.GetType().GetMethod("GetObject", InstanceAny, null, new[] { typeof(int) }, null);
            if (getObjectMethod == null)
            {
                return null;
            }

            return getObjectMethod.Invoke(menuAction, new object[] { index }) as GameObject;
        }

        private void UpdateCustomButtonText()
        {
            string buttonText = _settings != null ? _settings.ButtonText : "オナニー";
            if (_customButtonTmpText != null && _customButtonTmpText.text != buttonText)
            {
                _customButtonTmpText.text = buttonText;
            }

            if (_customButtonUiText != null && _customButtonUiText.text != buttonText)
            {
                _customButtonUiText.text = buttonText;
            }
        }

        private void DestroyCustomButton()
        {
            _cachedTemplateButton = null;
            _cachedAnchorButton = null;
            _nextMenuButtonLookupAt = 0f;
            if (_customButton != null)
            {
                Destroy(_customButton);
                _customButton = null;
            }
            if (_posePanel != null)
            {
                Destroy(_posePanel);
                _posePanel = null;
            }

            _customButtonTmpText = null;
            _customButtonUiText = null;
            _customButtonLayoutApplied = false;
            _posePanelRect = null;
            _posePanelContentRect = null;
            _posePanelButtons.Clear();
            _posePanelInfos.Clear();
        }

        private void LogInfoFile(string message)
        {
            if (SuppressRuntimeLogs) return;
            Logger.LogInfo(message);
            Append("[INFO] " + message);
        }

        private void LogWarnFile(string message)
        {
            if (SuppressRuntimeLogs) return;
            Logger.LogWarning(message);
            Append("[WARN] " + message);
        }

        private void LogErrorFile(string message)
        {
            if (SuppressRuntimeLogs) return;
            Logger.LogError(message);
            Append("[ERROR] " + message);
        }

        private void Append(string message)
        {
            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}";
                lock (_logLock)
                {
                    File.AppendAllText(_logPath, line, Utf8NoBom);
                }
            }
            catch
            {
                // logging must never throw
            }
        }
    }

    [HarmonyPatch(typeof(HSprite), "Update")]
    internal static class HSpriteUpdatePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                return;
            }

            plugin.OnHSpriteUpdatePostfix();
        }
    }

    [HarmonyPatch(typeof(HFlag), "MaleGaugeUp")]
    internal static class HFlagMaleGaugeUpPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static bool Prefix()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return true;
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                return true;
            }

            return plugin.OnMaleGaugeUpPrefix();
        }
    }

    [HarmonyPatch(typeof(HMasturbation), "Proc")]
    internal static class HMasturbationProcPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            var plugin = Plugin.Instance;
            if (plugin == null)
            {
                return;
            }

            plugin.OnMasturbationProcPostfix();
        }
    }
}
