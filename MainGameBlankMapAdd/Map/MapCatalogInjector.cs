using System;
using System.Collections.Generic;
using System.Linq;

namespace MainGameBlankMapAdd.Map
{
    internal sealed class MapCatalogInjector
    {
        private readonly Action<string> _info;
        private readonly Action<string> _warning;
        private int _sourceThumbnailId = -1;

        internal MapCatalogInjector(Action<string> info, Action<string> warning)
        {
            _info = info ?? throw new ArgumentNullException(nameof(info));
            _warning = warning ?? throw new ArgumentNullException(nameof(warning));
        }

        internal void InjectMap(Dictionary<int, MapInfo.Param> mapInfo, PluginSettings settings)
        {
            if (mapInfo == null || settings == null) { _warning("mapInfo or settings is null"); return; }
            if (mapInfo.Count == 0) { _warning("mapInfo dictionary is empty"); return; }

            if (!mapInfo.TryGetValue(settings.SourceMapNo, out MapInfo.Param source) || source == null)
            {
                source = mapInfo.Values.FirstOrDefault();
                _warning($"source map {settings.SourceMapNo} not found, fallback to first");
            }
            if (source == null) { _warning("source map is null, skip inject"); return; }

            _sourceThumbnailId = source.ThumbnailMorningID != -1 ? source.ThumbnailMorningID
                : source.ThumbnailDayTimeID != -1 ? source.ThumbnailDayTimeID
                : source.ThumbnailEveningID != -1 ? source.ThumbnailEveningID
                : source.ThumbnailNightID;

            MapInfo.Param added = CloneMapParam(source);
            added.No = settings.AddedMapNo;
            added.MapName = string.IsNullOrWhiteSpace(settings.AddedMapName)
                ? $"blank_test_{settings.AddedMapNo}"
                : settings.AddedMapName.Trim();
            added.DisplayName = string.IsNullOrWhiteSpace(settings.AddedDisplayName)
                ? $"Blank {settings.AddedMapNo}"
                : settings.AddedDisplayName.Trim();
            added.Sort = settings.AddedSort;
            added.isGate = settings.ForceIsGate;
            added.isFreeH = settings.ForceIsFreeH;
            added.isH = settings.ForceIsH;
            added.ThumbnailMorningID = settings.AddedThumbnailID;
            added.ThumbnailDayTimeID = settings.AddedThumbnailID;
            added.ThumbnailEveningID = settings.AddedThumbnailID;
            added.ThumbnailNightID = settings.AddedThumbnailID;
            mapInfo[settings.AddedMapNo] = added;

            if (settings.VerboseLog)
                _info($"map injected no={added.No} name={added.MapName} display={added.DisplayName} assetBundle={added.AssetBundleName} asset={added.AssetName} isGate={added.isGate} isFreeH={added.isFreeH} isH={added.isH} thumbs=[{added.ThumbnailMorningID},{added.ThumbnailDayTimeID},{added.ThumbnailEveningID},{added.ThumbnailNightID}]");
        }

        internal void InjectThumbnail(Dictionary<int, MapThumbnailInfo.Param> table, PluginSettings settings)
        {
            if (table == null || settings == null) { _warning("thumbnail table or settings is null, skip inject"); return; }
            if (table.ContainsKey(settings.AddedThumbnailID))
            {
                _warning($"thumbnail id={settings.AddedThumbnailID} already exists, skip inject");
                return;
            }

            MapThumbnailInfo.Param source = null;
            if (_sourceThumbnailId != -1) table.TryGetValue(_sourceThumbnailId, out source);
            if (source == null) source = table.Values.FirstOrDefault();
            if (source == null) { _warning("thumbnail table has no entries, cannot inject custom thumbnail"); return; }

            var added = new MapThumbnailInfo.Param
            {
                ID = settings.AddedThumbnailID,
                Name = string.IsNullOrWhiteSpace(settings.AddedDisplayName)
                    ? $"Blank {settings.AddedMapNo}"
                    : settings.AddedDisplayName.Trim(),
                Bundle = source.Bundle,
                Asset = source.Asset
            };
            table[settings.AddedThumbnailID] = added;
            _info($"thumbnail injected id={added.ID} name={added.Name} bundle={added.Bundle} asset={added.Asset} (cloned from sourceThumbnailId={_sourceThumbnailId})");
        }

        internal void Diagnose(
            MapSelectMenuScene.VisibleType visibleType,
            MapSelectMenuScene.ResultType resultType,
            IReadOnlyCollection<MapInfo.Param> infos,
            IReadOnlyDictionary<int, MapThumbnailInfo.Param> thumbnails,
            PluginSettings settings)
        {
            if (infos == null || settings == null)
            {
                _warning($"map ui diagnose: infos/settings null visibleType={visibleType} resultType={resultType}");
                return;
            }

            MapInfo.Param added = infos.FirstOrDefault(x => x != null && x.No == settings.AddedMapNo);
            if (added == null)
            {
                _warning($"map ui diagnose: added map no={settings.AddedMapNo} not found in infos (count={infos.Count})");
                return;
            }

            int? thumbnailId = added.FindThumbnailID();
            bool thumbnailExists = thumbnailId.HasValue && thumbnails != null && thumbnails.ContainsKey(thumbnailId.Value);
            bool ownThumbnailExists = thumbnails != null && thumbnails.ContainsKey(settings.AddedThumbnailID);
            _info($"map ui diagnose: visibleType={visibleType} resultType={resultType} no={added.No} sort={added.Sort} isGate={added.isGate} isFreeH={added.isFreeH} isH={added.isH} thumbId={thumbnailId?.ToString() ?? "null"} thumbExists={thumbnailExists} ownThumbId={settings.AddedThumbnailID} ownThumbInjected={ownThumbnailExists} ThumbnailMorningID={added.ThumbnailMorningID}");
            if (!thumbnailExists) _warning($"map ui diagnose: thumbnail missing for no={added.No} thumbId={thumbnailId?.ToString() ?? "null"} tableCount={thumbnails?.Count ?? 0}");
            if (!ownThumbnailExists) _warning($"map ui diagnose: own thumbnail not injected (id={settings.AddedThumbnailID})");
        }

        private static MapInfo.Param CloneMapParam(MapInfo.Param source)
        {
            return new MapInfo.Param
            {
                MapName = source.MapName,
                DisplayName = source.DisplayName,
                No = source.No,
                Sort = source.Sort,
                AssetBundleName = source.AssetBundleName,
                AssetName = source.AssetName,
                isGate = source.isGate,
                is2D = source.is2D,
                isWarning = source.isWarning,
                State = source.State,
                LookFor = source.LookFor,
                isOutdoors = source.isOutdoors,
                isFreeH = source.isFreeH,
                isSpH = source.isSpH,
                isSky = source.isSky,
                isH = source.isH,
                ThumbnailMorningID = source.ThumbnailMorningID,
                ThumbnailDayTimeID = source.ThumbnailDayTimeID,
                ThumbnailEveningID = source.ThumbnailEveningID,
                ThumbnailNightID = source.ThumbnailNightID
            };
        }
    }
}
