# MainGameCameraControl

## 概要
- 本編H向けのカメラ保存・呼び出し・補間制御プラグインです。
- `CameraControl_Ver2` のカメラ状態をプリセットとして保存し、あとから呼び出せます。
- カメラ位置だけでなく、回転、FOV、ボーン追従情報も一緒に扱えます。

## できること
- 現在のカメラ状態をプリセット保存
- 保存済みプリセットを補間付きで呼び出し
- 通常保存、ボーン保存、`ksFPV` 保存の切り替え
- FOV を含めて適用するかを切り替え
- ボーン追従時の注視点オフセットをギズモで調整
- 拍FOVズームの ON/OFF と倍率調整
- `MainGameBlankMapAdd` から UI 表示を切り替え
- 外部APIからプリセット名・番号指定で呼び出し

## 使い方
1. Hシーン中に UI を開きます。
2. `保存名` に名前を入れます。
3. `保存モード` を選びます。
4. `現在を保存` でプリセットを追加します。
5. `保存済み` 一覧のボタンを押すと、そのプリセットへ遷移します。

## 主なUI項目
- `FOVも適用`: 保存済みFOVを呼び出し時に適用するか
- `詳細ログ`: 詳細ログの ON/OFF
- `遷移時間`: カメラ遷移時間
- `イージング`: `Linear / EaseIn / EaseOut / EaseInOut`
- `現在FOV`: 現在のFOVを直接調整
- `拍FOVズーム`: BPM連動のFOVズームを有効化
- `ズーム倍率`: 拍FOVズーム倍率
- `保存モード`: `通常 / ボーン / ksFPV`
- `対象ボーン`: ボーン保存時の基準部位
- `ギズモ表示`: 注視点ギズモの表示/非表示
- `ギズモサイズ`: ギズモ表示サイズ
- `現在値`: 現在の `Target / Dir / Rot / DataFov / LiveFov / PresetFov` を表示
- `現在を保存`: 現在状態を新規プリセット保存
- `上書き保存`: 現在アクティブなボーン連携プリセットへ上書き
- `解除`: ボーン連携状態を解除
- `Reset`: 現在カメラを基準値へ戻す

## 保存モード
- `通常`: その時点の `TargetPosition / CameraDirection / Rotation / Fov` を保存
- `ボーン`: 女キャラの対象ボーンを基準にした注視点とカメラオフセットを保存
- `ksFPV`: `ksFPV` ベースの追従データを保存

## 設定ファイル
- ファイル: `CameraControlSettings.json`
- 配置先: `BepInEx/plugins/canon_plugins/MainGameCameraControl/`

### 主な設定項目
- `UiVisible`: UI表示状態
- `DetailLogEnabled`: 詳細ログ出力
- `DefaultFov`: 既定FOV
- `ApplyFov`: プリセット呼び出し時にFOVも適用するか
- `TransitionSeconds`: 遷移秒数
- `TransitionEasing`: イージング種別
- `WindowX` / `WindowY`: UIウィンドウ位置
- `SelectedSaveMode`: 現在の保存モード
- `SelectedBoneTarget`: 現在のボーン対象
- `GizmoVisible`: ギズモ表示状態
- `GizmoSize`: ギズモサイズ
- `BeatFovZoomEnabled`: 拍FOVズームの有効/無効
- `BeatFovZoomMultiplier`: 拍FOVズーム倍率
- `Presets`: 保存済みカメラプリセット一覧

## 外部連携
- `TryGetUiVisible(bool)`
- `TrySetUiVisible(bool)`
- `TryLoadPresetByName(string, out string)`
- `TryLoadPresetByIndex(int, out string)`
- `TryGetPresetNames(out string[], out string)`

## ログ
- 「詳細ログ」がOFFの間は、通常の切り替え・保存・追従ログとFOV診断ログを停止します。警告は出力します。
- FOV診断も「詳細ログ」に連動します。`fov-diagnostics.enable` ファイルは不要です。
- ファイル: `MainGameCameraControl.log`
- 配置先: `BepInEx/plugins/canon_plugins/MainGameCameraControl/`
- 主な内容:
  - 初期化
  - プリセット保存/削除/呼び出し
  - FOV適用
  - ボーン連携状態
  - 拍FOVズーム状態

## 注意点
- 対象プロセスは `KoikatsuSunshine` です。
- UI入力まわりは `MainGameUiInputCapture` があると安定します。
- ギズモ操作は `MainGameTransformGizmo` があると有効になります。
- 拍FOVズームは `MainGameBeatSyncSpeed` があると BPM 連動で動きます。
