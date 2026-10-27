using System;
using System.Runtime.Serialization;

namespace MainGameCharacterMorphBridge
{
    [DataContract]
    internal sealed class MorphCardRegistration
    {
        [DataMember(Order = 0)] public bool Enabled = true;
        [DataMember(Order = 1)] public string Word = string.Empty;
        [DataMember(Order = 2)] public string TriggerWords = string.Empty;
        [DataMember(Order = 3)] public string CardPath = string.Empty;

        internal MorphCardRegistration Normalize()
        {
            Word = (Word ?? string.Empty).Trim();
            TriggerWords = PluginSettings.NormalizeCsv(TriggerWords);
            CardPath = PluginSettings.NormalizePath(CardPath);
            return this;
        }
    }

    /// <summary>変身の数値変化のかかり方。</summary>
    internal enum MorphEasingMode
    {
        /// <summary>等速。</summary>
        Linear = 0,
        /// <summary>最初は加速、最後は減速。</summary>
        EaseInOut = 1,
        /// <summary>最初だけ加速して、最後は等速のまま止まる。</summary>
        EaseIn = 2,
        /// <summary>最初から速く、最後に減速して止まる。</summary>
        EaseOut = 3
    }

    [DataContract]
    internal sealed class PluginSettings
    {
        [DataMember(Order = 0)] public bool Enabled = true;
        [DataMember(Order = 1)] public bool EnableLogs;
        [DataMember(Order = 2)] public int TargetFemaleIndex;
        [DataMember(Order = 3)] public string TargetCardPath = string.Empty;
        [DataMember(Order = 4)] public bool AutoCaptureOriginal = true;
        [DataMember(Order = 5)] public bool AutoLoadOnHSceneStart = true;
        [DataMember(Order = 6)] public bool AutoResetBlendOnHSceneStart = true;
        [DataMember(Order = 7)] public string SelectedCardWord = string.Empty;
        [DataMember(Order = 8)] public string SelectedCardTriggerWords = string.Empty;
        [DataMember(Order = 9)] public MorphCardRegistration[] RegisteredCards = new MorphCardRegistration[0];
        [DataMember(Order = 10)] public float Blend;
        [DataMember(Order = 11)] public float Height = 0.5f;
        [DataMember(Order = 12)] public float Breast = 0.5f;
        [DataMember(Order = 13)] public int BodyShapeIndex;
        [DataMember(Order = 14)] public float BodyShapeValue = 0.5f;
        [DataMember(Order = 15)] public int FaceShapeIndex;
        [DataMember(Order = 16)] public float FaceShapeValue = 0.5f;
        // 変身(Blend遷移)中にMainGameGlowを焚く設定。変身する本人以外は光らせない。
        [DataMember(Order = 17)] public bool MorphGlowEnabled = true;
        [DataMember(Order = 18)] public float MorphGlowMaxScale = 1f;
        // 立ち上がりと引きの秒数。合計が変身の長さを超える場合は比率を保ったまま詰める。
        [DataMember(Order = 19)] public float MorphGlowFadeInSeconds = 1.5f;
        [DataMember(Order = 20)] public float MorphGlowFadeOutSeconds = 2f;
        // 焚いている間だけMainGameGlow側の値を上書きする。
        [DataMember(Order = 21)] public float MorphGlowThreshold = 0.5f;
        [DataMember(Order = 22)] public float MorphGlowStrength = 3f;
        // 変身開始に合わせて鳴らす効果音。mp3/wav/ogg。
        [DataMember(Order = 23)] public bool MorphSoundEnabled = true;
        // 相対パスはプラグインフォルダ基準。既定は同梱の sound\ 配下。
        [DataMember(Order = 24)] public string MorphSoundPath = @"sound\きらきら輝く4.wav";
        [DataMember(Order = 25)] public float MorphSoundVolume = 0.8f;
        // AudioSource.volume は1.0で頭打ちなので、それ以上上げたい時はこちらを使う。
        [DataMember(Order = 26)] public float MorphSoundGain = 1f;
        // 変身にかける秒数。ONにすると呼び出し側が渡してきた秒数を無視してこの値を使う。
        [DataMember(Order = 27)] public bool MorphDurationOverrideEnabled = false;
        [DataMember(Order = 28)] public float MorphDurationSeconds = 10f;
        // 数値変化のかかり方。既定は加減速あり。
        [DataMember(Order = 29)] public MorphEasingMode MorphEasing = MorphEasingMode.EaseInOut;
        // 大きいほど加減速がきつくなる。1.0で実質リニア。
        [DataMember(Order = 30)] public float MorphEasingPower = 2f;
        // 登録用の入力欄。既定の変身先(TargetCardPath)とは別に持つ。
        [DataMember(Order = 31)] public string RegisterCardPath = string.Empty;
        // 髪・肌・顔の色も一緒に補間するか。反映が重い場合に備えて間隔で間引く。
        [DataMember(Order = 32)] public bool MorphColorEnabled = true;
        [DataMember(Order = 33)] public bool MorphColorHair = true;
        [DataMember(Order = 34)] public bool MorphColorFace = true;
        [DataMember(Order = 35)] public bool MorphColorBody = true;
        // 髪型(メッシュ)の差し替え。IDなので中間で切り替わる。
        // 既定はfalse。従来の「髪型は変わらない」挙動をそのまま残すため。
        [DataMember(Order = 42)] public bool MorphHairStyleEnabled = false;
        // 上絵(タトゥー)の差し替え。
        [DataMember(Order = 40)] public bool MorphOverlaySwapEnabled = true;
        // 化粧(アイシャドウ・チーク・リップ)と顔/体のペイント。番号は中間で切り替わる。
        [DataMember(Order = 41)] public bool MorphColorMakeup = true;
        [DataMember(Order = 36)] public float MorphColorIntervalSeconds = 0.1f;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext context)
        {
            Normalize();
        }

