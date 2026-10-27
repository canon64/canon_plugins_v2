namespace MainGirlHipHijack.Services
{
    internal interface IFramePipelinePort
    {
        void RegisterUnifiedUi();
        void FlushSettings();
        bool IsEnabled();
        void RunFramePreamble();
        bool TryPrepareResolvedFrame();
        void RunResolvedFrame();
        bool CanRunAfterHSceneLateUpdate(HSceneProc process);
        void ApplyFemaleBodyIkAfterAnimation();
        void UpdateBodyControlLinkAfterAnimation();
        void ApplyVibeReactionAfterAnimation();
        void UpdateHipSpeedAfterAnimation();
        void UpdateMaleBodyAfterAnimation();
        void ApplyFemaleHeadAfterAnimation();
        void HandleHSceneAnimatorChanged(HSceneProc process);
    }

    internal sealed class FrameCoordinator
    {
        private readonly IFramePipelinePort _port;

        internal FrameCoordinator(IFramePipelinePort port)
        {
            _port = port;
        }

        internal void UpdateFrame()
        {
            _port.RegisterUnifiedUi();
            _port.FlushSettings();
            if (!_port.IsEnabled()) return;
            _port.RunFramePreamble();
            if (!_port.TryPrepareResolvedFrame()) return;
            _port.RunResolvedFrame();
        }

        internal void AfterHSceneLateUpdate(HSceneProc process)
        {
            if (!_port.CanRunAfterHSceneLateUpdate(process)) return;
            _port.ApplyFemaleBodyIkAfterAnimation();
            _port.UpdateBodyControlLinkAfterAnimation();
            _port.ApplyVibeReactionAfterAnimation();
            _port.UpdateHipSpeedAfterAnimation();
            _port.UpdateMaleBodyAfterAnimation();
            _port.ApplyFemaleHeadAfterAnimation();
        }

        internal void AfterHSceneAnimatorChanged(HSceneProc process)
        {
            if (_port.IsEnabled() && process != null)
                _port.HandleHSceneAnimatorChanged(process);
        }
    }
}
