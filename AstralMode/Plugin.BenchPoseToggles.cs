using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;

namespace AstralMode
{
    /// <summary>
    /// 体位ごとの「この体位ではベンチを出す」チェックボックス。
    ///
    /// 体位の一覧はHシーンが始まるまで分からない（相手やシーンで変わる）ため、
    /// Config へは起動時ではなく、シーンから一覧を拾えた時点で登録する。
    /// ConfigManager は窓を開くたびに項目を作り直すので、一度Hに入れば以降は
    /// チェックボックスとして並ぶ。登録した項目は cfg ファイルに残る。
    ///
    /// 既定値はベンチフェラ（AstralPoseNameKeyword）だけON、他は全部OFF。
    /// </summary>
    public sealed partial class Plugin
    {
        private const string BenchPoseCategory = "73. Astral Bench Poses";

        // BepInEx が ConfigDefinition のキーに許さない文字。
        private static readonly char[] ConfigKeyInvalidChars = { '=', '\n', '\t', '\\', '"', '\'', '[', ']' };

        private readonly Dictionary<string, ConfigEntry<bool>> _benchPoseAllow =
            new Dictionary<string, ConfigEntry<bool>>(StringComparer.OrdinalIgnoreCase);

        // 同じHシーンで何度も一覧を舐めないための番人。
        private HSceneProc _benchPoseToggleSource;

        /// <summary>Hシーンの体位一覧からチェックボックスを作る。同じシーンでは一度だけ走る。</summary>
        internal void EnsureBenchPoseToggles()
        {
            HSceneProc proc = _hSceneProc;
            if (proc == null || ReferenceEquals(proc, _benchPoseToggleSource)) return;

            try
            {
                var lists = SnapLstUseAnimInfoField != null
                    ? SnapLstUseAnimInfoField.GetValue(proc) as List<HSceneProc.AnimationListInfo>[]
                    : null;
                if (lists == null)
                {
                    // 一覧がまだ入っていないだけかもしれないので、次のフレームで作り直せるよう
                    // 番人は立てずに戻る。
                    return;
                }

                _benchPoseToggleSource = proc;

                string keyword = (AstralPoseKeyword ?? DefaultAstralPoseKeyword).Trim();
                if (keyword.Length == 0) keyword = DefaultAstralPoseKeyword;

                int added = 0;
                for (int i = 0; i < lists.Length; i++)
                {
                    var list = lists[i];
                    if (list == null) continue;

                    for (int j = 0; j < list.Count; j++)
                    {
                        var c = list[j];
                        if (c == null) continue;

                        string name = c.nameAnimation;
                        if (string.IsNullOrEmpty(name)) continue;

                        string key = SanitizeConfigKey(name);
                        if (key.Length == 0 || _benchPoseAllow.ContainsKey(key)) continue;

                        bool isBenchPose = key.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;

                        // ベンチフェラを上に出したいので order で並びを分ける。
                        _benchPoseAllow[key] = Bind(BenchPoseCategory, key, isBenchPose,
                            $"「{name}」のあいだベンチを出す。Bench Hide Off Pose がオンの時だけ効く。",
                            order: isBenchPose ? 100 : 0);
                        added++;
                    }
                }

                if (added > 0)
                    SimpleFileLogger.Info($"Bench pose toggles registered: added={added}, total={_benchPoseAllow.Count}, keyword='{keyword}'");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"EnsureBenchPoseToggles failed: {ex.Message}");
            }
        }

        /// <summary>その体位のチェックボックスを引く。まだ登録されていなければ false を返す。</summary>
        private bool TryGetBenchPoseAllow(string poseName, out bool allowed)
        {
            allowed = false;
            if (string.IsNullOrEmpty(poseName)) return false;

            ConfigEntry<bool> entry;
            if (!_benchPoseAllow.TryGetValue(SanitizeConfigKey(poseName), out entry) || entry == null)
                return false;

            allowed = entry.Value;
            return true;
        }

        private static string SanitizeConfigKey(string name)
        {
            string trimmed = (name ?? string.Empty).Trim();
            if (trimmed.Length == 0) return string.Empty;
            if (trimmed.IndexOfAny(ConfigKeyInvalidChars) < 0) return trimmed;

            StringBuilder sb = new StringBuilder(trimmed.Length);
            for (int i = 0; i < trimmed.Length; i++)
            {
                char ch = trimmed[i];
                sb.Append(Array.IndexOf(ConfigKeyInvalidChars, ch) >= 0 ? '_' : ch);
            }
            return sb.ToString().Trim();
        }
    }
}
