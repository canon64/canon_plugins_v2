using UnityEngine;

namespace MainGameSubtitleCore
{
    /// <summary>
    /// 「いま文字を入力している」ことを他プラグインへ知らせる表札。
    ///
    /// 入力パネルが開いているあいだ、キーボードは入力欄のものであって
    /// 他プラグインのショートカットではない。とくに Enter は送信キーなので、
    /// これを見ずにホットキーを走らせると送信のたびに誤爆する。
    ///
    /// 読む側は _shared/TextInputGate.cs（BlankRoomGate と同じ作りの門番）を使う。
    /// そちらは型名で reflection するため、この型と property 名は変えないこと。
    /// </summary>
    public static class TextInputState
    {
        // 表札を下ろしてから、この時間だけは立てたままにする。
        //
        // 送信の Enter はパネルを閉じるので、フレーム内の Update 順によっては
        // 他プラグインが「もう閉じている」状態で同じ Enter を見てしまう。
        // 猶予を挟めばどちらの順序でも取りこぼさない。
        private const float GraceSeconds = 0.35f;

        private static float _activeUntil = -1f;

        /// <summary>入力パネルが開いている、または閉じた直後の猶予内。</summary>
        public static bool IsTextInputActive
        {
            get { return Time.unscaledTime <= _activeUntil; }
        }

        /// <summary>入力パネルが開いているあいだ毎フレーム呼ぶ。</summary>
        internal static void MarkActive()
        {
            _activeUntil = Time.unscaledTime + GraceSeconds;
        }
    }
}
