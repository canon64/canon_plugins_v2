using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UnityEngine;

namespace MainGameHScenePoseInjector
{
    [DataContract]
    internal sealed class ExpressionPreset
    {
        [DataMember(Name = "Name")]
        public string Name = "";
        [DataMember(Name = "EyebrowPtn")]
        public int EyebrowPtn = -1;
        [DataMember(Name = "EyesPtn")]
        public int EyesPtn = -1;
        [DataMember(Name = "MouthPtn")]
        public int MouthPtn = -1;
    }

    [DataContract]
    internal sealed class PluginSettings
    {
        [DataMember(Name = "SettingsVersion")]
        public int SettingsVersion = 2;
        [DataMember(Name = "Enabled")]
        public bool Enabled = true;
        [DataMember(Name = "UiVisible")]
        public bool UiVisible = false;
        [DataMember(Name = "ToggleUiKey")]
        public KeyCode ToggleUiKey = KeyCode.F4;

        [DataMember(Name = "WindowX")]
        public float WindowX = 40f;
        [DataMember(Name = "WindowY")]
        public float WindowY = 80f;
        [DataMember(Name = "WindowWidth")]
        public float WindowWidth = 1100f;
        [DataMember(Name = "WindowHeight")]
        public float WindowHeight = 560f;

        [DataMember(Name = "LastAppliedPoseId")]
        public int LastAppliedPoseId = -1;
        [DataMember(Name = "LastEyebrowPtn")]
        public int LastEyebrowPtn = -1;
        [DataMember(Name = "LastEyesPtn")]
        public int LastEyesPtn = -1;
        [DataMember(Name = "LastMouthPtn")]
        public int LastMouthPtn = -1;
        [DataMember(Name = "AutoRestoreOnControllerChange")]
        public bool AutoRestoreOnControllerChange = true;
        [DataMember(Name = "ApplyToAllFemales")]
        public bool ApplyToAllFemales = false;
        [DataMember(Name = "KeepSelectedExpressionWhilePoseApplied")]
        public bool KeepSelectedExpressionWhilePoseApplied = true;
        [DataMember(Name = "HSceneLostConfirmSeconds")]
        public float HSceneLostConfirmSeconds = 0.75f;
        [DataMember(Name = "ControllerChangeConfirmSeconds")]
        public float ControllerChangeConfirmSeconds = 0.5f;
        [DataMember(Name = "ExpressionPresets")]
        public List<ExpressionPreset> ExpressionPresets = new List<ExpressionPreset>();

        /// <summary>ポーズと表情の自動変更設定。</summary>
        // この設定ファイルは DataContractJsonSerializer で読み書きされるため、
        // [DataMember] が無い項目は保存されず、起動のたびに初期値へ戻る。
        [DataMember]
        public AutoCycleSettings AutoCycle = new AutoCycleSettings();
        [DataMember(Name = "DefaultExpressionPresetsInstalled")]
        public bool DefaultExpressionPresetsInstalled = false;
    }

    internal static class SettingsStore
    {
        internal static PluginSettings LoadOrCreate(string path, Action<string> logInfo, Action<string> logWarn)
        {
            try
            {
                if (!File.Exists(path))
                {
                    PluginSettings created = Normalize(new PluginSettings());
                    Save(path, created, logWarn);
                    logInfo?.Invoke("settings created: " + path);
                    return created;
                }

                string json = File.ReadAllText(path, Encoding.UTF8);
                PluginSettings loaded = Deserialize(json) ?? new PluginSettings();
                loaded = Normalize(loaded);
                Save(path, loaded, logWarn);
                return loaded;
            }
            catch (Exception ex)
            {
                logWarn?.Invoke("settings load failed: " + ex.Message);
                return Normalize(new PluginSettings());
            }
        }

        internal static void Save(string path, PluginSettings settings, Action<string> logWarn)
        {
            try
            {
                PluginSettings normalized = Normalize(settings ?? new PluginSettings());
                string json = Serialize(normalized);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                logWarn?.Invoke("settings save failed: " + ex.Message);
            }
        }

