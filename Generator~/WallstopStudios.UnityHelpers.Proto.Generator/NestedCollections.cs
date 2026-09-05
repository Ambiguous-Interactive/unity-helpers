// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// One generated wrapper message: a formatter emitted inside a contract's own so that a value
    /// protobuf cannot express in place gets a message it can.
    /// </summary>
    /// <remarks>
    /// Two shapes need one. A collection whose element is another collection becomes
    /// <see cref="NestedCollection"/>, whose whole payload is a single repeated or map field. A
    /// rectangular array becomes <see cref="RectangularArray"/>, which carries a dimension header
    /// alongside its run because its elements alone cannot say what shape they came from.
    /// </remarks>
    internal interface IGeneratedMessage
    {
        /// <summary>The nested type's name inside the contract's generated formatter.</summary>
        string FormatterName { get; }

        /// <summary>The type this wrapper carries, fully qualified.</summary>
        string Qualified { get; }

        /// <summary>The type as a developer wrote it, for the generated comment.</summary>
        string Display { get; }

        /// <summary>
        /// How many sub-message levels this wrapper and everything under it occupy.
        /// </summary>
        /// <remarks>
        /// One for itself plus the deepest chain beneath it, so a wrapper reused from the cache can
        /// be asked whether it still fits where it is being used. Without it the depth bound is
        /// order-dependent: a shallow member resolved first seeds the cache, and a deep member
        /// reusing that entry assembles a chain past the reader's limit without the bound ever being
        /// consulted.
        /// </remarks>
        int Depth { get; set; }

        /// <summary>The wrapped value's run, encoded exactly as a top-level member of it would be.</summary>
        Member Inner { get; set; }

        /// <summary>The expression naming this wrapper's shared instance.</summary>
        string Instance { get; }

        /// <summary>Emits the wrapper as a nested formatter inside the contract's own.</summary>
        /// <param name="writer">The destination.</param>
        void Emit(Writer writer);
    }

    /// <summary>The pieces every generated wrapper message shares.</summary>
    internal static class GeneratedMessages
    {
        /// <summary>Escapes a type name for the XML doc comment the wrapper carries.</summary>
        /// <param name="display">The type as a developer wrote it.</param>
        /// <returns>The escaped name.</returns>
        internal static string Escape(string display)
        {
            return display.Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }

    /// <summary>
    /// One generated wrapper message: a formatter whose whole payload is a single repeated or map
    /// field at tag 1.
    /// </summary>
    internal sealed class NestedCollection : IGeneratedMessage
    {
        internal NestedCollection(string formatterName, string qualified, string display)
        {
            FormatterName = formatterName;
            Qualified = qualified;
            Display = display;
        }

        /// <inheritdoc />
        public string FormatterName { get; }

        /// <inheritdoc />
        public string Qualified { get; }

        /// <inheritdoc />
        public string Display { get; }

        /// <inheritdoc />
        public Member Inner { get; set; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public string Instance => FormatterName + ".Instance";

        /// <summary>
        /// Emits the wrapper as a nested formatter inside the contract's own.
        /// </summary>
        /// <param name="writer">The destination.</param>
        /// <remarks>
        /// Nested and private rather than shared across the assembly: the wrapper is an
        /// implementation detail of one contract's encoding, it is never looked up by type, and
        /// keeping it here means two contracts that both hold a <c>List&lt;int[]&gt;</c> cannot
        /// collide over a generated name.
        /// </remarks>
        public void Emit(Writer writer)
        {
            writer.Line(
                "/// <summary>Generated WallstopProto wrapper message for the nested collection <c>",
                GeneratedMessages.Escape(Display),
                "</c>. Do not edit.</summary>"
            );
            writer.Line(
                "private sealed class "
                    + FormatterName
                    + " : "
                    + NestedCollections.Proto
                    + ".IWProtoFormatter<"
                    + Qualified
                    + ">"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line(
                "internal static readonly ",
                FormatterName,
                " Instance = new ",
                FormatterName,
                "();"
            );
            writer.Blank();

            EmitMeasure(writer);
            writer.Blank();
            EmitWrite(writer);
            writer.Blank();
            EmitRead(writer);

            writer.Outdent();
            writer.Line("}");
        }

        private void EmitMeasure(Writer writer)
        {
            writer.Line("/// <inheritdoc />");
            writer.Line("public int Measure(in " + Qualified + " value)" + Writer.Open);
            writer.Indent();
            writer.Line("int size = 0;");
            Inner.EmitMeasure(writer);
            writer.Line("return size;");
            writer.Outdent();
            writer.Line("}");
        }

        private void EmitWrite(Writer writer)
        {
            writer.Line("/// <inheritdoc />");
            writer.Line(
                "public bool Write(ref "
                    + NestedCollections.Proto
                    + ".WProtoWriter writer, in "
                    + Qualified
                    + " value)"
                    + Writer.Open
            );
            writer.Indent();
            Inner.EmitWrite(writer);
            writer.Line("return true;");
            writer.Outdent();
            writer.Line("}");
        }

        /// <summary>
        /// Emits the read, which mirrors a contract's own field loop over a single field.
        /// </summary>
        /// <param name="writer">The destination.</param>
        /// <remarks>
        /// The unknown-field branch is not ceremony here either: a wrapper is a real message, so a
        /// payload written by a later build carrying a second field has to be stepped over exactly
        /// rather than refused.
        /// </remarks>
        private void EmitRead(Writer writer)
        {
            writer.Line("/// <inheritdoc />");
            writer.Line(
                "public bool TryRead(ref "
                    + NestedCollections.Proto
                    + ".WProtoReader reader, out "
                    + Qualified
                    + " value)"
                    + Writer.Open
            );
            writer.Indent();

            writer.Line(Qualified, " ", Inner.ReadLocal, " = default(", Qualified, ");");
            Inner.EmitReadLocals(writer);
            writer.Blank();

            writer.Line(
                "while (reader.TryReadTag(out int fieldNumber, out int wireType))" + Writer.Open
            );
            writer.Indent();
            writer.Line("switch (fieldNumber)" + Writer.Open);
            writer.Indent();
            Inner.EmitReadCases(writer, Qualified);
            writer.Line("default:" + Writer.Open);
            writer.Indent();
            writer.Line("if (!reader.TrySkipField(fieldNumber, wireType))" + Writer.Open);
            writer.Indent();
            writer.Line("value = default(", Qualified, ");");
            writer.Line("return false;");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
            writer.Line("break;");
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();

            writer.Line("if (reader.Malformed)" + Writer.Open);
            writer.Indent();
            writer.Line("value = default(", Qualified, ");");
            writer.Line("return false;");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();

            /*
             * A wrapper proves presence, so an empty run means an empty collection rather than an absent
             * outer member.
             */
            writer.Line(
                "// This wrapper message was read, so its collection exists even with no elements in"
            );
            writer.Line("// it -- which is what makes an empty inner collection round trip.");
            Inner.EmitPresentSeed(writer);
            Inner.EmitReadEpilogue(writer, Qualified);

            writer.Line("value = ", Inner.ReadLocal, ";");
            writer.Line("return true;");
            writer.Outdent();
            writer.Line("}");
        }
    }

    /// <summary>
    /// The wrapper messages one contract needs so that a repeated field can hold another one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>repeated repeated</c> has no protobuf spelling. A repeated field is a field number
    /// repeated N times and nothing marks where one inner run ends and the next begins, so every
    /// protobuf implementation answers the same way: a wrapper message per inner collection,
    /// <c>message Wrapper { repeated T values = 1; }</c>, which is exactly what <c>protoc</c> emits
    /// for the equivalent schema. <c>List&lt;int[]&gt;</c> is therefore a repeated <b>message</b>
    /// field whose message carries the inner run at field 1, and it maps back to a concrete proto3
    /// definition rather than to a shape only this package can read.
    /// </para>
    /// <para>
    /// protobuf-net refuses every one of these shapes outright on both 2.4.9 and 3.2.56 -- measured,
    /// <c>NotSupportedException: Nested or jagged lists, arrays and maps are not supported</c>. So
    /// there is no payload in the wild with this shape and no protobuf-net reader that will ever be
    /// handed one, which is what frees the encoding to be chosen at all. It also means these members
    /// alone do not round-trip through protobuf-net, by construction; that asymmetry is documented
    /// rather than hidden, and it is the price of the shapes a game actually holds -- a tile grid, a
    /// per-row cost table, a list of paths.
    /// </para>
    /// <para>
    /// The wrapper is generated from the <b>same</b> <see cref="Member"/> emitter that encodes a
    /// top-level collection, so a nested run gets packing, seeding, ordering, null-element refusal
    /// and reservation identically and by construction rather than by a second implementation that
    /// is free to drift. Nesting is therefore arbitrary-depth for free: the element of a wrapper's
    /// member is resolved through this same registry.
    /// </para>
    /// </remarks>
    internal sealed class NestedCollections
    {
        internal const string Proto =
            "global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto";

        /// <summary>
        /// The deepest chain of wrapper messages this generator will build.
        /// </summary>
        /// <remarks>
        /// The same number <c>WProtoReader.MaxNestingDepth</c> enforces at run time, and it is not
        /// larger for the same reason: every wrapper level is a real sub-message, so a deeper chain
        /// would produce bytes this package can write and can never read back. Refusing here is that
        /// failure moved to the build, where it can be acted on.
        /// <para>
        /// It is also the backstop on resolution itself. A collection whose element type is itself
        /// terminates on the cache below -- the entry is registered before its member is resolved,
        /// so the second visit finds it -- but a generic collection that closes over a <b>deeper</b>
        /// type at each level produces a new closed type every time and would never reach that
        /// cache. A source generator that recurses forever hangs the consumer's build, so the bound
        /// exists whether or not a type that reaches it is easy to write.
        /// </para>
        /// </remarks>
        internal const int MaxDepth = 64;

        private readonly Dictionary<string, IGeneratedMessage> _byType =
            new Dictionary<string, IGeneratedMessage>();

        private readonly List<IGeneratedMessage> _ordered = new List<IGeneratedMessage>();

        private readonly string _contractName;
        private readonly SurrogateMap _surrogates;
        private int _depth;

        /// <summary>
        /// How many wrapper names have been handed out.
        /// </summary>
        /// <remarks>
        /// Its own counter rather than <c>_ordered.Count</c>, because a wrapper is <b>named</b> when
        /// it is registered and <b>added</b> when its member finishes resolving -- and resolving one
        /// wrapper is what discovers the next. A contract holding only <c>int[][][]</c> registers the
        /// wrapper for <c>int[][]</c>, and the wrapper for <c>int[]</c> while that one is still in
        /// flight, so both saw a finished count of zero and both were called
        /// <c>NestedFormatter1</c>: <c>CS0102</c> in the consumer's build, and no generator
        /// diagnostic at all.
        /// </remarks>
        private int _names;

        /// <summary>The deepest wrapper chain the frame currently being resolved has discovered.</summary>
        private int _childDepth;

        internal NestedCollections(string contractName, SurrogateMap surrogates)
        {
            _contractName = contractName;
            _surrogates = surrogates;
        }

        /// <summary>The wrappers this contract needs, in the order they were discovered.</summary>
        internal IReadOnlyList<IGeneratedMessage> All => _ordered;

        /// <summary>
        /// How many times resolution has refused a type for being nested past
        /// <see cref="MaxDepth"/>.
        /// </summary>
        /// <remarks>
        /// A counter rather than a flag, so the caller can attribute a refusal to the member it was
        /// resolving by comparing the value across one <see cref="Member.Create"/> call. A flag
        /// would have to be cleared, and the clear is what a later edit forgets.
        /// </remarks>
        internal int DepthRefusals { get; private set; }

        /// <summary>
        /// Builds the shape for a collection-typed element or map value, or returns <c>null</c> when
        /// <paramref name="type"/> is not a collection this generator can wrap.
        /// </summary>
        /// <param name="type">The element or value type.</param>
        /// <param name="qualified">Its fully qualified name.</param>
        /// <param name="memberName">The declaring member, for a null element's runtime message.</param>
        /// <returns>The wrapper's shape, or <c>null</c>.</returns>
        internal Shape TryShape(ITypeSymbol type, string qualified, string memberName)
        {
            if (_byType.TryGetValue(qualified, out IGeneratedMessage existing))
            {
                if (existing.Inner == null)
                {
                    return null;
                }

                /*
                 * Cached wrappers must charge their entire inner depth; reuse deeper in a member cannot
                 * bypass the limit.
                 */
                if (MaxDepth < _depth + existing.Depth)
                {
                    DepthRefusals++;
                    return null;
                }

                _childDepth = existing.Depth < _childDepth ? _childDepth : existing.Depth;
                return ShapeFor(existing, type);
            }

            if (MaxDepth <= _depth)
            {
                DepthRefusals++;
                return null;
            }

            // Rectangular dimensions cannot be recovered from the element run and need a separate header.
            IArrayTypeSymbol rectangular =
                type is IArrayTypeSymbol array && 2 <= array.Rank ? array : null;

            IGeneratedMessage wrapper =
                rectangular == null
                    ? (IGeneratedMessage)
                        new NestedCollection(
                            "NestedFormatter" + ++_names,
                            qualified,
                            TypeNaming.Display(type)
                        )
                    : new RectangularArray(
                        "NestedFormatter" + ++_names,
                        qualified,
                        rectangular,
                        _contractName
                    );

            // Register the incomplete wrapper before resolving its member to stop self-referential recursion.
            _byType[qualified] = wrapper;

            int enclosingChildDepth = _childDepth;
            _childDepth = 0;
            _depth++;
            Member inner;
            try
            {
                if (rectangular != null)
                {
                    inner = RepeatedMember.CreateRectangular(
                        _contractName,
                        memberName,
                        RectangularArray.ValuesTag,
                        rectangular,
                        _surrogates,
                        this
                    );
                }
                else
                {
                    inner = Member.Create(
                        _contractName,
                        memberName,
                        1,
                        type,
                        false,
                        // Wrappers have no constructor seed, so reads replace rather than append.
                        true,
                        false,
                        _surrogates,
                        this,
                        out bool ambiguous
                    );

                    if (ambiguous)
                    {
                        inner = null;
                    }
                }
            }
            finally
            {
                _depth--;
            }

            int depth = _childDepth + 1;

            /*
             * Wrapping unsupported scalar or contract shapes would invent an encoding instead of reporting
             * refusal.
             */
            if (!(inner is RepeatedMember) && !(inner is MapMember))
            {
                _childDepth = enclosingChildDepth;

                /*
                 * Depth failures depend on the lookup location; caching them would poison supported shallower
                 * uses.
                 */
                _byType.Remove(qualified);
                return null;
            }

            _childDepth = enclosingChildDepth < depth ? depth : enclosingChildDepth;
            wrapper.Depth = depth;
            inner.WrapsWholeValue = true;
            inner.ConstructAtEnd = true;
            inner.DeclaredType = qualified;
            wrapper.Inner = inner;
            _ordered.Add(wrapper);
            return ShapeFor(wrapper, type);
        }

        /// <summary>
        /// Emits every wrapper this contract needs.
        /// </summary>
        /// <param name="writer">The destination.</param>
        internal void Emit(Writer writer)
        {
            foreach (IGeneratedMessage wrapper in _ordered)
            {
                writer.Blank();
                wrapper.Emit(writer);
            }
        }

        private static Shape ShapeFor(IGeneratedMessage wrapper, ITypeSymbol type)
        {
            return Shape.Message(wrapper.Instance, wrapper.Qualified, type.IsValueType);
        }
    }
}
