# LiquidEffectPreview

Unity / BepInEx 5 用の汎用液体エフェクト・手動プレビュープラグインです。
カメラ前の設定位置から、細い立体の筋が短時間出て滴に分かれます。
独立した GUID `com.canon.liquideffectpreview` と設定ファイルを使用します。

## 操作

1. Configuration Manager の **LiquidEffectPreview** を開く。
2. `01.プレビュー / Enabled` を ON。
3. **F8** を押すと一回試射。`EmitOnce` を ON にしても一回試射できます。
4. `Paused` で停止。`Enabled` を OFF にすると噴出予定と表示を即座に消去します。

初期状態は無効です。試射キーは `FireKey` で変更できます。
キーを押し続けても連射しません。停止中の入力は予約されません。
数値項目は Configuration Manager のスライダー、色は色選択で変更できます。
新規起動後に `BepInEx/config/com.canon.liquideffectpreview.cfg` が生成されます。

| 項目 | 初期値 | 範囲 |
|---|---:|---:|
| 一回の噴出時間 | 100 ms | 20–300 ms |
| 筋の本数 | 3 | 1–5 |
| 筋の直径 | 2 mm | 0.5–5 mm |
| 滴に分かれるまで | 100 ms | 20–300 ms |
| 滴の数 | 49 | 1–80 |
| 滴の直径 | 5 mm | 1–40 mm |
| 滴の尾の最大長 | 5 cm | 0–15 cm |
| 上下角 | 50° | −90°–90°（水平基準） |
| 色 | 白 | RGBA |

速度・左右角・広がり・重力・滴の寿命・尾の有無と太さ・カメラからの位置も変更できます。
放出中の筋の形状設定は次回の試射に反映されます。色と尾の設定は表示中の液体にも反映されます。

## 描画と時間

- 放出を 1/240 秒刻みの履歴に分け、観測したノズル位置の間を補間します。
- 筋ごとの方向は試射時に固定。筋の本数と滴の数は独立しています。
- 放出位置と速度をワールド座標で保存し、重力の式から位置を計算します。
- 筋は 8 角形の断面。中心線の頂点間隔を最大 5 mm に分割し、断面の向きを連続させます。
- 分離した滴は同じ位置・速度を引き継ぎます。尾は軌道を遡った短い独立メッシュです。
- 筋は Standard 系の陰影付き材質、滴は専用の柔らかい画像、尾は透明材質を使用します。
- Unity の TrailModule は使用しません。
- 同時噴出は 4 回、滴は 500 個まで。上限では最も古いものを終了します。
- カメラ交換、シーン変更、ノズルが 1 フレームで 25 cm を超えて移動した場合は履歴を消去します。

Unity の既存メッシュと粒子配列を再利用します。32-bit インデックスを使用し、大きい尾メッシュでも
16-bit インデックス上限を超えられる構成です。
参考 API: [Mesh.indexFormat](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/Mesh-indexFormat.html)、
[ParticleSystem.SetParticles](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/ParticleSystem.SetParticles.html)。

## ビルドと検証

```powershell
dotnet run --project LiquidEffectPreview\Tests\LiquidEffectPreview.Tests.csproj -c Release
dotnet build LiquidEffectPreview\LiquidEffectPreview.csproj -c Release --nologo
```

テストは実際の計算・メッシュ生成ソースをリンクし、Unity・ゲームを起動せず実行します。
30/60/120fps、フレーム落ち、移動するノズル、停止、OFF、文脈変更、数量上限、
尾の長さ、筋の分離、断面・面の向き、真上の軌道などを検証します。

成果物: `LiquidEffectPreview/bin/Release/net472/LiquidEffectPreview.dll`

配置先: `F:\kks\BepInEx\plugins\canon_plugins\LiquidEffectPreview\LiquidEffectPreview.dll`

ゲーム終了後の配置は `LiquidEffectPreview\Deploy.ps1` で行えます。
ゲーム起動中・別の配置先に同名 DLL がある場合は中断します。
コピー後はサイズ・更新日時・SHA256・配置数を照合します。ゲームの終了・起動は行いません。

**ゲーム内の見た目は未検証です。** 計算テストとビルド成功は、実際のシェーダー、照明、
透過順序、滴への移行の見え方まで保証するものではありません。今回、ゲームの起動・操作は行っていません。
