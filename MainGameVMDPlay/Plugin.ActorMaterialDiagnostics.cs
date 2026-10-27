using System;
using System.Collections;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private IEnumerator ApplySettledActorMaterials(ChaControl cha)
        {
            // The stock reload notification can run before the async head is ready.
            // Replay saved edits only after the complete owned model has settled.
            var type = AccessTools.TypeByName("KK_Plugins.MaterialEditor.MaterialEditorCharaController");
            var controller = type == null ? null : cha.GetComponent(type);
            if (controller == null) yield break;
            var method = AccessTools.Method(type, "LoadData", new[] { typeof(bool), typeof(bool), typeof(bool) });
            if (method == null) throw new MissingMethodException(type.FullName, "LoadData(bool,bool,bool)");
            DumpActorMaterials(cha, "before-final-materials");
            yield return (IEnumerator)method.Invoke(controller, new object[] { false, false, false });
            DumpActorMaterials(cha, "after-final-materials");
        }
        private void DumpActorMaterials(ChaControl cha, string reason)
        {
            if (!_diagnosticsEnabled) return;
            if (cha == null || !IndependentActorApi.IsIndependentActor(cha)) return;
            try {
                var text = new StringBuilder();
                text.AppendLine(DateTime.Now.ToString("o") + " " + reason + " actor=" + cha.GetInstanceID());
                text.AppendLine("cheek=" + cha.fileStatus.hohoAkaRate + " tears=" + cha.tearsLv + " coordinate=" + cha.fileStatus.coordinateType);
                ResolveActorSkin();
                var skin = _actorSkinType == null ? null : cha.GetComponent(_actorSkinType);
                for (int i = 0; i < ActorModProperties.Length; i++)
                    if (skin != null && _actorSkinProperties[i] != null) text.AppendLine(ActorModProperties[i] + "=" + _actorSkinProperties[i].GetValue(skin, null));
                foreach (var renderer in cha.GetComponentsInChildren<Renderer>(true))
                    foreach (var mat in renderer.sharedMaterials) {
                        if (mat == null || (!mat.name.Contains("face") && !mat.name.Contains("body"))) continue;
                        text.AppendLine("renderer=" + renderer.name + " material=" + mat.name + " id=" + mat.GetInstanceID() + " shader=" + (mat.shader != null ? mat.shader.name : "<null>") + " supported=" + (mat.shader != null && mat.shader.isSupported));
                        foreach (string name in new[] { "_Color", "_overcolor1", "_overcolor2", "_BaseColor", "_1st_ShadeColor" })
                            if (mat.HasProperty(name)) text.AppendLine(name + "=" + mat.GetColor(name));
                        foreach (string name in new[] { "_MainTex", "_DetailMask", "_NormalMap", "_overtex1", "_overtex2" })
                            if (mat.HasProperty(name)) {
                                var tex = mat.GetTexture(name);
                                text.AppendLine(name + "=" + (tex != null ? tex.name + " " + tex.width + "x" + tex.height + " id=" + tex.GetInstanceID() : "<null>"));
                            }
                    }
                string path = Path.Combine(_pluginDir, "IndependentActorMaterials.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.WriteAllText(path, "");
                File.AppendAllText(path, text.ToString());
            } catch (Exception ex) { LogError("[ActorMaterial] " + ex); }
        }
        private IEnumerator DumpSettledActorMaterials(ChaControl cha, int request)
        {
            yield return new WaitForSecondsRealtime(2);
            if (request == _actorRequest && cha != null && ReferenceEquals(cha, _actorSession?.Actor)) DumpActorMaterials(cha, "settled");
        }
        private void ReapplyActorMaterials(ChaControl cha)
        {
            DumpActorMaterials(cha, "before-reapply");
            try {
                var type = AccessTools.TypeByName("KK_Plugins.MaterialEditor.MaterialEditorCharaController");
                var controller = type == null ? null : cha.GetComponent(type);
                if (controller == null) { _actorAppearanceStatus = "MaterialEditorが利用できません"; return; }
                if (Equals(AccessTools.Property(type, "CharacterLoading")?.GetValue(controller, null), true)) {
                    _actorAppearanceStatus = "マテリアル読込中です"; return;
                }
                AccessTools.Method(type, "RefreshBodyEdits", Type.EmptyTypes).Invoke(controller, null);
                StartCoroutine(DumpSettledActorMaterials(cha, _actorRequest));
                _actorAppearanceStatus = "保存済みの肌マテリアルを再適用しました。診断ログを記録します";
            } catch (Exception ex) { _actorAppearanceStatus = ex.GetBaseException().Message; }
        }
    }
}
