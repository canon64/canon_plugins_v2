# MainGameTransformGizmo

## 概要
- 本編用の共有 Transform Gizmo プラグインです。
- 他プラグインが使うアタッチ式ギズモと API 補助を提供します。

## できること
- `TransformGizmoApi.Attach(...)` などのアタッチ API を公開
- 依存プラグイン向けの移動 / 変形操作を共通化
- 各プラグインが個別実装を持たずに同じギズモ基盤を使える

## 対象プロセス
- `KoikatsuSunshine`

## 主な利用先
- `MainGameBlankMapAdd`
- `MainGameCameraControl`
- `MainGirlHipHijack`

## 主なファイル
- `MainGameTransformGizmo.dll`
- `_logs/info.txt`

## 注意点
- 本プラグイン単体でのユーザー向け操作フローはありません。
- 表示 / ドラッグ挙動 / 用途はアタッチした側のプラグインが決めます。
