# MainGameUiInputCapture

## Overview
- Shared UI input arbitration plugin for MainGame and VR plugins.
- Prevents multiple in-game tools from fighting over cursor and camera input while dragging UI.

## What It Does
- Tracks active input owners and owner/source tokens
- Temporarily unlocks input while a plugin is interacting with UI
- Restores input state after capture ends
- Supports idle cursor unlock mode
- Exposes shared API methods for other plugins

## Target Processes
- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## Dependency
- Hard dependency: `MainGameLogRelay`

## Public API
- `Sync`
- `Begin`
- `Tick`
- `End`
- `EndOwner`
- `SetIdleCursorUnlock`
- `IsOwnerActive`
- `SetOwnerDebug`
- `IsAnyActive`
- `GetStateSummary`

## Main Files
- `MainGameUiInputCapture.dll`
- `MainGameUiInputCaptureSettings.json`

## Notes
- This is a coordination plugin, not a visible gameplay feature by itself.
- Plugins with draggable IMGUI windows should use this API instead of local ad-hoc input unlock logic.
