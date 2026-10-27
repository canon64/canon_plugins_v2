# MainGameSubtitleCore

## Overview
- Core subtitle display plugin for main-game use.
- It owns subtitle queueing, backend selection, and display behavior.
- Other subtitle/event plugins send text into this core through its API.

## What It Does
- Manages subtitle queue and display timing
- Selects subtitle backend such as `Auto / InformationUI / Overlay`
- Provides the subtitle API used by bridge plugins
- Supports an optional manual input panel
- Can forward manual text to external receivers

## Role In The Subtitle Set
- `MainGameSubtitleCore`: display core
- `MainGameSubtitleEventBridge`: HTTP subtitle receiver
- `MainGameVoiceFaceEventBridge`: event and voice side integration

## Main Files
- `MainGameSubtitleCore.dll`
- `SubtitleCoreSettings.json`
- optional runtime panel state files

## Main Settings
- subtitle backend selection
- queue and hold behavior
- manual input panel visibility and behavior
- optional forwarding target for manual text

## Logs
- Runtime behavior is written through the plugin's normal logging path.

## Notes
- This plugin is the display side of the subtitle workflow.
- Bridge plugins depend on this core being available first.
