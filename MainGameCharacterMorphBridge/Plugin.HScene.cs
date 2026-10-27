using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using CanonPlugins.Shared;

namespace MainGameCharacterMorphBridge
{
    public sealed partial class Plugin
    {
        private sealed class MorphSnapshot
        {
            internal float[] Body;
            internal float[] Face;

            internal bool HasBody => Body != null && Body.Length > 0;
            internal bool HasFace => Face != null && Face.Length > 0;
        }

        private static readonly FieldInfo FieldLstFemale = AccessTools.Field(typeof(HSceneProc), "lstFemale");
        private static readonly FieldInfo FieldLstMotionIk = AccessTools.Field(typeof(HSceneProc), "lstMotionIK");
        private static readonly FieldInfo FieldMale = AccessTools.Field(typeof(HSceneProc), "male");
        private static readonly FieldInfo FieldMale1 = AccessTools.Field(typeof(HSceneProc), "male1");
        private static readonly FieldInfo FieldItem = AccessTools.Field(typeof(HSceneProc), "item");

        private ColorSnapshot _originalColors;
        private ColorSnapshot _targetColors;
        // 遷移の起点。開始時点の見た目を保持する。
        private MorphSnapshot _transitionFrom;
        private ColorSnapshot _transitionFromColors;
        private MakeupSnapshot _originalMakeup;
        private MakeupSnapshot _targetMakeup;
        private MakeupSnapshot _transitionFromMakeup;
        // 遷移中の経過割合(0..1)。イージング前の素の時間比。遷移していない時は -1。
        private float _transitionTimeRatio = -1f;

        private Coroutine _autoHSceneInitCoroutine;
        private Coroutine _blendTransitionCoroutine;
        private Coroutine _postMotionReapplyCoroutine;
        private Coroutine _externalReapplyCoroutine;
        private Coroutine _clothesChangeReapplyCoroutine;
        private bool _suppressVisualRefreshReapply;
        private bool _hasObservedAnimationState;
        private string _lastObservedAnimStateName = string.Empty;
        private int _lastObservedAnimationId = int.MinValue;
        private bool _hasDirectBodyOverride;
        private int _directBodyShapeIndex;
        private float _directBodyShapeValue;
        private bool _hasHeightOverride;
        private float _directHeightValue;
        private bool _hasBreastOverride;
        private float _directBreastValue;
        private bool _hasDirectFaceOverride;
        private int _directFaceShapeIndex;
        private float _directFaceShapeValue;

        private void Update()
        {
            // BlankMapAdd の追加マップ以外では発火させない。
            if (!BlankRoomGate.IsInBlankRoom) return;
            TickAnimationChangeReapply();
        }

        internal bool CaptureOriginalFromConfig()
        {
            return CaptureOriginal(TargetFemaleIndex(), "config button");
        }

        internal bool LoadTargetCardFromConfig()
        {
            string path = _cfgTargetCardPath != null ? _cfgTargetCardPath.Value : _settings.TargetCardPath;
            if (string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(_settings.SelectedCardWord))
                return LoadTargetCardByWord(_settings.SelectedCardWord, TargetFemaleIndex(), "config button");

            return LoadTargetCard(path, TargetFemaleIndex(), "config button");
        }

        internal bool ResetToOriginalFromConfig()
        {
            return ResetToOriginal(TargetFemaleIndex(), "config button");
        }

        internal bool CaptureOriginal(int femaleIndex, string source, bool writeLog = true)
        {
            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!TryResolveTarget(femaleIndex, out ChaControl target, out string error))
            {
                if (writeLog)
                    LogWarnAlways("capture original failed: " + error);
                return false;
            }

            _originalSnapshot = SnapshotFrom(target);
            _originalColors = ColorSnapshotFrom(target.chaFile);
            _originalMakeup = MakeupSnapshotFrom(target.chaFile, ResolveCoordinateIndex(target));
            CaptureOriginalOverlay(target);
            CaptureOriginalHair(target);
            // キャラが変われば入力テクスチャの戻しもやり直す。
            _customTexInputsPrepared = false;
            _originalFemaleIndex = femaleIndex;
            if (_originalSnapshot == null)
            {
                if (writeLog)
                    LogWarnAlways("capture original failed: empty snapshot");
                return false;
            }

            SyncHeightBreastFromTarget(target);
            if (writeLog)
                LogExecution("capture original: female=" + femaleIndex + " source=" + source);
            return true;
        }

        internal bool LoadTargetCard(string cardPath, int femaleIndex, string source, bool writeLog = true)
        {
            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!IsEnabled())
            {
                if (writeLog)
                    LogExecution("load target card skipped: disabled");
                return false;
            }

            string normalizedPath = PluginSettings.NormalizePath(cardPath);
            if (string.IsNullOrEmpty(normalizedPath))
            {
                if (writeLog)
                    LogWarnAlways("load target card failed: empty path");
                return false;
            }

            if (!File.Exists(normalizedPath))
            {
                if (writeLog)
                    LogWarnAlways("load target card failed: file not found: " + normalizedPath);
                return false;
            }

            if (!TryResolveTarget(femaleIndex, out ChaControl target, out string error))
            {
                if (writeLog)
                    LogWarnAlways("load target card failed: " + error);
                return false;
            }

            if (_settings.AutoCaptureOriginal && (_originalSnapshot == null || _originalFemaleIndex != femaleIndex))
                CaptureOriginal(femaleIndex, "auto before card load", writeLog);

