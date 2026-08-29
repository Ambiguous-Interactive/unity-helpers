// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using UnityEngine.Scripting;

    /// <summary>
    /// One entry of an assembly's subtype tag manifest: the field number assigned to
    /// <see cref="SubTypeName"/> on <see cref="BaseType"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written by the assignment tool, not by hand. A subtype that declares itself without a number
    /// -- <c>[WProtoSubtype(typeof(Base))]</c> -- gets its number from here, so the author never
    /// picks one, never edits the base, and never reads a sibling. The manifest is committed source
    /// because the number is the wire contract: it has to survive a clean checkout, a different
    /// machine, and a reordering of the files the compiler happens to visit first.
    /// </para>
    /// <para>
    /// A manifest entry is never renumbered once written. Removing the subtype retires the number
    /// (see <see cref="WProtoRetiredSubtypeTagAttribute"/>) rather than freeing it, so adding a
    /// different type afterwards cannot be handed a number an old payload already means something
    /// else by, and re-adding the removed type restores exactly the number it had.
    /// </para>
    /// <para>
    /// Entries live on the assembly that contains BOTH types, because that is the only assembly
    /// whose compilation can see the pair -- the same constraint <see cref="WProtoSubtypeAttribute"/>
    /// enforces. Entries a compilation does not declare itself are ignored.
    /// </para>
    /// <para>
    /// The subtype is named as a string, symmetrically with
    /// <see cref="WProtoRetiredSubtypeTagAttribute"/>, and for the same reason: an entry has to
    /// outlive the type it names. A <c>typeof</c> here stops compiling the moment the subtype is
    /// deleted, and the only edit that makes the file compile again -- deleting the line -- frees
    /// the number for the next subtype to take, which is precisely the reuse retirement exists to
    /// forbid. A string keeps the assembly compiling with an entry that names nothing, which is
    /// what lets the assignment tool see the orphan and RETIRE it. The name is the fully qualified
    /// type name, with a dot before a nested type, compared as an ordinal string.
    /// </para>
    /// </remarks>
    [Preserve]
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    public sealed class WProtoSubtypeTagAttribute : Attribute
    {
        /// <summary>
        /// Initializes the entry with the pair it numbers and the number assigned to it.
        /// </summary>
        /// <param name="subTypeName">The fully qualified name of the subtype the number identifies.</param>
        /// <param name="baseType">The immediate base the subtype is written as.</param>
        /// <param name="tag">The field number, unique among that base's subtypes.</param>
        public WProtoSubtypeTagAttribute(string subTypeName, Type baseType, int tag)
        {
            SubTypeName = subTypeName;
            BaseType = baseType;
            Tag = tag;
        }

        /// <summary>The fully qualified name of the subtype this field number identifies.</summary>
        public string SubTypeName { get; }

        /// <summary>The immediate base contract the subtype is written as.</summary>
        public Type BaseType { get; }

        /// <summary>The field number carrying the subtype on the base message.</summary>
        public int Tag { get; }
    }
}
