using System;
using System.Collections.Generic;
using HarmonyLib;
using MainGameBlankMapAdd.Compatibility;

namespace MainGameBlankMapAdd
{
    public sealed partial class Plugin
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(BaseMap), "LoadMapInfo")]
        private static void BaseMap_LoadMapInfo_Postfix(ref Dictionary<int, MapInfo.Param> __result)
        {
            try
            {
                Plugin plugin = PluginCompatibilityResolver.ResolveLoadedPlugin();
                plugin?._mapCatalogInjector?.InjectMap(__result, plugin._settings);
            }
            catch (Exception ex)
            {
                PluginCompatibilityResolver.ResolveLoadedPlugin()?.LogError($"LoadMapInfo postfix failed: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BaseMap), "LoadMapThumbnailInfo")]
        private static void BaseMap_LoadMapThumbnailInfo_Postfix(ref Dictionary<int, MapThumbnailInfo.Param> __result)
        {
            try
            {
                Plugin plugin = PluginCompatibilityResolver.ResolveLoadedPlugin();
                plugin?._mapCatalogInjector?.InjectThumbnail(__result, plugin._settings);
            }
            catch (Exception ex)
            {
                PluginCompatibilityResolver.ResolveLoadedPlugin()?.LogError($"LoadMapThumbnailInfo postfix failed: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BaseMap), "Reserve")]
        private static void BaseMap_Reserve_Postfix(BaseMap __instance)
        {
            try
            {
                PluginCompatibilityResolver.ResolveLoadedPlugin()?.TryBlankifyCurrentMap(__instance);
            }
            catch (Exception ex)
            {
                PluginCompatibilityResolver.ResolveLoadedPlugin()?.LogError($"Reserve postfix failed: {ex}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MapSelectMenuScene), "Create")]
        private static void MapSelectMenuScene_Create_Postfix(
            MapSelectMenuScene.VisibleType visibleType,
            MapSelectMenuScene.ResultType resultType,
            IReadOnlyCollection<MapInfo.Param> infos,
            IReadOnlyDictionary<int, MapThumbnailInfo.Param> mapThumbnailInfoTable)
        {
            try
            {
                Plugin plugin = PluginCompatibilityResolver.ResolveLoadedPlugin();
                plugin?._mapCatalogInjector?.Diagnose(
                    visibleType,
                    resultType,
                    infos,
                    mapThumbnailInfoTable,
                    plugin._settings);
            }
            catch (Exception ex)
            {
                PluginCompatibilityResolver.ResolveLoadedPlugin()?.LogError($"Create postfix diagnose failed: {ex}");
            }
        }

        private void TryBlankifyCurrentMap(BaseMap map)
        {
            _mapSession.Apply(map, _settings);
            UpdateBlankRoomGate("map-reserve");
        }

        /// <summary>
        /// 追加マップ滞在中かどうかを再評価して表札(<see cref="BlankRoomState"/>)へ反映する。
        /// Reserve 直後だけでなく毎フレーム呼ぶ。退室はマップ遷移とは限らず、
        /// タイトル復帰やシーン破棄で mapRoot が消えるだけの場合があるため。
        /// </summary>
        private void UpdateBlankRoomGate(string reason)
        {
            BaseMap map = LastReservedMap;
            bool inRoom = map != null
                && map.mapRoot != null
                && _settings != null
                && map.no == _settings.AddedMapNo;

            BlankRoomState.Set(inRoom, map != null ? map.no : int.MinValue, reason, LogInfo);
        }

        private BaseMap LastReservedMap => _mapSession?.LastReservedMap;

        private void ForceReapplyCurrentMap()
        {
            _mapSession?.ForceReapply(_settings);
        }

        private void ReloadSettingsAndApply()
        {
            _settings = _settingsRepository.LoadCurrent();
            SyncConfigEntriesFromSettings();
            ApplyGlobalReverbState("reload-settings", persistConfig: true);

            BaseMap map = LastReservedMap;
            if (map?.mapRoot == null)
            {
                LogInfo("settings reloaded (map not ready)");
                return;
            }

            ForceReapplyCurrentMap();
            LogInfo($"settings reloaded + applied mapNo={map.no} useSphere={_settings.UseSphere} reverb={_settings.EnableVoiceReverb}");
        }
    }
}
