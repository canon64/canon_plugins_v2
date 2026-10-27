using System;
using System.IO;
using System.Text;
using System.Threading;
using BepInEx;

namespace AstralMode
{
    // アストラル界の突入/復帰を Human_2_kks（Grokブリッジ GUI）へ伝えるための
    // ファイル信号を書き出す。GUI 側が QTimer でこのファイルを監視し、
    // enter で指定 Grok URL へ、exit で直前のチャット URL へブラウザを遷移させる。
    // 信号元を AstralMode の実遷移に置くことで、F8 手動トグルでもトリガーワードでも
    // 同じく URL が切り替わる（両者とも Enter/ExitAstralCoroutine を通る）。
    public sealed partial class Plugin
    {
        private const string AstralSignalFileName = "astral_signal.json";
        private static long _astralSignalSeq;
        private string _astralSignalPathCache;

        private string ResolveAstralSignalPath()
        {
            if (!string.IsNullOrEmpty(_astralSignalPathCache))
            {
                return _astralSignalPathCache;
            }

            string dir = Path.GetDirectoryName(Info.Location) ?? Paths.PluginPath;
            _astralSignalPathCache = Path.Combine(dir, AstralSignalFileName);
            return _astralSignalPathCache;
        }

        // state は "enter" か "exit"。書き込み失敗は致命ではないので warn ログのみ。
        private void WriteAstralSignal(string state)
        {
            try
            {
                long seq = Interlocked.Increment(ref _astralSignalSeq);
                long ts = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;

                // 依存を増やさないよう手書きJSON（値は固定形式なのでエスケープ不要）。
                string json = "{\"seq\":" + seq.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"state\":\"" + state + "\""
                    + ",\"ts\":" + ts.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "}";

                string path = ResolveAstralSignalPath();
                File.WriteAllText(path, json, new UTF8Encoding(false));
                SimpleFileLogger.Info($"WriteAstralSignal state={state} seq={seq} path={path}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn("WriteAstralSignal failed: " + ex.Message);
            }
        }
    }
}
