using System;
using System.Collections;
using System.IO;
using System.Threading;
using BepInEx.Configuration;
using MainGameCharacterMorphBridge;
using UnityEngine;

namespace MainGameVoiceBreastMorph
{
    public sealed partial class Plugin
    {
        private sealed class TattooChoice
        {
            internal ConfigEntry<string> Keyword, Face, Body;
            internal string FaceDraft, BodyDraft;
        }

        private readonly TattooChoice[] _tattooChoices = new TattooChoice[8];
        private ConfigEntry<string> _tattooHideKeyword;
        private ConfigEntry<float> _tattooFadeSeconds;
        private Coroutine _tattooFade;

        private void SetupTattooSettings()
        {
            const string section = "Tattoo overlay";
            _tattooKeyword = Config.Bind(section, "00 Keyword", "タトゥーを見せるね",
                new ConfigDescription("1番のタトゥーを表示する言葉。", null,
                    new ConfigurationManager.ConfigurationManagerAttributes { Order = 110 }));
            _tattooFacePath = Config.Bind(section, "01 Face PNG", "",
                new ConfigDescription("1番の顔PNG。", null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 100, HideSettingName = true, HideDefaultButton = true,
                        CustomDrawer = _ => DrawTattooPath("顔 PNG", _tattooFacePath, ref _tattooChoices[0].FaceDraft, "TattooFacePath1")
                    }));
            _tattooBodyPath = Config.Bind(section, "02 Body PNG", "",
                new ConfigDescription("1番の体PNG。", null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 90, HideSettingName = true, HideDefaultButton = true,
                        CustomDrawer = _ => DrawTattooPath("体 PNG", _tattooBodyPath, ref _tattooChoices[0].BodyDraft, "TattooBodyPath1")
                    }));
            _tattooChoices[0] = new TattooChoice { Keyword = _tattooKeyword, Face = _tattooFacePath, Body = _tattooBodyPath };
            for (int index = 1; index < _tattooChoices.Length; index++)
            {
                int slot = index;
                string group = "Tattoo " + (slot + 1);
                var choice = new TattooChoice();
                _tattooChoices[slot] = choice;
                choice.Keyword = Config.Bind(group, "00 Keyword", "",
                    new ConfigDescription("このタトゥーを表示する言葉。空欄なら無効。"));
                choice.Face = Config.Bind(group, "01 Face PNG", "",
                    new ConfigDescription("顔PNG。", null,
                        new ConfigurationManager.ConfigurationManagerAttributes
                        {
                            HideSettingName = true, HideDefaultButton = true,
                            CustomDrawer = _ => DrawTattooPath("顔 PNG", choice.Face, ref choice.FaceDraft, "TattooFacePath" + (slot + 1))
                        }));
                choice.Body = Config.Bind(group, "02 Body PNG", "",
                    new ConfigDescription("体PNG。", null,
                        new ConfigurationManager.ConfigurationManagerAttributes
                        {
                            HideSettingName = true, HideDefaultButton = true,
                            CustomDrawer = _ => DrawTattooPath("体 PNG", choice.Body, ref choice.BodyDraft, "TattooBodyPath" + (slot + 1))
                        }));
            }
            _tattooHideKeyword = Config.Bind(section, "05 Hide Keyword", "タトゥー消すね",
                new ConfigDescription("発光中に顔・体のタトゥーを消す言葉。"));
            _tattooFadeSeconds = Config.Bind(section, "03 Fade Seconds", 10f,
                new ConfigDescription("変身音・発光の演出秒数。中間点でPNGを一度だけ変更。0なら即時。", new AcceptableValueRange<float>(0f, 60f)));
        }

        private void DrawTattooPath(string label, ConfigEntry<string> entry, ref string draft, string controlName)
        {
            if (draft == null) draft = entry.Value ?? "";
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(52f));
            GUI.SetNextControlName(controlName);
            draft = GUILayout.TextField(draft, GUILayout.Width(300f));
            if (GUILayout.Button("参照", GUILayout.Width(54f)))
            {
                string selected = BrowsePng(draft);
                if (!string.IsNullOrEmpty(selected)) { draft = selected; entry.Value = selected; }
            }
            GUILayout.EndHorizontal();
            if (GUI.GetNameOfFocusedControl() != controlName && draft != (entry.Value ?? "")) entry.Value = draft;
        }

        private string BrowsePng(string current)
        {
            string selected = null;
            Exception error = null;
            var thread = new Thread(() =>
            {
                try { selected = NativePngFileDialog.Show(current); }
                catch (Exception ex) { error = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            if (error != null) Logger.LogWarning("PNG browser failed: " + error);
            return selected;
        }

        private void ScheduleTattooTrigger(string text, Func<int, string, Action, float> schedule)
        {
            for (int slot = 0; slot < _tattooChoices.Length; slot++)
            {
                string keyword = (_tattooChoices[slot].Keyword.Value ?? "").Trim();
                if (keyword.Length == 0) continue;
                int chosen = slot;
                ScheduleOccurrences(text, keyword, "tattoo_show_" + slot, () => ShowTattoo(chosen), schedule);
            }
            string hide = (_tattooHideKeyword.Value ?? "").Trim();
            if (hide.Length > 0) ScheduleOccurrences(text, hide, "tattoo_hide", HideTattoo, schedule);
        }

        private static void ScheduleOccurrences(string text, string keyword, string id, Action action,
            Func<int, string, Action, float> schedule)
        {
            int search = 0;
            while (search < text.Length)
            {
                int index = text.IndexOf(keyword, search, StringComparison.Ordinal);
                if (index < 0) break;
                schedule(index, id, action);
                search = index + keyword.Length;
            }
        }

        private void ShowTattoo(int slot)
        {
            if (!_enabled.Value) return;
            var choice = _tattooChoices[slot];
            string face = ResolveCurrentPath(choice.Face, choice.FaceDraft);
            string body = ResolveCurrentPath(choice.Body, choice.BodyDraft);
            if (face.Length == 0 && body.Length == 0) { Logger.LogWarning("Tattoo " + (slot + 1) + " has no PNG paths"); return; }
            if ((face.Length > 0 && !File.Exists(face)) || (body.Length > 0 && !File.Exists(body)))
            {
                Logger.LogWarning("Tattoo PNG file not found: face=[" + face + "] body=[" + body + "]");
                return;
            }
            if (!CharacterMorphBridgeApi.TryGetTargetIdentity(out int identity, _femaleIndex.Value))
            { Logger.LogWarning("Tattoo target unavailable"); return; }
            CancelTattooFade();
            BeginTattooEffect(face, body, false, identity);
        }

        private void HideTattoo()
        {
            if (!_enabled.Value || !CharacterMorphBridgeApi.TryGetTargetIdentity(out int identity, _femaleIndex.Value)) return;
            CancelTattooFade();
            BeginTattooEffect(null, null, true, identity);
        }

        private void BeginTattooEffect(string face, string body, bool remove, int identity)
        {
            float seconds = Mathf.Clamp(_tattooFadeSeconds.Value, 0f, 60f);
            if (seconds <= 0f)
            {
                CharacterMorphBridgeApi.BeginTattooEffect(_femaleIndex.Value);
                try { ApplyTattooChange(face, body, remove, identity); }
                finally { CharacterMorphBridgeApi.EndTattooEffect(); }
                return;
            }
            _tattooFade = StartCoroutine(TattooEffect(face, body, remove, identity, seconds));
        }

        private IEnumerator TattooEffect(string face, string body, bool remove, int identity, float seconds)
        {
            CharacterMorphBridgeApi.BeginTattooEffect(_femaleIndex.Value);
            bool applied = false;
            try
            {
                float elapsed = 0f;
                while (elapsed < seconds)
                {
                    if (!applied && elapsed >= seconds * 0.5f)
                    {
                        ApplyTattooChange(face, body, remove, identity);
                        applied = true;
                    }
                    CharacterMorphBridgeApi.UpdateTattooEffect(elapsed, seconds);
                    yield return null;
                    elapsed = Mathf.Min(seconds, elapsed + Time.unscaledDeltaTime);
                }
                if (!applied) ApplyTattooChange(face, body, remove, identity);
            }
            finally
            {
                CharacterMorphBridgeApi.EndTattooEffect();
                _tattooFade = null;
            }
        }

        private void ApplyTattooChange(string face, string body, bool remove, int identity)
        {
            if (!CharacterMorphBridgeApi.TryGetTargetIdentity(out int current, _femaleIndex.Value) || current != identity)
            { Logger.LogWarning("Tattoo change skipped: target changed during effect"); return; }
            bool success = remove
                ? CharacterMorphBridgeApi.ClearTattooOverlays(true, true, _femaleIndex.Value)
                : CharacterMorphBridgeApi.ApplyTattooOverlays(face, body, _femaleIndex.Value);
            if (!remove && success && (face.Length == 0 || body.Length == 0))
                success = CharacterMorphBridgeApi.ClearTattooOverlays(face.Length == 0, body.Length == 0, _femaleIndex.Value);
            if (!success) Logger.LogWarning("Tattoo change failed");
        }

        private void CancelTattooFade()
        {
            if (_tattooFade != null) { StopCoroutine(_tattooFade); _tattooFade = null; }
            CharacterMorphBridgeApi.EndTattooEffect();
        }

        private static string ResolveCurrentPath(ConfigEntry<string> entry, string draft)
        {
            string saved = (entry.Value ?? "").Trim().Trim('"');
            string visible = (draft ?? "").Trim().Trim('"');
            if (visible.Length > 0 && visible != saved && File.Exists(visible)) { entry.Value = visible; return visible; }
            return saved;
        }
    }
}
