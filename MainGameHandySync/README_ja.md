# MainGameHandySync

KKS本編HシーンのアニメーションとThe Handy（Handy2）を連動させるプラグイン。

## 主な機能（MVP）
- HScene中のピストン位相をボーン基準で抽出（フォールバックあり）
- Connection KeyのみでThe Handyにリアルタイム位置指令（v2互換）
- ストローク範囲・反転・更新レート等の設定可能
- 位相ログで動作確認しやすい

## 使い方
1. Releaseビルド → DLLを `BepInEx/plugins/canon_plugins/` へ配置
2. 設定でConnectionKeyを入力（handyfeeling.comのデバイスページ参照）
3. Hシーン（挿入系）に入ると連動開始
4. ログ `MainGameHandySync.log` に位相サンプリングが出る

ライブ経路はクラウド遅延があるため、最高品質はFunscript録画＋外部再生を推奨（今後強化）。

詳細は `README.md` を参照。
