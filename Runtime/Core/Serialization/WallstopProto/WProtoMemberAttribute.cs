// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;
    using UnityEngine.Scripting;

    /// <summary>
    /// Assigns a wire field number, and optionally an explicit schema name, to a member.
    /// </summary>
    /// <remarks>
    /// Field numbers are the contract; names are not on the wire at all. <see cref="Name"/> exists
    /// because the number alone is unreadable in a schema, a diagnostic, or a hand-inspected
    /// payload dump, and because a member renamed in C# must not silently rename itself everywhere
    /// the schema is consumed. Leaving it unset defaults the schema name to the member name, which
    /// is exactly the coupling <see cref="Name"/> lets a contract break.
    /// </remarks>
    [Preserve]
    [AttributeUsage(
        AttributeTargets.Field | AttributeTargets.Property,
        AllowMultiple = false,
        Inherited = true
    )]
    public sealed class WProtoMemberAttribute : Attribute
    {
        /// <summary>
        /// Initializes the attribute with the member's wire field number.
        /// </summary>
        /// <param name="tag">The field number, 1 through 536,870,911.</param>
        public WProtoMemberAttribute(int tag)
        {
            Tag = tag;
        }

        /// <summary>The wire field number.</summary>
        public int Tag { get; }

        /// <summary>
        /// The schema name for this member. Defaults to the member's own name when unset.
        /// </summary>
        /// <remarks>Never written to the wire; protobuf identifies fields by number alone.</remarks>
        public string Name { get; set; }

        /// <summary>
        /// Indicates whether the member is written even when it holds its type's default value.
        /// </summary>
        /// <remarks>
        /// <para>
        /// It forces a <b>value</b> onto the wire, and never invents one: a required <c>int</c> at 0
        /// and a required struct sub-message at <c>default</c> are both written, while a required
        /// <c>null</c> <see cref="string"/>, <c>byte[]</c> or message reference is still absent.
        /// </para>
        /// <para>
        /// It has <b>no effect on a repeated member</b>, because null and empty are already the same
        /// bytes there and neither produces a field to force. protobuf-net ignores it in the same
        /// place, and matching that is what keeps saved data readable.
        /// </para>
        /// </remarks>
        public bool IsRequired { get; set; }

        /// <summary>
        /// Selects the encoding a signed integer member uses.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="WProtoDataFormat.ZigZag"/> is the one alternative offered, and it changes the
        /// bytes: a member that was <c>int32</c> and becomes <c>sint32</c> is read as a different
        /// number by anything holding the old payload, so it is a wire break rather than a hint.
        /// </para>
        /// <para>
        /// Only <c>sbyte</c>, <c>short</c>, <c>int</c> and <c>long</c> have a ZigZag encoding,
        /// including as a <see cref="System.Nullable{T}"/>. Asking for one anywhere else is a build
        /// error (<c>WPROTO037</c>) rather than an annotation that quietly does nothing.
        /// </para>
        /// </remarks>
        public WProtoDataFormat DataFormat { get; set; }

        /// <summary>
        /// Indicates whether reading a repeated member replaces the existing collection instead of
        /// appending to it.
        /// </summary>
        /// <remarks>
        /// Appending is the default, and it appends to whatever the constructor left in the member.
        /// An <b>absent</b> field leaves that value alone either way: "absent" and "empty" are the
        /// same bytes, so there is nothing for a replacement to be triggered by.
        /// </remarks>
        public bool OverwriteList { get; set; }
    }
}
