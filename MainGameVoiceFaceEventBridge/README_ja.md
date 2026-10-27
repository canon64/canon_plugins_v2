# MainGameVoiceFaceEventBridge

## 概要

このプラグインは、KKSメインゲーム向けの音声/イベント連携の中核です。  
外部コマンドを受け取り、以下を反映します。

- 音声再生
- 表情制御
- テキスト起点処理（`response_text` からの coord/clothes/pose 解析）

主通信はローカルNamedPipe: `kks_voice_face_events` です。

## 同梱ファイル

- `MainGameVoiceFaceEventBridge.dll`
- `VoiceFaceEventBridgeSettings.json`

## human_2_KKS_pipeline 連携

`human_2_KKS_pipeline` から `send_voice_face_event.ps1` 経由でコマンドが送られます。

既定経路:
- NamedPipe: `kks_voice_face_events`

任意経路:
- HTTPブリッジ（sender側の host/port/endpoint 設定）

## 基本コマンド

- `{"type":"speak", ...}` : 音声再生 + 必要に応じて表情制御
- `{"type":"stop"}` : 外部再生停止
- `{"type":"response_text","text":"...","delaySeconds":...}` : テキスト解析して衣装/体位変更などを遅延実行

## 補足

- 設定の再読込は Ctrl+R（設定有効時）。
- 主設定ファイルは `VoiceFaceEventBridgeSettings.json`。

## 使い方（今回追加分）

### 1. `response_text` から動画を再生する

- `EnableVideoPlaybackByResponseText` を `true` にします（既定ON）。
- 返答文に「流す」と、動画名の一部を入れると再生対象になります。
- 例:
  - `UNFORGIVENを流すね`
  - `流すね QUEENCARD`
- プラグインは現在選択中フォルダの動画ファイル名を走査し、部分一致した候補から1つをランダム再生します。
- 実再生は `BlankMapAdd` の `/videoroom/play` に内部送信して行います。

### 2. 設定変更（ConfigManager）

- ConfigManager から `EnableVideoPlaybackByResponseText` を切り替え可能です。
- 設定変更は `config.json` に保存され、次回起動時も反映されます。

### 3. 体位分類JSONの自動生成

- `pose_sonyu_classified.json` と `pose_houshi_classified.json` が無い場合は自動生成されます。

### 4. ログの見方（動画が動かないとき）

- `no video keyword matched (video response-text playback disabled)`
  - 動画トリガー設定がOFFです。
- `no video keyword matched (video name token not found)`
  - 「流す」はあっても、動画名として使う語を抽出できていません。

### 5. 体位変更が起こる条件

- 体位変更は `type=response_text` を受信したときだけ判定されます。
- ConfigManager で `体位制御.Enable` がOFFだと、体位変更は一切動きません。
- `体位制御.カテゴリ` でOFFにしたカテゴリは候補から除外されます。
- `体位制御.ルール.Enable` がONなら、`pose_score_rules.json` の `rules` を使って候補体位をスコア選択します。
- `体位制御.推定.Enable` がONなら、`pose_score_rules.json` の `inferRules` で文章から体位カテゴリを推定します。
- 最終的に、推定カテゴリ内の実在体位（`pose_sonyu_classified.json` / `pose_houshi_classified.json`）から選ばれたときだけ体位変更が実行されます。

判定の流れ（簡易）:

1. `response_text` の本文を受け取る
2. カテゴリ推定（例: 正常位/後背位/騎乗位）
3. スコアルールでカテゴリ内の具体体位を選ぶ
4. 体位名が確定した場合のみ体位変更を実行

よくある例:

- `正常位になるね`  
  - 正常位系カテゴリが推定され、該当体位へ変更されます。
- `仰向けになって足を広げるね`  
  - 正常位系の推定ルールに一致し、開脚系の正常位が優先されます。
- `立って後ろからするね`  
  - 立後背位系に寄りやすく、立ちバック系が選ばれます。

不発時のログ:

- `no pose keyword matched`
  - カテゴリ推定やスコアルールが成立せず、体位変更が行われなかった状態です。
