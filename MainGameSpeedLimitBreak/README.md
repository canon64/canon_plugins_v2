> Current builds are compatibility bridges for MainGameRhythmSync. The legacy source remains here for reference but is excluded from the csproj. The text below describes the legacy implementation.

# MainGameSpeedLimitBreak

## Overview
- Extended H-scene speed control plugin.
- Adds BPM-based speed control, presets, timeline cues, and UI-driven remapping.
- Can also work with video-linked timing through `MainGameBlankMapAdd`.

## What It Does
- Expands speed ranges beyond vanilla limits
- Supports BPM-based speed presets
- Supports manual remapping and force remapping
- Supports video time cue actions
- Supports calibration and diagnostics
- Provides an in-game UI and hotkey workflow

## Target Processes
- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## Integration
- Optional: `MainGameBlankMapAdd`
  - used for video-room time snapshots
- Optional: beat-related workflows in other canon plugins

## Main Files
- `MainGameSpeedLimitBreak.dll`
- `SpeedLimitBreakSettings.json`
- `SpeedTimeline.json` or template-based cue files
- `MainGameSpeedLimitBreak.log`

## Basic Usage
1. Open the BPM/speed UI with the configured hotkey.
2. Adjust or apply a speed preset.
3. Enable timeline cues if you want video-time-driven speed control.
4. Save settings and cue files for repeat use.

## Main Features
- `BPM` measurement and reference mode
- speed/gauge remapping
- vanilla-speed override workflow
- cue-based video timeline control
- diagnostics and calibration helpers

## Notes
- Normal BPM speed control works even without `MainGameBlankMapAdd`.
- Video timeline features require cue data and optional video linkage.
