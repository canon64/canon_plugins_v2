using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using MainGameLogRelay;
using RootMotion.FinalIK;
using UnityEngine;

namespace MainGirlShoulderIkStabilizer;

[BepInPlugin("com.kks.main.girlshoulderikstabilizer", "MainGirlShoulderIkStabilizer", "1.0.0")]
[BepInProcess("KoikatsuSunshine")]
[BepInProcess("KoikatsuSunshine_VR")]
[BepInDependency(MainGameLogRelay.Plugin.Guid, BepInDependency.DependencyFlags.HardDependency)]
[BepInDependency("com.kks.main.girlbodyikgizmo", BepInDependency.DependencyFlags.HardDependency)]
public sealed class ShoulderIkStabilizerPlugin : BaseUnityPlugin
{
	public const string Guid = "com.kks.main.girlshoulderikstabilizer";

	public const string PluginName = "MainGirlShoulderIkStabilizer";

	public const string Version = "1.0.0";

	private const string RelayOwner = Guid;

	private const string RelayLogKey = "main/" + PluginName;

	private const string HipHijackTypeName = "MainGirlHipHijack.Plugin";

	private const string HipHijackAssemblyName = "MainGirlHipHijack";

	private const int HipHijackLeftHandIndex = 0;

	private const int HipHijackRightHandIndex = 1;

	private static readonly FieldInfo FiHSceneLstFemale = AccessTools.Field(typeof(HSceneProc), "lstFemale");

	private Harmony _harmony;

	private PluginSettings _settings;

	private string _pluginDir;

	private string _lastResolveMissing;

	private float _nextResolveMissingLogTime;

	private DateTime _settingsFileLastWrite;

	private float _nextSettingsPollTime;

	private ChaControl _targetFemale;

	private Animator _animBody;

	private FullBodyBipedIK _fbbik;

	private ShoulderRotator _rotator;

	private ConfigEntry<bool> _cfgEnabled;

	private ConfigEntry<bool> _cfgVerboseLog;

	private ConfigEntry<bool> _cfgRelayLogEnabled;

	private ConfigEntry<bool> _cfgShoulderDiagnosticLog;

	private ConfigEntry<float> _cfgShoulderDiagnosticLogInterval;

	private ConfigEntry<bool> _cfgShoulderRotationEnabled;

	private ConfigEntry<bool> _cfgIndependentShoulders;

	private ConfigEntry<bool> _cfgReverseShoulderL;

	private ConfigEntry<bool> _cfgReverseShoulderR;

	private ConfigEntry<float> _cfgShoulderWeight;

	private ConfigEntry<float> _cfgShoulderOffset;

	private ConfigEntry<float> _cfgShoulderRightWeight;

	private ConfigEntry<float> _cfgShoulderRightOffset;

	private ConfigEntry<float> _cfgLoweredArmScale;

	private bool _leftArmIkRunning;

	private bool _rightArmIkRunning;

	private bool _hipHijackEnabledGate = true;

	private bool _hipHijackEventBound;

	private Type _hipHijackPluginType;

	private EventInfo _hipHijackArmIkChangedEvent;

	private MethodInfo _hipHijackIsArmIkRunningMethod;

	private Delegate _hipHijackArmIkChangedHandler;

	internal static ShoulderIkStabilizerPlugin Instance { get; private set; }

	// HipHijack からの有効/無効制御入口。
	public static bool SetEnabledFromHipHijack(bool enabled, string reason = null)
	{
		ShoulderIkStabilizerPlugin instance = Instance;
		if (instance == null)
		{
			return false;
		}

		bool changed = instance._hipHijackEnabledGate != enabled;
		instance._hipHijackEnabledGate = enabled;
		if (changed || (instance._settings != null && instance._settings.VerboseLog))
		{
			instance.LogInfo("HipHijack連携: 肩補正ゲート=" + (enabled ? "ON" : "OFF") + " reason=" + (reason ?? "unknown"));
		}

		if (!enabled)
		{
			instance.DisableCurrentRotator("HipHijack連携で無効");
		}

		return true;
	}

