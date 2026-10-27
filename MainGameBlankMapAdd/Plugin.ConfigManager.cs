using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Configuration;
using MainGameBlankMapAdd.Settings;
using UnityEngine;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        private bool _configDirty;
        private bool _syncingConfig;
        private string _lastAppliedConfigSignature = string.Empty;
        private string _presetDir;
        private const string PresetNone = "(none)";
        private ConfigEntry<string> _cfgPresetName;
        private ConfigEntry<bool> _cfgSaveCurrentState;
        private ConfigEntry<bool> _cfgDeleteSelectedPreset;

        private ConfigEntry<int> _cfgAddedMapNo;
        private ConfigEntry<int> _cfgSourceMapNo;
        private ConfigEntry<string> _cfgAddedMapName;
        private ConfigEntry<string> _cfgAddedDisplayName;
        private ConfigEntry<int> _cfgAddedSort;
        private ConfigEntry<bool> _cfgForceIsGate;
        private ConfigEntry<bool> _cfgForceIsFreeH;
        private ConfigEntry<bool> _cfgForceIsH;
        private ConfigEntry<int> _cfgAddedThumbnailID;

        private ConfigEntry<bool> _cfgBlankifySceneOnLoad;
        private ConfigEntry<bool> _cfgDisableRenderers;
        private ConfigEntry<bool> _cfgDisableTerrains;
        private ConfigEntry<bool> _cfgDisableLights;
        private ConfigEntry<bool> _cfgDisableParticles;
        private ConfigEntry<bool> _cfgDisableAudioSources;

        private ConfigEntry<bool> _cfgEnableVideoRoom;
        private ConfigEntry<string> _cfgVideoPath;
        private ConfigEntry<bool> _cfgUseFloorVideoOverride;
        private ConfigEntry<string> _cfgFloorOverrideVideoPath;
        private ConfigEntry<bool> _cfgBrowseFloorOverrideVideoPath;
        private ConfigEntry<bool> _cfgUseCeilingVideoOverride;
        private ConfigEntry<string> _cfgCeilingOverrideVideoPath;
        private ConfigEntry<bool> _cfgBrowseCeilingOverrideVideoPath;
        private ConfigEntry<bool> _cfgVideoLoop;
        private ConfigEntry<bool> _cfgMuteVideoAudio;
        private ConfigEntry<bool> _cfgAutoPlayOnMapLoad;
        private ConfigEntry<float> _cfgVideoVolume;
        private ConfigEntry<float> _cfgVideoAudioGain;
        private ConfigEntry<bool> _cfgApplyReverbToVideoAudio;
        private ConfigEntry<bool> _cfgEnablePlaybackBar;
        private ConfigEntry<bool> _cfgEnableUiHelpPopup;
        private ConfigEntry<float> _cfgPlaybackBarShowMouseBottomPx;
        private ConfigEntry<float> _cfgPlaybackBarHeight;
        private ConfigEntry<float> _cfgPlaybackBarMarginX;
        private ConfigEntry<float> _cfgPlaybackBarButtonWidth;
        private ConfigEntry<int>   _cfgCubeFaceTileCount;
        private ConfigEntry<bool>   _cfgUseWebCam;
        private ConfigEntry<string> _cfgWebCamDevice;

        private ConfigEntry<string> _cfgFolderPlayPath;
        private ConfigEntry<bool> _cfgBrowseFolderPlayPath;
        private ConfigEntry<bool> _cfgFolderPlayLoop;
        private ConfigEntry<bool> _cfgFolderPlaySingleLoop;
        private ConfigEntry<bool> _cfgFolderPlayScan;
        private ConfigEntry<string> _cfgFolderPlaySortMode;
        private ConfigEntry<bool> _cfgFolderPlaySortAscending;
        private ConfigEntry<float> _cfgFolderFadeDuration;
        private ConfigEntry<bool> _cfgHttpEnabled;
        private ConfigEntry<int> _cfgHttpPort;
        private ConfigEntry<bool> _cfgSyncVoiceSourcesToVideoRoom;

        private ConfigEntry<float> _cfgRoomWidth;
        private ConfigEntry<float> _cfgRoomDepth;
        private ConfigEntry<float> _cfgRoomHeight;
        private ConfigEntry<float> _cfgVideoRoomOffsetX;
        private ConfigEntry<float> _cfgVideoRoomOffsetY;
        private ConfigEntry<float> _cfgVideoRoomOffsetZ;
        private ConfigEntry<float> _cfgVideoRoomRotationX;
        private ConfigEntry<float> _cfgVideoRoomRotationY;
        private ConfigEntry<float> _cfgVideoRoomRotationZ;

        private ConfigEntry<bool> _cfgUseSphere;
        private ConfigEntry<float> _cfgSphereRadius;
        private ConfigEntry<bool> _cfgSphereInsideView;

        private ConfigEntry<bool> _cfgEnableVoiceReverb;
        private ConfigEntry<string> _cfgVoiceReverbPreset;
        private ConfigEntry<bool> _cfgImageSlideshowEnabled;
        private ConfigEntry<string> _cfgImageSlideshowFolderPath;
        private ConfigEntry<float> _cfgImageSlideshowSeconds;
        private ConfigEntry<float> _cfgImageSlideshowScanIntervalSec;
        private ConfigEntry<string> _cfgImageSlideshowSortMode;
        private ConfigEntry<bool> _cfgImageSlideshowSortAscending;
        private ConfigEntry<float> _cfgImageSlideshowOpacity;
        private ConfigEntry<string> _cfgImageSlideshowFitMode;
        private ConfigEntry<string> _cfgImageSlideshowTargetSurface;
        private ConfigEntry<bool> _cfgImageSlideshowLatestOnly;
        private ConfigEntry<string> _cfgImageSlideshowTileMode;
        private ConfigEntry<string> _cfgImageSlideshowTransitionMode;
        private ConfigEntry<float> _cfgImageSlideshowTransitionSeconds;
        private ConfigEntry<string> _cfgImageSlideshowVerticalFocus;
        private ConfigEntry<string> _cfgImageSlideshowHorizontalFocus;
        private ConfigEntry<string> _cfgImageSlideshowPanMode;

        private ConfigEntry<bool> _cfgVerboseLog;
        private ConfigEntry<bool> _cfgEnableAudioDiagnosticsLog;

        private void SetupConfigEntries()
        {
            _presetDir = Path.Combine(_pluginDir, "MapAddPresets");
            Directory.CreateDirectory(_presetDir);
            EnsureDefaultPresetIfMissing();
            string[] presetNames = BuildPresetNameList();

            _cfgPresetName = Config.Bind(
                "00.Preset",
                "PresetName",
                PresetNone,
                new ConfigDescription(
                    "読み込み対象プリセット（MapAddPresets/*.json）",
                    new AcceptableValueList<string>(presetNames),
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 100
                    }));
            _cfgPresetName.SettingChanged += OnPresetNameChanged;

            // Remove legacy entries renamed for simpler workflow.
            Config.Remove(new ConfigDefinition("00.Preset", "ApplyPreset"));
            Config.Remove(new ConfigDefinition("00.Preset", "SaveCurrentState"));
            Config.Remove(new ConfigDefinition("00.Preset", "VideoPathInput"));
            Config.Remove(new ConfigDefinition("00.Preset", "Load"));
            Config.Remove(new ConfigDefinition("00.Preset", "BrowseVideoPath"));
            Config.Remove(new ConfigDefinition("03.Video", "BrowseVideoPath"));

            _cfgSaveCurrentState = Config.Bind(
                "00.Preset",
                "Save",
                false,
                new ConfigDescription(
                    "現在状態を保存",
                    null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Order = 97,
                        HideDefaultButton = true,
                        CustomDrawer = DrawSaveCurrentStateButtonCustomDrawer
                    }));
            _cfgSaveCurrentState.SettingChanged += OnSaveCurrentStateChanged;

            _cfgDeleteSelectedPreset = Config.Bind(
                "00.Preset",
                "DeleteSelectedPreset",
                false,
                "trueにすると、現在選択中PresetNameのjsonを削除して自動でfalseへ戻す（defaultは保護）");
            _cfgDeleteSelectedPreset.SettingChanged += OnDeleteSelectedPresetChanged;

            if (!presetNames.Contains(_cfgPresetName.Value))
            {
                _syncingConfig = true;
                try
                {
                    _cfgPresetName.Value = PresetNone;
                }
                finally
                {
                    _syncingConfig = false;
                }
            }

            _cfgAddedMapNo = Config.Bind("01.Map", "AddedMapNo", _settings.AddedMapNo, "追加するマップ番号");
            _cfgSourceMapNo = Config.Bind("01.Map", "SourceMapNo", _settings.SourceMapNo, "複製元マップ番号");
            _cfgAddedMapName = Config.Bind("01.Map", "AddedMapName", NormalizeString(_settings.AddedMapName), "内部マップ名");
            _cfgAddedDisplayName = Config.Bind("01.Map", "AddedDisplayName", NormalizeString(_settings.AddedDisplayName), "表示名");
            _cfgAddedSort = Config.Bind("01.Map", "AddedSort", _settings.AddedSort, "並び順");
            _cfgForceIsGate = Config.Bind("01.Map", "ForceIsGate", _settings.ForceIsGate, "ルート移動先として扱う");
            _cfgForceIsFreeH = Config.Bind("01.Map", "ForceIsFreeH", _settings.ForceIsFreeH, "フリーH対象にする");
            _cfgForceIsH = Config.Bind("01.Map", "ForceIsH", _settings.ForceIsH, "Hマップ扱い");
            _cfgAddedThumbnailID = Config.Bind("01.Map", "AddedThumbnailID", _settings.AddedThumbnailID, "追加マップのサムネID");

            _cfgBlankifySceneOnLoad = Config.Bind("02.Blank", "BlankifySceneOnLoad", _settings.BlankifySceneOnLoad, "読み込み時に背景要素を消す");
            _cfgDisableRenderers = Config.Bind("02.Blank", "DisableRenderers", _settings.DisableRenderers, "Rendererを無効化");
            _cfgDisableTerrains = Config.Bind("02.Blank", "DisableTerrains", _settings.DisableTerrains, "Terrainを無効化");
            _cfgDisableLights = Config.Bind("02.Blank", "DisableLights", _settings.DisableLights, "Lightを無効化");
            _cfgDisableParticles = Config.Bind("02.Blank", "DisableParticles", _settings.DisableParticles, "Particleを無効化");
            _cfgDisableAudioSources = Config.Bind("02.Blank", "DisableAudioSources", _settings.DisableAudioSources, "既存AudioSourceをミュート");

            _cfgEnableVideoRoom = Config.Bind("03.Video", "EnableVideoRoom", _settings.EnableVideoRoom, "動画ルームを有効化");
            _cfgVideoPath = Config.Bind(
                "03.Video",
                "VideoPath",
                NormalizeVideoPathInput(NormalizeString(_settings.VideoPath)),
                new ConfigDescription(
                    "共通動画パス（内部適用値）",
                    null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        Browsable = false
                    }));
            _cfgUseFloorVideoOverride = Config.Bind("03.Video", "UseFloorVideoOverride", _settings.UseFloorVideoOverride, "床だけ別動画を使う");
            _cfgFloorOverrideVideoPath = Config.Bind("03.Video", "FloorOverrideVideoPath", NormalizeVideoPathInput(NormalizeString(_settings.FloorOverrideVideoPath)), "床オーバーライド動画パス");
            _cfgBrowseFloorOverrideVideoPath = Config.Bind("03.Video", "BrowseFloorOverrideVideoPath", false, "trueで床オーバーライド動画を参照し、選択結果をFloorOverrideVideoPathへ設定してfalseに戻す");
            _cfgUseCeilingVideoOverride = Config.Bind("03.Video", "UseCeilingVideoOverride", _settings.UseCeilingVideoOverride, "天井だけ別動画を使う");
            _cfgCeilingOverrideVideoPath = Config.Bind("03.Video", "CeilingOverrideVideoPath", NormalizeVideoPathInput(NormalizeString(_settings.CeilingOverrideVideoPath)), "天井オーバーライド動画パス");
            _cfgBrowseCeilingOverrideVideoPath = Config.Bind("03.Video", "BrowseCeilingOverrideVideoPath", false, "trueで天井オーバーライド動画を参照し、選択結果をCeilingOverrideVideoPathへ設定してfalseに戻す");
            _cfgVideoLoop = Config.Bind("03.Video", "VideoLoop", _settings.VideoLoop, "動画ループ");
            _cfgMuteVideoAudio = Config.Bind("03.Video", "MuteVideoAudio", _settings.MuteVideoAudio, "動画音声ミュート");
            _cfgAutoPlayOnMapLoad = Config.Bind("03.Video", "AutoPlayOnMapLoad", _settings.AutoPlayOnMapLoad, "マップ読み込み時に自動再生する");
            _cfgVideoVolume = Config.Bind("03.Video", "VideoVolume", _settings.VideoVolume, "動画音量");
            _cfgVideoAudioGain = Config.Bind(
                "03.Video",
                "VideoAudioGain",
                _settings.VideoAudioGain,
                new ConfigDescription("動画音声の追加ゲイン倍率", new AcceptableValueRange<float>(0.1f, 6f)));
            _cfgApplyReverbToVideoAudio = Config.Bind("03.Video", "ApplyReverbToVideoAudio", _settings.ApplyReverbToVideoAudio, "V-REV: 動画音声とVMD音源に残響を適用する。OFFでは適用しない");
            _cfgEnablePlaybackBar = Config.Bind("03.Video", "EnablePlaybackBar", _settings.EnablePlaybackBar, "画面下部の再生バーを有効化");
            _cfgEnableUiHelpPopup = Config.Bind("03.Video", "EnableUiHelpPopup", _settings.EnableUiHelpPopup, "再生バーの説明ポップアップを表示する");
            _cfgPlaybackBarShowMouseBottomPx = Config.Bind("03.Video", "PlaybackBarShowMouseBottomPx", _settings.PlaybackBarShowMouseBottomPx, "マウスが下端からこのpx以内で再生バー表示");
            _cfgPlaybackBarHeight = Config.Bind("03.Video", "PlaybackBarHeight", _settings.PlaybackBarHeight, "再生バー高さ(px)");
            _cfgPlaybackBarMarginX = Config.Bind("03.Video", "PlaybackBarMarginX", _settings.PlaybackBarMarginX, "再生バー左右マージン(px)");
            _cfgPlaybackBarButtonWidth = Config.Bind("03.Video", "PlaybackBarButtonWidth", _settings.PlaybackBarButtonWidth, "再生/停止ボタン幅(px)");
            _cfgCubeFaceTileCount = Config.Bind(
                "03.Video",
                "CubeFaceTileCount",
                _settings.CubeFaceTileCount,
                new ConfigDescription(
                    "立方体1面に並べる動画パネル枚数（サイズ固定）",
                    new AcceptableValueList<int>(1, 4, 9, 16, 25)));

            _cfgUseWebCam = Config.Bind(
                "03.Video",
                "UseWebCam",
                IsWebCamUrl(NormalizeString(_settings.VideoPath)),
                new ConfigDescription(
                    "WebCam映像を表示する（ONにするとVideoPathをwebcam://デバイス名に設定）"));
            _cfgUseWebCam.SettingChanged += OnUseWebCamChanged;

            var webCamDeviceNames = BuildWebCamDeviceList();
            string currentWebCamDevice = IsWebCamUrl(NormalizeString(_settings.VideoPath))
                ? ExtractWebCamDeviceName(NormalizeString(_settings.VideoPath))
                : (webCamDeviceNames.Length > 0 ? webCamDeviceNames[0] : string.Empty);
            if (!System.Array.Exists(webCamDeviceNames, d => d == currentWebCamDevice) && webCamDeviceNames.Length > 0)
                currentWebCamDevice = webCamDeviceNames[0];
            _cfgWebCamDevice = Config.Bind(
                "03.Video",
                "WebCamDevice",
                currentWebCamDevice,
                new ConfigDescription(
                    "WebCamデバイス名（UseWebCam=trueの場合に使用される）",
                    webCamDeviceNames.Length > 0
                        ? (AcceptableValueBase)new AcceptableValueList<string>(webCamDeviceNames)
                        : null));
            _cfgWebCamDevice.SettingChanged += OnWebCamDeviceChanged;

            Config.Remove(new ConfigDefinition("04.FolderPlay", "FolderPlayEnabled"));
            _cfgFolderPlayLoop = Config.Bind("04.FolderPlay", "FolderPlayLoop", _settings.FolderPlayLoop, "フォルダ末尾まで再生したら先頭に戻る");
            _cfgFolderPlaySingleLoop = Config.Bind("04.FolderPlay", "FolderPlaySingleLoop", _settings.FolderPlaySingleLoop, "1曲を単体ループ（有効時は次曲へ進まず同じ曲を再生）");
            _cfgFolderPlayPath = Config.Bind(
                "04.FolderPlay",
                "FolderPlayPath",
                NormalizeVideoPathInput(NormalizeString(_settings.FolderPlayPath)),
                "動画フォルダパス");
            _cfgBrowseFolderPlayPath = Config.Bind(
                "04.FolderPlay",
                "BrowseFolderPlayPath",
                false,
                new ConfigDescription(
                    "フォルダを参照して選択",
                    null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        HideDefaultButton = true,
                        CustomDrawer = DrawBrowseFolderPlayPathButtonCustomDrawer
                    }));
            _cfgFolderPlayScan = Config.Bind(
                "04.FolderPlay",
                "Scan",
                false,
                new ConfigDescription(
                    "フォルダを再スキャン",
                    null,
                    new ConfigurationManager.ConfigurationManagerAttributes
                    {
                        HideDefaultButton = true,
                        CustomDrawer = DrawFolderPlayScanButtonCustomDrawer
                    }));

            _cfgFolderPlaySortMode = Config.Bind(
                "04.FolderPlay",
                "FolderPlaySortMode",
                _settings.FolderPlaySortMode,
                new ConfigDescription(
                    "ソート方法（Name=名前順 / Date=更新日順）",
                    new AcceptableValueList<string>("Name", "Date")));
            _cfgFolderPlaySortAscending = Config.Bind(
                "04.FolderPlay",
                "FolderPlaySortAscending",
                _settings.FolderPlaySortAscending,
                "昇順=true / 降順=false");
            _cfgFolderFadeDuration = Config.Bind(
                "04.FolderPlay",
                "FolderFadeDuration",
                _settings.FolderFadeDuration,
                new ConfigDescription("フォルダ再生切り替え時のクロスフェード時間（秒）。0で即時切替。",
                    new AcceptableValueRange<float>(0f, 5f)));
            _cfgHttpEnabled = Config.Bind(
                "04.FolderPlay",
                "HttpEnabled",
                _settings.HttpEnabled,
                "外部HTTP受信を有効にする（要再起動）");
            _cfgHttpPort = Config.Bind(
                "04.FolderPlay",
                "HttpPort",
                _settings.HttpPort,
                new ConfigDescription("HTTP受信ポート番号（要再起動）",
                    new AcceptableValueRange<int>(1024, 65535)));
            _cfgSyncVoiceSourcesToVideoRoom = Config.Bind(
                "04.FolderPlay",
                "SyncVoiceSourcesToVideoRoom",
                _settings.SyncVoiceSourcesToVideoRoom,
                "再生中Hボイス音源(Voice/PlayObjectPCM)を動画部屋座標へ同期");

            _cfgRoomWidth = Config.Bind("04.Room", "RoomWidth", _settings.RoomWidth, "部屋幅");
            _cfgRoomDepth = Config.Bind("04.Room", "RoomDepth", _settings.RoomDepth, "部屋奥行");
            _cfgRoomHeight = Config.Bind("04.Room", "RoomHeight", _settings.RoomHeight, "部屋高さ");
            _cfgVideoRoomOffsetX = Config.Bind("04.Room", "VideoRoomOffsetX", _settings.VideoRoomOffsetX, "部屋オフセットX");
            _cfgVideoRoomOffsetY = Config.Bind("04.Room", "VideoRoomOffsetY", _settings.VideoRoomOffsetY, "部屋オフセットY");
            _cfgVideoRoomOffsetZ = Config.Bind("04.Room", "VideoRoomOffsetZ", _settings.VideoRoomOffsetZ, "部屋オフセットZ");
            _cfgVideoRoomRotationX = Config.Bind("04.Room", "VideoRoomRotationX", _settings.VideoRoomRotationX, "部屋回転X");
            _cfgVideoRoomRotationY = Config.Bind("04.Room", "VideoRoomRotationY", _settings.VideoRoomRotationY, "部屋回転Y");
            _cfgVideoRoomRotationZ = Config.Bind("04.Room", "VideoRoomRotationZ", _settings.VideoRoomRotationZ, "部屋回転Z");

            _cfgUseSphere = Config.Bind("05.Sphere", "UseSphere", _settings.UseSphere, "球体表示を使う");
            _cfgSphereRadius = Config.Bind("05.Sphere", "SphereRadius", _settings.SphereRadius, "球体半径");
            _cfgSphereInsideView = Config.Bind("05.Sphere", "SphereInsideView", _settings.SphereInsideView, "球体内側表示");

            _cfgEnableVoiceReverb = Config.Bind(
                "06.Reverb",
                "EnableVoiceReverb",
                _settings.EnableVoiceReverb,
                "リバーブ有効");
            _cfgVoiceReverbPreset = Config.Bind("06.Reverb", "VoiceReverbPreset",
                NormalizeString(_settings.VoiceReverbPreset),
                new ConfigDescription("AudioReverbPreset名",
                    new AcceptableValueList<string>(
                        "Off", "Generic", "PaddedCell", "Room", "Bathroom", "Livingroom",
                        "Stoneroom", "Auditorium", "Concerthall", "Cave", "Arena", "Hangar",
                        "CarpetedHallway", "Hallway", "StoneCorridor", "Alley", "Forest",
                        "City", "Mountains", "Quarry", "Plain", "ParkingLot", "SewerPipe",
                        "Underwater", "Drugged", "Dizzy", "Psychotic")));

            _cfgImageSlideshowEnabled = Config.Bind("07.ImageSlideshow", "ImageSlideshowEnabled", _settings.ImageSlideshowEnabled, "画像スライドショーを有効化");
            _cfgImageSlideshowFolderPath = Config.Bind("07.ImageSlideshow", "ImageSlideshowFolderPath", NormalizeVideoPathInput(NormalizeString(_settings.ImageSlideshowFolderPath)), "画像フォルダパス");
            _cfgImageSlideshowSeconds = Config.Bind(
                "07.ImageSlideshow",
                "ImageSlideshowSeconds",
                _settings.ImageSlideshowSeconds,
                new ConfigDescription("自動送り間隔（秒）", new AcceptableValueRange<float>(0.5f, 120f)));
            _cfgImageSlideshowScanIntervalSec = Config.Bind(
                "07.ImageSlideshow",
                "ImageSlideshowScanIntervalSec",
                _settings.ImageSlideshowScanIntervalSec,
                new ConfigDescription("フォルダ再走査間隔（秒）", new AcceptableValueRange<float>(0.25f, 60f)));
            _cfgImageSlideshowSortMode = Config.Bind(
                "07.ImageSlideshow",
                "ImageSlideshowSortMode",
                NormalizeString(_settings.ImageSlideshowSortMode),
                new ConfigDescription("画像ソート方法", new AcceptableValueList<string>("Name", "Date", "Random")));
            _cfgImageSlideshowSortAscending = Config.Bind("07.ImageSlideshow", "ImageSlideshowSortAscending", _settings.ImageSlideshowSortAscending, "画像ソートを昇順にする");
            _cfgImageSlideshowOpacity = Config.Bind(
                "07.ImageSlideshow",
                "ImageSlideshowOpacity",
                _settings.ImageSlideshowOpacity,
                new ConfigDescription("画像透明度", new AcceptableValueRange<float>(0.05f, 1f)));
            _cfgImageSlideshowFitMode = Config.Bind(
                "07.ImageSlideshow",
                "ImageSlideshowFitMode",
                NormalizeString(_settings.ImageSlideshowFitMode),
                new ConfigDescription("画像フィット方式", new AcceptableValueList<string>("Cover", "Stretch")));
            _cfgImageSlideshowTargetSurface = Config.Bind("07.ImageSlideshow", "ImageSlideshowTargetSurface", NormalizeString(_settings.ImageSlideshowTargetSurface), "表示対象面（All / Front,Back,...）");
            _cfgImageSlideshowLatestOnly = Config.Bind("07.ImageSlideshow", "ImageSlideshowLatestOnly", _settings.ImageSlideshowLatestOnly, "常に最新画像のみ表示");
            _cfgImageSlideshowTileMode = Config.Bind(
                "07.ImageSlideshow",
                "ImageSlideshowTileMode",
                NormalizeString(_settings.ImageSlideshowTileMode),
                new ConfigDescription("表示モード", new AcceptableValueList<string>("single", "tile")));
            _cfgImageSlideshowTransitionMode = Config.Bind(
                "07.ImageSlideshow",
                "ImageSlideshowTransitionMode",
                NormalizeString(_settings.ImageSlideshowTransitionMode),
                new ConfigDescription("トランジション方式", new AcceptableValueList<string>("None", "Fade", "CrossFade")));
            _cfgImageSlideshowTransitionSeconds = Config.Bind(
                "07.ImageSlideshow",
                "ImageSlideshowTransitionSeconds",
                _settings.ImageSlideshowTransitionSeconds,
                new ConfigDescription("トランジション時間（秒）", new AcceptableValueRange<float>(0.01f, 10f)));
            _cfgImageSlideshowVerticalFocus = Config.Bind(
                "07.ImageSlideshow",
                "ImageSlideshowVerticalFocus",
                NormalizeString(_settings.ImageSlideshowVerticalFocus),
                new ConfigDescription("縦長画像の表示基準", new AcceptableValueList<string>("Top", "Center", "Bottom")));
            _cfgImageSlideshowHorizontalFocus = Config.Bind(
                "07.ImageSlideshow",
                "ImageSlideshowHorizontalFocus",
                NormalizeString(_settings.ImageSlideshowHorizontalFocus),
                new ConfigDescription("横長画像の表示基準", new AcceptableValueList<string>("Left", "Center", "Right")));
            _cfgImageSlideshowPanMode = Config.Bind(
                "07.ImageSlideshow",
                "ImageSlideshowPanMode",
                NormalizeString(_settings.ImageSlideshowPanMode),
                new ConfigDescription("表示時間内のパン方向", new AcceptableValueList<string>("None", "TopToBottom", "BottomToTop", "LeftToRight", "RightToLeft")));

            _cfgVerboseLog = Config.Bind("99.Debug", "VerboseLog", _settings.VerboseLog, "詳細ログ");
            _cfgEnableAudioDiagnosticsLog = Config.Bind(
                "99.Debug",
                "EnableAudioDiagnosticsLog",
                _settings.EnableAudioDiagnosticsLog,
                "audio-snap診断ログ（通常は無効）");

            RegisterConfigChanged(_cfgAddedMapNo);
            RegisterConfigChanged(_cfgSourceMapNo);
            RegisterConfigChanged(_cfgAddedMapName);
            RegisterConfigChanged(_cfgAddedDisplayName);
            RegisterConfigChanged(_cfgAddedSort);
            RegisterConfigChanged(_cfgForceIsGate);
            RegisterConfigChanged(_cfgForceIsFreeH);
            RegisterConfigChanged(_cfgForceIsH);
            RegisterConfigChanged(_cfgAddedThumbnailID);

            RegisterConfigChanged(_cfgBlankifySceneOnLoad);
            RegisterConfigChanged(_cfgDisableRenderers);
            RegisterConfigChanged(_cfgDisableTerrains);
            RegisterConfigChanged(_cfgDisableLights);
            RegisterConfigChanged(_cfgDisableParticles);
            RegisterConfigChanged(_cfgDisableAudioSources);

            RegisterConfigChanged(_cfgEnableVideoRoom);
            RegisterConfigChanged(_cfgVideoPath);
            RegisterConfigChanged(_cfgUseFloorVideoOverride);
            RegisterConfigChanged(_cfgFloorOverrideVideoPath);
            RegisterConfigChanged(_cfgUseCeilingVideoOverride);
            RegisterConfigChanged(_cfgCeilingOverrideVideoPath);
            RegisterConfigChanged(_cfgVideoLoop);
            RegisterConfigChanged(_cfgMuteVideoAudio);
            RegisterConfigChanged(_cfgAutoPlayOnMapLoad);
            RegisterConfigChanged(_cfgVideoVolume);
            RegisterConfigChanged(_cfgVideoAudioGain);
            RegisterConfigChanged(_cfgApplyReverbToVideoAudio);
            RegisterConfigChanged(_cfgEnablePlaybackBar);
            RegisterConfigChanged(_cfgEnableUiHelpPopup);
            RegisterConfigChanged(_cfgPlaybackBarShowMouseBottomPx);
            RegisterConfigChanged(_cfgPlaybackBarHeight);
            RegisterConfigChanged(_cfgPlaybackBarMarginX);
            RegisterConfigChanged(_cfgPlaybackBarButtonWidth);
            RegisterConfigChanged(_cfgCubeFaceTileCount);

            _cfgBrowseFloorOverrideVideoPath.SettingChanged += OnBrowseFloorOverrideVideoPathChanged;
            _cfgBrowseCeilingOverrideVideoPath.SettingChanged += OnBrowseCeilingOverrideVideoPathChanged;

            RegisterConfigChanged(_cfgFolderPlayPath);
            RegisterConfigChanged(_cfgFolderPlayLoop);
            RegisterConfigChanged(_cfgFolderPlaySingleLoop);
            RegisterConfigChanged(_cfgFolderPlaySortMode);
            RegisterConfigChanged(_cfgFolderPlaySortAscending);
            RegisterConfigChanged(_cfgFolderFadeDuration);
            _cfgBrowseFolderPlayPath.SettingChanged += OnBrowseFolderPlayPathChanged;
            _cfgFolderPlayScan.SettingChanged += OnFolderPlayScanChanged;
            RegisterConfigChanged(_cfgSyncVoiceSourcesToVideoRoom);

            RegisterConfigChanged(_cfgRoomWidth);
            RegisterConfigChanged(_cfgRoomDepth);
            RegisterConfigChanged(_cfgRoomHeight);
            RegisterConfigChanged(_cfgVideoRoomOffsetX);
            RegisterConfigChanged(_cfgVideoRoomOffsetY);
            RegisterConfigChanged(_cfgVideoRoomOffsetZ);
            RegisterConfigChanged(_cfgVideoRoomRotationX);
            RegisterConfigChanged(_cfgVideoRoomRotationY);
            RegisterConfigChanged(_cfgVideoRoomRotationZ);

            RegisterConfigChanged(_cfgUseSphere);
            RegisterConfigChanged(_cfgSphereRadius);
            RegisterConfigChanged(_cfgSphereInsideView);

            RegisterConfigChanged(_cfgEnableVoiceReverb);
            RegisterConfigChanged(_cfgVoiceReverbPreset);
            RegisterConfigChanged(_cfgImageSlideshowEnabled);
            RegisterConfigChanged(_cfgImageSlideshowFolderPath);
            RegisterConfigChanged(_cfgImageSlideshowSeconds);
            RegisterConfigChanged(_cfgImageSlideshowScanIntervalSec);
            RegisterConfigChanged(_cfgImageSlideshowSortMode);
            RegisterConfigChanged(_cfgImageSlideshowSortAscending);
            RegisterConfigChanged(_cfgImageSlideshowOpacity);
            RegisterConfigChanged(_cfgImageSlideshowFitMode);
            RegisterConfigChanged(_cfgImageSlideshowTargetSurface);
            RegisterConfigChanged(_cfgImageSlideshowLatestOnly);
            RegisterConfigChanged(_cfgImageSlideshowTileMode);
            RegisterConfigChanged(_cfgImageSlideshowTransitionMode);
            RegisterConfigChanged(_cfgImageSlideshowTransitionSeconds);
            RegisterConfigChanged(_cfgImageSlideshowVerticalFocus);
            RegisterConfigChanged(_cfgImageSlideshowHorizontalFocus);
            RegisterConfigChanged(_cfgImageSlideshowPanMode);

            RegisterConfigChanged(_cfgVerboseLog);
            RegisterConfigChanged(_cfgEnableAudioDiagnosticsLog);

            // 廃止済みキーをcfgから削除して即保存
            Config.Remove(new ConfigDefinition("06.Reverb", "VoiceReverbMinDistance"));
            Config.Remove(new ConfigDefinition("06.Reverb", "VoiceReverbMaxDistance"));
            Config.Save();

            // JSONを正として起動時にcfgへ反映する
            SyncConfigEntriesFromSettings();
            ApplyConfigToSettings(reason: "init");
        }

        private void ApplyConfigChangesIfNeeded()
        {
            if (!_configDirty) return;
            _configDirty = false;

            string currentSignature = BuildConfigSignature();
            if (string.Equals(currentSignature, _lastAppliedConfigSignature, StringComparison.Ordinal))
                return;

            SettingsSnapshot before = SettingsSnapshot.Capture(_settings);
            ApplyConfigToSettings(reason: "changed");
            SettingsSnapshot after = SettingsSnapshot.Capture(_settings);
            SettingsChangePlan plan = SettingsChangePlanner.Create(before, after);
            if (!plan.RequiresRoomRebuild)
            {
                LogInfo(
                    $"config applied without room rebuild fields={string.Join(",", plan.ChangedFields)} " +
                    $"reverbOnly={plan.IsReverbOnly}");
                return;
            }

            RebuildRoomAfterSettingsChange("changed");
        }

        private void ApplyConfigToSettings(string reason)
        {
            _settings.AddedMapNo = _cfgAddedMapNo.Value;
            _settings.SourceMapNo = _cfgSourceMapNo.Value;
            _settings.AddedMapName = NormalizeString(_cfgAddedMapName.Value);
            _settings.AddedDisplayName = NormalizeString(_cfgAddedDisplayName.Value);
            _settings.AddedSort = _cfgAddedSort.Value;
            _settings.ForceIsGate = _cfgForceIsGate.Value;
            _settings.ForceIsFreeH = _cfgForceIsFreeH.Value;
            _settings.ForceIsH = _cfgForceIsH.Value;
            _settings.AddedThumbnailID = _cfgAddedThumbnailID.Value;

            _settings.BlankifySceneOnLoad = _cfgBlankifySceneOnLoad.Value;
            _settings.DisableRenderers = _cfgDisableRenderers.Value;
            _settings.DisableTerrains = _cfgDisableTerrains.Value;
            _settings.DisableLights = _cfgDisableLights.Value;
            _settings.DisableParticles = _cfgDisableParticles.Value;
            _settings.DisableAudioSources = _cfgDisableAudioSources.Value;

            _settings.EnableVideoRoom = _cfgEnableVideoRoom.Value;
            _settings.VideoPath = NormalizeVideoPathInput(NormalizeString(_cfgVideoPath.Value));
            _settings.UseFloorVideoOverride = _cfgUseFloorVideoOverride.Value;
            _settings.FloorOverrideVideoPath = NormalizeVideoPathInput(NormalizeString(_cfgFloorOverrideVideoPath.Value));
            _settings.UseCeilingVideoOverride = _cfgUseCeilingVideoOverride.Value;
            _settings.CeilingOverrideVideoPath = NormalizeVideoPathInput(NormalizeString(_cfgCeilingOverrideVideoPath.Value));
            _settings.VideoLoop = _cfgVideoLoop.Value;
            _settings.MuteVideoAudio = _cfgMuteVideoAudio.Value;
            _settings.AutoPlayOnMapLoad = _cfgAutoPlayOnMapLoad.Value;
            _settings.VideoVolume = _cfgVideoVolume.Value;
            _settings.VideoAudioGain = Mathf.Clamp(_cfgVideoAudioGain.Value, 0.1f, 6f);
            _settings.ApplyReverbToVideoAudio = _cfgApplyReverbToVideoAudio.Value;
            _settings.EnablePlaybackBar = _cfgEnablePlaybackBar.Value;
            _settings.EnableUiHelpPopup = _cfgEnableUiHelpPopup.Value;
            _settings.PlaybackBarShowMouseBottomPx = _cfgPlaybackBarShowMouseBottomPx.Value;
            _settings.PlaybackBarHeight = _cfgPlaybackBarHeight.Value;
            _settings.PlaybackBarMarginX = _cfgPlaybackBarMarginX.Value;
            _settings.PlaybackBarButtonWidth = _cfgPlaybackBarButtonWidth.Value;
            _settings.CubeFaceTileCount = _cfgCubeFaceTileCount.Value == 4 ||
                                          _cfgCubeFaceTileCount.Value == 9 ||
                                          _cfgCubeFaceTileCount.Value == 16 ||
                                          _cfgCubeFaceTileCount.Value == 25
                ? _cfgCubeFaceTileCount.Value
                : 1;

            _settings.RoomWidth = _cfgRoomWidth.Value;
            _settings.RoomDepth = _cfgRoomDepth.Value;
            _settings.RoomHeight = _cfgRoomHeight.Value;
            _settings.VideoRoomOffsetX = _cfgVideoRoomOffsetX.Value;
            _settings.VideoRoomOffsetY = _cfgVideoRoomOffsetY.Value;
            _settings.VideoRoomOffsetZ = _cfgVideoRoomOffsetZ.Value;
            _settings.VideoRoomRotationX = _cfgVideoRoomRotationX.Value;
            _settings.VideoRoomRotationY = _cfgVideoRoomRotationY.Value;
            _settings.VideoRoomRotationZ = _cfgVideoRoomRotationZ.Value;

            _settings.UseSphere = _cfgUseSphere.Value;
            _settings.SphereRadius = _cfgSphereRadius.Value;
            _settings.SphereInsideView = _cfgSphereInsideView.Value;

            _settings.EnableVoiceReverb = _cfgEnableVoiceReverb.Value;
            _settings.VoiceReverbPreset = NormalizeString(_cfgVoiceReverbPreset.Value);
            ApplyGlobalReverbState($"config:{reason}", persistConfig: true);
            _settings.ImageSlideshowEnabled = _cfgImageSlideshowEnabled.Value;
            _settings.ImageSlideshowFolderPath = NormalizeVideoPathInput(NormalizeString(_cfgImageSlideshowFolderPath.Value));
            _settings.ImageSlideshowSeconds = _cfgImageSlideshowSeconds.Value;
            _settings.ImageSlideshowScanIntervalSec = _cfgImageSlideshowScanIntervalSec.Value;
            _settings.ImageSlideshowSortMode = NormalizeString(_cfgImageSlideshowSortMode.Value);
            _settings.ImageSlideshowSortAscending = _cfgImageSlideshowSortAscending.Value;
            _settings.ImageSlideshowOpacity = _cfgImageSlideshowOpacity.Value;
            _settings.ImageSlideshowFitMode = NormalizeString(_cfgImageSlideshowFitMode.Value);
            _settings.ImageSlideshowTargetSurface = NormalizeString(_cfgImageSlideshowTargetSurface.Value);
            _settings.ImageSlideshowLatestOnly = _cfgImageSlideshowLatestOnly.Value;
            _settings.ImageSlideshowTileMode = NormalizeString(_cfgImageSlideshowTileMode.Value);
            _settings.ImageSlideshowTransitionMode = NormalizeString(_cfgImageSlideshowTransitionMode.Value);
            _settings.ImageSlideshowTransitionSeconds = _cfgImageSlideshowTransitionSeconds.Value;
            _settings.ImageSlideshowVerticalFocus = NormalizeString(_cfgImageSlideshowVerticalFocus.Value);
            _settings.ImageSlideshowHorizontalFocus = NormalizeString(_cfgImageSlideshowHorizontalFocus.Value);
            _settings.ImageSlideshowPanMode = NormalizeString(_cfgImageSlideshowPanMode.Value);

            _settings.FolderPlayEnabled = true;
            _settings.FolderPlayPath = NormalizeVideoPathInput(NormalizeString(_cfgFolderPlayPath.Value));
            _settings.FolderPlayLoop = _cfgFolderPlayLoop.Value;
            _settings.FolderPlaySingleLoop = _cfgFolderPlaySingleLoop.Value;
            _settings.FolderPlaySortMode = NormalizeString(_cfgFolderPlaySortMode.Value);
            _settings.FolderPlaySortAscending = _cfgFolderPlaySortAscending.Value;
            _settings.FolderFadeDuration = _cfgFolderFadeDuration.Value;
            _settings.HttpEnabled = _cfgHttpEnabled.Value;
            _settings.HttpPort = _cfgHttpPort.Value;
            _settings.SyncVoiceSourcesToVideoRoom = _cfgSyncVoiceSourcesToVideoRoom.Value;

            _settings.VerboseLog = _cfgVerboseLog.Value;
            _settings.EnableAudioDiagnosticsLog = _cfgEnableAudioDiagnosticsLog.Value;

            // フォルダ再生専用: 現在のフォルダエントリで VideoPath を上書き
            ApplyFolderVideoPath();

            var settingsPath = Path.Combine(_pluginDir, "MapAddSettings.json");
            _settingsRepository.SaveCurrent(_settings);
            _lastAppliedConfigSignature = BuildConfigSignature();

            LogInfo($"config applied reason={reason} mapNo={_settings.AddedMapNo} video={_settings.VideoPath}");

        }

        private void RebuildRoomAfterSettingsChange(string reason)
        {
            if (LastReservedMap == null || LastReservedMap.mapRoot == null)
            {
                LogInfo("config applied (map not ready)");
                return;
            }

            ForceReapplyCurrentMap();
            LogInfo($"config applied + map refreshed reason={reason} mapNo={LastReservedMap.no}");
        }

        private void SyncConfigEntriesFromSettings()
        {
            if (_syncingConfig) return;

            _syncingConfig = true;
            try
            {
                _cfgAddedMapNo.Value = _settings.AddedMapNo;
                _cfgSourceMapNo.Value = _settings.SourceMapNo;
                _cfgAddedMapName.Value = NormalizeString(_settings.AddedMapName);
                _cfgAddedDisplayName.Value = NormalizeString(_settings.AddedDisplayName);
                _cfgAddedSort.Value = _settings.AddedSort;
                _cfgForceIsGate.Value = _settings.ForceIsGate;
                _cfgForceIsFreeH.Value = _settings.ForceIsFreeH;
                _cfgForceIsH.Value = _settings.ForceIsH;
                _cfgAddedThumbnailID.Value = _settings.AddedThumbnailID;

                _cfgBlankifySceneOnLoad.Value = _settings.BlankifySceneOnLoad;
                _cfgDisableRenderers.Value = _settings.DisableRenderers;
                _cfgDisableTerrains.Value = _settings.DisableTerrains;
                _cfgDisableLights.Value = _settings.DisableLights;
                _cfgDisableParticles.Value = _settings.DisableParticles;
                _cfgDisableAudioSources.Value = _settings.DisableAudioSources;

                _cfgEnableVideoRoom.Value = _settings.EnableVideoRoom;
                _cfgVideoPath.Value = NormalizeVideoPathInput(NormalizeString(_settings.VideoPath));
                _cfgUseFloorVideoOverride.Value = _settings.UseFloorVideoOverride;
                _cfgFloorOverrideVideoPath.Value = NormalizeVideoPathInput(NormalizeString(_settings.FloorOverrideVideoPath));
                _cfgUseCeilingVideoOverride.Value = _settings.UseCeilingVideoOverride;
                _cfgCeilingOverrideVideoPath.Value = NormalizeVideoPathInput(NormalizeString(_settings.CeilingOverrideVideoPath));
                _cfgVideoLoop.Value = _settings.VideoLoop;
                _cfgMuteVideoAudio.Value = _settings.MuteVideoAudio;
                _cfgAutoPlayOnMapLoad.Value = _settings.AutoPlayOnMapLoad;
                _cfgVideoVolume.Value = _settings.VideoVolume;
                _cfgVideoAudioGain.Value = _settings.VideoAudioGain;
                _cfgApplyReverbToVideoAudio.Value = _settings.ApplyReverbToVideoAudio;
                _cfgEnablePlaybackBar.Value = _settings.EnablePlaybackBar;
                _cfgEnableUiHelpPopup.Value = _settings.EnableUiHelpPopup;
                _cfgPlaybackBarShowMouseBottomPx.Value = _settings.PlaybackBarShowMouseBottomPx;
                _cfgPlaybackBarHeight.Value = _settings.PlaybackBarHeight;
                _cfgPlaybackBarMarginX.Value = _settings.PlaybackBarMarginX;
                _cfgPlaybackBarButtonWidth.Value = _settings.PlaybackBarButtonWidth;
                _cfgCubeFaceTileCount.Value = _settings.CubeFaceTileCount == 4 ||
                                              _settings.CubeFaceTileCount == 9 ||
                                              _settings.CubeFaceTileCount == 16 ||
                                              _settings.CubeFaceTileCount == 25
                    ? _settings.CubeFaceTileCount
                    : 1;

                _cfgRoomWidth.Value = _settings.RoomWidth;
                _cfgRoomDepth.Value = _settings.RoomDepth;
                _cfgRoomHeight.Value = _settings.RoomHeight;
                _cfgVideoRoomOffsetX.Value = _settings.VideoRoomOffsetX;
                _cfgVideoRoomOffsetY.Value = _settings.VideoRoomOffsetY;
                _cfgVideoRoomOffsetZ.Value = _settings.VideoRoomOffsetZ;
                _cfgVideoRoomRotationX.Value = _settings.VideoRoomRotationX;
                _cfgVideoRoomRotationY.Value = _settings.VideoRoomRotationY;
                _cfgVideoRoomRotationZ.Value = _settings.VideoRoomRotationZ;

                _cfgUseSphere.Value = _settings.UseSphere;
                _cfgSphereRadius.Value = _settings.SphereRadius;
                _cfgSphereInsideView.Value = _settings.SphereInsideView;

                _cfgEnableVoiceReverb.Value = _settings.EnableVoiceReverb;
                _cfgVoiceReverbPreset.Value = NormalizeString(_settings.VoiceReverbPreset);
                _cfgImageSlideshowEnabled.Value = _settings.ImageSlideshowEnabled;
                _cfgImageSlideshowFolderPath.Value = NormalizeVideoPathInput(NormalizeString(_settings.ImageSlideshowFolderPath));
                _cfgImageSlideshowSeconds.Value = _settings.ImageSlideshowSeconds;
                _cfgImageSlideshowScanIntervalSec.Value = _settings.ImageSlideshowScanIntervalSec;
                _cfgImageSlideshowSortMode.Value = NormalizeString(_settings.ImageSlideshowSortMode);
                _cfgImageSlideshowSortAscending.Value = _settings.ImageSlideshowSortAscending;
                _cfgImageSlideshowOpacity.Value = _settings.ImageSlideshowOpacity;
                _cfgImageSlideshowFitMode.Value = NormalizeString(_settings.ImageSlideshowFitMode);
                _cfgImageSlideshowTargetSurface.Value = NormalizeString(_settings.ImageSlideshowTargetSurface);
                _cfgImageSlideshowLatestOnly.Value = _settings.ImageSlideshowLatestOnly;
                _cfgImageSlideshowTileMode.Value = NormalizeString(_settings.ImageSlideshowTileMode);
                _cfgImageSlideshowTransitionMode.Value = NormalizeString(_settings.ImageSlideshowTransitionMode);
                _cfgImageSlideshowTransitionSeconds.Value = _settings.ImageSlideshowTransitionSeconds;
                _cfgImageSlideshowVerticalFocus.Value = NormalizeString(_settings.ImageSlideshowVerticalFocus);
                _cfgImageSlideshowHorizontalFocus.Value = NormalizeString(_settings.ImageSlideshowHorizontalFocus);
                _cfgImageSlideshowPanMode.Value = NormalizeString(_settings.ImageSlideshowPanMode);

                _cfgFolderPlayPath.Value = NormalizeVideoPathInput(NormalizeString(_settings.FolderPlayPath));
                _cfgFolderPlayLoop.Value = _settings.FolderPlayLoop;
                _cfgFolderPlaySingleLoop.Value = _settings.FolderPlaySingleLoop;
                _cfgFolderPlaySortMode.Value = NormalizeString(_settings.FolderPlaySortMode);
                _cfgFolderPlaySortAscending.Value = _settings.FolderPlaySortAscending;
                _cfgFolderFadeDuration.Value = _settings.FolderFadeDuration;
                _cfgHttpEnabled.Value = _settings.HttpEnabled;
                _cfgHttpPort.Value = _settings.HttpPort;
                _cfgSyncVoiceSourcesToVideoRoom.Value = _settings.SyncVoiceSourcesToVideoRoom;

                _cfgUseWebCam.Value = IsWebCamUrl(_settings.VideoPath);
                _cfgWebCamDevice.Value = IsWebCamUrl(_settings.VideoPath)
                    ? ExtractWebCamDeviceName(_settings.VideoPath)
                    : _cfgWebCamDevice.Value;
                _cfgVerboseLog.Value = _settings.VerboseLog;
                _cfgEnableAudioDiagnosticsLog.Value = _settings.EnableAudioDiagnosticsLog;
            }
            finally
            {
                _syncingConfig = false;
                _configDirty = false;
            }
        }

        private void OnPresetNameChanged(object sender, EventArgs e)
        {
            if (_syncingConfig) return;
            string name = NormalizeString(_cfgPresetName?.Value);
            LogInfo($"preset selected name={name}");
            if (string.IsNullOrEmpty(name) || string.Equals(name, PresetNone, StringComparison.Ordinal))
            {
                LogInfo("preset selection is none");
                return;
            }

            if (!TryLoadPresetByName(name, out var loaded, out _, out var error))
            {
                LogWarn($"preset selection failed name={name} error={error}");
                return;
            }

            LogInfo($"preset selected name={name} video={NormalizeVideoPathInput(NormalizeString(loaded.VideoPath))}");
        }

        private void OnSaveCurrentStateChanged(object sender, EventArgs e)
        {
            if (_syncingConfig || _cfgSaveCurrentState == null) return;
            if (!_cfgSaveCurrentState.Value) return;

            try
            {
                SaveCurrentStateSnapshot("config-trigger");
            }
            finally
            {
                ResetPresetTriggerEntry(_cfgSaveCurrentState);
            }
        }

        private void OnUseWebCamChanged(object sender, EventArgs e)
        {
            if (_syncingConfig) return;
            _syncingConfig = true;
            try
            {
                if (_cfgUseWebCam.Value)
                {
                    string device = NormalizeString(_cfgWebCamDevice.Value);
                    _cfgVideoPath.Value = "webcam://" + device;
                }
                else if (IsWebCamUrl(_cfgVideoPath.Value))
                {
                    _cfgVideoPath.Value = string.Empty;
                }
            }
            finally
            {
                _syncingConfig = false;
            }
            _configDirty = true;
        }

        private void OnWebCamDeviceChanged(object sender, EventArgs e)
        {
            if (_syncingConfig) return;
            if (!_cfgUseWebCam.Value) return;
            _syncingConfig = true;
            try
            {
                string device = NormalizeString(_cfgWebCamDevice.Value);
                _cfgVideoPath.Value = "webcam://" + device;
            }
            finally
            {
                _syncingConfig = false;
            }
            _configDirty = true;
        }

        private static string[] BuildWebCamDeviceList()
        {
            try
            {
                var devices = WebCamTexture.devices;
                if (devices == null || devices.Length == 0)
                    return new[] { string.Empty };
                var names = new string[devices.Length];
                for (int i = 0; i < devices.Length; i++)
                    names[i] = devices[i].name;
                return names;
            }
            catch
            {
                return new[] { string.Empty };
            }
        }

        private void OnDeleteSelectedPresetChanged(object sender, EventArgs e)
        {
            if (_syncingConfig || _cfgDeleteSelectedPreset == null) return;
            if (!_cfgDeleteSelectedPreset.Value) return;

            try
            {
                DeleteSelectedPreset();
            }
            finally
            {
                ResetPresetTriggerEntry(_cfgDeleteSelectedPreset);
            }
        }

        private void OnBrowseFolderPlayPathChanged(object sender, EventArgs e)
        {
            if (_syncingConfig || _cfgBrowseFolderPlayPath == null) return;
            if (!_cfgBrowseFolderPlayPath.Value) return;
            try
            {
                string current = NormalizeString(_cfgFolderPlayPath?.Value);
                if (TryOpenFolderDialog(current, out string selected, out string error))
                {
                    if (!string.IsNullOrWhiteSpace(selected))
                    {
                        _cfgFolderPlayPath.Value = NormalizeVideoPathInput(selected);
                        _configDirty = true;
                    }
                }
                else if (!string.IsNullOrWhiteSpace(error))
                {
                    LogWarn($"folder browse failed: {error}");
                }
            }
            finally
            {
                ResetPresetTriggerEntry(_cfgBrowseFolderPlayPath);
            }
        }

        private void OnFolderPlayScanChanged(object sender, EventArgs e)
        {
            if (_syncingConfig || _cfgFolderPlayScan == null) return;
            if (!_cfgFolderPlayScan.Value) return;
            try
            {
                ForceFolderRescan();
                LogInfo("folder play: manual rescan triggered");
            }
            finally
            {
                ResetPresetTriggerEntry(_cfgFolderPlayScan);
            }
        }

        private void DrawBrowseFolderPlayPathButtonCustomDrawer(ConfigEntryBase entryBase)
        {
            if (GUILayout.Button("参照", GUILayout.MinWidth(72f)))
                _cfgBrowseFolderPlayPath.Value = true;
        }

        private void DrawFolderPlayScanButtonCustomDrawer(ConfigEntryBase entryBase)
        {
            if (GUILayout.Button("Scan", GUILayout.MinWidth(72f)))
                _cfgFolderPlayScan.Value = true;
        }

        private bool TryOpenFolderDialog(string currentPath, out string selectedPath, out string error)
        {
            selectedPath = null;
            error = null;
            string selected = null;
            string localError = null;
            bool opened = false;
            using (var done = new ManualResetEventSlim(false))
            {
                var thread = new Thread(() =>
                {
                    try { opened = TryOpenFolderDialogShell(currentPath, out selected, out localError); }
                    catch (Exception ex) { localError = ex.Message; }
                    finally { done.Set(); }
                });
                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                done.Wait();
            }
            if (!opened) { error = localError; return false; }
            selectedPath = selected;
            return true;
        }

        private bool TryOpenFolderDialogShell(string currentPath, out string selectedPath, out string error)
        {
            selectedPath = null;
            error = null;
            IFileOpenDialog dialog = null;
            IShellItem folderItem = null;
            IShellItem resultItem = null;
            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialogComObject();
                dialog.GetOptions(out uint options);
                options |= (uint)(FileDialogOptions.PickFolders | FileDialogOptions.ForceFileSystem | FileDialogOptions.NoChangeDir);
                dialog.SetOptions(options);
                dialog.SetTitle("動画フォルダを選択");
                string startDir = !string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath)
                    ? currentPath : null;
                if (!string.IsNullOrWhiteSpace(startDir) && TryCreateShellItem(startDir, out folderItem))
                {
                    dialog.SetDefaultFolder(folderItem);
                    dialog.SetFolder(folderItem);
                }
                int hr = dialog.Show(IntPtr.Zero);
                if (hr == HResultCanceled) return false;
                if (hr < 0) { error = $"folder dialog hr=0x{hr:X8}"; return false; }
                dialog.GetResult(out resultItem);
                if (!TryGetShellItemPath(resultItem, out selectedPath))
                {
                    error = "folder path resolve failed";
                    return false;
                }
                return !string.IsNullOrWhiteSpace(selectedPath);
            }
            catch (COMException ex) when (ex.HResult == HResultCanceled) { return false; }
            catch (Exception ex) { error = ex.Message; return false; }
            finally
            {
                ReleaseComObject(resultItem);
                ReleaseComObject(folderItem);
                ReleaseComObject(dialog);
            }
        }

        private void DrawSaveCurrentStateButtonCustomDrawer(ConfigEntryBase entryBase)
        {
            var entry = entryBase as ConfigEntry<bool>;
            if (entry == null) return;

            if (GUILayout.Button("SAVE", GUILayout.MinWidth(72f)))
                entry.Value = true;
        }

        private void OnBrowseFloorOverrideVideoPathChanged(object sender, EventArgs e)
        {
            HandleVideoPathBrowseTrigger(
                _cfgBrowseFloorOverrideVideoPath,
                _cfgFloorOverrideVideoPath,
                "floor",
                _cfgUseFloorVideoOverride,
                markDirty: true);
        }

        private void OnBrowseCeilingOverrideVideoPathChanged(object sender, EventArgs e)
        {
            HandleVideoPathBrowseTrigger(
                _cfgBrowseCeilingOverrideVideoPath,
                _cfgCeilingOverrideVideoPath,
                "ceiling",
                _cfgUseCeilingVideoOverride,
                markDirty: true);
        }

        private void HandleVideoPathBrowseTrigger(
            ConfigEntry<bool> triggerEntry,
            ConfigEntry<string> targetPathEntry,
            string targetLabel,
            ConfigEntry<bool> enableOverrideEntry,
            bool markDirty)
        {
            if (_syncingConfig || triggerEntry == null || targetPathEntry == null) return;
            if (!triggerEntry.Value) return;

            try
            {
                string currentPath = NormalizeString(targetPathEntry.Value);
                if (!TryOpenVideoFileDialog(currentPath, out string selectedPath, out string error))
                {
                    if (!string.IsNullOrWhiteSpace(error))
                        LogWarn($"video browse failed target={targetLabel} error={error}");
                    return;
                }

                if (string.IsNullOrWhiteSpace(selectedPath))
                    return;

                targetPathEntry.Value = NormalizeVideoPathInput(selectedPath);
                if (enableOverrideEntry != null)
                    enableOverrideEntry.Value = true;

                if (markDirty)
                    _configDirty = true;
                LogInfo($"video selected target={targetLabel} path={selectedPath}");
            }
            finally
            {
                ResetPresetTriggerEntry(triggerEntry);
            }
        }

        private bool TryOpenVideoFileDialog(
            string currentPath,
            out string selectedPath,
            out string error)
        {
            selectedPath = null;
            error = null;

            string selected = null;
            string localError = null;
            bool opened = false;

            using (var done = new ManualResetEventSlim(false))
            {
                var thread = new Thread(() =>
                {
                    try
                    {
                        opened = TryOpenVideoFileDialogShell(currentPath, out selected, out localError);
                    }
                    catch (Exception ex)
                    {
                        localError = ex.Message;
                    }
                    finally
                    {
                        done.Set();
                    }
                });

                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                done.Wait();
            }

            if (!opened)
            {
                error = localError;
                return false;
            }

            selectedPath = selected;
            return true;
        }

        private bool TryOpenVideoFileDialogShell(
            string currentPath,
            out string selectedPath,
            out string error)
        {
            selectedPath = null;
            error = null;

            IFileOpenDialog dialog = null;
            IShellItem folderItem = null;
            IShellItem resultItem = null;

            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialogComObject();

                dialog.GetOptions(out uint options);
                options |= (uint)(FileDialogOptions.ForceFileSystem |
                                  FileDialogOptions.PathMustExist |
                                  FileDialogOptions.FileMustExist |
                                  FileDialogOptions.NoChangeDir |
                                  FileDialogOptions.ForcePreviewPaneOn);
                dialog.SetOptions(options);

                var filters = new[]
                {
                    new FileDialogFilterSpec("Video Files", "*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.m4v"),
                    new FileDialogFilterSpec("All Files", "*.*")
                };
                dialog.SetFileTypes((uint)filters.Length, filters);
                dialog.SetFileTypeIndex(1);
                dialog.SetDefaultExtension("mp4");
                dialog.SetTitle("動画ファイルを選択");

                ResolveVideoDialogInitialSelection(currentPath, out string initialDir, out string fileName);
                if (!string.IsNullOrWhiteSpace(initialDir) && Directory.Exists(initialDir))
                {
                    if (TryCreateShellItem(initialDir, out folderItem))
                    {
                        dialog.SetDefaultFolder(folderItem);
                        dialog.SetFolder(folderItem);
                    }
                }

                if (!string.IsNullOrWhiteSpace(fileName))
                    dialog.SetFileName(fileName);

                int hr = dialog.Show(IntPtr.Zero);
                if (hr == HResultCanceled)
                    return false;
                if (hr < 0)
                {
                    error = $"shell dialog show failed hr=0x{hr:X8}";
                    return false;
                }

                dialog.GetResult(out resultItem);
                if (!TryGetShellItemPath(resultItem, out selectedPath))
                {
                    error = "shell dialog selected path resolve failed";
                    return false;
                }

                return !string.IsNullOrWhiteSpace(selectedPath);
            }
            catch (COMException ex)
            {
                if (ex.HResult == HResultCanceled)
                    return false;

                error = $"shell dialog failed: 0x{ex.HResult:X8} {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"shell dialog failed: {ex.Message}";
                return false;
            }
            finally
            {
                ReleaseComObject(resultItem);
                ReleaseComObject(folderItem);
                ReleaseComObject(dialog);
            }
        }

        private void ResolveVideoDialogInitialSelection(
            string currentPath,
            out string initialDir,
            out string fileName)
        {
            initialDir = null;
            fileName = null;

            string normalized = NormalizeVideoPathInput(currentPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            try
            {
                string fullPath = normalized;
                if (!Path.IsPathRooted(fullPath))
                    fullPath = Path.Combine(_pluginDir, fullPath);
                fullPath = Path.GetFullPath(fullPath);

                if (File.Exists(fullPath))
                {
                    initialDir = Path.GetDirectoryName(fullPath);
                    fileName = Path.GetFileName(fullPath);
                    return;
                }

                if (Directory.Exists(fullPath))
                {
                    initialDir = fullPath;
                    fileName = null;
                    return;
                }

                initialDir = Path.GetDirectoryName(fullPath);
                fileName = Path.GetFileName(fullPath);
            }
            catch
            {
            }
        }

        private void ResetPresetTriggerEntry(ConfigEntry<bool> entry)
        {
            if (entry == null) return;

            _syncingConfig = true;
            try
            {
                entry.Value = false;
            }
            finally
            {
                _syncingConfig = false;
            }
        }

        private bool TryLoadPresetByName(
            string presetName,
            out PluginSettings loaded,
            out string path,
            out string error)
        {
            loaded = null;
            path = null;
            error = null;

            string name = NormalizeString(presetName);
            if (string.IsNullOrEmpty(name) || string.Equals(name, PresetNone, StringComparison.Ordinal))
            {
                error = "preset name is empty";
                return false;
            }

            path = Path.Combine(_presetDir ?? string.Empty, name + ".json");
            if (!File.Exists(path))
            {
                error = $"preset not found path={path}";
                return false;
            }

            if (!_settingsRepository.TryLoadFromPath(path, out loaded, out error))
                return false;

            if (loaded != null)
                return true;

            error = "preset deserialize returned null";
            return false;
        }

        private void DeleteSelectedPreset()
        {
            string name = NormalizeString(_cfgPresetName?.Value);
            if (string.IsNullOrEmpty(name) || string.Equals(name, PresetNone, StringComparison.Ordinal))
            {
                LogWarn("preset delete skipped: PresetName is none");
                return;
            }

            if (string.Equals(name, "default", StringComparison.OrdinalIgnoreCase))
            {
                LogWarn("preset delete skipped: default is protected");
                return;
            }

            string path = Path.Combine(_presetDir ?? string.Empty, name + ".json");
            if (!File.Exists(path))
            {
                LogWarn($"preset delete skipped: file not found name={name} path={path}");
                return;
            }

            try
            {
                File.Delete(path);
                LogInfo($"preset deleted name={name} path={path}");
            }
            catch (Exception ex)
            {
                LogWarn($"preset delete failed name={name} error={ex.Message}");
                return;
            }

            RefreshPresetNameList(PresetNone);
        }

        private void ApplySelectedPreset()
        {
            string name = NormalizeString(_cfgPresetName?.Value);
            if (string.IsNullOrEmpty(name) || string.Equals(name, PresetNone, StringComparison.Ordinal))
            {
                LogWarn("preset apply requested but PresetName is not selected");
                return;
            }

            if (!TryLoadPresetByName(name, out var loaded, out var path, out var error))
            {
                LogWarn($"preset load failed name={name} path={path} error={error}");
                return;
            }

            _settings = loaded ?? new PluginSettings();
            _settingsRepository.SaveCurrent(_settings);
            SyncConfigEntriesFromSettings();
            LogInfo($"preset applied name={name} path={path}");

            if (LastReservedMap == null || LastReservedMap.mapRoot == null)
            {
                LogInfo("preset applied (map not ready)");
                return;
            }

            ForceReapplyCurrentMap();
            LogInfo($"preset applied + map refreshed mapNo={LastReservedMap.no}");
        }

        private string[] BuildPresetNameList()
        {
            var names = new List<string>();
            try
            {
                if (Directory.Exists(_presetDir))
                {
                    names.AddRange(
                        new DirectoryInfo(_presetDir)
                            .GetFiles("*.json", SearchOption.TopDirectoryOnly)
                            .OrderByDescending(f => f.LastWriteTime)
                            .Select(f => Path.GetFileNameWithoutExtension(f.Name))
                            .Where(x => !string.IsNullOrWhiteSpace(x)));
                }
            }
            catch (Exception ex)
            {
                LogWarn($"preset list scan failed: {ex.Message}");
            }

            var distinct = names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            distinct.Insert(0, PresetNone);
            return distinct.ToArray();
        }

        private void EnsureDefaultPresetIfMissing()
        {
            try
            {
                if (Directory.GetFiles(_presetDir, "*.json", SearchOption.TopDirectoryOnly).Length > 0)
                    return;

                string path = Path.Combine(_presetDir, "default.json");
                _settingsRepository.SaveToPath(path, _settings);
                LogInfo($"preset seed created path={path}");
            }
            catch (Exception ex)
            {
                LogWarn($"preset seed create failed: {ex.Message}");
            }
        }

        internal void RefreshPresetNameList(string preferredPresetName = null)
        {
            string[] presetNames = BuildPresetNameList();
            string preferred = NormalizeString(preferredPresetName);
            string selected = !string.IsNullOrEmpty(preferred) &&
                              presetNames.Contains(preferred, StringComparer.OrdinalIgnoreCase)
                ? preferred
                : NormalizeString(_cfgPresetName?.Value);
            if (string.IsNullOrEmpty(selected) ||
                !presetNames.Contains(selected, StringComparer.OrdinalIgnoreCase))
            {
                selected = PresetNone;
            }

            if (_cfgPresetName != null)
                _cfgPresetName.SettingChanged -= OnPresetNameChanged;

            _syncingConfig = true;
            try
            {
                if (_cfgPresetName != null)
                    Config.Remove(_cfgPresetName.Definition);

                _cfgPresetName = Config.Bind(
                    "00.Preset",
                    "PresetName",
                    selected,
                    new ConfigDescription(
                        "読み込み対象プリセット（MapAddPresets/*.json）",
                        new AcceptableValueList<string>(presetNames),
                        new ConfigurationManager.ConfigurationManagerAttributes
                        {
                            Order = 100
                        }));
                _cfgPresetName.Value = selected;
            }
            catch (Exception ex)
            {
                LogWarn($"preset list refresh failed: {ex.Message}");
                return;
            }
            finally
            {
                _syncingConfig = false;
            }

            _cfgPresetName.SettingChanged += OnPresetNameChanged;
            LogInfo($"preset list refreshed count={presetNames.Length}");
        }

        private static bool TryCreateShellItem(string path, out IShellItem item)
        {
            item = null;
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                Guid iidShellItem = typeof(IShellItem).GUID;
                SHCreateItemFromParsingName(path, IntPtr.Zero, ref iidShellItem, out item);
                return item != null;
            }
            catch
            {
                item = null;
                return false;
            }
        }

        private static bool TryGetShellItemPath(IShellItem item, out string path)
        {
            path = null;
            if (item == null)
                return false;

            IntPtr ptr = IntPtr.Zero;
            try
            {
                item.GetDisplayName(ShellItemDisplayName.FileSystemPath, out ptr);
                if (ptr == IntPtr.Zero)
                    return false;

                path = Marshal.PtrToStringUni(ptr);
                return !string.IsNullOrWhiteSpace(path);
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                    Marshal.FreeCoTaskMem(ptr);
            }
        }

        private static void ReleaseComObject(object instance)
        {
            if (instance == null)
                return;

            try
            {
                if (Marshal.IsComObject(instance))
                    Marshal.FinalReleaseComObject(instance);
            }
            catch
            {
            }
        }

        private void RegisterConfigChanged<T>(ConfigEntry<T> entry)
        {
            if (entry == null) return;
            entry.SettingChanged += OnConfigEntryChanged;
        }

        private void OnConfigEntryChanged(object sender, EventArgs e)
        {
            if (_syncingConfig) return;
            _configDirty = true;
        }

        private static string NormalizeString(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Trim();
        }

        private string BuildConfigSignature()
        {
            var sb = new System.Text.StringBuilder(1024);

            void AddBool(bool value) => sb.Append(value ? '1' : '0').Append('|');
            void AddInt(int value) => sb.Append(value).Append('|');
            void AddFloat(float value) => sb.Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append('|');
            void AddText(string value) => sb.Append(NormalizeString(value)).Append('|');

            AddInt(_cfgAddedMapNo?.Value ?? 0);
            AddInt(_cfgSourceMapNo?.Value ?? 0);
            AddText(_cfgAddedMapName?.Value);
            AddText(_cfgAddedDisplayName?.Value);
            AddInt(_cfgAddedSort?.Value ?? 0);
            AddBool(_cfgForceIsGate?.Value ?? false);
            AddBool(_cfgForceIsFreeH?.Value ?? false);
            AddBool(_cfgForceIsH?.Value ?? false);
            AddInt(_cfgAddedThumbnailID?.Value ?? 0);

            AddBool(_cfgBlankifySceneOnLoad?.Value ?? true);
            AddBool(_cfgDisableRenderers?.Value ?? true);
            AddBool(_cfgDisableTerrains?.Value ?? true);
            AddBool(_cfgDisableLights?.Value ?? true);
            AddBool(_cfgDisableParticles?.Value ?? true);
            AddBool(_cfgDisableAudioSources?.Value ?? true);

            AddBool(_cfgEnableVideoRoom?.Value ?? false);
            AddText(_cfgVideoPath?.Value);
            AddBool(_cfgUseFloorVideoOverride?.Value ?? false);
            AddText(_cfgFloorOverrideVideoPath?.Value);
            AddBool(_cfgUseCeilingVideoOverride?.Value ?? false);
            AddText(_cfgCeilingOverrideVideoPath?.Value);
            AddBool(_cfgVideoLoop?.Value ?? false);
            AddBool(_cfgMuteVideoAudio?.Value ?? false);
            AddBool(_cfgAutoPlayOnMapLoad?.Value ?? false);
            AddFloat(_cfgVideoVolume?.Value ?? 0f);
            AddFloat(_cfgVideoAudioGain?.Value ?? 1f);
            AddBool(_cfgApplyReverbToVideoAudio?.Value ?? false);
            AddBool(_cfgEnablePlaybackBar?.Value ?? false);
            AddBool(_cfgEnableUiHelpPopup?.Value ?? false);
            AddFloat(_cfgPlaybackBarShowMouseBottomPx?.Value ?? 0f);
            AddFloat(_cfgPlaybackBarHeight?.Value ?? 0f);
            AddFloat(_cfgPlaybackBarMarginX?.Value ?? 0f);
            AddFloat(_cfgPlaybackBarButtonWidth?.Value ?? 0f);
            AddInt(_cfgCubeFaceTileCount?.Value ?? 1);
            AddBool(_cfgUseWebCam?.Value ?? false);
            AddText(_cfgWebCamDevice?.Value);

            AddText(_cfgFolderPlayPath?.Value);
            AddBool(_cfgFolderPlayLoop?.Value ?? false);
            AddBool(_cfgFolderPlaySingleLoop?.Value ?? false);
            AddText(_cfgFolderPlaySortMode?.Value);
            AddBool(_cfgFolderPlaySortAscending?.Value ?? true);
            AddFloat(_cfgFolderFadeDuration?.Value ?? 0f);
            AddBool(_cfgHttpEnabled?.Value ?? false);
            AddInt(_cfgHttpPort?.Value ?? 0);
            AddBool(_cfgSyncVoiceSourcesToVideoRoom?.Value ?? false);

            AddFloat(_cfgRoomWidth?.Value ?? 0f);
            AddFloat(_cfgRoomDepth?.Value ?? 0f);
            AddFloat(_cfgRoomHeight?.Value ?? 0f);
            AddFloat(_cfgVideoRoomOffsetX?.Value ?? 0f);
            AddFloat(_cfgVideoRoomOffsetY?.Value ?? 0f);
            AddFloat(_cfgVideoRoomOffsetZ?.Value ?? 0f);
            AddFloat(_cfgVideoRoomRotationX?.Value ?? 0f);
            AddFloat(_cfgVideoRoomRotationY?.Value ?? 0f);
            AddFloat(_cfgVideoRoomRotationZ?.Value ?? 0f);

            AddBool(_cfgUseSphere?.Value ?? false);
            AddFloat(_cfgSphereRadius?.Value ?? 0f);
            AddBool(_cfgSphereInsideView?.Value ?? false);

            AddBool(_cfgEnableVoiceReverb?.Value ?? false);
            AddText(_cfgVoiceReverbPreset?.Value);
            AddBool(_cfgImageSlideshowEnabled?.Value ?? false);
            AddText(_cfgImageSlideshowFolderPath?.Value);
            AddFloat(_cfgImageSlideshowSeconds?.Value ?? 0f);
            AddFloat(_cfgImageSlideshowScanIntervalSec?.Value ?? 0f);
            AddText(_cfgImageSlideshowSortMode?.Value);
            AddBool(_cfgImageSlideshowSortAscending?.Value ?? false);
            AddFloat(_cfgImageSlideshowOpacity?.Value ?? 0f);
            AddText(_cfgImageSlideshowFitMode?.Value);
            AddText(_cfgImageSlideshowTargetSurface?.Value);
            AddBool(_cfgImageSlideshowLatestOnly?.Value ?? false);
            AddText(_cfgImageSlideshowTileMode?.Value);
            AddText(_cfgImageSlideshowTransitionMode?.Value);
            AddFloat(_cfgImageSlideshowTransitionSeconds?.Value ?? 0f);
            AddText(_cfgImageSlideshowVerticalFocus?.Value);
            AddText(_cfgImageSlideshowHorizontalFocus?.Value);
            AddText(_cfgImageSlideshowPanMode?.Value);
            AddBool(_cfgVerboseLog?.Value ?? false);
            AddBool(_cfgEnableAudioDiagnosticsLog?.Value ?? false);

            return sb.ToString();
        }

        private const int HResultCanceled = unchecked((int)0x800704C7);

        [Flags]
        private enum FileDialogOptions : uint
        {
            NoChangeDir = 0x00000008,
            PickFolders = 0x00000020,
            ForceFileSystem = 0x00000040,
            PathMustExist = 0x00000800,
            FileMustExist = 0x00001000,
            ForcePreviewPaneOn = 0x40000000
        }

        private enum ShellItemDisplayName : uint
        {
            FileSystemPath = 0x80058000
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileDialogFilterSpec
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Name;

            [MarshalAs(UnmanagedType.LPWStr)]
            public string Spec;

            public FileDialogFilterSpec(string name, string spec)
            {
                Name = name;
                Spec = spec;
            }
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialogComObject
        {
        }

        [ComImport]
        [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);
            void SetFileTypes(uint fileTypeCount, [MarshalAs(UnmanagedType.LPArray)] FileDialogFilterSpec[] filterSpec);
            void SetFileTypeIndex(uint fileTypeIndex);
            void GetFileTypeIndex(out uint fileTypeIndex);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem shellItem);
            void SetFolder(IShellItem shellItem);
            void GetFolder(out IShellItem shellItem);
            void GetCurrentSelection(out IShellItem shellItem);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem shellItem);
            void AddPlace(IShellItem shellItem, int alignment);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string defaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr filter);
            void GetResults(out IntPtr items);
            void GetSelectedItems(out IntPtr items);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(ShellItemDisplayName sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem shellItem);
    }
}
