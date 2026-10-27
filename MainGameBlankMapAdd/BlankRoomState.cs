using System;

namespace MainGameBlankMapAdd
{
    /// <summary>
    /// 「今プレイヤーが BlankMapAdd の追加マップ(＝自作の部屋)にいるか」を全プラグインへ公開する表札。
    ///
    /// 他プラグインはこの型を reflection で掴み、<see cref="IsInBlankRoom"/> が false の間は
    /// 一切発火しないようにする。BlankMapAdd 本体だけがこの値を書き換える。
    ///
    /// 判定の唯一の根拠は「直近に Reserve されたマップ番号 == 追加マップ番号、かつ mapRoot が生存」。
    /// マップ遷移・タイトル復帰・シーン破棄はすべて mapRoot の消滅として現れるため、
    /// 毎フレームの再評価だけで退室を取りこぼさない。
    /// </summary>
    public static class BlankRoomState
    {
        private static bool _isInBlankRoom;

        /// <summary>追加マップに滞在中なら true。他プラグインはこれを門番の判定に使う。</summary>
        public static bool IsInBlankRoom
        {
            get { return _isInBlankRoom; }
        }

        /// <summary>直近に Reserve されたマップ番号。未確定なら int.MinValue。</summary>
        public static int CurrentMapNo { get; private set; } = int.MinValue;

        /// <summary>入退室の瞬間に一度だけ発火する。引数は入室なら true。</summary>
        public static event Action<bool> Changed;

        internal static void Set(bool value, int mapNo, string reason, Action<string> log)
        {
            CurrentMapNo = mapNo;

            if (_isInBlankRoom == value) return;
            _isInBlankRoom = value;

            if (log != null)
                log($"blank room gate -> {(value ? "IN" : "OUT")} (mapNo={mapNo}, reason={reason})");

            Action<bool> handlers = Changed;
            if (handlers == null) return;

            // 購読側の例外で本体の遷移処理を巻き添えにしない。
            foreach (Delegate d in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<bool>)d)(value);
                }
                catch (Exception ex)
                {
                    if (log != null)
                        log($"blank room gate listener failed: {ex.Message}");
                }
            }
        }
    }
}
