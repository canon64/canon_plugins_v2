# SimpleAfterimage

## Overview
- Character afterimage effect plugin for `KoikatsuSunshine` and `KoikatsuSunshine_VR`.
- Captures character-only frames and renders trailing ghost images.

## What It Does
- Adds residual image trails to characters
- Lets you tune fade frames, capture interval, tint, alpha scale, and fade curve
- Supports preset storage
- Supports beat-linked fade range options

## Target Processes
- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## Main Files
- `SimpleAfterimage.dll`
- `config.json`
- `SimpleAfterimage.log`

## Main Settings
- fade frames
- max slots
- capture interval
- tint RGBA
- alpha scale
- fade curve
- optional preset actions

## Notes
- Strong settings can easily wash out the image.
- Start from generated config values and tune gradually.


## PlaybackBar 残像タブ / VMD BPM連動

- 拡張タブは「鎖 → 残像 → 操作説明」。既存の通常設定は折りたたみ、VMD設定を最初に表示する。
- VMDの登録済み曲BPMとRhythmSyncの音源解析を使い、強=1倍、中=1/2、弱=1/4の実効BPMから残像を補間する。H動作の位相同期やVMDの独立キャラ指定は不要。リズム同期自体はONにする。
- 基準点30・220は固定。中間点は追加・編集・削除可能。寿命0.01〜10秒、発生間隔1〜5000ms、濃さ0〜1を設定する。間は線形補間。30未満はBPM30の寿命に30÷実効BPMを掛ける（BPM30の1/4なら寿命4倍）。間隔と濃さは30の設定を使い、220超は220の設定を使う。
- 曲BPM240の弱区間なら60として計算する。240を先に220へ切り詰めない。
- 寿命はキャプチャごとの実時間を保持する。通常描画・VR左右の描画で同じ計算を使用する。発生回数は描画FPSが上限。同時残像数が不足する場合は寿命を保つために発生間隔を寿命÷同時残像数以上へ自動調整し、適用値と理由をタブに表示する。
- `VmdAfterimageSettings.json` にBPM別設定を保存。通常設定は従来の `config.json`。プリセット保存は従来のデータとVMD用の `vmd-preset-*.json` を保存する。既存プリセットにVMD用データがない場合は現在のVMD設定を維持する。
- 未登録BPM、解析準備中、停止、リズム同期OFFは理由を表示し通常設定へ戻る。曲変更時は以前の残像と解析状態を破棄する。
- 外部warp演出の指定と外部グロー中の停止を維持。入力範囲・ドラッグ・カーソル保持は既存PlaybackBar/UnifiedUiホストの共通入力キャプチャを使用する。

### 検証

`dotnet run --project SimpleAfterimage/Tests/SimpleAfterimage.Tests.csproj -c Release`

BPM端点・中間点・範囲外・1/2/4変換・JSON往復・不正値・30/60/120FPSの時刻計算・解除を検証する。Unityの描画、VR実機、画面内の入力操作の成功はこのテストでは確認しない。
