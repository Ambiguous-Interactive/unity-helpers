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

        /// <summary>
        /// Whether reading must hold this member's value aside and assign it after the loop.
        /// </summary>
        /// <remarks>
        /// Set for every member of a contract that declares <c>[WProtoInclude]</c>. The instance a
        /// member lands on is not known until an include tag is seen, and protobuf-net permits that
        /// tag in either position -- it writes it first, but a payload with it last is legal and
        /// decodes fine there. Assigning eagerly would write onto an object about to be replaced,
        /// and the loss would be silent.
        /// </remarks>
        internal bool Deferred { get; set; }

        protected string Name { get; }

        /// <summary>
        /// The member's declared type, fully qualified — needed by a contract that has to be built
        /// through a constructor rather than assigned member by member.
        /// </summary>
        internal string DeclaredType { get; set; }

        /// <summary>
        /// The local a deferred read holds this member in before the instance exists.
        /// </summary>
        internal string ReadLocal => "value" + Tag;

        /// <summary>The member's own name, for a constructor parameter list.</summary>
        internal string MemberName => Name;

        /// <summary>
        /// Whether this member cannot be assigned after construction -- a <c>readonly</c> field, or
        /// a property with no setter.
        /// </summary>
        /// <remarks>
        /// One of these anywhere on a contract switches the whole contract to being built through a
        /// generated constructor, because C# allows a readonly field to be assigned only there. That
        /// keeps the type's immutability intact instead of asking its author to give it up.
        /// </remarks>
        internal bool RequiresConstruction { get; set; }

        /// <summary>
        /// Whether the contract is built by a constructor once every member has been read, which
        /// means no member may be assigned onto the instance and none may seed from it.
        /// </summary>
        internal bool ConstructAtEnd { get; set; }

        /// <summary>
        /// Whether the generated read constructor is standing in for protobuf-net's uninitialized
        /// allocation because the contract declares <c>SkipConstructor</c>.
        /// </summary>
        /// <remarks>
        /// The generated constructor necessarily runs field initializers. A present repeated or map
        /// member must nevertheless replace an initialized collection rather than append to it,
        /// because protobuf-net's uninitialized instance has no constructor seed to preserve.
        /// Members that are absent from the payload still retain the initializer, which is the
        /// documented, safer difference of this AOT implementation.
        /// </remarks>
        internal bool SkipConstructor { get; set; }

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
        /// <param name="ambiguous">
        /// Set when the type is both a contract and a collection and nothing says which it is, so
        /// the caller reports <c>WPROTO012</c> rather than "unsupported".
        /// </param>
        /// <remarks>
        /// The order of these three questions is the whole encoding decision for a type that is both
        /// a message and a collection -- <c>Deque</c>, <c>CyclicBuffer</c>, <c>SparseSet</c>,
        /// <c>BitSet</c> and the four <c>Serializable*</c> collections are all exactly that. Reading
        /// one as a repeated field silently discards its <c>[WProtoMember]</c>s; reading it as a
        /// message silently discards its elements. protobuf-net resolves it by letting list handling
        /// win unless <c>IgnoreListHandling</c> is set, which is why that flag exists on eight of
        /// this package's own contracts. Guessing is refused here instead.
        /// </remarks>
        internal static Member Create(
            string contractName,
            string name,
            int tag,
            ITypeSymbol type,
            bool isRequired,
            bool overwriteList,
            SurrogateMap surrogates,
            out bool ambiguous
        )
        {
            ambiguous = false;

            // A member typed as one of the contract's own type parameters cannot be given a wire
            // type here; the closure decides it. Checked first, because a type parameter is neither
            // a contract nor a collection and every later question would answer "unsupported".
            GenericMember generic = GenericMember.TryCreate(name, tag, type, isRequired);
            if (generic != null)
            {
                return generic;
            }

            bool isContract = Shape.IsContract(type);
            if (isContract && Shape.IgnoresListHandling(type))
            {
                // Explicitly a message, whatever interfaces it implements.
                return ScalarMember.TryCreate(name, tag, type, isRequired, surrogates);
            }

            // Maps first: a dictionary also implements ICollection<KeyValuePair<K,V>>, and the
            // repeated path would take it and emit a repeated VALUE where protobuf wants a repeated
            // ENTRY MESSAGE. The two are different bytes, so the order of these two questions is
            // itself a wire-format decision.
            MapMember map = MapMember.TryCreate(
                contractName,
                name,
                tag,
                type,
                overwriteList,
                surrogates
            );
            if (map != null)
            {
                if (isContract)
                {
                    ambiguous = true;
                    return null;
                }

                return map;
            }

            RepeatedMember repeated = RepeatedMember.TryCreate(
                contractName,
                name,
                tag,
                type,
                overwriteList,
                surrogates
            );
            if (repeated == null)
            {
                return ScalarMember.TryCreate(name, tag, type, isRequired, surrogates);
            }

            if (isContract)
            {
                ambiguous = true;
                return null;
            }

            return repeated;
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
