using System.Text.RegularExpressions;
using MainGirlHipHijack.Services;

namespace MainGirlHipHijack.Characterization;

internal static class Program
{
    private static readonly List<string> Failures = new();

    private static int Main()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));

        AssertPluginIdentity(root);
        AssertExternalContract(root);
        AssertUpdateOrder(root);
        AssertLateUpdateOrder(root);
        AssertHarmonyContract(root);
        AssertSettingsContract(root);
        AssertStorageContract(root);
        AssertSettingsOwnership(root);
        AssertSteamVrOwnership(root);
        AssertComposerGatewayOwnership(root);
        AssertSessionOwnership(root);
        AssertBodyIkStateOwnership(root);
        AssertUiStateOwnership(root);
        AssertRuntimeFeatureStateOwnership(root);
        AssertLoggerOwnership(root);
        AssertReflectionOwnership(root);
        AssertUnifiedUiBoundary(root);
        AssertInteractionStateOwnership(root);
        AssertFemaleHeadStateOwnership(root);
        AssertFemaleHandFkStateOwnership(root);
        AssertMaleSessionOwnership(root);
        AssertPosePresetStateOwnership(root);
        AssertShoulderGatewayOwnership(root);
        AssertBodyIkLifecycleOwnership(root);
        AssertHipSpeedController(root);
        AssertFemaleAnimationSpeedBoundary(root);
        AssertPureServices();

        if (Failures.Count == 0)
        {
            Console.WriteLine("MainGirlHipHijack characterization: PASS");
            return 0;
        }

        Console.Error.WriteLine("MainGirlHipHijack characterization: FAIL");
        foreach (string failure in Failures)
            Console.Error.WriteLine("- " + failure);
        return 1;
    }

    private static void AssertPosePresetStateOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "FemalePosePresetState<PosePresetRuntime, PoseTransitionPoint> _femalePosePresetState",
            "female pose preset state owner");
        Contains(plugin, "MalePosePresetState<MalePosePresetItem> _malePosePresetState",
            "male pose preset state owner");
        Contains(plugin, "PoseAutomationState _poseAutomationState", "pose automation state owner");

        if (Regex.IsMatch(plugin, @"private readonly List<PosePresetRuntime>\s+_posePresets\s*="))
            Failures.Add("female pose preset list must not be stored directly on Plugin");
        if (Regex.IsMatch(plugin, @"private readonly List<MalePosePresetItem>\s+_malePosePresets\s*="))
            Failures.Add("male pose preset list must not be stored directly on Plugin");
        if (Regex.IsMatch(plugin, @"private bool\s+_autoPosePendingApply\s*;"))
            Failures.Add("pose automation pending state must not be stored directly on Plugin");
    }

    private static void AssertShoulderGatewayOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "ShoulderStabilizerGateway _shoulderStabilizerGateway",
            "shoulder stabilizer gateway owner");
        Contains(plugin, "_shoulderStabilizerGateway.SetEnabled(", "shoulder control delegation");
        Contains(plugin, "_shoulderStabilizerGateway.BuildDiagnosticStateText()",
            "shoulder diagnostic delegation");
        if (plugin.Contains("private MethodInfo _shoulderSetEnabledFromHipHijackMethod", StringComparison.Ordinal))
            Failures.Add("shoulder reflection method cache must not remain on Plugin");

        string gateway = Read(root, Path.Combine("Adapters", "ShoulderStabilizerGateway.cs"));
        Contains(gateway, "SetEnabledFromHipHijack", "shoulder compatibility API name");
        Contains(gateway, "BindingFlags.Public | BindingFlags.Static", "shoulder compatibility API binding");
    }

    private static void AssertBodyIkLifecycleOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "BodyIkLifecycleState _bodyIkLifecycle", "BodyIK lifecycle state owner");
        Contains(plugin, "BodyIkDiagnosticState _bodyIkDiagnosticState", "BodyIK diagnostic state owner");
        if (Regex.IsMatch(plugin, @"private readonly float\[\]\s+_nextOffLeakWarnTime\s*="))
            Failures.Add("BodyIK leak warning timers must not remain directly on Plugin");
        if (Regex.IsMatch(plugin, @"private bool\s+_abandonedByPostureChange\s*;"))
            Failures.Add("BodyIK posture lifecycle state must not remain directly on Plugin");

        string bodyIk = Read(root, "Plugin.BodyIK.cs");
        Contains(bodyIk, "_bodyIkDiagnosticState.Reset();", "BodyIK diagnostic reset delegation");
    }

    private static void AssertHipSpeedController(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "HipSpeedController _hipSpeedController", "hip speed controller owner");

        string bridge = Read(root, "Plugin.cs");
        string tick = ExtractMethod(bridge, "private void TickSpeedHijack()");
        Contains(tick, "_hipSpeedController.Tick(", "hip speed delegation");
        if (tick.Contains("SpeedUpClick", StringComparison.Ordinal))
            Failures.Add("hip speed mutation must not remain in Plugin TickSpeedHijack");

        string controller = Read(root, Path.Combine("Services", "HipSpeedController.cs"));
        InOrder(controller, "speed movement update order",
            "TryQueueSpeedBootstrap(",
            "TryQueueIdleInsert(",
            "ApplySpeedByMotion(");
        Contains(controller, "flags.SpeedUpClickAibu", "aibu speed compatibility path");
        Contains(controller, "flags.SpeedUpClick(deltaPerFrame, 1f)", "insertion speed compatibility path");
    }

    private static void AssertFemaleAnimationSpeedBoundary(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "FemaleAnimationSpeedInterceptor _femaleAnimationSpeedInterceptor",
            "female animation speed interceptor owner");

        string bridge = Read(root, "Plugin.cs");
        Equal(2, Regex.Matches(bridge, "_femaleAnimationSpeedInterceptor\\.TryApply\\(").Count,
            "female animation speed delegation overload count");
        if (bridge.Contains("FiHActionFemale", StringComparison.Ordinal))
            Failures.Add("female animation reflection access must not remain in HScene partial");

        string interceptor = Read(root, Path.Combine("Adapters", "FemaleAnimationSpeedInterceptor.cs"));
        Contains(interceptor, "reflection.HActionFemale.GetValue(action)", "female reflection boundary");
        Contains(interceptor, "female.setAnimatorParamFloat(parameter, 0f)", "female direct compatibility path");
        Contains(interceptor, "item?.SetAnimatorParamFloat(parameterHash, value)", "item speed compatibility path");

        string retired = Read(root, "Plugin.HSceneHijack.cs");
        if (retired.Contains("partial class Plugin", StringComparison.Ordinal))
            Failures.Add("HScene hijack partial must remain retired after service migration");

        string animatorSync = Read(root, Path.Combine("Services", "AnimatorSynchronizationController.cs"));
        Contains(animatorSync, "state.IsName(\"SLoop\")", "strong loop synchronization contract");
        Contains(animatorSync, "state.IsName(\"A_WLoop\")", "weak loop synchronization contract");
        Contains(animatorSync, "animator.Play(state.fullPathHash, 0, 0f)", "animator reset contract");
    }

    private static void AssertPluginIdentity(string root)
    {
        string source = Read(root, "Plugin.cs");
        Contains(source, "public const string Guid = \"com.kks.main.girlbodyikgizmo\"", "plugin GUID");
        Contains(source, "public const string PluginName = \"MainGirlHipHijack\"", "plugin name");
        Contains(source, "public const string Version = \"1.0.0\"", "plugin version");
        Contains(source, "[BepInProcess(\"KoikatsuSunshine\")]", "desktop process target");
        Contains(source, "[BepInProcess(\"KoikatsuSunshine_VR\")]", "VR process target");
        Contains(source, "internal const int BIK_TOTAL = 13", "13 body IK operation points");
    }

    private static void AssertExternalContract(string root)
    {
        string source = Read(root, "Plugin.PublicApi.cs");
        string[] methods =
        {
            "GetEffectorKeys", "TryGetBodyIkTrackingPosition", "TryGetFemaleHeadPosition",
            "TrySetEffectorFollowComposerObject", "TryClearEffectorFollowComposerObject",
            "TryGetEffectorFollowComposerObject", "TrySetEffectorEnabled", "TrySetEffectorWeight",
            "TrySetSpeedHijackEnabled", "TrySetCutFemaleAnimSpeedEnabled", "TryDisableBodyCtrlLink",
            "IsBodyCtrlLinkEnabled", "IsSpeedHijackEnabled", "IsCutFemaleAnimSpeedEnabled",
            "TryEnableBodyCtrlLink"
        };

        foreach (string method in methods)
            Contains(source, method + "(", "public API " + method);

        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "public static event Action<int, bool> ArmIkRunningChanged", "arm IK event");
        Contains(plugin, "Chainloader.PluginInfos.TryGetValue", "instance resolution through Chainloader");
        Contains(plugin, "private event Action<int, bool> _armIkRunningChanged", "instance arm IK event storage");
        if (plugin.Contains("Instance { get; private set; }", StringComparison.Ordinal))
            Failures.Add("Plugin.Instance must not retain a static mutable backing field");
    }

    private static void AssertUpdateOrder(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        string entry = ExtractMethod(plugin, "private void Update()");
        Contains(entry, "_frameCoordinator?.UpdateFrame();", "thin Unity Update entry");

        string coordinator = ExtractMethod(
            Read(root, Path.Combine("Services", "FrameCoordinator.cs")),
            "internal void UpdateFrame()");
        InOrder(coordinator, "frame phase order",
            "_port.RegisterUnifiedUi();",
            "_port.FlushSettings();",
            "_port.IsEnabled()",
            "_port.RunFramePreamble();",
            "_port.TryPrepareResolvedFrame()",
            "_port.RunResolvedFrame();");

        string preamble = ExtractMethod(plugin, "private void RunFramePreamble()");
        InOrder(preamble, "frame preamble order",
            "UpdateUiDraggingStateByMouseRelease();",
            "UpdateInputCaptureApiState();",
            "_steamVrPoseProvider.TickDeviceScan(Time.unscaledTime);",
            "_steamVrPoseProvider.FetchPoses();",
            "UpdateFemaleHeadVRInput();",
            "UpdateFemaleHeadAngleGizmo();");

        string prepare = ExtractMethod(plugin, "private bool TryPrepareResolvedFrame()");
        InOrder(prepare, "resolved frame preparation order",
            "TryResolveRuntimeRefs()",
            "TryFlushPendingAbandonByPostureChange");

        string resolved = ExtractMethod(plugin, "private void RunResolvedFrame()");
        InOrder(resolved, "resolved frame order",
            "ProcessAutoPoseRuntime();",
            "ExecuteHipQuickSetup();",
            "HandleVRGrab();",
            "DoEnableBodyIK(i)");
    }

    private static void AssertLateUpdateOrder(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        string entry = ExtractMethod(plugin, "internal void OnAfterHSceneLateUpdate(HSceneProc proc)");
        Contains(entry, "_frameCoordinator?.AfterHSceneLateUpdate(proc);", "thin HScene LateUpdate entry");

        string method = ExtractMethod(
            Read(root, Path.Combine("Services", "FrameCoordinator.cs")),
            "internal void AfterHSceneLateUpdate(HSceneProc process)");
        InOrder(method, "HScene LateUpdate postfix order",
            "_port.ApplyFemaleBodyIkAfterAnimation();",
            "_port.UpdateBodyControlLinkAfterAnimation();",
            "_port.ApplyVibeReactionAfterAnimation();",
            "_port.UpdateHipSpeedAfterAnimation();",
            "_port.UpdateMaleBodyAfterAnimation();",
            "_port.ApplyFemaleHeadAfterAnimation();");
    }

    private static void AssertHarmonyContract(string root)
    {
        string source = Read(root, "Plugin.Patches.cs");
        Contains(source, "[HarmonyPatch(typeof(HSceneProc), \"LateUpdate\")]", "LateUpdate patch");
        Contains(source, "[HarmonyPatch(typeof(HSceneProc), \"ChangeAnimator\")]", "ChangeAnimator patch");
        Equal(2, Regex.Matches(source, "HarmonyPatch\\(typeof\\(HActionBase\\), \\\"SetAnimatorFloat\\\"").Count,
            "SetAnimatorFloat patch overload count");
    }

    private static void AssertSettingsContract(string root)
    {
        string source = Read(root, "BodyIkGizmoSettings.cs");
        string[] fields =
        {
            "UiVisible", "AutoEnableAllOnResolve", "DetailLogEnabled", "BodyIkDiagnosticLog",
            "AutoPoseEnabled", "PoseTransitionSeconds", "FollowSnapDistance", "VRGrabDistance",
            "FemaleHeadAngleEnabled", "SpeedHijackEnabled", "CutFemaleAnimSpeedEnabled",
            "AutoInsertOnMoveEnabled", "MaleHmdEnabled", "Enabled", "GizmoVisible", "Weights",
            "FemaleLeftHandPoseEnabled", "FemaleRightHandPoseEnabled", "FollowComposerEnabled",
            "FollowComposerObjectIds"
        };

        foreach (string field in fields)
            RegexContains(source, $@"\b{Regex.Escape(field)}\s*(=|;)", "settings field " + field);

        string settingsStore = Read(root, "SettingsStore.cs");
        Contains(settingsStore, "internal const string FileName = \"FullIkGizmoSettings.json\"", "settings file name");
        InOrder(settingsStore, "atomic settings save", "File.WriteAllText(tempPath", "File.Replace(tempPath");
    }

    private static void AssertStorageContract(string root)
    {
        string female = Read(root, "Plugin.PosePresets.Models.cs");
        Contains(female, "Path.Combine(_pluginDir, \"pose_presets\")", "female pose directory");
        Contains(female, "Path.Combine(_posePresetRootDir, \"index.json\")", "female pose index");

        string male = Read(root, "Plugin.MalePosePresets.cs");
        Contains(male, "Path.Combine(_pluginDir, \"pose_presets_male\")", "male pose directory");
        Contains(male, "Path.Combine(_malePosePresetRootDir, \"index.json\")", "male pose index");
    }

    private static void AssertSettingsOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "private SettingsCoordinator _settingsCoordinator", "settings coordinator ownership");
        Contains(plugin, "_settingsCoordinator?.RequestSave(immediate)", "settings save delegation");
        Contains(plugin, "_settingsCoordinator.FlushIfDue()", "settings flush delegation");
        if (plugin.Contains("private bool _settingsSavePending", StringComparison.Ordinal))
            Failures.Add("settings pending state must not remain on Plugin");
        if (plugin.Contains("private float _settingsSaveDueTime", StringComparison.Ordinal))
            Failures.Add("settings due time must not remain on Plugin");
    }

    private static void AssertSteamVrOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "private readonly SteamVrPoseProvider _steamVrPoseProvider", "SteamVR provider ownership");
        Contains(plugin, "_steamVrPoseProvider.FetchPoses()", "SteamVR fetch delegation");

        string bridge = Read(root, "Plugin.VRDevicePose.cs");
        if (bridge.Contains("partial class Plugin", StringComparison.Ordinal))
            Failures.Add("VR device partial must remain retired after service migration");

        string provider = Read(root, Path.Combine("Services", "SteamVrPoseProvider.cs"));
        Contains(provider, "private readonly TrackedDevicePose_t[] _renderPoses", "instance SteamVR render pose buffer");
        Contains(provider, "private readonly Dictionary<string, uint> _serialToIndex", "instance SteamVR serial cache");
    }

    private static void AssertComposerGatewayOwnership(string root)
    {
        string gateway = Read(root, "ObjectComposerBridge.cs");
        Contains(gateway, "internal sealed class ObjectComposerGateway", "instance ObjectComposer gateway");
        if (gateway.Contains("private static bool _lookupDone", StringComparison.Ordinal))
            Failures.Add("ObjectComposer lookup state must not remain static");

        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "private readonly ObjectComposerGateway _objectComposerGateway", "ObjectComposer gateway owner");
        Contains(plugin, "private readonly ComposerFollowController _composerFollowController", "Composer follow controller owner");

        string retired = Read(root, "Plugin.ObjectComposerFollow.cs");
        if (retired.Contains("partial class Plugin", StringComparison.Ordinal))
            Failures.Add("ObjectComposer follow partial must remain retired after controller migration");

        string bodyIkFollow = Read(root, "Plugin.BodyIKFollow.cs");
        Contains(bodyIkFollow, "_composerFollowController.UpdateTargets(", "Composer follow update delegation");
    }

    private static void AssertSessionOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "private readonly HSceneSessionTracker _hSceneSessionTracker", "HScene tracker owner");
        Contains(plugin, "private HSceneSessionState _hSceneSession => _hSceneSessionTracker.Session", "HScene session view");
        Contains(plugin, "private FemaleCharacterSession _femaleSession => _hSceneSessionTracker.Female", "female session view");
        if (plugin.Contains("RuntimeState _runtime", StringComparison.Ordinal))
            Failures.Add("universal RuntimeState must not remain on Plugin");

        string hScene = Read(root, Path.Combine("Runtime", "HSceneSessionState.cs"));
        Contains(hScene, "internal HSceneProc Process", "HScene process state");
        string female = Read(root, Path.Combine("Runtime", "FemaleCharacterSession.cs"));
        Contains(female, "internal FullBodyBipedIK FullBodyIk", "female FinalIK state");
        string tracker = Read(root, Path.Combine("Services", "HSceneSessionTracker.cs"));
        Contains(tracker, "internal HSceneSessionState Session", "HScene tracker session ownership");
        Contains(tracker, "internal FemaleCharacterSession Female", "HScene tracker female ownership");
        Contains(tracker, "internal void Clear(string unknownMotionStrength)", "HScene tracker cleanup boundary");
        string runtime = Read(root, "Plugin.Runtime.cs");
        Contains(runtime, "_hSceneSessionTracker.Clear(PoseMotionStrengthUnknown)", "HScene cleanup delegation");
    }

    private static void AssertBodyIkStateOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "private readonly BodyIkStateStore _bodyIkState", "BodyIK state store");
        Contains(plugin, "_bodyIkState.OperationPoints", "BodyIK operation point delegation");
        if (plugin.Contains("new bool[BIK_TOTAL]", StringComparison.Ordinal))
            Failures.Add("BodyIK wanted state must not remain allocated on Plugin");

        string store = Read(root, Path.Combine("Services", "BodyIkStateStore.cs"));
        Contains(store, "internal BodyIkOperationPointState[] OperationPoints", "operation point state ownership");
        Contains(store, "internal bool[] Wanted", "BodyIK wanted state ownership");
        Contains(store, "internal float[] Weights", "BodyIK weight state ownership");
    }

    private static void AssertUiStateOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "private readonly HipHijackUiState _uiState", "UI state owner");
        Contains(plugin, "get => _uiState.WindowRect", "window state delegation");
        Contains(plugin, "private readonly InputCaptureState _inputCaptureState", "input capture state owner");
        Contains(plugin, "get => _inputCaptureState.GizmoDragging", "input drag state delegation");

        string state = Read(root, Path.Combine("Runtime", "HipHijackUiState.cs"));
        Contains(state, "internal Rect WindowRect", "UI window state");
        Contains(state, "internal string FemalePosePresetNameDraft", "UI pose draft state");
    }

    private static void AssertRuntimeFeatureStateOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "private readonly HipSpeedRuntimeState _hipSpeedState", "hip speed state owner");
        Contains(plugin, "private readonly BodyControlLinkState _bodyControlState", "body control state owner");
        Contains(plugin, "private readonly FollowHmdRuntimeState _followHmdState", "HMD follow state owner");
    }

    private static void AssertLoggerOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "private HipHijackLogger _hipHijackLogger", "logger service owner");
        Contains(plugin, "_hipHijackLogger?.DiagnosticInfo(message)", "diagnostic log delegation");
        if (plugin.Contains("private readonly SimpleFileLogger _fileLogger", StringComparison.Ordinal))
            Failures.Add("file logger state must not remain on Plugin");

        string logger = Read(root, Path.Combine("Services", "HipHijackLogger.cs"));
        Contains(logger, "private readonly SimpleFileLogger _file", "file logger service ownership");
        Contains(logger, "internal void ApplyRelayState", "relay state boundary");
    }

    private static void AssertReflectionOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "private readonly GameReflectionMap _gameReflection", "game reflection map owner");
        string retired = Read(root, "Plugin.State.cs");
        if (retired.Contains("partial class Plugin", StringComparison.Ordinal))
            Failures.Add("Plugin.State partial must remain retired after state migration");
        if (plugin.Contains("private static readonly FieldInfo FiHScene", StringComparison.Ordinal))
            Failures.Add("game reflection fields must not remain static on Plugin");

        string map = Read(root, Path.Combine("Adapters", "GameReflectionMap.cs"));
        Contains(map, "internal sealed class GameReflectionMap", "game reflection adapter");
        Contains(map, "FindField(typeof(HSceneProc), \"lstFemale\")", "female list reflection contract");
        Contains(map, "FindField(typeof(HFlag), \"nowAnimationInfo\")", "current animation reflection contract");
    }

    private static void AssertUnifiedUiBoundary(string root)
    {
        string gateway = Read(root, Path.Combine("Services", "UnifiedUiGateway.cs"));
        Contains(gateway, "private const string TabId = \"hiphijack\"", "UnifiedUI tab id");
        Contains(gateway, "private const string TabLabel = \"HipHijack\"", "UnifiedUI tab label");
        Contains(gateway, "private const int TabOrder = 20", "UnifiedUI tab order");

        string retired = Read(root, "Plugin.UnifiedUiBridge.cs");
        if (retired.Contains("partial class Plugin", StringComparison.Ordinal))
            Failures.Add("UnifiedUI partial must remain retired after gateway migration");
    }

    private static void AssertInteractionStateOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "private readonly VrInteractionState _vrInteractionState", "VR interaction state owner");
        Contains(plugin, "private readonly HipQuickSetupState _hipQuickSetupState", "HipQuick state owner");

        string vr = Read(root, Path.Combine("Runtime", "VrInteractionState.cs"));
        Contains(vr, "internal Controller.Lock LeftGrabLock", "left VR lock ownership");
        Contains(vr, "internal bool BodyControlLinkEnabled", "body control link ownership");
    }

    private static void AssertFemaleHeadStateOwnership(string root)
    {
        string bridge = Read(root, "Plugin.FemaleHeadVRGrab.cs");
        Contains(bridge, "private readonly FemaleHeadRuntimeState _femaleHeadState", "female head runtime state owner");
        Contains(bridge, "get => _femaleHeadState.AdditiveOffset", "female head additive state delegation");

        string state = Read(root, Path.Combine("Runtime", "FemaleHeadRuntimeState.cs"));
        Contains(state, "internal TransformGizmo Gizmo", "female head gizmo ownership");
        Contains(state, "internal float FadeTargetWeight", "female head fade ownership");
    }

    private static void AssertFemaleHandFkStateOwnership(string root)
    {
        string bridge = Read(root, "Plugin.FemaleHandFk.cs");
        Contains(bridge, "private readonly FemaleHandFkStateStore _femaleHandFkState", "female hand FK state owner");
        if (bridge.Contains("private sealed class FemaleHandFkRuntime", StringComparison.Ordinal))
            Failures.Add("female hand FK runtime must not remain nested in Plugin");

        string store = Read(root, Path.Combine("Services", "FemaleHandFkStateStore.cs"));
        Contains(store, "internal FemaleHandFkRuntimeState[] Hands", "female hand FK hand state ownership");
    }

    private static void AssertMaleSessionOwnership(string root)
    {
        string plugin = Read(root, "Plugin.cs");
        Contains(plugin, "private readonly MaleCharacterSession _maleSession", "male session owner");
        if (Read(root, "Plugin.State.cs").Contains("RuntimeState", StringComparison.Ordinal))
            Failures.Add("male session must not be inherited through universal RuntimeState");

        string male = Read(root, Path.Combine("Runtime", "MaleCharacterSession.cs"));
        Contains(male, "internal FullBodyBipedIK MaleFbbik", "male FinalIK ownership");
        Contains(male, "internal TransformGizmo MaleHeadTargetGizmo", "male head gizmo ownership");
        Contains(male, "internal void ResetReferences()", "male reference reset ownership");
        string maleBridge = Read(root, "Plugin.MaleHMD.cs");
        Contains(maleBridge, "_maleSession.ResetReferences();", "male reference reset delegation");
    }

    private static void AssertPureServices()
    {
        EqualFloat(0.25f, EasingFunctions.EvaluateHeadOffset(0.5f, 1), "head ease-in");
        EqualFloat(0.75f, EasingFunctions.EvaluateHeadOffset(0.5f, 2), "head ease-out");
        EqualFloat(0.5f, EasingFunctions.EvaluatePoseTransition(0.5f, 1), "pose smooth-step midpoint");
        EqualFloat(0.875f, EasingFunctions.EvaluatePoseTransition(0.5f, 2), "pose ease-out midpoint");

        string strong = MotionStrengthClassifier.Classify(name => name == "SLoop");
        string weak = MotionStrengthClassifier.Classify(name => name == "A_WLoop");
        string unknown = MotionStrengthClassifier.Classify(_ => false);
        Equal(MotionStrengthClassifier.Strong, strong, "strong motion classification");
        Equal(MotionStrengthClassifier.Weak, weak, "weak motion classification");
        Equal(MotionStrengthClassifier.Unknown, unknown, "unknown motion classification");
        Equal(true, MotionStrengthClassifier.IsStrongWeakSwitch(strong, weak), "strong/weak switch");
        Equal(false, MotionStrengthClassifier.IsStrongWeakSwitch(unknown, weak), "unknown is not a switch");
    }

    private static string Read(string root, string relativePath)
    {
        string path = Path.Combine(root, relativePath);
        if (!File.Exists(path))
        {
            Failures.Add("missing source file: " + relativePath);
            return string.Empty;
        }
        return File.ReadAllText(path);
    }

    private static string ExtractMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
        {
            Failures.Add("missing method signature: " + signature);
            return string.Empty;
        }

        int open = source.IndexOf('{', start);
        if (open < 0)
        {
            Failures.Add("missing method body: " + signature);
            return string.Empty;
        }

        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[start..(i + 1)];
        }

        Failures.Add("unterminated method body: " + signature);
        return source[start..];
    }

    private static void InOrder(string source, string label, params string[] tokens)
    {
        int cursor = 0;
        foreach (string token in tokens)
        {
            int found = source.IndexOf(token, cursor, StringComparison.Ordinal);
            if (found < 0)
            {
                Failures.Add($"{label}: missing or out of order: {token}");
                return;
            }
            cursor = found + token.Length;
        }
    }

    private static void Contains(string source, string token, string label)
    {
        if (!source.Contains(token, StringComparison.Ordinal))
            Failures.Add(label + ": missing " + token);
    }

    private static void RegexContains(string source, string pattern, string label)
    {
        if (!Regex.IsMatch(source, pattern))
            Failures.Add(label + ": missing pattern " + pattern);
    }

    private static void Equal(int expected, int actual, string label)
    {
        if (expected != actual)
            Failures.Add($"{label}: expected {expected}, actual {actual}");
    }

    private static void Equal(string expected, string actual, string label)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            Failures.Add($"{label}: expected {expected}, actual {actual}");
    }

    private static void Equal(bool expected, bool actual, string label)
    {
        if (expected != actual)
            Failures.Add($"{label}: expected {expected}, actual {actual}");
    }

    private static void EqualFloat(float expected, float actual, string label)
    {
        if (Math.Abs(expected - actual) > 0.0001f)
            Failures.Add($"{label}: expected {expected}, actual {actual}");
    }
}
