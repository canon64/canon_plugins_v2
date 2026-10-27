using BepInEx.Configuration;

namespace AstralMode
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> _cfgShowWhiteRoom;

        private void InitWhiteRoomConfig()
        {
            _cfgShowWhiteRoom = Config.Bind(
                "03. Effect Enable",
                "Use White Room",
                false,
                "アストラル中の3D白ルーム（床・壁・天井）を表示する。falseで白い6面ルームを作成しない。白フェード WhiteCanvas とは別設定。");

            SimpleFileLogger.Info($"White room config: Use White Room={_cfgShowWhiteRoom.Value}");
        }
    }
}