	private void Awake()
	{
		Instance = this;
		_pluginDir = Path.GetDirectoryName(base.Info.Location) ?? string.Empty;
		PreconfigureRelayLogRoutingEarly();
		_settings = SettingsStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
		try { _settingsFileLastWrite = File.GetLastWriteTimeUtc(Path.Combine(_pluginDir, SettingsStore.FileName)); } catch { }
		BindConfigEntries();
		ApplyRelayLoggingState();
		ApplyConfigOverrides(logChanges: false);
		TryBindHipHijackArmIkEvents(forceLog: true);
		_harmony = new Harmony("com.kks.main.girlshoulderikstabilizer");
		_harmony.PatchAll(typeof(ShoulderIkStabilizerPatches));
		LogInfo("起動完了");
		LogInfo("設定ファイル=" + Path.Combine(_pluginDir, "ShoulderIkStabilizerSettings.json"));
		LogInfo("有効状態: プラグイン=" + _settings.Enabled + " 肩補正=" + _settings.ShoulderRotationEnabled);
	}

	private void PreconfigureRelayLogRoutingEarly()
	{
		if (!LogRelayApi.IsAvailable)
		{
			return;
		}

		LogRelayApi.SetOwnerLogKey(RelayOwner, RelayLogKey);
	}

	private void OnDestroy()
	{
		try
		{
			DestroyAllRotators();
		}
		catch
		{
		}
		if (_harmony != null)
		{
			_harmony.UnpatchSelf();
			_harmony = null;
		}
		UnbindHipHijackArmIkEvents();
		UnbindConfigEntries();
		LogInfo("終了処理完了");
		Instance = null;
	}

	internal void OnAfterHSceneLateUpdate(HSceneProc proc)
	{
		PollSettingsFileReload();
		bool shoulderEnabled = _settings.Enabled && _settings.ShoulderRotationEnabled && _hipHijackEnabledGate;
		if (!shoulderEnabled)
		{
			DisableCurrentRotator("設定/連携で無効");
		}
		else if (TryResolveRuntimeRefs(proc))
		{
			EnsureRotator();
		}
	}

	private void PollSettingsFileReload()
	{
		if (Time.unscaledTime < _nextSettingsPollTime)
			return;
		_nextSettingsPollTime = Time.unscaledTime + 2f;

		string path = Path.Combine(_pluginDir, SettingsStore.FileName);
		try
		{
			DateTime lastWrite = File.GetLastWriteTimeUtc(path);
			if (lastWrite == _settingsFileLastWrite)
				return;
			_settings = SettingsStore.LoadOrCreate(_pluginDir, LogInfo, LogWarn, LogError);
			ApplyConfigOverrides(logChanges: false);
			_settingsFileLastWrite = File.GetLastWriteTimeUtc(path);
			LogInfo("設定ファイルを再読込");
		}
		catch (Exception ex)
		{
			LogWarn("設定監視に失敗: " + ex.Message);
		}
	}

	private bool TryResolveRuntimeRefs(HSceneProc proc)
	{
		if (proc == null)
		{
			LogResolveMissing("HSceneProc");
			return false;
		}
		ChaControl female = ResolveMainFemale(proc);
		if (female == null)
		{
			LogResolveMissing("FemaleChaControl");
			return false;
		}
		if ((object)_targetFemale != female)
		{
			_targetFemale = female;
			_animBody = null;
			_fbbik = null;
			DisableCurrentRotator("対象女性の変更");
			LogInfo("対象女性=" + GetFemaleName(_targetFemale));
		}
		Animator animBody = _targetFemale.animBody;
		if (animBody == null)
		{
			LogResolveMissing("animBody");
			return false;
		}
		if ((object)_animBody != animBody)
		{
			_animBody = animBody;
			_fbbik = null;
			DisableCurrentRotator("animBody変更");
			LogInfo("animBody=" + _animBody.name);
		}
		if (_fbbik == null)
		{
			_fbbik = ResolveFbbik(_targetFemale);
		}
		if (_fbbik == null)
		{
			LogResolveMissing("FullBodyBipedIK");
			return false;
		}
		return true;
	}

	private void EnsureRotator()
	{
		if (_fbbik == null)
		{
			return;
		}

		ShoulderRotator[] rotators = _fbbik.GetComponents<ShoulderRotator>();
		if (rotators != null && rotators.Length > 0)
		{
			_rotator = rotators[0];
			for (int i = 1; i < rotators.Length; i++)
			{
				if (rotators[i] != null)
				{
					UnityEngine.Object.Destroy(rotators[i]);
				}
			}

			if (rotators.Length > 1)
			{
				LogWarn("肩補正コンポーネント重複を検出し整理: " + rotators.Length + " -> 1");
			}
		}

		if (_rotator == null || (object)_rotator.gameObject != _fbbik.gameObject)
		{
			_rotator = _fbbik.gameObject.AddComponent<ShoulderRotator>();
			LogInfo("肩補正コンポーネントを接続: " + _fbbik.gameObject.name);
		}

		_rotator.Configure(_fbbik, (_targetFemale != null) ? _targetFemale.transform : null, _settings, _leftArmIkRunning, _rightArmIkRunning);
		if (!_rotator.enabled)
		{
			_rotator.enabled = true;
		}
	}

