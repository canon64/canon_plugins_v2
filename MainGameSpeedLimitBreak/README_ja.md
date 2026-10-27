> 現在のビルドは MainGameRhythmSync への互換ブリッジです。旧ソースは保存されていますが csproj のコンパイル対象外です。以下は旧実装の説明です。

# MainGameSpeedLimitBreak

## 概要
- Hシーンの速度制御を拡張するプラグインです。
- BPMベース速度制御、プリセット、タイムラインキュー、UI操作に対応します。
- `MainGameBlankMapAdd` と組み合わせると動画時間連動も扱えます。

## できること
- バニラ上限を超えた速度レンジ拡張
- BPMベース速度プリセット
- 手動リマップ / 強制リマップ
- 動画時間キューによる速度制御
- キャリブレーションと診断ログ
- ゲーム内UIとホットキー操作

## 対象プロセス
- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## 連携
- 任意: `MainGameBlankMapAdd`
  - 動画ルームの時間スナップショット取得に使用
- 任意: canon 系の Beat 関連ワークフロー

## 主なファイル
- `MainGameSpeedLimitBreak.dll`
- `SpeedLimitBreakSettings.json`
- `SpeedTimeline.json` またはテンプレート系キューファイル
- `MainGameSpeedLimitBreak.log`

## 基本的な使い方
1. 設定ホットキーで BPM / 速度UI を開きます。
2. 速度プリセットを調整または適用します。
3. 動画連動したい場合はタイムラインキューを有効にします。
4. 設定やキューファイルを保存して再利用します。

## 主な機能群
- `BPM` 計測と参照モード
- 速度 / ゲージのリマップ
- バニラ速度の上書き運用
- 動画タイムライン制御
- 診断 / キャリブレーション補助

## 注意点
- `MainGameBlankMapAdd` が無くても通常の BPM 速度制御は使えます。
- 動画タイムライン機能はキューデータと任意の動画連携が前提です。
