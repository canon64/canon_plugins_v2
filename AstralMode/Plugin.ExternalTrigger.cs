using UnityEngine;

namespace AstralMode
{
    // 外部プラグイン（MainGameVoiceFaceEventBridge 等）からトリガーワードで
    // アストラル界への突入／現実復帰を要求するための public static 窓口。
    // 判定条件は F キー（Plugin.cs:103-130）と完全に同じにして挙動を一貫させる。
    // 「すでにその状態なら無視」はここの state チェックがそのまま満たす。
    public sealed partial class Plugin
    {
        // Normal かつ HScene 中のみ突入。既にアストラル/遷移中/HScene外なら false（無視）。
        public static bool RequestEnterAstral()
        {
            Plugin inst = Instance;
            if (inst == null)
            {
                return false;
            }

            if (inst._state == AstralState.Normal && _inHScene)
            {
                SimpleFileLogger.Info("RequestEnterAstral: enter astral mode (external trigger)");
                inst.StartCoroutine(inst.EnterAstralCoroutine());
                return true;
            }

            SimpleFileLogger.Info($"RequestEnterAstral ignored: state={inst._state}, inHScene={_inHScene}");
            return false;
        }

        // ※ IsAstralActive() は Plugin.HScene.cs に既存（internal static, Normal以外でtrue）。
        //    VoiceFaceEventBridge はそれをリフレクションで参照する。

        // Astral のときのみ復帰。既に現実/遷移中なら false（無視）。
        public static bool RequestExitAstral()
        {
            Plugin inst = Instance;
            if (inst == null)
            {
                return false;
            }

            if (inst._state == AstralState.Astral)
            {
                SimpleFileLogger.Info("RequestExitAstral: exit astral mode (external trigger)");
                inst.StartCoroutine(inst.ExitAstralCoroutine());
                return true;
            }

            SimpleFileLogger.Info($"RequestExitAstral ignored: state={inst._state}");
            return false;
        }
    }
}