	private bool TryBindHipHijackArmIkEvents(bool forceLog)
	{
		if (_hipHijackEventBound)
		{
			return true;
		}

		if (!TryResolveHipHijackHandles(forceLog))
		{
			return false;
		}

		if (_hipHijackArmIkChangedEvent == null)
		{
			return false;
		}

		MethodInfo callback = GetType().GetMethod(nameof(OnHipHijackArmIkRunningChanged), BindingFlags.Instance | BindingFlags.NonPublic);
		Delegate handler = callback != null
			? Delegate.CreateDelegate(_hipHijackArmIkChangedEvent.EventHandlerType, this, callback, throwOnBindFailure: false)
			: null;
		if (handler == null)
		{
			if (forceLog || (_settings != null && _settings.VerboseLog))
			{
				LogWarn("HipHijack腕IKイベントの購読ハンドラ作成に失敗");
			}

			return false;
		}

		try
		{
			_hipHijackArmIkChangedEvent.AddEventHandler(null, handler);
			_hipHijackArmIkChangedHandler = handler;
			_hipHijackEventBound = true;
			SyncArmIkRunningStateFromHipHijack();
			if (forceLog || (_settings != null && _settings.VerboseLog))
			{
				LogInfo("HipHijack腕IKイベント購読を開始");
			}

			return true;
		}
		catch (Exception ex)
		{
			if (forceLog || (_settings != null && _settings.VerboseLog))
			{
				LogWarn("HipHijack腕IKイベント購読に失敗: " + ex.Message);
			}

			return false;
		}
	}

	private bool TryResolveHipHijackHandles(bool forceLog)
	{
		if (_hipHijackPluginType == null)
		{
			_hipHijackPluginType = Type.GetType(HipHijackTypeName + ", " + HipHijackAssemblyName, throwOnError: false);
			if (_hipHijackPluginType == null)
			{
				Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
				for (int i = 0; i < assemblies.Length; i++)
				{
					Type candidate = assemblies[i].GetType(HipHijackTypeName, throwOnError: false);
					if (candidate != null)
					{
						_hipHijackPluginType = candidate;
						break;
					}
				}
			}
		}

		if (_hipHijackPluginType == null)
		{
			if (forceLog || (_settings != null && _settings.VerboseLog))
			{
				LogWarn("HipHijackプラグイン型を解決できない: " + HipHijackTypeName);
			}

			return false;
		}

		if (_hipHijackArmIkChangedEvent == null)
		{
			_hipHijackArmIkChangedEvent = _hipHijackPluginType.GetEvent("ArmIkRunningChanged", BindingFlags.Public | BindingFlags.Static);
		}

		if (_hipHijackIsArmIkRunningMethod == null)
		{
			_hipHijackIsArmIkRunningMethod = _hipHijackPluginType.GetMethod("IsArmIkRunning", BindingFlags.Public | BindingFlags.Static);
		}

		if (_hipHijackArmIkChangedEvent == null || _hipHijackIsArmIkRunningMethod == null)
		{
			if (forceLog || (_settings != null && _settings.VerboseLog))
			{
				LogWarn("HipHijack連携API不足: ArmIkRunningChanged/IsArmIkRunning が見つからない");
			}

			return false;
		}

		return true;
	}

	private void UnbindHipHijackArmIkEvents()
	{
		if (!_hipHijackEventBound)
		{
			return;
		}

		try
		{
			_hipHijackArmIkChangedEvent?.RemoveEventHandler(null, _hipHijackArmIkChangedHandler);
		}
		catch
		{
		}

		_hipHijackArmIkChangedHandler = null;
		_hipHijackEventBound = false;
		_leftArmIkRunning = false;
		_rightArmIkRunning = false;
	}

