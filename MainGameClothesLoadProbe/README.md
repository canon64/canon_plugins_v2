# MainGameClothesLoadProbe

KKS のコーデ切り替え経路を変更せず、各処理時間とフレーム時間を CSV に記録する診断専用プラグイン。

## 静的解析で確認した経路

`ChaControl.ChangeCoordinateTypeAndReload` は、コーデの `SaveBytes` / `LoadBytes` を経て `Reload(false, true, true, true)` を呼ぶ。`Reload` は同期の `ReloadNoAsync` を呼び、服 (`ChangeClothes`) とアクセサリー (`ChangeAccessory`) を同一呼び出し内で作り直す。`ReloadAsync` は存在するが、この経路では呼ばれない。

## 記録

- 出力: `F:\kks\BepInEx\clothes-load-probe\trace-YYYYMMDD-HHMMSS.csv`
- コーデ切り替え時に自動で 15 秒記録。`F10` で手動記録。
- `method` 行: 各メソッドの同期所要時間、GC回数、管理ヒープ差分。入れ子の時間は重複するため合算しない。
- `frame` 行: Update 間の経過時間。GPU の描画時間そのものではない。
- Config で閾値・記録時間を変更できる。

次の実測では、同じ服を二回切り替えて初回と再読み込みを比較し、CSV の最大フレーム時間と `ReloadNoAsync` / `ChangeClothes*` / `LoadCharaFbxData` / `ChangeAccessory*` の時間を照合する。診断プラグイン自体の影響を見るため、測定後に無効化した条件でもフレーム時間を比較する。

`python analyze.py <CSV>` でセッションごとの最大フレーム時間と重いメソッドを表示する。
