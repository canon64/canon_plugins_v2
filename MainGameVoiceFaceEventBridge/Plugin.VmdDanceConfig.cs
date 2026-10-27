using BepInEx.Configuration;

namespace MainGameVoiceFaceEventBridge
{
    internal sealed partial class Plugin
    {
        private void BindVmdDanceConfig()
        {
            var enabled = Config.Bind("VMDダンス", "Enabled", true, "発話に含まれるVMDプリセット名と語尾で自動再生します。プリセットは統合UIのVMDタブで保存してください。");
            var suffix = Config.Bind("VMDダンス", "TriggerSuffix", "を踊るね", "プリセット名の後に付ける発話語尾。例：曲名を踊るね。空欄なら発話からの再生を停止します。");
            ResponseTextTriggerApi.VmdDanceEnabled = enabled.Value;
            ResponseTextTriggerApi.VmdDanceSuffix = suffix.Value;
            enabled.SettingChanged += (_, __) => ResponseTextTriggerApi.VmdDanceEnabled = enabled.Value;
            suffix.SettingChanged += (_, __) => ResponseTextTriggerApi.VmdDanceSuffix = suffix.Value;
        }
    }
}