	private void OnHipHijackArmIkRunningChanged(int idx, bool running)
	{
		if (idx == HipHijackLeftHandIndex)
		{
			_leftArmIkRunning = running;
			if (_settings != null && _settings.VerboseLog)
			{
				LogInfo("HipHijack通知: 左腕IK=" + (running ? "ON" : "OFF"));
			}

			return;
		}

		if (idx == HipHijackRightHandIndex)
		{
			_rightArmIkRunning = running;
			if (_settings != null && _settings.VerboseLog)
			{
				LogInfo("HipHijack通知: 右腕IK=" + (running ? "ON" : "OFF"));
			}
		}
	}

	private void SyncArmIkRunningStateFromHipHijack()
	{
		_leftArmIkRunning = QueryHipHijackArmIkRunning(HipHijackLeftHandIndex);
		_rightArmIkRunning = QueryHipHijackArmIkRunning(HipHijackRightHandIndex);
		if (_settings != null && _settings.VerboseLog)
		{
			LogInfo("HipHijack状態同期: 左腕IK=" + (_leftArmIkRunning ? "ON" : "OFF") + " 右腕IK=" + (_rightArmIkRunning ? "ON" : "OFF"));
		}
	}

	private bool QueryHipHijackArmIkRunning(int idx)
	{
		if (_hipHijackIsArmIkRunningMethod == null)
		{
			return false;
		}

		try
		{
			object value = _hipHijackIsArmIkRunningMethod.Invoke(null, new object[1] { idx });
			return value is bool result && result;
		}
		catch
		{
			return false;
		}
	}

	private void DisableCurrentRotator(string reason)
	{
		if (!(_rotator == null))
		{
			try
			{
				UnityEngine.Object.Destroy(_rotator);
			}
			catch
			{
			}
			LogInfo("肩補正コンポーネントを解除: " + reason);
			_rotator = null;
		}
	}

	private void DestroyAllRotators()
	{
		ShoulderRotator[] all = UnityEngine.Object.FindObjectsOfType<ShoulderRotator>();
		foreach (ShoulderRotator rot in all)
		{
			if (rot != null)
			{
				UnityEngine.Object.Destroy(rot);
			}
		}
		if (all.Length != 0)
		{
			LogInfo("肩補正コンポーネント一括解除数=" + all.Length);
		}
	}

	private ChaControl ResolveMainFemale(HSceneProc proc)
	{
		if (proc == null)
		{
			return null;
		}
		if (FiHSceneLstFemale != null && FiHSceneLstFemale.GetValue(proc) is IList listObj)
		{
			for (int i = 0; i < listObj.Count; i++)
			{
				ChaControl cha = listObj[i] as ChaControl;
				if (cha != null)
				{
					return cha;
				}
			}
		}
		ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindHActors();
		foreach (ChaControl cha2 in all)
		{
			if (cha2 != null && cha2.sex == 1)
			{
				return cha2;
			}
		}
		return null;
	}

	private static FullBodyBipedIK ResolveFbbik(ChaControl cha)
	{
		if (cha == null || cha.animBody == null)
		{
			return null;
		}
		FullBodyBipedIK direct = cha.animBody.GetComponent<FullBodyBipedIK>();
		if (direct != null)
		{
			return direct;
		}
		return cha.animBody.GetComponentInChildren<FullBodyBipedIK>(includeInactive: true);
	}

