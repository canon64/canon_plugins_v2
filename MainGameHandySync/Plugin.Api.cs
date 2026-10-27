namespace MainGameHandySync
{
    /// <summary>
    /// 外部プラグインから動作の開始/停止を切り替えるための公開API。
    /// リフレクション経由で型解決される前提（DLL参照は不要）。
    ///
    /// 実体は HAMP の有効フラグ。切ると次のフレームで StopHampIfRunning が走り、
    /// 機器へ hamp/stop が飛ぶ。入れると通常どおりゲージ連動で再開する。
    /// </summary>
    public static class MainGameHandySyncApi
    {
        public static bool IsAvailable => Plugin.Instance != null;

        /// <summary>現在の有効/無効。取得できなければ false を返す。</summary>
        public static bool TryGetEnabled(out bool enabled)
        {
            enabled = false;
            Plugin instance = Plugin.Instance;
            if (instance == null)
                return false;

            return instance.TryGetHampEnabledInternal(out enabled);
        }

        /// <summary>有効/無効を設定する。</summary>
        public static bool TrySetEnabled(bool enabled)
        {
            Plugin instance = Plugin.Instance;
            return instance != null && instance.TrySetHampEnabledInternal(enabled);
        }
    }

    internal sealed partial class Plugin
    {
        /// <summary>
        /// 外部から止められている間は true。
        /// この機器には動作系統が2つある（HAMPの自走往復と、LMのストローク送出）。
        /// HAMPのフラグだけ切ってもLM側が送り続けるので、両方をこのフラグで止める。
        /// </summary>
        internal bool _externalMotionStopped;

        internal bool TryGetHampEnabledInternal(out bool enabled)
        {
            enabled = false;
            if (_cfgHampEnabled == null)
                return false;

            enabled = _cfgHampEnabled.Value && !_externalMotionStopped;
            return true;
        }

        internal bool TrySetHampEnabledInternal(bool enabled)
        {
            if (_cfgHampEnabled == null)
                return false;

            if (_cfgHampEnabled.Value != enabled)
                _cfgHampEnabled.Value = enabled;

            // フラグを切るだけだと、毎フレームの停止処理がクールダウン中に黙って抜けて
            // 機器が止まらないまま処理も走らなくなる。押した時点で確実に反映させる。
            _externalMotionStopped = !enabled;

            if (enabled)
                PrepareHampRestartInternal();
            else
                ForceHampStopInternal();

            return true;
        }

        /// <summary>クールダウンを無視して即座に停止させる。</summary>
        private void ForceHampStopInternal()
        {
            _hampNextSwitchTime = 0f;
            _hampRunning = false;
            _hampLastVelocity = _hampLastMin = _hampLastMax = -1f;
            _gaugeSmoothed = -1f;

            if (_handyClient != null)
            {
                _handyClient.HampVelocity(0f);
                _handyClient.HampStop();
                // LM側は往復指令(SendXpt)を直接投げている。現在位置で止めるため0秒の指令を送る。
                _handyClient.SendXpt(0.5f, 0f);
            }
        }

        /// <summary>次のフレームで開始処理が確実に走る状態へ戻す。</summary>
        private void PrepareHampRestartInternal()
        {
            _hampNextSwitchTime = 0f;
            _hampRunning = false;
            _hampLastVelocity = _hampLastMin = _hampLastMax = -1f;
            _gaugeSmoothed = -1f;
        }
    }
}
