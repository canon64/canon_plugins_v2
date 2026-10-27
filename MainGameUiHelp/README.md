# MainGameUiHelp

canon_plugins のゲーム内 IMGUI 設定に共通の説明ポップアップを表示する。
項目名、チェック、スライダー、隣の数値欄へマウスを置くと、0.3秒後に説明を表示する。
スライダーと数値欄では、その行の説明に現在値と実際の設定範囲を添える。

## 説明データ

- `tools/help_texts.py`: 数値例や効果を含む日本語説明。ディルドの設定項目、Composerのレーン・L1/L2・軌道、カメラ、照明など。
- 各正規ソースの `Config.Bind`、`ConfigDescription`、既存の `tooltip:` も取り込む。
- 起動後にBepInEx設定の説明と表示名を補完する。
- `ui_inventory.json`: 独自IMGUIを持つ27プロジェクトの固定ラベルのソース位置。
- `coverage.json`: 手書き説明、ソース由来説明、単位・表示名に基づく共通説明の内訳。静的ラベル数は実機表示の検証数ではない。
- `help_catalog.json`: 配置する説明データ。DLLの隣に必須。

見出し・状態表示も一覧に含む。説明文が「表示するだけ」の項目と、値を変更する設定は区別する。
動的に生成されるファイル名・プリセット名の全組合せを静的ラベル一覧が網羅するわけではない。

## 表示と入力

実際のUnity 2019.4の管理メソッド `GUI.DoLabel / DoButton / DoToggle / DoTextField / Slider / DoButtonGrid` から、
現在マウスがある行の名前と範囲だけを観測する。対象は `BepInEx/plugins/canon_plugins` 内の呼び出し元。
既存のUIコールバックやスライダーの値、戻り値、`hotControl`、イベントを変更しない。
ポップアップは描画のみで、入力捕捉は各ホストの既存 `UiInputCaptureApi` が引き続き所有する。
現在のスクロール領域で見える部分だけを対象にし、別行のラベルを数値欄へ流用しない。

F1のMainGameUiHelp設定から、有効・表示待ち時間・文字サイズ・枠の幅を変更可能。
新規DLLのため、ゲーム起動中に配置した場合は次の起動から読み込まれる。

## 更新・検証

```powershell
python MainGameUiHelp/tools/inventory_ui.py
python MainGameUiHelp/tools/build_catalog.py
dotnet build MainGameUiHelp/MainGameUiHelp.csproj -c Release
dotnet run --project MainGameUiHelp/Tests/MainGameUiHelp.Tests.csproj -c Release
```

オフライン検証では説明キーの重複・静的ラベルの欠落、深い一撃の2条件の区別、
複数行ラベルと数値欄の対応、Unity本体の実際の呼び出し経路とHarmony引数を確認する。
この検証は、ゲーム内の見え方・位置・ホバー操作の実機確認を代用しない。

配置元はこのプロジェクト自身の `bin/Release/net472/MainGameUiHelp.dll` と `help_catalog.json`。
配置先は `F:\kks\BepInEx\plugins\canon_plugins\MainGameUiHelp\`。
