# MainGameSubtitleEventBridge

## 概要
- 本編字幕ワークフロー向けの HTTP ブリッジです。
- 外部ツールから字幕リクエストを受け取り、`MainGameSubtitleCore` へ渡します。

## できること
- 字幕イベント受信用 HTTP エンドポイントを開く
- POST ペイロードを解釈する
- 字幕本文、保持秒数、表示先、話者情報をコアへ転送する
- 任意の通知音と認証トークン確認

## 依存関係
- `MainGameSubtitleCore` が必要です

## 主なファイル
- `MainGameSubtitleEventBridge.dll`
- `SubtitleEventBridgeSettings.json`
- `chime/`

## 主なリクエスト項目
- `text`
- `hold_seconds`
- `backend`
- `display_mode`
- `speaker`
- `speaker_gender`

## 注意点
- このプラグインは受信側のみです。
- 実際の字幕描画は `MainGameSubtitleCore` が担当します。
