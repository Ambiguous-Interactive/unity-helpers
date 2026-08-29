// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using UnityEngine.Scripting;

    /// <summary>
    /// Declares, from the subtype, that it may be written in place of <see cref="BaseType"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inverse of <see cref="WProtoIncludeAttribute"/> and byte-for-byte equivalent to it:
    /// <c>[WProtoSubtype(typeof(Weapon), 100)]</c> on <c>Melee</c> produces exactly the bytes
    /// <c>[WProtoInclude(100, typeof(Melee))]</c> on <c>Weapon</c> produces. Which form is used is a
    /// source-level choice, so a base need not be edited -- and need not know its own subtypes --
    /// for one to become serializable. Both forms may be mixed on one base, as long as no two
    /// subtypes claim the same field number.
    /// </para>
    /// <para>
    /// The subtype is written as a length-delimited field at <see cref="Tag"/> on the base message,
    /// carrying the subtype's own members. Tags must be unique across every subtype of a base and
    /// must never be reused once shipped -- a payload written by an older build resolves the
    /// subtype by number alone, so renumbering silently deserializes one type as another.
    /// </para>
    /// <para>
    /// <c>[WProtoSubtype(typeof(Weapon))]</c> -- no number -- takes its field number from the
    /// assembly's committed manifest (<see cref="WProtoSubtypeTagAttribute"/>) instead, which the
    /// editor writes on the assembly reload that first sees the declaration. The author then picks
    /// nothing and runs nothing: the tool takes the next free small number, never renumbers an
    /// entry it already wrote, and never reuses one <see cref="WProtoRetiredSubtypeTagAttribute"/>
    /// holds. Until the entry exists the declaration is <c>WPROTO041</c> rather than a guess,
    /// because a guessed number is a wire contract nobody agreed to -- a warning in the editor, so
    /// that the assembly compiles and the tool can see the type, and an error for any compilation
    /// without <c>UNITY_EDITOR</c>, because an unnumbered subtype has no wire form to ship. The two
    /// forms produce identical bytes for the same number; an explicit number is the override, and
    /// everything already published uses it.
    /// </para>
    /// <para>
    /// <see cref="BaseType"/> must be the annotated type's <b>immediate</b> base and must itself be
    /// a <see cref="WProtoContractAttribute"/> in the same assembly, and neither type may be
    /// generic. protobuf-net refuses a grandchild declared on the grandparent, so a deeper type
    /// names the type it actually derives from; the generator that emits the base's formatter only
    /// sees its own compilation, so a subtype in another assembly could never reach the base's
    /// dispatch chain; and one field number cannot identify a generic type, which is as many types
    /// as it has closures. Each of these is a build error rather than a declaration that silently
    /// does nothing.
    /// </para>
    /// </remarks>
    [Preserve]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class WProtoSubtypeAttribute : Attribute
    {
        /// <summary>
        /// Initializes the attribute with the base type and this subtype's field number on it.
        /// </summary>
        /// <param name="baseType">The immediate base contract this type may be written as.</param>
        /// <param name="tag">The field number, unique among the base type's subtypes.</param>
        public WProtoSubtypeAttribute(Type baseType, int tag)
        {
            BaseType = baseType;
            Tag = tag;
            HasTag = true;
        }

        /// <summary>
        /// Initializes the attribute with the base type alone, taking the field number from the
        /// assembly's manifest.
        /// </summary>
        /// <param name="baseType">The immediate base contract this type may be written as.</param>
        /// <remarks>
        /// The zero-touch form. The number comes from a <see cref="WProtoSubtypeTagAttribute"/> the
        /// assignment tool committed, so nothing about this declaration has to change when a sibling
        /// is added or removed.
        /// </remarks>
        public WProtoSubtypeAttribute(Type baseType)
        {
            BaseType = baseType;
            Tag = 0;
            HasTag = false;
        }

        /// <summary>The immediate base contract this type may be written as.</summary>
        public Type BaseType { get; }

        /// <summary>
        /// The field number carrying this subtype on the base message, or <c>0</c> when the manifest
        /// supplies it.
        /// </summary>
        public int Tag { get; }

        /// <summary>
        /// Whether the declaration states its own field number rather than taking the manifest's.
        /// </summary>
        public bool HasTag { get; }
    }
}
