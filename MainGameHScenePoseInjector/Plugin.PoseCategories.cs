using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace MainGameHScenePoseInjector
{
    [Serializable]
    internal sealed class PoseCategoryDictionaryFile
    {
        public int version = 1;
        public List<PoseCategoryEntry> poses = new List<PoseCategoryEntry>();
    }

    [Serializable]
    internal sealed class PoseCategoryEntry
    {
        public int id;
        public string name;
        public string category;
    }

    public sealed partial class Plugin
    {
        private const string PoseCategoryDictionaryFileName = "PoseCategoryDictionary.json";
        private static readonly HashSet<string> ValidPoseCategories = new HashSet<string>
        {
            "立ち", "座り", "中腰", "寝", "未分類"
        };

        private void EnsurePoseCategoryDictionary(List<PoseEntry> poses)
        {
            if (poses == null || poses.Count == 0)
                return;

            string path = Path.Combine(PluginDir, PoseCategoryDictionaryFileName);
            try
            {
                PoseCategoryDictionaryFile dictionary = LoadPoseCategoryDictionary(path);
                Dictionary<int, PoseCategoryEntry> existing = dictionary.poses
                    .Where(entry => entry != null)
                    .GroupBy(entry => entry.id)
                    .ToDictionary(group => group.Key, group => group.First());

                List<PoseCategoryEntry> merged = new List<PoseCategoryEntry>();
                foreach (PoseEntry pose in poses.OrderBy(pose => pose.Id))
                {
                    PoseCategoryEntry entry;
                    if (!existing.TryGetValue(pose.Id, out entry))
                    {
                        entry = new PoseCategoryEntry
                        {
                            id = pose.Id,
                            name = pose.Name,
                            category = InferPoseCategory(pose.Name)
                        };
                    }
                    else
                    {
                        entry.name = pose.Name;
                        if (!ValidPoseCategories.Contains(entry.category ?? string.Empty))
                            entry.category = "未分類";
                    }
                    merged.Add(entry);
                }

                dictionary.version = 1;
                dictionary.poses = merged;

                // JsonUtility 経由だと poses が書き出されず、辞書が
                // {"version":1} だけになってしまう事故が起きていた。
                // 全ポーズが「未分類」に落ちてカテゴリ絞り込みが対象0件になるため、
                // ここは取りこぼしの無い手書きの出力にする。
                File.WriteAllText(path, BuildPoseCategoryJson(dictionary), new UTF8Encoding(false));
                LogInfo("pose category dictionary saved count=" + merged.Count + " path=" + path);
            }
            catch (Exception ex)
            {
                LogWarn("pose category dictionary failed: " + ex.Message);
            }
        }

        private static string BuildPoseCategoryJson(PoseCategoryDictionaryFile dictionary)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  " + Quote("version") + ": " + dictionary.version + ",");
            sb.AppendLine("  " + Quote("poses") + ": [");

            for (int i = 0; i < dictionary.poses.Count; i++)
            {
                PoseCategoryEntry e = dictionary.poses[i];
                if (e == null) continue;
                string line = "    {" + Quote("id") + ": " + e.id
                    + ", " + Quote("name") + ": " + Quote(EscapeJson(e.name))
                    + ", " + Quote("category") + ": " + Quote(EscapeJson(e.category)) + "}";
                if (i < dictionary.poses.Count - 1) line += ",";
                sb.AppendLine(line);
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Quote(string value)
        {
            return "\"" + value + "\"";
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static PoseCategoryDictionaryFile LoadPoseCategoryDictionary(string path)
        {
            if (!File.Exists(path))
                return new PoseCategoryDictionaryFile();

            string json = File.ReadAllText(path, Encoding.UTF8);
            PoseCategoryDictionaryFile loaded = new PoseCategoryDictionaryFile();
            loaded.poses = new List<PoseCategoryEntry>();

            // 出力と同じ素朴な形だけを読む。依存を増やさず、欠落もしない。
            Regex rx = new Regex(@"\{\s*""id""\s*:\s*(-?\d+)\s*,\s*""name""\s*:\s*""(.*?)""\s*,\s*""category""\s*:\s*""(.*?)""\s*\}");
            foreach (Match m in rx.Matches(json))
            {
                int id;
                if (!int.TryParse(m.Groups[1].Value, out id)) continue;
                loaded.poses.Add(new PoseCategoryEntry
                {
                    id = id,
                    name = m.Groups[2].Value,
                    category = m.Groups[3].Value,
                });
            }
            return loaded;
        }

        private static string InferPoseCategory(string name)
        {
            string value = (name ?? string.Empty).Trim();
            if (ContainsAny(value, "寝", "仰向", "うつ伏", "横たわ", "寝そべ", "添い寝"))
                return "寝";
            if (ContainsAny(value, "座", "正座", "あぐら", "胡坐"))
                return "座り";
            if (ContainsAny(value, "中腰", "しゃが", "屈み", "かがみ", "前屈"))
                return "中腰";
            if (ContainsAny(value, "立ち", "立つ", "ファンタジーポーズ"))
                return "立ち";
            return "未分類";
        }

        private static bool ContainsAny(string value, params string[] words)
        {
            for (int i = 0; i < words.Length; i++)
                if (value.IndexOf(words[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }
    }
}
