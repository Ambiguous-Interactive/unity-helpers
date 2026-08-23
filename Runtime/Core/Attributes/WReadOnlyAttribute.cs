// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Attributes
{
    using System;
    using PropertyAttribute = UnityEngine.PropertyAttribute;

    // Declared rather than inherited, so the accepted targets are this package's decision instead
    // of drifting with whatever UnityEngine.PropertyAttribute declares in a given editor version.
    // Property is included because a property's data can genuinely be serialized -- through
    // [field: SerializeField], where the attribute lands on the backing field, and through Odin,
    // which draws a property directly. It is NOT an invitation to decorate a computed property
    // nothing serializes: that reaches no drawer, and WUH003 reports it.
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = true
    )]
    public sealed class WReadOnlyAttribute : PropertyAttribute { }
}
