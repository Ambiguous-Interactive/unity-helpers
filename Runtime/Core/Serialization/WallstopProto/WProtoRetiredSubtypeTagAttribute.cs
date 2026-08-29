// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using UnityEngine.Scripting;

    /// <summary>
    /// A field number that was assigned to a subtype which no longer exists, and which must never be
    /// handed to another type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retirement is what makes remove-then-re-add safe. A number freed on deletion would be given
    /// to the next subtype added, and every payload saved before the deletion would then read that
    /// number as the new type -- a silent type confusion no diagnostic can catch, because both
    /// builds are internally consistent. Holding the number instead costs one line of manifest and
    /// makes re-adding the removed type restore exactly the number it had.
    /// </para>
    /// <para>
    /// The removed type is named as a string because it no longer exists to <c>typeof</c>. The name
    /// is the fully qualified type name the assignment tool recorded, and it is compared as an ordinal string:
    /// re-adding under a different name is a rename, and a rename is a wire change the manifest diff
    /// puts in front of a reviewer rather than resolving on its own.
    /// </para>
    /// </remarks>
    [Preserve]
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class WProtoRetiredSubtypeTagAttribute : Attribute
    {
        /// <summary>
        /// Initializes the entry with the removed type's name and the number it held.
        /// </summary>
        /// <param name="subTypeName">The fully qualified name of the removed subtype.</param>
        /// <param name="baseType">The base whose field-number space the number belongs to.</param>
        /// <param name="tag">The retired field number.</param>
        public WProtoRetiredSubtypeTagAttribute(string subTypeName, Type baseType, int tag)
        {
            SubTypeName = subTypeName;
            BaseType = baseType;
            Tag = tag;
        }

        /// <summary>The fully qualified name of the subtype that held this field number.</summary>
        public string SubTypeName { get; }

        /// <summary>The base whose field-number space the retired number belongs to.</summary>
        public Type BaseType { get; }

        /// <summary>The field number no other subtype of that base may take.</summary>
        public int Tag { get; }
    }
}
