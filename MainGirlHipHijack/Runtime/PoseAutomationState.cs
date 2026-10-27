namespace MainGirlHipHijack
{
    /// <summary>
    /// 姿勢切替に連動した自動適用判定だけを所有する。
    /// </summary>
    internal sealed class PoseAutomationState
    {
        internal bool PendingApply { get; set; }
        internal int LastAnimatorStateHash { get; set; }
        internal int LastAnimatorLoop { get; set; }
        internal bool LoopReady { get; set; }
        internal int LoopCountSinceSwitch { get; set; }
        internal string LastFemalePresetId { get; set; }
        internal string LastMalePresetId { get; set; }
    }
}
