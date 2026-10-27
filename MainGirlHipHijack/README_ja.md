# MainGirlHipHijack

KoikatsuSunshine の H シーン向け BepInEx プラグインです。  
女性側 BodyIK 制御、追従編集、頭角度操作、ポーズ自動呼び出し、VR補助操作を提供します。

英語版: [README.md](README.md)

## ステータス

- ベータ版
- 女性向けワークフローを主対象に継続開発中
- 男性操作UIは現在非表示（内部機能は一部保持）
- `KoikatsuSunshine` と `KoikatsuSunshine_VR` の両方を対象にしています
- ConfigManager から `Enabled` `UI > Visible` `Logging > EnableLogs` を切り替えできます

## 主な機能

- 女性 BodyIK 13エフェクタ制御（手/足/肩/腿/肘/膝/腰中央）
- 各エフェクタの `有効/無効` `ウェイト` `ギズモ表示` `現在アニメ姿勢へリセット`
- ボーン追従（Nearest Follow、追従ミラー、距離しきい値、VR時の頭ボーン→HMD変換）
- 女性頭角度操作
  - スライダー（X/Y/Z）
  - 回転ギズモ表示
  - `体位/モーション変更後も維持` チェック（OFF時は変更時に0へリセット）
- VR補助操作
  - IKプロキシのVR掴みモード
  - 女性頭のグラブ回転（加算回転として保持）
  - 主要用途: VR時にトリガーを押しながら `B` で女性腰IKを有効化し、左コントローラーへ親子付けして、実コントローラーの動きをゲーム内へ反映
- ポーズプリセット（スクショ付き）
  - 保存/読込/上書き/削除
  - 体位一致時の自動呼び出し（`auto` チェック）
  - 遷移秒数・イージング（Linear/SmoothStep/EaseOut）
  - 頭角度ありポーズは頭角度も遷移ウェイトで補間
  - 頭角度なしポーズへ遷移時は頭角度を0へフェードアウト
  - `auto` 候補が0件または1件のときはループ回数による切替は行わない
- 腰系ツール
  - Enterキー（初期値）で HipQuickSetup をON/OFFトグル
  - ON時: 腰IK有効化 + SpeedHijack/女性アニメ速度切断を有効
  - OFF時: IK全OFF + 腰リンク解除 + アニメ同期リセット
  - `待機中の動きで自動挿入` をONにすると、待機中の腰動き検知から自動で挿入→ピストンへ移行します
- ログ/診断
  - `詳細ログ` で入力キャプチャや状態監視ログを出力
  - `BodyIK診断ログ` でIK適用前後の差分確認ログを出力
  - ログ出力先は `MainGameLogRelay` と専用ログファイルの両系統です
- `MainGameBlankMapAdd` 連携（VideoAllposeRoom）
  - `MainGameBlankMapAdd` が導入されている場合、VideoAllposeRoom の再生バーから HipHijack UI の表示/非表示を切替可能
  - 再生バー2段目の `説明` の右側にある `HipUI` チェックで切替

## 実行時挙動メモ

- 起動時はUI表示を強制OFFで開始します
- `auto` 候補が0件のときは BodyIK を全OFF にして、その体位では自動適用しません
- Hシーン突入時に以下をリセットします
  - SpeedHijack / 女性アニメ速度切断
  - 頭角度ギズモ表示
  - BodyIK有効状態
  - 腰リンク・HipQuickSetup状態
- `auto` 候補が1件だけのときは、その候補を使いますが、ループ回数による切替ローテーションは行いません

## 要件

- KoikatsuSunshine
- BepInEx 5.x

## 依存関係

### 必須（ハード依存）

- `MainGameTransformGizmo`
- `MainGameUiInputCapture`
- `MainGameLogRelay`

### 任意同梱（推奨）

- `MainGirlShoulderIkStabilizer`  
  必須依存ではありませんが、HipHijack配布プロファイルでは同梱対象として扱います。

## インストール

DLLを各プラグインフォルダへ配置してください。

- `BepInEx/plugins/canon_plugins/MainGirlHipHijack/MainGirlHipHijack.dll`
- `BepInEx/plugins/canon_plugins/MainGameTransformGizmo/MainGameTransformGizmo.dll`
- `BepInEx/plugins/canon_plugins/MainGameUiInputCapture/MainGameUiInputCapture.dll`
- `BepInEx/plugins/canon_plugins/MainGameLogRelay/MainGameLogRelay.dll`

任意同梱:

- `BepInEx/plugins/canon_plugins/MainGirlShoulderIkStabilizer/MainGirlShoulderIkStabilizer.dll`

## 設定と保存ファイル

- 設定: `BepInEx/plugins/canon_plugins/MainGirlHipHijack/FullIkGizmoSettings.json`
- ログ: `BepInEx/plugins/canon_plugins/MainGirlHipHijack/MainGirlHipHijack.log`
- 女性ポーズ: `BepInEx/plugins/canon_plugins/MainGirlHipHijack/pose_presets/index.json`
- 女性ポーズ画像: `BepInEx/plugins/canon_plugins/MainGirlHipHijack/pose_presets/shots/`
- 男性ポーズ内部保存: `BepInEx/plugins/canon_plugins/MainGirlHipHijack/pose_presets_male/index.json`
- 自動保存される主な項目:
  - `AutoEnableAllOnResolve`
  - `AutoPoseEnabled`
  - `AutoPoseSwitchAnimationLoops`
  - `PoseTransitionSeconds`
  - `PoseTransitionEasing`
  - `AutoInsertOnMoveEnabled`
  - `DetailLogEnabled`
  - `BodyIkDiagnosticLog`

補足:

- 初回起動時に自動生成されます
- 読み込み/保存時に値は正規化・クランプされます
- セッション限定のIK ON/OFF状態は起動時にリセットされます

## ビルド（ソース）

ターゲットフレームワーク: `net472`

ビルド:

`dotnet build MainGirlHipHijack.csproj -c Release`

出力:

`bin/Release/net472/MainGirlHipHijack.dll`

## プラグイン情報

- GUID: `com.kks.main.girlbodyikgizmo`
- Name: `MainGirlHipHijack`
- Version: `1.0.0`
- Process: `KoikatsuSunshine`, `KoikatsuSunshine_VR`
