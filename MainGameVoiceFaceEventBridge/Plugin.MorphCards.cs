using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace MainGameVoiceFaceEventBridge
{
    /// <summary>
    /// 合言葉とキャラカードの対応表。
    ///
    /// 「子供の姿になるね」と喋ったら子供のカードへ、というように、合言葉そのものが変身の引き金になる。
    /// 汎用の変身キーワード（既定「変身するね」）を別に言う必要はない。
    ///
    /// 保存形式は "合言葉=カードのフルパス" の1行1件。同じカードに複数の言い方を持たせたい時は
    /// パスが同じ行を並べる。
    /// </summary>
    internal sealed partial class Plugin
    {
        private string _morphCardMessage = string.Empty;

        internal sealed class MorphCardEntry
        {
            internal string Keyword = string.Empty;
            internal string CardPath = string.Empty;
        }

        private List<MorphCardEntry> GetMorphCards()
        {
            var list = new List<MorphCardEntry>();
            string[] lines = Settings != null ? Settings.MorphBridgeCards : null;
            if (lines == null)
                return list;

            foreach (string line in lines)
            {
                MorphCardEntry entry = ParseMorphCardLine(line);
                if (entry != null)
                    list.Add(entry);
            }

            return list;
        }

        private static MorphCardEntry ParseMorphCardLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            // パスにも "=" が入りうるので、最初の1つだけで区切る。
            int sep = line.IndexOf('=');
            if (sep <= 0 || sep >= line.Length - 1)
                return null;

            string keyword = line.Substring(0, sep).Trim();
            string path = line.Substring(sep + 1).Trim().Trim('"');
            if (keyword.Length <= 0 || path.Length <= 0)
                return null;

            return new MorphCardEntry { Keyword = keyword, CardPath = path };
        }

        private void SaveMorphCards(List<MorphCardEntry> cards)
        {
            if (Settings == null)
                return;

            var lines = new List<string>();
            foreach (MorphCardEntry card in cards)
            {
                if (card == null || string.IsNullOrWhiteSpace(card.Keyword) || string.IsNullOrWhiteSpace(card.CardPath))
                    continue;
                lines.Add(card.Keyword.Trim() + "=" + card.CardPath.Trim());
            }

            Settings.MorphBridgeCards = lines.ToArray();
            SaveSettingsToConfigJson("morph cards");
        }

        /// <summary>
        /// 発話から合言葉を探す。複数当たった場合は文中でより前にあるもの、
        /// 同じ位置なら長い合言葉を優先する。短い語の巻き添えを避けるため。
        /// </summary>
        private MorphCardEntry FindMorphCardInText(string text, out int matchIndex)
        {
            matchIndex = -1;
            if (string.IsNullOrWhiteSpace(text))
                return null;

            MorphCardEntry best = null;
            int bestIndex = int.MaxValue;
            int bestLength = 0;

            foreach (MorphCardEntry card in GetMorphCards())
            {
                int index = text.IndexOf(card.Keyword, StringComparison.Ordinal);
                if (index < 0)
                    continue;

                if (index < bestIndex || (index == bestIndex && card.Keyword.Length > bestLength))
                {
                    best = card;
                    bestIndex = index;
                    bestLength = card.Keyword.Length;
                }
            }

            if (best != null)
                matchIndex = bestIndex;

            return best;
        }

        // ---------------- ConfigManager 表示 ----------------

        /// <summary>
        /// 一覧と入力欄は普段畳んでおく。開閉ボタンはポーズ設定と同じ作り。
        /// </summary>
        private void SetupMorphCardEntries()
        {
            _cfgMorphCardsExpanded = Config.Bind(
                "CharacterMorph",
                "【表示】変身カード一覧",
                false,
                BuildPoseControlToggleButtonDescription(
                    openLabel: "変身カード設定を開く",
                    closeLabel: "変身カード設定を閉じる",
                    order: 100,
                    readOnly: false));
            _morphCardsExpanded = _cfgMorphCardsExpanded.Value;
            HookManagedConfigEntry(
                _cfgMorphCardsExpanded,
                "morph-card-expand-toggle",
                null,
                value =>
                {
                    _morphCardsExpanded = value;
                    UpdateMorphCardEntriesVisibility();
                    return true;
                },
                saveValue: null,
                refreshConfigManager: true);

            _cfgMorphCardList = Config.Bind(
                "CharacterMorph",
                "Cards",
                false,
                BuildMorphCardListDescription(90));

            _cfgMorphCardKeyword = Config.Bind(
                "CharacterMorph",
                "AddKeyword",
                string.Empty,
                BuildMorphCardChildDescription("合言葉。この言葉を喋るとそのカードへ変身します。例: 子供の姿になるね。", 80));

            _cfgMorphCardPath = Config.Bind(
                "CharacterMorph",
                "AddCardPath",
                string.Empty,
                BuildMorphCardChildDescription("そのカードPNGのフルパス。", 70));

            _cfgMorphCardUpsert = Config.Bind(
                "CharacterMorph",
                "AddOrUpdate",
                false,
                BuildMorphCardButtonDescription("この内容で追加/更新", 60));

            UpdateMorphCardEntriesVisibility();
        }

        private ConfigDescription BuildMorphCardListDescription(int order)
        {
            var attrs = new ConfigurationManager.ConfigurationManagerAttributes
            {
                Order = order,
                HideSettingName = true,
                HideDefaultButton = true,
                Browsable = false
            };
            attrs.CustomDrawer = DrawMorphCardList;
            return new ConfigDescription(string.Empty, null, attrs);
        }

        private ConfigDescription BuildMorphCardChildDescription(string description, int order)
        {
            return new ConfigDescription(
                description,
                null,
                new ConfigurationManager.ConfigurationManagerAttributes
                {
                    Order = order,
                    Browsable = false
                });
        }

        private ConfigDescription BuildMorphCardButtonDescription(string label, int order)
        {
            var attrs = new ConfigurationManager.ConfigurationManagerAttributes
            {
                Order = order,
                HideSettingName = true,
                HideDefaultButton = true,
                Browsable = false
            };
            attrs.CustomDrawer = _ =>
            {
                if (GUILayout.Button(label, GUILayout.ExpandWidth(true)))
                    UpsertMorphCardFromInputs();
            };
            return new ConfigDescription(string.Empty, null, attrs);
        }

        /// <summary>
        /// 開閉に合わせて、一覧と入力欄の表示を切り替える。
        /// ポーズ設定の「カテゴリ一覧を開く」と同じやり方（子項目の Browsable を切り替えて再描画）。
        /// </summary>
        private void UpdateMorphCardEntriesVisibility()
        {
            bool show = _morphCardsExpanded;
            SetMorphCardEntryBrowsable(_cfgMorphCardList, show);
            SetMorphCardEntryBrowsable(_cfgMorphCardKeyword, show);
            SetMorphCardEntryBrowsable(_cfgMorphCardPath, show);
            SetMorphCardEntryBrowsable(_cfgMorphCardUpsert, show);
        }

        private static void SetMorphCardEntryBrowsable(ConfigEntryBase entry, bool browsable)
        {
            if (entry == null || entry.Description == null || entry.Description.Tags == null)
                return;

            foreach (object tag in entry.Description.Tags)
            {
                var attr = tag as ConfigurationManager.ConfigurationManagerAttributes;
                if (attr != null)
                    attr.Browsable = browsable;
            }
        }

        private void DrawMorphCardList(ConfigEntryBase entry)
        {
            List<MorphCardEntry> cards = GetMorphCards();

            GUILayout.BeginVertical();

            if (cards.Count <= 0)
            {
                GUILayout.Label("登録なし。下の欄に合言葉とカードのパスを入れて［追加/更新］を押してください。");
            }
            else
            {
                for (int i = 0; i < cards.Count; i++)
                {
                    MorphCardEntry card = cards[i];
                    bool exists = FileExistsSafe(card.CardPath);

                    GUILayout.BeginHorizontal();
                    GUILayout.Label(
                        (exists ? "" : "× ") + card.Keyword + "  →  " + FileNameSafe(card.CardPath),
                        GUILayout.ExpandWidth(true));

                    if (GUILayout.Button("編集", GUILayout.Width(60f)))
                        EditMorphCard(card);
                    if (GUILayout.Button("削除", GUILayout.Width(60f)))
                        RemoveMorphCard(card.Keyword);

                    GUILayout.EndHorizontal();
                }

                GUILayout.Label("× はカードのファイルが見つからない行です。");
            }

            if (!string.IsNullOrEmpty(_morphCardMessage))
                GUILayout.Label(_morphCardMessage);

            GUILayout.EndVertical();
        }

        private static bool FileExistsSafe(string path)
        {
            try
            {
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static string FileNameSafe(string path)
        {
            try
            {
                string name = Path.GetFileName(path);
                return string.IsNullOrEmpty(name) ? "(パスなし)" : name;
            }
            catch
            {
                return "(パス不正)";
            }
        }

        private void EditMorphCard(MorphCardEntry card)
        {
            if (card == null)
                return;

            if (_cfgMorphCardKeyword != null)
                _cfgMorphCardKeyword.Value = card.Keyword;
            if (_cfgMorphCardPath != null)
                _cfgMorphCardPath.Value = card.CardPath;

            _morphCardMessage = "「" + card.Keyword + "」を下の欄へ読み込みました";
        }

        private void RemoveMorphCard(string keyword)
        {
            List<MorphCardEntry> cards = GetMorphCards();
            int removed = cards.RemoveAll(x => x != null && string.Equals(x.Keyword, keyword, StringComparison.Ordinal));
            if (removed <= 0)
            {
                _morphCardMessage = "見つかりません: " + keyword;
                return;
            }

            SaveMorphCards(cards);
            _morphCardMessage = "「" + keyword + "」を削除しました";
            Log("[morph] card removed keyword=" + keyword);
        }

        /// <summary>合言葉が同じなら上書き、違えば追加。</summary>
        private void UpsertMorphCardFromInputs()
        {
            string keyword = (_cfgMorphCardKeyword != null ? _cfgMorphCardKeyword.Value : string.Empty) ?? string.Empty;
            string path = (_cfgMorphCardPath != null ? _cfgMorphCardPath.Value : string.Empty) ?? string.Empty;

            keyword = keyword.Trim();
            path = path.Trim().Trim('"');

            if (keyword.Length <= 0)
            {
                _morphCardMessage = "追加できません: 合言葉が空です";
                return;
            }

            if (path.Length <= 0)
            {
                _morphCardMessage = "追加できません: パスが空です";
                return;
            }

            if (!FileExistsSafe(path))
            {
                _morphCardMessage = "追加できません: ファイルが見つかりません " + path;
                return;
            }

            List<MorphCardEntry> cards = GetMorphCards();
            MorphCardEntry existing = cards.Find(x => x != null && string.Equals(x.Keyword, keyword, StringComparison.Ordinal));
            bool updated = existing != null;

            if (updated)
                existing.CardPath = path;
            else
                cards.Add(new MorphCardEntry { Keyword = keyword, CardPath = path });

            SaveMorphCards(cards);
            _morphCardMessage = updated
                ? "「" + keyword + "」を更新しました"
                : "「" + keyword + "」を追加しました";
            Log("[morph] card upsert keyword=" + keyword + " path=" + path + " updated=" + updated);
        }
    }
}
