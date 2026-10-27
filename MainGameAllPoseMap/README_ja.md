# MainGameAllPoseMap

## 概要
- FreeH で全ポーズを扱える専用マップを追加するプラグインです。
- 仮想Hポイントの生成やカテゴリ制限の緩和を行います。
- デフォルト対象マップは `AddedMapNo=900`（動画マップ想定）です。
- `AddedMapNo` で指定した map が既存ならその map を対象化し、存在しない場合のみ新規作成します。

## 依存関係
- 必須依存プラグインなし
- 対象プロセス: `KoikatsuSunshine`, `KoikatsuSunshine_VR`

## 導入
- `MainGameAllPoseMap.dll` を `BepInEx/plugins/canon_plugins/MainGameAllPoseMap/` に配置します。

## 基本操作
- 本プラグインは常駐型です。基本は設定ファイルで動作を制御します。
- `AllPoseMapSettings.json` の `AddedMapNo` を変更すると、任意の map 番号を対象にできます。
- 既存 map を指定すればその map に AllPose 拡張を適用します。

## 設定とログ
- JSON設定: `AllPoseMapSettings.json`
- ログ: `BepInEx/plugins/canon_plugins/MainGameAllPoseMap/MainGameAllPoseMap.log`

## 注意点
- `AddedMapNo` に既存 map を指定する運用が可能です（重複は許容）。
- `AddedMapNo` に未使用番号を指定した場合は、その番号で新規 map を作成します。
- `EnableVirtualPoints` を有効化すると仮想ポイントが追加され、体位選択の幅が広がります。


