// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using UnityEngine.Scripting;

    /// <summary>
    /// Declares a subtype that may be written in place of the annotated base type.
    /// </summary>
    /// <remarks>
    /// The subtype is written as a length-delimited field at <see cref="Tag"/> on the base message,
    /// carrying the subtype's own members. Tags must be unique across every include on a base and
    /// must never be reused once shipped -- a payload written by an older build resolves the
    /// subtype by number alone, so renumbering silently deserializes one type as another.
    /// </remarks>
    [Preserve]
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Interface,
        AllowMultiple = true,
        Inherited = false
    )]
    public sealed class WProtoIncludeAttribute : Attribute
    {
        /// <summary>
        /// Initializes the attribute with the subtype's field number on the base message.
        /// </summary>
        /// <param name="tag">The field number, unique among the base type's includes.</param>
        /// <param name="knownType">The subtype this tag identifies.</param>
        public WProtoIncludeAttribute(int tag, Type knownType)
        {
            Tag = tag;
            KnownType = knownType;
        }

        /// <summary>The field number carrying the subtype on the base message.</summary>
        public int Tag { get; }

        /// <summary>The subtype this tag identifies.</summary>
        public Type KnownType { get; }
    }
}
