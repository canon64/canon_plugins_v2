# MainGameVrHipIkVoiceImpactBoost

VR本編で「男が女の腰にぶつかる瞬間」（突き入れ）を検知し、外部ボイスを一時的にブーストするBepInExプラグイン。

- 監視対象: `MainGirlHipHijack` の腰中央 BodyIK の世界位置
- 判定: 腰の動きを「腰→頭」方向に投影し、頭方向に進む速度がしきい値以上で発火
- 引き戻し（足方向）は無視される（投影値がマイナスになるため）
- 発火時の動作: `MainGameVoiceFaceEventBridge.PublicApi.TryRequestTransientVolumeBoost` を呼ぶ

## 依存プラグイン

- `MainGameVoiceFaceEventBridge` (`com.kks.maingame.voicefaceeventbridge`)
- `MainGirlHipHijack` (`com.kks.main.girlbodyikgizmo`)

両プラグインともVRプロセスで起動している必要がある。

## 対応プロセス

- KoikatsuSunshine
- KoikatsuSunshine_VR

## インストール

1. `MainGameVrHipIkVoiceImpactBoost.dll` を `BepInEx\plugins\canon_plugins\MainGameVrHipIkVoiceImpactBoost\` に配置
2. ゲーム起動
3. 初回起動時に同フォルダへ `MainGameVrHipIkVoiceImpactBoostSettings.json` が自動生成される

## 設定の変え方

設定の保存先は2か所あり、JSONがマスター、cfgがGUI操作用のミラー。

- **JSONマスター**: `BepInEx\plugins\canon_plugins\MainGameVrHipIkVoiceImpactBoost\MainGameVrHipIkVoiceImpactBoostSettings.json`
- **GUI**: BepInEx ConfigurationManager（F1）でプラグイン名 `MainGameVrHipIkVoiceImpactBoost` のセクションから全項目編集可
- **cfg**: `BepInEx\config\com.kks.main.vrhipikvoiceimpactboost.cfg`（ConfigurationManager の保存先）

### 同期ルール

| 契機 | 動作 |
|---|---|
| 起動時 | JSON → cfg に流し込む。JSONが無ければ既定値で新規作成 |
| ConfigurationManagerで変更 | cfg 更新 → 即 JSON に書き戻し |
| cfg ファイル直編集 | cfg 更新 → 即 JSON に書き戻し |
| JSON ファイル直編集 | 次回起動時に反映（実行中は監視しない） |

## 設定項目

### 1.全般

| 項目 | 既定値 | 説明 |
|---|---|---|
| Enabled | true | プラグイン全体のON/OFF |
| VerboseLog | false | 詳細ログをファイルに書く。普段OFF、調査時のみON |
| RequireVrActive | true | VRモード起動中だけ動かす |
| RequireBodyIkRunning | true | 腰BodyIKが動いてる時だけ動かす |

### 2.検出（突きを検知する条件）

| 項目 | 既定値 | 範囲 | 説明 |
|---|---|---|---|
| VelocityThresholdMps | 0.45 | 0.01〜10 | 頭方向への速さ(m/秒)。これ以上で突きとみなす。小さくすると敏感 |
| MinDeltaMeters | 0.005 | 0〜1 | 1フレームで頭方向に進む最小距離(m)。ノイズ除去 |
| SmoothingFactor | 0.35 | 0〜1 | 速度の平滑化。0=生、1=即応。小さいとブレに強いが鈍い |
| CooldownMs | 250 | 0〜5000 | 一度発火したら次まで何msあけるか。高速ピストンで取りこぼすなら下げる |

### 3.速度スケーリング（突きの強さで効果を変える）

| 項目 | 既定値 | 範囲 | 説明 |
|---|---|---|---|
| EnableSpeedScaling | true | - | 速度で効果を変えるか。OFFにするとセクション4の固定値を使う |
| SpeedMaxMps | 1.60 | 0.05〜10 | 「最強の突き」とみなす速度(m/秒)。これ以上は頭打ち |
| MinPeakMultiplier | 1.35 | 1〜5 | 弱い突き時のボイス音量倍率 |
| MaxPeakMultiplier | 2.00 | 1〜5 | 最強の突き時のボイス音量倍率 |
| MinSilenceMs | 60 | 0〜2000 | 弱い突き時、ブースト前に挟む無音時間(ms) |
| MaxSilenceMs | 180 | 0〜2000 | 最強の突き時の無音時間(ms) |

### 4.エンベロープ（音量変化のカーブ。スケーリング無効時に使用）

| 項目 | 既定値 | 範囲 | 説明 |
|---|---|---|---|
| PeakMultiplier | 1.8 | 1〜5 | スケーリング無効時の固定音量倍率 |
| AttackMs | 20 | 0〜2000 | 音量が最大に達するまでの時間 |
| HoldMs | 20 | 0〜2000 | 最大音量を保持する時間 |
| ReleaseMs | 220 | 1〜5000 | 最大から元音量に戻るまでの時間。長いと余韻 |
| SilenceMs | 100 | 0〜2000 | スケーリング無効時の固定無音時間 |
| Easing | CosineInOut | CosineInOut/Linear | 音量変化の形。CosineInOut=滑らか、Linear=直線 |

## よくある調整パターン

- 弱い突きでも反応してほしい → `VelocityThresholdMps` を下げる(0.30など)
- ピストン中ぜんぶ拾いたい → `CooldownMs` を下げる(100など) + `SmoothingFactor` を上げる(0.6など)
- ガクガク誤検出する → `MinDeltaMeters` を上げる(0.01) + `SmoothingFactor` を下げる(0.2)
- 強い突きと弱い突きの差を出したい → `MaxPeakMultiplier` を上げる + `MinPeakMultiplier` を下げる
- 無音演出いらない → `MinSilenceMs` と `MaxSilenceMs` を 0 にする

## ログ

プラグインフォルダ内に `MainGameVrHipIkVoiceImpactBoost.log` が生成される。発火時の速度・距離・倍率などが記録される。

## 内部構成

| ファイル | 役割 |
|---|---|
| `Plugin.cs` | BepInEx入口、JSON⇔cfg同期、VRガード、各サービス配線 |
| `PluginSettings.cs` | `ConfigEntry<T>` ラッパー、`Changed` イベント公開 |
| `SettingsJsonDto.cs` | JSONシリアライズ用DTO |
| `SettingsStore.cs` | JSON DTOのロード/セーブ |
| `HipIkTrackingPositionSource.cs` | MainGirlHipHijack の腰/頭位置取得APIをリフレクション呼び出し |
| `HipIkMotionDetector.cs` | 頭方向への進行速度を算出、しきい値判定 |
| `VoiceImpactBoostService.cs` | cooldown判定、ボイスブースト要求 |
| `PluginFileLogger.cs` | プラグイン専用ログ |

## バージョン

v0.2.0
