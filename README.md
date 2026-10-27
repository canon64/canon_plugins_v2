# canon_plugins

`canon_plugins` は、KoikatsuSunshine（KKS / KKS VR）の本編Hを拡張する BepInEx プラグイン群です。
動画ルーム、体位・IK制御、外部ツール連携を軸に、カメラ・速度・映像演出・字幕・音声までを一式で扱います。

- ゲーム: `KoikatsuSunshine` / `KoikatsuSunshine_VR`（一部は `CharaStudio`）
- BepInEx: 5.x / .NET Framework `net472`
- DLL配置先: `BepInEx/plugins/canon_plugins/<プラグイン名>/<プラグイン名>.dll`

---

## 三本柱

このリポジトリの中心は次の3つです。他のプラグインは、ほぼこの3つを補強・拡張するために存在します。

### 1. MainGameBlankMapAdd — 動画ルーム基盤

blank マップ上に動画ルームを構築し、画面下端の再生バーからすべてを操作するハブ。

- 動画の再生 / 一時停止 / シーク / フォルダ再生 / ループ / タイル表示
- 部屋の SIZE・POSITION・ROTATION 調整（Gizmo 編集: `Ctrl+D`）
- 音量ゲイン、残響（REV / V-REV）、Hボイス音源の動画ルーム追従
- フォルダ単位（`SaveF`）と動画単位（`SaveV`）の二段プロファイル保存
- HTTP 制御エンドポイント（`HttpEnabled` / `HttpPort`）
- 再生バーから他プラグインをトグル: `HipUI` / `ClubUI` / `AfterImage` / `人形` / `体位変更` / `状況Auto` / `状況送信` / `AutoVoice`

必須依存: `MainGameTransformGizmo`（→ `MainGameLogRelay`）
保存先: `MapAddSettings.json` / `RoomLayoutProfiles.json`
詳細: [MainGameBlankMapAdd/README_ja.md](MainGameBlankMapAdd/README_ja.md)

### 2. MainGirlHipHijack — 女性IK・姿勢制御

Hシーン中の女性側 BodyIK とポーズ運用の中核。

- 女性 BodyIK 13エフェクタ（手/足/肩/腿/肘/膝/腰中央）の有効化・ウェイト・ギズモ編集
- ボーン追従（Nearest Follow、ミラー、距離しきい値、VR時の頭ボーン→HMD 変換）
- 女性頭角度操作（スライダー / 回転ギズモ / 体位変更後の維持切替）
- スクショ付きポーズプリセット。体位一致時の自動呼び出し、遷移秒数・イージング補間
- HipQuickSetup（既定 `Enter`）で腰IK + SpeedHijack を一括ON/OFF、待機中の腰動きから自動挿入
- VR掴み: トリガー + `B` で腰IKを左コントローラーへ親子付け

必須依存: `MainGameTransformGizmo` / `MainGameUiInputCapture` / `MainGameLogRelay`
詳細: [MainGirlHipHijack/README_ja.md](MainGirlHipHijack/README_ja.md)

### 3. MainGameVoiceFaceEventBridge — 外部連携ブリッジ

外部ツール（`human_2_KKS_pipeline` など）からコマンドを受け、ゲーム内へ反映する中核。

- 受信経路: NamedPipe `kks_voice_face_events`（既定） / HTTP `:18765/voice-face-event`（任意）
- `{"type":"speak"}` — 外部音声再生 + 表情制御
- `{"type":"stop"}` — 外部再生停止
- `{"type":"response_text"}` — 返答文を解析して衣装 / 体位 / 動画再生を遅延実行
- 体位選択は `pose_score_rules.json` のカテゴリ推定 + スコアルール、実在体位は `pose_sonyu_classified.json` / `pose_houshi_classified.json` から解決
- 「流す」+ 動画名で `BlankMapAdd` の `/videoroom/play` へ内部送信して動画再生

詳細: [MainGameVoiceFaceEventBridge/README_ja.md](MainGameVoiceFaceEventBridge/README_ja.md)

---

## 全プラグイン一覧

### 動画・速度・リズム

| プラグイン | 概要 |
|---|---|
| `MainGameBlankMapAdd` | 動画ルーム構築と再生バー（三本柱） |
| `MainGameAllPoseMap` | FreeH で全体位を扱える専用マップを追加（既定 `AddedMapNo=900`） |
| `MainGameSpeedLimitBreak` | バニラ上限を超えた速度レンジ、BPM速度プリセット、リマップ、動画時間キュー |
| `MainGameBeatSyncSpeed` | 動画/音源のビート解析を速度へ反映。曲別BPM保存、タップテンポ（`右Ctrl`）。`ffmpeg.exe` 使用 |

### IK・体位・キャラ状態

| プラグイン | 概要 |
|---|---|
| `MainGirlHipHijack` | 女性 BodyIK / ポーズプリセット / VR掴み（三本柱） |
| `MainGirlShoulderIkStabilizer` | 肩まわりの AdvIK 安定化を本編へ適用 |
| `MainGameAdvIkBridge` | `AdvIKPlugin` のコントローラ設定をリフレクション経由で操作するブリッジ |
| `MainGameHScenePoseInjector` | Hシーンへ外部定義のポーズを注入 |
| `MainGameHHandPoseProbe` | H中の手ポーズパターンを調査・適用するプローブUI |
| `MainGameFreeHMasturbationMenu` | FreeH の通常アクションメニューにオナニー項目を追加、体位巡回 |
| `MainGameDollMode` | 人形モード。目ハイライトOFFを維持し、他プラグインへ API 公開 |
| `MainGameFaceSweatControl` | 顔の汗・液体・ツヤ表現を制御 |
| `MainGameCoordinateCardSwap` | Hシーン中に男女のコーデ／キャラカードを差し替え |
| `MainGameCharacterMorphBridge` | Hシーン中のキャラモーフ（色 / グロー / メイク / オーバーレイ / サウンド）を橋渡し |
| `MainGamePregnancyPlusBridge` | `KKS_PregnancyPlus` の膨らみパラメータをプリセット制御 |

