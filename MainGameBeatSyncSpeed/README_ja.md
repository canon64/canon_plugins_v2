> 現在のビルドは MainGameRhythmSync への互換ブリッジです。旧ソースは保存されていますが csproj のコンパイル対象外です。以下は旧実装の説明です。

# MainGameBeatSyncSpeed

## 概要
- 動画/音源からビート強度を解析し、H速度へ反映するプラグインです。
- 楽曲ごとの BPM 保存、タップテンポ、モーション自動切替をサポートします。

## 依存関係
- 必須依存プラグインなし
- 対象プロセス: `KoikatsuSunshine`, `KoikatsuSunshine_VR`
- 任意連携: `MainGameBlankMapAdd`（動画再生時間/動画パス取得）
- 任意連携: `MainGameSpeedLimitBreak`（タップテンポ反映）
- 外部ツール: `ffmpeg.exe`（動画から WAV 抽出に使用）

## 導入
- `MainGameBeatSyncSpeed.dll` を `BepInEx/plugins/canon_plugins/MainGameBeatSyncSpeed/` に配置します。
- 動画から自動抽出を使う場合は、`ffmpeg.exe` を次のどちらかで用意してください。
  - 同梱（推奨）: `BepInEx/plugins/canon_plugins/_tools/ffmpeg/bin/ffmpeg.exe`
  - 既存環境: `PATH` に `ffmpeg.exe` が通っている状態

## 基本操作
- 既定ホットキー: `F9`（有効/無効の切替）
- BPM は Config の `Audio.Bpm` で設定します。
- 動画再生中は楽曲単位 BPM を自動読込/保存します。

## 設定とログ
- BepInEx設定: `BepInEx/config/com.kks.maingame.beatsyncseed.cfg`
- 楽曲BPMマップ: `SongBpmMap.json`
- ログ: `BepInEx/plugins/canon_plugins/MainGameBeatSyncSpeed/MainGameBeatSyncSpeed.log`

## 注意点
- `WavFilePath` 指定時はその WAV を優先利用します。
- 動画抽出が必要な場合、`ffmpeg.exe` が見つからないと解析できません。


