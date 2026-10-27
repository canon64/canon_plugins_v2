# MainGameAutoHVoice

## Overview
- This plugin captures voice IDs played during main-game H scenes and replays the latest captured voice at intervals.
- It remembers the most recent voice entry and re-triggers it on a selected `main` slot, either automatically or manually.
- The in-game UI is shown as a draggable window.

## What It Does
- Captures the latest voice ID from `HVoiceCtrl.VoiceProc`
- Replays the captured voice automatically at a configurable interval
- Lets you trigger the current captured voice manually with `Speak Now`
- Can require H mode matching to avoid replaying a voice in the wrong mode
- Lets you tune capture expiration and minimum spacing between triggers
- Supports verbose logging

## Basic Usage
1. Start a main-game H scene.
2. Wait until the plugin captures at least one voice.
3. Turn `Auto` on to replay the captured voice at intervals.
4. Press `Speak Now` to test the current captured voice immediately.

## Main UI Items
- `Auto`: Enables or disables automatic replay
- `詳細ログ`: Enables verbose capture and state logging
- `Mode一致`: Requires the current H mode to match the captured mode
- `未捕捉許可`: Allows manual triggering even when nothing has been captured yet
- `Main Index`: Target `main` slot used for replay
- `Auto Interval`: Automatic replay interval in seconds
- `Min Spacing`: Minimum spacing between voice triggers
- `Capture Expire`: How long a captured voice stays valid
- `Speak Now`: Immediately replay the currently captured voice
- `Reload Settings`: Reload the JSON settings file

## Settings File
- File: `AutoHVoiceSettings.json`
- Location: `BepInEx/plugins/canon_plugins/MainGameAutoHVoice/`

### Settings
- `Enabled`: Whether automatic replay is enabled
- `ShowGui`: Whether the UI window is visible
- `VerboseLog`: Whether verbose logging is enabled
- `TargetMainIndex`: Target `main` slot for replay
- `AutoIntervalSeconds`: Base interval for automatic replay
- `MinimumSpacingSeconds`: Minimum spacing between triggers
- `CaptureExpireSeconds`: Lifetime of a captured voice entry
- `RequireModeMatch`: Whether current H mode must match the captured mode
- `AllowManualTriggerWhenNoCapture`: Whether manual trigger is allowed without a captured voice
- `WindowX`: UI window X position
- `WindowY`: UI window Y position

## Logs
- File: `MainGameAutoHVoice.log`
- Location: `BepInEx/plugins/canon_plugins/MainGameAutoHVoice/`
- Main entries:
  - H scene capture start/release
  - Captured voice IDs
  - Automatic and manual trigger events
  - Settings reload and state changes

## Notes
- Target process: `KoikatsuSunshine`
- Dependency: `MainGameUiInputCapture`
- Replay will not run if no recent voice has been captured, the capture is expired, or `playVoices` is still occupied.
- If `Mode一致` is enabled, replay only runs when the current H mode matches the captured mode.
