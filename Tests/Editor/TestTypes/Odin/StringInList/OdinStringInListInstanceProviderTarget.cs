// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.StringInList
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for StringInList attribute with instance method provider.
    /// </summary>
    internal sealed class OdinStringInListInstanceProviderTarget : SerializedScriptableObject
    {
        public string[] dynamicOptions = { "DynamicA", "DynamicB", "DynamicC" };

        [StringInList(nameof(GetInstanceOptions))]
        public string instanceProviderSelection;

        public IEnumerable<string> GetInstanceOptions()
        {
            return dynamicOptions;
        }
    }
#endif
}
