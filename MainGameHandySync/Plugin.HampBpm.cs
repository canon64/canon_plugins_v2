using System;
using System.Reflection;
using UnityEngine;

namespace MainGameHandySync
{
    /// <summary>
    /// HAMP自走によるBPM追従。
    ///
    /// HDSPで1指令ずつ送る方式はこの回線では成立しない（実測: 往復1541ms＝毎秒0.6指令）。
    /// HAMPは速度を1回渡せば自走で往復し続けるので、往復遅延が動きに効かない。
    /// 引き換えに折り返しが直角になりイージングはかけられない（加減速のAPIは存在しない）。
    /// 詳細は HANDY_V3_SPEC_FINDINGS.md 4.5〜4.7。
    ///
    /// 実機で確定した関係（H02_pro / fw4.2.1）:
    ///   上限BPM ≈ VelocityConstant / 振幅の幅
    ///   velocity = 目標BPM × 幅 / VelocityConstant
    /// BPM120狙いの3段で 160/80/40 が出て、3段とも同じ倍率でずれた＝形は正しく定数だけの問題。
    /// 実測から VelocityConstant = 133。
    ///
    /// 経路: HFlag.speedCalc → SpeedLimitBreakの較正BPM → 倍率 → velocity + 振幅 → HAMP
    /// </summary>
    internal sealed partial class Plugin
    {
        private bool _hampRunning;
        private float _hampLastVelocity = -1f;
        private float _hampLastMin = -1f;
        private float _hampLastMax = -1f;
        private float _hampNextSwitchTime;
        private bool _hampWarnedNoBpm;
        private float _hampLastLogTime;
        private float _hampNextSendTime;
        private float _gaugeSmoothed = -1f;
        private bool? _hampMusicPlaying;

        private static Type _slbType;
        private static MethodInfo _slbBpmFromGauge;
        private static bool _slbResolved;

        /// <summary>SpeedLimitBreak の較正BPMをリフレクションで引く。未導入でも落ちない。</summary>
        private static float ResolveBpmFromGauge(float gauge01)
        {
            if (!_slbResolved)
            {
                _slbResolved = true;
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        Type t = asm.GetType("MainGameSpeedLimitBreak.Plugin", false);
                        if (t == null) continue;
                        _slbType = t;
                        _slbBpmFromGauge = t.GetMethod("BpmFromGauge",
                            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                            null, new[] { typeof(float) }, null);
                        break;
                    }
                    catch { }
                }
                if (Instance != null && Instance.ShouldLogNormal())
                    SimpleFileLogger.Info("[hamp] SpeedLimitBreak連携: "
                        + (_slbBpmFromGauge != null ? "利用可" : "見つからない（手動BPMへ落とす）"));
            }

