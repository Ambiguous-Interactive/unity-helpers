// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ShowIf
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using System.Collections.Generic;
    using Sirenix.OdinInspector;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WShowIf collection condition tests with Odin Inspector.
    /// </summary>
    internal sealed class OdinShowIfCollectionTarget : SerializedScriptableObject
    {
        public List<int> listCondition = new();

        [WShowIf(nameof(listCondition), WShowIfComparison.IsNotNullOrEmpty)]
        public int dependentField;
    }
#endif
}