            try
            {
                var card = new ChaFileControl();
                if (!card.LoadCharaFile(normalizedPath, target.sex, noLoadPng: true, noLoadStatus: true))
                {
                    if (writeLog)
                        LogWarnAlways("load target card returned false: " + normalizedPath);
                    return false;
                }

                _targetSnapshot = SnapshotFrom(card);
                _targetColors = ColorSnapshotFrom(card);
                _targetMakeup = MakeupSnapshotFrom(card, ResolveCoordinateIndex(target));
                CaptureTargetOverlay(card);
                CaptureTargetHair(card);
                _targetFemaleIndex = femaleIndex;
                _targetCardPath = normalizedPath;
                _settings.TargetCardPath = normalizedPath;
                if (_cfgTargetCardPath != null && !string.Equals(_cfgTargetCardPath.Value, normalizedPath, StringComparison.Ordinal))
                    _cfgTargetCardPath.Value = normalizedPath;

                if (writeLog)
                {
                    LogExecution("load target card values: female=" + femaleIndex + " path=" + normalizedPath + " source=" + source);
                    LogOriginalVsCardColors(target);
                }
                return true;
            }
            catch (Exception ex)
            {
                if (writeLog)
                    LogErrorAlways("load target card failed: " + ex.Message);
                return false;
            }
        }

        internal bool ApplyBlend(float blend, int femaleIndex, string source, bool writeLog = true,
            bool forceVisualRefresh = true, bool preserveDirectOverrides = false)
        {
            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!IsEnabled())
            {
                if (writeLog)
                    LogExecution("blend skipped: disabled");
                return false;
            }

            blend = PluginSettings.Round2(PluginSettings.Clamp01(blend));
            _settings.Blend = blend;

            if (!TryResolveTarget(femaleIndex, out ChaControl target, out string error))
            {
                if (writeLog)
                    LogWarnAlways("blend failed: " + error);
                return false;
            }

            if (_originalSnapshot == null || _originalFemaleIndex != femaleIndex)
            {
                if (!CaptureOriginal(femaleIndex, "auto before blend", writeLog))
                    return false;
            }

            if (_targetSnapshot == null || _targetFemaleIndex != femaleIndex)
            {
                if (!LoadActiveTargetCard(femaleIndex, "auto before blend", writeLog))
                    return false;
            }

            if (_originalSnapshot == null || _targetSnapshot == null)
                return false;

            bool bodyApplied = ApplyBodyBlend(target, _originalSnapshot.Body, _targetSnapshot.Body, blend,
                preserveDirectOverrides && _hasHeightOverride ? 0 : -1,
                preserveDirectOverrides && _hasBreastOverride ? 4 : -1,
                preserveDirectOverrides && _hasDirectBodyOverride ? _directBodyShapeIndex : -1);
            bool faceApplied = ApplyFaceBlend(target, _originalSnapshot.Face, _targetSnapshot.Face, blend,
                preserveDirectOverrides && _hasDirectFaceOverride ? _directFaceShapeIndex : -1);

            // 色は反映が重くなりうるので、遷移中は間引き、ログを出す節目では必ず入れる。
            ApplyColorBlend(target, blend, writeLog);

            if (bodyApplied || forceVisualRefresh)
            {
                target.UpdateShapeBodyValueFromCustomInfo();
                RefreshHAfterBodyChange(femaleIndex, "blend", writeLog);
                SyncHeightBreastFromTarget(target);
            }

            if (faceApplied || forceVisualRefresh)
                target.UpdateShapeFaceValueFromCustomInfo();

            if (writeLog)
                LogExecution("blend applied: female=" + femaleIndex + " value=" + blend.ToString("0.00") + " source=" + source);
            return bodyApplied || faceApplied;
        }

        internal bool TryGetBodyShape(int index, int femaleIndex, out float value)
        {
            value = 0f;
            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!TryResolveTarget(femaleIndex, out ChaControl target, out _)) return false;
            value = target.GetShapeBodyValue(index);
            return true;
        }

        internal bool TryGetTargetIdentity(int femaleIndex, out int identity)
        {
            identity = 0;
            if (!TryResolveTarget(ResolveFemaleIndex(femaleIndex), out ChaControl target, out _)) return false;
            identity = target.GetInstanceID();
            return true;
        }

        internal bool SetBodyShape(int index, float value, int femaleIndex, string source, bool writeLog = true, bool trackDirectOverride = true)
        {
            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!IsEnabled())
            {
                if (writeLog)
                    LogExecution("body shape skipped: disabled");
                return false;
            }

            int safeIndex = PluginSettings.ClampInt(index, 0, 43);
            float safeValue = PluginSettings.Round2(PluginSettings.Clamp01(value));
            _settings.BodyShapeIndex = safeIndex;
            _settings.BodyShapeValue = safeValue;
            if (safeIndex == 0)
                _settings.Height = safeValue;
            if (safeIndex == 4)
                _settings.Breast = safeValue;
            if (trackDirectOverride)
                TrackDirectBodyOverride(safeIndex, safeValue);

            if (!TryResolveTarget(femaleIndex, out ChaControl target, out string error))
            {
                if (writeLog)
                    LogWarnAlways("body shape failed: " + error);
                return false;
            }

            bool changed = target.SetShapeBodyValue(safeIndex, safeValue);
            target.UpdateShapeBodyValueFromCustomInfo();
            RefreshHAfterBodyChange(femaleIndex, "body index " + safeIndex, writeLog);

            if (writeLog)
                LogExecution("body shape applied: female=" + femaleIndex + " index=" + safeIndex + " value=" + safeValue.ToString("0.00") + " source=" + source);
            return changed;
        }

        internal bool SetFaceShape(int index, float value, int femaleIndex, string source, bool writeLog = true, bool trackDirectOverride = true)
        {
            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!IsEnabled())
            {
                if (writeLog)
                    LogExecution("face shape skipped: disabled");
                return false;
            }

            int safeIndex = PluginSettings.ClampInt(index, 0, 51);
            float safeValue = PluginSettings.Round2(PluginSettings.Clamp01(value));
            _settings.FaceShapeIndex = safeIndex;
            _settings.FaceShapeValue = safeValue;
            if (trackDirectOverride)
                TrackDirectFaceOverride(safeIndex, safeValue);

            if (!TryResolveTarget(femaleIndex, out ChaControl target, out string error))
            {
                if (writeLog)
                    LogWarnAlways("face shape failed: " + error);
                return false;
            }

            bool changed = target.SetShapeFaceValue(safeIndex, safeValue);
            target.UpdateShapeFaceValueFromCustomInfo();

            if (writeLog)
                LogExecution("face shape applied: female=" + femaleIndex + " index=" + safeIndex + " value=" + safeValue.ToString("0.00") + " source=" + source);
            return changed;
        }

        internal bool ResetToOriginal(int femaleIndex, string source)
        {
            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!IsEnabled())
            {
                LogExecution("reset skipped: disabled");
                return false;
            }

            if (_originalSnapshot == null || _originalFemaleIndex != femaleIndex)
            {
                LogWarnAlways("reset failed: original snapshot not captured");
                return false;
            }

            if (!TryResolveTarget(femaleIndex, out ChaControl target, out string error))
            {
                LogWarnAlways("reset failed: " + error);
                return false;
            }

            bool bodyApplied = ApplySnapshotBody(target, _originalSnapshot.Body);
            bool faceApplied = ApplySnapshotFace(target, _originalSnapshot.Face);

            if (bodyApplied)
            {
                target.UpdateShapeBodyValueFromCustomInfo();
                RefreshHAfterBodyChange(femaleIndex, "reset original");
                SyncHeightBreastFromTarget(target);
            }

            if (faceApplied)
                target.UpdateShapeFaceValueFromCustomInfo();

            _settings.Blend = 0f;
            SetEntryValue(_cfgBlend, 0f);
            ClearDirectOverrideState();
            LogExecution("reset original applied: female=" + femaleIndex + " source=" + source);
            return bodyApplied || faceApplied;
        }

        internal bool UpsertSelectedCardFromConfig()
        {
            string word = _cfgSelectedCardWord != null ? _cfgSelectedCardWord.Value : _settings.SelectedCardWord;
            string path = _cfgTargetCardPath != null ? _cfgTargetCardPath.Value : _settings.TargetCardPath;
            string triggerWords = _cfgSelectedCardTriggerWords != null ? _cfgSelectedCardTriggerWords.Value : _settings.SelectedCardTriggerWords;
            return RegisterCard(word, path, triggerWords, "config button");
        }

        internal bool RegisterCard(string word, string cardPath, string triggerWords, string source)
        {
            word = (word ?? string.Empty).Trim();
            string path = PluginSettings.NormalizePath(cardPath);
            triggerWords = PluginSettings.NormalizeCsv(triggerWords);

            if (string.IsNullOrWhiteSpace(word))
            {
                LogWarnAlways("register card failed: selected card word is empty");
                return false;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                LogWarnAlways("register card failed: card path is empty word=" + word);
                return false;
            }

            var cards = new List<MorphCardRegistration>(_settings.RegisteredCards ?? new MorphCardRegistration[0]);
            MorphCardRegistration target = cards.FirstOrDefault(x => x != null && string.Equals(x.Word, word, StringComparison.Ordinal));
            if (target == null)
            {
                target = new MorphCardRegistration();
                cards.Add(target);
            }

            target.Enabled = true;
            target.Word = word;
            target.TriggerWords = triggerWords;
            target.CardPath = path;
            target.Normalize();

            _settings.SelectedCardWord = word;
            _settings.SelectedCardTriggerWords = triggerWords;
            _settings.TargetCardPath = path;
            _settings.RegisteredCards = cards.Where(x => x != null).Select(x => x.Normalize()).ToArray();
            SaveSettings("registered card: " + word);
            LogExecution("registered card: word=" + word + " path=" + path + " source=" + source);
            return true;
        }

        internal string[] GetRegisteredCardWords()
        {
            return EnabledRegisteredCards()
                .Select(x => x.Word)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
        }

        internal void SchedulePostMotionReapply(string reason, int femaleIndex = -1)
        {
            if (!IsEnabled() || _currentHScene == null)
                return;

            StopPostMotionReapply();
            _postMotionReapplyCoroutine = StartCoroutine(PostMotionReapplyRoutine(reason, femaleIndex));
        }

        internal void ScheduleExternalReapplyCurrentMorphState(string reason, int femaleIndex = -1)
        {
            if (!IsEnabled() || _currentHScene == null)
                return;

            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!HasReapplyableMorphState(femaleIndex))
                return;

            if (IsClothesReason(reason))
            {
                ScheduleClothesChangeReapply(reason, femaleIndex);
                return;
            }

            StopClothesChangeReapply();

            if (_externalReapplyCoroutine != null)
                StopCoroutine(_externalReapplyCoroutine);

            _externalReapplyCoroutine = StartCoroutine(ExternalReapplyRoutine(reason, femaleIndex));
        }

        internal void ScheduleChaControlVisualRefreshReapply(ChaControl female, string methodName)
        {
            if (_suppressVisualRefreshReapply)
                return;

            int femaleIndex = ResolveFemaleIndex(female);
            if (femaleIndex < 0)
                return;

            string safeMethodName = string.IsNullOrWhiteSpace(methodName) ? "unknown" : methodName;
            string safeReason = "cha-control " + safeMethodName;
            if (IsClothesRefreshMethod(safeMethodName))
            {
                ScheduleClothesChangeReapply(safeReason, femaleIndex, female);
                return;
            }

            ScheduleExternalReapplyCurrentMorphState(safeReason, femaleIndex);
        }

        internal void ScheduleClothesChangeReapply(string reason, int femaleIndex = -1)
        {
            ScheduleClothesChangeReapply(reason, femaleIndex, null);
        }

        private void ScheduleClothesChangeReapply(string reason, int femaleIndex, ChaControl female)
        {
            if (!IsEnabled() || _currentHScene == null)
                return;

            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!HasReapplyableMorphState(femaleIndex))
                return;

            if (female == null && !TryResolveTarget(femaleIndex, out female, out _))
                return;

            StopExternalReapply();
            StopClothesChangeReapply();
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "clothes changed" : reason;
            byte[] preservedClothesState = CopyArray(female.fileStatus?.clothesState);
            _clothesChangeReapplyCoroutine = StartCoroutine(
                ClothesChangeReapplyRoutine(female, femaleIndex, safeReason, preservedClothesState));
        }

        private bool HasReapplyableMorphState(int femaleIndex)
        {
            int resolvedFemaleIndex = ResolveFemaleIndex(femaleIndex);
            bool hasBlendState =
                _originalSnapshot != null
                && _targetSnapshot != null
                && _originalFemaleIndex == resolvedFemaleIndex
                && _targetFemaleIndex == resolvedFemaleIndex
                && _settings != null
                && Math.Abs(_settings.Blend) > 0.0001f;

            return hasBlendState
                || _hasHeightOverride
                || _hasBreastOverride
                || _hasDirectBodyOverride
                || _hasDirectFaceOverride;
        }

        internal void StopPostMotionReapply()
        {
            if (_postMotionReapplyCoroutine == null)
                return;

            StopCoroutine(_postMotionReapplyCoroutine);
            _postMotionReapplyCoroutine = null;
        }

        internal void StopExternalReapply()
        {
            if (_externalReapplyCoroutine == null)
                return;

            StopCoroutine(_externalReapplyCoroutine);
            _externalReapplyCoroutine = null;
        }

        internal void StopClothesChangeReapply()
        {
            if (_clothesChangeReapplyCoroutine == null)
                return;

            StopCoroutine(_clothesChangeReapplyCoroutine);
            _clothesChangeReapplyCoroutine = null;
            _suppressVisualRefreshReapply = false;
        }

        internal void ResetAnimationObservation()
        {
            _hasObservedAnimationState = false;
            _lastObservedAnimStateName = string.Empty;
            _lastObservedAnimationId = int.MinValue;
        }

        internal void ClearDirectOverrideState()
        {
            _hasDirectBodyOverride = false;
            _directBodyShapeIndex = 0;
            _directBodyShapeValue = 0f;
            _hasHeightOverride = false;
            _directHeightValue = 0f;
            _hasBreastOverride = false;
            _directBreastValue = 0f;
            _hasDirectFaceOverride = false;
            _directFaceShapeIndex = 0;
            _directFaceShapeValue = 0f;
        }

        internal bool LoadTargetCardByWord(string word, int femaleIndex, string source, bool writeLog = true)
        {
            femaleIndex = ResolveFemaleIndex(femaleIndex);
            MorphCardRegistration card = FindRegisteredCardByWord(word);
            if (card == null)
            {
                if (writeLog)
                    LogWarnAlways("load target card by word failed: card not registered word=" + (word ?? string.Empty));
                return false;
            }

            SelectRegisteredCard(card);
            return LoadTargetCard(card.CardPath, femaleIndex, source + " word=" + card.Word, writeLog);
        }

        internal bool BlendToCardWord(string word, float targetBlend, float seconds, int femaleIndex, string source)
        {
            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!LoadTargetCardByWord(word, femaleIndex, source, true))
                return false;

            return BlendTo(targetBlend, seconds, femaleIndex, source + " word=" + word);
        }

        internal bool BlendToCardPath(string cardPath, float targetBlend, float seconds, int femaleIndex, string source)
        {
            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!LoadTargetCard(cardPath, femaleIndex, source, true))
                return false;

            return BlendTo(targetBlend, seconds, femaleIndex, source + " path=" + PluginSettings.NormalizePath(cardPath));
        }

        internal bool BlendTo(float targetBlend, float seconds, int femaleIndex, string source)
        {
            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!IsEnabled())
            {
                LogExecution("blend transition skipped: disabled");
                return false;
            }

            if (!EnsureMorphSnapshots(femaleIndex, source, true))
                return false;

            StopBlendTransition();
            targetBlend = PluginSettings.Round2(PluginSettings.Clamp01(targetBlend));

            // 遷移の起点は「今の見た目」。元キャラ基準で混ぜるだけだと、既に変身済み(Blend=1)の時に
            // 別のカードへ移ろうとしても数値が動かず、何も起きないまま終わる。
            ChaControl transitionTarget;
            string transitionError;
            if (TryResolveTarget(femaleIndex, out transitionTarget, out transitionError))
            {
                _transitionFrom = SnapshotFrom(transitionTarget);
                _transitionFromColors = ColorSnapshotFrom(transitionTarget.chaFile);
                _transitionFromMakeup = MakeupSnapshotFrom(transitionTarget.chaFile, ResolveCoordinateIndex(transitionTarget));
            }
            else
            {
                _transitionFrom = null;
                _transitionFromColors = null;
                _transitionFromMakeup = null;
            }

            // 秒数は本来呼び出し側（VoiceFaceEventBridge等）が渡してくるが、
            // 光・音とまとめてここで詰められるように上書きできる。
            if (_settings.MorphDurationOverrideEnabled)
            {
                float overridden = PluginSettings.ClampFloat(_settings.MorphDurationSeconds, 0f, 60f);
                if (!Mathf.Approximately(overridden, seconds))
                    LogExecution("blend seconds overridden: requested=" + seconds.ToString("0.00") + " used=" + overridden.ToString("0.00"));
                seconds = overridden;
            }

            seconds = Mathf.Max(0f, seconds);
            if (seconds <= 0f)
            {
                SetEntryValue(_cfgBlend, targetBlend);
                return ApplyBlend(targetBlend, femaleIndex, source, true);
            }

            float startBlend = PluginSettings.Round2(PluginSettings.Clamp01(_settings.Blend));
            _blendTransitionCoroutine = StartCoroutine(BlendTransitionRoutine(startBlend, targetBlend, seconds, femaleIndex, source));
            LogExecution("blend transition started: female=" + femaleIndex + " from=" + startBlend.ToString("0.00") + " to=" + targetBlend.ToString("0.00") + " seconds=" + seconds.ToString("0.00") + " source=" + source);
            return true;
        }

        internal bool TryResolveRegisteredCardFromText(string text, out string word, out string cardPath)
        {
            word = string.Empty;
            cardPath = string.Empty;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            MorphCardRegistration bestCard = null;
            string bestKeyword = string.Empty;
            int bestIndex = -1;
            foreach (MorphCardRegistration card in EnabledRegisteredCards())
            {
                foreach (string keyword in GetCardKeywords(card))
                {
                    int index = text.IndexOf(keyword, StringComparison.Ordinal);
                    if (index < 0)
                        continue;

                    if (bestCard == null
                        || index < bestIndex
                        || (index == bestIndex && keyword.Length > bestKeyword.Length))
                    {
                        bestCard = card;
                        bestKeyword = keyword;
                        bestIndex = index;
                    }
                }
            }

            if (bestCard == null)
                return false;

            word = bestCard.Word;
            cardPath = bestCard.CardPath;
            return true;
        }

        private bool LoadActiveTargetCard(int femaleIndex, string source, bool writeLog)
        {
            MorphCardRegistration selected = FindSelectedRegisteredCard();
            if (selected != null)
            {
                SelectRegisteredCard(selected);
                return LoadTargetCard(selected.CardPath, femaleIndex, source + " word=" + selected.Word, writeLog);
            }

            return LoadTargetCard(_settings.TargetCardPath, femaleIndex, source, writeLog);
        }

        private bool EnsureMorphSnapshots(int femaleIndex, string source, bool writeLog)
        {
            if (_originalSnapshot == null || _originalFemaleIndex != femaleIndex)
            {
                if (!CaptureOriginal(femaleIndex, "auto before " + source, writeLog))
                    return false;
            }

            if (_targetSnapshot == null || _targetFemaleIndex != femaleIndex)
            {
                if (!LoadActiveTargetCard(femaleIndex, "auto before " + source, writeLog))
                    return false;
            }

            return _originalSnapshot != null && _targetSnapshot != null;
        }

        /// <summary>
        /// 経過割合(0..1)を、変化のかかり方に応じて曲げる。
        /// EaseInOutは前半を加速、後半を減速させる。Powerが大きいほどメリハリが強くなる。
        /// </summary>
        private float ApplyMorphEasing(float t)
        {
            t = Mathf.Clamp01(t);
            if (_settings == null)
                return t;

            float power = PluginSettings.ClampFloat(_settings.MorphEasingPower, 1f, 5f);

            switch (_settings.MorphEasing)
            {
                case MorphEasingMode.EaseInOut:
                    return t < 0.5f
                        ? 0.5f * Mathf.Pow(t * 2f, power)
                        : 1f - 0.5f * Mathf.Pow((1f - t) * 2f, power);

                case MorphEasingMode.EaseIn:
                    return Mathf.Pow(t, power);

                case MorphEasingMode.EaseOut:
                    return 1f - Mathf.Pow(1f - t, power);

                default:
                    return t;
            }
        }

        /// <summary>
        /// 開始時点の見た目から、行き先（1ならカード、0なら元キャラ）へ直接混ぜる。
        /// これにより、変身済みの状態から別のカードへ移る場合もちゃんと動く。
        /// 起点が取れていない時は false を返して従来の経路に任せる。
        /// </summary>
        private bool ApplyTransitionStep(int femaleIndex, float targetBlend, float t)
        {
            if (_transitionFrom == null)
                return false;

            MorphSnapshot to = targetBlend >= 0.5f ? _targetSnapshot : _originalSnapshot;
            ColorSnapshot toColors = targetBlend >= 0.5f ? _targetColors : _originalColors;
            if (to == null)
                return false;

            ChaControl target;
            string error;
            if (!TryResolveTarget(femaleIndex, out target, out error))
                return false;

            bool bodyApplied = ApplyBodyBlend(target, _transitionFrom.Body, to.Body, t);
            bool faceApplied = ApplyFaceBlend(target, _transitionFrom.Face, to.Face, t);

            // 上絵(KSOX)はテクスチャなので混ぜられない。変身の中間点で行き先の一式へ丸ごと差し替える。
            // 切り替えはイージング後の値ではなく経過時間の真ん中で行う。加減速を変えても瞬間がずれない。
            TickOverlaySwap(target, targetBlend, _transitionTimeRatio >= 0f ? _transitionTimeRatio : t);

            // 髪型(メッシュ)も同じ理由で混ぜられない。同じ中間点で行き先の髪へ差し替える。
            TickHairSwap(target, targetBlend, _transitionTimeRatio >= 0f ? _transitionTimeRatio : t);

            if (_transitionFromColors != null && toColors != null)
            {
                // 差し替え直後はKSOXがゲーム標準のベースから焼き直すので、
                // 間引きを飛ばして肌色を入れ直さないと上絵ごと元の色に戻る。
                // 髪を作り直した直後も同じ。塗り直さないと髪の色が元へ戻る。
                bool force = _forceColorAfterOverlay || _forceColorAfterHair;
                _forceColorAfterOverlay = false;
                _forceColorAfterHair = false;
                MakeupSnapshot toMakeup = targetBlend >= 0.5f ? _targetMakeup : _originalMakeup;
                ApplyColorBlendBetween(target, _transitionFromColors, toColors, _transitionFromMakeup, toMakeup, t, force);
            }

            if (bodyApplied)
            {
                target.UpdateShapeBodyValueFromCustomInfo();
                SyncHeightBreastFromTarget(target);
            }

            if (faceApplied)
                target.UpdateShapeFaceValueFromCustomInfo();

            // t=0 では値が動かず false になるが、ここで従来経路へ落とすと最終値へ一気に飛ぶ。
            // 起点が取れている間はこちらの経路で通し切る。
            return true;
        }

        private IEnumerator BlendTransitionRoutine(float startBlend, float targetBlend, float seconds, int femaleIndex, string source)
        {
            // 変身の進み具合をそのままグローの強さに使う。MainGameGlowが無ければ何も起きない。
            bool glow = BeginMorphGlow(femaleIndex);
            PlayMorphSound();
            ResetOverlaySwapState();
            ResetHairSwapState();

            float elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                float t = seconds > 0f ? Mathf.Clamp01(elapsed / seconds) : 1f;
                // 数値の変化にだけイージングを掛ける。グローと音は実時間のままにする。
                _transitionTimeRatio = t;
                float eased = ApplyMorphEasing(t);
                if (!ApplyTransitionStep(femaleIndex, targetBlend, eased))
                {
                    float value = Mathf.Lerp(startBlend, targetBlend, eased);
                    ApplyBlend(value, femaleIndex, source + " transition", false);
                }

                if (glow)
                {
                    float glowScale = MorphGlowScale(elapsed, seconds);
                    SetMorphGlowScale(glowScale);
                    LogMorphGlowDiag("t=" + t.ToString("0.00")
                        + " elapsed=" + elapsed.ToString("0.00")
                        + " scale=" + glowScale.ToString("0.000"));
                }

                yield return null;
            }

            _blendTransitionCoroutine = null;
            _transitionTimeRatio = -1f;
            SetEntryValue(_cfgBlend, targetBlend);
            ApplyBlend(targetBlend, femaleIndex, source + " transition end", true);

            if (glow)
            {
                SetMorphGlowScale(0f);
                EndMorphGlow();
            }
        }

        private void StartHSceneAutoInitialize(HSceneProc proc)
        {
            StopHSceneAutoInitialize();
            if (_settings == null || !_settings.AutoLoadOnHSceneStart || proc == null)
                return;

            _autoHSceneInitCoroutine = StartCoroutine(AutoInitializeHSceneRoutine(proc));
        }

        private void StopHSceneAutoInitialize()
        {
            if (_autoHSceneInitCoroutine == null)
                return;

            StopCoroutine(_autoHSceneInitCoroutine);
            _autoHSceneInitCoroutine = null;
        }

        private void StopBlendTransition()
        {
            // 遷移を止めたらグローも必ず落とす。途中で切られて光りっぱなしにしない。
            if (!_tattooGlowDriving) EndMorphGlow();

            if (_blendTransitionCoroutine == null)
                return;

            StopCoroutine(_blendTransitionCoroutine);
            _blendTransitionCoroutine = null;
        }

        private IEnumerator AutoInitializeHSceneRoutine(HSceneProc proc)
        {
            const int maxFrames = 600;
            int frame = 0;
            int femaleIndex = TargetFemaleIndex();
            while (frame < maxFrames && proc != null && ReferenceEquals(_currentHScene, proc))
            {
                List<ChaControl> females = GetFemales(proc);
                if (females != null && females.Count > 0)
                {
                    int index = PluginSettings.ClampInt(femaleIndex, 0, females.Count - 1);
                    if (females[index] != null)
                    {
                        femaleIndex = index;
                        break;
                    }
                }

                frame++;
                yield return null;
            }

            _autoHSceneInitCoroutine = null;
            if (proc == null || !ReferenceEquals(_currentHScene, proc))
                yield break;

            if (_settings.AutoCaptureOriginal)
                CaptureOriginal(femaleIndex, "hscene auto start", true);

            bool loaded = LoadActiveTargetCard(femaleIndex, "hscene auto start", true);
            if (_settings.AutoResetBlendOnHSceneStart)
            {
                _settings.Blend = 0f;
                SetEntryValue(_cfgBlend, 0f);
                if (loaded)
                    ApplyBlend(0f, femaleIndex, "hscene auto reset", false);
            }

            LogExecution("hscene auto initialize completed: female=" + femaleIndex + " targetLoaded=" + loaded);
        }

        private int ResolveFemaleIndex(int femaleIndex)
        {
            return femaleIndex < 0 ? TargetFemaleIndex() : PluginSettings.ClampInt(femaleIndex, 0, 1);
        }

        private int ResolveFemaleIndex(ChaControl female)
        {
            HSceneProc hscene = ResolveHScene();
            List<ChaControl> females = GetFemales(hscene);
            if (female == null || females == null)
                return -1;

            for (int i = 0; i < females.Count; i++)
            {
                if (ReferenceEquals(females[i], female))
                    return i;
            }

            return -1;
        }

        private MorphCardRegistration FindSelectedRegisteredCard()
        {
            MorphCardRegistration selected = FindRegisteredCardByWord(_settings.SelectedCardWord);
            if (selected != null)
                return selected;

            string path = PluginSettings.NormalizePath(_settings.TargetCardPath);
            if (!string.IsNullOrWhiteSpace(path))
            {
                selected = EnabledRegisteredCards()
                    .FirstOrDefault(x => string.Equals(PluginSettings.NormalizePath(x.CardPath), path, StringComparison.OrdinalIgnoreCase));
                if (selected != null)
                    return selected;
            }

            MorphCardRegistration[] cards = EnabledRegisteredCards().ToArray();
            return cards.Length == 1 ? cards[0] : null;
        }

        private MorphCardRegistration FindRegisteredCardByWord(string word)
        {
            string key = (word ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(key))
                return null;

            foreach (MorphCardRegistration card in EnabledRegisteredCards())
            {
                foreach (string keyword in GetCardKeywords(card))
                {
                    if (string.Equals(keyword, key, StringComparison.Ordinal))
                        return card;
                }
            }

            return null;
        }

        private IEnumerable<MorphCardRegistration> EnabledRegisteredCards()
        {
            MorphCardRegistration[] cards = _settings != null ? _settings.RegisteredCards : null;
            if (cards == null)
                yield break;

            foreach (MorphCardRegistration card in cards)
            {
                if (card == null || !card.Enabled)
                    continue;

                card.Normalize();
                if (string.IsNullOrWhiteSpace(card.CardPath))
                    continue;

                yield return card;
            }
        }

        private static IEnumerable<string> GetCardKeywords(MorphCardRegistration card)
        {
            if (card == null)
                yield break;

            if (!string.IsNullOrWhiteSpace(card.Word))
                yield return card.Word.Trim();

            string csv = PluginSettings.NormalizeCsv(card.TriggerWords);
            if (string.IsNullOrWhiteSpace(csv))
                yield break;

            string[] parts = csv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                string keyword = (part ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(keyword))
                    yield return keyword;
            }
        }

        private void SelectRegisteredCard(MorphCardRegistration card)
        {
            if (card == null)
                return;

            card.Normalize();
            _settings.SelectedCardWord = card.Word;
            _settings.SelectedCardTriggerWords = card.TriggerWords;
            _settings.TargetCardPath = card.CardPath;
            if (_cfgSelectedCardWord != null && !string.Equals(_cfgSelectedCardWord.Value, card.Word, StringComparison.Ordinal))
                _cfgSelectedCardWord.Value = card.Word;
            if (_cfgSelectedCardTriggerWords != null && !string.Equals(_cfgSelectedCardTriggerWords.Value, card.TriggerWords, StringComparison.Ordinal))
                _cfgSelectedCardTriggerWords.Value = card.TriggerWords;
            if (_cfgTargetCardPath != null && !string.Equals(_cfgTargetCardPath.Value, card.CardPath, StringComparison.Ordinal))
                _cfgTargetCardPath.Value = card.CardPath;
        }

        private void TickAnimationChangeReapply()
        {
            if (!IsEnabled() || _currentHScene == null || _currentHScene.flags == null)
                return;

            string stateName = _currentHScene.flags.nowAnimStateName ?? string.Empty;
            int animationId = _currentHScene.flags.nowAnimationInfo != null ? _currentHScene.flags.nowAnimationInfo.id : -1;
            if (!_hasObservedAnimationState)
            {
                _hasObservedAnimationState = true;
                _lastObservedAnimStateName = stateName;
                _lastObservedAnimationId = animationId;
                return;
            }

            if (string.Equals(_lastObservedAnimStateName, stateName, StringComparison.Ordinal)
                && _lastObservedAnimationId == animationId)
            {
                return;
            }

            _lastObservedAnimStateName = stateName;
            _lastObservedAnimationId = animationId;
            SchedulePostMotionReapply("animation changed id=" + animationId + " state=" + stateName);
        }

        private IEnumerator PostMotionReapplyRoutine(string reason, int femaleIndex)
        {
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "motion changed" : reason;
            if (femaleIndex < 0)
                femaleIndex = TargetFemaleIndex();
            bool anyApplied = false;

            for (int i = 0; i < 6; i++)
            {
                yield return null;
                anyApplied |= ReapplyCurrentMorphState(femaleIndex, safeReason + " pass=" + (i + 1), false, true);
            }

            _postMotionReapplyCoroutine = null;
            if (anyApplied)
                LogExecution("post motion reapply completed: female=" + femaleIndex + " reason=" + safeReason);
        }

        private IEnumerator ExternalReapplyRoutine(string reason, int femaleIndex)
        {
            string safeReason = string.IsNullOrWhiteSpace(reason) ? "external" : reason;
            if (femaleIndex < 0)
                femaleIndex = TargetFemaleIndex();

            bool anyApplied = false;
            for (int i = 0; i < 90; i++)
            {
                yield return null;
                // Refreshes from other plugins can arrive late. Reassert for the first
                // six frames, then periodically until the 90-frame guard ends.
                bool forceVisualRefresh = i < 6 || (i + 1) % 10 == 0;
                anyApplied |= ReapplyCurrentMorphState(femaleIndex, safeReason + " pass=" + (i + 1), false, forceVisualRefresh);
            }

            _externalReapplyCoroutine = null;
            if (anyApplied)
                LogExecution("external reapply completed: female=" + femaleIndex + " reason=" + safeReason);
        }

        private IEnumerator ClothesChangeReapplyRoutine(
            ChaControl female,
            int femaleIndex,
            string reason,
            byte[] preservedClothesState)
        {
            // SetClothesState系のPostfix後、ゲーム側の衣装処理を先に完了させる。
            yield return null;
            yield return null;

            bool applied = false;
            bool restored = false;
            _suppressVisualRefreshReapply = true;
            try
            {
                applied = ReapplyCurrentMorphStateForClothes(femaleIndex, reason + " morph once");
                restored = RestoreClothesState(female, preservedClothesState);
            }
            finally
            {
                _suppressVisualRefreshReapply = false;
                _clothesChangeReapplyCoroutine = null;
            }

            if (applied)
            {
                LogExecution(
                    "clothes change morph reapplied incrementally: female=" + femaleIndex
                    + " clothesRestored=" + restored
                    + " reason=" + reason);
            }
        }

        private static bool IsClothesRefreshMethod(string methodName)
        {
            return string.Equals(methodName, "SetClothesState", StringComparison.Ordinal)
                || string.Equals(methodName, "SetClothesStatePrev", StringComparison.Ordinal)
                || string.Equals(methodName, "SetClothesStateNext", StringComparison.Ordinal)
                || string.Equals(methodName, "SetClothesStateAll", StringComparison.Ordinal)
                || string.Equals(methodName, "UpdateClothesStateAll", StringComparison.Ordinal)
                || string.Equals(methodName, "RandomChangeOfClothesLowPoly", StringComparison.Ordinal)
                || string.Equals(methodName, "RandomChangeOfClothesLowPolyEnd", StringComparison.Ordinal)
                || string.Equals(methodName, "ChangeToiletStateLowPoly", StringComparison.Ordinal);
        }

        private static bool IsClothesReason(string reason)
        {
            return !string.IsNullOrWhiteSpace(reason)
                && reason.IndexOf("clothes", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool RestoreClothesState(ChaControl female, byte[] preserved)
        {
            byte[] current = female?.fileStatus?.clothesState;
            if (current == null || preserved == null)
                return false;

            bool restored = false;
            int count = Math.Min(current.Length, preserved.Length);
            for (int i = 0; i < count; i++)
            {
                if (current[i] == preserved[i])
                    continue;

                female.SetClothesState(i, preserved[i], next: false);
                restored = true;
            }

            return restored;
        }

        private bool ReapplyCurrentMorphState(int femaleIndex, string reason, bool writeLog, bool forceVisualRefresh)
        {
            bool applied = false;
            bool hasBlendState =
                _originalSnapshot != null
                && _targetSnapshot != null
                && _originalFemaleIndex == ResolveFemaleIndex(femaleIndex)
                && _targetFemaleIndex == ResolveFemaleIndex(femaleIndex)
                && _settings != null
                && Math.Abs(_settings.Blend) > 0.0001f;

            if (hasBlendState)
                applied |= ApplyBlend(_settings.Blend, femaleIndex, reason + " blend", writeLog,
                    forceVisualRefresh, preserveDirectOverrides: true);

            ChaControl target = null;
            if (_hasHeightOverride || _hasBreastOverride || _hasDirectBodyOverride || _hasDirectFaceOverride)
                TryResolveTarget(femaleIndex, out target, out _);

            if (_hasHeightOverride && target != null
                && (forceVisualRefresh || target.GetShapeBodyValue(0) != _directHeightValue))
                applied |= SetBodyShape(0, _directHeightValue, femaleIndex, reason + " height", writeLog, false);

            if (_hasBreastOverride && target != null
                && (forceVisualRefresh || target.GetShapeBodyValue(4) != _directBreastValue))
                applied |= SetBodyShape(4, _directBreastValue, femaleIndex, reason + " breast", writeLog, false);

            if (_hasDirectBodyOverride && _directBodyShapeIndex != 0 && _directBodyShapeIndex != 4
                && target != null
                && (forceVisualRefresh || target.GetShapeBodyValue(_directBodyShapeIndex) != _directBodyShapeValue))
                applied |= SetBodyShape(_directBodyShapeIndex, _directBodyShapeValue, femaleIndex, reason + " body", writeLog, false);

            if (_hasDirectFaceOverride && target != null
                && (forceVisualRefresh || target.GetShapeFaceValue(_directFaceShapeIndex) != _directFaceShapeValue))
                applied |= SetFaceShape(_directFaceShapeIndex, _directFaceShapeValue, femaleIndex, reason + " face", writeLog, false);

            return applied;
        }

        private bool ReapplyCurrentMorphStateForClothes(int femaleIndex, string reason)
        {
            femaleIndex = ResolveFemaleIndex(femaleIndex);
            if (!TryResolveTarget(femaleIndex, out ChaControl target, out _))
                return false;

            bool bodyApplied = false;
            bool faceApplied = false;
            bool hasBlendState =
                _originalSnapshot != null
                && _targetSnapshot != null
                && _originalFemaleIndex == femaleIndex
                && _targetFemaleIndex == femaleIndex
                && _settings != null
                && Math.Abs(_settings.Blend) > 0.0001f;

            if (hasBlendState)
            {
                float blend = Mathf.Clamp01(_settings.Blend);
                float[] currentBody = target.chaFile?.custom?.body?.shapeValueBody;
                if (currentBody != null && _originalSnapshot.Body != null && _targetSnapshot.Body != null)
                {
                    int count = Math.Min(
                        currentBody.Length,
                        Math.Min(_originalSnapshot.Body.Length, _targetSnapshot.Body.Length));
                    for (int i = 0; i < count; i++)
                    {
                        float value = Mathf.Lerp(_originalSnapshot.Body[i], _targetSnapshot.Body[i], blend);
                        bodyApplied |= target.SetShapeBodyValue(i, value);
                    }
                }

                float[] currentFace = target.chaFile?.custom?.face?.shapeValueFace;
                if (currentFace != null && _originalSnapshot.Face != null && _targetSnapshot.Face != null)
                {
                    int count = Math.Min(
                        currentFace.Length,
                        Math.Min(_originalSnapshot.Face.Length, _targetSnapshot.Face.Length));
                    for (int i = 0; i < count; i++)
                    {
                        float value = Mathf.Lerp(_originalSnapshot.Face[i], _targetSnapshot.Face[i], blend);
                        faceApplied |= target.SetShapeFaceValue(i, value);
                    }
                }
            }

            if (_hasHeightOverride)
                bodyApplied |= target.SetShapeBodyValue(0, _directHeightValue);
            if (_hasBreastOverride)
                bodyApplied |= target.SetShapeBodyValue(4, _directBreastValue);
            if (_hasDirectBodyOverride && _directBodyShapeIndex != 0 && _directBodyShapeIndex != 4)
                bodyApplied |= target.SetShapeBodyValue(_directBodyShapeIndex, _directBodyShapeValue);
            if (_hasDirectFaceOverride)
                faceApplied |= target.SetShapeFaceValue(_directFaceShapeIndex, _directFaceShapeValue);

            if (bodyApplied)
            {
                RefreshHAfterBodyChange(femaleIndex, reason, false);
                SyncHeightBreastFromTarget(target);
            }

            return bodyApplied || faceApplied;
        }

        private void TrackDirectBodyOverride(int index, float value)
        {
            _hasDirectBodyOverride = true;
            _directBodyShapeIndex = PluginSettings.ClampInt(index, 0, 43);
            _directBodyShapeValue = PluginSettings.Round2(PluginSettings.Clamp01(value));

            if (_directBodyShapeIndex == 0)
            {
                _hasHeightOverride = true;
                _directHeightValue = _directBodyShapeValue;
            }
            else if (_directBodyShapeIndex == 4)
            {
                _hasBreastOverride = true;
                _directBreastValue = _directBodyShapeValue;
            }
        }

        private void TrackDirectFaceOverride(int index, float value)
        {
            _hasDirectFaceOverride = true;
            _directFaceShapeIndex = PluginSettings.ClampInt(index, 0, 51);
            _directFaceShapeValue = PluginSettings.Round2(PluginSettings.Clamp01(value));
        }

        private bool TryResolveTarget(int femaleIndex, out ChaControl target, out string error)
        {
            target = null;
            error = null;

            HSceneProc hscene = ResolveHScene();
            if (hscene == null)
            {
                error = "HSceneProc not found";
                return false;
            }

            List<ChaControl> females = GetFemales(hscene);
            if (females == null || females.Count == 0)
            {
                error = "female list empty";
                return false;
            }

            int index = PluginSettings.ClampInt(femaleIndex, 0, females.Count - 1);
            target = females[index];
            if (target == null)
            {
                error = "target female is null";
                return false;
            }

            _settings.TargetFemaleIndex = index;
            return true;
        }

        private HSceneProc ResolveHScene()
        {
            if (_currentHScene != null)
                return _currentHScene;

            _currentHScene = UnityEngine.Object.FindObjectOfType<HSceneProc>();
            return _currentHScene;
        }

        private static List<ChaControl> GetFemales(HSceneProc hscene)
        {
            // キャラクリなどHシーン外では hscene が null になる。
            // インスタンスフィールドに null を渡すと TargetException が飛び、
            // Harmonyのpostfix経由でゲーム側の処理まで巻き添えで止まる。
            if (hscene == null)
                return null;

            return FieldLstFemale?.GetValue(hscene) as List<ChaControl>;
        }

        private static List<MotionIK> GetMotionIks(HSceneProc hscene)
        {
            return FieldLstMotionIk?.GetValue(hscene) as List<MotionIK>;
        }

        private static MorphSnapshot SnapshotFrom(ChaControl target)
        {
            if (target == null || target.chaFile == null || target.chaFile.custom == null)
                return null;

            return new MorphSnapshot
            {
                Body = CopyArray(target.chaFile.custom.body?.shapeValueBody),
                Face = CopyArray(target.chaFile.custom.face?.shapeValueFace)
            };
        }

        private static MorphSnapshot SnapshotFrom(ChaFileControl file)
        {
            if (file == null || file.custom == null)
                return null;

            return new MorphSnapshot
            {
                Body = CopyArray(file.custom.body?.shapeValueBody),
                Face = CopyArray(file.custom.face?.shapeValueFace)
            };
        }

        private static float[] CopyArray(float[] source)
        {
            if (source == null)
                return null;

            var copy = new float[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static byte[] CopyArray(byte[] source)
        {
            if (source == null)
                return null;

            var copy = new byte[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }

        private static bool ApplyBodyBlend(ChaControl target, float[] original, float[] targetValues, float blend,
            int skipIndexA = -1, int skipIndexB = -1, int skipIndexC = -1)
        {
            if (target == null || target.chaFile == null || original == null || targetValues == null)
                return false;

            float[] values = target.chaFile.custom.body.shapeValueBody;
            int count = Math.Min(values.Length, Math.Min(original.Length, targetValues.Length));
            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                if (i == skipIndexA || i == skipIndexB || i == skipIndexC) continue;
                float value = Mathf.Lerp(original[i], targetValues[i], blend);
                if (values[i] == value) continue;
                values[i] = value;
                changed = true;
            }

            return changed;
        }

        private static bool ApplyFaceBlend(ChaControl target, float[] original, float[] targetValues, float blend,
            int skipIndex = -1)
        {
            if (target == null || target.chaFile == null || original == null || targetValues == null)
                return false;

            float[] values = target.chaFile.custom.face.shapeValueFace;
            int count = Math.Min(values.Length, Math.Min(original.Length, targetValues.Length));
            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                if (i == skipIndex) continue;
                float value = Mathf.Lerp(original[i], targetValues[i], blend);
                if (values[i] == value) continue;
                values[i] = value;
                changed = true;
            }

            return changed;
        }

        private static bool ApplySnapshotBody(ChaControl target, float[] source)
        {
            if (target == null || target.chaFile == null || source == null)
                return false;

            float[] values = target.chaFile.custom.body.shapeValueBody;
            int count = Math.Min(values.Length, source.Length);
            Array.Copy(source, values, count);
            return count > 0;
        }

        private static bool ApplySnapshotFace(ChaControl target, float[] source)
        {
            if (target == null || target.chaFile == null || source == null)
                return false;

            float[] values = target.chaFile.custom.face.shapeValueFace;
            int count = Math.Min(values.Length, source.Length);
            Array.Copy(source, values, count);
            return count > 0;
        }

        private void SyncHeightBreastFromTarget(ChaControl target)
        {
            if (target == null)
                return;

            float height = PluginSettings.Round2(target.GetShapeBodyValue(0));
            float breast = PluginSettings.Round2(target.GetShapeBodyValue(4));
            _settings.Height = height;
            _settings.Breast = breast;
            SetEntryValue(_cfgHeight, height);
            SetEntryValue(_cfgBreast, breast);
        }

        private void RefreshHAfterBodyChange(int femaleIndex, string reason, bool writeLog = true)
        {
            HSceneProc hscene = ResolveHScene();
            if (hscene == null)
                return;

            RefreshAnimatorShapeParameters(hscene);
            RecalculateMotionIk(hscene, reason, writeLog);
        }

        private void RefreshAnimatorShapeParameters(HSceneProc hscene)
        {
            List<ChaControl> females = GetFemales(hscene);
            if (females == null || females.Count == 0)
                return;

            ChaControl female0 = females.Count > 0 ? females[0] : null;
            ChaControl female1 = females.Count > 1 ? females[1] : null;
            ChaControl male = FieldMale?.GetValue(hscene) as ChaControl;
            ChaControl male1 = FieldMale1?.GetValue(hscene) as ChaControl;
            ItemObject item = FieldItem?.GetValue(hscene) as ItemObject;

            float h0 = female0 != null ? female0.GetShapeBodyValue(0) : 0f;
            float b0 = female0 != null ? female0.GetShapeBodyValue(4) : 0f;
            float h1 = female1 != null ? female1.GetShapeBodyValue(0) : h0;
            float b1 = female1 != null ? female1.GetShapeBodyValue(4) : b0;

            SetAnimatorFloat(female0, "height", h0);
            SetAnimatorFloat(female0, "Breast", b0);
            SetAnimatorFloat(female0, "height1", h1);
            SetAnimatorFloat(female0, "Breast1", b1);

            if (female1 != null)
            {
                SetAnimatorFloat(female1, "height", h1);
                SetAnimatorFloat(female1, "Breast", b1);
                SetAnimatorFloat(female1, "height1", h0);
                SetAnimatorFloat(female1, "Breast1", b0);
            }

            SetAnimatorFloat(male, "height", h0);
            SetAnimatorFloat(male1, "height", h0);

            if (item != null)
            {
                int id = hscene.flags != null && hscene.flags.nowAnimationInfo != null ? hscene.flags.nowAnimationInfo.id : 0;
                bool secondIsPrimary = female1 != null && (id % 2) != 0;
                item.SetAnimatorParamFloat("height", secondIsPrimary ? h1 : h0);
                item.SetAnimatorParamFloat("height1", secondIsPrimary ? h0 : h1);
                item.SetAnimatorParamFloat("Breast", secondIsPrimary ? b1 : b0);
                item.SetAnimatorParamFloat("Breast1", secondIsPrimary ? b0 : b1);
            }
        }

        private static void SetAnimatorFloat(ChaControl cha, string key, float value)
        {
            if (cha != null)
                cha.setAnimatorParamFloat(key, value);
        }

        private void RecalculateMotionIk(HSceneProc hscene, string reason, bool writeLog = true)
        {
            List<MotionIK> motionIks = GetMotionIks(hscene);
            if (motionIks == null || motionIks.Count == 0)
                return;

            string stateName = hscene.flags != null ? hscene.flags.nowAnimStateName : null;
            if (string.IsNullOrEmpty(stateName))
                stateName = "Idle";

            int count = 0;
            foreach (MotionIK motionIk in motionIks.Where(x => x != null))
            {
                try
                {
                    motionIk.Calc(stateName);
                    count++;
                }
                catch (Exception ex)
                {
                    LogWarnAlways("MotionIK recalc failed: " + ex.Message);
                }
            }

            if (writeLog)
                LogExecution("MotionIK recalculated: state=" + stateName + " count=" + count + " reason=" + reason);
        }
    }
}
