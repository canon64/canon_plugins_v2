# MainGameDisplacementMap

Koikatsu Sunshine / KKS VR 向け **画面全体ディスプレイスメントマップ波紋** プラグイン。

## 特徴

- SimpleAfterimage / MainGameCircularDisplacement で確立された「キャプチャ → OnPostRender オーバーレイ」パターンを踏襲
- **ディスプレイスメントマップ** (ランタイム生成 Texture2D) による有機的な波紋
- 同心円波 + 減衰 + 軽いノイズ + スクロールで自然な水面・熱波のような歪み
- Config から即時 `Enabled` 切り替え可能（最優先）
- 外部API (`SetExternalWarp`) を CircularDisplacement と同シグネチャで公開 → AstralMode からの将来対応が容易
- 全画面 (UI除外) / Chara レイヤー 両対応

## 設定 (BepInEx ConfigManager)

**00.General**
- `Enabled` — プラグインのマスターオンオフ（これをオフにすると完全停止）
- `OnlyHScene` — Hシーン中のみ
- `VerboseLog` — 詳細ログ

**20.MapGeneration** が本プラグインの核心:
- `MapResolution` (推奨 128〜256)
- `WaveCount` / `BaseFrequency` / `PropagationSpeed`
- `Damping` / `NoiseAmount`
- `ScrollSpeedX/Y`

## CircularDisplacement との違い

- Circular = 純粋数式 (Radial / Ripple / Swirl / Ring)
- 本プラグイン = **マップ駆動** でより複雑で有機的な波形を低コストで実現

両方共存可能。用途に応じて使い分けてください。

## ビルド・配置

```
dotnet build -c Release
```

生成された `bin/Release/net472/MainGameDisplacementMap.dll` を

`F:\kks\BepInEx\plugins\canon_plugins\MainGameDisplacementMap\`

へ配置（テスト用）。

## 注意

- 強めの設定にすると背景が激しく歪みます。最初は Strength 0.02 前後、Radius 0.8〜1.0 で調整を。
- マップ更新は軽量ですが、グリッド密度 (GridX/GridY) を上げすぎると描画コストが増えます。

## バージョン

0.1.0 — 初版 (マップ波紋コア実装 + Enabled 制御 + 外部API)
