using BepInEx.Configuration;
using ConfigurationManager;
using UnityEngine;

namespace MainGameStudioItemSummon
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> _cfgBuild;
        private ConfigEntry<bool> _cfgRebuild;
        private ConfigEntry<bool> _cfgShowHud;

        // 操作は全部 ConfigManager のボタン/トグルから。ホットキーは使わない（キー枯渇対策）。
        private void SetupConfig()
        {
            _cfgBuild = Config.Bind(
                "Catalog", "Build / Load", false,
                new ConfigDescription(
                    "台帳を用意：キャッシュが有効なら即読み込み、無ければフル構築(~2分)して保存。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        Order = 100,
                        HideDefaultButton = true,
                        CustomDrawer = DrawBuildButton,
                    }));
            _cfgBuild.SettingChanged += (s, e) =>
            {
                if (!_cfgBuild.Value) return;
                _cfgBuild.Value = false;
                EnsureCatalog("config", force: false);
            };

            _cfgRebuild = Config.Bind(
                "Catalog", "Force rebuild", false,
                new ConfigDescription(
                    "mod を足し引きした後など、キャッシュを無視して作り直す。",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        Order = 90,
                        HideDefaultButton = true,
                        CustomDrawer = DrawRebuildButton,
                    }));
            _cfgRebuild.SettingChanged += (s, e) =>
            {
                if (!_cfgRebuild.Value) return;
                _cfgRebuild.Value = false;
                EnsureCatalog("config-force", force: true);
            };

            _cfgShowHud = Config.Bind(
                "HUD", "Show HUD", true,
                "画面左上の状態オーバーレイ（読み取り専用）を表示する。");
            _hudVisible = _cfgShowHud.Value;
            _cfgShowHud.SettingChanged += (s, e) => { _hudVisible = _cfgShowHud.Value; };

            LogInfo("config ready: ConfigManager > Catalog (Build / Force rebuild), HUD (Show HUD)");
        }

        private void DrawBuildButton(ConfigEntryBase entryBase)
        {
            if (GUILayout.Button("Build / Load catalog", GUILayout.MinWidth(180f)))
                ((ConfigEntry<bool>)entryBase).Value = true;
        }

        private void DrawRebuildButton(ConfigEntryBase entryBase)
        {
            if (GUILayout.Button("Force rebuild", GUILayout.MinWidth(180f)))
                ((ConfigEntry<bool>)entryBase).Value = true;
        }
    }
}
