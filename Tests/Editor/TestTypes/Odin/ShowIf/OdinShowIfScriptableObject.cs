// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ShowIf
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test ScriptableObject for WShowIf integration tests with Odin Inspector.
    /// </summary>
    internal sealed class OdinShowIfScriptableObject : SerializedScriptableObject
    {
        public bool showDependent;

        [WShowIf(nameof(showDependent))]
        public int dependentField;

        public int threshold;

        [WShowIf(nameof(threshold), WShowIfComparison.GreaterThan, 5)]
        public string highThresholdField;

        public GameObject reference;

        [WShowIf(nameof(reference), WShowIfComparison.IsNull)]
        public GameObject fallbackReference;
    }
#endif
}
