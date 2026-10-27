> Current builds are compatibility bridges for MainGameRhythmSync. The legacy source remains here for reference but is excluded from the csproj. The text below describes the legacy implementation.

# MainGameBeatSyncSpeed

## Overview
- Analyzes beat intensity from video/audio and applies it to H speed control.
- Supports per-song BPM persistence, tap tempo, and motion auto-switch.

## Dependencies
- No hard required plugin
- Target process: `KoikatsuSunshine`, `KoikatsuSunshine_VR`
- Optional integration: `MainGameBlankMapAdd` (video playback time/path)
- Optional integration: `MainGameSpeedLimitBreak` (tap-tempo transfer)
- External tool: `ffmpeg.exe` (WAV extraction from video)

## Installation
- Place `MainGameBeatSyncSpeed.dll` in `BepInEx/plugins/canon_plugins/MainGameBeatSyncSpeed/`.
- For automatic extraction from video, provide `ffmpeg.exe` via either:
  - Bundled (recommended): `BepInEx/plugins/canon_plugins/_tools/ffmpeg/bin/ffmpeg.exe`
  - System PATH: `ffmpeg.exe` is available in `PATH`

## Basic Usage
- Default hotkey: `F9` (enable/disable)
- Set BPM in Config `Audio.Bpm`.
- While video is playing, per-song BPM values are auto-loaded/saved.

## Settings and Logs
- BepInEx config: `BepInEx/config/com.kks.maingame.beatsyncseed.cfg`
- Song BPM map: `SongBpmMap.json`
- Log: `BepInEx/plugins/canon_plugins/MainGameBeatSyncSpeed/MainGameBeatSyncSpeed.log`

## Notes
- If `WavFilePath` is specified, that WAV is prioritized.
- If `ffmpeg.exe` is not found when extraction is needed, analysis will fail.
