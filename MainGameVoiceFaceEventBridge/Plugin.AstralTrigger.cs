using System;

namespace MainGameVoiceFaceEventBridge
{
    // AstralMode プラグインの public static 窓口
    // （RequestEnterAstral / RequestExitAstral）をリフレクションで叩く。
    // 呼び出し先だけを保持し、状態は呼び出すたびに取得する。
    internal sealed partial class Plugin
    {
        private const string AstralPluginTypeName = "AstralMode.Plugin";
        private readonly External.AstralMethodCache _astralMethods = new External.AstralMethodCache();

        // enter=true でアストラル界突入、false で現実復帰を要求する。
        // 突入可否（state 判定・無視）の最終判断は AstralMode 側に委ねる。
        private void InvokeAstralTransition(bool enter)
        {
            string methodName = enter ? "RequestEnterAstral" : "RequestExitAstral";
            try
            {
                if (!_astralMethods.TryTransition(enter, out bool result))
                {
                    Log($"[astral] call skipped: entry point unavailable {AstralPluginTypeName}.{methodName}");
                    return;
                }
                Log($"[astral] {methodName} result={result}");
            }
            catch (Exception ex)
            {
                Log($"[astral] call failed {AstralPluginTypeName}.{methodName} error={ex.Message}");
            }
        }

        // 今 AstralMode が突入〜帰還の最中（Normal 以外）かどうか。型/メソッドが無ければ false。
        // 音声ガードの isVoicePrcoStop 復元で、アストラル由来の stale な true を判別するのに使う。
        private bool IsAstralModeActiveCore()
        {
            try
            {
                return _astralMethods.IsActive();
            }
            catch
            {
                return false;
            }
        }
    }
}
