using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace AstralMode
{
    // 現実Hの状態スナップショット & 復元（背骨）
    //   ① 保存: ワープ瞬間に現実キャラの全状態をキャッシュ
    //   ② リセット: アストラル入場時にクリーン化（汗/汁/頬赤/涙/感度/回数をゼロ）
    //      ※ベンチフェラ体位切替はHSceneProcが生きてないと処理されないため、凍結解除と
    //        セットで後段（プレイ可能化）で実装する。本v1は体位は触らない。
    //   ④ 復元: 帰還時にキャッシュを書き戻す（粗→細の順）
    //   詳細フィールドマップ: work/analysis/astral_state_snapshot_fields_2026-05-28.md
    public sealed partial class Plugin
    {
        private static readonly FieldInfo SnapLstUseAnimInfoField = AccessTools.Field(typeof(HSceneProc), "lstUseAnimInfo");
        private static readonly FieldInfo SnapHFlagNowAnimStateNameField = AccessTools.Field(typeof(HFlag), "nowAnimStateName");

        private ConfigEntry<bool> _cfgUseStateSnapshot;
        private ConfigEntry<bool> _cfgEnableEntryReset;
        private ConfigEntry<bool> _cfgSuppressHSceneLateUpdate;

        // アストラル中に HSceneProc.LateUpdate(=LateProc) を抑制するか。
        // 既定 false: in-place設計では抑制不要で、抑制するとH体位が立ち/Tポーズへ崩れるため。
        // true で旧挙動（テレポート版で必要だった引き戻し防止）に戻せる。
        internal bool SuppressHSceneLateUpdate
        {
            get
            {
                if (_cfgSuppressHSceneLateUpdate == null)
                {
                    _cfgSuppressHSceneLateUpdate = Config.Bind(
                        "Astral State",
                        "SuppressHSceneLateUpdate",
                        false,
                        "アストラル中に HSceneProc.LateUpdate(LateProc) を止めるか。既定false=止めない（H体位を維持）。trueにすると体が立ち/Tポーズへ崩れる（旧テレポート設計の名残）。");
                }
                return _cfgSuppressHSceneLateUpdate.Value;
            }
        }

        // 各キャラの状態（ChaFileStatus は純正Copyで丸取り）
        private sealed class RealCharaState
        {
            public ChaControl Cha;
            public ChaFileStatus Status;

            // siruLv は Status.Copy が参照共有のためライブと一緒に 0 化される。
            // 汁だけは捕獲時に独立 Clone で別配列として保持し、復元元はこちらを使う。
            public byte[] SavedSiruLv;

            public int AnimFullPathHash;
            public float AnimNormalizedTime;
            public float AnimSpeed;
            public string AnimClipName;
            public bool HasAnimState;

            public bool HasSkinEffectsSweatLevel;
            public int SavedSkinEffectsSweatLevel;
            public bool HasSkinEffectsTearLevel;
            public int SavedSkinEffectsTearLevel;
        }

        private readonly List<RealCharaState> _realCharaStates = new List<RealCharaState>();

        // シーン共通（HSceneProc/HFlag は1組）
        private bool _hasSavedSceneState;
        private int _savedPoseId;
        private HFlag.EMode _savedPoseMode;
        private string _savedPoseName;
        private string _savedNowAnimStateName;
        private float _savedGaugeFemale;
        private float _savedGaugeMale;
        private bool _savedLockGugeFemale;
        private bool _savedLockGugeMale;
        private HFlag.Count _savedCount;
        // アストラル入る瞬間にオート（自動で腰振り）だったかを保存。
        // sonyu系は内部 bool isAuto、houshi系は autoStart。両方とも非publicフィールド。
        private bool _savedAutoEnabled;

        private static Type _skinEffectsControllerType;
        private static PropertyInfo _skinEffectsSweatLevelProperty;
        private static PropertyInfo _skinEffectsTearLevelProperty;
        private static bool _skinEffectsApiReadyLogged;
        private static bool _skinEffectsApiMissingLogged;
        private static bool _skinEffectsComponentMissingLogged;

        private bool _astralCleanVisualsActive;
        private float _nextAstralCleanVisualsCheckTime;
        private float _nextAstralCleanVisualsLogTime;

        private bool UseStateSnapshot
        {
            get
            {
                if (_cfgUseStateSnapshot == null)
                {
                    _cfgUseStateSnapshot = Config.Bind(
                        "Astral State",
                        "UseStateSnapshot",
                        true,
                        "現実Hの状態を保存/復元する背骨を有効化（汗/汁/頬赤/涙/感度/回数）。ONでcapture/reset/restoreを実行する。");
                }
                return _cfgUseStateSnapshot.Value;
            }
        }

        // アストラル入場時のクリーン化（汗/汁/頬赤/涙/感度/回数ゼロ）。
        // 既定OFF: HSceneProc が半分生きてる状態でHFlagを触ると体位がリセットされ崩れるため、
        //         凍結解除＋ベンチフェラ切替を実装するまで封印。
        private bool EnableEntryReset
        {
            get
            {
                if (_cfgEnableEntryReset == null)
                {
                    _cfgEnableEntryReset = Config.Bind(
                        "Astral State",
                        "EnableEntryReset",
                        true,
                        "アストラル入場時のクリーン化（汗/汁/頬赤/涙/感度/回数ゼロ）。");
                }
                return _cfgEnableEntryReset.Value;
            }
        }

        // ====================================================================
        // ① 保存
        // ====================================================================
        internal void CaptureRealStateForAstral()
        {
            _realCharaStates.Clear();
            _hasSavedSceneState = false;

            if (!UseStateSnapshot)
            {
                SimpleFileLogger.Info("StateSnapshot capture skipped: UseStateSnapshot=false");
                return;
            }

            try
            {
                var charas = CollectCurrentHSceneCharas();
                foreach (var cha in charas)
                {
                    if (cha == null) continue;
                    var st = new RealCharaState { Cha = cha };
                    try
                    {
                        var copy = new ChaFileStatus();
                        copy.Copy(cha.fileStatus);
                        st.Status = copy;

                        // ChaFileStatus.Copy は siruLv を参照コピーするため、後段のリセット/KKS処理で
                        // st.Status.siruLv が一緒に 0 化される。独立 Clone で隔離保存する。
                        st.SavedSiruLv = cha.fileStatus.siruLv != null
                            ? (byte[])cha.fileStatus.siruLv.Clone()
                            : null;
                        SimpleFileLogger.Warn($"StateSnapshot: SavedSiruLv cha={cha.name} values=[{(st.SavedSiruLv != null ? string.Join(",", st.SavedSiruLv) : "null")}]");

                        if (TryGetSkinEffectsSweatLevel(cha, out int sweatLevel))
                        {
                            st.HasSkinEffectsSweatLevel = true;
                            st.SavedSkinEffectsSweatLevel = sweatLevel;
                            SimpleFileLogger.Warn($"StateSnapshot: SkinEffectsSweat saved cha={cha.name} level={sweatLevel}");
                        }
                        if (TryGetSkinEffectsTearLevel(cha, out int tearLevel))
                        {
                            st.HasSkinEffectsTearLevel = true;
                            st.SavedSkinEffectsTearLevel = tearLevel;
                            SimpleFileLogger.Warn($"StateSnapshot: SkinEffectsTear saved cha={cha.name} level={tearLevel}");
                        }
                    }
                    catch (Exception ex)
                    {
                        SimpleFileLogger.Warn($"StateSnapshot: fileStatus copy failed for {cha.name}: {ex.Message}");
                    }
                    try
                    {
                        Animator anim = cha.animBody;
                        if (anim != null && anim.runtimeAnimatorController != null)
                        {
                            var asi = anim.GetCurrentAnimatorStateInfo(0);
                            st.AnimFullPathHash = asi.fullPathHash;
                            st.AnimNormalizedTime = asi.normalizedTime;
                            st.AnimSpeed = anim.speed;
                            st.HasAnimState = true;

                            string clip = "none";
                            try
                            {
                                var clips = anim.GetCurrentAnimatorClipInfo(0);
                                if (clips != null && clips.Length > 0 && clips[0].clip != null)
                                    clip = clips[0].clip.name;
                            }
                            catch { }

                            st.AnimClipName = clip;

                            SimpleFileLogger.Warn($"StateSnapshot: animator saved cha={cha.name} clip={clip} hash={st.AnimFullPathHash} nt={st.AnimNormalizedTime:F3} speed={st.AnimSpeed:F2}");
                        }
                    }
                    catch (Exception ex)
                    {
                        SimpleFileLogger.Warn($"StateSnapshot: animator state copy failed for {cha.name}: {ex.Message}");
                    }

                    _realCharaStates.Add(st);
                }

                // シーン共通: 体位 + 感度 + 回数
                HFlag flags = _hFlag;
                if (flags != null)
                {
                    HSceneProc.AnimationListInfo now = flags.nowAnimationInfo;
                    if (now != null)
                    {
                        _savedPoseId = now.id;
                        _savedPoseMode = now.mode;
                        _savedPoseName = now.nameAnimation;
                    }
                    _savedNowAnimStateName = ReadHFlagNowAnimStateName(flags);
                    _savedGaugeFemale = flags.gaugeFemale;
                    _savedGaugeMale = flags.gaugeMale;
                    _savedLockGugeFemale = flags.lockGugeFemale;
                    _savedLockGugeMale = flags.lockGugeMale;
                    _savedCount = CloneCount(flags.count);
                    _savedAutoEnabled = ReadCurrentActionAuto(_savedPoseMode);
                    _hasSavedSceneState = true;
                }

                SimpleFileLogger.Info($"StateSnapshot captured: charas={_realCharaStates.Count}, pose=(id={_savedPoseId},mode={_savedPoseMode},name={_savedPoseName}), nowAnim={_savedNowAnimStateName}, gaugeF={_savedGaugeFemale:F2}, gaugeM={_savedGaugeMale:F2}, auto={_savedAutoEnabled}");
                LogRealStateDiff("after-capture");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"StateSnapshot capture failed: {ex.Message}");
            }
        }

        // ====================================================================
        // ② 入場リセット（クリーン化のみ。体位切替は凍結解除とセットで後段）
        // ====================================================================
        internal void ResetCharasForAstralClean()
        {
            if (!UseStateSnapshot) return;
            if (!EnableEntryReset)
            {
                SimpleFileLogger.Info("StateReset skipped: EnableEntryReset=false（凍結中のクリーン化は体位を崩すため封印中）");
                return;
            }

            try
            {
                Array siruParts = Enum.GetValues(typeof(ChaFileDefine.SiruParts));
                foreach (var st in _realCharaStates)
                {
                    ChaControl cha = st != null ? st.Cha : null;
                    if (cha == null) continue;
                    try
                    {
                        // 汁クリア
                        foreach (ChaFileDefine.SiruParts part in siruParts)
                            cha.SetSiruFlags(part, 0);
                        // 汗/肌ツヤクリア
                        cha.fileStatus.skinTuyaRate = 0f;
                        cha.ChangeSettingSkinGlossPower();
                        // KKS_SkinEffects の Additional skin effects > Sweat クリア
                        if (TrySetSkinEffectsSweatLevel(cha, 0))
                            SimpleFileLogger.Warn($"StateReset: SkinEffectsSweat zeroed cha={cha.name}");
                        // 頬赤クリア
                        cha.ChangeHohoAkaRate(0f);
                        // 涙クリア
                        cha.fileStatus.tearsLv = 0;
                        cha.tearsLv = 0;
                        // KKS_SkinEffects の Additional skin effects > Tears クリア
                        if (TrySetSkinEffectsTearLevel(cha, 0))
                            SimpleFileLogger.Warn($"StateReset: SkinEffectsTear zeroed cha={cha.name}");
                    }
                    catch (Exception ex)
                    {
                        SimpleFileLogger.Warn($"StateReset: clean failed for {cha.name}: {ex.Message}");
                    }
                }

                // 感度・回数ゼロ
                if (_hFlag != null)
                {
                    _hFlag.gaugeFemale = 0f;
                    _hFlag.gaugeMale = 0f;
                    _hFlag.count = new HFlag.Count();
                }

                SimpleFileLogger.Info("StateReset: clean done (siru/sweat/blush/tears/gauge/count zeroed)");
                LogRealStateDiff("after-reset");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"StateReset failed: {ex.Message}");
            }
        }

        // ====================================================================
        // ④ 帰還・復元（粗→細）
        // ====================================================================
        internal void RestoreRealStateFromAstral()
        {
            if (!UseStateSnapshot) return;
            // EnableEntryReset=false でも復元する。
            // アストラル中に ForceAstralPose / ChangeAnimator で体位やHFlagが変わるため、
            // 「入場リセットしてないから復元不要」ではない。
            if (!_hasSavedSceneState && _realCharaStates.Count == 0)
            {
                SimpleFileLogger.Info("StateRestore skipped: nothing saved");
                return;
            }

            LogRealStateDiff("before-restore");

            // 1) ベース体位を保存時のものへ戻す
            RestoreSavedPose();
            RestoreSavedNowAnimStateName("after-RestoreSavedPose");

            // 2) IK（HipHijack）: TODO 外部API経由で apply

            // 3) キャラ状態: fileStatus 書き戻し + 再適用
            try
            {
                Array siruParts = Enum.GetValues(typeof(ChaFileDefine.SiruParts));
                Array clothesKinds = Enum.GetValues(typeof(ChaFileDefine.ClothesKind));
                foreach (var st in _realCharaStates)
                {
                    ChaControl cha = st != null ? st.Cha : null;
                    if (cha == null || st.Status == null) continue;
                    try
                    {
                        // キャラ見た目の反映はここでは行わない。
                        // 体位・モーション・オート復元が終わった後、帰還処理の最後でまとめて戻す。
                        // 対象: 頬赤 → 肌ツヤ → 服状態 → 眉 → 目 → 口 → 目開き → 口開き → 汁
                    }
                    catch (Exception ex)
                    {
                        SimpleFileLogger.Warn($"StateRestore: chara restore failed for {cha.name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"StateRestore: chara loop failed: {ex.Message}");
            }

            // 4) 感度・回数
            try
            {
                if (_hasSavedSceneState && _hFlag != null)
                {
                    _hFlag.gaugeFemale = _savedGaugeFemale;
                    _hFlag.gaugeMale = _savedGaugeMale;
                    _hFlag.lockGugeFemale = _savedLockGugeFemale;
                    _hFlag.lockGugeMale = _savedLockGugeMale;
                    if (_savedCount != null) _hFlag.count = _savedCount;
                    RestoreSavedNowAnimStateName("after-HFlag-restore");
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"StateRestore: HFlag restore failed: {ex.Message}");
            }

            SimpleFileLogger.Info("StateRestore done");
            LogRealStateDiff("after-restore");

            // Do not clear saved state here.
            // Plugin.Warp.cs calls ClearSavedRealState() after delayed animator motion restore.
        }

        private static string ReadHFlagNowAnimStateName(HFlag flags)
        {
            if (flags == null || SnapHFlagNowAnimStateNameField == null)
                return null;

            try
            {
                object value = SnapHFlagNowAnimStateNameField.GetValue(flags);
                return value != null ? value.ToString() : null;
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"ReadHFlagNowAnimStateName failed: {ex.Message}");
                return null;
            }
        }

        private void RestoreSavedNowAnimStateName(string phase)
        {
            if (_hFlag == null || SnapHFlagNowAnimStateNameField == null)
                return;

            if (string.IsNullOrWhiteSpace(_savedNowAnimStateName))
                return;

            try
            {
                string before = ReadHFlagNowAnimStateName(_hFlag);
                if (before == _savedNowAnimStateName)
                    return;

                SnapHFlagNowAnimStateNameField.SetValue(_hFlag, _savedNowAnimStateName);
                string after = ReadHFlagNowAnimStateName(_hFlag);
                SimpleFileLogger.Warn($"StateRestore: nowAnimStateName restored phase={phase} before={before} after={after}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"RestoreSavedNowAnimStateName failed phase={phase}: {ex.Message}");
            }
        }

        // 帰還時、保存スナップショットの speed/speedCalc/motion を HFlag に書き戻す。
        // これをやらないと帰還直後 speedCalc=0 のまま H 本体が SLoop → InsertIdle に脱落する。
        private void RestoreSavedHFlagDriveFields(string phase)
        {
            if (_hFlag == null) return;
            if (_savedHInternalState == null || _savedHInternalState.Count == 0) return;

            try
            {
                bool wroteSpeed = TryReadSavedFloat("HFlag.speed", out float savedSpeed);
                bool wroteSpeedCalc = TryReadSavedFloat("HFlag.speedCalc", out float savedSpeedCalc);
                bool wroteMotion = TryReadSavedFloat("HFlag.motion", out float savedMotion);

                if (wroteSpeed) _hFlag.speed = savedSpeed;
                if (wroteSpeedCalc) _hFlag.speedCalc = savedSpeedCalc;
                if (wroteMotion) _hFlag.motion = savedMotion;

                SimpleFileLogger.Warn(
                    $"StateRestore: HFlag drive restored phase={phase}"
                    + $" speed={(wroteSpeed ? _hFlag.speed.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "skip")}"
                    + $" speedCalc={(wroteSpeedCalc ? _hFlag.speedCalc.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "skip")}"
                    + $" motion={(wroteMotion ? _hFlag.motion.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) : "skip")}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"RestoreSavedHFlagDriveFields failed phase={phase}: {ex.Message}");
            }
        }

        private bool TryReadSavedFloat(string key, out float value)
        {
            value = 0f;
            if (_savedHInternalState == null) return false;
            if (!_savedHInternalState.TryGetValue(key, out string s)) return false;
            return float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        // 現在の体位クラス（sonyu系/houshi系）からオート状態を読む。
        // sonyu系: 非public bool isAuto
        // houshi系: 非public bool autoStart
        private bool ReadCurrentActionAuto(HFlag.EMode mode)
        {
            try
            {
                HActionBase action = ResolveActionForMode(mode);
                if (action == null) return false;

                string fieldName = AutoFieldNameForMode(mode);
                if (fieldName == null) return false;

                FieldInfo f = action.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (f == null || f.FieldType != typeof(bool)) return false;

                return (bool)f.GetValue(action);
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"ReadCurrentActionAuto failed mode={mode}: {ex.Message}");
                return false;
            }
        }

        internal void WriteCurrentActionAuto(HFlag.EMode mode, bool value, string phase)
        {
            try
            {
                HActionBase action = ResolveActionForMode(mode);
                if (action == null)
                {
                    SimpleFileLogger.Warn($"WriteCurrentActionAuto skipped phase={phase} mode={mode}: action=null");
                    return;
                }

                string fieldName = AutoFieldNameForMode(mode);
                if (fieldName == null)
                {
                    SimpleFileLogger.Warn($"WriteCurrentActionAuto skipped phase={phase} mode={mode}: no field for mode");
                    return;
                }

                FieldInfo f = action.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (f == null || f.FieldType != typeof(bool))
                {
                    SimpleFileLogger.Warn($"WriteCurrentActionAuto skipped phase={phase} mode={mode}: field {fieldName} not found");
                    return;
                }

                f.SetValue(action, value);
                SimpleFileLogger.Warn($"AutoRestore: phase={phase} mode={mode} field={fieldName} value={value}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"WriteCurrentActionAuto failed phase={phase} mode={mode}: {ex.Message}");
            }
        }

        private HActionBase ResolveActionForMode(HFlag.EMode mode)
        {
            if (_hSceneProc == null || HActionFixLstProcField == null) return null;
            IList actions = HActionFixLstProcField.GetValue(_hSceneProc) as IList;
            int modeIndex = (int)mode;
            if (actions == null || modeIndex < 0 || modeIndex >= actions.Count) return null;
            return actions[modeIndex] as HActionBase;
        }

        private static string AutoFieldNameForMode(HFlag.EMode mode)
        {
            switch (mode)
            {
                case HFlag.EMode.sonyu:
                case HFlag.EMode.sonyu3P:
                case HFlag.EMode.sonyu3PMMF:
                    return "isAuto";
                case HFlag.EMode.houshi:
                case HFlag.EMode.houshi3P:
                case HFlag.EMode.houshi3PMMF:
                    return "autoStart";
                default:
                    return null;
            }
        }

        private void RestoreSavedPose()
        {
            if (!_hasSavedSceneState || _hSceneProc == null)
            {
                SimpleFileLogger.Info("RestoreSavedPose skipped: no saved scene state or HSceneProc null");
                return;
            }

            try
            {
                var lists = SnapLstUseAnimInfoField != null
                    ? SnapLstUseAnimInfoField.GetValue(_hSceneProc) as List<HSceneProc.AnimationListInfo>[]
                    : null;

                if (lists == null)
                {
                    SimpleFileLogger.Warn("RestoreSavedPose: lstUseAnimInfo not found");
                    return;
                }

                // houshi 等は mode 内で id が一意でない（MOD体位と vanilla体位で id が衝突する）。
                // id+mode だけだと別ポーズに当たるため、nameAnimation 完全一致を最優先で判別する。
                HSceneProc.AnimationListInfo found = null;          // id+mode+name 完全一致
                HSceneProc.AnimationListInfo idModeFallback = null; // id+mode のみ（後方互換フォールバック）

                for (int i = 0; i < lists.Length; i++)
                {
                    var list = lists[i];
                    if (list == null) continue;

                    for (int j = 0; j < list.Count; j++)
                    {
                        var c = list[j];
                        if (c == null) continue;
                        if (c.id != _savedPoseId || c.mode != _savedPoseMode) continue;

                        if (idModeFallback == null) idModeFallback = c;

                        if (!string.IsNullOrEmpty(_savedPoseName)
                            && string.Equals(c.nameAnimation, _savedPoseName, StringComparison.Ordinal))
                        {
                            found = c;
                            break;
                        }
                    }

                    if (found != null) break;
                }

                bool usedNameMatch = found != null;
                if (found == null) found = idModeFallback;

                if (found == null)
                {
                    SimpleFileLogger.Warn($"RestoreSavedPose: saved pose not found id={_savedPoseId} mode={_savedPoseMode} name={_savedPoseName}");
                    return;
                }

                _hFlag.selectAnimationListInfo = found;
                _hFlag.click = HFlag.ClickKind.actionChange;

                SimpleFileLogger.Warn($"RestoreSavedPose: actionChange -> id={found.id} mode={found.mode} name={found.nameAnimation} (savedName={_savedPoseName}, match={(usedNameMatch ? "id+mode+name" : "id+mode-fallback")})");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"RestoreSavedPose failed: {ex.Message}");
            }
        }

        internal IEnumerator RestoreSavedAnimatorMotionDelayed(int waitFrames)
        {
            SimpleFileLogger.Warn($"StateRestore: animator delayed restore begin waitFrames={waitFrames} savedCount={_realCharaStates.Count}");

            for (int i = 0; i < waitFrames; i++)
                yield return null;

            RestoreSavedAnimatorMotionNow("delayed-0f");
            LogCurrentAnimatorMotion("after-delayed-0f");

            yield return null;
            RestoreSavedAnimatorMotionNow("delayed-1f");
            LogCurrentAnimatorMotion("after-delayed-1f");

            for (int i = 0; i < 4; i++)
                yield return null;
            RestoreSavedAnimatorMotionNow("delayed-5f");
            LogCurrentAnimatorMotion("after-delayed-5f");

            for (int i = 0; i < 10; i++)
                yield return null;
            LogCurrentAnimatorMotion("after-delayed-15f-no-replay");
        }

        private void RestoreSavedAnimatorMotionNow(string phase)
        {
            int restored = 0;
            int skipped = 0;

            try
            {
                SimpleFileLogger.Warn($"StateRestore: animator motion restore enter phase={phase} savedCount={_realCharaStates.Count}");
                RestoreSavedNowAnimStateName("anim-restore-enter:" + phase);

                foreach (var st in _realCharaStates)
                {
                    ChaControl cha = st != null ? st.Cha : null;
                    if (cha == null)
                    {
                        skipped++;
                        SimpleFileLogger.Warn($"StateRestore: animator motion skip {phase} reason=cha-null");
                        continue;
                    }

                    if (!st.HasAnimState)
                    {
                        skipped++;
                        SimpleFileLogger.Warn($"StateRestore: animator motion skip {phase} cha={cha.name} reason=no-saved-anim-state");
                        continue;
                    }

                    Animator anim = cha.animBody;
                    if (anim == null)
                    {
                        skipped++;
                        SimpleFileLogger.Warn($"StateRestore: animator motion skip {phase} cha={cha.name} reason=anim-null");
                        continue;
                    }

                    if (anim.runtimeAnimatorController == null)
                    {
                        skipped++;
                        SimpleFileLogger.Warn($"StateRestore: animator motion skip {phase} cha={cha.name} reason=controller-null");
                        continue;
                    }

                    LogAnimatorMotionOne(phase + ":before-play", cha, anim, st);

                    anim.speed = st.AnimSpeed;
                    anim.Play(st.AnimFullPathHash, 0, st.AnimNormalizedTime);
                    anim.Update(0f);
                    RestoreSavedNowAnimStateName("anim-after-play:" + phase);

                    restored++;
                    LogAnimatorMotionOne(phase + ":after-play", cha, anim, st);
                    SimpleFileLogger.Warn($"StateRestore: animator motion restored {phase} for {cha.name} savedClip={st.AnimClipName} savedHash={st.AnimFullPathHash} savedNt={st.AnimNormalizedTime:F3} savedSpeed={st.AnimSpeed:F2}");
                }

                RestoreSavedNowAnimStateName("anim-restore-done:" + phase);
                SimpleFileLogger.Warn($"StateRestore: animator motion restore done phase={phase} restored={restored} skipped={skipped} savedCount={_realCharaStates.Count}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"StateRestore: delayed animator restore failed phase={phase} restored={restored} skipped={skipped} error={ex}");
            }
        }

        private void LogCurrentAnimatorMotion(string phase)
        {
            try
            {
                SimpleFileLogger.Warn($"StateRestore: animator motion probe phase={phase} savedCount={_realCharaStates.Count}");

                foreach (var st in _realCharaStates)
                {
                    ChaControl cha = st != null ? st.Cha : null;
                    Animator anim = cha != null ? cha.animBody : null;
                    LogAnimatorMotionOne(phase, cha, anim, st);
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"StateRestore: animator motion probe failed phase={phase} error={ex}");
            }
        }

        private void LogAnimatorMotionOne(string phase, ChaControl cha, Animator anim, RealCharaState saved)
        {
            try
            {
                if (cha == null)
                {
                    SimpleFileLogger.Warn($"ANIM_STATE phase={phase} cha=null");
                    return;
                }

                if (anim == null)
                {
                    SimpleFileLogger.Warn($"ANIM_STATE phase={phase} cha={cha.name} anim=null");
                    return;
                }

                string clip = "none";
                try
                {
                    var clips = anim.GetCurrentAnimatorClipInfo(0);
                    if (clips != null && clips.Length > 0 && clips[0].clip != null)
                        clip = clips[0].clip.name;
                }
                catch { }

                AnimatorStateInfo asi = anim.GetCurrentAnimatorStateInfo(0);

                int savedHash = saved != null ? saved.AnimFullPathHash : 0;
                float savedNt = saved != null ? saved.AnimNormalizedTime : 0f;
                string savedClip = saved != null ? saved.AnimClipName : "null";
                bool sameHash = saved != null && asi.fullPathHash == saved.AnimFullPathHash;

                HSceneProc.AnimationListInfo now = _hFlag != null ? _hFlag.nowAnimationInfo : null;

                SimpleFileLogger.Warn(
                    $"ANIM_STATE phase={phase}"
                    + $" cha={cha.name}"
                    + $" currentClip={clip}"
                    + $" currentHash={asi.fullPathHash}"
                    + $" currentNt={asi.normalizedTime:F3}"
                    + $" currentSpeed={anim.speed:F2}"
                    + $" savedClip={savedClip}"
                    + $" savedHash={savedHash}"
                    + $" savedNt={savedNt:F3}"
                    + $" sameHash={sameHash}"
                    + $" poseId={(now != null ? now.id : -1)}"
                    + $" poseMode={(now != null ? now.mode.ToString() : "?")}"
                    + $" poseName={(now != null ? now.nameAnimation : "null")}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"ANIM_STATE failed phase={phase} cha={(cha != null ? cha.name : "null")} error={ex.Message}");
            }
        }

        // 帰還の最終段でキャラ見た目を戻す。
        // 体位・モーション・オート復元の後にKKS側が顔/服/涙/汁を上書きするため、見た目は最後に反映する。
        internal void RestoreSavedCharaVisualsFinal(string phase)
        {
            if (!UseStateSnapshot) return;
            if (_realCharaStates == null || _realCharaStates.Count == 0) return;

            try
            {
                Array clothesKinds = Enum.GetValues(typeof(ChaFileDefine.ClothesKind));
                Array siruParts = Enum.GetValues(typeof(ChaFileDefine.SiruParts));

                foreach (ChaFileDefine.SiruParts p in siruParts)
                    SimpleFileLogger.Warn($"StateRestore: SiruParts enum {(int)p}={p}");

                int restored = 0;
                int skipped = 0;

                foreach (var st in _realCharaStates)
                {
                    ChaControl cha = st != null ? st.Cha : null;
                    if (cha == null || st.Status == null)
                    {
                        skipped++;
                        continue;
                    }

                    // 1. 頬赤
                    cha.ChangeHohoAkaRate(st.Status.hohoAkaRate);

                    // 2. 肌ツヤ
                    cha.fileStatus.skinTuyaRate = st.Status.skinTuyaRate;
                    cha.ChangeSettingSkinGlossPower();
                    if (st.HasSkinEffectsSweatLevel)
                    {
                        bool ok = TrySetSkinEffectsSweatLevel(cha, st.SavedSkinEffectsSweatLevel);
                        SimpleFileLogger.Warn($"StateRestore: SkinEffectsSweat restored phase={phase} cha={cha.name} level={st.SavedSkinEffectsSweatLevel} ok={ok}");
                    }
                    if (st.HasSkinEffectsTearLevel)
                    {
                        bool ok = TrySetSkinEffectsTearLevel(cha, st.SavedSkinEffectsTearLevel);
                        SimpleFileLogger.Warn($"StateRestore: SkinEffectsTear restored phase={phase} cha={cha.name} level={st.SavedSkinEffectsTearLevel} ok={ok}");
                    }

                    // 3. 通常涙
                    cha.fileStatus.tearsLv = st.Status.tearsLv;
                    cha.tearsLv = st.Status.tearsLv;

                    // 4. 服状態
                    if (st.Status.clothesState != null)
                    {
                        foreach (ChaFileDefine.ClothesKind kind in clothesKinds)
                        {
                            int idx = (int)kind;
                            if (idx < 0 || idx >= st.Status.clothesState.Length) continue;
                            cha.SetClothesState(idx, st.Status.clothesState[idx]);
                        }
                    }

                    // 男根表示の復元
                    cha.fileStatus.visibleSon = st.Status.visibleSon;
                    cha.fileStatus.visibleSonAlways = st.Status.visibleSonAlways;

                    // 5. 眉
                    cha.ChangeEyebrowPtn(st.Status.eyebrowPtn, true);

                    // 6. 目
                    cha.ChangeEyesPtn(st.Status.eyesPtn, true);

                    // 7. 口
                    cha.ChangeMouthPtn(st.Status.mouthPtn, true);

                    // 8. 目開き
                    cha.ChangeEyesOpenMax(st.Status.eyesOpenMax);

                    // 9. 口開き
                    cha.ChangeMouthOpenMax(st.Status.mouthOpenMax);

                    // 10. 汁。これは最後。
                    //   復元元は st.Status.siruLv ではなく st.SavedSiruLv（独立 Clone）を使う。
                    //   st.Status.siruLv は ChaFileStatus.Copy の参照共有のせいで 0 に書き換わっている可能性がある。
                    //
                    //   重要：fileStatus.siruLv には直接書かない。
                    //   SetSiruFlags は siruNewLv にしか書かない設計で、KKS の UpdateSiru が後で
                    //   「fileStatus.siruLv != siruNewLv」の差分検知をして体マテリアルに反映する。
                    //   ここで fileStatus.siruLv を先に書くと差分が消えて UpdateSiru が体を更新しなくなる。
                    byte[] savedSiru = st.SavedSiruLv;
                    SimpleFileLogger.Warn($"SiruRestore phase={phase} cha={cha.name} source=SavedSiruLv values=[{(savedSiru != null ? string.Join(",", savedSiru) : "null")}]");
                    if (savedSiru != null)
                    {
                        foreach (ChaFileDefine.SiruParts part in siruParts)
                        {
                            int idx = (int)part;
                            if (idx < 0 || idx >= savedSiru.Length) continue;

                            cha.SetSiruFlags(part, savedSiru[idx]);
                        }
                    }

                    restored++;
                }

                SimpleFileLogger.Warn($"StateRestore: final chara visuals restored phase={phase} restored={restored} skipped={skipped}");
                LogRealStateDiff("after-final-visuals");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"StateRestore: final chara visuals restore failed phase={phase}: {ex}");
            }
        }

        internal void ClearSavedRealState()
        {
            _realCharaStates.Clear();
            _hasSavedSceneState = false;
            _savedNowAnimStateName = null;
            _savedCount = null;
            SetAstralCleanVisualsActive(false, "clear-saved-state");
        }

        internal void SetAstralCleanVisualsActive(bool active, string phase)
        {
            if (_astralCleanVisualsActive == active) return;
            _astralCleanVisualsActive = active;
            _nextAstralCleanVisualsCheckTime = 0f;
            _nextAstralCleanVisualsLogTime = 0f;
            SimpleFileLogger.Info($"AstralCleanVisuals active={active} phase={phase}");
        }

        private void MaintainAstralCleanVisualState()
        {
            if (!_astralCleanVisualsActive) return;
            if (!UseStateSnapshot || !EnableEntryReset) return;
            if (_state == AstralState.Normal || _state == AstralState.ReturnHold || _state == AstralState.ReturnFadeOut)
                return;

            float now = Time.unscaledTime;
            if (now < _nextAstralCleanVisualsCheckTime) return;
            _nextAstralCleanVisualsCheckTime = now + 0.05f;

            try
            {
                int charaCount = 0;
                int vanillaTearFixCount = 0;
                int glossFixCount = 0;
                int blushFixCount = 0;
                int skinSweatFixCount = 0;
                int skinTearFixCount = 0;

                if (_realCharaStates != null && _realCharaStates.Count > 0)
                {
                    foreach (var st in _realCharaStates)
                    {
                        ChaControl cha = st != null ? st.Cha : null;
                        if (cha == null) continue;
                        charaCount++;
                        ApplyAstralCleanVisualsToChara(
                            cha,
                            ref vanillaTearFixCount,
                            ref glossFixCount,
                            ref blushFixCount,
                            ref skinSweatFixCount,
                            ref skinTearFixCount);
                    }
                }
                else
                {
                    foreach (ChaControl cha in CollectCurrentHSceneCharas())
                    {
                        if (cha == null) continue;
                        charaCount++;
                        ApplyAstralCleanVisualsToChara(
                            cha,
                            ref vanillaTearFixCount,
                            ref glossFixCount,
                            ref blushFixCount,
                            ref skinSweatFixCount,
                            ref skinTearFixCount);
                    }
                }

                bool changed = vanillaTearFixCount > 0 || glossFixCount > 0
                    || blushFixCount > 0 || skinSweatFixCount > 0 || skinTearFixCount > 0;
                if (changed && Time.unscaledTime >= _nextAstralCleanVisualsLogTime)
                {
                    _nextAstralCleanVisualsLogTime = Time.unscaledTime + 2f;
                    SimpleFileLogger.Info(
                        $"AstralCleanVisuals maintain charas={charaCount}"
                        + $" tears={vanillaTearFixCount}"
                        + $" gloss={glossFixCount} blush={blushFixCount}"
                        + $" skinSweat={skinSweatFixCount} skinTear={skinTearFixCount}");
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"AstralCleanVisuals maintain failed: {ex.Message}");
            }
        }

        private static void ApplyAstralCleanVisualsToChara(
            ChaControl cha,
            ref int vanillaTearFixCount,
            ref int glossFixCount,
            ref int blushFixCount,
            ref int skinSweatFixCount,
            ref int skinTearFixCount)
        {
            if (cha == null) return;

            try
            {
                if (cha.fileStatus != null)
                {
                    if (Mathf.Abs(cha.fileStatus.skinTuyaRate) > 0.0001f)
                    {
                        cha.fileStatus.skinTuyaRate = 0f;
                        cha.ChangeSettingSkinGlossPower();
                        glossFixCount++;
                    }

                    if (Mathf.Abs(cha.fileStatus.hohoAkaRate) > 0.0001f)
                    {
                        cha.ChangeHohoAkaRate(0f);
                        blushFixCount++;
                    }

                    if (cha.fileStatus.tearsLv != 0)
                    {
                        cha.fileStatus.tearsLv = 0;
                        vanillaTearFixCount++;
                    }
                }

                if (cha.tearsLv != 0)
                {
                    cha.tearsLv = 0;
                    vanillaTearFixCount++;
                }

                if (TryGetSkinEffectsSweatLevel(cha, out int sweatLevel) && sweatLevel != 0
                    && TrySetSkinEffectsSweatLevel(cha, 0))
                {
                    skinSweatFixCount++;
                }

                if (TryGetSkinEffectsTearLevel(cha, out int tearLevel) && tearLevel != 0
                    && TrySetSkinEffectsTearLevel(cha, 0))
                {
                    skinTearFixCount++;
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"AstralCleanVisuals chara failed cha={(cha != null ? cha.name : "null")}: {ex.Message}");
            }
        }

        private static bool EnsureSkinEffectsApi(bool requireSweat, bool requireTear)
        {
            try
            {
                if (_skinEffectsControllerType == null)
                    _skinEffectsControllerType = AccessTools.TypeByName("KK_SkinEffects.SkinEffectsController");

                if (_skinEffectsControllerType != null)
                {
                    if (_skinEffectsSweatLevelProperty == null)
                        _skinEffectsSweatLevelProperty = AccessTools.Property(_skinEffectsControllerType, "SweatLevel");
                    if (_skinEffectsTearLevelProperty == null)
                        _skinEffectsTearLevelProperty = AccessTools.Property(_skinEffectsControllerType, "TearLevel");
                }

                bool ok = _skinEffectsControllerType != null
                    && (!requireSweat || _skinEffectsSweatLevelProperty != null)
                    && (!requireTear || _skinEffectsTearLevelProperty != null);

                if (ok && !_skinEffectsApiReadyLogged)
                {
                    _skinEffectsApiReadyLogged = true;
                    SimpleFileLogger.Info(
                        $"SkinEffectsApi ready type={_skinEffectsControllerType.FullName}"
                        + $" sweat={(_skinEffectsSweatLevelProperty != null)} tear={(_skinEffectsTearLevelProperty != null)}");
                }
                else if (!ok && !_skinEffectsApiMissingLogged)
                {
                    _skinEffectsApiMissingLogged = true;
                    SimpleFileLogger.Warn(
                        $"SkinEffectsApi not ready type={(_skinEffectsControllerType != null ? _skinEffectsControllerType.FullName : "null")}"
                        + $" sweat={(_skinEffectsSweatLevelProperty != null)} tear={(_skinEffectsTearLevelProperty != null)}");
                }

                return ok;
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"SkinEffectsApi lookup failed: {ex.Message}");
                return false;
            }
        }

        private static Component GetSkinEffectsComponent(ChaControl cha, bool requireSweat, bool requireTear)
        {
            if (cha == null || !EnsureSkinEffectsApi(requireSweat, requireTear))
                return null;

            Component component = cha.GetComponent(_skinEffectsControllerType);
            if (component == null && !_skinEffectsComponentMissingLogged)
            {
                _skinEffectsComponentMissingLogged = true;
                SimpleFileLogger.Warn($"SkinEffectsApi component missing cha={cha.name} type={_skinEffectsControllerType.FullName}");
            }
            return component;
        }

        private static bool TryGetSkinEffectsSweatLevel(ChaControl cha, out int level)
        {
            level = 0;
            try
            {
                Component component = GetSkinEffectsComponent(cha, requireSweat: true, requireTear: false);
                if (component == null) return false;

                object value = _skinEffectsSweatLevelProperty.GetValue(component, null);
                if (value is int i)
                {
                    level = i;
                    return true;
                }
                if (value != null)
                {
                    level = Convert.ToInt32(value);
                    return true;
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"SkinEffectsSweat read failed cha={(cha != null ? cha.name : "null")}: {ex.Message}");
            }

            return false;
        }

        private static bool TrySetSkinEffectsSweatLevel(ChaControl cha, int level)
        {
            try
            {
                Component component = GetSkinEffectsComponent(cha, requireSweat: true, requireTear: false);
                if (component == null || _skinEffectsSweatLevelProperty == null || !_skinEffectsSweatLevelProperty.CanWrite)
                    return false;

                _skinEffectsSweatLevelProperty.SetValue(component, level, null);
                return true;
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"SkinEffectsSweat write failed cha={(cha != null ? cha.name : "null")} level={level}: {ex.Message}");
                return false;
            }
        }

        private static bool TryGetSkinEffectsTearLevel(ChaControl cha, out int level)
        {
            level = 0;
            try
            {
                Component component = GetSkinEffectsComponent(cha, requireSweat: false, requireTear: true);
                if (component == null) return false;

                object value = _skinEffectsTearLevelProperty.GetValue(component, null);
                if (value is int i)
                {
                    level = i;
                    return true;
                }
                if (value != null)
                {
                    level = Convert.ToInt32(value);
                    return true;
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"SkinEffectsTear read failed cha={(cha != null ? cha.name : "null")}: {ex.Message}");
            }

            return false;
        }

        private static bool TrySetSkinEffectsTearLevel(ChaControl cha, int level)
        {
            try
            {
                Component component = GetSkinEffectsComponent(cha, requireSweat: false, requireTear: true);
                if (component == null || _skinEffectsTearLevelProperty == null || !_skinEffectsTearLevelProperty.CanWrite)
                    return false;

                _skinEffectsTearLevelProperty.SetValue(component, level, null);
                return true;
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"SkinEffectsTear write failed cha={(cha != null ? cha.name : "null")} level={level}: {ex.Message}");
                return false;
            }
        }

        // ====================================================================
        // 検証用 diff ログ
        // ====================================================================
        private void LogRealStateDiff(string phase)
        {
            try
            {
                for (int i = 0; i < _realCharaStates.Count; i++)
                {
                    ChaControl cha = _realCharaStates[i] != null ? _realCharaStates[i].Cha : null;
                    if (cha == null) continue;
                    ChaFileStatus s = cha.fileStatus;
                    string siru = s.siruLv != null ? string.Join(",", Array.ConvertAll(s.siruLv, b => b.ToString())) : "null";
                    SimpleFileLogger.Info(
                        $"STATE_DIFF[{phase}] cha={cha.name}"
                        + $" skinTuya={s.skinTuyaRate:F3} hohoAka={s.hohoAkaRate:F3} tears={s.tearsLv}"
                        + $" siru=[{siru}] eyeP={s.eyesPtn} mouthP={s.mouthPtn}");
                }
                if (_hFlag != null)
                {
                    var c = _hFlag.count;
                    HSceneProc.AnimationListInfo now = _hFlag.nowAnimationInfo;
                    SimpleFileLogger.Info(
                        $"STATE_DIFF[{phase}] scene"
                        + $" gaugeF={_hFlag.gaugeFemale:F2} gaugeM={_hFlag.gaugeMale:F2}"
                        + $" pose=(id={(now != null ? now.id : -1)},mode={(now != null ? now.mode.ToString() : "?")})"
                        + $" cnt(sonyuSame={(c != null ? c.sonyuSame : -1)},sonyuOrg={(c != null ? c.sonyuOrg : -1)},houshiInside={(c != null ? c.houshiInside : -1)})");
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"STATE_DIFF[{phase}] failed: {ex.Message}");
            }
        }

        // ====================================================================
        // 体位崩れ診断: 現実キャラの Animator 状態 + 全体bounds + 主要ボーン位置
        //   どの局面で 正常位 → 立ち/Tポーズ に崩れるかを特定する。
        // ====================================================================
        internal void LogRealBodyPoseDiag(string phase)
        {
            try
            {
                var charas = CollectCurrentHSceneCharas();
                foreach (var cha in charas)
                {
                    if (cha == null) continue;

                    Animator anim = cha.animBody;
                    string animInfo;
                    bool isHuman = false;
                    if (anim != null)
                    {
                        string clip = "none";
                        try { var ci = anim.GetCurrentAnimatorClipInfo(0); if (ci != null && ci.Length > 0 && ci[0].clip != null) clip = ci[0].clip.name; }
                        catch { }
                        float nt = 0f; int hash = 0;
                        try { var si = anim.GetCurrentAnimatorStateInfo(0); nt = si.normalizedTime; hash = si.fullPathHash; }
                        catch { }
                        isHuman = anim.isHuman;
                        animInfo = $"animEnabled={anim.enabled} speed={anim.speed:F2} ctrl={(anim.runtimeAnimatorController != null)} isHuman={isHuman} clip={clip} nt={nt:F3} state={hash}";
                    }
                    else
                    {
                        animInfo = "anim=null";
                    }

                    // 全体bounds（高さ size.y が低い=横たわる正常位 / 高い=立ち）
                    Bounds b = default(Bounds);
                    bool has = false;
                    var smrs = cha.GetComponentsInChildren<SkinnedMeshRenderer>(false);
                    foreach (var s in smrs)
                    {
                        if (s == null) continue;
                        if (!has) { b = s.bounds; has = true; }
                        else b.Encapsulate(s.bounds);
                    }
                    string bounds = has ? $"bCenter={b.center.ToString("F2")} bSize={b.size.ToString("F2")}" : "bounds=none(smr=" + (smrs != null ? smrs.Length : 0) + ")";

                    // 主要ボーン（humanoidのみ）
                    string bones = "";
                    if (anim != null && isHuman)
                    {
                        Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
                        Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
                        Transform lfoot = anim.GetBoneTransform(HumanBodyBones.LeftFoot);
                        bones = $"hips={(hips != null ? hips.position.ToString("F2") : "?")} head={(head != null ? head.position.ToString("F2") : "?")} lfoot={(lfoot != null ? lfoot.position.ToString("F2") : "?")}";
                    }

                    SimpleFileLogger.Warn($"POSE_DIAG[{phase}] cha={cha.name} {animInfo} {bounds} {bones}");
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"POSE_DIAG[{phase}] failed: {ex.Message}");
            }
        }

        // HFlag.Count を独立コピー（値フィールドはそのまま、配列はクローン）
        private static HFlag.Count CloneCount(HFlag.Count src)
        {
            var dst = new HFlag.Count();
            if (src == null) return dst;
            try
            {
                FieldInfo[] fields = typeof(HFlag.Count).GetFields(BindingFlags.Public | BindingFlags.Instance);
                foreach (FieldInfo f in fields)
                {
                    object v = f.GetValue(src);
                    if (v is Array arr) v = arr.Clone();
                    f.SetValue(dst, v);
                }
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"CloneCount failed: {ex.Message}");
            }
            return dst;
        }
    }
}
