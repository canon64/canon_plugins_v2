# MainGameLogRelay

## 概要
- canon 系 `MainGame` / `Studio` プラグイン群向けの共有ログ中継プラグインです。
- owner 単位のログ出力先を統一し、ファイル出力を安定化します。
- 各プラグイン側で個別のログ分配処理を持たせずに運用できます。

## できること
- owner key ごとのログ振り分け
- プラグインフォルダ配下へのログファイル出力
- owner ごとの log key 上書き
- 複数のファイルレイアウト方式への対応
- 依存プラグインの共通ロギング基盤

## 対象プロセス
- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`
- `CharaStudio`

## 主なファイル
- `MainGameLogRelay.dll`
- `MainGameLogRelaySettings.json`
- `log/`

## 設定
- ファイル: `MainGameLogRelaySettings.json`
- 主用途:
  - owner 単位のルーティング設定
  - log key / file layout 制御
  - relay 挙動の正規化

## 出力先
- ルートログフォルダ: `BepInEx/plugins/canon_plugins/MainGameLogRelay/log/`
- 依存プラグインは独自分配処理を持たず、この relay 経由で出力できます

## 主な利用先
- `MainGameTransformGizmo` などの依存プラグイン
- MainGame / Studio をまたいで安定したファイルログを出したいプラグイン

## 注意点
- これはユーザー向け機能プラグインではなく基盤プラグインです。
- 依存プラグインのログが出ない、または分散する場合はまず本プラグインを確認してください。
