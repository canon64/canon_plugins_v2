using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Studio;
using UnityEngine;

namespace StudioFacePresetTool.Services
{
    internal sealed class StudioFaceRuntime
    {
        private readonly PluginLogger _log;

        internal StudioFaceRuntime(PluginLogger log)
        {
            _log = log;
        }

        internal bool IsStudioReady()
        {
            return Singleton<Studio.Studio>.Instance != null;
        }

        internal bool TryCaptureFromSelected(out FacePreset preset, out string message)
        {
            _log.Entry(nameof(TryCaptureFromSelected));
            preset = null;
            message = string.Empty;

            List<OCIChar> selected = GetSelectedChars();
            bool hasSelection = selected.Count > 0;
            _log.Branch("capture-has-selection", hasSelection, "count=" + selected.Count);
            if (!hasSelection)
            {
                message = "選択中キャラがいません";
                return false;
            }

            ChaControl cha = GetChaControl(selected[0]);
            bool hasCha = cha != null;
            _log.Branch("capture-has-cha", hasCha, string.Empty);
            if (!hasCha)
            {
                message = "ChaControl 取得に失敗";
                return false;
            }

            try
            {
                GetCtrlMinMax(cha, "eyesCtrl", out float eyeMin, out float eyeMax);
                GetCtrlMinMax(cha, "mouthCtrl", out float mouthMin, out float mouthMax);

                preset = new FacePreset
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "new-preset",
                    Eyebrow = cha.GetEyebrowPtn(),
                    Eye = cha.GetEyesPtn(),
                    Mouth = cha.GetMouthPtn(),
                    EyeMin = eyeMin,
                    EyeMax = eyeMax,
                    MouthMin = mouthMin,
                    MouthMax = mouthMax,
                    Tears = cha.tearsLv,
                    Cheek = cha.fileStatus != null ? cha.fileStatus.hohoAkaRate : 0f
                };

                message = "現在表情を取得しました";
                _log.Info("capture success eyebrow=" + preset.Eyebrow
                    + " eye=" + preset.Eye
                    + " mouth=" + preset.Mouth
                    + " eyeMin=" + preset.EyeMin
                    + " eyeMax=" + preset.EyeMax
                    + " mouthMin=" + preset.MouthMin
                    + " mouthMax=" + preset.MouthMax
                    + " tears=" + preset.Tears
                    + " cheek=" + preset.Cheek);
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("capture failed", ex);
                message = "現在表情取得で例外";
                return false;
            }
        }

        internal int ApplyToSelected(FacePreset preset, out string message)
        {
            _log.Entry(nameof(ApplyToSelected));
            message = string.Empty;

            bool presetNull = preset == null;
            _log.Branch("apply-preset-null", presetNull, string.Empty);
            if (presetNull)
            {
                message = "適用対象プリセットが空です";
                return 0;
            }

            List<OCIChar> selected = GetSelectedChars();
            bool hasSelection = selected.Count > 0;
            _log.Branch("apply-has-selection", hasSelection, "count=" + selected.Count);
            if (!hasSelection)
            {
                message = "選択中キャラがいません";
                return 0;
            }

            int applied = 0;
            for (int i = 0; i < selected.Count; i++)
            {
                OCIChar item = selected[i];
                ChaControl cha = GetChaControl(item);
                bool hasCha = cha != null;
                _log.Branch("apply-has-cha", hasCha, "index=" + i);
                if (!hasCha)
                    continue;

                try
                {
                    ApplyPresetToCha(cha, preset);
                    applied++;
                }
                catch (Exception ex)
                {
                    _log.Error("apply preset failed index=" + i, ex);
                }
            }

            message = "適用数=" + applied;
            _log.Info("apply done count=" + applied + " preset=" + preset.Name);
            return applied;
        }

