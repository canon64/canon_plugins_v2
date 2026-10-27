using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MainGameHScenePoseInjector
{
    /// <summary>
    /// ポーズと表情の自動変更。
    ///
    /// 系統ごとに独立した時計を持つ。ポーズ・表情プリセット・眉・目・口が
    /// それぞれ自分の保持秒数で動き、互いに待ち合わせはしない。
    /// 一括モード中は全系統が一括の順序と秒数に従う(個別値は消さずに無視するだけ)。
    /// </summary>
    public sealed partial class Plugin
    {
        private sealed class CycleClock
        {
            public float NextAt;
            public int Index = -1;
        }

        private readonly CycleClock _poseClock = new CycleClock();
        private readonly CycleClock _presetClock = new CycleClock();
        private readonly CycleClock _eyebrowClock = new CycleClock();
        private readonly CycleClock _eyesClock = new CycleClock();
        private readonly CycleClock _mouthClock = new CycleClock();
        private readonly CycleClock _hohoClock = new CycleClock();
        private readonly CycleClock _tearsClock = new CycleClock();

        /// <summary>頬赤の既定段階(百分率)。選択が空ならこれを回す。</summary>
        internal static readonly int[] DefaultHohoLevels = { 0, 25, 50, 75, 100 };
        /// <summary>涙の既定段階。ゲーム側 tearsFlags が bool[4,3] なので0〜3。</summary>
        internal static readonly int[] DefaultTearsLevels = { 0, 1, 2, 3 };

        private System.Random _cycleRandom;
        private Dictionary<int, string> _poseCategoryCache;
        private bool _autoNormalized;

        private AutoCycleSettings Auto
        {
            get
            {
                if (_settings == null) return null;
                if (_settings.AutoCycle == null) _settings.AutoCycle = new AutoCycleSettings();
                // 読み込み直後に 0 が入っている場合の保険。
                // 遷移秒数が 0 だと補間せずジャンプするため、ここでも必ず通す。
                if (!_autoNormalized) { _autoNormalized = true; _settings.AutoCycle.Normalize(); }
                return _settings.AutoCycle;
            }
        }

        /// <summary>毎フレーム呼ぶ。止まっている時は何もしない。</summary>
        private void UpdateAutoCycle()
        {
            AutoCycleSettings a = Auto;
            if (a == null || !a.Enabled) return;
            if (!IsInHScene()) return;

            float now = Time.unscaledTime;

            if (a.PoseEnabled) TickPoseCycle(a, now);

            if (a.ExpressionEnabled)
            {
                if ((ExpressionCycleSource)a.ExpressionSource == ExpressionCycleSource.Preset)
                    TickPresetCycle(a, now);
                else
                    TickPerChannelCycle(a, now);
            }
        }

        /// <summary>自動変更の開始/停止。開始時は時計を今に合わせ、1回目をすぐ出す。</summary>
        internal void SetAutoCycleEnabled(bool enabled)
        {
            AutoCycleSettings a = Auto;
            if (a == null) return;
            a.Enabled = enabled;
            if (!enabled) return;

            float now = Time.unscaledTime;
            _poseClock.NextAt = now;
            _presetClock.NextAt = now;
            _eyebrowClock.NextAt = now;
            _eyesClock.NextAt = now;
            _mouthClock.NextAt = now;
            LogInfo("auto cycle enabled bulk=" + a.BulkMode
                + " poseOrder=" + a.EffectiveOrder(a.PoseOrder)
                + " poseHold=" + a.EffectiveHold(a.PoseHoldSeconds).ToString("0.##"));
        }

        // ── ポーズ ────────────────────────────────────────────────────────

        private void TickPoseCycle(AutoCycleSettings a, float now)
        {
            if (now < _poseClock.NextAt) return;

            // 有効化直後の Update はまだそのフレームの IK 後の姿勢を持たない。
            // 最初の LateUpdate で一度だけ採取してから切り替える。
            if (a.EffectiveTransition() > 0f && !_liveSnapshotValid && !_poseSnapshotWaited)
            {
                _poseSnapshotWaited = true;
                return;
            }

            List<PoseEntry> candidates = ResolvePoseCandidates(a);
            if (candidates.Count == 0)
            {
                // 対象が無い時に毎フレーム探し直さない。
                _poseClock.NextAt = now + 2f;
                _liveSnapshotValid = false;
                return;
            }

            _poseClock.Index = NextIndex(_poseClock.Index, candidates.Count, a.EffectiveOrder(a.PoseOrder));
            PoseEntry pose = candidates[_poseClock.Index];

            // 切り替える「前」の姿勢を先に記録する。
            // ApplyPose はコントローラごと差し替えて即座に新ポーズへ飛ぶので、
            // 適用後に記録しても混ぜる相手が残っていない。
            float transition = a.EffectiveTransition();
            if (transition > 0f) CapturePoseBlendSnapshot();

            if (ApplyPose(pose))
            {
                if (transition > 0f) BeginPoseBlend(transition);
            }
            else
            {
                ClearPoseBlend();
            }

            _poseClock.NextAt = now + a.EffectiveHold(a.PoseHoldSeconds);
        }

        // ── ポーズ遷移(骨補間) ────────────────────────────────────────────
        //
        // ポーズごとにアニメーターコントローラを丸ごと差し替えるため、
        // Unity標準のクロスフェードは使えない(別コントローラ間は混ざらない)。
        // そこで切り替え直前の骨の姿勢を控えておき、切り替え後は
        // アニメーターが書いた結果へ向けて骨単位で補間する。
        // LateUpdate はアニメーション適用の後に走るので、そこで上書きする。

        private sealed class PoseBlendState
        {
            public ChaControl Cha;
            public Transform[] Bones;
            public Quaternion[] FromRotations;
            // 位置も骨ごとに控える。
            // 回転だけ混ぜても、腰の移動はアニメーションが位置で書くため
            // そこだけ瞬間移動して「腰が飛ぶ」ように見える。
            public Vector3[] FromPositions;
            public Vector3 FromRootPosition;
            public Transform Root;

            // キャラ本体の transform。objBodyBone の外にあるため骨の走査に入らない。
            // ポーズ差し替えでここが動くと、姿勢は正しいのに立ち位置だけ飛ぶ。
            public Transform CharaRoot;
            public Vector3 FromCharaPos;
            public Quaternion FromCharaRot;
            public float NextBoneRefreshAt;
        }

        private readonly List<PoseBlendState> _poseBlends = new List<PoseBlendState>();
        private float _poseBlendStart;
        private float _poseBlendDuration;
        private int _poseBlendFrames;

        // 画面に出ている姿勢を毎フレーム控え続ける。
        //
        // Update の時点で骨を読むと、今フレームのアニメーションもIKも通る前の値
        // (＝元のHモーション側の残骸)しか取れず、遷移が毎回そこから始まってしまう。
        // IKまで全部終わった LateUpdate の最後で読んだものだけが、実際に見えている姿勢。
        private readonly List<PoseBlendState> _liveSnapshot = new List<PoseBlendState>();
        private readonly List<PoseBlendState> _liveSnapshotCache = new List<PoseBlendState>();
        private readonly List<ChaControl> _liveFemales = new List<ChaControl>();
        private HSceneProc _liveSnapshotScene;
        private bool _liveSnapshotValid;
        private bool _poseSnapshotWaited;

        /// <summary>LateUpdateの最後に呼ぶ。今表示されている姿勢を控える。</summary>
        private void CaptureLiveSnapshot()
        {
            AutoCycleSettings auto = Auto;
            if (auto == null || !auto.Enabled || !auto.PoseEnabled || auto.EffectiveTransition() <= 0f)
            {
                _liveSnapshot.Clear();
                _liveSnapshotValid = false;
                _poseSnapshotWaited = false;
                return;
            }
            // 遷移中は書き換えている最中なので控え直さない(混ぜた結果を控えてしまう)。
            if (_poseBlendDuration > 0f) return;
            if (!IsInHScene())
            {
                _liveSnapshot.Clear();
                _liveSnapshotValid = false;
                return;
            }

            // 次のポーズ変更直前の LateUpdate だけ、IK後の姿勢を控える。
            // 想定より長いフレームが来た場合は TickPoseCycle が1フレーム待ち、
            // その間にここで最新の姿勢を採取する。
            if (Time.unscaledTime + Mathf.Max(Time.unscaledDeltaTime, 0.05f) < _poseClock.NextAt)
                return;

            if (!ReferenceEquals(_liveSnapshotScene, _activeHSceneProc))
            {
                _liveSnapshotScene = _activeHSceneProc;
                _liveSnapshotCache.Clear();
            }

            ResolveFemalesInto(_liveFemales);
            _liveSnapshot.Clear();
            for (int femaleIndex = 0; femaleIndex < _liveFemales.Count; femaleIndex++)
            {
                ChaControl cha = _liveFemales[femaleIndex];
                if (cha == null || cha.objBodyBone == null) continue;
                try
                {
                    Transform root = cha.objBodyBone.transform;
                    PoseBlendState state = null;
                    for (int i = 0; i < _liveSnapshotCache.Count; i++)
                    {
                        if (ReferenceEquals(_liveSnapshotCache[i].Cha, cha))
                        {
                            state = _liveSnapshotCache[i];
                            break;
                        }
                    }
                    if (state == null)
                    {
                        state = new PoseBlendState { Cha = cha };
                        _liveSnapshotCache.Add(state);
                    }
                    if (state.Bones == null || state.Root != root || Time.unscaledTime >= state.NextBoneRefreshAt)
                    {
                        state.Bones = cha.objBodyBone.GetComponentsInChildren<Transform>(true);
                        if (state.Bones == null || state.Bones.Length == 0) continue;
                        state.FromRotations = new Quaternion[state.Bones.Length];
                        state.FromPositions = new Vector3[state.Bones.Length];
                        state.NextBoneRefreshAt = Time.unscaledTime + 2f;
                    }

                    bool missingBone = false;
                    for (int i = 0; i < state.Bones.Length; i++)
                    {
                        Transform bone = state.Bones[i];
                        if (bone == null) missingBone = true;
                        state.FromRotations[i] = bone != null ? bone.localRotation : Quaternion.identity;
                        state.FromPositions[i] = bone != null ? bone.localPosition : Vector3.zero;
                    }
                    if (missingBone) state.NextBoneRefreshAt = 0f;

                    state.Root = root;
                    state.FromRootPosition = root.localPosition;
                    state.CharaRoot = cha.transform;
                    state.FromCharaPos = cha.transform.localPosition;
                    state.FromCharaRot = cha.transform.localRotation;
                    _liveSnapshot.Add(state);
                }
                catch { }
            }
            _liveSnapshotValid = _liveSnapshot.Count > 0;
            if (_liveSnapshotValid)
                _poseSnapshotWaited = false;
        }

        /// <summary>切り替え直前の姿勢として、最後に控えた「見えている姿勢」を使う。</summary>
        private void CapturePoseBlendSnapshot()
        {
            _poseBlends.Clear();

            // 常時控えがあればそれを使う。これが実際に画面に出ていた姿勢。
            if (_liveSnapshotValid && _liveSnapshot.Count > 0)
            {
                for (int i = 0; i < _liveSnapshot.Count; i++)
                {
                    PoseBlendState src = _liveSnapshot[i];
                    if (src == null || src.Cha == null) continue;
                    _poseBlends.Add(new PoseBlendState
                    {
                        Cha = src.Cha,
                        Bones = src.Bones,
                        FromRotations = (Quaternion[])src.FromRotations.Clone(),
                        FromPositions = src.FromPositions != null ? (Vector3[])src.FromPositions.Clone() : null,
                        Root = src.Root,
                        FromRootPosition = src.FromRootPosition,
                        CharaRoot = src.CharaRoot,
                        FromCharaPos = src.FromCharaPos,
                        FromCharaRot = src.FromCharaRot,
                    });
                }
                LogInfo("[blend] source=live-snapshot chars=" + _poseBlends.Count);
                return;
            }

            // 控えが無い初回だけ、その場で読む(Hモーション側の値になるが他に手が無い)。
            LogInfo("[blend] source=fallback-update-read");
            foreach (ChaControl cha in ResolveFemales())
            {
                if (cha == null || cha.objBodyBone == null) continue;
                try
                {
                    Transform[] bones = cha.objBodyBone.GetComponentsInChildren<Transform>(true);
                    if (bones == null || bones.Length == 0) continue;

                    Quaternion[] rots = new Quaternion[bones.Length];
                    Vector3[] poss = new Vector3[bones.Length];
                    for (int i = 0; i < bones.Length; i++)
                    {
                        rots[i] = bones[i] != null ? bones[i].localRotation : Quaternion.identity;
                        poss[i] = bones[i] != null ? bones[i].localPosition : Vector3.zero;
                    }

                    Transform root = cha.objBodyBone.transform;
                    _poseBlends.Add(new PoseBlendState
                    {
                        Cha = cha,
                        Bones = bones,
                        FromRotations = rots,
                        FromPositions = poss,
                        Root = root,
                        FromRootPosition = root != null ? root.localPosition : Vector3.zero,
                        CharaRoot = cha.transform,
                        FromCharaPos = cha.transform.localPosition,
                        FromCharaRot = cha.transform.localRotation,
                    });
                }
                catch (Exception ex)
                {
                    LogWarn("pose blend capture failed: " + ex.Message);
                }
            }
        }

        private void BeginPoseBlend(float seconds)
        {
            if (_poseBlends.Count == 0)
            {
                LogInfo("[blend] skipped: no snapshot");
                return;
            }
            _poseBlendStart = Time.unscaledTime;
            _poseBlendDuration = seconds;
            _poseBlendFrames = 0;

            int bones = 0;
            for (int i = 0; i < _poseBlends.Count; i++)
                if (_poseBlends[i] != null && _poseBlends[i].Bones != null) bones += _poseBlends[i].Bones.Length;

            LogInfo("[blend] start duration=" + seconds.ToString("0.##")
                + " chars=" + _poseBlends.Count + " bones=" + bones);
        }

        /// <summary>
        /// ポーズ遷移の進行度。0=旧ポーズ、1=新ポーズ、遷移していない時は -1。
        /// 端を滑らかにした後の値なので、これに乗れば時間もカーブも完全に一致する。
        ///
        /// カメラ側(MainGameCameraControl)がリフレクションで読む。ポーズ遷移は
        /// Unity のクロスフェードではなくここが骨を直接補間しているため、
        /// 向こうからは Animator を見ても遷移していることが分からない。
        /// 読まれている前提の公開フィールドなので、名前と意味を変えない。
        /// </summary>
        public static float PoseBlendProgress = -1f;

        private void ClearPoseBlend()
        {
            _poseBlends.Clear();
            _poseBlendDuration = 0f;
            PoseBlendProgress = -1f;
            // 遷移が終わったら、次フレームから新しい姿勢を控え直す。
            _liveSnapshotValid = false;
        }

        /// <summary>
        /// LateUpdate から呼ぶ。アニメーターが書いた新ポーズと、控えた旧ポーズを混ぜる。
        /// t=0 で旧ポーズ、t=1 で新ポーズ。
        /// </summary>
        private void UpdatePoseBlend()
        {
            if (_poseBlendDuration <= 0f || _poseBlends.Count == 0) return;

            float t = (Time.unscaledTime - _poseBlendStart) / _poseBlendDuration;
            if (t >= 1f)
            {
                LogInfo("[blend] end frames=" + _poseBlendFrames);
                ClearPoseBlend();
                return;
            }
            if (t < 0f) t = 0f;
            _poseBlendFrames++;

            // 端で急に動き出さないよう滑らかにする。
            float k = t * t * (3f - 2f * t);
            PoseBlendProgress = k;

            // 旧姿勢を保持する重み。1で旧ポーズのまま固まり、0で新ポーズ。
            // 遷移秒数をかけてこれを抜いていく。
            float holdWeight = 1f - k;
            if (_poseBlendFrames == 1 || _poseBlendFrames % 30 == 0)
                LogInfo("[blend] t=" + t.ToString("0.00") + " holdWeight=" + holdWeight.ToString("0.00")
                    + " frames=" + _poseBlendFrames);

            for (int b = 0; b < _poseBlends.Count; b++)
            {
                PoseBlendState st = _poseBlends[b];
                if (st == null || st.Cha == null || st.Bones == null) continue;

                Transform[] bones = st.Bones;
                Quaternion[] from = st.FromRotations;
                Vector3[] fromPos = st.FromPositions;
                for (int i = 0; i < bones.Length; i++)
                {
                    Transform bone = bones[i];
                    if (bone == null) continue;
                    bone.localRotation = Quaternion.Slerp(from[i], bone.localRotation, k);
                    // 位置も混ぜる。腰などアニメーションが位置を書く骨がここで飛ぶ。
                    if (fromPos != null && i < fromPos.Length)
                        bone.localPosition = Vector3.Lerp(fromPos[i], bone.localPosition, k);
                }

                if (st.Root != null)
                    st.Root.localPosition = Vector3.Lerp(st.FromRootPosition, st.Root.localPosition, k);

                // キャラ本体の立ち位置と向き。ここが飛ぶと「姿勢はそのままで全身が移動」に見える。
                if (st.CharaRoot != null)
                {
                    st.CharaRoot.localPosition = Vector3.Lerp(st.FromCharaPos, st.CharaRoot.localPosition, k);
                    st.CharaRoot.localRotation = Quaternion.Slerp(st.FromCharaRot, st.CharaRoot.localRotation, k);
                }
            }
        }

        private List<PoseEntry> ResolvePoseCandidates(AutoCycleSettings a)
        {
            List<PoseEntry> all = GetPoseList() ?? new List<PoseEntry>();

            if (a.PoseIdWhitelist != null && a.PoseIdWhitelist.Count > 0)
            {
                HashSet<int> ids = new HashSet<int>(a.PoseIdWhitelist);
                return all.Where(p => p != null && ids.Contains(p.Id)).ToList();
            }

            if (a.PoseCategories != null && a.PoseCategories.Count > 0)
            {
                HashSet<string> cats = new HashSet<string>(a.PoseCategories);
                Dictionary<int, string> map = GetPoseCategoryMap();
                return all.Where(p =>
                {
                    if (p == null) return false;
                    string c;
                    if (!map.TryGetValue(p.Id, out c)) c = "未分類";
                    return cats.Contains(c);
                }).ToList();
            }

            return all.Where(p => p != null).ToList();
        }

        /// <summary>ポーズID→カテゴリ。辞書ファイルを1度だけ読んで持っておく。</summary>
        private Dictionary<int, string> GetPoseCategoryMap()
        {
            if (_poseCategoryCache != null) return _poseCategoryCache;

            _poseCategoryCache = new Dictionary<int, string>();
            try
            {
                string path = Path.Combine(PluginDir, PoseCategoryDictionaryFileName);
                PoseCategoryDictionaryFile dict = LoadPoseCategoryDictionary(path);
                if (dict != null && dict.poses != null)
                {
                    foreach (PoseCategoryEntry e in dict.poses)
                    {
                        if (e == null) continue;
                        _poseCategoryCache[e.id] = string.IsNullOrEmpty(e.category) ? "未分類" : e.category;
                    }
                }
            }
            catch (Exception ex)
            {
                LogWarn("pose category map load failed: " + ex.Message);
            }
            return _poseCategoryCache;
        }

        /// <summary>カテゴリ一覧(UI用)。</summary>
        internal IEnumerable<string> GetSelectablePoseCategories()
        {
            return ValidPoseCategories;
        }

        // ── 表情(プリセット単位) ──────────────────────────────────────────

        private void TickPresetCycle(AutoCycleSettings a, float now)
        {
            if (now < _presetClock.NextAt) return;

            List<ExpressionPreset> presets = ResolvePresetCandidates(a);
            if (presets.Count == 0)
            {
                _presetClock.NextAt = now + 2f;
                return;
            }

            _presetClock.Index = NextIndex(_presetClock.Index, presets.Count, a.EffectiveOrder(a.PresetOrder));
            ExpressionPreset preset = presets[_presetClock.Index];
            bool blend = a.EffectiveBlend(a.PresetBlend);

            foreach (ChaControl cha in ResolveFemales())
            {
                if (cha == null) continue;
                try
                {
                    if (preset.EyebrowPtn >= 0) { cha.ChangeEyebrowPtn(preset.EyebrowPtn, blend); _lockedEyebrowPtn = preset.EyebrowPtn; }
                    if (preset.EyesPtn >= 0) { cha.ChangeEyesPtn(preset.EyesPtn, blend); _lockedEyesPtn = preset.EyesPtn; }
                    if (preset.MouthPtn >= 0) { cha.ChangeMouthPtn(preset.MouthPtn, blend); _lockedMouthPtn = preset.MouthPtn; }
                }
                catch (Exception ex)
                {
                    LogWarn("preset cycle apply failed: " + ex.Message);
                }
            }

            _presetClock.NextAt = now + a.EffectiveHold(a.PresetHoldSeconds);
        }

        private List<ExpressionPreset> ResolvePresetCandidates(AutoCycleSettings a)
        {
            List<ExpressionPreset> all = _settings != null && _settings.ExpressionPresets != null
                ? _settings.ExpressionPresets
                : new List<ExpressionPreset>();

            if (a.PresetWhitelist == null || a.PresetWhitelist.Count == 0)
                return all.Where(p => p != null).ToList();

            HashSet<string> names = new HashSet<string>(a.PresetWhitelist);
            return all.Where(p => p != null && names.Contains(p.Name ?? string.Empty)).ToList();
        }

        // ── 表情(系統別) ──────────────────────────────────────────────────

        private void TickPerChannelCycle(AutoCycleSettings a, float now)
        {
            TickChannel(a, a.Eyebrow, _eyebrowClock, now, GetEyebrowList(),
                (cha, ptn, blend) => { cha.ChangeEyebrowPtn(ptn, blend); _lockedEyebrowPtn = ptn; });

            TickChannel(a, a.Eyes, _eyesClock, now, GetEyesList(),
                (cha, ptn, blend) => { cha.ChangeEyesPtn(ptn, blend); _lockedEyesPtn = ptn; });

            TickChannel(a, a.Mouth, _mouthClock, now, GetMouthList(),
                (cha, ptn, blend) => { cha.ChangeMouthPtn(ptn, blend); _lockedMouthPtn = ptn; });

            // 数値系統は選択キャラ専用の TickEffectCycle で処理する。
        }

        /// <summary>
        /// 数値段階を回す系統(頬赤・涙)。
        /// 眉/目/口と違って番号一覧がゲーム側に無いので、既定の段階か選択した段階を回す。
        /// </summary>
        private void TickValueChannel(
            AutoCycleSettings a,
            ChannelCycleSettings ch,
            CycleClock clock,
            float now,
            int[] defaults,
            Action<ChaControl, int> apply)
        {
            if (ch == null || !ch.Enabled) return;
            if (now < clock.NextAt) return;

            List<int> values = BuildValueSteps(ch);
            if (values.Count == 0)
            {
                clock.NextAt = now + 2f;
                return;
            }

            clock.Index = NextIndex(clock.Index, values.Count, a.EffectiveOrder(ch.Order));
            int v = values[clock.Index];

            foreach (ChaControl cha in ResolveFemales())
            {
                if (cha == null) continue;
                try { apply(cha, v); }
                catch (Exception ex) { LogWarn("value cycle apply failed: " + ex.Message); }
            }

            clock.NextAt = now + a.EffectiveHold(ch.HoldSeconds);
        }

        private void TickChannel(
            AutoCycleSettings a,
            ChannelCycleSettings ch,
            CycleClock clock,
            float now,
            List<PatternEntry> list,
            Action<ChaControl, int, bool> apply)
        {
            if (ch == null || !ch.Enabled) return;
            if (now < clock.NextAt) return;

            list = FilterPatterns(list, ch.Whitelist);
            if (list == null || list.Count == 0)
            {
                clock.NextAt = now + 2f;
                return;
            }

            clock.Index = NextIndex(clock.Index, list.Count, a.EffectiveOrder(ch.Order));
            int ptn = list[clock.Index].Id;
            bool blend = a.EffectiveBlend(ch.Blend);

            foreach (ChaControl cha in ResolveFemales())
            {
                if (cha == null) continue;
                try { apply(cha, ptn, blend); }
                catch (Exception ex) { LogWarn("channel cycle apply failed: " + ex.Message); }
            }

            clock.NextAt = now + a.EffectiveHold(ch.HoldSeconds);
        }

        /// <summary>使う番号だけに絞る。空なら全部。</summary>
        internal List<PatternEntry> FilterPatterns(List<PatternEntry> list, List<int> whitelist)
        {
            if (list == null) return null;
            if (whitelist == null || whitelist.Count == 0) return list;

            HashSet<int> keep = new HashSet<int>(whitelist);
            return list.Where(e => e != null && keep.Contains(e.Id)).ToList();
        }

        // ── 共通 ──────────────────────────────────────────────────────────

        // ── 直接適用(UIのスライダーから呼ぶ) ────────────────────────────

        // 望みの値。設定されている間は毎フレーム押し切る。
        // Hシーンは興奮度などから頬赤・涙を自分で書き換えてくるため、
        // 一度入れただけでは次のフレームで戻される。
        private int? _desiredHohoAka;
        private int? _desiredTears;
        private bool _hohoGateLogged;

        /// <summary>頬赤を今すぐ適用し、以後も維持する。0〜100(%)。</summary>
        internal void ApplyHohoAkaNow(int percent)
        {
            _desiredHohoAka = Mathf.Clamp(percent, 0, 100);
            LogHohoAkaGateOnce();

            float rate = _desiredHohoAka.Value / 100f;
            foreach (ChaControl cha in ResolveFemales())
            {
                if (cha == null) continue;
                try { cha.ChangeHohoAkaRate(rate); }
                catch (Exception ex) { LogWarn("hohoAka apply failed: " + ex.Message); }
            }
        }

        /// <summary>
        /// ゲーム設定の「頬赤」が切ってあると、いくら値を入れても
        /// ChangeHohoAkaRate の中で透明にされる。見えない原因がこれかどうかを残す。
        /// </summary>
        private void LogHohoAkaGateOnce()
        {
            if (_hohoGateLogged) return;
            _hohoGateLogged = true;
            try
            {
                bool gate = Manager.Config.EtcData != null && Manager.Config.EtcData.hohoAka;
                LogInfo("[hohoAka] game setting hohoAka=" + gate
                    + (gate ? "" : "  ← ゲーム設定で頬赤が切られているため表示されない"));
            }
            catch (Exception ex)
            {
                LogWarn("[hohoAka] gate read failed: " + ex.Message);
            }
        }

        /// <summary>望みの値を毎フレーム押し切る。ゲーム側の上書きに負けないため。</summary>
        private void MaintainValueChannels()
        {
            if (_desiredHohoAka == null && _desiredTears == null) return;
            if (!IsInHScene()) return;

            ResolveFemalesInto(_valueChannelFemales);
            foreach (ChaControl cha in _valueChannelFemales)
            {
                if (cha == null) continue;
                try
                {
                    if (_desiredHohoAka != null)
                    {
                        float want = _desiredHohoAka.Value / 100f;
                        if (Mathf.Abs(cha.fileStatus.hohoAkaRate - want) > 0.001f)
                            cha.ChangeHohoAkaRate(want);
                    }
                    if (_desiredTears != null && cha.tearsLv != (byte)_desiredTears.Value)
                        ApplyTearsLevel(cha, _desiredTears.Value);
                }
                catch { }
            }
        }

        private readonly List<ChaControl> _valueChannelFemales = new List<ChaControl>();

        /// <summary>涙を今すぐ適用し、以後も維持する。0〜3。</summary>
        internal void ApplyTearsNow(int level)
        {
            _desiredTears = Mathf.Clamp(level, 0, 3);
            foreach (ChaControl cha in ResolveFemales())
            {
                if (cha == null) continue;
                ApplyTearsLevel(cha, _desiredTears.Value);
            }
        }

        /// <summary>維持をやめる。ゲーム側の制御に戻す。</summary>
        internal void ReleaseHohoAka() { _desiredHohoAka = null; }
        internal void ReleaseTears() { _desiredTears = null; }

        private static System.Reflection.MethodInfo _updateVisibleMethod;
        private static bool _updateVisibleResolved;

        /// <summary>
        /// 涙の段階を反映する。
        /// tearsLv の setter は fileStatus に値を書くだけで、
        /// 実際の見た目(S_TEARS_01〜03の表示切替)は UpdateVisible() が行う。
        /// これを呼ばないと値だけ変わって画面は何も変わらない。
        /// </summary>
        private void ApplyTearsLevel(ChaControl cha, int level)
        {
            if (cha == null) return;
            try
            {
                cha.tearsLv = (byte)Mathf.Clamp(level, 0, 3);

                if (!_updateVisibleResolved)
                {
                    _updateVisibleResolved = true;
                    _updateVisibleMethod = typeof(ChaControl).GetMethod(
                        "UpdateVisible",
                        System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic
                        | System.Reflection.BindingFlags.Public,
                        null, System.Type.EmptyTypes, null);
                    LogInfo("[tears] UpdateVisible " + (_updateVisibleMethod != null ? "found" : "NOT FOUND"));
                }

                if (_updateVisibleMethod != null)
                    _updateVisibleMethod.Invoke(cha, null);
            }
            catch (Exception ex)
            {
                LogWarn("tears apply failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 最小〜最大を段階数で割った値の並びを作る。
        /// 連続値(頬赤)も段階値(涙)も同じ扱いにできる。
        /// </summary>
        internal static List<int> BuildValueSteps(ChannelCycleSettings ch)
        {
            List<int> result = new List<int>();
            if (ch == null) return result;

            int min = Mathf.Min(ch.MinValue, ch.MaxValue);
            int max = Mathf.Max(ch.MinValue, ch.MaxValue);
            int steps = Mathf.Clamp(ch.StepCount, 2, 20);

            if (min == max) { result.Add(min); return result; }

            for (int i = 0; i < steps; i++)
            {
                float t = i / (float)(steps - 1);
                result.Add(Mathf.RoundToInt(Mathf.Lerp(min, max, t)));
            }
            return result;
        }

        /// <summary>順送り / 逆戻り / ランダム で次の位置を決める。</summary>
        private int NextIndex(int current, int count, CycleOrder order)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;

            switch (order)
            {
                case CycleOrder.Reverse:
                    return current <= 0 ? count - 1 : current - 1;

                case CycleOrder.Random:
                    if (_cycleRandom == null) _cycleRandom = new System.Random();
                    // 直前と同じものを続けて出さない。
                    int pick = _cycleRandom.Next(count - 1);
                    if (current >= 0 && pick >= current) pick++;
                    return pick;

                default:
                    return current < 0 ? 0 : (current + 1) % count;
            }
        }
    }
}
