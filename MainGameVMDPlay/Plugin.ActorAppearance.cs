using Bindings = CanonPlugins.Shared.PluginInput;
using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private static bool IndependentLiquidShortcut(HSceneProc __instance)
        {
            var p = Instance;
            if (p == null
                || !ReferenceEquals(__instance, p._hSceneProc) || !p._cfgEnabled.Value
                || !Bindings.Down("MainGameVMDPlay.liquid", "Alpha5")) return true;
            // Do not send the same key to the H participant or type into a numeric field.
            if (GUIUtility.keyboardControl != 0 || __instance.sprite.isFade || __instance.sprite.GetFadeKindProc() == HSprite.FadeKindProc.OutEnd) return false;
            var targets = ClothingTargets.Capture(p._clothingTarget.Value,
                HFemales.GetValue(__instance) as System.Collections.Generic.List<ChaControl>,
                p.AstralBlocked ? null : p.ReadyIndependentActor());
            foreach (var cha in targets) {
            if (cha == null) continue;
            byte level = 2;
            for (int i = 0; i < 5; i++) if (cha.GetSiruFlags((ChaFileDefine.SiruParts)i) != 0) { level = 0; break; }
            for (int i = 0; i < 5; i++) if (!SetPoseEffect(cha, i == 0 ? 2 : i + 6, level)) cha.SetSiruFlags((ChaFileDefine.SiruParts)i, level);
            ActorUpdateSiru.Invoke(cha, new object[] { true });
            }
            return false;
        }
        private bool _showActorAppearance;
        private bool _showActorCoordinates;
        private Vector2 _actorCoordinateScroll;
        private string _actorAppearanceStatus;
        private static readonly string[] ActorClothesLabels = { "トップス", "ボトムス", "ブラ", "ショーツ", "手袋", "パンスト", "靴下", "室内靴", "外靴" };
        private static readonly string[] ActorLiquidLabels = { "顔", "胸・腹上", "腹下", "背中", "腰・尻" };
        private static readonly string[] ActorModLabels = { "涙(MOD)", "よだれ", "唾液", "汗(MOD)", "赤み(MOD)", "精液(MOD)", "肛門の精液", "鼻の精液", "尻の赤み", "愛液", "血液" };
        private static readonly string[] ActorModProperties = { "TearLevel", "DroolLevel", "SalivaLevel", "SweatLevel", "BlushLevel", "BukkakeLevel", "AnalBukkakeLevel", "CumInNoseLevel", "ButtLevel", "PussyJuiceLevel", "BloodLevel" };
        private static readonly string[] ActorModCounts = { "TearTexturesCount", "DroolTexturesCount", "SalivaTexturesCount", "WetTexturesFaceCount", "BlushTexturesFaceCount", "CumTexturesCount", "AnalCumTexturesCount", "CumInNoseTexturesCount", "ButtTexturesCount", "PussyJuiceTexturesCount", "BldTexturesCount" };
        private Type _actorSkinType, _actorTextureType;
        private readonly PropertyInfo[] _actorSkinProperties = new PropertyInfo[ActorModProperties.Length], _actorSkinCounts = new PropertyInfo[ActorModProperties.Length];
        private bool _actorSkinResolved;
        private static readonly MethodInfo ActorUpdateSiru = AccessTools.Method(typeof(ChaControl), "UpdateSiru", new[] { typeof(bool) });
        private static readonly MethodInfo ActorUpdateVisible = AccessTools.Method(typeof(ChaControl), "UpdateVisible", Type.EmptyTypes);

        private void DrawActorAppearance()
        {
            if (!IsIndependentTarget || !_actorReady || _trialChara == null) return;
            _showActorAppearance = GUILayout.Toggle(_showActorAppearance, "呼出キャラの衣装・着脱・肌の状態", GUI.skin.button);
            if (!_showActorAppearance) return;
            var cha = _trialChara;
            GUILayout.Label("対象: 呼出キャラ（H参加キャラには適用しません）");
            _showActorCoordinates = GUILayout.Toggle(_showActorCoordinates, "カード内のコーディネートを選ぶ", GUI.skin.button);
            if (_showActorCoordinates && cha.chaFile.coordinate != null) {
                GUILayout.Label("VMD・音楽を再生したまま衣装を切り替えます");
                int selectedCoordinate = -1;
                using (var scroll = new GUILayout.ScrollViewScope(_actorCoordinateScroll, GUILayout.Height(160))) {
                    _actorCoordinateScroll = scroll.scrollPosition;
                    for (int i = 0; i < cha.chaFile.coordinate.Length; i++) {
                        var coordinate = cha.chaFile.coordinate[i];
                        if (coordinate == null) continue;
                        string name = GetActorCoordinateName(cha, i);
                        bool current = cha.fileStatus.coordinateType == i;
                        if (GUILayout.Button((current ? "● " : "") + name)) selectedCoordinate = i;
                    }
                }
                if (selectedCoordinate >= 0) ApplyActorCoordinate(null, selectedCoordinate);
            }
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !_pickerBusy;
            if (GUILayout.Button("衣装カードを読み込む（再生を継続）")) BeginPickFile(false, false, true);
            GUI.enabled = enabled;
            if (!string.IsNullOrEmpty(_actorAppearanceStatus)) GUILayout.Label(_actorAppearanceStatus);
            if (GUILayout.Button("肌マテリアルをカード設定から再適用・診断")) ReapplyActorMaterials(cha);
            try {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("全て着る")) cha.SetClothesStateAll(0);
                if (GUILayout.Button("全て脱ぐ")) cha.SetClothesStateAll(3);
                GUILayout.EndHorizontal();
                for (int i = 0; i < cha.fileStatus.clothesState.Length; i++) {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(i < ActorClothesLabels.Length ? ActorClothesLabels[i] : "衣類 " + i, GUILayout.Width(90));
                    string[] states = { "着衣", "半脱ぎ1", "半脱ぎ2", "脱衣" };
                    for (byte state = 0; state < 4; state++) {
                        GUI.enabled = enabled && (cha.IsClothesStateType(i, state) || state == 0 || state == 3);
                        if (GUILayout.Toggle(cha.fileStatus.clothesState[i] == state, states[state], GUI.skin.button) && cha.fileStatus.clothesState[i] != state) cha.SetClothesState(i, state);
                    }
                    GUI.enabled = enabled;
                    GUILayout.EndHorizontal();
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("アクセサリ全表示")) cha.SetAccessoryStateAll(true);
                if (GUILayout.Button("アクセサリ全非表示")) cha.SetAccessoryStateAll(false);
                GUILayout.EndHorizontal();
                for (int i = 0; i < cha.fileStatus.showAccessory.Length; i++) {
                    if (cha.objAccessory == null || i >= cha.objAccessory.Length || cha.objAccessory[i] == null) continue;
                    bool visible = GUILayout.Toggle(cha.fileStatus.showAccessory[i], "アクセサリ " + (i + 1) + ": " + cha.objAccessory[i].name);
                    if (visible != cha.fileStatus.showAccessory[i]) cha.SetAccessoryState(i, visible);
                }
                int cheek = Mathf.RoundToInt(cha.fileStatus.hohoAkaRate * 100f);
                int next = ActorValueRow("頬赤 (%)", cheek, 100);
                if (next != cheek && !SetPoseEffect(cha, 0, next)) cha.ChangeHohoAkaRate(next / 100f);
                next = ActorValueRow("涙(標準)", cha.tearsLv, 3);
                if (next != cha.tearsLv && !SetPoseEffect(cha, 1, next)) { cha.tearsLv = (byte)next; ActorUpdateVisible.Invoke(cha, null); }
                for (int i = 0; i < cha.fileStatus.siruLv.Length; i++) {
                    int current = cha.fileStatus.siruLv[i];
                    next = ActorValueRow("精液: " + (i < ActorLiquidLabels.Length ? ActorLiquidLabels[i] : i.ToString()), current, 3);
                    if (next != current && !SetPoseEffect(cha, i == 0 ? 2 : i + 6, next)) { cha.SetSiruFlags((ChaFileDefine.SiruParts)i, (byte)next); ActorUpdateSiru.Invoke(cha, new object[] { true }); }
                }
                ResolveActorSkin();
                var skin = _actorSkinType == null ? null : cha.GetComponent(_actorSkinType);
                for (int i = 0; i < ActorModProperties.Length; i++) {
                    var property = _actorSkinProperties[i];
                    if (skin == null || property == null || !property.CanWrite) { GUILayout.Label(ActorModLabels[i] + ": Additional Skin Effectsが利用できません"); continue; }
                    int maximum = Convert.ToInt32(_actorSkinCounts[i]?.GetValue(null, null) ?? 0);
                    if (maximum <= 0) { GUILayout.Label(ActorModLabels[i] + ": テクスチャ未準備"); continue; }
                    int current = Convert.ToInt32(property.GetValue(skin, null));
                    next = ActorValueRow(ActorModLabels[i], current, maximum);
                    if (next != current && !(i < 4 && SetPoseEffect(cha, i + 3, next))) property.SetValue(skin, next, null);
                }
                if (GUILayout.Button("頬赤・涙・精液・汗など全ての肌効果を消す")) {
                    for (int c = 0; c < 11; c++) SetPoseEffect(cha,c,0);
                    cha.ChangeHohoAkaRate(0); cha.tearsLv = 0; ActorUpdateVisible.Invoke(cha, null);
                    for (int i = 0; i < cha.fileStatus.siruLv.Length; i++) cha.SetSiruFlags((ChaFileDefine.SiruParts)i, 0);
                    ActorUpdateSiru.Invoke(cha, new object[] { true });
                    if (skin != null) foreach (var property in _actorSkinProperties) if (property?.CanWrite == true) property.SetValue(skin, 0, null);
                }
            } catch (ExitGUIException) { throw; }
            catch (Exception ex) { _actorAppearanceStatus = "調整失敗: " + ex.GetBaseException().Message; }
            finally { GUI.enabled = enabled; }
        }
        private static int ActorValueRow(string label, int current, int maximum)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            int value = Mathf.RoundToInt(GUILayout.HorizontalSlider(current, 0, maximum));
            string text = GUILayout.TextField(value.ToString(), GUILayout.Width(48));
            if (int.TryParse(text, out int parsed)) value = Mathf.Clamp(parsed, 0, maximum);
            if (GUILayout.Button("消す", GUILayout.Width(48))) value = 0;
            GUILayout.EndHorizontal(); return value;
        }
        private void ResolveActorSkin()
        {
            if (_actorSkinResolved) return;
            _actorSkinResolved = true;
            _actorSkinType = AccessTools.TypeByName("KK_SkinEffects.SkinEffectsController");
            _actorTextureType = _actorSkinType?.Assembly.GetType("KK_SkinEffects.TextureLoader");
            for (int i = 0; i < ActorModProperties.Length; i++) {
                _actorSkinProperties[i] = _actorSkinType?.GetProperty(ActorModProperties[i]);
                _actorSkinCounts[i] = _actorTextureType?.GetProperty(ActorModCounts[i], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
        }
        private void ApplyActorCoordinate(string path, int slot = -1, ChaControl explicitActor = null)
        {
            if (AstralBlocked || !_actorReady || (explicitActor == null && (!IsIndependentTarget || _trialChara == null))) return;
            var cha = explicitActor ?? _trialChara;
            byte[] backup = null; Version version = null;
            int previousSlot = cha.fileStatus.coordinateType, previousBack = cha.fileStatus.backCoordinateType;
            try {
                ChaFileCoordinate coordinate = null;
                if (slot >= 0) {
                    if (cha.chaFile.coordinate == null || slot >= cha.chaFile.coordinate.Length || cha.chaFile.coordinate[slot] == null)
                        throw new InvalidOperationException("コーディネートが見つかりません");
                } else {
                    coordinate = new ChaFileCoordinate(); coordinate.MemberInit();
                    if (!coordinate.LoadFile(path)) throw new InvalidOperationException("衣装カードを読み込めません");
                }
                backup = cha.nowCoordinate.SaveBytes(); version = cha.nowCoordinate.loadVersion;
                bool applied = slot >= 0 ? cha.ChangeCoordinateType((ChaFileDefine.CoordinateType)slot) : cha.SetNowCoordinate(coordinate);
                if (!applied) throw new InvalidOperationException("衣装の適用に失敗しました");
                cha.Reload(noChangeClothes: false, noChangeHead: true, noChangeHair: true, noChangeBody: true);
                RefreshCoordinateMotionReferences(cha);
                _actorAppearanceStatus = "衣装を変更しました（再生状態を維持）";
            } catch (Exception ex) {
                _actorAppearanceStatus = "衣装変更失敗: " + ex.GetBaseException().Message;
                if (backup != null && cha != null) try {
                    cha.fileStatus.coordinateType = previousSlot; cha.fileStatus.backCoordinateType = previousBack;
                    cha.nowCoordinate.LoadBytes(backup, version);
                    cha.Reload(noChangeClothes: false, noChangeHead: true, noChangeHair: true, noChangeBody: true);
                    RefreshCoordinateMotionReferences(cha);
                } catch (Exception restore) { _actorAppearanceStatus += " / 復元失敗: " + restore.GetBaseException().Message; }
                LogError(_actorAppearanceStatus);
            }
        }
        private void RefreshCoordinateMotionReferences(ChaControl cha)
        {
            if (!ReferenceEquals(cha, _trialChara) || _controller == null) return;
            // Clothes/accessories are replaced; the body skeleton and motion stay alive.
            var skirt = AccessTools.Field(_controller.GetType(), "skirtBoneAdjust")?.GetValue(_controller);
            if (skirt != null) {
                // Dependency DoSetup appends colliders; clear stale clothing references first.
                (AccessTools.Field(skirt.GetType(), "boneColliders")?.GetValue(skirt) as System.Collections.IList)?.Clear();
                AccessTools.Method(skirt.GetType(), "DoSetup", new[] { typeof(GameObject) })?.Invoke(skirt, new object[] { cha.gameObject });
            }
            ApplyBodyCorrection();
        }
    }
}
