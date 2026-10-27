using System;
using UnityEngine;

namespace MainGirlHipHijack.Adapters
{
    /// <summary>
    /// Harmonyから渡された速度設定を男女・アイテムへ互換適用するゲーム境界。
    /// </summary>
    internal sealed class FemaleAnimationSpeedInterceptor
    {
        internal bool TryApply(
            HActionBase action,
            string parameter,
            float value,
            bool isMale,
            bool isFemale1,
            bool forceZeroByHipLock,
            bool cutBySetting,
            GameReflectionMap reflection)
        {
            if ((!forceZeroByHipLock && !cutBySetting) || action == null)
                return false;
            if (!IsSpeedParameter(parameter) || reflection == null)
                return false;
            if (reflection.HActionFemale == null || reflection.HActionFemale1 == null ||
                reflection.HActionMale == null || reflection.HActionMale1 == null ||
                reflection.HActionItem == null)
                return false;

            int parameterHash = Animator.StringToHash(parameter);
            ChaControl female = reflection.HActionFemale.GetValue(action) as ChaControl;
            ChaControl female1 = reflection.HActionFemale1.GetValue(action) as ChaControl;
            ChaControl male = reflection.HActionMale.GetValue(action) as ChaControl;
            ChaControl male1 = reflection.HActionMale1.GetValue(action) as ChaControl;
            ItemObject item = reflection.HActionItem.GetValue(action) as ItemObject;

            if (forceZeroByHipLock)
            {
                female?.setAnimatorParamFloat(parameterHash, 0f);
                if (female1 != null && female1.visibleAll && isFemale1)
                    female1.setAnimatorParamFloat(parameterHash, 0f);
            }

            if (male != null && male.visibleAll && isMale)
                male.setAnimatorParamFloat(parameterHash, value);
            if (male1 != null && isMale)
                male1.setAnimatorParamFloat(parameterHash, value);
            item?.SetAnimatorParamFloat(parameterHash, value);
            return true;
        }

        internal bool TryApply(
            ChaControl female,
            string parameter,
            float value,
            bool forceZeroByHipLock,
            bool cutBySetting)
        {
            if ((!forceZeroByHipLock && !cutBySetting) || female == null)
                return false;
            if (!IsSpeedParameter(parameter))
                return false;

            if (forceZeroByHipLock)
                female.setAnimatorParamFloat(parameter, 0f);
            return true;
        }

        private static bool IsSpeedParameter(string parameter)
        {
            return string.Equals(parameter, "speed", StringComparison.Ordinal) ||
                string.Equals(parameter, "speedBody", StringComparison.Ordinal);
        }
    }
}
