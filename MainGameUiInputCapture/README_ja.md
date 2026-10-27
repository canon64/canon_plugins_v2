# MainGameUiInputCapture

## 概要
- MainGame / VR 系プラグイン向けの共有 UI 入力調停プラグインです。
- UIドラッグ中に複数ツールがカーソルやカメラ入力を奪い合うのを防ぎます。

## できること
- owner / source トークン単位で入力状態を管理
- UI操作中だけ入力ロックを一時解除
- キャプチャ終了後に入力状態を復元
- アイドル時カーソル解放モード
- 他プラグイン向け共有 API 公開

## 対象プロセス
- `KoikatsuSunshine`
- `KoikatsuSunshine_VR`

## 依存関係
- ハード依存: `MainGameLogRelay`

## 公開 API
- `Sync`
- `Begin`
- `Tick`
- `End`
- `EndOwner`
- `SetIdleCursorUnlock`
- `IsOwnerActive`
- `SetOwnerDebug`
- `IsAnyActive`
- `GetStateSummary`

## 主なファイル
- `MainGameUiInputCapture.dll`
- `MainGameUiInputCaptureSettings.json`

## 注意点
- これは見える機能を直接増やすプラグインではなく、調停用の基盤です。
- IMGUI のドラッグUIを持つプラグインは、ローカル実装ではなくこの API を使う前提です。
