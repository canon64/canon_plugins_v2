using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MainGameHScenePoseInjector
{
    internal sealed class AnimatorSnapshot
    {
        public ChaControl Cha;
        public RuntimeAnimatorController Controller;
        public int StateHash;
        public float NormalizedTime;
        public string AppliedPoseName;
        public int AppliedPoseId;
    }

    public sealed partial class Plugin
    {
        private static readonly FieldInfo HSceneFemalesField = typeof(HSceneProc).GetField(
            "lstFemale", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private HSceneProc _activeHSceneProc;
        private float _nextHSceneProbeAt;
        private bool _hSceneWasDetected;
        private float _hSceneLostCandidateSince = -1f;
        private bool _isApplyingPose;
        private bool _isRestoringSnapshots;

        private readonly Dictionary<int, AnimatorSnapshot> _snapshots = new Dictionary<int, AnimatorSnapshot>();
        private readonly Dictionary<int, float> _controllerChangeCandidateSince = new Dictionary<int, float>();
        private readonly List<ChaControl> _expressionFemales = new List<ChaControl>();

        internal bool IsInHScene()
        {
            return _hSceneWasDetected && _activeHSceneProc != null && _hSceneLostCandidateSince < 0f;
        }

        internal bool IsAnyPoseApplied()
        {
            return _snapshots.Count > 0;
        }

        internal int CurrentAppliedPoseId()
        {
            foreach (var kv in _snapshots)
                return kv.Value.AppliedPoseId;
            return -1;
        }

        private void UpdateHSceneDetect()
        {
            float pollTime = Time.unscaledTime;
            HSceneProc proc = _activeHSceneProc;
            if (pollTime >= _nextHSceneProbeAt)
            {
                _nextHSceneProbeAt = pollTime + 0.5f;
                proc = UnityEngine.Object.FindObjectOfType<HSceneProc>();
            }
            else if (proc == null || !proc.isActiveAndEnabled)
            {
                proc = null;
            }
            if (proc != null && proc.isActiveAndEnabled)
            {
                bool changed = !_hSceneWasDetected || _activeHSceneProc != proc;
                _activeHSceneProc = proc;
                _hSceneWasDetected = true;
                if (_hSceneLostCandidateSince >= 0f)
                {
                    LogInfo("hscene lost cancelled elapsed=" + FormatSeconds(Time.realtimeSinceStartup - _hSceneLostCandidateSince));
                    _hSceneLostCandidateSince = -1f;
                }
                if (changed)
                    LogInfo("hscene detected");
                return;
            }

            if (!_hSceneWasDetected)
            {
                _activeHSceneProc = null;
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (_hSceneLostCandidateSince < 0f)
            {
                _hSceneLostCandidateSince = now;
                LogInfo("hscene lost pending delay=" + FormatSeconds(_settings.HSceneLostConfirmSeconds)
                    + " snapshots=" + _snapshots.Count);
                return;
            }

            float elapsed = now - _hSceneLostCandidateSince;
            if (elapsed < _settings.HSceneLostConfirmSeconds)
                return;

            LogInfo("hscene lost confirmed elapsed=" + FormatSeconds(elapsed) + " snapshots=" + _snapshots.Count);
            RestoreAllSnapshots("hscene-lost");
            _activeHSceneProc = null;
            _hSceneWasDetected = false;
            _hSceneLostCandidateSince = -1f;
            _controllerChangeCandidateSince.Clear();
        }

        internal List<ChaControl> ResolveFemales()
        {
            List<ChaControl> result = new List<ChaControl>();
            ResolveFemalesInto(result);
            return result;
        }

        private void ResolveFemalesInto(List<ChaControl> result)
        {
            result.Clear();
            HSceneProc proc = _activeHSceneProc != null
                ? _activeHSceneProc
                : UnityEngine.Object.FindObjectOfType<HSceneProc>();
            if (proc != null)
            {
                IList listObj = HSceneFemalesField != null ? HSceneFemalesField.GetValue(proc) as IList : null;
                if (listObj != null)
                {
                    for (int i = 0; i < listObj.Count; i++)
                    {
                        ChaControl cha = listObj[i] as ChaControl;
                        if (cha != null && cha.sex == 1)
                            result.Add(cha);
                    }
                    if (result.Count > 0)
                        return;
                }
            }

            ChaControl[] all = CanonPlugins.Shared.IndependentActorFilter.FindHActors();
            for (int i = 0; i < all.Length; i++)
            {
                ChaControl cha = all[i];
                if (cha != null && cha.sex == 1)
                    result.Add(cha);
            }
        }

        internal bool ApplyPose(PoseEntry pose)
        {
            if (pose == null)
                return false;
            if (_isRestoringSnapshots)
            {
                LogWarn("apply pose skipped: restore in progress");
                return false;
            }

            _isApplyingPose = true;
            try
            {
                List<ChaControl> targets = ResolveFemales();
                if (targets.Count == 0)
                {
                    LogWarn("apply pose: no female found");
                    return false;
                }

                if (!_settings.ApplyToAllFemales && targets.Count > 1)
                    targets = new List<ChaControl> { targets[0] };

                bool anySuccess = false;
                foreach (ChaControl cha in targets)
                {
                    if (cha == null || cha.animBody == null)
                        continue;
                    int key = cha.GetInstanceID();
                    _controllerChangeCandidateSince.Remove(key);

                    if (!_snapshots.ContainsKey(key))
                    {
                        AnimatorSnapshot snap = new AnimatorSnapshot
                        {
                            Cha = cha,
                            Controller = cha.animBody.runtimeAnimatorController,
                            StateHash = 0,
                            NormalizedTime = 0f,
                        };
                        try
                        {
                            AnimatorStateInfo si = cha.animBody.GetCurrentAnimatorStateInfo(0);
                            snap.StateHash = si.shortNameHash;
                            snap.NormalizedTime = si.normalizedTime;
                        }
                        catch (Exception ex)
                        {
                            LogWarn("snapshot state read failed: " + ex.Message);
                        }
                        _snapshots[key] = snap;
                        LogInfo("snapshot saved cha=" + cha.name + " ctrl=" + (snap.Controller != null ? snap.Controller.name : "(null)"));
                    }

                    try
                    {
                        RuntimeAnimatorController loaded = cha.LoadAnimation(pose.Bundle, pose.Asset);
                        if (loaded == null)
                        {
                            LogWarn("LoadAnimation returned null bundle=" + pose.Bundle + " asset=" + pose.Asset);
                            continue;
                        }
                        cha.AnimPlay(pose.State);
                        cha.resetDynamicBoneAll = true;

                        _snapshots[key].AppliedPoseId = pose.Id;
                        _snapshots[key].AppliedPoseName = pose.Name;
                        anySuccess = true;
                        LogInfo("pose applied cha=" + cha.name + " id=" + pose.Id + " name=" + pose.Name + " state=" + pose.State);
                    }
                    catch (Exception ex)
                    {
                        LogWarn("apply pose exception cha=" + cha.name + " err=" + ex.Message);
                    }
                }

                if (anySuccess)
                {
                    _settings.LastAppliedPoseId = pose.Id;
                    SaveSettings();
                }
                return anySuccess;
            }
            finally
            {
                _isApplyingPose = false;
            }
        }

        internal void RestoreAllSnapshots(string reason)
        {
            if (_snapshots.Count == 0)
            {
                LogInfo("restore skipped reason=" + reason + " snapshots=0");
                return;
            }
            if (_isApplyingPose)
            {
                LogWarn("restore skipped reason=" + reason + " apply in progress snapshots=" + _snapshots.Count);
                return;
            }
            if (_isRestoringSnapshots)
            {
                LogWarn("restore skipped reason=" + reason + " restore already in progress snapshots=" + _snapshots.Count);
                return;
            }

            _isRestoringSnapshots = true;
            try
            {
                int restored = 0;
                List<int> keys = new List<int>(_snapshots.Keys);
                foreach (int key in keys)
                {
                    AnimatorSnapshot snap = _snapshots[key];
                    try
                    {
                        if (snap.Cha != null && snap.Cha.animBody != null && snap.Controller != null)
                        {
                            snap.Cha.animBody.runtimeAnimatorController = snap.Controller;
                            if (snap.StateHash != 0)
                            {
                                try
                                {
                                    snap.Cha.animBody.Play(snap.StateHash, 0, Mathf.Repeat(snap.NormalizedTime, 1f));
                                }
                                catch { }
                            }
                            snap.Cha.resetDynamicBoneAll = true;
                            restored++;
                            LogInfo("snapshot restored cha=" + snap.Cha.name + " reason=" + reason);
                        }
                    }
                    catch (Exception ex)
                    {
                        LogWarn("restore exception cha=" + (snap.Cha != null ? snap.Cha.name : "(null)") + " err=" + ex.Message);
                    }
                }
                _snapshots.Clear();
                _controllerChangeCandidateSince.Clear();
                LogInfo("restore executed reason=" + reason + " restored=" + restored + " total=" + keys.Count);
            }
            finally
            {
                _isRestoringSnapshots = false;
            }
        }

        private void AutoRestoreIfControllerChanged()
        {
            if (!_settings.AutoRestoreOnControllerChange || _snapshots.Count == 0 || _isApplyingPose || _isRestoringSnapshots)
            {
                _controllerChangeCandidateSince.Clear();
                return;
            }
            if (_hSceneLostCandidateSince >= 0f)
                return;

            List<int> deadKeys = null;
            float now = Time.realtimeSinceStartup;
            foreach (var kv in _snapshots)
            {
                AnimatorSnapshot snap = kv.Value;
                if (snap.Cha == null || snap.Cha.animBody == null)
                {
                    if (MarkControllerChangeCandidate(kv.Key, now, "(missing-cha)", "(null)"))
                    {
                        if (deadKeys == null) deadKeys = new List<int>();
                        deadKeys.Add(kv.Key);
                    }
                    continue;
                }

                RuntimeAnimatorController cur = snap.Cha.animBody.runtimeAnimatorController;
                if (cur == null)
                {
                    ClearControllerChangeCandidate(kv.Key, snap.Cha.name);
                    continue;
                }
                if (cur == snap.Controller || IsPoseController(cur, snap.AppliedPoseName))
                {
                    ClearControllerChangeCandidate(kv.Key, snap.Cha.name);
                    continue;
                }

                if (MarkControllerChangeCandidate(kv.Key, now, snap.Cha.name, cur.name))
                {
                    if (deadKeys == null) deadKeys = new List<int>();
                    deadKeys.Add(kv.Key);
                    LogInfo("auto-restore detect confirmed: HScene swapped controller cha=" + snap.Cha.name + " new=" + cur.name);
                }
            }

            if (deadKeys != null)
            {
                foreach (int k in deadKeys)
                {
                    _snapshots.Remove(k);
                    _controllerChangeCandidateSince.Remove(k);
                }
            }
        }

        private bool MarkControllerChangeCandidate(int key, float now, string chaName, string controllerName)
        {
            if (!_controllerChangeCandidateSince.TryGetValue(key, out float since))
            {
                _controllerChangeCandidateSince[key] = now;
                LogInfo("auto-restore detect pending cha=" + chaName
                    + " new=" + controllerName
                    + " delay=" + FormatSeconds(_settings.ControllerChangeConfirmSeconds));
                return false;
            }

            return now - since >= _settings.ControllerChangeConfirmSeconds;
        }

        private void ClearControllerChangeCandidate(int key, string chaName)
        {
            if (_controllerChangeCandidateSince.TryGetValue(key, out float since))
            {
                _controllerChangeCandidateSince.Remove(key);
                LogInfo("auto-restore detect cancelled cha=" + chaName
                    + " elapsed=" + FormatSeconds(Time.realtimeSinceStartup - since));
            }
        }

        private static bool IsPoseController(RuntimeAnimatorController cur, string poseName)
        {
            if (cur == null) return false;
            string n = cur.name ?? string.Empty;
            return n.IndexOf("pose", StringComparison.OrdinalIgnoreCase) >= 0
                || (!string.IsNullOrEmpty(poseName) && n.IndexOf(poseName, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string FormatSeconds(float value)
        {
            return value.ToString("0.00") + "s";
        }

        internal bool ApplyExpression(int? eyebrowPtn, int? eyesPtn, int? mouthPtn)
        {
            List<ChaControl> targets = ResolveFemales();
            if (targets.Count == 0)
            {
                LogWarn("apply expression: no female found");
                return false;
            }
            if (!_settings.ApplyToAllFemales && targets.Count > 1)
                targets = new List<ChaControl> { targets[0] };

            bool anySuccess = false;
            foreach (ChaControl cha in targets)
            {
                if (cha == null) continue;
                try
                {
                    if (eyebrowPtn.HasValue) cha.ChangeEyebrowPtn(eyebrowPtn.Value, blend: true);
                    if (eyesPtn.HasValue) cha.ChangeEyesPtn(eyesPtn.Value, blend: true);
                    if (mouthPtn.HasValue) cha.ChangeMouthPtn(mouthPtn.Value, blend: true);
                    anySuccess = true;
                    LogInfo("expression applied cha=" + cha.name + " eb=" + eyebrowPtn + " e=" + eyesPtn + " m=" + mouthPtn);
                }
                catch (Exception ex)
                {
                    LogWarn("apply expression exception cha=" + cha.name + " err=" + ex.Message);
                }
            }
            if (anySuccess)
            {
                if (eyebrowPtn.HasValue) _lockedEyebrowPtn = eyebrowPtn.Value;
                if (eyesPtn.HasValue) _lockedEyesPtn = eyesPtn.Value;
                if (mouthPtn.HasValue) _lockedMouthPtn = mouthPtn.Value;
            }
            return anySuccess;
        }

        // ── ポーズ適用中の「首をカメラに向ける」維持 ──────────────────────
        //
        // ゲームの首カメラ追従は HSceneProc が
        //   eyeneckFemale.Proc(animatorStateInfo, ..., Manager.Config.HData.FemaleNeckCamera)
        // として毎フレーム流しており、第1引数の animatorStateInfo が
        // Hモーションの状態であることを前提にしている。
        // ポーズ適用でキャラの runtimeAnimatorController を単体クリップへ
        // 差し替えると前提が崩れ、チェックを入れても首まで届かない。
        //
        // 掛ける／戻すは MainGamePortraitCapture と同じ方式にする。
        // 掛ける前の target / enabled / パターンを退避し、解除時にそれを書き戻す。
        // パターン番号を決め打ちで戻すと、変な角度で固まる。

        private sealed class NeckLookSnapshot
        {
            internal ChaControl Female;
            internal NeckLookControllerVer2 Controller;
            internal Transform Target;
            internal bool Enabled;
            internal int Pattern;
        }

        // 首カメラ追従で使うパターン。PortraitCapture の既定と同じ。
        private const int NeckLookCameraPattern = 1;

        private readonly List<NeckLookSnapshot> _neckLookSnapshots = new List<NeckLookSnapshot>();
        private bool _neckCameraApplied;

        private void MaintainNeckLookCamera()
        {
            if (!IsInHScene())
            {
                // 捨てる前に必ず戻す。
                // 戻さずに退避だけ消すと、首がカメラを向いたまま固定され、
                // 書き戻す材料も失われて二度と直せなくなる。
                if (_neckLookSnapshots.Count > 0) RestoreNeckLookCamera();
                _neckCameraApplied = false;
                return;
            }

            bool want = IsAnyPoseApplied() && ReadFemaleNeckCameraFlag();

            // 掛かっているのに掛けるべきでない状態なら、必ず外す。
            // フラグ1個の遷移だけで判断すると、取りこぼした時に外れなくなる。
            if (!want)
            {
                if (_neckCameraApplied || _neckLookSnapshots.Count > 0)
                    RestoreNeckLookCamera();
                return;
            }

            if (!_neckCameraApplied) ApplyNeckLookCamera();
        }

        private void ApplyNeckLookCamera()
        {
            Transform camTf = ResolveHSceneCameraTransform();
            if (camTf == null) return;

            List<ChaControl> targets = ResolveFemales();
            if (targets == null || targets.Count == 0) return;

            _neckLookSnapshots.Clear();
            for (int i = 0; i < targets.Count; i++)
            {
                ChaControl cha = targets[i];
                if (cha == null || cha.neckLookCtrl == null) continue;
                try
                {
                    _neckLookSnapshots.Add(new NeckLookSnapshot
                    {
                        Female = cha,
                        Controller = cha.neckLookCtrl,
                        Target = cha.neckLookCtrl.target,
                        Enabled = cha.neckLookCtrl.enabled,
                        Pattern = cha.GetLookNeckPtn()
                    });

                    cha.neckLookCtrl.target = camTf;
                    cha.neckLookCtrl.enabled = true;
                    cha.ChangeLookNeckPtn(NeckLookCameraPattern, 1f);
                }
                catch (Exception ex)
                {
                    LogWarn("neck look apply failed: " + ex.Message);
                }
            }

            _neckCameraApplied = true;
            LogInfo("neck look camera ON targets=" + _neckLookSnapshots.Count);
        }

        private void RestoreNeckLookCamera()
        {
            int restored = 0;
            for (int i = 0; i < _neckLookSnapshots.Count; i++)
            {
                NeckLookSnapshot saved = _neckLookSnapshots[i];
                if (saved == null || saved.Controller == null) continue;
                try
                {
                    saved.Controller.target = saved.Target;
                    saved.Controller.enabled = saved.Enabled;
                    if (saved.Female != null)
                        saved.Female.ChangeLookNeckPtn(saved.Pattern, 1f);
                    restored++;
                }
                catch (Exception ex)
                {
                    LogWarn("neck look restore failed: " + ex.Message);
                }
            }

            _neckLookSnapshots.Clear();
            _neckCameraApplied = false;
            LogInfo("neck look camera OFF restored=" + restored);
        }

        /// <summary>設定画面の「1人目の首をカメラに向ける」。読めなければ false。</summary>
        private static bool ReadFemaleNeckCameraFlag()
        {
            try { return Manager.Config.HData.FemaleNeckCamera; }
            catch { return false; }
        }

        /// <summary>Hシーンのカメラ。HSceneProc が首の対象に渡しているものと同じ。</summary>
        private static Transform ResolveHSceneCameraTransform()
        {
            try
            {
                HFlag flag = UnityEngine.Object.FindObjectOfType<HFlag>();
                if (flag != null && flag.ctrlCamera != null && flag.ctrlCamera.thisCmaera != null)
                    return flag.ctrlCamera.thisCmaera.transform;
            }
            catch { }

            Camera cam = Camera.main;
            return cam != null ? cam.transform : null;
        }

        private void MaintainSelectedExpression()
        {
            if (!IsInHScene() || (!_lockedEyebrowPtn.HasValue && !_lockedEyesPtn.HasValue && !_lockedMouthPtn.HasValue)) return;

            ResolveFemalesInto(_expressionFemales);
            int targetCount = _settings.ApplyToAllFemales ? _expressionFemales.Count : Math.Min(_expressionFemales.Count, 1);

            bool restored = false;
            for (int i = 0; i < targetCount; i++)
            {
                ChaControl cha = _expressionFemales[i];
                if (cha == null) continue;
                if (_lockedEyebrowPtn.HasValue && cha.GetEyebrowPtn() != _lockedEyebrowPtn.Value)
                {
                    cha.ChangeEyebrowPtn(_lockedEyebrowPtn.Value, blend: false);
                    restored = true;
                }
                if (_lockedEyesPtn.HasValue && cha.GetEyesPtn() != _lockedEyesPtn.Value)
                {
                    cha.ChangeEyesPtn(_lockedEyesPtn.Value, blend: false);
                    restored = true;
                }
                if (_lockedMouthPtn.HasValue && cha.GetMouthPtn() != _lockedMouthPtn.Value)
                {
                    cha.ChangeMouthPtn(_lockedMouthPtn.Value, blend: false);
                    restored = true;
                }
            }

            if (restored && Time.unscaledTime - _lastExpressionRestoreLogTime >= 1f)
            {
                _lastExpressionRestoreLogTime = Time.unscaledTime;
                LogInfo("selected expression restored after game override eb=" + _lockedEyebrowPtn + " e=" + _lockedEyesPtn + " m=" + _lockedMouthPtn);
            }
        }
    }
}
