// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.WGroup
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WGroup fields on an Odin SerializedScriptableObject.
    /// </summary>
    internal sealed class OdinWGroupScriptableObjectTarget : SerializedScriptableObject
    {
        [WGroup("stats", "Stats", autoIncludeCount: WGroupAttribute.InfiniteAutoInclude)]
        public int health;

        public int mana;

        [WGroup("stats"), WGroupEnd("stats")]
        public int stamina;

        public int outsideGroup;
    }
#endif
}
