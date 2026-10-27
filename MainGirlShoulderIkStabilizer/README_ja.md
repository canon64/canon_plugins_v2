# MainGirlShoulderIkStabilizer

## 概要
- 本編向けの肩 IK 安定化ブリッジです。
- MainGame 環境で肩まわりの AdvIK 安定化設定を適用します。

## できること
- MainGame で肩スタビライザ値を適用
- 専用肩設定ファイルの読込
- ConfigurationManager 経由の設定制御
- HipHijack 中心の編集ワークフローを補助

## 対象プロセス
- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## 依存関係
- `MainGameLogRelay`
- `MainGirlHipHijack` 系のブリッジ対象

## 主なファイル
- `MainGirlShoulderIkStabilizer.dll`
- `ShoulderIkStabilizerSettings.json`
- `MainGirlShoulderIkStabilizer.log`

## 注意点
- 単独で完結する編集プラグインというより補助ブリッジです。
- 通常は `MainGirlHipHijack` と組み合わせて使います。