	private void BindConfigEntries()
	{
		_cfgEnabled = Config.Bind("General", "Enabled", _settings.Enabled, "プラグイン全体のON/OFF。");
		_cfgVerboseLog = Config.Bind("General", "VerboseLog", _settings.VerboseLog, "詳細なデバッグログを出力する。");
		_cfgRelayLogEnabled = Config.Bind("Logging", "EnableLogs", false, "MainGameLogRelay経由ログのON/OFF");
		_cfgShoulderDiagnosticLog = Config.Bind("Logging", "ShoulderDiagnosticLog", _settings.ShoulderDiagnosticLog, "肩補正の内部計算ログを一定間隔で出力する。");
		_cfgShoulderDiagnosticLogInterval = Config.Bind("Logging", "ShoulderDiagnosticLogInterval", _settings.ShoulderDiagnosticLogInterval, new ConfigDescription("肩補正診断ログの出力間隔（秒）。", new AcceptableValueRange<float>(0.05f, 2f)));
		_cfgRelayLogEnabled.SettingChanged += OnRelayLogSettingChanged;
		_cfgShoulderRotationEnabled = Config.Bind("General", "ShoulderRotationEnabled", _settings.ShoulderRotationEnabled, "肩の回転補正をON/OFFする。これがOFFだと肩補正は一切動かない。");

		_cfgIndependentShoulders = Config.Bind("Shoulder", "IndependentShoulders", _settings.IndependentShoulders, "左右の肩で別々のウェイト・オフセット値を使う。OFFにすると左の設定が両肩に適用される。");
		_cfgReverseShoulderL = Config.Bind("Shoulder", "ReverseShoulderL", _settings.ReverseShoulderL, "腕が下がっているとき、左肩の補正方向を逆にする。");
		_cfgReverseShoulderR = Config.Bind("Shoulder", "ReverseShoulderR", _settings.ReverseShoulderR, "腕が下がっているとき、右肩の補正方向を逆にする。");
		_cfgShoulderWeight = Config.Bind("Shoulder", "ShoulderWeight", _settings.ShoulderWeight, new ConfigDescription("左肩補正の反応強度。上げると肩が大きく動く。", new AcceptableValueRange<float>(0f, 5f)));
		_cfgShoulderOffset = Config.Bind("Shoulder", "ShoulderOffset", _settings.ShoulderOffset, new ConfigDescription("左肩補正が効き始める閾値。上げると補正が早めに働く。", new AcceptableValueRange<float>(-1f, 1f)));
		_cfgShoulderRightWeight = Config.Bind("Shoulder", "ShoulderRightWeight", _settings.ShoulderRightWeight, new ConfigDescription("右肩補正の反応強度（IndependentShouldersがONのとき有効）。", new AcceptableValueRange<float>(0f, 5f)));
		_cfgShoulderRightOffset = Config.Bind("Shoulder", "ShoulderRightOffset", _settings.ShoulderRightOffset, new ConfigDescription("右肩補正の閾値（IndependentShouldersがONのとき有効）。", new AcceptableValueRange<float>(-1f, 1f)));

		_cfgLoweredArmScale = Config.Bind("ArmState", "LoweredArmScale", _settings.LoweredArmScale, new ConfigDescription("腕が上腕より下がっているときの補正強度の倍率。0で無効、1でそのまま。", new AcceptableValueRange<float>(0f, 1f)));

		HookSettingChanged(_cfgEnabled);
		HookSettingChanged(_cfgVerboseLog);
		HookSettingChanged(_cfgShoulderDiagnosticLog);
		HookSettingChanged(_cfgShoulderDiagnosticLogInterval);
		HookSettingChanged(_cfgShoulderRotationEnabled);
		HookSettingChanged(_cfgIndependentShoulders);
		HookSettingChanged(_cfgReverseShoulderL);
		HookSettingChanged(_cfgReverseShoulderR);
		HookSettingChanged(_cfgShoulderWeight);
		HookSettingChanged(_cfgShoulderOffset);
		HookSettingChanged(_cfgShoulderRightWeight);
		HookSettingChanged(_cfgShoulderRightOffset);
		HookSettingChanged(_cfgLoweredArmScale);
	}

	private void ApplyConfigOverrides(bool logChanges)
	{
		if (_settings == null)
		{
			return;
		}

		_settings.Enabled = _cfgEnabled?.Value ?? _settings.Enabled;
		_settings.VerboseLog = _cfgVerboseLog?.Value ?? _settings.VerboseLog;
		_settings.ShoulderDiagnosticLog = _cfgShoulderDiagnosticLog?.Value ?? _settings.ShoulderDiagnosticLog;
		_settings.ShoulderDiagnosticLogInterval = Mathf.Clamp(_cfgShoulderDiagnosticLogInterval?.Value ?? _settings.ShoulderDiagnosticLogInterval, 0.05f, 2f);
		_settings.ShoulderRotationEnabled = _cfgShoulderRotationEnabled?.Value ?? _settings.ShoulderRotationEnabled;
		_settings.IndependentShoulders = _cfgIndependentShoulders?.Value ?? _settings.IndependentShoulders;
		_settings.ReverseShoulderL = _cfgReverseShoulderL?.Value ?? _settings.ReverseShoulderL;
		_settings.ReverseShoulderR = _cfgReverseShoulderR?.Value ?? _settings.ReverseShoulderR;
		_settings.ShoulderWeight = Mathf.Clamp(_cfgShoulderWeight?.Value ?? _settings.ShoulderWeight, 0f, 5f);
		_settings.ShoulderOffset = Mathf.Clamp(_cfgShoulderOffset?.Value ?? _settings.ShoulderOffset, -1f, 1f);
		_settings.ShoulderRightWeight = Mathf.Clamp(_cfgShoulderRightWeight?.Value ?? _settings.ShoulderRightWeight, 0f, 5f);
		_settings.ShoulderRightOffset = Mathf.Clamp(_cfgShoulderRightOffset?.Value ?? _settings.ShoulderRightOffset, -1f, 1f);
		_settings.LoweredArmScale = Mathf.Clamp01(_cfgLoweredArmScale?.Value ?? _settings.LoweredArmScale);

		if (logChanges && _settings.VerboseLog)
		{
			LogInfo("BepInEx設定変更を反映");
		}
	}

