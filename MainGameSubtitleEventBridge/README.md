# MainGameSubtitleEventBridge

## Overview
- HTTP subtitle bridge for main-game subtitle workflows.
- Receives subtitle requests from external tools and forwards them to `MainGameSubtitleCore`.

## What It Does
- Opens an HTTP endpoint for subtitle events
- Parses POST payloads
- Forwards subtitle text, hold time, backend, and speaker fields
- Supports optional notification sounds and auth token checks

## Dependency
- Requires `MainGameSubtitleCore`

## Main Files
- `MainGameSubtitleEventBridge.dll`
- `SubtitleEventBridgeSettings.json`
- `chime/`

## Typical Request Fields
- `text`
- `hold_seconds`
- `backend`
- `display_mode`
- `speaker`
- `speaker_gender`

## Notes
- This plugin is the receiver side only.
- Subtitle rendering is done by `MainGameSubtitleCore`.
