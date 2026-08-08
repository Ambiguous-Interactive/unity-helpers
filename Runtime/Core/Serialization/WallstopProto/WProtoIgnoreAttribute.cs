// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

    /// <summary>
    /// Excludes a member from the generated formatter.
    /// </summary>
    /// <remarks>
    /// Derived state belongs behind this attribute rather than on the wire: a cached hash, a
    /// dictionary rebuilt from parallel arrays, a pooled scratch buffer. Anything excluded here has
    /// to be restored by a <see cref="WProtoAfterDeserializationAttribute"/> hook.
    /// </remarks>
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = true
    )]
    public sealed class WProtoIgnoreAttribute : Attribute { }
}
