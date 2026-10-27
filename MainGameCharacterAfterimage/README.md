# MainGameCharacterAfterimage

KoikatsuSunshine (KKS) 本編で、キャラクターの残像エフェクトを表示する BepInEx プラグイン。

## 機能

- キャラクターを毎フレームキャプチャし、残像として背景とキャラの間に描画
- 残像は設定フレーム数で自動フェードアウト
- VR対応

## インストール

1. ZIPを解凍し、`BepInEx/` フォルダをゲームフォルダに上書きコピー
2. ゲームを起動

## 設定

プラグインフォルダ内の `MainGameCharacterAfterimageSettings.json` で調整可能。

| パラメータ | 説明 |
|-----------|------|
| FadeFrames | 残像が消えるまでのフレーム数 |
| MaxAfterimageSlots | 残像の最大表示数 |
| CaptureIntervalFrames | キャプチャ間隔（フレーム） |

## 依存

- BepInEx 5.x
- KoikatsuSunshine

## ライセンス

MIT