        private static PluginSettings Normalize(PluginSettings settings)
        {
            if (settings == null)
                settings = new PluginSettings();

            // 旧v0.1デフォルト(320)/v0.2初期デフォルト(720) のまま残ってる設定は新レイアウト用に自動拡張
            if (settings.WindowWidth <= 760f)
                settings.WindowWidth = 1100f;
            settings.WindowWidth = Mathf.Clamp(settings.WindowWidth, 600f, 1920f);
            settings.WindowHeight = Mathf.Clamp(settings.WindowHeight, 200f, 1080f);
            settings.WindowX = Mathf.Max(0f, settings.WindowX);
            settings.WindowY = Mathf.Max(0f, settings.WindowY);
            if (settings.HSceneLostConfirmSeconds <= 0f)
                settings.HSceneLostConfirmSeconds = 0.75f;
            if (settings.ControllerChangeConfirmSeconds <= 0f)
                settings.ControllerChangeConfirmSeconds = 0.5f;
            settings.HSceneLostConfirmSeconds = Mathf.Clamp(settings.HSceneLostConfirmSeconds, 0.1f, 5f);
            settings.ControllerChangeConfirmSeconds = Mathf.Clamp(settings.ControllerChangeConfirmSeconds, 0.1f, 5f);
            if (settings.SettingsVersion < 2)
                settings.KeepSelectedExpressionWhilePoseApplied = true;
            if (settings.ExpressionPresets == null)
                settings.ExpressionPresets = new List<ExpressionPreset>();
            if (!settings.DefaultExpressionPresetsInstalled)
            {
                settings.ExpressionPresets.AddRange(CreateDefaultExpressionPresets());
                settings.DefaultExpressionPresetsInstalled = true;
            }
            settings.SettingsVersion = 2;

            return settings;
        }

        private static List<ExpressionPreset> CreateDefaultExpressionPresets()
        {
            return new List<ExpressionPreset>
            {
                Preset("01 自然", 0, 0, 0),
                Preset("02 やさしい微笑み", 0, 4, 1),
                Preset("03 満面の笑顔", 0, 2, 2),
                Preset("04 目閉じ笑顔", 0, 3, 2),
                Preset("05 照れ笑顔", 2, 8, 3),
                Preset("06 はにかみ", 11, 7, 1),
                Preset("07 左ウィンク", 15, 5, 2),
                Preset("08 右ウィンク", 16, 6, 2),
                Preset("09 得意顔", 14, 20, 19),
                Preset("10 いたずら顔", 14, 4, 20),
                Preset("11 かわいい猫口", 0, 2, 37),
                Preset("12 キス待ち", 2, 1, 23),
                Preset("13 舌ペロ", 14, 4, 20),
                Preset("14 舌出し", 14, 8, 24),
                Preset("15 とろけ顔", 2, 7, 6),
                Preset("16 ハート目", 2, 26, 5),
                Preset("17 瞳にハート", 2, 25, 2),
                Preset("18 瞳に星", 12, 24, 2),
                Preset("19 興奮", 12, 27, 5),
                Preset("20 真剣", 10, 10, 10),
                Preset("21 怒り", 1, 9, 8),
                Preset("22 激怒", 8, 9, 9),
                Preset("23 不満", 3, 11, 15),
                Preset("24 嫌悪", 1, 13, 12),
                Preset("25 呆れ", 3, 11, 16),
                Preset("26 疑問・左", 4, 14, 0),
                Preset("27 疑問・右", 5, 14, 0),
                Preset("28 思案", 6, 14, 10),
                Preset("29 困り", 2, 19, 13),
                Preset("30 不安", 11, 17, 14),
                Preset("31 切ない", 11, 7, 13),
                Preset("32 悲しい", 13, 15, 13),
                Preset("33 泣き", 13, 16, 13),
                Preset("34 焦り", 12, 17, 14),
                Preset("35 驚き", 12, 0, 17),
                Preset("36 落胆", 13, 18, 13),
                Preset("37 苦しい", 2, 12, 14),
                Preset("38 つまらない", 3, 11, 16),
                Preset("39 ぐるぐる目", 12, 21, 18),
                Preset("40 デフォルメ泣き", 13, 32, 38),
            };
        }

        private static ExpressionPreset Preset(string name, int eyebrow, int eyes, int mouth)
        {
            return new ExpressionPreset
            {
                Name = name,
                EyebrowPtn = eyebrow,
                EyesPtn = eyes,
                MouthPtn = mouth,
            };
        }

        private static string Serialize(PluginSettings settings)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(PluginSettings));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, settings);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static PluginSettings Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(PluginSettings));
            using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return serializer.ReadObject(stream) as PluginSettings;
            }
        }
    }
}