            if (_slbBpmFromGauge == null) return -1f;
            try
            {
                object v = _slbBpmFromGauge.Invoke(null, new object[] { gauge01 });
                return v is float ? (float)v : -1f;
            }
            catch { return -1f; }
        }

        /// <summary>待機中か。稼働状態(ループ系)でなければ動かさない。</summary>
        private bool IsIdleState()
        {
            if (_hFlag == null) return true;
            string s = _hFlag.nowAnimStateName ?? string.Empty;
            if (s.Length == 0) return true;

            // WLoop/MLoop/SLoop とその派生(A_/WS_IN_/sameS/orgS)だけが稼働状態。
            return s.IndexOf("Loop", StringComparison.OrdinalIgnoreCase) < 0
                && s.IndexOf("same", StringComparison.OrdinalIgnoreCase) < 0
                && s.IndexOf("org", StringComparison.OrdinalIgnoreCase) < 0;
        }

        /// <summary>
        /// 速度ゲージ(0〜1)。BeatSyncが動いていればその書いた値が入っている。
        /// 生値は毎秒 0.06⇔0.55 のように大きく揺れるので、時定数で均す。
        /// 均さないと振幅と速度が毎フレーム変わり、1本0.92秒かかる指令が渋滞して何も届かなくなる。
        /// </summary>
        private float ReadGauge01()
        {
            if (_hFlag == null) return 0f;
            float raw = _hFlag.speedCalc;
            float lo = _cfgGaugeInputMin.Value, hi = _cfgGaugeInputMax.Value;
            float g = (hi - lo < 0.0001f) ? 0f : Mathf.Clamp01((raw - lo) / (hi - lo));

            float tau = _cfgGaugeSmoothSec.Value;
            if (tau <= 0.0001f || _gaugeSmoothed < 0f)
            {
                _gaugeSmoothed = g;
                return g;
            }
            _gaugeSmoothed += (g - _gaugeSmoothed) * Mathf.Clamp01(Time.unscaledDeltaTime / tau);
            return Mathf.Clamp01(_gaugeSmoothed);
        }

        /// <summary>毎フレーム呼ぶ。</summary>
        private void TickHampBpm()
        {
            if (!_cfgHampEnabled.Value) { StopHampIfRunning("disabled"); return; }

            if (_cfgStopWhenIdle.Value && IsIdleState())
            {
                StopHampIfRunning("idle");
                return;
            }

            float gauge = ReadGauge01();

            bool musicPlaying = MusicPlaybackReader.IsPlaying();
            if (_hampMusicPlaying != musicPlaying || (!musicPlaying && _noMusicBpmChanged))
            {
                // Playback changes and config edits must not be lost to the velocity deadband.
                _hampMusicPlaying = musicPlaying;
                _hampLastVelocity = -1f;
                _hampNextSendTime = 0f;
                if (!musicPlaying) _noMusicBpmChanged = false;
            }

            float bpm;
            string bpmSource;
            if (!musicPlaying)
            {
                // These endpoints are the final target BPM, independent of the music settings.
                bpm = GetNoMusicBpm(gauge);
                bpmSource = "no-music";
            }
            else
            {
                // Preserve the existing music playback calculation.
                bpm = _cfgUseSpeedLimitBreakBpm.Value ? ResolveBpmFromGauge(gauge) : -1f;
                bpmSource = "SLB";
                if (bpm <= 0f)
                {
                    if (_cfgUseSpeedLimitBreakBpm.Value && !_hampWarnedNoBpm)
                    {
                        _hampWarnedNoBpm = true;
                        if (ShouldLogError())
                            SimpleFileLogger.Warn("[hamp] SpeedLimitBreakの較正BPMが取れない"
                                + "（未導入か較正未実施）。手動BPMへ落とす");
                    }
                    bpm = Mathf.Lerp(_cfgManualBpmAtGauge0.Value, _cfgManualBpmAtGauge1.Value, gauge);
                    bpmSource = "manual";
                }

                bpm *= _cfgBpmMultiplier.Value;
                bpm = Mathf.Clamp(bpm, _cfgBpmMin.Value, _cfgBpmMax.Value);
            }

            // 振幅はゲージで補間。速いほど狭く浅く。
            float lo2 = Mathf.Lerp(_cfgStrokeMinAtGauge0.Value, _cfgStrokeMinAtGauge1.Value, gauge);
            float hi2 = Mathf.Lerp(_cfgStrokeMaxAtGauge0.Value, _cfgStrokeMaxAtGauge1.Value, gauge);
            if (hi2 < lo2) { float t = lo2; lo2 = hi2; hi2 = t; }
            float width = Mathf.Max(0.01f, hi2 - lo2);

            float velocity = Mathf.Clamp(
                bpm * width / Mathf.Max(1f, _cfgVelocityConstant.Value),
                _cfgVelocityMin.Value, _cfgVelocityMax.Value);

            if (!_hampRunning)
            {
                if (Time.unscaledTime < _hampNextSwitchTime) return;
                _hampNextSwitchTime = Time.unscaledTime + _cfgSwitchCooldownSec.Value;
                _hampRunning = true;
                _hampLastVelocity = _hampLastMin = _hampLastMax = -1f;
                _handyClient.EnsureHampMode();
                _handyClient.HampStart();
                if (ShouldLogNormal())
                    SimpleFileLogger.Info($"[hamp] 開始 state={_hFlag.nowAnimStateName} "
                        + $"gauge={gauge:0.###} bpm={bpm:0.#}");
            }

            // 指令1本が届くのに約0.92秒。間隔を空けないと渋滞する。
            // 開始/停止は上で処理済みなので、ここで間引くのは速度と振幅だけ。
            if (Time.unscaledTime < _hampNextSendTime) return;
            _hampNextSendTime = Time.unscaledTime + _cfgSendIntervalSec.Value;

            bool always = !_cfgSendOnlyWhenChanged.Value;
            bool strokeChanged = always
                              || Mathf.Abs(lo2 - _hampLastMin) >= _cfgStrokeDeadband.Value
                              || Mathf.Abs(hi2 - _hampLastMax) >= _cfgStrokeDeadband.Value;
            if (strokeChanged)
            {
                _hampLastMin = lo2; _hampLastMax = hi2;
                _handyClient.SetStrokeRange(lo2, hi2);
            }

            if (always || Mathf.Abs(velocity - _hampLastVelocity) >= _cfgVelocityDeadband.Value)
            {
                _hampLastVelocity = velocity;
                _handyClient.HampVelocity(velocity);
            }
            else if (!strokeChanged)
            {
                return;
            }

            float logEvery = _cfgLogIntervalSec != null ? _cfgLogIntervalSec.Value : 1.5f;
            if (logEvery > 0f && Time.unscaledTime - _hampLastLogTime >= logEvery && ShouldLogDetailed())
            {
                _hampLastLogTime = Time.unscaledTime;
                SimpleFileLogger.Debug($"[hamp] gauge={gauge:0.###} bpm={bpm:0.#}({bpmSource}) "
                    + $"stroke={lo2:0.##}-{hi2:0.##}(幅{width:0.##}) velocity={velocity:0.###} "
                    + $"state={_hFlag.nowAnimStateName}");
            }
        }

        private void StopHampIfRunning(string reason)
        {
            if (!_hampRunning) return;
            if (Time.unscaledTime < _hampNextSwitchTime) return;
            _hampNextSwitchTime = Time.unscaledTime + _cfgSwitchCooldownSec.Value;
            _hampRunning = false;
            _hampLastVelocity = _hampLastMin = _hampLastMax = -1f;
            _gaugeSmoothed = -1f;
            _handyClient.HampVelocity(0f);
            _handyClient.HampStop();
            if (ShouldLogNormal())
                SimpleFileLogger.Info($"[hamp] 停止 reason={reason}");
        }
    }
}
