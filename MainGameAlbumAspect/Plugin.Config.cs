using BepInEx.Configuration;
using ConfigurationManager;

namespace MainGameAlbumAspect
{
    /// <summary>アスペクト比の合わせ方。</summary>
    public enum AlbumAspectMode
    {
        /// <summary>何もしない（バニラ＝矩形いっぱいに引き伸ばし）。</summary>
        Off = 0,

        /// <summary>元の矩形の内側に収める（レターボックス）。RectTransform の sizeDelta を直接調整する。</summary>
        FitRect = 1,

        /// <summary>AspectRatioFitter(FitInParent)。親の矩形に対して内側フィット。</summary>
        FitInParent = 2,

        /// <summary>
        /// 枠(親)ごと画像の縦横比に作り直し、画面の縦いっぱいまで広げる。
        /// 枠が横長のままだと画像を大きくしても両脇に白が残るので、先に枠側を潰す。
        /// 縦長画像を画面いっぱいで見たい時用。
        /// </summary>
        FrameToImage = 5,

        /// <summary>AspectRatioFitter(EnvelopeParent)。親を覆う＝はみ出しはトリミング（Mask が無いと溢れる）。</summary>
        EnvelopeParent = 3,

        /// <summary>RawImage.uvRect で中央トリミング。レイアウトを一切触らないので最も安全だが画像は切れる。</summary>
        CropUv = 4,
    }

    /// <summary>どの RawImage に対する適用かの区別。</summary>
    internal enum AspectTargetKind
    {
        /// <summary>CapFileViewer.Preview の大きいプレビュー画像。</summary>
        Preview = 0,

        /// <summary>CapFileNode のサムネイル一覧。</summary>
        Thumbnail = 1,

        /// <summary>CapFileDeleteDialog の削除確認プレビュー。</summary>
        DeleteDialog = 2,
    }

    public sealed partial class Plugin
    {
        internal ConfigEntry<bool> CfgEnabled { get; private set; }
        internal ConfigEntry<AlbumAspectMode> CfgMode { get; private set; }
        internal ConfigEntry<float> CfgFitScale { get; private set; }
        internal ConfigEntry<bool> CfgUseParentBounds { get; private set; }
        internal ConfigEntry<bool> CfgAutoFallbackOnLayoutGroup { get; private set; }

        internal ConfigEntry<bool> CfgApplyToPreview { get; private set; }
        internal ConfigEntry<bool> CfgApplyToThumbnail { get; private set; }
        internal ConfigEntry<bool> CfgApplyToDeleteDialog { get; private set; }

        internal ConfigEntry<bool> CfgVerboseLog { get; private set; }

        private const string SectionGeneral = "1. 基本";
        private const string SectionTargets = "2. 適用対象";
        private const string SectionDebug = "9. デバッグ";

        private void SetupConfig()
        {
            CfgEnabled = Config.Bind(
                SectionGeneral, "有効", true,
                new ConfigDescription(
                    "アルバム（撮影画像ビューア）のアスペクト比補正を有効にする。オフにすると即座にバニラの見た目へ戻る。",
                    null,
                    new ConfigurationManagerAttributes { Order = 100 }));

            CfgMode = Config.Bind(
                SectionGeneral, "表示モード", AlbumAspectMode.FitRect,
                new ConfigDescription(
                    "Off=バニラ（引き伸ばし） / FitRect=元の枠内に収める（推奨・レターボックス） / "
                        + "FitInParent=AspectRatioFitter で親に内側フィット / "
                        + "EnvelopeParent=AspectRatioFitter で親を覆う（はみ出しあり） / "
                        + "CropUv=uvRect で中央トリミング（レイアウト非変更）",
                    null,
                    new ConfigurationManagerAttributes { Order = 90 }));

            CfgUseParentBounds = Config.Bind(
                SectionGeneral, "基準を親にする", false,
                new ConfigDescription(
                    "FitRect モードの基準を、画像枠ではなく親の矩形にする。"
                    + "縦長画像を縦いっぱいに広げたい時にON。枠が小さい縦画面で効く。",
                    null,
                    new ConfigurationManagerAttributes { Order = 85 }));

            CfgFitScale = Config.Bind(
                SectionGeneral, "サイズ倍率", 1.0f,
                new ConfigDescription(
                    "FitRect モードのとき、収めた後の大きさに掛ける倍率。1.0 で枠いっぱい。1.0 を超えると枠からはみ出して大きくなる（両脇の余白を潰したい時に上げる）。",
                    new AcceptableValueRange<float>(0.1f, 4.0f),
                    new ConfigurationManagerAttributes { Order = 80 }));

            CfgAutoFallbackOnLayoutGroup = Config.Bind(
                SectionGeneral, "LayoutGroup配下では自動でFitRectに退避", true,
                new ConfigDescription(
                    "親に LayoutGroup がある場合 AspectRatioFitter は効かないことがあるため、"
                        + "FitInParent / EnvelopeParent を選んでいても FitRect に切り替える。",
                    null,
                    new ConfigurationManagerAttributes { Order = 70 }));

            CfgApplyToPreview = Config.Bind(
                SectionTargets, "プレビュー画像", true,
                new ConfigDescription(
                    "サムネイルをクリックした時に出る大きなプレビュー（CapFileViewer.Preview）に適用する。",
                    null,
                    new ConfigurationManagerAttributes { Order = 60 }));

            CfgApplyToThumbnail = Config.Bind(
                SectionTargets, "サムネイル一覧", true,
                new ConfigDescription(
                    "アルバムのサムネイル格子（CapFileNode）に適用する。",
                    null,
                    new ConfigurationManagerAttributes { Order = 50 }));

            CfgApplyToDeleteDialog = Config.Bind(
                SectionTargets, "削除確認ダイアログ", true,
                new ConfigDescription(
                    "削除確認ダイアログのプレビュー（CapFileDeleteDialog）に適用する。",
                    null,
                    new ConfigurationManagerAttributes { Order = 40 }));

            CfgVerboseLog = Config.Bind(
                SectionDebug, "詳細ログ", false,
                new ConfigDescription(
                    "適用の詳細をログファイルへ出す。原因調査のとき以外はオフでよい。",
                    null,
                    new ConfigurationManagerAttributes { Order = 10, IsAdvanced = true }));
        }

        /// <summary>対象種別ごとの有効判定。</summary>
        internal bool IsTargetEnabled(AspectTargetKind kind)
        {
            switch (kind)
            {
                case AspectTargetKind.Preview:
                    return CfgApplyToPreview != null && CfgApplyToPreview.Value;
                case AspectTargetKind.Thumbnail:
                    return CfgApplyToThumbnail != null && CfgApplyToThumbnail.Value;
                case AspectTargetKind.DeleteDialog:
                    return CfgApplyToDeleteDialog != null && CfgApplyToDeleteDialog.Value;
                default:
                    return false;
            }
        }
    }
}
