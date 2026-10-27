# MainGameDollMode

## Overview
- Main-game doll-mode state plugin.
- Keeps eye highlight disabled while doll mode is active and exposes an API for other plugins to toggle the state.

## What It Does
- Turns doll mode on and off
- Applies `HideEyeHighlight(true)` to target characters
- Periodically reapplies the state while enabled
- Restores previous highlight state when disabled
- Exposes public API helpers for linked plugins

## Target Process
- `KoikatsuSunshine`

## Optional Dependency
- `KSOX` (soft dependency)

## Main Files
- `MainGameDollMode.dll`
- `config.json`
- `MainGameDollMode.log`

## Public API
- `Plugin.IsDollModeEnabled()`
- `Plugin.SetDollModeEnabled(bool enabled, string source)`

## Notes
- This is a state/control plugin rather than a large standalone UI tool.
- It is meant to be toggled directly or by linked plugins such as playback-bar integrations.
