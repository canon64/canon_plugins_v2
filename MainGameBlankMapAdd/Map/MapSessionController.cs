using System;

namespace MainGameBlankMapAdd.Map
{
    internal sealed class MapSessionController
    {
        private readonly SourceMapBlankifier _blankifier;
        private readonly Action<BaseMap> _ensureRoom;
        private readonly Action _destroyRoom;

        internal MapSessionController(SourceMapBlankifier blankifier, Action<BaseMap> ensureRoom, Action destroyRoom)
        {
            _blankifier = blankifier ?? throw new ArgumentNullException(nameof(blankifier));
            _ensureRoom = ensureRoom ?? throw new ArgumentNullException(nameof(ensureRoom));
            _destroyRoom = destroyRoom ?? throw new ArgumentNullException(nameof(destroyRoom));
        }

        internal BaseMap LastReservedMap { get; private set; }

        internal void Apply(BaseMap map, PluginSettings settings)
        {
            LastReservedMap = map;
            if (map?.mapRoot == null || settings == null || map.no != settings.AddedMapNo)
            {
                _blankifier.Reset();
                _destroyRoom();
                return;
            }

            _blankifier.Apply(map, settings);
            if (settings.EnableVideoRoom) _ensureRoom(map);
            else _destroyRoom();
        }

        internal void ForceReapply(PluginSettings settings)
        {
            _blankifier.Reset();
            Apply(LastReservedMap, settings);
        }
    }
}
