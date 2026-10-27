# MainGameDollMode

## 概要
- 本編向けの人形モード状態プラグインです。
- 人形モード中は目ハイライトをOFFで維持し、他プラグインから切り替えられる API を提供します。

## できること
- 人形モードの ON/OFF
- 対象キャラへ `HideEyeHighlight(true)` を適用
- ON 中の再適用による上書き対策
- OFF 時に元のハイライト状態を復元
- 連携プラグイン向け公開 API

## 対象プロセス
- `KoikatsuSunshine`

## 任意依存
- `KSOX`（soft dependency）

## 主なファイル
- `MainGameDollMode.dll`
- `config.json`
- `MainGameDollMode.log`

## 公開 API
- `Plugin.IsDollModeEnabled()`
- `Plugin.SetDollModeEnabled(bool enabled, string source)`

## 注意点
- 大きな単独UIツールというより状態制御プラグインです。
- 再生バー連携など、他プラグインから切り替える前提で使われます。
