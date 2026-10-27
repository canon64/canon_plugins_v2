using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MainGameHScenePoseInjector
{
    /// <summary>送り順。順送り / 逆戻り / ランダム。</summary>
    public enum CycleOrder
    {
        Forward = 0,
        Reverse = 1,
        Random = 2,
    }

    /// <summary>
    /// 1系統ぶんの自動送り設定。
    /// 眉・目・口はそれぞれ独立した番号一覧を持つため、系統ごとにこれを持たせる。
    /// </summary>
    [DataContract]
    public sealed class ChannelCycleSettings
    {
        [DataMember]
        public bool Enabled = false;
        [DataMember]
        public int Order = (int)CycleOrder.Forward;
        [DataMember]
        public float HoldSeconds = 5f;
        /// <summary>切り替え時にゲーム側の補間を使うか。表情APIは秒数指定を持たないためON/OFFのみ。</summary>
        [DataMember]
        public bool Blend = true;
        /// <summary>実際に回すパターン番号。空なら全部。(眉/目/口 用)</summary>
        [DataMember]
        public List<int> Whitelist = new List<int>();

        // ── 数値系統(頬赤・涙)用 ────────────────────────────────
        // 連続値に選択肢を並べても意味が薄いので、範囲と段階数で持つ。
        [DataMember]
        public int MinValue = 0;
        [DataMember]
        public int MaxValue = 100;
        /// <summary>最小から最大までを何段階に割るか。2以上。</summary>
        [DataMember]
        public int StepCount = 5;
        /// <summary>スライダーで直接指定した値。自動がOFFでもこれが維持される。</summary>
        [DataMember]
        public int ManualValue = 0;

        /// <summary>逆シリアル化で 0 になった項目を意味のある値へ戻す。</summary>
        internal void Normalize(int defMin, int defMax, int defSteps)
        {
            if (HoldSeconds <= 0f) HoldSeconds = 5f;
            if (StepCount < 2) StepCount = defSteps;
            if (MinValue == 0 && MaxValue == 0) { MinValue = defMin; MaxValue = defMax; }
            if (Whitelist == null) Whitelist = new List<int>();
        }

        public ChannelCycleSettings Clone()
        {
            return new ChannelCycleSettings
            {
                Enabled = Enabled,
                Order = Order,
                HoldSeconds = HoldSeconds,
                Blend = Blend,
                Whitelist = new List<int>(Whitelist ?? new List<int>()),
                MinValue = MinValue,
                MaxValue = MaxValue,
                StepCount = StepCount,
            };
        }
    }

    /// <summary>表情をどの単位で送るか。</summary>
    public enum ExpressionCycleSource
    {
        /// <summary>保存済みの表情プリセット(眉+目+口の組)を1単位として送る。</summary>
        Preset = 0,
        /// <summary>眉・目・口をそれぞれ独立に送る。</summary>
        PerChannel = 1,
    }

    /// <summary>
    /// ポーズと表情の自動変更の全設定。
    ///
    /// 一括(BulkMode)と個別の二段構えにしてある。
    /// 面倒な時は一括の順序と秒数だけ決めれば全系統がそれに従い、
    /// 詰めたい時は BulkMode を切って系統ごとに設定する。
    /// </summary>
    [DataContract]
    public sealed class AutoCycleSettings
    {
        internal AutoCycleSettings SuspensionCopy() {
            var serializer=new System.Runtime.Serialization.DataContractSerializer(typeof(AutoCycleSettings));
            using(var stream=new System.IO.MemoryStream()) {serializer.WriteObject(stream,this); stream.Position=0; return (AutoCycleSettings)serializer.ReadObject(stream);}
        }

        [DataMember]
        public bool Enabled = false;

        // ── 一括 ──────────────────────────────────────────────
        /// <summary>ONの間、全系統が下の一括値で動く(個別値は保持したまま無視される)。</summary>
        [DataMember]
        public bool BulkMode = true;
        [DataMember]
        public int BulkOrder = (int)CycleOrder.Forward;
        [DataMember]
        public float BulkHoldSeconds = 5f;
        [DataMember]
        public float BulkTransitionSeconds = 0.3f;
        [DataMember]
        public bool BulkBlend = true;

        // ── ポーズ ────────────────────────────────────────────
        [DataMember]
        public bool PoseEnabled = true;
        [DataMember]
        public int PoseOrder = (int)CycleOrder.Forward;
        [DataMember]
        public float PoseHoldSeconds = 5f;
        /// <summary>次のポーズへ移るのにかける秒数。同一コントローラ内でのみ有効。</summary>
        [DataMember]
        public float PoseTransitionSeconds = 0.3f;
        /// <summary>対象カテゴリ。空なら全件。</summary>
        [DataMember]
        public List<string> PoseCategories = new List<string>();
        /// <summary>個別に選んだポーズIDだけを回す。空ならカテゴリ条件に従う。</summary>
        [DataMember]
        public List<int> PoseIdWhitelist = new List<int>();

        // ── 表情 ──────────────────────────────────────────────
        [DataMember]
        public bool ExpressionEnabled = true;
        [DataMember]
        public int ExpressionSource = (int)ExpressionCycleSource.Preset;

        // プリセット単位
        [DataMember]
        public int PresetOrder = (int)CycleOrder.Forward;
        [DataMember]
        public float PresetHoldSeconds = 5f;
        [DataMember]
        public bool PresetBlend = true;
        /// <summary>対象プリセット名。空なら全件。</summary>
        [DataMember]
        public List<string> PresetWhitelist = new List<string>();

        // 系統別
        [DataMember]
        public ChannelCycleSettings Eyebrow = new ChannelCycleSettings();
        [DataMember]
        public ChannelCycleSettings Eyes = new ChannelCycleSettings();
        [DataMember]
        public ChannelCycleSettings Mouth = new ChannelCycleSettings();

        /// <summary>頬赤。Whitelist は百分率(0〜100)で持ち、適用時に0〜1へ直す。</summary>
        [DataMember]
        public ChannelCycleSettings HohoAka = new ChannelCycleSettings();
        /// <summary>涙。0〜3の4段階。ゲーム側 tearsFlags が bool[4,3] のため。</summary>
        [DataMember]
        public ChannelCycleSettings Tears = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
        [DataMember] public ChannelCycleSettings FrontUpSiru = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
        [DataMember] public ChannelCycleSettings FrontDownSiru = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
        [DataMember] public ChannelCycleSettings BackUpSiru = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
        [DataMember] public ChannelCycleSettings BackDownSiru = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
        [DataMember] public ChannelCycleSettings FaceSiru = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
        [DataMember] public ChannelCycleSettings ModTears = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
        [DataMember] public ChannelCycleSettings ModDrool = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
        [DataMember] public ChannelCycleSettings ModSaliva = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
        [DataMember] public ChannelCycleSettings ModSweat = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };

        /// <summary>
        /// 逆シリアル化の後始末。
        ///
        /// DataContractJsonSerializer はフィールドの初期化子を実行しない。
        /// JSONに項目が無いと 0.3秒 ではなく型の既定値 0 が入り、
        /// 遷移秒数が 0 になって補間せずジャンプする。保持秒数も 0 になる。
        /// 読み込み直後に、意味を成さない値だけを既定へ戻す。
        /// </summary>
        [OnDeserialized]
        internal void OnDeserialized(StreamingContext context)
        {
            Normalize();
        }

        internal void Normalize()
        {
            if (BulkHoldSeconds <= 0f) BulkHoldSeconds = 5f;
            if (BulkTransitionSeconds <= 0f) BulkTransitionSeconds = 0.3f;
            if (PoseHoldSeconds <= 0f) PoseHoldSeconds = 5f;
            if (PoseTransitionSeconds <= 0f) PoseTransitionSeconds = 0.3f;
            if (PresetHoldSeconds <= 0f) PresetHoldSeconds = 5f;

            if (PoseCategories == null) PoseCategories = new List<string>();
            if (PoseIdWhitelist == null) PoseIdWhitelist = new List<int>();
            if (PresetWhitelist == null) PresetWhitelist = new List<string>();

            if (Eyebrow == null) Eyebrow = new ChannelCycleSettings();
            if (Eyes == null) Eyes = new ChannelCycleSettings();
            if (Mouth == null) Mouth = new ChannelCycleSettings();
            if (HohoAka == null) HohoAka = new ChannelCycleSettings();
            if (Tears == null) Tears = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
            if (FrontUpSiru == null) FrontUpSiru = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
            if (FrontDownSiru == null) FrontDownSiru = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
            if (BackUpSiru == null) BackUpSiru = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
            if (BackDownSiru == null) BackDownSiru = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
            if (FaceSiru == null) FaceSiru = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
            if (ModTears == null) ModTears = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
            if (ModDrool == null) ModDrool = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
            if (ModSaliva == null) ModSaliva = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };
            if (ModSweat == null) ModSweat = new ChannelCycleSettings { MinValue = 0, MaxValue = 3, StepCount = 4 };

            Eyebrow.Normalize(0, 100, 5);
            Eyes.Normalize(0, 100, 5);
            Mouth.Normalize(0, 100, 5);
            HohoAka.Normalize(0, 100, 5);
            Tears.Normalize(0, 3, 4);
            FrontUpSiru.Normalize(0, 3, 4);
            FrontDownSiru.Normalize(0, 3, 4);
            BackUpSiru.Normalize(0, 3, 4);
            BackDownSiru.Normalize(0, 3, 4);
            FaceSiru.Normalize(0, 3, 4);
            ModTears.Normalize(0, 3, 4);
            ModDrool.Normalize(0, 3, 4);
            ModSaliva.Normalize(0, 3, 4);
            ModSweat.Normalize(0, 3, 4);
        }

        /// <summary>一括の値を個別設定へ流し込む。一括で大枠を決めてから個別に直すための口。</summary>
        public void PushBulkToChannels()
        {
            PoseOrder = BulkOrder;
            PoseHoldSeconds = BulkHoldSeconds;
            PoseTransitionSeconds = BulkTransitionSeconds;

            PresetOrder = BulkOrder;
            PresetHoldSeconds = BulkHoldSeconds;
            PresetBlend = BulkBlend;

            foreach (ChannelCycleSettings ch in new[] { Eyebrow, Eyes, Mouth, HohoAka, Tears, FaceSiru, FrontUpSiru, FrontDownSiru, BackUpSiru, BackDownSiru, ModTears, ModDrool, ModSaliva, ModSweat })
            {
                if (ch == null) continue;
                ch.Order = BulkOrder;
                ch.HoldSeconds = BulkHoldSeconds;
                ch.Blend = BulkBlend;
            }
        }

        /// <summary>実際に使う順序。一括モード中は一括値が勝つ。</summary>
        public CycleOrder EffectiveOrder(int individualOrder)
        {
            return (CycleOrder)(BulkMode ? BulkOrder : individualOrder);
        }

        /// <summary>実際に使う保持秒数。一括モード中は一括値が勝つ。</summary>
        public float EffectiveHold(float individualSeconds)
        {
            float v = BulkMode ? BulkHoldSeconds : individualSeconds;
            return v < 0.1f ? 0.1f : v;
        }

        public bool EffectiveBlend(bool individualBlend)
        {
            return BulkMode ? BulkBlend : individualBlend;
        }

        public float EffectiveTransition()
        {
            float v = BulkMode ? BulkTransitionSeconds : PoseTransitionSeconds;
            return v < 0f ? 0f : v;
        }
    }
}
