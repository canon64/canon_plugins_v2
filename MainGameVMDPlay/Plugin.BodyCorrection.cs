using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MainGameVMDPlay
{
    public sealed partial class Plugin
    {
        private readonly BepInEx.Configuration.ConfigEntry<int>[] _bodyChoices = new BepInEx.Configuration.ConfigEntry<int>[10];
        private ExpressionBone _bodyCorrection;
        private readonly Dictionary<FieldInfo, bool> _bodyBefore = new Dictionary<FieldInfo, bool>();
        private bool _expressionApplying, _expressionPatched;
        private int _expressionFrame = -1;
        private static readonly string[] BodyLabels = { "左腕", "右腕", "左脚", "右脚", "左前腕", "右前腕", "左太もも", "右太もも", "胸", "アクセサリ追従" };
        private static readonly string[] BodyFields = { "enableShoulderL", "enableShoulderR", "enableLegL", "enableLegR", "enableForearmL", "enableForearmR", "enableThighL", "enableThighR", "enableBust", "enableAcsNull" };

        private void InitBodyCorrectionSettings()
        {
            for (int i = 0; i < BodyFields.Length; i++) {
                _bodyChoices[i] = Config.Bind("Body correction", BodyFields[i], -1,
                    new BepInEx.Configuration.ConfigDescription(BodyLabels[i] + "：-1=初回の本体設定を採用、0=OFF、1=ON。全プリセット共通で記憶。",
                        new BepInEx.Configuration.AcceptableValueRange<int>(-1, 1)));
                _bodyChoices[i].SettingChanged += (sender, args) => ApplyBodyCorrection();
            }
        }

        private void CaptureBodyCorrection()
        {
            _bodyBefore.Clear();
            // Studio.AddObjectFemale.Add / AddCoroutine explicitly initializes this
            // after Load. Category setters target ChaInfo.expression, not a child search.
            if (_trialChara.expression == null) _trialChara.InitializeExpression(false);
            _bodyCorrection = _trialChara.expression;
            if (_bodyCorrection == null || _bodyCorrection.dictBone == null || _bodyCorrection.dictBone.Count == 0)
                throw new InvalidOperationException("Studio変形補正の初期化に失敗しました");
            foreach (var field in typeof(ExpressionBone).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (field.FieldType == typeof(bool) && field.Name.StartsWith("enable")) _bodyBefore[field] = (bool)field.GetValue(_bodyCorrection);
            for (int i = 0; i < BodyFields.Length; i++)
                if (_bodyChoices[i].Value < 0)
                    _bodyChoices[i].Value = (bool)AccessTools.Field(typeof(ExpressionBone), BodyFields[i]).GetValue(_bodyCorrection) ? 1 : 0;
            if (!_expressionPatched) {
                _trialHarmony.Patch(AccessTools.Method(typeof(ExpressionBone), "LateUpdate"), prefix: new HarmonyMethod(typeof(Plugin), nameof(ExpressionPrefix)));
                _trialHarmony.Patch(AccessTools.Method(_tCtrl.Assembly.GetType("KKVMDPlayPlugin.KKAutoTrack"), "ProcessAfter"), postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterVmdBones)));
                _expressionPatched = true;
            }
            ApplyBodyCorrection();
        }
        private void ApplyBodyCorrection()
        {
            if (_bodyCorrection == null || !_vmdOwnsCharacter) return;
            _bodyCorrection.enable = true;
            for (int i = 0; i < BodyLabels.Length; i++)
                if (_bodyChoices[i] != null && _bodyChoices[i].Value >= 0)
                    _trialChara.EnableExpressionCategory(i, _bodyChoices[i].Value != 0);
        }
        private void RestoreBodyCorrection()
        {
            if (_bodyCorrection != null) foreach (var item in _bodyBefore) item.Key.SetValue(_bodyCorrection, item.Value);
            _bodyBefore.Clear(); _bodyCorrection = null; _expressionFrame = -1;
        }
        private static bool ExpressionPrefix(ExpressionBone __instance)
        {
            var p = Instance;
            return p == null || !p._vmdOwnsCharacter || __instance != p._bodyCorrection || p._expressionApplying;
        }
        private static void AfterVmdBones(object ___controller)
        {
            var p = Instance;
            if (p == null || p.AstralBlocked || !p._vmdOwnsCharacter || !ReferenceEquals(p._controller, ___controller) || p._bodyCorrection == null || p._expressionFrame == Time.frameCount) return;
            p._expressionFrame = Time.frameCount;
            p._expressionApplying = true;
            try { p._bodyCorrection.LateUpdate(); } finally { p._expressionApplying = false; }
        }
        private void DrawBodyCorrection()
        {
            GUILayout.Label("Studio標準：部位別の変形補正（共通設定として自動保存）");
            if (_bodyCorrection == null) {
                GUILayout.Label("VMD読込時にStudioと同じ補正処理を初期化します（現在は未適用）");
            }
            for (int i = 0; i < BodyLabels.Length; i++) {
                string key = "body." + i;
                bool value = _bodyChoices[i].Value >= 0 ? _bodyChoices[i].Value != 0 :
                    _bodyCorrection == null || (bool)AccessTools.Field(typeof(ExpressionBone), BodyFields[i]).GetValue(_bodyCorrection);
                bool next = DrawBinaryControl(key, BodyLabels[i], value);
                if (next != value) { _bodyChoices[i].Value = next ? 1 : 0; Config.Save(); ApplyBodyCorrection(); }
            }
        }
    }
}
