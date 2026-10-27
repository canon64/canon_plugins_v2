# MainGameCircularDisplacement

Koikatsu Sunshine / KKS VR 用の円形ディスプレイスメント演出プラグイン。

## 概要

- Hシーン中だけ有効化可能
- `Chara` レイヤーだけをキャプチャ
- 円形パラメータでUVを歪ませて画面上に重ね描き
- ConfigManager から全パラメータを変更可能
- 設定値を `MainGameCircularDisplacement.json` に保存

## 注意

画面全体キャプチャは入れない。重ね描き結果を次フレームで再キャプチャするフィードバック構造になるため、初期版では `Chara` レイヤー専用にしている。

## Mode

- `Off`
- `Radial`
- `Ripple`
- `Swirl`
- `Ring`

## 主な設定

### General

- `Enabled`
- `OnlyHScene`

### Capture

- `CharaLayerName`
- `UseScreenSize`
- `CaptureWidth`
- `CaptureHeight`

### Camera

- `PreferCameraMain`
- `CameraNameFilter`
- `CameraFallbackIndex`

### Displacement

- `Mode`
- `GridX`
- `GridY`
- `Opacity`
- `CenterX`
- `CenterY`
- `Radius`
- `InnerRadius`
- `Strength`
- `FalloffPower`
- `TimeScale`
- `Pulse`

### Ripple

- `WaveCount`
- `WaveAmplitude`
- `WaveSpeed`
- `WavePhase`

### Swirl

- `SwirlStrength`
- `RotationSpeed`

### Ring

- `RingRadius`
- `RingWidth`
- `RingStrength`
- `RingSpeed`

## 初期値

最初は `Mode=Ripple`、`CharaLayerName=Chara`、`OnlyHScene=true`。

ただ歪ませるだけの初期版なので、外部ディスプレイスメント画像は使わない。
