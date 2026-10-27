using UnityEngine;

namespace AstralMode
{
    /// <summary>
    /// アストラル界のあいだだけ霧を出す。
    ///
    /// 地面が途中で終わって星空との境目が線に見えるのを消すのが目的。
    /// 絵に焼いて隠す方法だと地面しか沈められないが、霧なら草も家具も遠景もまとめて沈み、
    /// 奥行きも出る。
    ///
    /// 霧はゲーム全体の設定（RenderSettings）なので、戻し忘れると現実に残る。
    /// そこを塞ぐため、戻す口を3つ用意する:
    ///   1) 通常の帰還（景色を隠すとき）
    ///   2) 緊急脱出（部屋を出た等でアストラルが強制終了したとき）
    ///   3) 毎フレームの見張り（アストラルでないのに霧が残っていたら即座に戻す）
    /// 元の値は掛ける直前に必ず控え、戻すのは何度呼んでも安全にする。
    ///
    /// 近距離は霧が掛からないよう開始距離を離してある。100m下の「止まった現実」を覗く
    /// サブカメラは被写体のすぐ近くにいるため、あちらの絵は霧の影響を受けない。
    /// </summary>
    public sealed partial class Plugin
    {
        private bool _astralFogApplied;
        private bool _savedFogEnabled;
        private Color _savedFogColor;
        private FogMode _savedFogMode;
        private float _savedFogStart;
        private float _savedFogEnd;
        private float _savedFogDensity;

        private void ApplyAstralFog()
        {
            if (_cfgUseAstralFog == null || !_cfgUseAstralFog.Value)
            {
                RestoreAstralFog("disabled by config");
                return;
            }

            if (!_astralFogApplied)
            {
                // 掛ける前に必ず控える。ここを飛ばすと戻せなくなる。
                _savedFogEnabled = RenderSettings.fog;
                _savedFogColor = RenderSettings.fogColor;
                _savedFogMode = RenderSettings.fogMode;
                _savedFogStart = RenderSettings.fogStartDistance;
                _savedFogEnd = RenderSettings.fogEndDistance;
                _savedFogDensity = RenderSettings.fogDensity;
                _astralFogApplied = true;
                SimpleFileLogger.Info($"Astral fog saved originals: enabled={_savedFogEnabled}, mode={_savedFogMode}, "
                    + $"color={_savedFogColor}, start={_savedFogStart:F1}, end={_savedFogEnd:F1}, density={_savedFogDensity:F4}");
            }

            float start = _cfgAstralFogStart != null ? _cfgAstralFogStart.Value : 18f;
            float end = Mathf.Max(start + 1f, _cfgAstralFogEnd != null ? _cfgAstralFogEnd.Value : 90f);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = _cfgAstralHorizonColor != null ? _cfgAstralHorizonColor.Value : new Color(0.047f, 0.063f, 0.133f);
            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = end;

            SimpleFileLogger.Info($"Astral fog applied: start={start:F1}, end={end:F1}, color={RenderSettings.fogColor}");
        }

        /// <summary>何度呼んでも安全。控えを取っていなければ何もしない。</summary>
        internal void RestoreAstralFog(string reason)
        {
            if (!_astralFogApplied) return;

            RenderSettings.fog = _savedFogEnabled;
            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.fogMode = _savedFogMode;
            RenderSettings.fogStartDistance = _savedFogStart;
            RenderSettings.fogEndDistance = _savedFogEnd;
            RenderSettings.fogDensity = _savedFogDensity;
            _astralFogApplied = false;
            _fogOverrideWarned = false;

            SimpleFileLogger.Info($"Astral fog restored ({reason}): enabled={_savedFogEnabled}, mode={_savedFogMode}");
        }

        private bool _fogOverrideWarned;

        /// <summary>
        /// 毎フレームの見張り。役目は2つ。
        ///
        /// 1) アストラルでないのに霧が残っていたら即座に戻す（現実へ持ち帰らせない最後の砦）
        /// 2) アストラル中は霧の値を上書きし続ける。
        ///    このゲームは元から霧を使っており（マップの夕焼け霧: 橙 start=5 end=150）、
        ///    ゲーム側やマップ側が毎フレーム霧を書き戻していると、こちらが一度掛けただけでは
        ///    次のフレームで消される。押し続けることで確実にこちらの値にする。
        ///    奪い返された形跡があれば一度だけログに残し、原因を追えるようにする。
        /// </summary>
        internal void TickAstralFogSafety()
        {
            if (!_astralFogApplied) return;

            if (_astralHillRoot == null || !_astralHillRoot.activeSelf)
            {
                RestoreAstralFog("safety-net: scenery not active");
                return;
            }

            if (_cfgUseAstralFog == null || !_cfgUseAstralFog.Value)
            {
                RestoreAstralFog("fog disabled by config");
                return;
            }

            Color want = _cfgAstralHorizonColor != null ? _cfgAstralHorizonColor.Value : new Color(0.047f, 0.063f, 0.133f);
            float start = _cfgAstralFogStart != null ? _cfgAstralFogStart.Value : 18f;
            float end = Mathf.Max(start + 1f, _cfgAstralFogEnd != null ? _cfgAstralFogEnd.Value : 90f);

            bool taken = !RenderSettings.fog
                || RenderSettings.fogMode != FogMode.Linear
                || Mathf.Abs(RenderSettings.fogStartDistance - start) > 0.01f
                || Mathf.Abs(RenderSettings.fogEndDistance - end) > 0.01f
                || RenderSettings.fogColor != want;

            if (!taken) return;

            if (!_fogOverrideWarned)
            {
                _fogOverrideWarned = true;
                SimpleFileLogger.Warn($"Astral fog was overwritten by someone else -> re-asserting every frame. "
                    + $"found: fog={RenderSettings.fog}, mode={RenderSettings.fogMode}, color={RenderSettings.fogColor}, "
                    + $"start={RenderSettings.fogStartDistance:F1}, end={RenderSettings.fogEndDistance:F1}");
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = want;
            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = end;
        }

        /// <summary>霧の距離・色だけを塗り替える。掛かっていないときは何もしない。</summary>
        private void RefreshAstralFogSettings()
        {
            if (!_astralFogApplied) return;
            ApplyAstralFog();
        }
    }
}
