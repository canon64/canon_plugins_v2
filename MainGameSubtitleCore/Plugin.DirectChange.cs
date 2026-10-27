using BepInEx.Configuration;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;

namespace MainGameSubtitleCore
{
    internal sealed partial class Plugin
    {
        private const int DirectChangeProtocol = 2;
        private readonly Dictionary<string, ConfigEntry<string>> _directChangeRules =
            new Dictionary<string, ConfigEntry<string>>();
        private bool _directChangeRulesDirty = true;

        private sealed class DirectChangeConfigAttributes
        {
            public int Order;
            public bool IsAdvanced = false;
        }

        [DataContract]
        private sealed class DirectChangeAck
        {
            [DataMember] public bool ok { get; set; }
            [DataMember] public bool enabled { get; set; }
            [DataMember] public int protocol { get; set; }
            [DataMember] public bool rules_applied { get; set; }
        }

        private void BindDirectChangeConfig()
        {
            BindDirectRule("morph", "01 変身の語尾", "の姿になれ,の姿になって", "カード名の後に付ける言葉。例：色黒ギャルの姿になれ。カードはVoiceFaceEventBridgeの登録を使います。");
            BindDirectRule("reset", "02 元の姿に戻す", "元の姿に戻れ,元に戻れ", "全文が一致すると元の姿に戻します。");
            BindDirectRule("coord", "03 衣装変更の語尾", "に着替えろ,に着替えて", "衣装名の後に付ける言葉。ランダムに着替えろで現在と異なる衣装を選びます。");
            BindDirectRule("remove", "04 脱衣の語尾", "を脱げ,を脱いで,を外せ,を外して,を取れ,を取って", "服・部位名の後に付ける言葉。");
            BindDirectRule("put_on", "05 着用の語尾", "を着ろ,を着て,を付けろ,を付けて,を履け,を履いて", "服・部位名の後に付ける言葉。");
            BindDirectRule("shift", "06 ずらしの語尾", "をずらせ,をずらして,をめくれ,をめくって", "服・部位名の後に付ける言葉。");
            BindDirectRule("shift_second", "07 二段目ずらしの語尾", "を下げろ,を下げて", "服・部位名の後に付ける言葉。");
            BindDirectRule("remove_all", "08 全脱衣", "全裸になれ,全部脱げ", "全文が一致すると全脱衣します。");
            BindDirectRule("put_on_all", "09 全着用", "全部着ろ,全部着て", "全文が一致すると全着用します。");
            BindDirectRule("named_pose", "10 登録ポーズの語尾", "のポーズになれ,のポーズにして", "登録ポーズ名の後に付ける言葉。");
            BindDirectRule("pose", "11 体位の語尾", "になれ,になって", "体位名の後に付ける言葉。より長い語尾の設定を優先します。");
            BindDirectRule("camera", "12 カメラの語尾", "のカメラにしろ,のカメラにして,の視点にしろ,の視点にして,のアングルにしろ,で見せろ,を見せろ", "カメラプリセット名の後に付ける言葉。メイン・サブカメラ共通です。");
            BindDirectRule("random_dance", "21 ランダムダンス", "ランダムに踊れ", "全文が一致すると登録済みVMDプリセットからランダムに1曲再生します。");
            BindDirectRule("dance", "20 ダンスの語尾", "を踊って", "VMDプリセット名の後に付ける言葉。例：曲名を踊って。");
            BindDirectRule("video", "13 動画の語尾", "を流せ,を流して", "動画名の後に付ける言葉。");
            BindDirectRule("astral", "14 アストラル移行", "アストラルに入れ", "全文が一致するとアストラルへ移行します。");
            BindDirectRule("real_world", "15 現実に戻す", "現実に戻れ", "全文が一致すると現実に戻します。");
            BindDirectRule("custom", "16 自由登録", "", "入力する言葉=>VoiceFaceEventBridgeの既存トリガー の形式。複数は ; 区切り。全文一致で最優先します。例：ギャルになれ=>色黒ギャルの姿になるね;戻せ=>元の姿に戻るね");
            BindDirectRule("breast_large", "17 巨乳に変更", "巨乳になれ,巨乳になって,巨乳にして", "全文が一致すると胸サイズを巨乳側の設定値へ変更します。体位の語尾設定とは独立しています。");
            BindDirectRule("breast_small", "18 ペチャパイに変更", "ペチャパイになれ,ペチャパイになって,ペチャパイにして", "全文が一致すると胸サイズをペチャパイ側の設定値へ変更します。体位の語尾設定とは独立しています。");
            BindDirectRule("breast_reset", "19 元のおっぱいに戻す", "元のおっぱいに戻れ,もとのおっぱいに戻れ,元のおっぱいに戻して,もとのおっぱいに戻して,元のおっぱいに戻る,もとのおっぱいに戻る,元の胸に戻れ,元の胸に戻して,元のおっぱいになれ,もとのおっぱいになれ", "全文が一致すると、胸変更前に記録された元の胸サイズへ戻します。体位の語尾設定とは独立しています。");
        }

        private void BindDirectRule(string id, string label, string defaults, string description)
        {
            var entry = Config.Bind("直接変更 トリガー", label, defaults,
                new ConfigDescription(description + (id == "custom" ? "" : " 複数はカンマ区切り。空欄でこの入力命令を無効化。")
                    + " 直接変更ON中、マイクと字幕入力に使用。変更はHuman_2_kksへ自動反映します。",
                    null, new DirectChangeConfigAttributes { Order = 100 - _directChangeRules.Count }));
            _directChangeRules.Add(id, entry);
            entry.SettingChanged += (_, __) =>
            {
                _directChangeRulesDirty = true;
                _nextDirectChangeRulesRetry = Time.unscaledTime + 0.5f;
                UpdateDirectChangeVisual();
            };
        }

        private string BuildDirectChangeStateJson(bool enabled)
        {
            // Mode changes must never depend on validation of trigger words.
            return "{\"protocol\":" + DirectChangeProtocol + ",\"enabled\":" + (enabled ? "true" : "false") + "}";
        }

        private string BuildDirectChangeRulesJson()
        {
            var json = new StringBuilder("{\"protocol\":" + DirectChangeProtocol
                + ",\"rules\":{");
            bool first = true;
            foreach (var pair in _directChangeRules)
            {
                if (!first) json.Append(',');
                first = false;
                json.Append('"').Append(pair.Key).Append("\":\"")
                    .Append(EscapeJsonString(pair.Value.Value ?? "")).Append('"');
            }
            return json.Append("}}").ToString();
        }

        private static bool ReadDirectChangeAck(Stream stream, bool enabled)
        {
            var serializer = new DataContractJsonSerializer(typeof(DirectChangeAck));
            var ack = serializer.ReadObject(stream) as DirectChangeAck;
            return ack != null && ack.ok && ack.enabled == enabled && ack.protocol == DirectChangeProtocol;
        }
    }
}