	private void HookSettingChanged<T>(ConfigEntry<T> entry)
	{
		if (entry != null)
		{
			entry.SettingChanged += OnAnyConfigSettingChanged;
		}
	}

	private void UnhookSettingChanged<T>(ConfigEntry<T> entry)
	{
		if (entry != null)
		{
			entry.SettingChanged -= OnAnyConfigSettingChanged;
		}
	}

	private void UnbindConfigEntries()
	{
		if (_cfgRelayLogEnabled != null)
		{
			_cfgRelayLogEnabled.SettingChanged -= OnRelayLogSettingChanged;
		}

		UnhookSettingChanged(_cfgEnabled);
		UnhookSettingChanged(_cfgVerboseLog);
		UnhookSettingChanged(_cfgShoulderDiagnosticLog);
		UnhookSettingChanged(_cfgShoulderDiagnosticLogInterval);
		UnhookSettingChanged(_cfgShoulderRotationEnabled);
		UnhookSettingChanged(_cfgIndependentShoulders);
		UnhookSettingChanged(_cfgReverseShoulderL);
		UnhookSettingChanged(_cfgReverseShoulderR);
		UnhookSettingChanged(_cfgShoulderWeight);
		UnhookSettingChanged(_cfgShoulderOffset);
		UnhookSettingChanged(_cfgShoulderRightWeight);
		UnhookSettingChanged(_cfgShoulderRightOffset);
		UnhookSettingChanged(_cfgLoweredArmScale);
	}

	private void OnAnyConfigSettingChanged(object sender, EventArgs e)
	{
		ApplyConfigOverrides(logChanges: true);
	}

	private void OnRelayLogSettingChanged(object sender, EventArgs e)
	{
		ApplyRelayLoggingState();
	}

	private void ApplyRelayLoggingState()
	{
		if (!LogRelayApi.IsAvailable)
		{
			return;
		}

		bool enabled = _cfgRelayLogEnabled != null && _cfgRelayLogEnabled.Value;
		LogRelayApi.SetOwnerLogKey(RelayOwner, RelayLogKey);
		LogRelayApi.SetOwnerEnabled(RelayOwner, enabled);
	}

	private void LogResolveMissing(string what)
	{
		float now = Time.unscaledTime;
		if (!string.Equals(_lastResolveMissing, what, StringComparison.Ordinal) || !(now < _nextResolveMissingLogTime))
		{
			_lastResolveMissing = what;
			_nextResolveMissingLogTime = now + 1f;
			LogWarn("参照解決に失敗: " + what);
		}
	}

	private static string GetFemaleName(ChaControl cha)
	{
		if (cha == null)
		{
			return "(null)";
		}
		try
		{
			if (cha.fileParam != null && !string.IsNullOrEmpty(cha.fileParam.fullname))
			{
				return cha.fileParam.fullname;
			}
		}
		catch
		{
		}
		return cha.name ?? "(unnamed)";
	}

	internal void LogShoulderDiagnostic(string message)
	{
		if (_settings == null || !_settings.ShoulderDiagnosticLog)
		{
			return;
		}

		LogInfo("[肩診断] " + message);
	}

	private void LogInfo(string message)
	{
		if (LogRelayApi.IsAvailable)
		{
			LogRelayApi.Info(RelayOwner, message);
			return;
		}

		base.Logger.LogInfo("[MainGirlShoulderIkStabilizer] " + message);
	}

	private void LogWarn(string message)
	{
		if (LogRelayApi.IsAvailable)
		{
			LogRelayApi.Warn(RelayOwner, message);
			return;
		}

		base.Logger.LogWarning("[MainGirlShoulderIkStabilizer] " + message);
	}

	private void LogError(string message)
	{
		if (LogRelayApi.IsAvailable)
		{
			LogRelayApi.Error(RelayOwner, message);
			return;
		}

		base.Logger.LogError("[MainGirlShoulderIkStabilizer] " + message);
	}
}
