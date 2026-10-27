using BepInEx;
using MainGamePregnancyPlusBridge;
using CanonPlugins.Shared;

namespace MainGameVoiceImpactBoost
{
    [BepInPlugin(GUID, PluginName, Version)]
    [BepInProcess("KoikatsuSunshine")]
    [BepInProcess("KoikatsuSunshine_VR")]
    [BepInDependency("com.kks.main.pregnancyplusbridge", BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("com.kks.maingame.voicefaceeventbridge", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.kks.main.voiceimpactboost";
        public const string PluginName = "MainGameVoiceImpactBoost";
        public const string Version = "0.3.0";

        private PluginConfig _config;
        private PluginFileLogger _logger;
        private HSceneMonitor _hSceneMonitor;
        private VoiceImpactBoostService _boostService;
        private bool _subscribed;
        private bool? _lastCueModeEnabled;
        private float _nextCueModeRefreshTime;
        private float _nextGateDiagnosticAt;

        private void Awake()
        {
            _config = new PluginConfig(Config);
            _logger = new PluginFileLogger(base.Logger, Info.Location, PluginName + ".log", _config.VerboseLog.Value);
            _boostService = new VoiceImpactBoostService(_config, _logger);
            _hSceneMonitor = new HSceneMonitor(_config, _logger, _boostService.ResetOneShotState);
            _config.Enabled.SettingChanged += OnCueModeChanged;
            _config.MoanModeEnabled.SettingChanged += OnCueModeChanged;
            UpdateCueMode();

            TrySubscribe();
            _logger.LogInfo("起動完了 v" + Version);
        }

        private void Update()
        {
            UpdateCueMode();
            if (_config.VerboseLog.Value && UnityEngine.Time.unscaledTime >= _nextGateDiagnosticAt)
            {
                _nextGateDiagnosticAt = UnityEngine.Time.unscaledTime + 10f;
                _logger.LogInfo("gate enabled=" + _config.Enabled.Value + " moanMode=" + _config.MoanModeEnabled.Value
                    + " blankRoom=" + BlankRoomGate.IsInBlankRoom);
            }
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            _hSceneMonitor.Update();
        }

        private void OnDestroy()
        {
            MainGameVoiceFaceEventBridge.PublicApi.SetImpactCueEnabled(GUID, false);
            _config.Enabled.SettingChanged -= OnCueModeChanged;
            _config.MoanModeEnabled.SettingChanged -= OnCueModeChanged;
            TryUnsubscribe();
        }

        private void OnCueModeChanged(object sender, System.EventArgs args) => UpdateCueMode();

        private void UpdateCueMode()
        {
            bool enabled = _config.Enabled.Value && _config.MoanModeEnabled.Value && BlankRoomGate.IsInBlankRoom;
            if (_lastCueModeEnabled == enabled && UnityEngine.Time.unscaledTime < _nextCueModeRefreshTime)
                return;
            _lastCueModeEnabled = enabled;
            _nextCueModeRefreshTime = UnityEngine.Time.unscaledTime + 1f;
            MainGameVoiceFaceEventBridge.PublicApi.SetImpactCueEnabled(GUID, enabled);
        }

        private void TrySubscribe()
        {
            try
            {
                MainGamePregnancyPlusBridge.Plugin.OnBellyBokoPeak += _boostService.HandleBellyBokoPeak;
                _subscribed = true;
                _logger.LogInfo("subscribed OnBellyBokoPeak");
            }
            catch (System.Exception ex)
            {
                _logger.LogInfo("subscribe failed: " + ex.Message);
            }
        }

        private void TryUnsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            try
            {
                MainGamePregnancyPlusBridge.Plugin.OnBellyBokoPeak -= _boostService.HandleBellyBokoPeak;
                _logger.LogInfo("unsubscribed OnBellyBokoPeak");
            }
            catch (System.Exception ex)
            {
                _logger.LogInfo("unsubscribe failed: " + ex.Message);
            }

            _subscribed = false;
        }
    }
}
