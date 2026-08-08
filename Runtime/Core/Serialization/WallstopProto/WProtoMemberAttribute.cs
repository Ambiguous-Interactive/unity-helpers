// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto
{
    using System;

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
        public bool IsRequired { get; set; }

        /// <summary>
        /// Indicates whether reading a repeated member replaces the existing collection instead of
        /// appending to it.
        /// </summary>
        public bool OverwriteList { get; set; }
    }
}
