using System;
using MainGameUnifiedUi;

namespace MainGirlHipHijack.Services
{
    internal sealed class UnifiedUiGateway
    {
        private const string TabId = "hiphijack";
        private const string TabLabel = "HipHijack";
        private const int TabOrder = 20;

        internal bool IsHosting()
        {
            return UnifiedUiApi.IsAvailable && UnifiedUiApi.IsHosting(TabId);
        }

        internal void Register(Action drawContent)
        {
            if (UnifiedUiApi.IsAvailable)
                UnifiedUiApi.RegisterTab(TabId, TabLabel, drawContent, TabOrder);
        }
    }
}
