using System.Collections.Generic;
using System.Runtime.Serialization;

namespace MainGameBlankMapAdd
{
    [DataContract]
    internal sealed class PluginSettings
    {
        private static int NormalizeCubeFaceTileCount(int value)
        {
            switch (value)
            {
                case 1:
                case 4:
                case 9:
                case 16:
                case 25:
                    return value;
                default:
                    return 1;
            }
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext ctx)
        {
            OverlayBrightness = 1f;
        }

        [DataMember(Order = 99)]
        public float OverlayBrightness = 1f;

        [OnDeserialized]
        private void OnDeserialized(StreamingContext ctx)
        {
            if (float.IsNaN(OverlayBrightness) || float.IsInfinity(OverlayBrightness)) OverlayBrightness = 1f;
            OverlayBrightness = System.Math.Max(0f, System.Math.Min(2f, OverlayBrightness));
            // 旧デフォルト900は民宿マップと競合するため自動マイグレーション
            if (AddedMapNo == 900 || AddedMapNo == 9000) AddedMapNo = 5682352;
            if (AddedThumbnailID <= 0) AddedThumbnailID = 9000;
            if (VideoPath == null) VideoPath = string.Empty;
            if (FloorOverrideVideoPath == null) FloorOverrideVideoPath = string.Empty;
            if (CeilingOverrideVideoPath == null) CeilingOverrideVideoPath = string.Empty;
            if (RoomWidth  <= 0.01f) RoomWidth  = 12f;
            if (RoomDepth  <= 0.01f) RoomDepth  = 12f;
            if (RoomHeight <= 0.01f) RoomHeight = 6f;
            if (VideoVolume < 0f)    VideoVolume = 0.3f;
            if (SphereRadius <= 0.01f) SphereRadius = 8f;
            if (string.IsNullOrWhiteSpace(VoiceReverbPreset)) VoiceReverbPreset = "Psychotic";
            if (PlaybackBarShowMouseBottomPx < 0f) PlaybackBarShowMouseBottomPx = 0f;
            if (PlaybackBarHeight < 20f) PlaybackBarHeight = 72f;
            if (PlaybackBarMarginX < 0f) PlaybackBarMarginX = 0f;
            if (PlaybackBarButtonWidth < 36f) PlaybackBarButtonWidth = 64f;
            CubeFaceTileCount = NormalizeCubeFaceTileCount(CubeFaceTileCount);
            if (FolderFadeDuration < 0.01f) FolderFadeDuration = 1.0f;
            if (HttpPort <= 0 || HttpPort > 65535) HttpPort = 55782;
            FolderPlayEnabled = true;
            if (FolderPlayPath == null) FolderPlayPath = string.Empty;
            if (FolderPlayPaths == null) FolderPlayPaths = new List<string>();
            if (float.IsNaN(VideoAudioGain) || float.IsInfinity(VideoAudioGain) || VideoAudioGain <= 0f)
                VideoAudioGain = 1f;
            if (WebCamRequestedWidth <= 0) WebCamRequestedWidth = 1920;
            if (WebCamRequestedHeight <= 0) WebCamRequestedHeight = 1080;
            if (WebCamRequestedFps <= 0) WebCamRequestedFps = 30;
            if (WebCamStatusLogIntervalSec < 0.25f) WebCamStatusLogIntervalSec = 1f;
            if (WebCamBlackSampleIntervalSec < 0.5f) WebCamBlackSampleIntervalSec = 2f;
            if (string.IsNullOrWhiteSpace(OverlayTargetSurface)) OverlayTargetSurface = "All";
            else OverlayTargetSurface = OverlayTargetSurface.Trim();
            if (float.IsNaN(OverlayOpacity) || float.IsInfinity(OverlayOpacity)) OverlayOpacity = 0.5f;
            if (OverlayOpacity < 0f) OverlayOpacity = 0f;
            if (OverlayOpacity > 1f) OverlayOpacity = 1f;
            if (string.IsNullOrWhiteSpace(OverlayTileMode)) OverlayTileMode = "single";
            if (OverlayTileMode != "single" && OverlayTileMode != "tile") OverlayTileMode = "single";
            if (float.IsNaN(OverlayBeatOpacityMin) || float.IsInfinity(OverlayBeatOpacityMin)) OverlayBeatOpacityMin = 0.1f;
            if (float.IsNaN(OverlayBeatOpacityMax) || float.IsInfinity(OverlayBeatOpacityMax)) OverlayBeatOpacityMax = 1.0f;
            if (OverlayBeatOpacityMin < 0f) OverlayBeatOpacityMin = 0f;
            if (OverlayBeatOpacityMin > 1f) OverlayBeatOpacityMin = 1f;
            if (OverlayBeatOpacityMax < 0f) OverlayBeatOpacityMax = 0f;
            if (OverlayBeatOpacityMax > 1f) OverlayBeatOpacityMax = 1f;
            if (float.IsNaN(SolidRoomColorR) || float.IsInfinity(SolidRoomColorR)) SolidRoomColorR = 0f;
            if (float.IsNaN(SolidRoomColorG) || float.IsInfinity(SolidRoomColorG)) SolidRoomColorG = 0f;
            if (float.IsNaN(SolidRoomColorB) || float.IsInfinity(SolidRoomColorB)) SolidRoomColorB = 0f;
            if (SolidRoomColorR < 0f) SolidRoomColorR = 0f;
            if (SolidRoomColorR > 1f) SolidRoomColorR = 1f;
            if (SolidRoomColorG < 0f) SolidRoomColorG = 0f;
            if (SolidRoomColorG > 1f) SolidRoomColorG = 1f;
            if (SolidRoomColorB < 0f) SolidRoomColorB = 0f;
            if (SolidRoomColorB > 1f) SolidRoomColorB = 1f;
            if (ImageSlideshowFolderPath == null) ImageSlideshowFolderPath = string.Empty;
            if (ImageSlideshowFolderPaths == null) ImageSlideshowFolderPaths = new List<string>();
            if (ImageSlideshowSeconds < 0.5f) ImageSlideshowSeconds = 5f;
            if (ImageSlideshowSeconds > 120f) ImageSlideshowSeconds = 120f;
            if (ImageSlideshowScanIntervalSec < 0.25f) ImageSlideshowScanIntervalSec = 2f;
            if (ImageSlideshowScanIntervalSec > 60f) ImageSlideshowScanIntervalSec = 60f;
            if (string.IsNullOrWhiteSpace(ImageSlideshowSortMode)) ImageSlideshowSortMode = "Date";
            if (ImageSlideshowSortMode != "Name" && ImageSlideshowSortMode != "Date" && ImageSlideshowSortMode != "Random")
                ImageSlideshowSortMode = "Date";
            if (float.IsNaN(ImageSlideshowOpacity) || float.IsInfinity(ImageSlideshowOpacity) || ImageSlideshowOpacity <= 0f)
                ImageSlideshowOpacity = 1f;
            if (ImageSlideshowOpacity > 1f) ImageSlideshowOpacity = 1f;
            if (string.IsNullOrWhiteSpace(ImageSlideshowFitMode)) ImageSlideshowFitMode = "Cover";
            if (ImageSlideshowFitMode != "Cover" && ImageSlideshowFitMode != "Stretch") ImageSlideshowFitMode = "Cover";
            if (string.IsNullOrWhiteSpace(ImageSlideshowVerticalFocus)) ImageSlideshowVerticalFocus = "Center";
            if (ImageSlideshowVerticalFocus != "Top" && ImageSlideshowVerticalFocus != "Center" && ImageSlideshowVerticalFocus != "Bottom")
                ImageSlideshowVerticalFocus = "Center";
            if (string.IsNullOrWhiteSpace(ImageSlideshowHorizontalFocus)) ImageSlideshowHorizontalFocus = "Center";
            if (ImageSlideshowHorizontalFocus != "Left" && ImageSlideshowHorizontalFocus != "Center" && ImageSlideshowHorizontalFocus != "Right")
                ImageSlideshowHorizontalFocus = "Center";
            if (string.IsNullOrWhiteSpace(ImageSlideshowPanMode)) ImageSlideshowPanMode = "None";
            if (ImageSlideshowPanMode != "None" &&
                ImageSlideshowPanMode != "TopToBottom" &&
                ImageSlideshowPanMode != "BottomToTop" &&
                ImageSlideshowPanMode != "LeftToRight" &&
                ImageSlideshowPanMode != "RightToLeft")
                ImageSlideshowPanMode = "None";
            if (string.IsNullOrWhiteSpace(ImageSlideshowTargetSurface)) ImageSlideshowTargetSurface = "All";
            if (string.IsNullOrWhiteSpace(ImageSlideshowTileMode)) ImageSlideshowTileMode = "single";
            if (ImageSlideshowTileMode != "single" && ImageSlideshowTileMode != "tile") ImageSlideshowTileMode = "single";
            if (string.IsNullOrWhiteSpace(ImageSlideshowTransitionMode)) ImageSlideshowTransitionMode = "CrossFade";
            if (ImageSlideshowTransitionMode != "None" && ImageSlideshowTransitionMode != "Fade" && ImageSlideshowTransitionMode != "CrossFade")
                ImageSlideshowTransitionMode = "CrossFade";
            if (ImageSlideshowTransitionSeconds <= 0f) ImageSlideshowTransitionSeconds = 0.5f;
            if (ImageSlideshowTransitionSeconds > 10f) ImageSlideshowTransitionSeconds = 10f;
            // 旧 ImageSlideshowLatestOnly からの移行 / モード正規化
            if (string.IsNullOrWhiteSpace(ImageSlideshowPlayMode))
                ImageSlideshowPlayMode = ImageSlideshowLatestOnly ? "Latest" : "Queue";
            if (ImageSlideshowPlayMode != "Latest" && ImageSlideshowPlayMode != "Queue")
                ImageSlideshowPlayMode = "Latest";
            // 既存コードは LatestOnly を「Latest=瞬時最新 / false=自動送り＋控え」として参照するため同期しておく
            ImageSlideshowLatestOnly = (ImageSlideshowPlayMode == "Latest");
            if (!string.IsNullOrWhiteSpace(FolderPlayPath))
            {
                bool exists = false;
                for (int i = 0; i < FolderPlayPaths.Count; i++)
                {
                    if (string.Equals(FolderPlayPaths[i], FolderPlayPath, System.StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    FolderPlayPaths.Add(FolderPlayPath);
            }
            if (!string.IsNullOrWhiteSpace(ImageSlideshowFolderPath))
            {
                bool exists = false;
                for (int i = 0; i < ImageSlideshowFolderPaths.Count; i++)
                {
                    if (string.Equals(ImageSlideshowFolderPaths[i], ImageSlideshowFolderPath, System.StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    ImageSlideshowFolderPaths.Add(ImageSlideshowFolderPath);
            }
        }

        [DataMember(Order = 0)]
        public int AddedMapNo = 5682352;

        [DataMember(Order = 1)]
        public int SourceMapNo = 1;

        [DataMember(Order = 2)]
        public string AddedMapName = "video_all_pose_room";

        [DataMember(Order = 3)]
        public string AddedDisplayName = "VideoAllPoseRoom";

        [DataMember(Order = 4)]
        public int AddedSort = 910;

        [DataMember(Order = 5)]
        public bool ForceIsGate = true;

        [DataMember(Order = 6)]
        public bool ForceIsFreeH = true;

        [DataMember(Order = 7)]
        public bool ForceIsH = false;

        [DataMember(Order = 8)]
        public bool BlankifySceneOnLoad = true;

        [DataMember(Order = 9)]
        public bool DisableRenderers = true;

        [DataMember(Order = 10)]
        public bool DisableTerrains = true;

        [DataMember(Order = 11)]
        public bool DisableLights = true;

        [DataMember(Order = 12)]
        public bool DisableParticles = true;

        [DataMember(Order = 13)]
        public bool VerboseLog = false;

        [DataMember(Order = 14)]
        public int AddedThumbnailID = 9000;

        [DataMember(Order = 15)]
        public bool EnableVideoRoom = true;

        [DataMember(Order = 16)]
        public string VideoPath = "";

        [DataMember(Order = 17)]
        public bool UseFloorVideoOverride = false;

        [DataMember(Order = 18)]
        public string FloorOverrideVideoPath = "";

        [DataMember(Order = 19)]
        public bool UseCeilingVideoOverride = false;

        [DataMember(Order = 20)]
        public string CeilingOverrideVideoPath = "";

        [DataMember(Order = 21)]
        public bool VideoLoop = true;

        [DataMember(Order = 22)]
        public bool MuteVideoAudio = false;

        [DataMember(Order = 26)]
        public bool AutoPlayOnMapLoad = false;

        [DataMember(Order = 23)]
        public float RoomWidth = 12f;

        [DataMember(Order = 24)]
        public float RoomDepth = 12f;

        [DataMember(Order = 25)]
        public float RoomHeight = 6f;

        [DataMember(Order = 27)]
        public float VideoVolume = 0.3f;

        [DataMember(Order = 28)]
        public bool DisableAudioSources = true;

        [DataMember(Order = 29)]
        public float VideoRoomOffsetX = 0f;

        [DataMember(Order = 30)]
        public float VideoRoomOffsetY = 0f;

        [DataMember(Order = 31)]
        public float VideoRoomOffsetZ = 0f;

        [DataMember(Order = 32)]
        public float VideoRoomRotationX = 0f;

        [DataMember(Order = 33)]
        public float VideoRoomRotationY = 180f;

        [DataMember(Order = 34)]
        public float VideoRoomRotationZ = 0f;

        /// <summary>true=球体、false=平面(Quad)</summary>
        [DataMember(Order = 35)]
        public bool UseSphere = false;

        [DataMember(Order = 36)]
        public float SphereRadius = 8f;

        [DataMember(Order = 37)]
        public bool EnableVoiceReverb = true;

        [DataMember(Order = 38)]
        public string VoiceReverbPreset = "Psychotic";

        /// <summary>true=球体の内側面に表示、false=外側面に表示</summary>
        [DataMember(Order = 41)]
        public bool SphereInsideView = true;

        [DataMember(Order = 42)]
        public bool EnablePlaybackBar = true;

        [DataMember(Order = 43)]
        public float PlaybackBarShowMouseBottomPx = 0f;

        [DataMember(Order = 44)]
        public float PlaybackBarHeight = 72f;

        [DataMember(Order = 45)]
        public float PlaybackBarMarginX = 0f;

        [DataMember(Order = 46)]
        public float PlaybackBarButtonWidth = 64f;

        [DataMember(Order = 47)]
        public int CubeFaceTileCount = 1;

        [DataMember(Order = 50)]
        public bool FolderPlayEnabled = true;

        [DataMember(Order = 51)]
        public string FolderPlayPath = "";

        [DataMember(Order = 59)]
        public List<string> FolderPlayPaths = new List<string>();

        [DataMember(Order = 52)]
        public bool FolderPlayLoop = true;

        [DataMember(Order = 58)]
        public bool FolderPlaySingleLoop = false;

        /// <summary>"Name", "Date", or "Random"</summary>
        [DataMember(Order = 53)]
        public string FolderPlaySortMode = "Name";

        [DataMember(Order = 54)]
        public bool FolderPlaySortAscending = true;

        /// <summary>フォルダ再生時の動画切り替えクロスフェード時間（秒）。0で即時切替。</summary>
        [DataMember(Order = 55)]
        public float FolderFadeDuration = 1.0f;

        /// <summary>外部HTTP受信を有効にする</summary>
        [DataMember(Order = 56)]
        public bool HttpEnabled = true;

        /// <summary>HTTP受信ポート番号</summary>
        [DataMember(Order = 57)]
        public int HttpPort = 55782;

        /// <summary>再生中Hボイス音源(Voice/PlayObjectPCM)を動画部屋座標へ同期する</summary>
        [DataMember(Order = 60)]
        public bool SyncVoiceSourcesToVideoRoom = false;

        [DataMember(Order = 61)]
        public bool ApplyReverbToVideoAudio = false;

        /// <summary>動画音声の追加ゲイン倍率（最終適用はDualMonoFilter側で処理）</summary>
        [DataMember(Order = 62)]
        public float VideoAudioGain = 1f;

        /// <summary>再生バーの説明ポップアップ表示</summary>
        [DataMember(Order = 63)]
        public bool EnableUiHelpPopup = true;

        /// <summary>audio-snap 診断ログを出力する（通常は false でよい）</summary>
        [DataMember(Order = 64)]
        public bool EnableAudioDiagnosticsLog = false;

        [DataMember(Order = 65)]
        public int WebCamRequestedWidth = 1920;

        [DataMember(Order = 66)]
        public int WebCamRequestedHeight = 1080;

        [DataMember(Order = 67)]
        public int WebCamRequestedFps = 30;

        [DataMember(Order = 68)]
        public float WebCamStatusLogIntervalSec = 1f;

        [DataMember(Order = 69)]
        public float WebCamBlackSampleIntervalSec = 2f;

        /// <summary>サブカメラ映像をVideoSurfaceの上に半透明オーバーレイする</summary>
        [DataMember(Order = 70)]
        public bool OverlayEnabled = false;

        /// <summary>オーバーレイ対象の論理面（Front/Back/Left/Right/Floor/Ceiling/All）</summary>
        [DataMember(Order = 71)]
        public string OverlayTargetSurface = "All";

        /// <summary>オーバーレイ透明度（0=完全透明, 1=完全不透明）</summary>
        [DataMember(Order = 72)]
        public float OverlayOpacity = 0.5f;

        /// <summary>オーバーレイ分割モード（single=論理面全体に1枚, tile=物理Quadごとに1枚）</summary>
        [DataMember(Order = 73)]
        public string OverlayTileMode = "single";

        /// <summary>オーバーレイ透明度の拍連動を有効化</summary>
        [DataMember(Order = 74)]
        public bool OverlayBeatOpacityEnabled = false;

        /// <summary>拍連動透明度の最小値（0=完全透明）</summary>
        [DataMember(Order = 75)]
        public float OverlayBeatOpacityMin = 0.1f;

        /// <summary>拍連動透明度の最大値（1=完全不透明）</summary>
        [DataMember(Order = 76)]
        public float OverlayBeatOpacityMax = 1.0f;

        /// <summary>拍連動透明度を反転（true=拍強で透明、拍弱で不透明）</summary>
        [DataMember(Order = 77)]
        public bool OverlayBeatOpacityInverted = false;

        /// <summary>動画ルーム全面を単色で表示する</summary>
        [DataMember(Order = 78)]
        public bool SolidRoomColorEnabled = false;

        /// <summary>全面単色モードの赤成分（0-1）</summary>
        [DataMember(Order = 79)]
        public float SolidRoomColorR = 0f;

        /// <summary>全面単色モードの緑成分（0-1）</summary>
        [DataMember(Order = 80)]
        public float SolidRoomColorG = 0f;

        /// <summary>全面単色モードの青成分（0-1）</summary>
        [DataMember(Order = 81)]
        public float SolidRoomColorB = 0f;

        /// <summary>画像フォルダのスライドショーを動画面の上、サブカメラの下に表示する</summary>
        [DataMember(Order = 82)]
        public bool ImageSlideshowEnabled = false;

        [DataMember(Order = 83)]
        public string ImageSlideshowFolderPath = "";

        [DataMember(Order = 84)]
        public List<string> ImageSlideshowFolderPaths = new List<string>();

        [DataMember(Order = 85)]
        public float ImageSlideshowSeconds = 5f;

        [DataMember(Order = 86)]
        public float ImageSlideshowScanIntervalSec = 2f;

        /// <summary>"Name", "Date", or "Random"</summary>
        [DataMember(Order = 87)]
        public string ImageSlideshowSortMode = "Date";

        [DataMember(Order = 88)]
        public bool ImageSlideshowSortAscending = false;

        [DataMember(Order = 89)]
        public float ImageSlideshowOpacity = 1f;

        /// <summary>"Cover" or "Stretch"</summary>
        [DataMember(Order = 90)]
        public string ImageSlideshowFitMode = "Cover";

        /// <summary>表示対象の論理面（Front/Back/Left/Right/Floor/Ceiling/All）</summary>
        [DataMember(Order = 91)]
        public string ImageSlideshowTargetSurface = "All";

        /// <summary>true=常にフォルダ内の最新画像を表示し、自動送りしない（PlayMode="Latest"と同期）</summary>
        [DataMember(Order = 92)]
        public bool ImageSlideshowLatestOnly = false;

        /// <summary>"Latest"=常に最新を瞬時表示 / "Queue"=控え1枚を持ち60秒ごとに最新へ進む(無ければ放置)</summary>
        [DataMember(Order = 99)]
        public string ImageSlideshowPlayMode = "Latest";

        /// <summary>true=指定フォルダ直下の一番新しいサブフォルダを画像スライドショー対象にする</summary>
        [DataMember(Order = 100)]
        public bool ImageSlideshowAutoLatestSubfolder = true;

        /// <summary>"single"=論理面全体に1枚, "tile"=物理Quadごとに1枚</summary>
        [DataMember(Order = 93)]
        public string ImageSlideshowTileMode = "single";

        /// <summary>"None", "Fade", or "CrossFade"</summary>
        [DataMember(Order = 94)]
        public string ImageSlideshowTransitionMode = "CrossFade";

        [DataMember(Order = 95)]
        public float ImageSlideshowTransitionSeconds = 0.5f;

        /// <summary>"Top", "Center", or "Bottom"</summary>
        [DataMember(Order = 96)]
        public string ImageSlideshowVerticalFocus = "Center";

        /// <summary>"Left", "Center", or "Right"</summary>
        [DataMember(Order = 97)]
        public string ImageSlideshowHorizontalFocus = "Center";

        /// <summary>"None", "TopToBottom", "BottomToTop", "LeftToRight", or "RightToLeft"</summary>
        [DataMember(Order = 98)]
        public string ImageSlideshowPanMode = "None";

    }
}
