using BepInEx;
using BepInEx.Logging;
using KKAPI.Chara;

namespace MainGameClothColliderCards
{
    [BepInPlugin(Guid, "Cloth Collider Cards", "0.1.0")]
    [BepInDependency("marco.kkapi")]
    [BepInProcess("KoikatsuSunshine")]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        public const string Guid = "canon.kks.clothcollidercards";
        internal static ManualLogSource LogSource;

        private void Awake()
        {
            LogSource = Logger;
            CharacterApi.RegisterExtraBehaviour<ColliderCardController>(Guid);
            Logger.LogInfo("Card collider data registered.");
        }
    }
}
