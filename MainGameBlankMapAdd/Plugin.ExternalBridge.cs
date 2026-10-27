using MainGameBlankMapAdd.Integrations;
using BeatSyncSnapshot = MainGameBlankMapAdd.Integrations.IntegrationRegistry.BeatSyncSnapshot;
using SpeedLimitBreakSnapshot = MainGameBlankMapAdd.Integrations.IntegrationRegistry.SpeedLimitBreakSnapshot;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private bool TryGetSpeedLimitBreakSnapshot(out SpeedLimitBreakSnapshot snapshot) =>
            _integrations.TryGetSpeedLimitBreakSnapshot(out snapshot);

        private bool TryApplySpeedLimitBreakSnapshot(SpeedLimitBreakSnapshot snapshot, string reason) =>
            _integrations.TryApplySpeedLimitBreakSnapshot(snapshot, reason);

        private bool TryGetBeatSyncSnapshot(out BeatSyncSnapshot snapshot) =>
            _integrations.TryGetBeatSyncSnapshot(out snapshot);

        private bool TryApplyBeatSyncSnapshot(BeatSyncSnapshot snapshot, string reason) =>
            _integrations.TryApplyBeatSyncSnapshot(snapshot, reason);

        private bool TryApplyBeatSyncSnapshot(
            BeatSyncSnapshot snapshot,
            string reason,
            bool applyEnabled,
            bool preferSavedSongBpm,
            bool applyGlobalAutoMotionSwitch,
            bool applyGlobalAutoThreshold,
            bool applyGlobalVerboseLog) =>
            _integrations.TryApplyBeatSyncSnapshot(
                snapshot,
                reason,
                applyEnabled,
                preferSavedSongBpm,
                applyGlobalAutoMotionSwitch,
                applyGlobalAutoThreshold,
                applyGlobalVerboseLog);

        private bool TryGetAfterimageEnabled(out bool enabled) =>
            _integrations.TryGetAfterimageEnabled(out enabled);

        private bool TryApplyAfterimageEnabled(bool enabled, string reason) =>
            _integrations.TryApplyAfterimageEnabled(enabled, reason);

        private bool TryGetDollModeEnabled(out bool enabled) =>
            _integrations.TryGetDollModeEnabled(out enabled);

        private bool TryApplyDollModeEnabled(bool enabled, string reason) =>
            _integrations.TryApplyDollModeEnabled(enabled, reason);

        private bool TryGetVfebPoseChangeEnabled(out bool enabled) =>
            _integrations.TryGetVfebPoseChangeEnabled(out enabled);

        private bool TryApplyVfebPoseChangeEnabled(bool enabled, string reason) =>
            _integrations.TryApplyVfebPoseChangeEnabled(enabled, reason);

        private bool TryGetVfebScenarioTextAutoSendEnabled(out bool enabled) =>
            _integrations.TryGetVfebScenarioTextAutoSendEnabled(out enabled);

        private bool TryApplyVfebScenarioTextAutoSendEnabled(bool enabled, string reason) =>
            _integrations.TryApplyVfebScenarioTextAutoSendEnabled(enabled, reason);

        private bool TrySendVfebScenarioTextManualNow(string reason) =>
            _integrations.TrySendVfebScenarioTextManualNow(reason);

        private bool TryGetVrScreenEffectEnabled(out bool enabled) =>
            _integrations.TryGetVrScreenEffectEnabled(out enabled);

        private bool TryApplyVrScreenEffectEnabled(bool enabled, string reason) =>
            _integrations.TryApplyVrScreenEffectEnabled(enabled, reason);

        private bool TryGetVrScreenEffectUiVisible(out bool visible) =>
            _integrations.TryGetVrScreenEffectUiVisible(out visible);

        private bool TryApplyVrScreenEffectUiVisible(bool visible, string reason) =>
            _integrations.TryApplyVrScreenEffectUiVisible(visible, reason);

        private bool TryGetUnifiedUiVisible(out bool visible) =>
            _integrations.TryGetUnifiedUiVisible(out visible);

        private bool TryApplyUnifiedUiVisible(bool visible, string reason) =>
            _integrations.TryApplyUnifiedUiVisible(visible, reason);
    }
}
