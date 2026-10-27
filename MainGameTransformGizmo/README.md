# MainGameTransformGizmo

## Overview
- Shared transform gizmo plugin for main-game plugins.
- Provides attachable gizmo objects and API helpers used by other plugins.

## What It Does
- Exposes attach helpers such as `TransformGizmoApi.Attach(...)`
- Supports runtime move/transform interaction for dependent plugins
- Works as a common gizmo layer instead of each plugin shipping its own implementation

## Target Process
- `KoikatsuSunshine`

## Main Usage
- Used by plugins such as:
  - `MainGameBlankMapAdd`
  - `MainGameCameraControl`
  - `MainGirlHipHijack`

## Main Files
- `MainGameTransformGizmo.dll`
- `_logs/info.txt`

## Notes
- This plugin has no end-user workflow by itself.
- Visibility, drag behavior, and meaning of the gizmo are controlled by the plugin that attaches it.
