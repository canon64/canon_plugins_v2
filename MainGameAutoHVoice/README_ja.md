# MainGameAutoHVoice

## 概要
- 本編H中に再生された音声IDを捕捉し、一定間隔で再発話させるプラグインです。
- 直前に流れた音声を記録して、指定した `main` スロットへ自動または手動で流し直します。
- UI はゲーム内ウィンドウで表示され、ドラッグ移動できます。

## できること
- `HVoiceCtrl.VoiceProc` で流れた直近の音声IDを捕捉する
- 指定間隔ごとに同じ音声IDを自動再生する
- `Speak Now` ボタンで手動再生する
- Hモード番号の一致チェックを入れて、別モードへの誤発話を防ぐ
- キャプチャ有効期限や最小再生間隔を調整する
- 詳細ログのON/OFFを切り替える

## 使い方
1. 本編Hを開始します。
2. プラグインUIで直近音声が捕捉されるまで待ちます。
3. `Auto` をONにすると、捕捉済み音声が一定間隔で再発話されます。
4. すぐに試したい場合は `Speak Now` を押します。

## 画面上の主な項目
- `Auto`: 自動再生の有効/無効
- `詳細ログ`: 捕捉ログや状態ログを詳しく出すか
- `Mode一致`: 現在のHモードと捕捉時モードが一致した時だけ再生するか
- `未捕捉許可`: 音声未捕捉状態でも手動再生を許可するか
- `Main Index`: 再発話先の `main` 番号
- `Auto Interval`: 自動再生の間隔秒数
- `Min Spacing`: 連続発話を抑える最小間隔秒数
- `Capture Expire`: 捕捉音声を有効とみなす保持秒数
- `Speak Now`: 現在の捕捉音声をその場で再生
- `Reload Settings`: JSON設定を再読込

## 設定ファイル
- ファイル: `AutoHVoiceSettings.json`
- 配置先: `BepInEx/plugins/canon_plugins/MainGameAutoHVoice/`

### 設定項目
- `Enabled`: 自動再生を有効にするか
- `ShowGui`: UIを表示するか
- `VerboseLog`: 詳細ログを出すか
- `TargetMainIndex`: 再発話先の `main` 番号
- `AutoIntervalSeconds`: 自動再生の基準間隔
- `MinimumSpacingSeconds`: 再発話の最小間隔
- `CaptureExpireSeconds`: 捕捉音声の有効期限
- `RequireModeMatch`: 捕捉時と現在のHモード一致を必須にするか
- `AllowManualTriggerWhenNoCapture`: 未捕捉でも手動再生を許可するか
- `WindowX`: UIウィンドウのX座標
- `WindowY`: UIウィンドウのY座標

## ログ
- ファイル: `MainGameAutoHVoice.log`
- 配置先: `BepInEx/plugins/canon_plugins/MainGameAutoHVoice/`
- 主な記録内容:
  - Hシーン捕捉開始/解放
  - 音声ID捕捉
  - 自動再生/手動再生の実行
  - 設定再読込や状態変化

## 注意点
- 対象プロセスは `KoikatsuSunshine` です。
- `MainGameUiInputCapture` への依存があります。
- 直近音声が未捕捉、期限切れ、または `playVoices` が埋まっている場合は再生されません。
- `Mode一致` をONにしていると、捕捉時と異なるHモードでは再生されません。
