using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace AstralMode
{
    // アストラル入場時に本体側の女をベンチフェラ待機へ強制切替する。
    // 銅像はこの前に現在体位のまま焼く。ここでは銅像ではなく本体側だけを切り替える。
    public sealed partial class Plugin
    {
        private const string DefaultAstralPoseKeyword = "ベンチフェラ";

        private ConfigEntry<string> _cfgAstralPoseKeyword;
        private ConfigEntry<string> _cfgAstralPoseMode;

        private string AstralPoseKeyword
        {
            get
            {
                if (_cfgAstralPoseKeyword == null)
                    _cfgAstralPoseKeyword = Config.Bind(
                        "Astral State", "AstralPoseNameKeyword", DefaultAstralPoseKeyword,
                        "アストラル入場後、本体側の女に強制する体位の nameAnimation キーワード（部分一致）。空ならベンチフェラ。ASTRAL_POSE_LISTログで実名を確認できる。");

                string value = (_cfgAstralPoseKeyword.Value ?? string.Empty).Trim();
                return value.Length > 0 ? value : DefaultAstralPoseKeyword;
            }
        }

        private string AstralPoseModeName
        {
            get
            {
                if (_cfgAstralPoseMode == null)
                    _cfgAstralPoseMode = Config.Bind(
                        "Astral State", "AstralPoseMode", "houshi",
                        "アストラル入場後、本体側の女に強制する体位のモード（EMode名の部分一致: houshi/sonyu/aibu 等）。空なら全モード対象。");
                return _cfgAstralPoseMode.Value;
            }
        }

        internal void ForceAstralPose()
        {
            try
            {
                HSceneProc proc = _hSceneProc;
                if (proc == null || proc.flags == null)
                {
                    SimpleFileLogger.Warn("ForceAstralPose: HSceneProc/flags null");
                    return;
                }

                var lists = SnapLstUseAnimInfoField != null
                    ? SnapLstUseAnimInfoField.GetValue(proc) as List<HSceneProc.AnimationListInfo>[]
                    : null;
                if (lists == null)
                {
                    SimpleFileLogger.Warn("ForceAstralPose: lstUseAnimInfo not found");
                    return;
                }

                string keyword = (AstralPoseKeyword ?? DefaultAstralPoseKeyword).Trim();
                if (keyword.Length == 0) keyword = DefaultAstralPoseKeyword;
                string modeName = (AstralPoseModeName ?? string.Empty).Trim().ToLowerInvariant();

                HSceneProc.AnimationListInfo exact = null;
                HSceneProc.AnimationListInfo partial = null;
                int dumped = 0;

                for (int i = 0; i < lists.Length; i++)
                {
                    var list = lists[i];
                    if (list == null) continue;

                    for (int j = 0; j < list.Count; j++)
                    {
                        var c = list[j];
                        if (c == null) continue;
                    // verbose pose list disabled
                        dumped++;

                        string mode = c.mode.ToString().ToLowerInvariant();
                        bool modeOk = modeName.Length == 0 || mode.IndexOf(modeName) >= 0;
                        if (!modeOk) continue;

                        string name = c.nameAnimation ?? string.Empty;
                        if (string.Equals(name, keyword, StringComparison.OrdinalIgnoreCase))
                        {
                            exact = c;
                            break;
                        }

                        if (partial == null && name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                            partial = c;
                    }

                    if (exact != null) break;
                }

                HSceneProc.AnimationListInfo best = exact ?? partial;
                if (best == null)
                {
                    SimpleFileLogger.Warn($"ForceAstralPose: no pose matched keyword='{keyword}' mode='{modeName}', dumped={dumped}");
                    return;
                }

                proc.flags.selectAnimationListInfo = best;
                proc.flags.click = HFlag.ClickKind.actionChange;

                SimpleFileLogger.Warn($"ForceAstralPose: actionChange -> id={best.id} mode={best.mode} name={best.nameAnimation} keyword='{keyword}' modeFilter='{modeName}' match={(exact != null ? "exact" : "partial")} dumped={dumped}");
            }
            catch (Exception ex)
            {
                SimpleFileLogger.Warn($"ForceAstralPose failed: {ex.Message}");
            }
        }
    }
}
