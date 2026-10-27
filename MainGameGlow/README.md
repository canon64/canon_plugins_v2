# MainGameGlow

## Overview
- Character glow effect plugin for main-game and VR use.
- Uses a capture camera and bloom pipeline to render glowing character output.

## What It Does
- Builds a dedicated capture path for character glow
- Uses bloom-based glow rendering
- Supports main-game and VR targets
- Writes a dedicated plugin log

## Target Processes
- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## Main Files
- `MainGameGlow.dll`
- `MainGameGlow.log`

## Notes
- This plugin is focused on rendering effect output rather than gameplay control.
- The capture and bloom pipeline is managed internally.
