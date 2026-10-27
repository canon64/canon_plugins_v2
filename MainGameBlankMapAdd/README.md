# MainGameBlankMapAdd

MainGameBlankMapAdd builds a video room on the MainGame blank map,
and provides playback-bar based control for video playback, room transform, audio gain, and profile saving.

## Environment
- Game: KoikatsuSunshine
- BepInEx: 5.x
- Process: `KoikatsuSunshine`

## Dependencies
- Required: `MainGameTransformGizmo.dll`
- Required in dependency chain: `MainGameLogRelay.dll` (required by TransformGizmo)
- Optional integration:
  - `MainGameBeatSyncSpeed.dll` (BEAT SYNC integration)
  - `MainGameSpeedLimitBreak.dll` (speed-control integration)
  - `MainGameAllPoseMap.dll` (videomap workflow support)

## Installation
1. Place `MainGameBlankMapAdd.dll` in `BepInEx/plugins/canon_plugins/MainGameBlankMapAdd/`.
2. Place required dependencies:
   - `BepInEx/plugins/canon_plugins/MainGameTransformGizmo/MainGameTransformGizmo.dll`
   - `BepInEx/plugins/canon_plugins/MainGameLogRelay/MainGameLogRelay.dll`
3. Start the game, enter MainGame/H scene, and open the videomap.

## Quick Start
1. Move the mouse to the bottom edge of the screen to show the playback bar.
2. Click `フォルダ登録` (Add Folder) and register your video folder.
3. Select folder in `Folder`, then select a file in `Video`.
4. Adjust room transform and audio while playing.
5. Save shared values with `SaveF` and per-video values with `SaveV`.

## Hotkeys
- `Ctrl+P`: Play/Pause video
- `Ctrl+R`: Reload settings
- `Ctrl+D`: Toggle Gizmo edit mode

## Playback Bar Guide

### Fold/Help area
- `▲ / ▼`: Expand/collapse top panel (room transform, AUDIO/SAVE, BEAT SYNC)
- `説明` checkbox: show/hide hover help
- `HipUI` checkbox (to the right of `説明`): toggles `MainGirlHipHijack` UI visibility
- `ClubUI` checkbox: toggles `MainGameClubLights` UI visibility
- `AfterImage` checkbox: enables/disables `SimpleAfterimage`
- `人形` checkbox: enables/disables `MainGameDollMode`
- `体位変更` checkbox: enables/disables pose-change control in `MainGameVoiceFaceEventBridge`
- `状況Auto` checkbox: enables/disables periodic scenario-text sending in `MainGameVoiceFaceEventBridge`
- `状況送信` button: sends the current scenario text once immediately through `MainGameVoiceFaceEventBridge`
- `AutoVoice` checkbox: toggles `MainGameAutoHVoice` UI visibility
- If `MainGirlHipHijack` is not loaded, this toggle is disabled
- Other linked toggles are also disabled when the target plugin is not loaded

### Bottom row (always visible)
- `Play / Pause / Stop`: playback controls
- `|< / >|`: previous/next video
- `1Loop`: loop current video
- `Loop`: wrap to first video at folder end
- `Tiles:n`: tile count switch
- `フォルダ登録`: add playable folder
- `Folder`: select registered folder
- `Video`: select video in folder
- Sliders:
  - `再生位置`: seek position
  - `VOL`: video volume
  - `REV`: reverb intensity
  - `V-REV`: apply reverb to video audio

### Top row (`▲` expanded)
- `SIZE / POSITION / ROTATION`: room scale, position, rotation
- Numeric input and slider operate the same value

#### AUDIO / SAVE
- `Gain`: video audio gain (0.1 to 6.0)
- `RoomF` / `RoomV`: save room layout
- `GainF` / `GainV`: save gain values

#### BEAT SYNC
- Adjust `Enabled / AutoMotion / AutoThreshold / VerboseLog` and related sliders
- Save folder/video values via `SaveF` / `SaveV`
- `AutoMotion / AutoThreshold / VerboseLog` are treated as global linked toggles through `MainGameBeatSyncSpeed`

## Save Behavior (Important)
- Saved files:
  - `MapAddSettings.json` (general settings)
  - `RoomLayoutProfiles.json` (folder/video profile values)
- Priority:
  - If a per-video value exists for a category, per-video wins.
  - Otherwise folder value is applied.
- Main categories:
  - Room layout
  - Video gain
  - BeatSync values

## Main Settings
- `MapAddSettings.json`
  - `EnablePlaybackBar`: enables/disables the playback bar itself
  - `EnableUiHelpPopup`: enables/disables hover help popups for the bar
  - `FolderPlayPath` / `FolderPlayPaths`: current folder-play selection and registered folders
  - `FolderPlayLoop` / `FolderPlaySingleLoop` / `FolderPlaySortMode`: folder playback behavior
  - `VideoAudioGain`: extra gain multiplier for video audio
  - `ApplyReverbToVideoAudio`: applies room reverb to video audio
  - `SyncVoiceSourcesToVideoRoom`: moves H voice sources to the video room position
  - `HttpEnabled` / `HttpPort`: external HTTP control endpoint
  - `WebCamRequestedWidth` / `WebCamRequestedHeight` / `WebCamRequestedFps`: requested webcam capture format
  - `WebCamStatusLogIntervalSec` / `WebCamBlackSampleIntervalSec`: webcam diagnostic log intervals

## Output Files
- `BepInEx/config/com.kks.maingameblankmapadd.cfg`
- `BepInEx/plugins/canon_plugins/MainGameBlankMapAdd/MapAddSettings.json`
- `BepInEx/plugins/canon_plugins/MainGameBlankMapAdd/RoomLayoutProfiles.json`
- `BepInEx/plugins/canon_plugins/MainGameBlankMapAdd/_logs/info.txt`
- `BepInEx/plugins/canon_plugins/MainGameBlankMapAdd/webcam_devices.txt`

## Troubleshooting
- Playback bar is not shown:
  - Confirm you are in MainGame/H scene.
  - Move the mouse to the bottom edge.
  - Check `EnablePlaybackBar` is not OFF.
- Video does not play:
  - Check folder registration and `Folder`/`Video` selection.
  - Confirm the folder includes supported video files.
- BEAT SYNC not working:
  - Check `MainGameBeatSyncSpeed.dll` placement and logs.
- Gizmo does not respond:
  - Check `MainGameTransformGizmo.dll` and `MainGameLogRelay.dll` placement.
