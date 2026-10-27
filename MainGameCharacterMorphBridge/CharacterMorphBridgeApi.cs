using UnityEngine;

namespace MainGameCharacterMorphBridge
{
    public static class CharacterMorphBridgeApi
    {
        public static bool IsAvailable => Plugin.Instance != null;

        public static bool CaptureOriginal(int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.CaptureOriginal(femaleIndex, "api");
        }

        public static bool LoadTargetCard(string cardPath, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.LoadTargetCard(cardPath, femaleIndex, "api");
        }

        public static bool LoadTargetCardByWord(string word, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.LoadTargetCardByWord(word, femaleIndex, "api");
        }

        public static bool RegisterCard(string word, string cardPath, string triggerWords = "")
        {
            return Plugin.Instance != null && Plugin.Instance.RegisterCard(word, cardPath, triggerWords, "api");
        }

        public static string[] GetRegisteredCardWords()
        {
            return Plugin.Instance != null ? Plugin.Instance.GetRegisteredCardWords() : new string[0];
        }

        public static bool SetBlend(float blend, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.ApplyBlend(blend, femaleIndex, "api");
        }

        public static bool BlendTo(float targetBlend, float seconds, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.BlendTo(targetBlend, seconds, femaleIndex, "api");
        }

        public static bool BlendToCardWord(string word, float targetBlend, float seconds, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.BlendToCardWord(word, targetBlend, seconds, femaleIndex, "api");
        }

        public static bool BlendToCardPath(string cardPath, float targetBlend, float seconds, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.BlendToCardPath(cardPath, targetBlend, seconds, femaleIndex, "api");
        }

        public static bool TryResolveRegisteredCardFromText(string text, out string word, out string cardPath)
        {
            word = string.Empty;
            cardPath = string.Empty;
            return Plugin.Instance != null && Plugin.Instance.TryResolveRegisteredCardFromText(text, out word, out cardPath);
        }

        public static bool ScheduleReapplyCurrentMorphState(string reason = "api", int femaleIndex = -1)
        {
            if (Plugin.Instance == null)
                return false;

            Plugin.Instance.ScheduleExternalReapplyCurrentMorphState(reason, femaleIndex);
            return true;
        }

        public static bool ScheduleClothesChangeReapply(string reason = "api clothes", int femaleIndex = -1)
        {
            if (Plugin.Instance == null)
                return false;

            Plugin.Instance.ScheduleClothesChangeReapply(reason, femaleIndex);
            return true;
        }

        public static bool SetBodyShape(int shapeIndex, float value, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.SetBodyShape(shapeIndex, value, femaleIndex, "api");
        }

        public static bool SetFaceShape(int shapeIndex, float value, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.SetFaceShape(shapeIndex, value, femaleIndex, "api");
        }

        public static bool SetHeight(float value, int femaleIndex = -1)
        {
            return SetBodyShape(0, value, femaleIndex);
        }

        public static bool SetBreast(float value, int femaleIndex = -1)
        {
            return SetBodyShape(4, value, femaleIndex);
        }

        public static bool TryGetBreast(out float value, int femaleIndex = -1)
        {
            value = 0f;
            return Plugin.Instance != null && Plugin.Instance.TryGetBodyShape(4, femaleIndex, out value);
        }

        public static bool TryGetSkin(out Color color, out int skinId, int femaleIndex = -1)
        {
            color = Color.white;
            skinId = 0;
            return Plugin.Instance != null && Plugin.Instance.TryGetSkin(color: out color, skinId: out skinId, femaleIndex: femaleIndex);
        }

        public static bool TryGetSkin(out Color main, out Color sub, out int skinId, int femaleIndex = -1)
        {
            main = Color.white;
            sub = Color.white;
            skinId = 0;
            return Plugin.Instance != null && Plugin.Instance.TryGetSkin(out main, out sub, out skinId, femaleIndex);
        }

        public static bool TryGetTargetIdentity(out int identity, int femaleIndex = -1)
        {
            identity = 0;
            return Plugin.Instance != null && Plugin.Instance.TryGetTargetIdentity(femaleIndex, out identity);
        }

        public static bool SetSkin(Color color, int skinId, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.SetSkin(color, skinId, femaleIndex);
        }

        public static bool SetSkin(Color main, Color sub, int skinId, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.SetSkin(main, sub, skinId, femaleIndex);
        }

        public static bool ApplyTattooOverlays(string facePngPath, string bodyPngPath, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.ApplyTattooOverlays(facePngPath, bodyPngPath, femaleIndex);
        }

        public static bool ApplyTattooOverlayBytes(byte[] facePng, byte[] bodyPng, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.ApplyTattooOverlayBytes(facePng, bodyPng, femaleIndex);
        }

        public static bool ClearTattooOverlays(bool face, bool body, int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.ClearTattooOverlays(face, body, femaleIndex);
        }

        public static bool TryGetTattooOverlays(out byte[] face, out byte[] body, int femaleIndex = -1)
        {
            face = null;
            body = null;
            return Plugin.Instance != null && Plugin.Instance.TryGetTattooOverlays(out face, out body, femaleIndex);
        }

        public static bool BeginTattooEffect(int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.BeginTattooEffect(femaleIndex);
        }

        public static void UpdateTattooEffect(float elapsed, float duration)
        {
            Plugin.Instance?.UpdateTattooEffect(elapsed, duration);
        }

        public static void EndTattooEffect()
        {
            Plugin.Instance?.EndTattooEffect();
        }

        public static bool ResetToOriginal(int femaleIndex = -1)
        {
            return Plugin.Instance != null && Plugin.Instance.ResetToOriginal(femaleIndex, "api");
        }
    }
}
