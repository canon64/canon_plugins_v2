using System.Reflection;

namespace MainGirlHipHijack.Adapters
{
    internal sealed class GameReflectionMap
    {
        internal readonly FieldInfo HActionFemale = FindField(typeof(HActionBase), "female");
        internal readonly FieldInfo HActionFemale1 = FindField(typeof(HActionBase), "female1");
        internal readonly FieldInfo HActionMale = FindField(typeof(HActionBase), "male");
        internal readonly FieldInfo HActionMale1 = FindField(typeof(HActionBase), "male1");
        internal readonly FieldInfo HActionItem = FindField(typeof(HActionBase), "item");
        internal readonly FieldInfo HSceneFemaleList = FindField(typeof(HSceneProc), "lstFemale");
        internal readonly FieldInfo HSceneMaleList = FindField(typeof(HSceneProc), "lstMale");
        internal readonly FieldInfo HSceneFlags = FindField(typeof(HSceneProc), "flags");
        internal readonly PropertyInfo HSceneFlagsProperty = FindProperty(typeof(HSceneProc), "flags");
        internal readonly FieldInfo HFlagCurrentAnimationInfo = FindField(typeof(HFlag), "nowAnimationInfo");
        internal readonly PropertyInfo HFlagCurrentAnimationInfoProperty = FindProperty(typeof(HFlag), "nowAnimationInfo");
        internal readonly FieldInfo HFlagSelectedAnimationInfo = FindField(typeof(HFlag), "selectAnimationListInfo");
        internal readonly PropertyInfo HFlagSelectedAnimationInfoProperty = FindProperty(typeof(HFlag), "selectAnimationListInfo");

        private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static FieldInfo FindField(System.Type type, string name)
        {
            for (System.Type current = type; current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(name, MemberFlags);
                if (field != null) return field;
            }
            return null;
        }

        private static PropertyInfo FindProperty(System.Type type, string name)
        {
            for (System.Type current = type; current != null; current = current.BaseType)
            {
                PropertyInfo property = current.GetProperty(name, MemberFlags);
                if (property != null) return property;
            }
            return null;
        }
    }
}