        internal PluginSettings Normalize()
        {
            TargetFemaleIndex = ClampInt(TargetFemaleIndex, 0, 1);
            TargetCardPath = NormalizePath(TargetCardPath);
            SelectedCardWord = (SelectedCardWord ?? string.Empty).Trim();
            SelectedCardTriggerWords = NormalizeCsv(SelectedCardTriggerWords);
            RegisteredCards = NormalizeRegisteredCards(RegisteredCards);
            Blend = Round2(Clamp01(Blend));
            Height = Round2(Clamp01(Height));
            Breast = Round2(Clamp01(Breast));
            BodyShapeIndex = ClampInt(BodyShapeIndex, 0, 43);
            BodyShapeValue = Round2(Clamp01(BodyShapeValue));
            FaceShapeIndex = ClampInt(FaceShapeIndex, 0, 51);
            FaceShapeValue = Round2(Clamp01(FaceShapeValue));
            MorphGlowMaxScale = Round2(Clamp01(MorphGlowMaxScale));
            MorphGlowFadeInSeconds = Round2(ClampFloat(MorphGlowFadeInSeconds, 0f, 30f));
            MorphGlowFadeOutSeconds = Round2(ClampFloat(MorphGlowFadeOutSeconds, 0f, 30f));
            MorphGlowThreshold = Round2(ClampFloat(MorphGlowThreshold, 0f, 5f));
            MorphGlowStrength = Round2(ClampFloat(MorphGlowStrength, 0f, 10f));
            MorphSoundPath = NormalizePath(MorphSoundPath);
            MorphSoundVolume = Round2(Clamp01(MorphSoundVolume));
            MorphSoundGain = Round2(ClampFloat(MorphSoundGain, 0f, 8f));
            MorphDurationSeconds = Round2(ClampFloat(MorphDurationSeconds, 0f, 60f));
            MorphEasingPower = Round2(ClampFloat(MorphEasingPower, 1f, 5f));
            RegisterCardPath = NormalizePath(RegisterCardPath);
            MorphColorIntervalSeconds = Round2(ClampFloat(MorphColorIntervalSeconds, 0f, 2f));
            return this;
        }

        internal static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
        }

        internal static float ClampFloat(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        internal static int ClampInt(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        internal static float Round2(float value)
        {
            return (float)Math.Round(value, 2, MidpointRounding.AwayFromZero);
        }

        internal static string NormalizePath(string value)
        {
            return (value ?? string.Empty).Trim().Trim('"');
        }

        internal static string NormalizeCsv(string csv)
        {
            string source = (csv ?? string.Empty).Replace('、', ',');
            string[] parts = source.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 0)
                return string.Empty;

            var results = new System.Collections.Generic.List<string>();
            foreach (string part in parts)
            {
                string item = (part ?? string.Empty).Trim();
                if (item.Length <= 0)
                    continue;
                if (!results.Contains(item))
                    results.Add(item);
            }

            return string.Join(",", results.ToArray());
        }

        private static MorphCardRegistration[] NormalizeRegisteredCards(MorphCardRegistration[] cards)
        {
            if (cards == null || cards.Length <= 0)
                return new MorphCardRegistration[0];

            var results = new System.Collections.Generic.List<MorphCardRegistration>();
            foreach (MorphCardRegistration card in cards)
            {
                if (card == null)
                    continue;

                card.Normalize();
                if (string.IsNullOrWhiteSpace(card.Word) && string.IsNullOrWhiteSpace(card.CardPath))
                    continue;

                results.Add(card);
            }

            return results.ToArray();
        }
    }
}
