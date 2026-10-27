using UnityEngine;

namespace MainGameCameraControl
{
    internal sealed partial class Plugin
    {
        private void DrawHelpTab()
        {
            var heading = new GUIStyle(GUI.skin.label) { wordWrap = true };
            var body = new GUIStyle(GUI.skin.label) { wordWrap = true };

            HelpSection("呼び出しタブ", heading);
            HelpLine("一覧のチェック：自動カメラ切替で使うプリセットを選びます。▷／▽：詳細を開閉。名前：そのプリセットを呼び出します。↻：現在の体位の構図で更新し、未登録の体位なら追加します。✎：改名。x：削除。", body);
            HelpLine("詳細の「通常／ボーン」：選択したプリセットを通常カメラ／ボーン追従に切り替えます。「頭／胸／腰」：追従対象を切り替えます。「↻ 現在体位を保存」：このプリセットへ今の体位を保存します。", body);
            HelpLine("詳細の「位置」：保存済みのカメラ位置を使用するか切り替えます。「位置を全部使う」はそのプリセット内、「位置を全部使う（全プリセット）」は全プリセットの位置指定を一括変更します。体位行の x はその体位だけを削除します。", body);
            HelpLine("自動カメラ切替／滞在時間：チェックしたプリセットを指定間隔で切り替えます。体位で自動切替：体位に合わせて保存した構図へ切り替えます。拍FOVズーム／ズーム倍率：拍に合わせて画角を変化させます。", body);
            HelpLine("遷移時間／イージング：呼び出し時の移動時間と動き方を設定します。解除：ボーン追従を解除します。Reset：カメラ追従状態をリセットします。", body);

            HelpSection("保存タブ", heading);
            HelpLine("現在FOV：今の画角を調整します。保存モード「通常／ボーン」：固定構図かボーン追従かを選びます。対象ボーン「頭／胸／腰」：追従先を選びます。", body);
            HelpLine("相対方向で保存：キャラ基準の方向と距離を保存します。ロールも追従：寝姿勢などで画面の傾きも追従します。体位ごとに保存：現在体位の構図を記録します。", body);
            HelpLine("保存名と ＋／↻ 保存：新しい名前なら新規保存、同じ名前なら上書きします。＋ 体位を追加／↻ 体位を更新：選択中のプリセットに現在体位を登録します。▷／▽：選択中プリセットの詳細を開閉します。", body);
            HelpLine("注視点オフセット X／Y／Z：ボーン追従中の注視位置を調整します。呼び出しタブの詳細にも表示されます。", body);

            HelpSection("設定タブ", heading);
            HelpLine("FOVも適用：プリセット呼び出し時に画角も反映します。相対方向の3項目：手動移動後の角度更新、床下への移動防止、腰が倒れた際の向きの補正を個別に切り替えます。", body);
            HelpLine("詳細ログ：診断ログを記録します。VoiceFaceEventカメラトリガー：対応イベントでカメラを切り替えます。「後に戻す」と滞在時間：イベント後に元の構図へ戻す設定です。", body);
            HelpLine("ギズモ表示／ギズモサイズ：注視点の目印を表示し、大きさを調整します。現在値（デバッグ）：カメラの座標・画角などを表示します。", body);
        }

        private static void HelpSection(string title, GUIStyle style)
        {
            GUILayout.Space(8f);
            GUILayout.Label(title, style);
        }

        private static void HelpLine(string description, GUIStyle style)
        {
            GUILayout.Label(description, style, GUILayout.ExpandWidth(true));
        }
    }
}
