using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace MainGameObjectComposer
{
    /// <summary>
    /// 1プリセット = 1体位/レーンの設定。Loop1/Loop2 それぞれにオブジェクト配置を保持する。
    /// JsonUtility が List&lt;internalClass&gt; をシリアライズ対象から外す不具合があるため、
    /// 各 ManagedObjectData は個別に JSON 化して List&lt;string&gt; で持つ（ToJson は単体だと動く）。
    /// </summary>
    [Serializable]
    internal sealed class PresetEntry
    {
        public string key;
        public string displayName;
        // 表示用メタ情報。key は互換性維持の正本として残し、人間向けの確認はここを使う。
        public bool hasConditionMetadata;
        public string modeDisplayName;
        public int animationId = -1;
        public string animationName;
        public int posture = -1;
        public int sysTaii = -1;
        public int kindHoushi = -1;
        public string categorySummary;
        public bool useLoopSplit = true;
        // 自動切替モード時の候補。複数同キーがあれば最初の候補を採用。
        public bool autoLoadEligible = true;
        // レーン適用フラグ (W/S/Other)。自動切替時、現在のレーンが許可されている場合のみ候補化。
        public bool applyToW = true;
        public bool applyToS = true;
        public bool applyToOther = true;
        public List<string> loop1Json = new List<string>();
        public List<string> loop2Json = new List<string>();
        // HipHijack IK 連動情報: 各エントリは "effectorKey\tobjectId" 形式
        public List<string> ikLinks = new List<string>();
        // Each entry is a serialized MotionIntensityRule. IDs survive renames.
        public List<string> motionIntensityRulesJson = new List<string>();
        public List<string> motionIntensityTargetIds = new List<string>();
        public List<string> motionPositionRulesJson = new List<string>();
        public int movementVersion;
        public bool movementEnabled;
        public float movementZeroMultiplier, movementFullMultiplier, movementTransitionSeconds;
        public List<string> movementTargetIds = new List<string>();
        public List<string> movementRulesJson = new List<string>();
        public bool betterPenetrationTargetConfigured;
        public bool betterPenetrationTargetEnabled;
        public float betterPenetrationVaginaTarget;
    }

    [Serializable]
    internal sealed class PresetsFile
    {
        public string format = "ObjectComposerPresetsV2";
        // 同上の理由で PresetEntry も個別に JSON 化して List&lt;string&gt; で持つ
        public List<string> presetsJson = new List<string>();
    }

    public sealed partial class Plugin
    {
        private const string PresetsFileName = "ObjectComposerPresets.json";

        private string _presetsPath;
        // 永続化形式（書き出すときだけ使う）
        private PresetsFile _presetsFile = new PresetsFile();
        // 実行時はこちらを直接操作する
        private readonly List<PresetEntry> _presets = new List<PresetEntry>();

        private Vector2 _presetListScroll;
        private string _newPresetName = string.Empty;

        // 自動切替: 直近に評価したキー（変化検知用）
        private string _lastAutoSwitchKey;

        // アクティブプリセット: HFlag.motion で毎フレーム L1↔L2 補間してシーンを駆動する
        private PresetEntry _activePreset;
        private Dictionary<string, ManagedObjectData> _activeL1ById;
        private Dictionary<string, ManagedObjectData> _activeL2ById;

        // アクティブ化時に HipHijack に加えた変更を解除時に巻き戻すためのスナップ
        private readonly List<string> _activatedFollowKeys = new List<string>();
        private bool _capturedHipState;
        private bool _preSpeedHijack;
        private bool _preCutFemaleAnimSpeed;
        private bool _preBodyCtrlLink;

        private void InitPresetsPath()
        {
            _presetsPath = Path.Combine(_pluginDir ?? string.Empty, PresetsFileName);
        }

        private void LoadPresetsOrCreate()
        {
            try
            {
                if (string.IsNullOrEmpty(_presetsPath)) InitPresetsPath();
                _presets.Clear();
                _movementRuleCache.Clear();
                _presetsFile = new PresetsFile();

                if (!File.Exists(_presetsPath))
                {
                    SavePresets();
                    LogInfo("presets created");
                    return;
                }
                string json = File.ReadAllText(_presetsPath, Encoding.UTF8);
                PresetsFile parsed = JsonUtility.FromJson<PresetsFile>(json);
                if (parsed == null) parsed = new PresetsFile();
                if (parsed.presetsJson == null) parsed.presetsJson = new List<string>();
                _presetsFile = parsed;

                bool migratedBetterPenetrationTarget = false;
                for (int i = 0; i < parsed.presetsJson.Count; i++)
                {
                    string s = parsed.presetsJson[i];
                    if (string.IsNullOrEmpty(s)) continue;
                    try
                    {
                        PresetEntry e = JsonUtility.FromJson<PresetEntry>(s);
                        if (e == null) continue;
                        if (e.loop1Json == null) e.loop1Json = new List<string>();
                        if (e.loop2Json == null) e.loop2Json = new List<string>();
                        if (e.motionIntensityRulesJson == null) e.motionIntensityRulesJson = new List<string>();
                        if (e.motionIntensityTargetIds == null) e.motionIntensityTargetIds = new List<string>();
                        if (e.motionPositionRulesJson == null) e.motionPositionRulesJson = new List<string>();
                        EnsureMovementPreset(e);
                        if (!e.betterPenetrationTargetConfigured && string.Equals(e.key, "sonyu/4/S", StringComparison.Ordinal))
                        {
                            e.betterPenetrationTargetConfigured = true;
                            e.betterPenetrationTargetEnabled = true;
                            e.betterPenetrationVaginaTarget = 0.5f;
                            migratedBetterPenetrationTarget = true;
                        }
                        _presets.Add(e);
                    }
                    catch (Exception inner)
                    {
                        LogWarn("preset entry parse failed: " + inner.Message);
                    }
                }
                if (migratedBetterPenetrationTarget) SavePresets();
                LogInfo("presets loaded: count=" + _presets.Count);
            }
            catch (Exception ex)
            {
                _presets.Clear();
                _presetsFile = new PresetsFile();
                LogError("presets load failed: " + ex.Message);
            }
        }

        private void SavePresets()
        {
            try
            {
                if (string.IsNullOrEmpty(_presetsPath)) InitPresetsPath();

                // _presets → _presetsFile.presetsJson に書き戻し
                _presetsFile.presetsJson = new List<string>(_presets.Count);
                for (int i = 0; i < _presets.Count; i++)
                {
                    PresetEntry e = _presets[i];
                    if (e == null) continue;
                    _presetsFile.presetsJson.Add(JsonUtility.ToJson(e, false));
                }

                string json = JsonUtility.ToJson(_presetsFile, true);
                SaveJsonAtomic(_presetsPath, json, createBackup: true);
            }
            catch (Exception ex)
            {
                LogError("presets save failed: " + ex.Message);
            }
        }

        // ── 体位キー / Loop 判定 ─────────────────────────────────────

        private string BuildCurrentPositionKey()
        {
            if (_runtime == null || _runtime.Flags == null) return string.Empty;
            string mode = _runtime.Flags.mode.ToString();
            int animId = TryReadAnimId(_runtime.Flags);
            string lane = ResolveCurrentLane(_runtime.Flags);
            return mode + "/" + animId + "/" + lane;
        }

        private int GetCurrentLoopIdx()
        {
            if (_runtime == null || _runtime.Flags == null) return 1;
            return _runtime.Flags.motion < 0.5f ? 1 : 2;
        }

        private static int TryReadAnimId(HFlag flags)
        {
            try
            {
                var fi = typeof(HFlag).GetField("nowAnimationInfo",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fi == null) return -1;
                object info = fi.GetValue(flags);
                if (info == null) return -1;
                var idFi = info.GetType().GetField("id",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (idFi == null) return -1;
                object idObj = idFi.GetValue(info);
                return idObj is int v ? v : Convert.ToInt32(idObj);
            }
            catch
            {
                return -1;
            }
        }

        private static string ResolveCurrentLane(HFlag flags)
        {
            try
            {
                string s = flags.nowAnimStateName ?? string.Empty;
                if (s.IndexOf("WLoop", StringComparison.OrdinalIgnoreCase) >= 0) return "W";
                if (s.IndexOf("SLoop", StringComparison.OrdinalIgnoreCase) >= 0) return "S";
                return "O";
            }
            catch
            {
                return "O";
            }
        }

        // ── ManagedObjectData ⇔ List<string> 変換 ──────────────────

        private static List<string> EncodeObjects(List<ManagedObjectData> source)
        {
            var list = new List<string>();
            if (source == null) return list;
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i] == null) continue;
                list.Add(JsonUtility.ToJson(source[i], false));
            }
            return list;
        }

        private static List<ManagedObjectData> DecodeObjects(List<string> source)
        {
            var list = new List<ManagedObjectData>();
            if (source == null) return list;
            for (int i = 0; i < source.Count; i++)
            {
                if (string.IsNullOrEmpty(source[i])) continue;
                try
                {
                    var obj = JsonUtility.FromJson<ManagedObjectData>(source[i]);
                    if (obj != null) list.Add(obj);
                }
                catch { /* skip */ }
            }
            return list;
        }

        private static bool StructureMatches(List<string> aJson, List<ManagedObjectData> b)
        {
            if (aJson == null || aJson.Count == 0) return true; // 相手スロット空なら通す
            var a = DecodeObjects(aJson);
            if (a.Count != (b?.Count ?? 0)) return false;
            var setA = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < a.Count; i++)
                if (a[i] != null && !string.IsNullOrEmpty(a[i].id)) setA.Add(a[i].id);
            for (int i = 0; i < b.Count; i++)
            {
                if (b[i] == null || string.IsNullOrEmpty(b[i].id)) return false;
                if (!setA.Contains(b[i].id)) return false;
            }
            return true;
        }

        // ── 保存・読込 ─────────────────────────────────────────────

        private void SaveCurrentToPreset(PresetEntry overwriteTarget = null, string customName = null, int loopIdx = 0)
        {
            string key = BuildCurrentPositionKey();
            if (string.IsNullOrEmpty(key))
            {
                LogWarn("preset save failed: no position key (runtime not ready)");
                return;
            }

            PresetEntry entry = overwriteTarget;
            bool isNew = entry == null;
            if (isNew)
            {
                entry = new PresetEntry
                {
                    key = key,
                    displayName = !string.IsNullOrEmpty(customName) ? customName : key,
                    useLoopSplit = true
                };
                _presets.Add(entry);
            }
            else
            {
                // 上書き保存時、entry.key を現在体位に紐付け直す
                // (旧 key のままでは自動切替が新しい体位でヒットしないため)
                if (!string.Equals(entry.key, key, StringComparison.Ordinal))
                {
                    LogInfo("preset key rebound: name=" + entry.displayName
                        + " " + entry.key + " -> " + key);
                    entry.key = key;
                    _lastAutoSwitchKey = null; // 次フレームで再評価
                }
            }
            UpdatePresetConditionMetadata(entry, key);

            SyncAllDataFromRuntime();
            List<ManagedObjectData> snapshot = CloneObjectList(_objects);
            List<string> snapshotJson = EncodeObjects(snapshot);

            if (!entry.useLoopSplit)
            {
                entry.loop1Json = snapshotJson;
                entry.loop2Json = new List<string>();
            }
            else if (loopIdx == 0)
            {
                entry.loop1Json = snapshotJson;
                entry.loop2Json = EncodeObjects(snapshot);
            }
            else
            {
                List<string> otherJson = loopIdx == 1 ? entry.loop2Json : entry.loop1Json;
                if (!StructureMatches(otherJson, snapshot))
                {
                    LogWarn("preset save failed: structure mismatch with the other slot. "
                        + "Loop1/Loop2 must share the same object structure (IDs).");
                    if (isNew) _presets.Remove(entry);
                    return;
                }
                if (loopIdx == 1) entry.loop1Json = snapshotJson;
                else entry.loop2Json = snapshotJson;
            }

            // ikLinks はチェックボックス操作で直接プリセットに反映済み。
            // ここで HipHijack 側を読み込むと OFF 時に空で上書きしてしまうため、何もしない。

            SavePresets();
            if (_activePreset == entry) RebuildActivePresetCache();
            LogInfo("preset saved: key=" + key + " loop=" + loopIdx
                + " split=" + entry.useLoopSplit + " objects=" + snapshot.Count
                + " name=" + entry.displayName
                + " ikLinks=" + (entry.ikLinks?.Count ?? 0));
        }

        // ── アクティブプリセットの ikLinks を UI から直接編集するヘルパー群 ──

        /// <summary>
        /// 現在アクティブなプリセットの ikLinks に (effectorKey, objectId) ペアを追加。
        /// 同じ effectorKey の既存エントリは置換。アクティブでない場合は何もしない。
        /// </summary>
        private void AddIkLinkToActivePreset(string effectorKey, string objectId)
        {
            if (_activePreset == null) return;
            if (string.IsNullOrEmpty(effectorKey) || string.IsNullOrEmpty(objectId)) return;
            if (_activePreset.ikLinks == null) _activePreset.ikLinks = new List<string>();

            // 同じ effectorKey のエントリがあれば削除してから追加
            RemoveIkLinkFromActivePreset(effectorKey, save: false);
            _activePreset.ikLinks.Add(effectorKey + "\t" + objectId);
            SavePresets();
        }

        /// <summary>
        /// 現在アクティブなプリセットの ikLinks から指定 effectorKey のエントリを削除。
        /// </summary>
        private void RemoveIkLinkFromActivePreset(string effectorKey, bool save = true)
        {
            if (_activePreset == null) return;
            if (_activePreset.ikLinks == null) return;
            string prefix = effectorKey + "\t";
            int removed = _activePreset.ikLinks.RemoveAll(s =>
                !string.IsNullOrEmpty(s) && s.StartsWith(prefix, StringComparison.Ordinal));
            if (removed > 0 && save) SavePresets();
        }

        /// <summary>
        /// 現在アクティブなプリセットの ikLinks に effectorKey が登録されており、
        /// その objectId が指定 id と一致する場合 true。
        /// </summary>
        private bool IsIkLinkedInActivePreset(string effectorKey, string objectId)
        {
            if (_activePreset == null) return false;
            if (_activePreset.ikLinks == null) return false;
            string prefix = effectorKey + "\t";
            for (int i = 0; i < _activePreset.ikLinks.Count; i++)
            {
                string s = _activePreset.ikLinks[i];
                if (string.IsNullOrEmpty(s)) continue;
                if (!s.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string id = s.Substring(prefix.Length);
                return string.Equals(id, objectId, StringComparison.Ordinal);
            }
            return false;
        }

        private void DeletePresetByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            int removed = _presets.RemoveAll(e => e != null
                && string.Equals(e.key, key, StringComparison.Ordinal));
            if (removed > 0)
            {
                if (_activePreset != null && string.Equals(_activePreset.key, key, StringComparison.Ordinal))
                {
                    DeactivatePreset();
                }
                SavePresets();
                LogInfo("preset deleted: key=" + key);
            }
        }

        private PresetEntry FindPresetByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < _presets.Count; i++)
            {
                if (_presets[i] != null
                    && string.Equals(_presets[i].key, key, StringComparison.Ordinal))
                {
                    return _presets[i];
                }
            }
            return null;
        }

        // ── アクティブプリセット ───────────────────────────────────

        private void ActivatePreset(PresetEntry entry)
        {
            if (entry == null) return;
            // L1 を「構造ベース」としてシーンに展開
            List<ManagedObjectData> l1 = DecodeObjects(entry.loop1Json);
            if (l1.Count == 0)
            {
                LogWarn("activate failed: L1 is empty (key=" + entry.key + ")");
                return;
            }
            RecordUndoSnapshot("activate preset");
            var file = new ObjectLayoutFile { objectsJson = EncodeObjects(l1) };
            ApplyLayoutData(file, rebuildRuntime: true);
            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();

            _activePreset = entry;
            RebuildActivePresetCache();
            SyncBetterPenetrationTarget();

            // 現在のシーンのオブジェクト ID 集合と HipHijack の「Composer 追従中エフェクタ」
            // を突き合わせ、対応する IK を HipHijack 側で ON + ウエイト 1.0 にする。
            ActivateHipHijackIksForCurrentLinks();

            LogInfo("preset activated: name=" + entry.displayName + " key=" + entry.key
                + " split=" + entry.useLoopSplit);
        }

        /// <summary>
        /// アクティブ化時にプリセットの ikLinks (保存された IK 連動情報) を HipHijack に書き戻し、
        /// 対応する IK を ON + ウエイト 1.0 にする。
        /// body の場合は競合機能を解除（解除時に巻き戻せるよう元状態をスナップ）。
        /// </summary>
        private void ActivateHipHijackIksForCurrentLinks()
        {
            if (!HipHijackBridge.IsAvailable) return;

            // アクティブ化前の競合機能状態をスナップ（body を有効化する前に必ず取る）
            _preSpeedHijack = HipHijackBridge.IsSpeedHijackEnabled();
            _preCutFemaleAnimSpeed = HipHijackBridge.IsCutFemaleAnimSpeedEnabled();
            _preBodyCtrlLink = HipHijackBridge.IsBodyCtrlLinkEnabled();
            _capturedHipState = true;
            _activatedFollowKeys.Clear();

            if (_activePreset == null || _activePreset.ikLinks == null || _activePreset.ikLinks.Count == 0)
            {
                return;
            }

            // 現在シーンに存在するオブジェクトID集合
            var idSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _objects.Count; i++)
                if (_objects[i] != null && !string.IsNullOrEmpty(_objects[i].id)) idSet.Add(_objects[i].id);

            for (int i = 0; i < _activePreset.ikLinks.Count; i++)
            {
                string entry = _activePreset.ikLinks[i];
                if (string.IsNullOrEmpty(entry)) continue;
                int sep = entry.IndexOf('\t');
                if (sep <= 0 || sep >= entry.Length - 1) continue;
                string key = entry.Substring(0, sep);
                string objectId = entry.Substring(sep + 1);
                if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(objectId)) continue;
                // 対象オブジェクトが現在シーンに無ければスキップ
                if (!idSet.Contains(objectId)) continue;

                // HipHijack 側に Follow リンクを設定
                HipHijackBridge.TrySet(key, objectId);
                // IK ON + ウエイト 1.0 (body なら競合機能 OFF)
                ActivateHipHijackIkForKey(key);
                _activatedFollowKeys.Add(key);
            }
        }

        private void DeactivatePreset()
        {
            if (_activePreset == null) return;
            RestoreBetterPenetrationTarget();
            ClearAppliedMotionPositionOffsets();
            string n = _activePreset.displayName;

            // アクティブ化時に弄った HipHijack の状態を巻き戻す
            if (HipHijackBridge.IsAvailable)
            {
                // 我々が ON にした IK を OFF + Follow リンクを clear
                for (int i = 0; i < _activatedFollowKeys.Count; i++)
                {
                    string key = _activatedFollowKeys[i];
                    if (string.IsNullOrEmpty(key)) continue;
                    HipHijackBridge.TryClear(key);
                    HipHijackBridge.TrySetEffectorEnabled(key, false);
                }
                // body 関連の競合機能を元の状態に復元
                if (_capturedHipState)
                {
                    HipHijackBridge.TrySetSpeedHijackEnabled(_preSpeedHijack);
                    HipHijackBridge.TrySetCutFemaleAnimSpeedEnabled(_preCutFemaleAnimSpeed);
                    bool nowCtrl = HipHijackBridge.IsBodyCtrlLinkEnabled();
                    if (_preBodyCtrlLink && !nowCtrl) HipHijackBridge.TryEnableBodyCtrlLink();
                    else if (!_preBodyCtrlLink && nowCtrl) HipHijackBridge.TryDisableBodyCtrlLink();
                }
            }

            _activatedFollowKeys.Clear();
            _capturedHipState = false;

            _activePreset = null;
            _activeL1ById = null;
            _activeL2ById = null;
            LogInfo("preset deactivated + HipHijack state restored: name=" + n);
        }

        private void RebuildActivePresetCache()
        {
            if (_activePreset == null)
            {
                _activeL1ById = null;
                _activeL2ById = null;
                return;
            }
            _activeL1ById = BuildIdMap(DecodeObjects(_activePreset.loop1Json));
            _activeL2ById = BuildIdMap(DecodeObjects(_activePreset.loop2Json));
        }

        private static Dictionary<string, ManagedObjectData> BuildIdMap(List<ManagedObjectData> list)
        {
            var map = new Dictionary<string, ManagedObjectData>(StringComparer.Ordinal);
            if (list == null) return map;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null) continue;
                if (string.IsNullOrEmpty(list[i].id)) continue;
                map[list[i].id] = list[i];
            }
            return map;
        }

        /// <summary>
        /// 体位/モーション変化を検知して自動的にプリセットを切替える。
        /// 設定 AutoSwitchPresetOnPositionChange が ON のときだけ動く。
        /// 一致条件: preset.key の (mode/animId) 部が現在キーと一致 かつ 現在レーン (W/S/O) が
        /// preset.applyToW/applyToS/applyToOther で許可されている。
        /// </summary>
        private void TickAutoSwitchPreset()
        {
            if (_movementEditPreset != null) return;
            if (_settings == null || !_settings.AutoSwitchPresetOnPositionChange) return;

            string key = BuildCurrentPositionKey();
            if (string.IsNullOrEmpty(key)) return;
            if (string.Equals(key, _lastAutoSwitchKey, StringComparison.Ordinal)) return;

            _lastAutoSwitchKey = key;

            ParseKey(key, out _, out _, out string currentLane);

            // 新キーの mode/animId に一致 & lane 許可 & 自動候補ON のプリセットを探す
            PresetEntry target = null;
            for (int i = 0; i < _presets.Count; i++)
            {
                PresetEntry e = _presets[i];
                if (e == null) continue;
                if (!e.autoLoadEligible) continue;
                if (!MatchesModeAnimId(e.key, key)) continue;
                if (!IsLaneAllowed(e, currentLane)) continue;
                target = e;
                break;
            }

            if (target == _activePreset) return; // 既に同じものがアクティブなら何もしない

            if (_activePreset != null) DeactivatePreset();
            if (target != null) ActivatePreset(target);
            LogInfo("auto-switch preset: key=" + key + " lane=" + currentLane
                + " -> " + (target?.displayName ?? "<none>"));
        }

        private static void ParseKey(string key, out string mode, out int animId, out string lane)
        {
            mode = string.Empty; animId = -1; lane = "O";
            if (string.IsNullOrEmpty(key)) return;
            string[] parts = key.Split('/');
            if (parts.Length >= 1) mode = parts[0];
            if (parts.Length >= 2) int.TryParse(parts[1], out animId);
            if (parts.Length >= 3 && !string.IsNullOrEmpty(parts[2])) lane = parts[2];
        }

        private static bool MatchesModeAnimId(string presetKey, string currentKey)
        {
            ParseKey(presetKey, out string pm, out int pa, out _);
            ParseKey(currentKey, out string cm, out int ca, out _);
            return string.Equals(pm, cm, StringComparison.Ordinal) && pa == ca;
        }

        private static bool IsLaneAllowed(PresetEntry e, string lane)
        {
            if (e == null) return false;
            if (string.Equals(lane, "W", StringComparison.Ordinal)) return e.applyToW;
            if (string.Equals(lane, "S", StringComparison.Ordinal)) return e.applyToS;
            return e.applyToOther;
        }

        private void UpdatePresetConditionMetadata(PresetEntry entry, string key)
        {
            if (entry == null) return;

            ParseKey(key, out string modeFromKey, out int animIdFromKey, out _);
            entry.hasConditionMetadata = true;
            entry.modeDisplayName = modeFromKey;
            entry.animationId = animIdFromKey;
            entry.animationName = string.Empty;
            entry.posture = -1;
            entry.sysTaii = -1;
            entry.kindHoushi = -1;
            entry.categorySummary = string.Empty;

            if (_runtime == null || _runtime.Flags == null || _runtime.Flags.nowAnimationInfo == null)
            {
                return;
            }

            HSceneProc.AnimationListInfo info = _runtime.Flags.nowAnimationInfo;
            entry.modeDisplayName = info.mode.ToString();
            entry.animationId = info.id;
            entry.animationName = info.nameAnimation ?? string.Empty;
            entry.posture = info.posture;
            entry.sysTaii = info.sysTaii;
            entry.kindHoushi = info.kindHoushi;
            entry.categorySummary = BuildCategorySummary(info.lstCategory);
        }

        private string BuildCurrentConditionText(string key)
        {
            ParseKey(key, out string modeFromKey, out int animIdFromKey, out _);
            if (_runtime == null || _runtime.Flags == null || _runtime.Flags.nowAnimationInfo == null)
            {
                return BuildConditionText(key, modeFromKey, animIdFromKey, string.Empty, -1, -1, -1, string.Empty);
            }

            HSceneProc.AnimationListInfo info = _runtime.Flags.nowAnimationInfo;
            return BuildConditionText(
                key,
                info.mode.ToString(),
                info.id,
                info.nameAnimation,
                info.posture,
                info.sysTaii,
                info.kindHoushi,
                BuildCategorySummary(info.lstCategory));
        }

        private string BuildPresetConditionText(PresetEntry entry)
        {
            if (entry == null) return "<none>";

            ParseKey(entry.key, out string modeFromKey, out int animIdFromKey, out _);
            if (!entry.hasConditionMetadata)
            {
                if (TryResolveAnimationInfoByKey(entry.key, out HSceneProc.AnimationListInfo resolved))
                {
                    return BuildConditionText(
                        entry.key,
                        resolved.mode.ToString(),
                        resolved.id,
                        resolved.nameAnimation,
                        resolved.posture,
                        resolved.sysTaii,
                        resolved.kindHoushi,
                        BuildCategorySummary(resolved.lstCategory));
                }

                return BuildConditionText(entry.key, modeFromKey, animIdFromKey, string.Empty, -1, -1, -1, string.Empty);
            }

            string mode = !string.IsNullOrEmpty(entry.modeDisplayName) ? entry.modeDisplayName : modeFromKey;
            int animId = entry.animationId >= 0 ? entry.animationId : animIdFromKey;
            string animationName = entry.animationName;
            int posture = entry.posture;
            int sysTaii = entry.sysTaii;
            int kindHoushi = entry.kindHoushi;
            string categorySummary = entry.categorySummary;

            if (string.IsNullOrEmpty(animationName)
                && TryResolveAnimationInfoByKey(entry.key, out HSceneProc.AnimationListInfo resolvedFromKey))
            {
                mode = resolvedFromKey.mode.ToString();
                animId = resolvedFromKey.id;
                animationName = resolvedFromKey.nameAnimation;
                posture = resolvedFromKey.posture;
                sysTaii = resolvedFromKey.sysTaii;
                kindHoushi = resolvedFromKey.kindHoushi;
                categorySummary = BuildCategorySummary(resolvedFromKey.lstCategory);
            }

            return BuildConditionText(
                entry.key,
                mode,
                animId,
                animationName,
                posture,
                sysTaii,
                kindHoushi,
                categorySummary);
        }

        private bool TryResolveAnimationInfoByKey(string key, out HSceneProc.AnimationListInfo info)
        {
            info = null;
            ParseKey(key, out string modeText, out int animId, out _);
            if (string.IsNullOrEmpty(modeText) || animId < 0)
            {
                return false;
            }

            if (!Enum.TryParse(modeText, out HFlag.EMode mode))
            {
                return false;
            }

            if (TryResolveAnimationInfoFromField(RuntimeReflection.FiHSceneLstAnimInfo, mode, animId, out info))
            {
                return true;
            }

            return TryResolveAnimationInfoFromField(RuntimeReflection.FiHSceneLstUseAnimInfo, mode, animId, out info);
        }

        private bool TryResolveAnimationInfoFromField(
            System.Reflection.FieldInfo field,
            HFlag.EMode mode,
            int animId,
            out HSceneProc.AnimationListInfo info)
        {
            info = null;
            if (field == null || _runtime == null || _runtime.HSceneProc == null)
            {
                return false;
            }

            var lists = field.GetValue(_runtime.HSceneProc) as List<HSceneProc.AnimationListInfo>[];
            int modeIndex = (int)mode;
            if (lists == null || modeIndex < 0 || modeIndex >= lists.Length)
            {
                return false;
            }

            List<HSceneProc.AnimationListInfo> list = lists[modeIndex];
            if (list == null)
            {
                return false;
            }

            for (int i = 0; i < list.Count; i++)
            {
                HSceneProc.AnimationListInfo candidate = list[i];
                if (candidate == null || candidate.id != animId)
                {
                    continue;
                }

                info = candidate;
                return true;
            }

            return false;
        }

        private static string BuildConditionText(
            string key,
            string mode,
            int animId,
            string animationName,
            int posture,
            int sysTaii,
            int kindHoushi,
            string categorySummary)
        {
            ParseKey(key, out string keyMode, out int keyAnimId, out string lane);
            if (string.IsNullOrEmpty(mode)) mode = keyMode;
            if (animId < 0) animId = keyAnimId;

            string name = string.IsNullOrEmpty(animationName) ? "<名称未保存>" : animationName;
            string text = "体位: " + name
                + " / mode=" + (string.IsNullOrEmpty(mode) ? "?" : mode)
                + " id=" + animId
                + " lane=" + FormatLaneLabel(lane);

            if (posture >= 0) text += " posture=" + posture;
            if (sysTaii >= 0) text += " sysTaii=" + sysTaii;
            if (kindHoushi >= 0) text += " kindHoushi=" + kindHoushi;
            if (!string.IsNullOrEmpty(categorySummary)) text += " category=" + categorySummary;

            return text;
        }

        private static string FormatLaneLabel(string lane)
        {
            if (string.Equals(lane, "W", StringComparison.Ordinal)) return "W";
            if (string.Equals(lane, "S", StringComparison.Ordinal)) return "S";
            return "他";
        }

        private static string BuildCategorySummary(List<HSceneProc.Category> categories)
        {
            if (categories == null || categories.Count == 0)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < categories.Count; i++)
            {
                HSceneProc.Category category = categories[i];
                if (category == null) continue;
                if (sb.Length > 0) sb.Append(",");
                sb.Append(category.category);
                if (!string.IsNullOrEmpty(category.fileMove))
                {
                    sb.Append(":").Append(category.fileMove);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 毎フレーム: アクティブプリセット中なら HFlag.motion で L1↔L2 を補間して
        /// 各オブジェクトの Data フィールド (＋ 該当 transform) に適用する。
        /// L1/L2区別 が OFF の場合は L1 を静的に保持（補間なし）。
        /// </summary>
        private void TickActivePreset()
        {
            if (_activePreset == null) return;
            if (_activeL1ById == null) return;

            bool useBlend = _activePreset.useLoopSplit && _activeL2ById != null && _activeL2ById.Count > 0;
            float t = 0f;
            if (useBlend && _runtime != null && _runtime.Flags != null)
            {
                t = Mathf.Clamp01(_runtime.Flags.motion);
            }

            foreach (var kv in _runtimeObjects)
            {
                var rr = kv.Value;
                if (rr == null || rr.Data == null || rr.GameObject == null) continue;
                string id = rr.Data.id;
                if (string.IsNullOrEmpty(id)) continue;
                if (!_activeL1ById.TryGetValue(id, out var v1)) continue;

                ManagedObjectData v2 = null;
                if (useBlend) _activeL2ById.TryGetValue(id, out v2);
                if (v2 == null) v2 = v1;
                float previousOrbitSpeed = rr.Data.orbitSpeedHz;
                rr.Data.vibeSlowParentEnabled = v1.vibeSlowParentEnabled;
                rr.Data.vibeInsertionAlignEnabled = v1.vibeInsertionAlignEnabled;
                rr.Data.vibeSlowParentWeight = v1.vibeSlowParentWeight;
                rr.Data.vibeSlowRiseSeconds = v1.vibeSlowRiseSeconds;
                rr.Data.vibeSlowFallSeconds = v1.vibeSlowFallSeconds;

                // L2 がない姿勢は補間せず、そのまま Data に反映する。
                if (ReferenceEquals(v1, v2))
                {
                    rr.Data.localPosition = v1.localPosition;
                    rr.Data.localEulerAngles = v1.localEulerAngles;
                    rr.Data.localScale = v1.localScale;
                    rr.Data.orbitRadiusX = v1.orbitRadiusX;
                    rr.Data.orbitRadiusZ = v1.orbitRadiusZ;
                    rr.Data.tubeRadius = v1.tubeRadius;
                    rr.Data.orbitSpeedHz = v1.orbitSpeedHz;
                    rr.Data.orbitPhaseTurns = v1.orbitPhaseTurns;
                    rr.Data.animSpeedMultiplier = v1.animSpeedMultiplier;
                    rr.Data.animSyncPhaseShift = v1.animSyncPhaseShift;
                }
                else
                {
                    rr.Data.localPosition = Vector3.Lerp(v1.localPosition, v2.localPosition, t);
                    rr.Data.localEulerAngles = v1.localEulerAngles == v2.localEulerAngles
                        ? v1.localEulerAngles
                        : LerpEuler(v1.localEulerAngles, v2.localEulerAngles, t);
                    rr.Data.localScale = Vector3.Lerp(v1.localScale, v2.localScale, t);
                    rr.Data.orbitRadiusX = Mathf.Lerp(v1.orbitRadiusX, v2.orbitRadiusX, t);
                    rr.Data.orbitRadiusZ = Mathf.Lerp(v1.orbitRadiusZ, v2.orbitRadiusZ, t);
                    rr.Data.tubeRadius = Mathf.Lerp(v1.tubeRadius, v2.tubeRadius, t);
                    rr.Data.orbitSpeedHz = Mathf.Lerp(v1.orbitSpeedHz, v2.orbitSpeedHz, t);
                    rr.Data.orbitPhaseTurns = Mathf.Lerp(v1.orbitPhaseTurns, v2.orbitPhaseTurns, t);
                    rr.Data.animSpeedMultiplier = Mathf.Lerp(v1.animSpeedMultiplier, v2.animSpeedMultiplier, t);
                    rr.Data.animSyncPhaseShift = Mathf.Lerp(v1.animSyncPhaseShift, v2.animSyncPhaseShift, t);
                }
                ApplyMovementPose(rr, v1, v2, t);
                if (!rr.Data.animSync && MovementBlendFor(id) > 0f
                    && !Mathf.Approximately(previousOrbitSpeed, rr.Data.orbitSpeedHz))
                    rr.Data.phaseContinuityOffsetTurns += (float)((previousOrbitSpeed - rr.Data.orbitSpeedHz) * GetSyncTime());

                // Wrapper には位置/回転だけ、scale は 1 固定（親子の shear を防ぐ）。
                // ユーザー設定 scale は Visual 側に適用。
                ApplyMotionPositionOffset(rr);
                if (rr.GameObject.transform.localEulerAngles != rr.Data.localEulerAngles)
                    rr.GameObject.transform.localEulerAngles = rr.Data.localEulerAngles;
                if (rr.Visual != null)
                {
                    if (rr.Visual.transform.localScale != rr.Data.localScale)
                        rr.Visual.transform.localScale = rr.Data.localScale;
                }
            }
        }

        private static Vector3 LerpEuler(Vector3 a, Vector3 b, float t)
        {
            return Quaternion.Slerp(Quaternion.Euler(a), Quaternion.Euler(b), t).eulerAngles;
        }

        private void LoadPresetSlot(PresetEntry entry, bool useLoop2)
        {
            if (entry == null) return;
            List<string> slotJson = useLoop2 ? entry.loop2Json : entry.loop1Json;
            if (slotJson == null || slotJson.Count == 0)
            {
                LogWarn("preset slot empty: key=" + entry.key + " loop=" + (useLoop2 ? 2 : 1));
                return;
            }
            List<ManagedObjectData> slot = DecodeObjects(slotJson);
            if (slot.Count == 0)
            {
                LogWarn("preset slot decode failed: key=" + entry.key + " loop=" + (useLoop2 ? 2 : 1));
                return;
            }
            RecordUndoSnapshot("load preset (manual)");
            var file = new ObjectLayoutFile { objectsJson = EncodeObjects(slot) };
            ApplyLayoutData(file, rebuildRuntime: true);
            SaveLayoutIfNeeded();
            RaiseManagedObjectListChanged();
            LogInfo("preset loaded: key=" + entry.key + " loop=" + (useLoop2 ? 2 : 1)
                + " objects=" + slot.Count);
        }

        // ── UI ─────────────────────────────────────────────────────

        private void DrawPresetSection()
        {
            if (_movementEditPreset != null)
            {
                _expandedMovementPreset = _movementEditPreset;
                DrawMovementEditor(_movementEditPreset);
                return;
            }
            string key = BuildCurrentPositionKey();
            int loopIdx = GetCurrentLoopIdx();
            ParseKey(key, out _, out _, out string currentLane);
            GUIStyle presetMetaStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };

            GUILayout.Label("プリセット (体位/モーション)");
            GUILayout.Label("現在: " + (string.IsNullOrEmpty(key) ? "<未取得>" : BuildCurrentConditionText(key)) + "  Loop" + loopIdx, presetMetaStyle);

            bool nextAutoSwitch = GUILayout.Toggle(_settings.AutoSwitchPresetOnPositionChange,
                "体位/モーション変化で自動切替");
            if (nextAutoSwitch != _settings.AutoSwitchPresetOnPositionChange)
            {
                _settings.AutoSwitchPresetOnPositionChange = nextAutoSwitch;
                _lastAutoSwitchKey = null; // 次フレームで再評価させる
                SaveSettings();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("名前:", GUILayout.Width(40f));
            _newPresetName = GUILayout.TextField(_newPresetName ?? string.Empty, GUILayout.Width(180f));
            bool prevEnabled = GUI.enabled;
            GUI.enabled = !string.IsNullOrEmpty(key);
            if (GUILayout.Button("新規保存", GUILayout.Width(90f)))
            {
                SaveCurrentToPreset(overwriteTarget: null, customName: _newPresetName);
                _newPresetName = string.Empty;
            }
            GUI.enabled = prevEnabled;
            GUILayout.EndHorizontal();

            GUILayout.Label("保存済みプリセット:");
            float presetListHeight = Mathf.Clamp(_mainWindowRect.height * 0.38f, 260f, 460f);
            _presetListScroll = GUILayout.BeginScrollView(_presetListScroll, GUILayout.Height(presetListHeight));
            for (int i = _presets.Count - 1; i >= 0; i--)
            {
                PresetEntry e = _presets[i];
                if (e == null) continue;

                int l1 = e.loop1Json != null ? e.loop1Json.Count : 0;
                int l2 = e.loop2Json != null ? e.loop2Json.Count : 0;
                string slotInfo = e.useLoopSplit
                    ? "L1:" + l1 + " L2:" + l2
                    : "[" + l1 + "]";
                bool isCurrentKey = string.Equals(e.key, key, StringComparison.Ordinal);
                bool isApplicableNow = !string.IsNullOrEmpty(key)
                    && MatchesModeAnimId(e.key, key)
                    && IsLaneAllowed(e, currentLane);

                GUILayout.BeginVertical("box");

                GUILayout.BeginHorizontal();
                GUILayout.Label(isCurrentKey ? "●" : (isApplicableNow ? "○" : " "), GUILayout.Width(14f));
                string newName = GUILayout.TextField(e.displayName ?? string.Empty, GUILayout.Width(160f));
                if (!string.Equals(newName, e.displayName ?? string.Empty, StringComparison.Ordinal))
                {
                    e.displayName = newName;
                    SavePresets();
                }
                GUILayout.Label(slotInfo, GUILayout.Width(90f));
                GUILayout.Label(isCurrentKey ? "現在" : (isApplicableNow ? "一致候補" : ""), GUILayout.Width(70f));
                if (GUILayout.Button("削除", GUILayout.Width(50f)))
                {
                    if (ReferenceEquals(_activePreset, e)) DeactivatePreset();
                    _presets.RemoveAt(i);
                    SavePresets();
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    break;
                }
                GUILayout.EndHorizontal();

                GUILayout.Label(BuildPresetConditionText(e), presetMetaStyle);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("L1ロード", GUILayout.Width(80f))) LoadPresetSlot(e, useLoop2: false);
                if (GUILayout.Button("L1上書き", GUILayout.Width(80f)))
                    SaveCurrentToPreset(overwriteTarget: e, customName: null, loopIdx: 1);
                if (e.useLoopSplit)
                {
                    if (GUILayout.Button("L2ロード", GUILayout.Width(80f))) LoadPresetSlot(e, useLoop2: true);
                    if (GUILayout.Button("L2上書き", GUILayout.Width(80f)))
                        SaveCurrentToPreset(overwriteTarget: e, customName: null, loopIdx: 2);
                }

                bool nextSplit = GUILayout.Toggle(e.useLoopSplit, "L1/L2区別");
                if (nextSplit != e.useLoopSplit)
                {
                    e.useLoopSplit = nextSplit;
                    if (!nextSplit) e.loop2Json = new List<string>();
                    SavePresets();
                    if (_activePreset == e) RebuildActivePresetCache();
                }
                bool nextAuto = GUILayout.Toggle(e.autoLoadEligible, "自動候補");
                if (nextAuto != e.autoLoadEligible)
                {
                    e.autoLoadEligible = nextAuto;
                    SavePresets();
                }
                GUILayout.EndHorizontal();

                // レーン適用フラグ (自動切替時の絞り込み)
                GUILayout.BeginHorizontal();
                GUILayout.Label("レーン適用:", GUILayout.Width(80f));
                bool nextW = GUILayout.Toggle(e.applyToW, "W", GUILayout.Width(40f));
                bool nextS = GUILayout.Toggle(e.applyToS, "S", GUILayout.Width(40f));
                bool nextO = GUILayout.Toggle(e.applyToOther, "他", GUILayout.Width(40f));
                if (nextW != e.applyToW || nextS != e.applyToS || nextO != e.applyToOther)
                {
                    e.applyToW = nextW;
                    e.applyToS = nextS;
                    e.applyToOther = nextO;
                    SavePresets();
                    _lastAutoSwitchKey = null; // 次フレームで再評価させる
                }
                GUILayout.EndHorizontal();

                // 3行目: アクティブ化 (HFlag.motion で L1↔L2 を毎フレーム補間してシーンを駆動)
                GUILayout.BeginHorizontal();
                bool isActive = _activePreset == e;
                if (!isActive)
                {
                    if (GUILayout.Button("適用 (アクティブ化)", GUILayout.Width(180f)))
                    {
                        ActivatePreset(e);
                    }
                }
                else
                {
                    GUILayout.Label("● アクティブ中 (L1↔L2 補間中)", GUILayout.Width(220f));
                    if (GUILayout.Button("解除", GUILayout.Width(60f)))
                    {
                        DeactivatePreset();
                    }
                }
                GUILayout.EndHorizontal();

                DrawMotionIntensityPresetEditor(e);
                DrawMovementEditor(e);
                DrawBetterPenetrationPresetEditor(e);

                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
        }
    }
}
