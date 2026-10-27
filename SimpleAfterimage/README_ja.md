# SimpleAfterimage

## 概要
- `KoikatsuSunshine` / `KoikatsuSunshine_VR` 向けの残像表現プラグインです。
- キャラだけをキャプチャして、後ろに尾を引くような残像を描画します。

## できること
- キャラ残像の追加
- 残像寿命、キャプチャ間隔、色、透明度、フェードカーブ調整
- プリセット保存
- Beat 連動のフェード範囲調整

## 対象プロセス
- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## 主なファイル
- `SimpleAfterimage.dll`
- `config.json`
- `SimpleAfterimage.log`

## 主な設定
- fade frames
- max slots
- capture interval
- tint RGBA
- alpha scale
- fade curve
- 任意のプリセット操作

## 注意点
- 強くしすぎると白飛びしやすくなります。
- まず自動生成された設定値を基準に少しずつ調整してください。
