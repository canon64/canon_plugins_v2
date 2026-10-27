# MainGameClubLights

## Overview
- A club-light control plugin for H scenes that can add multiple lights and control color, intensity, angle, rotation, strobe, and rainbow effects.
- It supports per-light settings, presets, video-path mapping, and BeatSync-linked behavior.
- It can also override native scene lights.

## What It Does
- Add, remove, and edit multiple custom lights
- Switch between follow-camera placement and free placement
- Control female look-at, revolution, rotation, color, intensity, and angles
- Use `Rainbow / Strobe / IntensityLoop / RangeLoop / SpotAngleLoop`
- Switch presets by BeatSync intensity zone
- Drive effects by BPM wave or zone-follow intensity
- Save light states as presets and reapply them
- Map video paths to presets for automatic switching
- Override native scene lights with intensity, rainbow, and strobe behavior

## Basic Usage
1. Open the UI during an H scene.
2. Add a light and adjust position, color, intensity, and angle.
3. Enable options such as `FollowCamera`, `LookAtFemale`, `Rainbow`, or `Strobe` as needed.
4. Save a preset when you like the current state.
5. If you use video linkage, map a video path to a preset.

## Main UI Sections
- `ライト一覧`
  - Per-light enable/disable
  - `FollowCamera` or free placement
  - Position, rotation, target, color, intensity, range, and spot angle
  - `Rainbow / Strobe / IntensityLoop / RangeLoop / SpotAngleLoop`
  - `ビート→プリセット` assignments for `Low / Mid / High`
- `プリセット`
  - Save current light states with names
  - Delete saved presets
  - Apply a preset to a selected light
- `動画連携`
  - Add mappings between video paths and presets
  - Switch presets based on the currently playing video
- `元ライト`
  - Enable override for native scene lights
  - Intensity scale
  - Native-light rainbow, strobe, and intensity loop
- `ビート閾値`
  - `Low閾値` and `High閾値`
  - Current intensity and current zone display

## Effect Groups
- `Rainbow`
  - Changes hue over time
  - Supports `動画連携(BPM)` and `動画連携(強度追従)`
- `Strobe`
  - Controls blink frequency and duty ratio
  - Both frequency and duty ratio can use BPM or zone-linked behavior
- `IntensityLoop`
  - Animates light intensity inside a range
- `RangeLoop`
  - Animates light range inside a range
- `SpotAngleLoop`
  - Animates spot angle inside a range

## Saved Files
- Main settings: `ClubLightsSettings.json`
- Profile storage: `profiles/`
- Log: `MainGameClubLights.log`

## Main Data Structure
- `Lights`: Actual per-light settings
- `Presets`: Saved light presets
- `VideoPresetMappings`: Video path to preset mapping
- `NativeLight`: Native light override settings
- `BeatLowThreshold` / `BeatHighThreshold`: Zone threshold values
- `UiVisible` / `UiX` / `UiY`: UI visibility and position

## Integrations
- `MainGameBlankMapAdd`
  - Current video path linkage
  - UI visibility linkage
- `MainGameBeatSyncSpeed`
  - BPM and intensity-zone source
- `MainGameTransformGizmo`
  - Gizmo control for light and target positioning
- `MainGameUiInputCapture`
  - Input capture while dragging the UI

## Logs
- File: `MainGameClubLights.log`
- Location: `BepInEx/plugins/canon_plugins/MainGameClubLights/`
- Main entries:
  - Light creation and removal
  - Preset save and apply
  - Profile load and save
  - Video-link application
  - BeatSync zone transitions

## Notes
- Target processes are `KoikatsuSunshine` and `KoikatsuSunshine_VR`.
- Due to shader limitations, custom light does not fully affect every hair or clothing material.
- BeatSync-linked features do not work without `MainGameBeatSyncSpeed`.
