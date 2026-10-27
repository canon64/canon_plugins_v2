# MainGameGlow

## 概要
- 本編 / VR 向けのキャラ発光表現プラグイン
- キャラだけを別カメラで切り出し、Bloomで発光抽出してから本編画面の上に重ねる
- 本編の画は変えず、キャラだけが光るオーラのような効果になる

## 対象プロセス
- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## 使い方

設定はすべて **BepInEx ConfigurationManager** から行う。専用UIは無い。

1. ゲーム起動
2. **F1キー** で ConfigurationManager を開く
3. プラグイン一覧から「**MainGameGlow**」を探す
4. 5つのカテゴリ（01〜05）の値を調整

### 01. General（基本）
| 項目 | 意味 |
|---|---|
| Enabled | プラグイン全体のON/OFF |
| Verbose Log | 詳細ログ出力。普段OFF、不具合調査時にON。出力先は `MainGameGlow.log`（DLLと同じフォルダ） |

### 02. Capture（キャプチャ解像度）
| 項目 | 意味 |
|---|---|
| Use Screen Size | ON推奨。画面解像度と同じサイズで発光描画する |
| Capture Width / Height | Use Screen Size が OFF の時に使う固定解像度 |
| Character Layer Name | 発光対象のレイヤー名（既定 `Chara`）。指定レイヤーのオブジェクトだけが光る |

### 03. Glow（発光の本体）← メインで触る場所
| 項目 | 意味 | 推奨値 |
|---|---|---|
| Glow Threshold | 「この明るさ以上を光らせる」閾値。0で全部光る、大きいほど明るい部分だけ光る | 0.5〜1.5 |
| Glow Strength | 発光の強さ（Bloom Intensity）。0で発光なし、10で最大 | 1.0〜3.0 |
| Glow Blur Percent | ぼかし量。0でシャープ、100でふんわり広がる | 30〜60 |

**Strength または Blur Percent が 0 だと発光しない**。両方 >0 で初めて効く。

### 04. Overlay（発光色と全体透明度）
| 項目 | 意味 |
|---|---|
| Tint R / G / B | 発光色のRGB（0〜1）。`(1, 1, 1)` で白。`(0.3, 0.6, 1.0)` で青っぽい光 |
| Tint A | 色そのもののアルファ |
| Overlay Alpha | 最終合成アルファ |

最終的な見た目の透明度 = `Tint A × Overlay Alpha`。両方1.0で最大、どちらか0で透明（=見えない）。

### 05. Source Camera（発光対象画面の特定）— 普段は触らない
| 項目 | 意味 |
|---|---|
| Prefer Camera.main | ON推奨。`Camera.main` を優先 |
| Camera Name Filter | カメラ名の部分一致フィルタ。`Camera.main` がうまく取れないときのチューニング |
| Camera Fallback Index | 候補リストの何番目のカメラを使うか（0が最優先） |

## プリセット例

**柔らかい白い発光（既定相当）**
- Threshold 1.0 / Strength 2.0 / Blur 35
- Tint (1, 1, 1, 1) / Overlay Alpha 1.0

**強い青いオーラ**
- Threshold 0.5 / Strength 5.0 / Blur 60
- Tint (0.3, 0.6, 1.0, 1.0) / Overlay Alpha 1.0

**ふんわり淡い赤**
- Threshold 1.5 / Strength 1.5 / Blur 80
- Tint (1.0, 0.4, 0.4, 1.0) / Overlay Alpha 0.5

**オフ（発光しない）**
- Enabled OFF、または Strength 0、または Blur Percent 0

## 仕組み

1. 本編カメラ（`Camera.main` か候補から特定）と同じ位置・向き・FOVのコピーカメラを内部で作る
2. そのコピーカメラで「Character Layer」だけを黒背景の RenderTexture に描画
3. その RT に Unity の PostProcess Bloom をかけて発光部分を抽出＋ぼかす
4. 本編カメラの最終描画タイミング（OnPostRender）に、発光RTを Tint付きで重ねる

つまり「本編はそのまま、その上にキャラだけが光る画像を被せる」方式。本編側のレンダリングや色味には影響しない。

## トラブルシューティング

| 症状 | 確認 |
|---|---|
| 発光しない | `Enabled` ON、`Strength` >0、`Blur Percent` >0 を確認 |
| 一部キャラだけ光る／光らない | `Character Layer Name` がそのキャラのレイヤーと合っているか確認 |
| ログ確認 | `F:/kks/BepInEx/plugins/canon_plugins/MainGameGlow/MainGameGlow.log`（`Verbose Log` ON で詳細） |
| pipeline pending と出続ける | `PostProcessLayer` または `PostProcessResources` が見つからない。シーンが変わると解決することがある |

## ファイル構成
- `MainGameGlow.dll`
- `MainGameGlow.log`（実行時に自動生成）

## 注意点
- ゲーム制御ではなくレンダリング効果のみ。挙動には影響しない
- キャプチャと Bloom のパイプラインは内部で自動管理される
- 他の canon_plugins とは独立（依存なし）
