# MainGameLogRelay

## Overview
- Shared log relay plugin for the canon `MainGame` / `Studio` plugin set.
- It standardizes owner-based log routing and file output.
- Other plugins can rely on it instead of implementing separate log fan-out logic.

## What It Does
- Routes logs by owner key
- Writes logs into relay-managed files under the plugin folder
- Supports per-owner log key overrides
- Supports multiple file layout styles
- Works as a logging base for dependent plugins

## Target Processes
- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`
- `CharaStudio`

## Main Files
- `MainGameLogRelay.dll`
- `MainGameLogRelaySettings.json`
- `log/`

## Settings
- File: `MainGameLogRelaySettings.json`
- Main purpose:
  - owner-based routing rules
  - log key and file layout control
  - relay behavior normalization

## Output
- Root log directory: `BepInEx/plugins/canon_plugins/MainGameLogRelay/log/`
- Dependent plugins can write through the relay instead of creating their own custom dispatcher

## Typical Usage
- Required by plugins such as `MainGameTransformGizmo`
- Used by plugins that want stable file logging across MainGame / Studio environments

## Notes
- This is an infrastructure plugin, not an end-user feature plugin.
- If dependent plugin logs are missing or split unexpectedly, check this plugin first.
