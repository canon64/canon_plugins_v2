using System;
using System.Reflection;
using BepInEx.Configuration;

namespace AstralMode
{
    // VR(KoikatsuSunshine_VR)で起動している時だけ、波紋・残像を専用トグルで制御する。
    // 通常版とVR版は BepInEx config を共有するため、VR時に通常トグルを書き換えると
    // デスクトップ側にも波及する。そこで VR 起動を検出し、VR 時は専用トグルへ切り替える。
    public sealed partial class Plugin
    {
        // VRGIN(KKS_VR)はVR版exeでしかロードされない。デスクトップ版での型ロード例外を避けるため
        // 直接参照せず、外部プラグインと同じくリフレクションで VRGIN.Core.VR.Active を読む。
        private bool _vrLookupDone;
        private PropertyInfo _vrActiveProp;

        private bool IsVrActive()
        {
            if (!_vrLookupDone)
            {
                _vrLookupDone = true;
                try
                {
                    Type t = FindType("VRGIN.Core.VR", "VRGIN_OpenXR");
                    if (t != null)
                        _vrActiveProp = t.GetProperty("Active", BindingFlags.Public | BindingFlags.Static);
                }
                catch { }
            }

            if (_vrActiveProp == null) return false;
            try { return (bool)_vrActiveProp.GetValue(null, null); }
            catch { return false; }
        }

        // 残像を出してよいか。VR時はVR専用トグル、通常時は渡された通常トグルを見る。
        private bool AfterimageEnabled(ConfigEntry<bool> normalToggle)
        {
            if (IsVrActive()) return _cfgVrUseAfterimage != null && _cfgVrUseAfterimage.Value;
            return normalToggle != null && normalToggle.Value;
        }

        // 波紋を出してよいか。VR時はVR専用トグル、通常時は渡された通常トグルを見る。
        private bool DisplacementEnabled(ConfigEntry<bool> normalToggle)
        {
            if (IsVrActive()) return _cfgVrUseDisplacement != null && _cfgVrUseDisplacement.Value;
            return normalToggle != null && normalToggle.Value;
        }
    }
}