        internal List<OCIChar> GetSelectedChars()
        {
            _log.Entry(nameof(GetSelectedChars));
            List<OCIChar> result = new List<OCIChar>();
            try
            {
                Type studioType = Type.GetType("Studio.Studio, Assembly-CSharp");
                bool hasType = studioType != null;
                _log.Branch("selected-type-found", hasType, string.Empty);
                if (!hasType)
                    return result;

                MethodInfo method = studioType.GetMethod("GetSelectObjectCtrl", BindingFlags.Public | BindingFlags.Static);
                bool hasMethod = method != null;
                _log.Branch("selected-method-found", hasMethod, string.Empty);
                if (!hasMethod)
                    return result;

                Array array = method.Invoke(null, null) as Array;
                bool hasArray = array != null;
                _log.Branch("selected-array-found", hasArray, string.Empty);
                if (!hasArray)
                    return result;

                foreach (object item in array)
                {
                    if (item is OCIChar ociChar)
                    {
                        result.Add(ociChar);
                    }
                }

                _log.Info("selected chars count=" + result.Count);
                return result;
            }
            catch (Exception ex)
            {
                _log.Error("get selected chars failed", ex);
                return result;
            }
        }

        internal static ChaControl GetChaControl(OCIChar ociChar)
        {
            if (ociChar == null)
                return null;

            Type type = ociChar.GetType();
            PropertyInfo prop = type.GetProperty("charInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo field = type.GetField("charInfo", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object value = prop != null ? prop.GetValue(ociChar, null) : field?.GetValue(ociChar);
            return value as ChaControl;
        }

        private void ApplyPresetToCha(ChaControl cha, FacePreset preset)
        {
            _log.Entry(nameof(ApplyPresetToCha));
            bool invalid = cha == null || preset == null;
            _log.Branch("apply-cha-or-preset-null", invalid, string.Empty);
            if (invalid)
                return;

            cha.ChangeEyebrowPtn(preset.Eyebrow, true);
            cha.ChangeEyesPtn(preset.Eye, true);
            cha.ChangeMouthPtn(preset.Mouth, true);

            float eyeMax = Mathf.Clamp01(Mathf.Max(preset.EyeMin, preset.EyeMax));
            cha.ChangeEyesOpenMax(eyeMax);
            SetCtrlMinMax(cha, "eyesCtrl", Mathf.Clamp01(preset.EyeMin), eyeMax);

            float mouthMin = Mathf.Clamp01(preset.MouthMin);
            float mouthMax = Mathf.Clamp01(Mathf.Max(mouthMin, preset.MouthMax));
            cha.ChangeMouthOpenMax(mouthMax);
            SetCtrlMinMax(cha, "mouthCtrl", mouthMin, mouthMax);
            cha.mouthCtrl.FixedRate = mouthMin;

            cha.tearsLv = (byte)Mathf.Clamp(preset.Tears, 0, 10);
            cha.ChangeHohoAkaRate(Mathf.Clamp01(preset.Cheek));

            _log.Info("apply preset to cha done name=" + preset.Name);
        }

        private static void GetCtrlMinMax(ChaControl cha, string ctrlName, out float min, out float max)
        {
            min = 0f;
            max = 1f;
            if (cha == null)
                return;

            Type chaType = cha.GetType();
            FieldInfo ctrlField = chaType.GetField(ctrlName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object ctrl = ctrlField?.GetValue(cha);
            if (ctrl == null)
                return;

            Type ctrlType = ctrl.GetType();
            FieldInfo minField = ctrlType.GetField("OpenMin", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo maxField = ctrlType.GetField("OpenMax", BindingFlags.Instance | BindingFlags.Public);
            if (minField != null)
                min = Convert.ToSingle(minField.GetValue(ctrl));
            if (maxField != null)
                max = Convert.ToSingle(maxField.GetValue(ctrl));
        }

        private static void SetCtrlMinMax(ChaControl cha, string ctrlName, float min, float max)
        {
            if (cha == null)
                return;

            Type type = cha.GetType();
            PropertyInfo prop = type.GetProperty(ctrlName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldInfo field = type.GetField(ctrlName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object ctrl = prop != null ? prop.GetValue(cha, null) : field?.GetValue(cha);
            if (ctrl == null)
                return;

            Type ctrlType = ctrl.GetType();
            FieldInfo minField = ctrlType.GetField("OpenMin", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo maxField = ctrlType.GetField("OpenMax", BindingFlags.Instance | BindingFlags.Public);
            if (minField != null)
                minField.SetValue(ctrl, min);
            if (maxField != null)
                maxField.SetValue(ctrl, max);
        }
    }
}
