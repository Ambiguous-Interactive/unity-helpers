// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// One <c>[WProtoMember]</c>, reduced to the code that encodes and decodes it.
    /// </summary>
    /// <remarks>
    /// The encoding rules here are protobuf-net's at CompatibilityLevel 200, not proto3's, because
    /// wire compatibility with data already on disk is the whole point. Several of them are
    /// counter-intuitive and were measured against the oracle rather than reasoned about: a member
    /// equal to its type's default is omitted -- which silently turns <c>-0.0</c> into <c>+0.0</c>,
    /// since <c>-0.0 == 0.0</c> -- while an empty-but-non-null <c>string</c> or <c>byte[]</c> is
    /// written as a tag and a zero length. Only null is absent. Inside a repeated member the
    /// omission rule is reversed: every element is written, defaults included.
    /// </remarks>
    internal abstract class Member
    {
        protected const string Proto =
            "global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto";

        protected Member(string name, int tag)
        {
            Name = name;
            Tag = tag;
        }

        internal int Tag { get; }

        protected string Name { get; }

        /// <summary>The member access on the value being written.</summary>
        protected string Access => "value." + Name;

        /// <summary>
        /// Builds the member for <paramref name="type"/>, or <c>null</c> when it is not supported.
        /// </summary>
        /// <param name="contractName">The declaring contract's name, for diagnostics at runtime.</param>
        /// <param name="name">The member's name.</param>
        /// <param name="tag">The wire field number.</param>
        /// <param name="type">The member's declared type.</param>
        /// <param name="isRequired">Whether <c>IsRequired</c> was set.</param>
        /// <param name="overwriteList">Whether <c>OverwriteList</c> was set.</param>
        internal static Member Create(
            string contractName,
            string name,
            int tag,
            ITypeSymbol type,
            bool isRequired,
            bool overwriteList
        )
        {
            RepeatedMember repeated = RepeatedMember.TryCreate(
                contractName,
                name,
                tag,
                type,
                overwriteList
            );
            if (repeated != null)
            {
                return repeated;
            }

            return ScalarMember.TryCreate(name, tag, type, isRequired);
        }

        /// <summary>Appends this member's contribution to the formatter's <c>Measure</c>.</summary>
        internal abstract void EmitMeasure(Writer writer);

        /// <summary>Appends this member's contribution to the formatter's <c>Write</c>.</summary>
        internal abstract void EmitWrite(Writer writer);

        /// <summary>
        /// Appends any locals the read loop needs, declared before it starts.
        /// </summary>
        internal virtual void EmitReadLocals(Writer writer) { }

        /// <summary>
        /// Appends the <c>case</c> sections that decode this member.
        /// </summary>
        /// <param name="writer">The destination.</param>
        /// <param name="qualifiedContract">The contract type, for the failure path's <c>default</c>.</param>
        internal abstract void EmitReadCases(Writer writer, string qualifiedContract);

        /// <summary>
        /// Appends anything that has to happen after the read loop, such as committing an
        /// accumulated collection.
        /// </summary>
        internal virtual void EmitReadEpilogue(Writer writer) { }

        /// <summary>
        /// Emits the two statements that abandon a read, shared by every failure path.
        /// </summary>
        protected static void EmitReadFailure(Writer writer, string qualifiedContract)
        {
            writer.Line("value = default(" + qualifiedContract + ");");
            writer.Line("return false;");
        }

        /// <summary>
        /// Opens a <c>case</c> section for this member at <paramref name="wireType"/>.
        /// </summary>
        protected void OpenCase(Writer writer, string wireType)
        {
            writer.Line("case " + Tag + " when wireType == " + wireType + ":" + Writer.Open);
            writer.Indent();
        }

        protected static void Close(Writer writer)
        {
            writer.Outdent();
            writer.Line("}");
        }
    }
}
