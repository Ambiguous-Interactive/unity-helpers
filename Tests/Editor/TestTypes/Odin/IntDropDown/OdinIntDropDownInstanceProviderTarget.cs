// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.IntDropDown
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for IntDropDown attribute using an instance method provider for options.
    /// </summary>
    internal sealed class OdinIntDropDownInstanceProviderTarget : SerializedScriptableObject
    {
        public int[] dynamicOptions = { 10, 20, 30, 40, 50 };

        [IntDropDown(nameof(GetInstanceOptions))]
        public int instanceProviderSelection;

        public IEnumerable<int> GetInstanceOptions()
        {
            return dynamicOptions;
        }
    }
#endif
}
