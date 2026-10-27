# MainGameCameraControl

## Overview
- A camera preset and transition plugin for main-game H scenes.
- It saves `CameraControl_Ver2` camera states as presets and lets you load them later.
- It can work with position, rotation, FOV, and bone-linked camera data.

## What It Does
- Save the current camera state as a preset
- Load saved presets with smooth interpolation
- Switch between normal, bone-linked, and `ksFPV` save modes
- Choose whether saved FOV should also be applied
- Adjust bone-link look-at offsets with a gizmo
- Enable beat-linked FOV zoom and change its multiplier
- Toggle UI visibility from `MainGameBlankMapAdd`
- Load presets from external APIs by name or index

## Basic Usage
1. Open the UI during an H scene.
2. Enter a name in `保存名`.
3. Choose a `保存モード`.
4. Press `現在を保存` to add a preset.
5. Press a button in the `保存済み` list to move to that preset.

## Main UI Items
- `FOVも適用`: Whether saved FOV is also applied when loading presets
- `詳細ログ`: Enables detailed logging
- `遷移時間`: Camera transition time
- `イージング`: `Linear / EaseIn / EaseOut / EaseInOut`
- `現在FOV`: Directly adjusts current FOV
- `拍FOVズーム`: Enables beat-linked FOV zoom
- `ズーム倍率`: Beat FOV zoom multiplier
- `保存モード`: `通常 / ボーン / ksFPV`
- `対象ボーン`: Reference bone target for bone-linked save mode
- `ギズモ表示`: Shows or hides the look-at gizmo
- `ギズモサイズ`: Gizmo size
- `現在値`: Shows current `Target / Dir / Rot / DataFov / LiveFov / PresetFov`
- `現在を保存`: Saves the current state as a new preset
- `上書き保存`: Overwrites the currently active bone-linked preset
- `解除`: Clears active bone-link mode
- `Reset`: Resets the current camera to its base state

## Save Modes
- `通常`: Saves the current `TargetPosition / CameraDirection / Rotation / Fov`
- `ボーン`: Saves look-at and camera offsets relative to the selected female bone
- `ksFPV`: Saves `ksFPV`-based follow data

## Settings File
- File: `CameraControlSettings.json`
- Location: `BepInEx/plugins/canon_plugins/MainGameCameraControl/`

### Main Settings
- `UiVisible`: UI visibility
- `DetailLogEnabled`: Detailed logging
- `DefaultFov`: Default FOV
- `ApplyFov`: Whether preset loads also apply FOV
- `TransitionSeconds`: Transition duration
- `TransitionEasing`: Easing type
- `WindowX` / `WindowY`: UI window position
- `SelectedSaveMode`: Current save mode
- `SelectedBoneTarget`: Current bone target
- `GizmoVisible`: Gizmo visibility
- `GizmoSize`: Gizmo size
- `BeatFovZoomEnabled`: Enables beat-linked FOV zoom
- `BeatFovZoomMultiplier`: Beat FOV zoom multiplier
- `Presets`: Saved camera preset list

## External Integration
- `TryGetUiVisible(bool)`
- `TrySetUiVisible(bool)`
- `TryLoadPresetByName(string, out string)`
- `TryLoadPresetByIndex(int, out string)`
- `TryGetPresetNames(out string[], out string)`

## Logs
- File: `MainGameCameraControl.log`
- Location: `BepInEx/plugins/canon_plugins/MainGameCameraControl/`
- Main entries:
  - Initialization
  - Preset save/remove/load
  - FOV apply events
  - Bone-link state
  - Beat FOV zoom state

## Notes
- Target process: `KoikatsuSunshine`
- UI input handling is more stable when `MainGameUiInputCapture` is present.
- Gizmo control is enabled when `MainGameTransformGizmo` is available.
- Beat FOV zoom can follow BPM when `MainGameBeatSyncSpeed` is available.
