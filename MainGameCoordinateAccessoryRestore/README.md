# MainGameCoordinateAccessoryRestore

本編でコーディネート変更が成功すると、変更後の全アクセサリーの表示状態をONに戻す。
Y/U自体にはフックせず、ChangeCoordinateType / SetNowCoordinateの成功を検知する。
読み込みや他MODの遅延処理を考慮し、既定では変更後1秒間LateUpdateで再適用する。
期間終了後は手動の非表示操作を妨げない。連続変更時は期間を更新する。

ゲームのSetAccessoryStateAllを呼ぶため、導入済みMoreAccessoriesの追加スロット対応も利用する。
変更先コーデに存在しないアクセサリーのコピーや、欠落したMOD素材の復元は行わない。
アクセサリー表示を継続的に上書きする別MODへの恒久的な強制表示ではない。
カード・セーブへの保存処理は行わない。

設定: BepInEx/config/canon.maingame.coordinateaccessoryrestore.cfg
- Enabled: 有効/無効
- RestoreSeconds: 再適用期間（0.1～5秒、既定1秒）

確認手順: ゲーム再起動後、非表示のアクセサリーがあるキャラでY/Uによる衣装変更を行い、
変更先に存在する通常・追加スロットのアクセサリーが表示されることを確認する。
1秒経過後は手動で非表示にできることも確認する。
