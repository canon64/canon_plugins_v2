# MainGameSubtitleCore

## 概要
- 本編向け字幕表示の中核プラグインです。
- 字幕キュー管理、表示先選択、表示タイミング制御を担当します。
- 他の字幕 / イベント系プラグインはこのコアの API に字幕を流し込みます。

## できること
- 字幕キューと表示時間の管理
- `Auto / InformationUI / Overlay` などの表示バックエンド切り替え
- ブリッジ側が使う字幕 API の提供
- 任意の手動入力パネル
- 手動入力文字列の外部送信

## 字幕セット内での役割
- `MainGameSubtitleCore`: 表示コア
- `MainGameSubtitleEventBridge`: HTTP字幕受信
- `MainGameVoiceFaceEventBridge`: 音声 / イベント連携

## 主なファイル
- `MainGameSubtitleCore.dll`
- `SubtitleCoreSettings.json`
- 任意のランタイム入力パネル状態ファイル

## 主な設定
- 字幕バックエンド選択
- キュー / 保持秒数まわり
- 手動入力パネルの表示 / 挙動
- 手動入力文字列の外部送信先

## ログ
- 実行時挙動は通常のプラグインログ経路に出力されます。

## 注意点
- このプラグインは字幕ワークフローの表示側です。
- ブリッジ系プラグインは本コアが先に使える状態である必要があります。
