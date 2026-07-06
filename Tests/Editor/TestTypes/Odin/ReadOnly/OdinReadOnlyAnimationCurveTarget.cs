// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Editor.TestTypes.Odin.ReadOnly
{
#if UNITY_EDITOR && WALLSTOP_UNITY_HELPERS_ODIN_INSPECTOR
    using Sirenix.OdinInspector;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;

    /// <summary>
    /// Test target for WReadOnly attribute on AnimationCurve field with Odin Inspector.
    /// </summary>
    internal sealed class OdinReadOnlyAnimationCurveTarget : SerializedScriptableObject
    {
        [WReadOnly]
        public AnimationCurve readOnlyCurve;
    }
#endif
}
