using MainGameVoiceFaceEventBridge.Audio;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed partial class ExternalVoicePlayer
    {
        private VoiceMouthEmitter _dialogueEmitter, _cueEmitter;
        private ChaControl _dialogueFemale;

        internal void InitializeSpatialVoice(SpatialVoiceSettings settings)
        {
            EnsureSource();
            _dialogueEmitter = _host.AddComponent<VoiceMouthEmitter>();
            _dialogueEmitter.Initialize(_source, settings, _logInfo);
            if (_cueHost != null)
            {
                _cueEmitter = _cueHost.AddComponent<VoiceMouthEmitter>();
                _cueEmitter.Initialize(_cueSource, settings, _logInfo);
            }
        }
    }
}
