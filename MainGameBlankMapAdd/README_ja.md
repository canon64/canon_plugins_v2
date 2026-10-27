# MainGameBlankMapAdd

MainGame の blank map に動画ルームを構築し、
再生バーから動画再生・部屋配置・音量補正・プロファイル保存を行うプラグインです。

## 対応環境
- ゲーム: KoikatsuSunshine
- BepInEx: 5.x
- プロセス: `KoikatsuSunshine`

## 依存関係
- 必須: `MainGameTransformGizmo.dll`
- 必須チェーン上で必要: `MainGameLogRelay.dll`（TransformGizmo 側依存）
- 任意連携:
  - `MainGameBeatSyncSpeed.dll`（BEAT SYNC 連携）
  - `MainGameSpeedLimitBreak.dll`（速度制御連携）
  - `MainGameAllPoseMap.dll`（動画マップ運用補助）

## 導入手順
1. `MainGameBlankMapAdd.dll` を `BepInEx/plugins/canon_plugins/MainGameBlankMapAdd/` に配置する。
2. 依存DLLを配置する。
   - `BepInEx/plugins/canon_plugins/MainGameTransformGizmo/MainGameTransformGizmo.dll`
   - `BepInEx/plugins/canon_plugins/MainGameLogRelay/MainGameLogRelay.dll`
3. ゲーム起動後、MainGame/Hシーンで動画マップを開く。

## 最短クイックスタート
1. 画面下端にマウスを寄せて再生バーを表示する。
2. `フォルダ登録` で動画フォルダを追加する。
3. `Folder` でフォルダを選び、`Video` で動画を選ぶ。
4. 再生しながら部屋位置や音量を調整する。
5. 共通設定は `SaveF`、動画専用設定は `SaveV` で保存する。

## ホットキー
- `Ctrl+P`: 動画再生/一時停止
- `Ctrl+R`: 設定再読み込み
- `Ctrl+D`: Gizmo 編集モード ON/OFF

## 再生バーの見方

### 折りたたみと説明
- `▲ / ▼`: 上段パネル（部屋調整・AUDIO/SAVE・BEAT SYNC）の開閉
- `説明` チェック: ON でホバー説明を表示、OFF で非表示
- `HipUI` チェック（`説明` の右側）: `MainGirlHipHijack` UI の表示/非表示を切り替える
- `ClubUI` チェック: `MainGameClubLights` UI の表示/非表示を切り替える
- `AfterImage` チェック: `SimpleAfterimage` の有効/無効を切り替える
- `人形` チェック: `MainGameDollMode` の有効/無効を切り替える
- `体位変更` チェック: `MainGameVoiceFaceEventBridge` の体位変更機能を ON/OFF する
- `状況Auto` チェック: `MainGameVoiceFaceEventBridge` の状況文定期送信を ON/OFF する
- `状況送信` ボタン: `MainGameVoiceFaceEventBridge` の状況文をその場で1回送信する
- `AutoVoice` チェック: `MainGameAutoHVoice` UI の表示/非表示を切り替える
- `MainGirlHipHijack` 未ロード時はトグルが無効化される
- ほかの連携トグルも対象プラグイン未ロード時は無効化される

### 下段（常時操作）
- `Play / Pause / Stop`: 再生制御
- `|< / >|`: 前後動画へ移動
- `1Loop`: 現在動画をループ
- `Loop`: フォルダ末尾時に先頭へ戻る
- `Tiles:n`: 表示タイル数切替
- `フォルダ登録`: 再生対象フォルダ追加
- `Folder`: 登録済みフォルダ選択
- `Video`: フォルダ内動画選択
- スライダー:
  - `再生位置`: シーク
  - `VOL`: 動画音量
  - `REV`: 残響強度
  - `V-REV`: 動画音への残響適用

### 上段（`▲` 展開時）
- `SIZE / POSITION / ROTATION`: 部屋のスケール・位置・回転調整
- 数値欄とスライダーは同じ値を操作

#### AUDIO / SAVE
- `Gain`: 動画音声ゲイン（0.1〜6.0）
- `RoomF` / `RoomV`: 部屋レイアウト保存
- `GainF` / `GainV`: 音量ゲイン保存

#### BEAT SYNC
- `Enabled / AutoMotion / AutoThreshold / VerboseLog` などを調整
- `SaveF` / `SaveV` でフォルダ・動画個別に保存
- `AutoMotion / AutoThreshold / VerboseLog` は `MainGameBeatSyncSpeed` へグローバル連携されるトグル

## 保存仕様（重要）
- 保存ファイル:
  - `MapAddSettings.json`（通常設定）
  - `RoomLayoutProfiles.json`（フォルダ設定/動画個別設定）
- 適用優先:
  - 同一カテゴリで動画個別設定があれば最優先
  - 動画個別設定がなければフォルダ設定を適用
- 主な保存カテゴリ:
  - 部屋レイアウト
  - 動画ゲイン
  - BeatSync 関連値

## 主な設定項目
- `MapAddSettings.json`
  - `EnablePlaybackBar`: 再生バー自体の有効/無効
  - `EnableUiHelpPopup`: ホバー説明ポップアップの有効/無効
  - `FolderPlayPath` / `FolderPlayPaths`: 現在のフォルダ再生対象と登録済みフォルダ一覧
  - `FolderPlayLoop` / `FolderPlaySingleLoop` / `FolderPlaySortMode`: フォルダ再生の挙動
  - `VideoAudioGain`: 動画音声の追加ゲイン倍率
  - `ApplyReverbToVideoAudio`: 動画音声へ部屋リバーブを適用するか
  - `SyncVoiceSourcesToVideoRoom`: Hボイス音源を動画ルーム座標へ寄せるか
  - `HttpEnabled` / `HttpPort`: 外部HTTP制御の有効/ポート
  - `WebCamRequestedWidth` / `WebCamRequestedHeight` / `WebCamRequestedFps`: WebCam取得要求フォーマット
  - `WebCamStatusLogIntervalSec` / `WebCamBlackSampleIntervalSec`: WebCam診断ログ間隔

## 出力ファイル
- `BepInEx/config/com.kks.maingameblankmapadd.cfg`
- `BepInEx/plugins/canon_plugins/MainGameBlankMapAdd/MapAddSettings.json`
- `BepInEx/plugins/canon_plugins/MainGameBlankMapAdd/RoomLayoutProfiles.json`
- `BepInEx/plugins/canon_plugins/MainGameBlankMapAdd/_logs/info.txt`
- `BepInEx/plugins/canon_plugins/MainGameBlankMapAdd/webcam_devices.txt`

## トラブルシュート
- 再生バーが出ない:
  - MainGame/Hシーンか確認
  - 画面下端にマウスを移動
  - `EnablePlaybackBar` が OFF でないか確認
- 動画が再生されない:
  - フォルダ登録・`Folder`/`Video` 選択状態を確認
  - 対象フォルダに対応拡張子の動画があるか確認
- BEAT SYNC が使えない:
  - `MainGameBeatSyncSpeed.dll` の配置とログを確認
- Gizmo が動かない:
  - `MainGameTransformGizmo.dll` と `MainGameLogRelay.dll` の配置を確認

