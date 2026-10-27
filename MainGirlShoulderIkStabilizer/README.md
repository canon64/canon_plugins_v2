# MainGirlShoulderIkStabilizer

## Overview
- Shoulder IK stabilizer bridge for main-game female IK workflows.
- Applies shoulder-related AdvIK stabilization settings during main-game use.

## What It Does
- Applies shoulder stabilizer values in MainGame
- Reads dedicated shoulder settings
- Exposes config-driven control through ConfigurationManager
- Works as a support plugin for HipHijack-centered workflows

## Target Processes
- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## Dependencies
- `MainGameLogRelay`
- `MainGirlHipHijack` plugin ID bridge target

## Main Files
- `MainGirlShoulderIkStabilizer.dll`
- `ShoulderIkStabilizerSettings.json`
- `MainGirlShoulderIkStabilizer.log`

## Notes
- This plugin is mainly a support bridge, not a standalone editing workflow.
- It is typically paired with `MainGirlHipHijack`.
