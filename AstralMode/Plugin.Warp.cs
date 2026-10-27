using System;
using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private bool _astralPreBakeDone;

        // ワープ中（白へ→白→白から）は喘ぎ／口パク音声を止める。
        //   ・isVoicePrcoStop=true で HVoiceCtrl.Proc 内の新規発声を抑制
        //   ・各 ChaControl の asVoice.Stop() で再生中の音を切る
        private void SuppressVoiceDuringWarp(bool suppress, string phase)
        {
            try
            {
                bool entryClimaxGrace = suppress && phase == "enter-warp-start" && IsEntryClimaxVoiceGraceActive();

                if (_hSceneProc != null && _hSceneProc.voice != null)
                {
                    _hSceneProc.voice.isVoicePrcoStop = entryClimaxGrace ? false : suppress;
                }

                if (suppress)
                {
                    foreach (var cha in CollectCurrentHSceneCharas())
                    {
                        if (cha == null) continue;
                        try
                        {
                            if (cha.asVoice != null && cha.asVoice.isPlaying)
                                cha.asVoice.Stop();
                        }
                        catch (Exception ex)
                        {
                            SimpleFileLogger.Warn($"SuppressVoiceDuringWarp: Stop failed cha={cha.name} phase={phase}: {ex.Message}");
                        }
                    }
                }

                SimpleFileLogger.Info($"SuppressVoiceDuringWarp: phase={phase} suppress={suppress}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"SuppressVoiceDuringWarp failed phase={phase}: {ex.Message}");
            }
        }

        // 突入開始から一定秒数だけ毎フレーム呼ぶ。突入直後に漏れるゲーム音声を確実に潰す。
        // isVoicePrcoStop=true（新規発声抑制）＋ 再生中 asVoice.Stop()。窓中は呼吸も切れるが突入直後のみ。
        // ログは出さない（毎フレーム呼ばれるため）。
        private float _astralHardStopUntil;

        private void HardStopHSceneVoice()
        {
            try
            {
                if (IsEntryClimaxVoiceGraceActive())
                {
                    if (_hSceneProc != null && _hSceneProc.voice != null)
                        _hSceneProc.voice.isVoicePrcoStop = false;
                    return;
                }

                if (_hSceneProc != null && _hSceneProc.voice != null)
                {
                    _hSceneProc.voice.isVoicePrcoStop = true;
                }

                foreach (var cha in CollectCurrentHSceneCharas())
                {
                    if (cha == null) continue;
                    try
                    {
                        if (cha.asVoice != null && cha.asVoice.isPlaying)
                            cha.asVoice.Stop();
                    }
                    catch { }
                }
            }
            catch { }
        }

        // 待機セリフ抑制用（HFlag.voice の SetXxxIdleTime をリフレクションで呼ぶ）。
        private bool _idleVoiceLookupDone;
        private System.Reflection.FieldInfo _hflagVoiceField;
        private System.Reflection.MethodInfo _miSetAibuIdle, _miSetHoushiIdle, _miSetSonyuIdle;

        private void EnsureIdleVoiceApi()
        {
            if (_idleVoiceLookupDone) return;
            _idleVoiceLookupDone = true;
            try
            {
                _hflagVoiceField = AccessTools.Field(typeof(HFlag), "voice");
                System.Type vt = _hflagVoiceField != null ? _hflagVoiceField.FieldType : null;
                if (vt != null)
                {
                    _miSetAibuIdle = AccessTools.Method(vt, "SetAibuIdleTime");
                    _miSetHoushiIdle = AccessTools.Method(vt, "SetHoushiIdleTime");
                    _miSetSonyuIdle = AccessTools.Method(vt, "SetSonyuIdleTime");
                    SimpleFileLogger.Info($"IdleVoiceApi: voiceField={(_hflagVoiceField != null)} aibu={(_miSetAibuIdle != null)} houshi={(_miSetHoushiIdle != null)} sonyu={(_miSetSonyuIdle != null)}");
                }
                else
                {
                    SimpleFileLogger.Warn("IdleVoiceApi: HFlag.voice field not found");
                }
            }
            catch (Exception ex) { SimpleFileLogger.Warn("EnsureIdleVoiceApi failed: " + ex.Message); }
        }

        // アストラル界 滞在中、VoiceProc は Harmony Prefix で選別する。
        //   → 通常voiceは落とし、絶頂前/絶頂時のHSonyu voiceだけ通す。
        //   → breath/breathShort(呼吸/息づかい)は VoiceProc の後段なのでそのまま鳴る。
        //   → asVoice.Stop() は呼ばない（呼ぶと呼吸まで切れる）。
        private void MaintainAstralVoiceSuppression()
        {
            if (_cfgSuppressVoiceInAstral == null || !_cfgSuppressVoiceInAstral.Value) return;
            if (_state != AstralState.Astral) return;
            if (_hSceneProc == null || _hSceneProc.voice == null) return;

            if (_astralHardStopUntil > Time.unscaledTime) return;
            _hSceneProc.voice.isVoicePrcoStop = false;
        }

        // ============================================================
        // 突入コルーチン
        //   Normal → Warping(全エフェクト同時0→1) → WhiteHold(裏処理) → FadeIn → Astral
        // ============================================================
        internal IEnumerator EnterAstralCoroutine()
        {
            _astralPreBakeDone = false;
            if (!SuspendVmdForAstral()) yield break;

            // Human_2_kks(Grokブリッジ)へ突入を通知（指定URLへ遷移させる）。
            WriteAstralSignal("enter");

            CaptureFovsForWarp("enter");

            _state = AstralState.Warping;
            SimpleFileLogger.Warn("WARP_FILE_VERSION=prebake-at-toggle-20260528-1145");
            SimpleFileLogger.Info("=== EnterAstral: Warping start ===");
            SetKsPlugTimePassesSuppressed(true, "enter-warp-start");
            SuppressVoiceDuringWarp(true, "enter-warp-start");
            // 突入直後に漏れるゲーム音声を確実に潰すため、ここから一定秒数だけ毎フレーム強制Stopする窓を開く。
            _astralHardStopUntil = Time.unscaledTime
                + (_cfgAstralEntryHardStopSeconds != null ? Mathf.Max(0f, _cfgAstralEntryHardStopSeconds.Value) : 0f);
            // verbose pose diag disabled
            // Fキーを押した瞬間の現実H状態を保存する。
            // Animator.speed も保存するので、Freeze より前に取る必要がある。
            CaptureRealStateForAstral();
            CaptureSubCameraTargetLineBeforeWarp();
            CaptureSkyDisplayOrigin();

            // 現実Hポーズを固定して、その場で即焼く。
            // WhiteHoldまで待つとIK/物理/後段更新で見た目が変わる。
            FreezeAllCharacterAnimators();
            // verbose pose diag disabled
            // verbose pose diag disabled
            TeleportCharasToAstral();
            // verbose pose diag disabled
            ReleaseVmdAfterBake();
            _astralPreBakeDone = true;

            PlayWarpSound("enter");

            EnsureWhiteCanvas();
            EnsureBloom();
            SetCanvasAlpha(0f);
            SetBloomIntensity(0f);

            float duration = Mathf.Max(0.01f, _cfgWarpDuration.Value);
            float elapsed = 0f;
            SimpleFileLogger.Info($"EnterAstral warp timing: duration={duration:F3}, whiteCanvasStartT={_cfgWhiteCanvasStartT.Value:F3}, whiteCanvasEndT={_cfgWhiteCanvasEndT.Value:F3}, whiteCanvasEasing={_cfgWhiteCanvasEasing.Value}");

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t01 = Mathf.Clamp01(elapsed / duration);
                DriveWarpEffects(t01);
                yield return null;
            }

            // 確実に最大値に揃える
            DriveWarpEffects(1f);
            SetCanvasAlpha(1f);
            ClearExternalWarpEffects();
            SimpleFileLogger.Info("Warping complete -> WhiteHold");

            // ---- White Hold Phase ----
            _state = AstralState.WhiteHold;
            yield return ProcessWhileWhite_EnterAstral();

            // ---- Fade In Phase ----
            _state = AstralState.FadeIn;
            SimpleFileLogger.Info("FadeIn start");

            float fadeDur = Mathf.Max(0.01f, _cfgFadeOutDuration.Value);
            float fadeElapsed = 0f;
            EasingType fadeEase = _cfgFadeOutEasing.Value;
            SimpleFileLogger.Info($"FadeIn timing: duration={fadeDur:F3}, easing={fadeEase}, canvasAlphaFrom=1.000, canvasAlphaTo=0.000, configKey=Fade Out Duration");

            while (fadeElapsed < fadeDur)
            {
                fadeElapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(fadeElapsed / fadeDur);
                float eased = Easing.Apply(fadeEase, t);
                SetCanvasAlpha(1f - eased);
                DriveFovFade(t);
                yield return null;
            }
            SetCanvasAlpha(0f);

            SetBloomIntensity(0f);
            // steady(残像＋波紋) が有効なら、白画面～フェード中に既に適用済みの効果をここで消さず
            // そのまま Astral へ継続させる（消すと一瞬効果が途切れてから再適用される）。
            if (_cfgAstralSteadyEnabled == null || !_cfgAstralSteadyEnabled.Value)
                ClearExternalWarpEffects();
            RestoreOriginalFov("fade-in-end");

            _state = AstralState.Astral;
            SimpleFileLogger.Info("=== Astral steady state ===");
            // voice は全有効に戻す（動作中の喘ぎを鳴らす）。待機セリフだけは Update側でアイドルタイマーを
            // 毎フレームリセットして発火させない（喘ぎ・呼吸はそのまま）。
            SuppressVoiceDuringWarp(false, "astral-steady");
            StartAstralBgm();
            // verbose pose diag disabled
        }

        // ============================================================
        // 帰還コルーチン
        //   Astral → ReturnWarping(白へ) → ReturnHold(裏で復元) → ReturnFadeOut → Normal
        // ============================================================
        internal IEnumerator ExitAstralCoroutine()
        {
            // Human_2_kks(Grokブリッジ)へ復帰を通知（直前のチャットURLへ戻させる）。
            WriteAstralSignal("exit");

            // 突入の強制停止窓を確実に閉じる。素早い enter→exit でも窓が現実復帰を跨いで
            // isVoicePrcoStop=true を再セットしてしまわないようにする（現実で音声が止まる事故防止）。
            _astralHardStopUntil = 0f;

            CaptureFovsForWarp("exit");

            _state = AstralState.ReturnWarping;
            SimpleFileLogger.Info("=== ExitAstral: ReturnWarping start ===");
            SuppressVoiceDuringWarp(true, "return-warp-start");
            PlayWarpSound("return");

            EnsureWhiteCanvas();
            EnsureBloom();
            SetCanvasAlpha(0f);
            SetBloomIntensity(0f);

            float duration = Mathf.Max(0.01f, _cfgWarpDuration.Value);
            float elapsed = 0f;
            SimpleFileLogger.Info($"ExitAstral warp timing: duration={duration:F3}, whiteCanvasStartT={_cfgWhiteCanvasStartT.Value:F3}, whiteCanvasEndT={_cfgWhiteCanvasEndT.Value:F3}, whiteCanvasEasing={_cfgWhiteCanvasEasing.Value}");

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t01 = Mathf.Clamp01(elapsed / duration);
                DriveWarpEffects(t01);
                yield return null;
            }

            DriveWarpEffects(1f);
            SetCanvasAlpha(1f);
            ClearExternalWarpEffects();
            SimpleFileLogger.Info("ReturnWarping complete -> ReturnHold");

            // ---- Return Hold ----
            _state = AstralState.ReturnHold;
            yield return ProcessWhileWhite_ExitAstral();

            // ---- Return Fade Out ----
            _state = AstralState.ReturnFadeOut;
            SimpleFileLogger.Info("ReturnFadeOut start");

            float fadeDur = Mathf.Max(0.01f, _cfgFadeOutDuration.Value);
            float fadeElapsed = 0f;
            EasingType fadeEase = _cfgFadeOutEasing.Value;
            SimpleFileLogger.Info($"ReturnFadeOut timing: duration={fadeDur:F3}, easing={fadeEase}, canvasAlphaFrom=1.000, canvasAlphaTo=0.000, configKey=Fade Out Duration");

            while (fadeElapsed < fadeDur)
            {
                fadeElapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(fadeElapsed / fadeDur);
                float eased = Easing.Apply(fadeEase, t);
                SetCanvasAlpha(1f - eased);
                DriveFovFade(t);
                yield return null;
            }
            SetCanvasAlpha(0f);
            SetBloomIntensity(0f);
            ClearExternalWarpEffects();
            RestoreOriginalFov("return-fade-out-end");

            StopAstralBgm();

            _state = AstralState.Normal;
            ResumeVmdAfterFade();
            _astralPreBakeDone = false;
            SimpleFileLogger.Info("=== Back to Normal HScene ===");
            SetKsPlugTimePassesSuppressed(false, "back-to-normal");
            SuppressVoiceDuringWarp(false, "back-to-normal");

            // 復元は白フェード裏で全部完了済み。ここでは何もしない。
            ClearSavedRealState();
        }

        // ============================================================
        // 全エフェクトを正規化時間 t01 (0..1) で駆動
        // ============================================================
        private void DriveWarpEffects(float t01)
        {
            DriveWarpMixer(t01);
        }

        // ============================================================
        // 白Canvas固定中の重い処理(突入)
        // ここでマップ生成・キャラ移動・体位変更を全部やる(Phase 2以降)
        // ============================================================
        private IEnumerator ProcessWhileWhite_EnterAstral()
        {
            SimpleFileLogger.Info("WhiteHold: enter processing start");
            DriveFovHold();
            SuspendMainGameSubCameraForAstral("enter-whitehold");
            // verbose pose diag disabled
            // 1) Animator停止 (現実位置の本体も止める)
            // EnterAstralCoroutine の開始時点で既に止めている。
            // ここでは二重停止して、白画面裏処理中も確実に固定する。
            if (!_astralPreBakeDone)
            {
                FreezeAllCharacterAnimators();
            }
            else
            {
                SimpleFileLogger.Warn("WhiteHold: skip second freeze because animators were already frozen at warp-start");
            }
            // verbose pose diag disabled
            yield return null;
            // verbose pose diag disabled
            if (!_astralPreBakeDone)
            {
                SimpleFileLogger.Warn("WhiteHold: fallback bake path used");

                CaptureRealStateForAstral();
                yield return null;
            // verbose pose diag disabled
                TeleportCharasToAstral();
            // verbose pose diag disabled
                yield return null;
            }
            else
            {
                SimpleFileLogger.Warn("WhiteHold: skip bake because FrozenClone was already created at warp-start");
            // verbose pose diag disabled
                yield return null;
            }

            // 2.3) 本体を生かして、アストラルの体位（ベンチフェラ）へ強制切替。
            //      凍結中(speed=0)だと actionChange が処理されないので先に解除する。
            //      入力(HSprite)は抑制されたままなので、切替後はベンチフェラの待機状態で止まる。
            UnfreezeAllCharacterAnimators();
            ForceAstralPose();
            yield return null;
            yield return null;
            // verbose pose diag disabled
            // 2.5) ② アストラル入場リセット（クリーン化: 汗/汁/頬赤/涙/感度/回数ゼロ）
            ResetCharasForAstralClean();
            SetAstralCleanVisualsActive(true, "enter-after-reset");
            yield return null;

            yield return ForceMaleSonInvisibleOnAstralEntry(5);

            // 2.6) 女キャラを指定スロットのコーディネートへ切替（男キャラは対象外）
            CaptureAndApplyAstralCoordinate();
            yield return null;

            // 2.7) アストラル中は男女とも全パーツを着衣状態にする。
            //      女はコーデReload後でないと clothesState[] が戻されるため、この順番で数フレーム維持する。
            yield return ForceAllClothesOnAstralEntry(5);

            BeginExternalAstralRuntime();
            yield return null;

            // 3) 白い部屋生成 (アストラル界)
            CreateWhiteRoom();
            ShowAstralHill();
            ApplyAstralSkybox();
            ShowAstralLight();
            yield return null;

            // 4) 現実世界にサブカメラ配置(自動オービット) + アストラル界にディスプレイ
            CreateRealWorldSubCamera(GetRealWorldCharaCenter());
            yield return null;

            // 5) メインカメラをアストラル界へテレポート (乗っ取らない、ゲーム本体のカメラ操作は維持)
            SimpleFileLogger.Info("MainCamera stays in-place for AstralMode");

            // Phase 3予定: 服状態変更

            // ホールド時間
            float hold = Mathf.Max(0f, _cfgWhiteHoldDuration.Value);
            SimpleFileLogger.Info($"WhiteHold enter hold timing: duration={hold:F3}");
            float elapsed = 0f;
            while (elapsed < hold)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            SimpleFileLogger.Info("WhiteHold: enter processing done");
        }

        // ============================================================
        // 白Canvas固定中の重い処理(帰還)
        // 逆処理: 座標復元 → 体位戻し → 部屋オブジェクト破棄 → Animator復活
        // ============================================================
        private IEnumerator ProcessWhileWhite_ExitAstral()
        {
            SimpleFileLogger.Info("WhiteHold: exit processing start");
            DriveFovHold();

            // 1) メインカメラを現実世界へ復元
            RestoreMainCameraToReal();
            yield return null;

            // 2) サブカメラ+ディスプレイ破棄
            DestroyRealWorldSubCamera();
            yield return null;

            // 3) 白い部屋破棄
            RestoreAstralSkybox();
            HideAstralHill();
            HideAstralLight();
            DestroyWhiteRoom();
            yield return null;

            EndExternalAstralRuntime();
            yield return null;

            // 3.5) ④ 現実Hの状態を書き戻す。体位リクエスト（selectAnimationListInfo + click=actionChange）を出す。
            SetAstralCleanVisualsActive(false, "exit-before-restore-real-state");
            RestoreRealStateFromAstral();
            yield return null;

            // 4) 本体を現実位置に復元 + FrozenClone破棄
            RestoreCharasToReal();
            yield return null;

            // 5) Animator再開
            UnfreezeAllCharacterAnimators();
            yield return null;

            // 5.5) 段階復元（白フェード裏で全部終わらせる）：体位 → モーション → オート
            // ここでフェード前に完了させる事で、フェードアウト後の "余計な処理" が見えない。

            // 体位はリクエスト済み。actionChange がゲーム本体で処理されるのを数フレーム待つ。
            for (int i = 0; i < 4; i++)
                yield return null;

            // モーション段階: H ステートを SLoop に戻す + Animator clip を保存値で再生
            RestoreSavedHActionState("white-motion");
            RestoreSavedAnimatorMotionNow("white-motion");
            yield return null;

            // 念押しで Animator もう一度（KKS が直後に上書きしてくる可能性ケア）
            RestoreSavedAnimatorMotionNow("white-motion-2");
            yield return null;
            yield return null;

            // オート段階: 保存値が True ならオートをONに
            if (_savedAutoEnabled)
                WriteCurrentActionAuto(_savedPoseMode, true, "white-auto");
            yield return null;

            // 女キャラのコーディネートを元スロットへ戻す。
            // Reload で clothesState[] が初期化されるため、必ず RestoreSavedCharaVisualsFinal の前。
            RestoreOriginalCoordinate();
            yield return null;

            // 最終見た目復元。
            // 体位・モーション・オートが終わった後に戻す。
            // 順番: 頬赤 → 肌ツヤ → 涙 → 服状態 → 眉 → 目 → 口 → 目開き → 口開き → 汁
            RestoreSavedCharaVisualsFinal("white-final");
            yield return null;

            yield return RestoreVmdForAstral();
            ResumeMainGameSubCameraFromAstral("exit-whitehold-after-restore");
            yield return null;

            float hold = Mathf.Max(0f, _cfgWhiteHoldDuration.Value);
            SimpleFileLogger.Info($"WhiteHold exit hold timing: duration={hold:F3}");
            float elapsed = 0f;
            while (elapsed < hold)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
            SimpleFileLogger.Info("WhiteHold: exit processing done");
        }

        private IEnumerator ForceAllClothesOnAstralEntry(int frameCount)
        {
            if (_hSceneProc == null || frameCount <= 0) yield break;

            var charas = CollectCurrentHSceneCharas();
            Array clothesKinds = Enum.GetValues(typeof(ChaFileDefine.ClothesKind));

            int failureCount = 0;
            int appliedCount = 0;

            for (int f = 0; f < frameCount; f++)
            {
                foreach (ChaControl cha in charas)
                {
                    if (cha == null) continue;

                    foreach (ChaFileDefine.ClothesKind kind in clothesKinds)
                    {
                        try
                        {
                            cha.SetClothesState((int)kind, 0);
                            appliedCount++;
                        }
                        catch
                        {
                            failureCount++;
                        }
                    }
                }
                yield return null;
            }

            if (failureCount > 0)
            {
                SimpleFileLogger.Info($"ForceAllClothesOnAstralEntry: applied={appliedCount} failures={failureCount} charas={charas.Count} frames={frameCount}");
            }
            else
            {
                SimpleFileLogger.Info($"ForceAllClothesOnAstralEntry: applied={appliedCount} charas={charas.Count} frames={frameCount}");
            }
        }

        private IEnumerator ForceMaleSonInvisibleOnAstralEntry(int frameCount)
        {
            if (_hSceneProc == null || frameCount <= 0) yield break;

            ChaControl male = AccessTools.Field(typeof(HSceneProc), "male")?.GetValue(_hSceneProc) as ChaControl;
            ChaControl male1 = AccessTools.Field(typeof(HSceneProc), "male1")?.GetValue(_hSceneProc) as ChaControl;

            ChaControl[] males = new[] { male, male1 };

            int failureCount = 0;

            // 突入時に「今の表示」を一度だけオフにする。毎フレーム維持はしない。
            // visibleSonAlways はゲームコンフィグ(男根表示)に対応する持続フラグなので触らない
            //   = コンフィグON なら次フレームでゲームが戻して出せる / OFF ならそのまま消えたまま。
            // 以前は frameCount ループで両フラグを潰し続けていたため、コンフィグONでも出せなかった。
            foreach (ChaControl cha in males)
            {
                if (cha == null) continue;

                try
                {
                    cha.fileStatus.visibleSon = false;
                }
                catch
                {
                    failureCount++;
                }
            }

            SimpleFileLogger.Info($"ForceMaleSonInvisibleOnAstralEntry: applied once (visibleSon=false only, config respected), failures={failureCount}");
            yield break;
        }
    }
}
