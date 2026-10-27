# MainGameAllPoseMap

## Overview
- Adds an all-pose-enabled map for FreeH usage.
- Expands category limitations and supports virtual H points.
- Default target map is `AddedMapNo=900` (videomap scenario).
- If `AddedMapNo` already exists, that map is enhanced; otherwise a new map is created.

## Dependencies
- No hard required plugin
- Target process: `KoikatsuSunshine`, `KoikatsuSunshine_VR`

## Installation
- Place `MainGameAllPoseMap.dll` in `BepInEx/plugins/canon_plugins/MainGameAllPoseMap/`.

## Basic Usage
- This plugin runs as resident behavior and is controlled mainly by settings.
- Change `AddedMapNo` in `AllPoseMapSettings.json` to target another map number.
- If you point to an existing map number, all-pose extension is applied to that map.

## Settings and Logs
- JSON settings: `AllPoseMapSettings.json`
- Log: `BepInEx/plugins/canon_plugins/MainGameAllPoseMap/MainGameAllPoseMap.log`

## Notes
- Using an existing map number for `AddedMapNo` is supported.
- If `AddedMapNo` is unused, a new map is created with that number.
- Enabling `EnableVirtualPoints` adds virtual points and expands pose choices.
