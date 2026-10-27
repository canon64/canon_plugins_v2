namespace MainGirlHipHijack
{
    /// <summary>
    /// BodyIKの再バインド抑制と姿勢変更による中断状態を所有する。
    /// </summary>
    internal sealed class BodyIkLifecycleState
    {
        internal BodyIkLifecycleState(int operationPointCount)
        {
            NextOffLeakWarningTimes = new float[operationPointCount];
            NextOnRebindWarningTimes = new float[operationPointCount];
        }

        internal float[] NextOffLeakWarningTimes { get; }
        internal float[] NextOnRebindWarningTimes { get; }
        internal float NextFbbikReenableWarningTime { get; set; }
        internal bool AbandonedByPostureChange { get; set; }
        internal bool PendingPostureAbandon { get; set; }
        internal string PendingPostureAbandonTrigger { get; set; }
        internal float PendingPostureAbandonRequestTime { get; set; }
    }
}
