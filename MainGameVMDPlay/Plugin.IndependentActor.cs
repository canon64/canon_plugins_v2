using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private sealed class TargetContext {
            internal bool Independent;
            internal int Generation, SceneGeneration;
            internal HSceneProc Scene;
            internal ChaControl Actor;
            internal object Controller;
            internal Transform BoneRoot;
            internal PoseSnapshot Snapshot;
            internal readonly ControlTransition Transition = new ControlTransition();
        }
        private readonly TargetContext _target = new TargetContext();
        private bool IsIndependentTarget => _target.Independent;
        internal ChaControl ReadyIndependentActor() => _actorReady && !AstralBlocked ? _actorSession?.Actor : null;
        private ChaControl _hActor;
        private ActorLoadSession _actorSession;
        private GameObject _actorStage;
        private Coroutine _actorRoutine;
        private string _actorStatus = "未呼出", _queuedActorPreset;
        private bool _actorReady, _actorLoaderAvailable, _actorPreparing;
        private Behaviour _actorPreparationAbmx;
        private object _actorPreparationSolver;
        private Harmony _actorHarmony;
        private BepInEx.Configuration.ConfigEntry<float> _actorTimeout;
        private BepInEx.Configuration.ConfigEntry<bool> _actorPlacementSaved;
        private BepInEx.Configuration.ConfigEntry<Vector3> _actorSavedPosition, _actorSavedRotation;
        private string _actorPlacementStatus = "";
        private Vector3 _actorSliderCenter;
        private int _actorRequest, _actorBodyRevision;
        private readonly string[] _stageText = new[] { "0", "0", "0", "0", "0", "0" };

        private void InitIndependentActor() {
            LoadActorPlaces();
            InitActorLook();
            _actorTimeout = Config.Bind("IndependentActor", "LoadTimeoutSeconds", 120f, "追加キャラのモデル読込上限（秒）");
            _actorPlacementSaved = Config.Bind("IndependentActor", "PlacementSaved", false, "保存したワールド座標・角度で呼び出す");
            _actorSavedPosition = Config.Bind("IndependentActor", "Position", Vector3.zero, "呼出キャラの保存位置 XYZ（ワールド座標）");
            _actorSavedRotation = Config.Bind("IndependentActor", "Rotation", Vector3.zero, "呼出キャラの保存角度 XYZ（度）");
            _actorHarmony = new Harmony(Guid + ".independent-loader");
            try {
                OwnedCharacterLoader.Initialize(_actorHarmony);
                _actorHarmony.Patch(AccessTools.Method(typeof(HSceneProc), "ShortCut", Type.EmptyTypes), prefix: new HarmonyMethod(typeof(Plugin), nameof(IndependentLiquidShortcut)));
                _actorHarmony.Patch(AccessTools.Method(_tCtrl, "DisableEnableDefaultAnim"), prefix: new HarmonyMethod(typeof(Plugin), nameof(IndependentDefaultAnimation)));
                var shoulder = AccessTools.TypeByName("AdvIKPlugin.AdvIKShoulderRotator");
                if (shoulder != null) _actorHarmony.Patch(AccessTools.Method(shoulder, "Start"), prefix: new HarmonyMethod(typeof(Plugin), nameof(IndependentShoulderStart)));
                var abmx = AccessTools.TypeByName("KKABMX.Core.BoneController");
                if (abmx != null) _actorHarmony.Patch(ResolveActorAbmxReload(abmx), postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterActorAbmxReload)));
                _actorLoaderAvailable = true;
            }
            catch (Exception ex) { _actorHarmony.UnpatchSelf(); _actorStatus = "呼出非対応: " + ex.GetBaseException().Message; LogError(_actorStatus + "\n" + ex); }
        }
        private static System.Reflection.MethodInfo ResolveActorAbmxReload(Type abmx) {
            // KKAPI also declares OnReload(GameMode); name-only lookup is ambiguous.
            var method = abmx.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly)
                .SingleOrDefault(m => m.Name == "OnReload" && m.GetParameters().Length == 2
                    && m.GetParameters()[0].ParameterType.FullName == "KKAPI.GameMode" && m.GetParameters()[1].ParameterType == typeof(bool));
            return method ?? throw new MissingMethodException(abmx.FullName, "OnReload(KKAPI.GameMode, bool)");
        }
        private static bool IndependentShoulderStart(Component __instance) {
            var actor = __instance.GetComponentInParent<ChaControl>();
            return !IndependentActorApi.IsIndependentActor(actor) || __instance.GetComponent("FullBodyBipedIK") != null;
        }
        private static bool IndependentDefaultAnimation(object __instance, bool enableVMD) {
            var p = Instance;
            if (p == null || !p.IsIndependentTarget || !ReferenceEquals(__instance, p._controller)) return true;
            if (p._originalAnimator != null) p._originalAnimator.enabled = enableVMD ? false : p._animatorEnabled;
            if (p._originalIk != null) p._originalIk.enabled = enableVMD ? false : p._ikEnabled;
            return false;
        }
        private static void AfterActorAbmxReload(object __instance) {
            var p = Instance;
            if (p != null && p.IsIndependentTarget && ReferenceEquals(ReadProperty(__instance, "ChaControl"), p._trialChara)) p._actorBodyRevision++;
        }
        private void OnDisable() { DisposeIndependentActor(); if (_vmdOwnsCharacter || _loadRoutine != null) ReleaseCharacter(); }
        private void TickIndependentActor() {
            if ((!_cfgEnabled.Value || _hSceneProc == null || !_insideHScene) && (_actorSession != null || _actorRoutine != null)) DisposeIndependentActor();
            if (_actorSession != null && _actorSession.Error != null) { string error = _actorSession.Error.GetBaseException().Message; DisposeIndependentActor(); _actorStatus = "呼出失敗: " + error; }
            if (_actorReady && _actorSession?.Actor == null) { DisposeIndependentActor(); _actorStatus = "呼出キャラが失われました"; }
            TickActorCamera();
        }
        private void SwitchActorTarget(bool independent) {
            if (AstralBlocked) return;
            if (_target.Independent == independent) return;
            if (!independent && _actorRoutine != null) DisposeIndependentActor();
            ReleaseCharacter(); ReleaseActorCamera(); RetireController();
            _modelReloadAt = -1; _modelResumeTime = -1;
            _target.Generation++; _target.Independent = independent; _target.Scene = _hSceneProc;
            _target.Actor = independent ? _actorSession?.Actor : _hActor;
            _trialChara = _target.Actor;
            NeckTrace("factory-return");
            _preparedBoneRoot = null;
            if (_trialChara != null && (!independent || _actorReady)) PrepareDiagnosticTrial(_trialChara);
        }
        private void RequestCurrentHActor() {
            if (AstralBlocked || !_cfgEnabled.Value || !_actorLoaderAvailable || !_insideHScene || _hActor == null) {
                _actorStatus = "現在のHキャラを呼び出せません（キャラ未準備または機能利用不可）";
                _presetMessage = _actorStatus;
                return;
            }
            try {
                var type = AccessTools.TypeByName("KKAPI.Chara.CharacterExtensions");
                var method = type == null ? null : AccessTools.Method(type, "GetSourceFilePath", new[] { typeof(ChaFile) });
                if (method == null) throw new InvalidOperationException("元カードパス取得APIが利用できません");
                // Replacements may load directly into the live actor. Normal H creation
                // instead copies the original Heroine data and loses its file identity.
                string path = method.Invoke(null, new object[] { _hActor.chaFile }) as string;
                if (string.IsNullOrEmpty(path)) {
                    var heroine = _hSceneProc.flags?.lstHeroine?.FirstOrDefault(h => h != null && ReferenceEquals(h.chaCtrl, _hActor));
                    if (heroine != null) path = method.Invoke(null, new object[] { heroine.charFile }) as string;
                }
                if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("現在キャラに元カードの読込パスが記録されていません");
                if (!File.Exists(path)) throw new FileNotFoundException("元カードが見つかりません: " + path);
                Logger.LogInfo("[Actor] current-card path=" + path);
                RequestActorCard(path);
                _presetMessage = _actorStatus;
            } catch (Exception ex) { _actorStatus = "現在キャラの呼出失敗: " + ex.GetBaseException().Message; _presetMessage = _actorStatus; Logger.LogWarning(_actorStatus); LogError(_actorStatus); }
        }
        private void RequestActorCard(string path) {
            if (AstralBlocked) return; _lastActorCardRequest=path;
            if (!_actorLoaderAvailable || !_insideHScene) return;
            var card = new ChaFileControl();
            try { if (!File.Exists(path) || !card.LoadCharaFile(path)) throw new InvalidOperationException("有効なキャラカードではありません"); }
            catch (Exception ex) { _actorStatus = "カード読込失敗: " + ex.GetBaseException().Message; return; }
            _settings.LastActorCardDirectory = Path.GetDirectoryName(path); SaveSettings("actor-card");
            StartActorCard(card);
        }
        private void StartActorCard(ChaFileControl card) {
            DisposeIndependentActor();
            SwitchActorTarget(true);
            _target.Generation++; _target.Scene = _hSceneProc;
            int request = ++_actorRequest;
            _actorRoutine = StartCoroutine(GuardActorLoad(CreateActor(card, request), request));
        }
        private IEnumerator GuardActorLoad(IEnumerator workflow, int request) {
            yield return null;
            var stack = new Stack<IEnumerator>(); stack.Push(workflow);
            try {
                while (stack.Count > 0 && request == _actorRequest) {
                    object next = null; bool more = false; Exception error = null;
                    try { more = stack.Peek().MoveNext(); if (more) next = stack.Peek().Current; }
                    catch (Exception ex) { error = ex; }
                    if (error != null) { _actorRoutine = null; DisposeIndependentActor(); _actorStatus = "呼出失敗: " + error.GetBaseException().Message; LogError(_actorStatus); yield break; }
                    if (!more) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                    if (next is IEnumerator nested) stack.Push(nested); else yield return next;
                }
            } finally { while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose(); }
            if (request == _actorRequest) _actorRoutine = null;
        }
        private IEnumerator CreateActor(ChaFileControl card, int request) {
            _actorStatus = "待機中";
            float deadline = Time.realtimeSinceStartup + 30;
            yield return null; // Let the previous controller/actor finish Unity destruction.
            while (Manager.Character.loading.isLoading || Manager.Scene.IsOverlap || _hActor == null || _hActor.objTop == null) {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("共有ロード・Hキャラ準備待機");
                yield return null;
            }
            _actorStage = new GameObject("VMD Independent Stage");
            var anchor = _hActor != null ? _hActor.objTop.transform : null;
            if (anchor != null) {
                Vector3 forward = Vector3.ProjectOnPlane(anchor.forward, Vector3.up);
                _actorStage.transform.rotation = forward.sqrMagnitude > .001f ? Quaternion.LookRotation(forward) : Quaternion.identity;
                _actorStage.transform.position = anchor.position + _actorStage.transform.right * 2;
            }
            if (_actorPlacementSaved.Value && IsFinitePlacement(_actorSavedPosition.Value) && IsFinitePlacement(_actorSavedRotation.Value))
                _actorStage.transform.SetPositionAndRotation(_actorSavedPosition.Value, Quaternion.Euler(_actorSavedRotation.Value));
            int id = UnityEngine.Object.FindObjectsOfType<ChaControl>().Select(c => c.chaID).DefaultIfEmpty(0).Max() + 1;
            _actorSession = new ActorLoadSession();
            OwnedCharacterLoader.Creating = _actorSession;
            try {
                _target.Actor = card.parameter.sex == 0 ? Manager.Character.CreateMale(card.parameter.exType, _actorStage, id, card, true) : Manager.Character.CreateFemale(_actorStage, id, card, true);
            } finally { OwnedCharacterLoader.Creating = null; }
            _trialChara = _target.Actor;
            if (_trialChara == null) throw new InvalidOperationException("キャラ生成失敗");
            _actorStatus = "モデル読込中";
            deadline = Time.realtimeSinceStartup + Mathf.Clamp(_actorTimeout.Value, 10, 1800);
            // A separate monitor also covers Unity Coroutine handles returned by the native loader.
            _trialChara.StartCoroutine(_trialChara.LoadAsync(true, true));
            while (!_trialChara.loadEnd) {
                if (_actorSession.Error != null) throw _actorSession.Error;
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("モデル読込");
                yield return null;
            }
            if (_trialChara.objTop == null || _trialChara.objBodyBone == null || _trialChara.objAnim == null || _trialChara.animBody == null) throw new InvalidOperationException("モデル構造が不足しています");
            var placement = _trialChara.gameObject.AddComponent<IndependentActorPlacementDriver>();
            NeckTrace("model-loaded");
            placement.Stage = _actorStage.transform;
            placement.Apply();
            _trialChara.SetActiveTop(true); _trialChara.visibleAll = true;
            _actorStatus = "体型補正待機中";
            yield return null;
            var abmxType = AccessTools.TypeByName("KKABMX.Core.BoneController");
            var abmx = abmxType == null ? null : _trialChara.GetComponent(abmxType) as Behaviour;
            deadline = Time.realtimeSinceStartup + 30;
            if (abmx != null) {
                if (!abmx.enabled) throw new InvalidOperationException("ABMXが無効です");
                foreach (string field in new[] { "_baselineKnown", "_previousAnimSpeed" }) if (AccessTools.Field(abmxType, field) == null) throw new MissingFieldException(abmxType.FullName, field);
                while (!AbmxSettled(abmx)) { if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("ABMX初期補正"); yield return null; }
                CaptureMainGameControl(_trialChara); _actorPreparing = true;
                _actorPreparationAbmx = abmx;
                _actorPreparationSolver = _originalIk == null ? null : AccessTools.Field(_originalIk.GetType(), "solver")?.GetValue(_originalIk);
                int applied = _abmxApplied;
                while (applied == _abmxApplied) { if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("ABMX効果解除"); yield return null; }
                AccessTools.Property(abmxType, "NeedsFullRefresh").SetValue(abmx, true, null);
                yield return null;
                while (!AbmxSettled(abmx)) { if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("ABMX基準構築"); yield return null; }
            }
            yield return new WaitForEndOfFrame(); yield return new WaitForEndOfFrame();
            _actorPreparing = false; _actorPreparationAbmx = null; _actorPreparationSolver = null;
            if (_trialChara.GetComponentInChildren<ExpressionBone>() == null) _trialChara.InitializeExpression(false);
            var boneNames = new HashSet<string>(_trialChara.objBodyBone.GetComponentsInChildren<Transform>(true).Select(t => t.name));
            string[] required = { "cf_j_hips", "cf_j_spine01", "cf_j_head", "cf_j_arm00_L", "cf_j_arm00_R", "cf_j_forearm01_L", "cf_j_forearm01_R", "cf_j_thigh00_L", "cf_j_thigh00_R", "cf_j_leg01_L", "cf_j_leg01_R", "cf_j_foot_L", "cf_j_foot_R" };
            string missing = string.Join(",", required.Where(n => !boneNames.Contains(n)));
            if (missing.Length != 0) throw new InvalidOperationException("VMD必須骨がありません: " + missing);
            PrepareDiagnosticTrial(_trialChara);
            _actorStatus = "肌マテリアル適用中";
            yield return ApplySettledActorMaterials(_trialChara);
            _actorSession.Generation.Reconcile(obj => obj != null && obj.transform.IsChildOf(_trialChara.transform));
            _actorReady = true; _actorStatus = "準備完了"; SetState(PluginState.CharaReady);
            NeckTrace("actor-ready");
            DumpActorMaterials(_trialChara, "ready");
            StartCoroutine(DumpSettledActorMaterials(_trialChara, request));
            RefreshActorPlacementText();
            LogInfo("[Actor] ready components=" + string.Join(",", _trialChara.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().FullName)));
            string preset = _queuedActorPreset; _queuedActorPreset = null;
            if (preset != null) { bool switching = _myListSwitching; _myListSwitching = _myListRunning; try { ApplyPreset(preset); } finally { _myListSwitching = switching; } }
        }
        private static bool AbmxSettled(Behaviour abmx) => Equals(AccessTools.Field(abmx.GetType(), "_baselineKnown").GetValue(abmx), true) &&
            !Equals(ReadProperty(abmx, "NeedsFullRefresh"), true) && !Equals(ReadProperty(abmx, "NeedsBaselineUpdate"), true) && AccessTools.Field(abmx.GetType(), "_previousAnimSpeed").GetValue(abmx) == null;
        private void DisposeIndependentActor() {
            ReleaseActorGizmo();
            NeckTrace("dispose"); FinishNeckDiagnostics();
            if (AstralBlocked) AbortAstral();
            RestoreActorLook();
            _actorRequest++;
            bool selected = IsIndependentTarget;
            if (selected) { _target.Generation++; _controlTransition.Invalidate(); _modelReloadAt = -1; _modelResumeTime = -1; }
            _queuedActorPreset = null;
            var session = _actorSession; var actor = session?.Actor; var stage = _actorStage; var routine = _actorRoutine;
            session?.Generation.Invalidate(); // Late callbacks must be rejected before any cleanup can fail.
            _actorRoutine = null;
            try {
                ControlTransition.Cleanup(ex => LogError("[Actor] dispose: " + ex),
                    () => { if (routine != null) StopCoroutine(routine); },
                    () => { if (selected) ReleaseCharacter(); },
                    () => { if (selected) ReleaseActorCamera(); },
                    () => { if (selected) RetireController(); },
                    () => session?.Cancel(),
                    () => { if (!ReferenceEquals(actor, null)) OwnedCharacterLoader.Sessions.Remove(actor); if (actor != null) Manager.Character.DeleteChara(actor); },
                    () => { if (stage != null) Destroy(stage); });
            } finally {
                if (selected) { _trialChara = null; _controller = null; _fullBodyOwned = false; _ownedSolver = null; _abmx = null; }
                _actorStage = null; _actorSession = null; _actorReady = false;
                _actorPreparing = false; _actorPreparationAbmx = null; _actorPreparationSolver = null;
                _actorStatus = "未呼出";
                _actorCameraRequested = false; _actorCameraStatus = "";
            }
        }
        private void ApplyActorPlacement(Vector3 position, Quaternion rotation) {
            if (_actorStage == null) return;
            var stage = _actorStage.transform;
            var actor = _actorSession?.Actor;
            // Keep the owned actor attached even after a third-party hierarchy change.
            if (actor != null && !actor.transform.IsChildOf(stage))
                actor.transform.SetParent(stage, true);
            string beforePlacement = DescribeActorPlacement("before", actor);
            stage.SetPositionAndRotation(position, rotation);
            if (actor != null) {
                actor.transform.localPosition = Vector3.zero;
                actor.transform.localRotation = Quaternion.identity;
            }
            // Re-evaluate the cached VMD world poses, including while paused.
            // Audio position is intentionally untouched.
            if (IsIndependentTarget && _controller != null && _vmdOwnsCharacter && _loadRoutine == null)
                _motionClockSequence?.Apply(_currentAnimTime);
            QueueActorPlacementDiagnostic(actor, beforePlacement);
        }
        private static bool IsFinitePlacement(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        private void RefreshActorPlacementText() {
            var position = _actorStage.transform.position;
            _actorSliderCenter = position;
            var rotation = _actorStage.transform.eulerAngles;
            for (int i = 0; i < 3; i++) {
                _stageText[i] = position[i].ToString("0.###");
                _stageText[i + 3] = rotation[i].ToString("0.###");
            }
        }
        private void DrawIndependentActor() {
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(!IsIndependentTarget, "Hキャラ", "Button") && IsIndependentTarget) SwitchActorTarget(false);
            if (GUILayout.Toggle(IsIndependentTarget, "呼出キャラ", "Button") && !IsIndependentTarget) SwitchActorTarget(true);
            GUILayout.EndHorizontal();
            GUILayout.Label("呼出キャラ: " + _actorStatus);
            GUILayout.BeginHorizontal();
            GUI.enabled = _actorLoaderAvailable && _insideHScene && !_pickerBusy;
            if (GUILayout.Button("キャラカードを選んで呼び出す")) BeginPickActorCard();
            GUI.enabled = true;
            if (GUILayout.Button(_actorRoutine != null ? "呼出取消" : "呼出キャラを消す")) DisposeIndependentActor();
            GUILayout.EndHorizontal();
            if (_actorStage == null) return;
            GUILayout.Label("呼出キャラの配置（ワールド座標・角度）");
            string[] labels = { "位置X", "位置Y", "位置Z", "角度X", "向きY", "角度Z" };
            for (int i = 0; i < 6; i++) {
                GUILayout.BeginHorizontal();
                GUILayout.Label(labels[i], GUILayout.Width(48));
                float current;
                if (!float.TryParse(_stageText[i], out current) || float.IsNaN(current) || float.IsInfinity(current))
                    current = i < 3 ? _actorStage.transform.position[i] : _actorStage.transform.eulerAngles[i - 3];
                float min = i < 3 ? _actorSliderCenter[i] - 10f : 0f;
                float max = i < 3 ? _actorSliderCenter[i] + 10f : 360f;
                float sliderInput = Mathf.Clamp(current, min, max);
                float next = GUILayout.HorizontalSlider(sliderInput, min, max, GUILayout.MinWidth(60), GUILayout.ExpandWidth(true));
                bool sliderChanged = next != sliderInput;
                string previousText = _stageText[i];
                string editedText = GUILayout.TextField(previousText, GUILayout.Width(75));
                // Consume the slider's return value directly. A focused text editor must
                // not replace a drag result with its previous text in the same event.
                _stageText[i] = sliderChanged ? next.ToString("0.###") : editedText;
                float value;
                if ((sliderChanged || previousText != _stageText[i]) && float.TryParse(_stageText[i], out value) && !float.IsNaN(value) && !float.IsInfinity(value)) {
                    if (i < 3) {
                        var position = _actorStage.transform.position; position[i] = value;
                        ApplyActorPlacement(position, _actorStage.transform.rotation);
                        if (!sliderChanged && (value < min || value > max)) _actorSliderCenter[i] = value;
                    } else {
                        var rotation = _actorStage.transform.eulerAngles;
                        for (int axis = 0; axis < 3; axis++) {
                            float angle;
                            if (float.TryParse(_stageText[axis + 3], out angle) && !float.IsNaN(angle) && !float.IsInfinity(angle)) rotation[axis] = angle;
                        }
                        ApplyActorPlacement(_actorStage.transform.position, Quaternion.Euler(rotation));
                    }
                    AutoSaveActorPlace();
                }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("位置スライダーの中心を現在位置にする（±10）"))
                _actorSliderCenter = _actorStage.transform.position;
            if (GUILayout.Button("配置を適用して記憶")) {
                var values = new float[6]; bool valid = true;
                for (int i = 0; i < 6; i++) valid &= float.TryParse(_stageText[i], out values[i]) && !float.IsNaN(values[i]) && !float.IsInfinity(values[i]);
                if (valid) {
                    var position = new Vector3(values[0], values[1], values[2]);
                    var rotation = new Vector3(values[3], values[4], values[5]);
                    ApplyActorPlacement(position, Quaternion.Euler(rotation));
                    _actorSavedPosition.Value = position; _actorSavedRotation.Value = rotation;
                    _actorPlacementSaved.Value = true; Config.Save();
                    _actorPlacementStatus = "配置を保存しました。次回の呼出・ゲーム再起動後も使用します。";
                } else _actorPlacementStatus = "座標と角度には有限の数値を入力してください。";
            }
            if (GUILayout.Button("保存配置を解除（次回はHキャラの右側）")) {
                _actorPlacementSaved.Value = false; Config.Save();
                _actorPlacementStatus = "保存配置を解除しました。現在の位置は維持します。";
            }
            if (!string.IsNullOrEmpty(_actorPlacementStatus)) GUILayout.Label(_actorPlacementStatus);
            DrawActorPlaces();
            bool followCamera = GUILayout.Toggle(_actorCameraRequested, "既存サブカメラで呼出キャラを追従");
            if (followCamera != _actorCameraRequested) {
                _actorCameraRequested = followCamera;
                if (!followCamera) { ReleaseActorCamera(); _actorCameraStatus = ""; }
            }
            if (_actorCameraRequested && !string.IsNullOrEmpty(_actorCameraStatus)) GUILayout.Label(_actorCameraStatus);
            DrawActorLook();
            DrawActorAppearance();
        }
    }
}
