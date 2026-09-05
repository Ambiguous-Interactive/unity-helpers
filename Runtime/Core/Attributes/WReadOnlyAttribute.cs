// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using PropertyAttribute = UnityEngine.PropertyAttribute;

    // Explicit targets prevent Unity-version drift and include serialized properties.
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = true
    )]
    public sealed class WReadOnlyAttribute : PropertyAttribute { }
}
