# MainGameClubLights

## 概要
- Hシーン中に複数のクラブライトを追加し、色・強度・照射角・回転・ストロボ・レインボーを制御するプラグインです。
- ライトごとの設定保存、プリセット化、動画パスとのマッピング、BeatSync による連動に対応します。
- 元からシーンにあるライトへの上書き制御もできます。

## できること
- 複数ライトの追加、削除、個別設定
- カメラ追従配置と自由配置の切り替え
- 女キャラ注視、公転、自転、色変更、強度変更
- `Rainbow / Strobe / IntensityLoop / RangeLoop / SpotAngleLoop` の各演出
- BeatSync の強度ゾーンに応じたプリセット切り替え
- BPM波動連動、ゾーン強度追従連動
- ライト状態のプリセット保存と再適用
- 動画パスごとのプリセット自動切り替え
- 元ライトの強度・レインボー・ストロボ上書き

## 使い方
1. Hシーン中に UI を開きます。
2. ライトを追加して、位置・色・強度・角度を調整します。
3. 必要なら `FollowCamera` や `LookAtFemale`、`Rainbow`、`Strobe` を設定します。
4. 気に入った状態をプリセット保存します。
5. 動画連携を使う場合は、動画パスとプリセットをマッピングします。

## 主なUI構成
- `ライト一覧`
  - 各ライトの有効/無効
  - `FollowCamera` と自由配置の切り替え
  - 位置、回転、注視先、色、強度、距離、照射角
  - `Rainbow / Strobe / IntensityLoop / RangeLoop / SpotAngleLoop`
  - `ビート→プリセット` の `Low / Mid / High` 割り当て
- `プリセット`
  - ライト状態を名前付きで保存
  - 保存済みプリセットの削除
  - 任意ライトへのプリセット適用
- `動画連携`
  - 動画パスとプリセットのマッピング追加
  - 再生中動画に応じたプリセット切り替え
- `元ライト`
  - シーン既存ライトの制御有効化
  - 強度スケール
  - 既存ライト側のレインボー、ストロボ、強度ループ
- `ビート閾値`
  - `Low閾値` と `High閾値`
  - 現在強度と現在ゾーン表示

## 演出項目
- `Rainbow`
  - 色相を時間変化させる
  - `動画連携(BPM)` と `動画連携(強度追従)` に対応
- `Strobe`
  - 点滅周波数と ON 比率を制御
  - 周波数と ON 比率の両方に BPM/ゾーン連動あり
- `IntensityLoop`
  - 強度を範囲内で変化
- `RangeLoop`
  - 照射距離を範囲内で変化
- `SpotAngleLoop`
  - 照射角を範囲内で変化

## 保存ファイル
- 設定本体: `ClubLightsSettings.json`
- プロファイル保存先: `profiles/`
- ログ: `MainGameClubLights.log`

## 設定データの主な構造
- `Lights`: ライトごとの実設定
- `Presets`: 保存済みライトプリセット
- `VideoPresetMappings`: 動画パスとプリセットの対応
- `NativeLight`: 既存ライト上書き設定
- `BeatLowThreshold` / `BeatHighThreshold`: ゾーン判定閾値
- `UiVisible` / `UiX` / `UiY`: UI表示と位置

## 連携
- `MainGameBlankMapAdd`
  - 再生中動画パス連携
  - UI表示切り替え連携
- `MainGameBeatSyncSpeed`
  - BPM と強度ゾーンの取得
- `MainGameTransformGizmo`
  - ライトや注視点のギズモ操作
- `MainGameUiInputCapture`
  - UIドラッグ中の入力奪取

## ログ
- ファイル: `MainGameClubLights.log`
- 配置先: `BepInEx/plugins/canon_plugins/MainGameClubLights/`
- 主な内容:
  - ライト生成/削除
  - プリセット保存/適用
  - プロファイル読込/保存
  - 動画連携適用
  - BeatSync ゾーン切り替え

## 注意点
- 対象プロセスは `KoikatsuSunshine` と `KoikatsuSunshine_VR` です。
- キャラの髪や服はシェーダー都合で全素材にライトが完全反映されるわけではありません。
- `MainGameBeatSyncSpeed` がない場合、BeatSync 連動機能は動きません。