### 音声・字幕・デバイス

| プラグイン | 概要 |
|---|---|
| `MainGameVoiceFaceEventBridge` | 外部コマンド受信の中核（三本柱） |
| `MainGameSubtitleCore` | 字幕表示コア。キュー管理、`Auto / InformationUI / Overlay` バックエンド、手動入力パネル |
| `MainGameSubtitleEventBridge` | HTTP `:18766/subtitle-event` で字幕要求を受け Core へ転送。Core へ HardDependency |
| `MainGameAutoHVoice` | 直近に流れたボイスIDを捕捉し、一定間隔で再発話 |
| `MainGameVoiceImpactBoost` | 突き入れ検知で外部ボイスを一時ブースト（BlankRoom 内限定、`PregnancyPlusBridge` 必須） |
| `MainGameVrHipIkVoiceImpactBoost` | VR で `MainGirlHipHijack` の腰 BodyIK 位置から突き入れを検知してブースト |
| `MainGameVrVoiceGate` | VR コントローラのボタンを音声入力ゲート（録音トリガー）として外部送出 |
| `MainGameHandySync` | HScene のピストン位相を抽出し The Handy（Handy2）へリアルタイム位置指令 |

### カメラ・撮影

| プラグイン | 概要 |
|---|---|
| `MainGameCameraControl` | `CameraControl_Ver2` の状態をプリセット保存し補間呼び出し。FOV / ボーン追従 / `ksFPV` |
| `MainGameSubCameraDisplayProbe` | サブカメラを立てて画面内ディスプレイに表示。撮影、解像度プリセット、BeatSync、VR対応 |
| `MainGameLiveLookCamera` | ライブ配信風の追従カメラ（通常保存 / ボーン保存、頭・胸・腰ターゲット） |
| `MainGameAlbumAspect` | アルバム（`CapFileViewer`）の引き伸ばし表示をアスペクト比維持に補正 |

### 映像演出

| プラグイン | 概要 |
|---|---|
| `SimpleAfterimage` | キャラ残像。寿命・間隔・色・フェード曲線・プリセット・Beat連動 |
| `SimpleAfterimage_VR` | `KoikatsuSunshine_VR` 専用の残像版 |
| `MainGameCharacterAfterimage` | 背景とキャラの間にレイヤ描画する残像パイプライン |
| `MainGameVrAfterimageLab` | VR残像の左右描画方式を実機で詰めるための実験台（全パラメータをConfig化） |
| `MainGameGlow` | キャラだけを別カメラで切り出し、Bloom で発光させて本編画面へ重ねる |
| `MainGameCircularDisplacement` | Chara レイヤーを円形パラメータでUV歪みさせる演出 |
| `MainGameDisplacementMap` | ランタイム生成のディスプレイスメントマップによる全画面波紋 |
| `MainGameClubLights` | クラブライトを複数追加。色・強度・照射角・公転/自転・ストロボ・レインボー、BeatSync 連動 |
| `MainGameVrScreenEffectProbe` | VR の各眼カメラへスクリーンエフェクト層を付与するプローブ |
| `AstralMode` | 同時絶頂を契機に現実を凍結し、別世界（アストラル界）へ飛ぶ演出モード |

### 基盤（単体用途ではなく他プラグインを支える）

| プラグイン | 概要 |
|---|---|
| `MainGameLogRelay` | owner キー単位のログ振り分けとファイル出力を集約 |
| `MainGameUiInputCapture` | UI ドラッグ中のカーソル/カメラ入力の奪い合いを調停 |
| `MainGameTransformGizmo` | 共有 Transform ギズモと `TransformGizmoApi.Attach(...)` |
| `MainGameUnifiedUi` | 各プラグインの UI をタブとして集約する統合ウィンドウ |
| `MainGameObjectComposer` | 任意オブジェクトの配置・親子付け・モーション・プリセット・Undo/Redo。HipHijack 追従連携 |
| `MainGameStudioItemSummon` | Studio アイテムを本編シーンへ召喚 |
| `_shared` | `BlankRoomGate` / `TextInputGate` の共有ソース（各 csproj からリンク参照） |

### Studio / 診断

| プラグイン | 概要 |
|---|---|
| `StudioFacePresetTool` | `CharaStudio` 用の表情プリセットツール |
| `StudioCharaStateLogger` | Studio のキャラ状態フルダンプ（汗 / ツヤ / 液体 / 頬 / 涙の調査用） |
| `UnityResourceDiagnostics` | Unity 上に残っている主要リソース数を `_logs/resource_diagnostics.txt` へ記録 |

---

## 導入

1. BepInEx 5.x が導入済みの KKS を用意する。
2. `BepInEx/plugins/canon_plugins/` 配下に、プラグインごとのサブフォルダを作って DLL を置く。
3. 依存プラグインを先に満たす（下記チェーン）。

主要な依存チェーン:

```
MainGameLogRelay
  └─ MainGameTransformGizmo
       ├─ MainGameBlankMapAdd
       ├─ MainGirlHipHijack   (+ MainGameUiInputCapture)
       └─ MainGameSubCameraDisplayProbe (+ MainGameUiInputCapture)

MainGameSubtitleCore
  └─ MainGameSubtitleEventBridge

MainGamePregnancyPlusBridge
  └─ MainGameVoiceImpactBoost

MainGameVoiceFaceEventBridge
  └─ MainGameVrHipIkVoiceImpactBoost
```
