// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// A member whose field number carries a map: a dictionary, written as a repeated sub-message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A protobuf map is not a repeated value; it is a repeated <b>entry message</b> with the key at
    /// field 1 and the value at field 2. That is why a dictionary could not simply ride the
    /// repeated-field path -- the bytes are a different shape, and accepting one there would have
    /// produced a payload no protobuf implementation could read back.
    /// </para>
    /// <para>
    /// Measured against protobuf-net 3.2.56, and the surprise is that the entry obeys the ordinary
    /// scalar rules rather than always carrying both halves. <c>{"a": 0}</c> encodes as
    /// <c>0A 03 0A 01 61</c> -- tag, length 3, key only -- because the value equals its default and
    /// is omitted exactly as a member would be. An empty-string key is still written (<c>0A 00</c>),
    /// only null is absent, and a null value is dropped the same way. An empty dictionary writes
    /// nothing at all, so it reads back as whatever the constructor left behind.
    /// </para>
    /// <para>
    /// Reading an entry with no key or value yields that type's protobuf default. In particular,
    /// a missing string half is <see cref="string.Empty"/> rather than <c>null</c>. A repeated key is
    /// last-wins -- the entry is applied through the indexer rather than <c>Add</c>, which would throw
    /// on the second occurrence of a key a hostile payload repeated.
    /// </para>
    /// </remarks>
    internal sealed class MapMember : Member
    {
        private const string ListType = "global::System.Collections.Generic.List";

        private const string PairType = "global::System.Collections.Generic.KeyValuePair";

        private const string DictionaryType = "global::System.Collections.Generic.Dictionary";

        private const string ReadOnlyDictionaryType =
            "global::System.Collections.ObjectModel.ReadOnlyDictionary";

        private readonly Shape _key;
        private readonly Shape _value;
        private readonly string _keyQualified;
        private readonly string _valueQualified;
        private readonly string _mapQualified;
        private readonly string _accumulatorQualified;
        private readonly string _commitGeneric;
        private readonly string _contractName;
        private readonly bool _overwrite;
        private readonly bool _mapIsValueType;
        private readonly bool _keyIsString;
        private readonly bool _valueIsString;

        private MapMember(
            string contractName,
            string name,
            int tag,
            Shape key,
            Shape value,
            string keyQualified,
            string valueQualified,
            string mapQualified,
            string accumulatorQualified,
            string commitGeneric,
            bool overwrite,
            bool mapIsValueType,
            bool keyIsString,
            bool valueIsString
        )
            : base(name, tag)
        {
            _contractName = contractName;
            _key = key;
            _value = value;
            _keyQualified = keyQualified;
            _valueQualified = valueQualified;
            _mapQualified = mapQualified;
            _accumulatorQualified = accumulatorQualified;
            _commitGeneric = commitGeneric;
            _overwrite = overwrite;
            _mapIsValueType = mapIsValueType;
            _keyIsString = keyIsString;
            _valueIsString = valueIsString;
        }

        /// <summary>
        /// Whether entries are collected into a dictionary of a different type from the member's.
        /// </summary>
        /// <remarks>
        /// True for the two dictionary interfaces and for <c>ReadOnlyDictionary&lt;K,V&gt;</c>: none
        /// can be constructed and filled, so the member's current entries are copied into a
        /// <c>Dictionary&lt;K,V&gt;</c> and the decoded ones merged on top.
        /// </remarks>
        private bool SeedsByCopy => _accumulatorQualified != _mapQualified;

        /// <summary>
        /// Builds the member when <paramref name="type"/> is a supported map, and returns
        /// <c>null</c> otherwise so the caller can try the other shapes.
        /// </summary>
        /// <remarks>
        /// The requirements mirror the collection path: it implements <c>IDictionary&lt;K,V&gt;</c>
        /// exactly once so the key and value types are unambiguous, it has an accessible
        /// parameterless constructor because reading has to produce one, and it exposes a settable
        /// indexer because entries are applied last-wins. Nothing here requires a class -- a
        /// dictionary implemented as a struct is accepted on the same terms as any other.
        /// </remarks>
        internal static MapMember TryCreate(
            string contractName,
            string name,
            int tag,
            ITypeSymbol type,
            bool overwriteList,
            SurrogateMap surrogates,
            NestedCollections nested
        )
        {
            if (!(type is INamedTypeSymbol named))
            {
                return null;
            }

            ITypeSymbol keyType;
            ITypeSymbol valueType;
            string commitGeneric;
            bool wellKnown = TryWellKnown(named, out keyType, out valueType, out commitGeneric);

            if (!wellKnown)
            {
                commitGeneric = null;
                if (named.TypeKind != TypeKind.Class && named.TypeKind != TypeKind.Struct)
                {
                    return null;
                }

                if (named.IsAbstract)
                {
                    return null;
                }

                foreach (INamedTypeSymbol candidate in named.AllInterfaces)
                {
                    if (
                        !candidate.IsGenericType
                        || candidate.ConstructedFrom.ToDisplayString()
                            != "System.Collections.Generic.IDictionary<TKey, TValue>"
                    )
                    {
                        continue;
                    }

                    if (keyType != null)
                    {
                        return null;
                    }

                    keyType = candidate.TypeArguments[0];
                    valueType = candidate.TypeArguments[1];
                }

                if (keyType == null)
                {
                    return null;
                }

                if (!HasParameterlessConstructor(named) || !HasSettableIndexer(named, keyType))
                {
                    return null;
                }
            }

            string keyQualified = keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string valueQualified = valueType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );

            // Wrapped collection keys lack stable identity and have no oracle-compatible wire form.
            Shape key = Shape.For(keyType, keyQualified, surrogates);
            Shape value = Shape.For(valueType, valueQualified, surrogates, nested, name);
            if (key == null || value == null)
            {
                return null;
            }

            /*
             * Compatibility permits protobuf-net scalar keys beyond the spec; reference keys other than
             * string lack stable round-trip identity.
             */
            bool keyIsString = keyType.SpecialType == SpecialType.System_String;
            if (key.IsReference && !keyIsString)
            {
                return null;
            }

            string mapQualified = named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            return new MapMember(
                contractName,
                name,
                tag,
                key,
                value,
                keyQualified,
                valueQualified,
                mapQualified,
                wellKnown
                    ? DictionaryType + "<" + keyQualified + ", " + valueQualified + ">"
                    : mapQualified,
                commitGeneric,
                overwriteList,
                named.IsValueType,
                keyIsString,
                valueType.SpecialType == SpecialType.System_String
            );
        }

        /// <summary>
        /// Recognizes the dictionary shapes that cannot be constructed and filled directly.
        /// </summary>
        /// <param name="named">The member's declared type.</param>
        /// <param name="keyType">Receives the key type.</param>
        /// <param name="valueType">Receives the value type.</param>
        /// <param name="commitGeneric">Receives the type the finished dictionary is wrapped in.</param>
        /// <returns><c>true</c> when the type is one of them.</returns>
        /// <remarks>
        /// <c>Dictionary&lt;K,V&gt;</c> is what protobuf-net produces for both interfaces, measured
        /// against 3.2.56, and which type a round trip leaves behind is a decision rather than an
        /// implementation detail. <c>ReadOnlyDictionary&lt;K,V&gt;</c> is the map analogue of
        /// <c>ReadOnlyCollection&lt;T&gt;</c>: protobuf-net writes it and then refuses to read it
        /// back ("No parameterless constructor found"), so accepting it is strictly more than the
        /// oracle does with bytes it produced itself.
        /// </remarks>
        private static bool TryWellKnown(
            INamedTypeSymbol named,
            out ITypeSymbol keyType,
            out ITypeSymbol valueType,
            out string commitGeneric
        )
        {
            keyType = null;
            valueType = null;
            commitGeneric = null;
            if (!named.IsGenericType || named.TypeArguments.Length != 2)
            {
                return false;
            }

            INamedTypeSymbol definition = named.ConstructedFrom;
            string qualified =
                definition.ContainingNamespace == null
                    ? definition.MetadataName
                    : definition.ContainingNamespace.ToDisplayString()
                        + "."
                        + definition.MetadataName;

            switch (qualified)
            {
                case "System.Collections.Generic.IDictionary`2":
                case "System.Collections.Generic.IReadOnlyDictionary`2":
                    break;

                case "System.Collections.ObjectModel.ReadOnlyDictionary`2":
                    commitGeneric = ReadOnlyDictionaryType;
                    break;

                default:
                    return false;
            }

            keyType = named.TypeArguments[0];
            valueType = named.TypeArguments[1];
            return true;
        }

        private static bool HasParameterlessConstructor(INamedTypeSymbol type)
        {
            if (type.IsValueType)
            {
                return true;
            }

            foreach (IMethodSymbol constructor in type.InstanceConstructors)
            {
                if (
                    constructor.Parameters.Length == 0
                    && constructor.DeclaredAccessibility == Accessibility.Public
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSettableIndexer(INamedTypeSymbol type, ITypeSymbol keyType)
        {
            for (INamedTypeSymbol current = type; current != null; current = current.BaseType)
            {
                foreach (ISymbol member in current.GetMembers("this[]"))
                {
                    if (
                        member is IPropertySymbol indexer
                        && indexer.SetMethod != null
                        && indexer.SetMethod.DeclaredAccessibility == Accessibility.Public
                        && indexer.Parameters.Length == 1
                        && SymbolEqualityComparer.Default.Equals(
                            indexer.Parameters[0].Type,
                            keyType
                        )
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private string PairLocal => "pair" + Tag;

        private string EntrySize => "entrySize" + Tag;

        private string Accumulator => "map" + Tag;

        /// <summary>Whether <c>SkipConstructor</c> suppresses this member's seed outright.</summary>
        /// <remarks>
        /// Only where the instance can never have come from a caller; otherwise the answer is a
        /// run-time one. See <see cref="Member.SeedGuard"/>.
        /// </remarks>
        private bool SeedSuppressed => SkipConstructor && SeedGuard == null;

        /// <summary>The run-time guard on this member's seed, or <c>null</c> when it has none.</summary>
        private string Guard => SkipConstructor ? SeedGuard : null;

        private string SeenFlag => "seen" + Tag;

        private string PendingType =>
            ListType + "<" + PairType + "<" + _keyQualified + ", " + _valueQualified + ">>";

        private string KeyAccess => PairLocal + ".Key";

        private string ValueAccess => PairLocal + ".Value";

        /// <inheritdoc />
        internal override void EmitMeasure(Writer writer)
        {
            int open = OpenLoop(writer);
            EmitEntrySize(writer);
            writer.Line(
                "size += "
                    + Proto
                    + ".WProtoSizes.TagSize("
                    + Tag
                    + ") + "
                    + Proto
                    + ".WProtoSizes.LengthDelimitedSize("
                    + EntrySize
                    + ");"
            );
            CloseAll(writer, open);
            writer.Blank();
        }

        /// <inheritdoc />
        internal override void EmitWrite(Writer writer)
        {
            int open = OpenLoop(writer);

            // Back-patch map lengths to avoid re-measuring values and repeating serialization hooks.
            string token = "entry" + Tag;
            writer.Line(
                "if (!writer.TryBeginLengthDelimited("
                    + Tag
                    + ", out "
                    + Proto
                    + ".WProtoLengthToken "
                    + token
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line("return false;");
            Close(writer);
            writer.Blank();

            EmitHalfWrite(writer, _key, KeyAccess, 1, true);
            EmitHalfWrite(writer, _value, ValueAccess, 2, false);

            writer.Line("if (!writer.TryCloseLengthDelimited(" + token + "))" + Writer.Open);
            writer.Indent();
            writer.Line("return false;");
            Close(writer);

            CloseAll(writer, open);
            writer.Blank();
        }

        private void EmitHalfWrite(Writer writer, Shape shape, string access, int tag, bool isKey)
        {
            writer.Line("if (" + MapPresence(shape, access, isKey) + ")" + Writer.Open);
            writer.Indent();
            writer.Line("if (!(" + shape.WriteCall(access, tag) + "))" + Writer.Open);
            writer.Indent();
            writer.Line("return false;");
            Close(writer);
            Close(writer);
            writer.Blank();
        }

        /// <summary>
        /// Emits the presence guard and the <c>foreach</c> header, and returns how many blocks the
        /// caller has to close.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A struct dictionary is always present, so it gets no guard: <c>member != null</c> on a
        /// value type is <c>CS0019</c>, a compiler error inside code the consumer never wrote.
        /// </para>
        /// <para>
        /// Unlike a repeated element, a null map <b>value</b> is legal -- protobuf-net omits it and
        /// the entry carries only its key -- so there is nothing to guard inside the loop.
        /// </para>
        /// </remarks>
        private int OpenLoop(Writer writer)
        {
            int open = 0;
            if (!_mapIsValueType)
            {
                writer.Line("if (" + Access + " != null)" + Writer.Open);
                writer.Indent();
                open++;
            }

            writer.Line(
                "foreach ("
                    + PairType
                    + "<"
                    + _keyQualified
                    + ", "
                    + _valueQualified
                    + "> "
                    + PairLocal
                    + " in "
                    + Access
                    + ")"
                    + Writer.Open
            );
            writer.Indent();
            return open + 1;
        }

        private static void CloseAll(Writer writer, int count)
        {
            for (int closed = 0; closed < count; closed++)
            {
                Close(writer);
            }
        }

        /// <summary>
        /// Emits the entry's payload size, which both halves obey protobuf-net's map omission rules for.
        /// </summary>
        private void EmitEntrySize(Writer writer)
        {
            writer.Line("int " + EntrySize + " = 0;");
            EmitHalfSize(writer, _key, KeyAccess, 1, true);
            EmitHalfSize(writer, _value, ValueAccess, 2, false);
            writer.Blank();
        }

        private void EmitHalfSize(Writer writer, Shape shape, string access, int tag, bool isKey)
        {
            writer.Line("if (" + MapPresence(shape, access, isKey) + ")" + Writer.Open);
            writer.Indent();
            writer.Line(
                EntrySize
                    + " += "
                    + Proto
                    + ".WProtoSizes.TagSize("
                    + tag
                    + ") + "
                    + Shape.Fill(shape.SizeExpression, access)
                    + ";"
            );
            Close(writer);
        }

        private static string MapPresence(Shape shape, string access, bool isKey)
        {
            /*
             * The shipped v3 oracle writes fixed-width zero keys but omits zero values; both forms remain
             * readable across majors.
             */
            return
                isKey
                && (
                    shape.WireType == Proto + ".WProtoWireType.Fixed32"
                    || shape.WireType == Proto + ".WProtoWireType.Fixed64"
                )
                ? "true"
                : Shape.Fill(shape.PresenceTest, access);
        }

        /// <inheritdoc />
        internal override void EmitReadLocals(Writer writer)
        {
            writer.Line("bool " + SeenFlag + " = false;");

            if (Deferred)
            {
                writer.Line(PendingType + " " + Accumulator + " = null;");
                if (ConstructAtEnd)
                {
                    writer.Line(
                        DeclaredType
                            + " "
                            + ReadLocal
                            + " = "
                            + (SeedsFromInstance ? SeedSource : "default(" + DeclaredType + ")")
                            + ";"
                    );
                }

                return;
            }

            writer.Line(
                _accumulatorQualified
                    + " "
                    + Accumulator
                    + " = default("
                    + _accumulatorQualified
                    + ");"
            );
        }

        /// <inheritdoc />
        internal override void EmitReadCases(Writer writer, string qualifiedContract)
        {
            string entry = "entry" + Tag;

            // Map temporaries need distinct names from immutable member read locals to avoid CS0136.
            string keyLocal = "entryKey" + Tag;
            string valueLocal = "entryValue" + Tag;
            string decodedKey = "decodedKey" + Tag;
            string decodedValue = "decodedValue" + Tag;

            OpenCase(writer, Proto + ".WProtoWireType.LengthDelimited");
            EmitSeed(writer);

            /*
             * Unlike packed primitives, a map entry may contain another message and must charge nesting
             * depth.
             */
            writer.Line(
                "if (!reader.TryReadMessage(out "
                    + Proto
                    + ".WProtoReader "
                    + entry
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();

            writer.Line(
                _keyQualified
                    + " "
                    + keyLocal
                    + " = "
                    + (_keyIsString ? "string.Empty" : "default(" + _keyQualified + ")")
                    + ";"
            );
            writer.Line(
                _valueQualified
                    + " "
                    + valueLocal
                    + " = "
                    + (_valueIsString ? "string.Empty" : "default(" + _valueQualified + ")")
                    + ";"
            );
            writer.Blank();
            writer.Line(
                "while ("
                    + entry
                    + ".TryReadTag(out int entryField"
                    + Tag
                    + ", out int entryWire"
                    + Tag
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line("switch (entryField" + Tag + ")" + Writer.Open);
            writer.Indent();

            EmitHalfRead(writer, _key, 1, keyLocal, decodedKey, entry, qualifiedContract);
            EmitHalfRead(writer, _value, 2, valueLocal, decodedValue, entry, qualifiedContract);

            writer.Line("default:" + Writer.Open);
            writer.Indent();
            writer.Line(
                "if (!"
                    + entry
                    + ".TrySkipField(entryField"
                    + Tag
                    + ", entryWire"
                    + Tag
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();
            writer.Line("break;");
            Close(writer);

            Close(writer);
            Close(writer);
            writer.Blank();

            writer.Line("if (" + entry + ".Malformed)" + Writer.Open);
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();

            // Indexer assignment preserves last-wins behavior without throwing on repeated payload keys.
            if (Deferred)
            {
                writer.Line(
                    Accumulator
                        + ".Add(new "
                        + PairType
                        + "<"
                        + _keyQualified
                        + ", "
                        + _valueQualified
                        + ">("
                        + keyLocal
                        + ", "
                        + valueLocal
                        + "));"
                );
            }
            else
            {
                writer.Line(Accumulator + "[" + keyLocal + "] = " + valueLocal + ";");
            }

            writer.Line("break;");
            Close(writer);
        }

        private void EmitHalfRead(
            Writer writer,
            Shape shape,
            int tag,
            string target,
            string decoded,
            string entry,
            string qualifiedContract
        )
        {
            writer.Line(
                "case "
                    + tag
                    + " when entryWire"
                    + Tag
                    + " == "
                    + shape.WireType
                    + ":"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line(
                "if (!"
                    + entry
                    + "."
                    + shape.ReadMethod
                    + "("
                    + shape.ReadArguments
                    + "out "
                    + shape.ReadLocalType
                    + " "
                    + decoded
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();
            writer.Line(target + " = " + Shape.Fill(shape.AssignExpression, decoded) + ";");
            writer.Line("break;");
            Close(writer);
        }

        /// <summary>
        /// Emits the one-time creation of the accumulator.
        /// </summary>
        private void EmitSeed(Writer writer)
        {
            writer.Line("if (!" + SeenFlag + ")" + Writer.Open);
            writer.Indent();
            writer.Line(SeenFlag + " = true;");

            if (Deferred)
            {
                writer.Line(Accumulator + " = new " + PendingType + "();");
            }
            else if (_overwrite || SeedSuppressed)
            {
                writer.Line(Accumulator + " = new " + _accumulatorQualified + "();");
            }
            else if (SeedsByCopy)
            {
                writer.Line(Accumulator + " = new " + _accumulatorQualified + "();");
                EmitCopyFromMember(writer, Accumulator);
            }
            else
            {
                writer.Line(Accumulator + " = " + ExistingOrFresh() + ";");
            }

            Close(writer);
            writer.Blank();
        }

        /// <summary>
        /// Emits the merge of the member's current entries into <paramref name="accumulator"/>.
        /// </summary>
        /// <remarks>
        /// Entry by entry through the indexer rather than through a copying constructor: the member
        /// may be an <c>IReadOnlyDictionary&lt;K,V&gt;</c>, which no <c>Dictionary&lt;K,V&gt;</c>
        /// constructor accepts on every target framework this package supports.
        /// </remarks>
        private void EmitCopyFromMember(Writer writer, string accumulator)
        {
            string present = "read." + Name + " != null";
            writer.Line(
                "if (" + (Guard == null ? present : Guard + " && " + present) + ")" + Writer.Open
            );
            writer.Indent();
            writer.Line(
                "foreach ("
                    + PairType
                    + "<"
                    + _keyQualified
                    + ", "
                    + _valueQualified
                    + "> "
                    + PairLocal
                    + " in read."
                    + Name
                    + ")"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line(accumulator + "[" + KeyAccess + "] = " + ValueAccess + ";");
            Close(writer);
            Close(writer);
        }

        /// <summary>
        /// The dictionary entries are applied to: the constructor's own, or a fresh one.
        /// </summary>
        /// <remarks>
        /// For a struct dictionary this is a <b>copy</b>, necessarily -- which is why the epilogue
        /// assigns it back. <c>??</c> has no meaning on a value type (<c>CS0019</c>), and there is
        /// no null state for it to test: a struct member is always present.
        /// </remarks>
        private string ExistingOrFresh()
        {
            string fresh = "new " + _accumulatorQualified + "()";
            if (Guard == null)
            {
                return _mapIsValueType ? "read." + Name : "read." + Name + " ?? " + fresh;
            }

            return _mapIsValueType
                ? "(" + Guard + " ? read." + Name + " : " + fresh + ")"
                : "(" + Guard + " ? read." + Name + " : null) ?? " + fresh;
        }

        /// <inheritdoc />
        internal override void EmitPresentSeed(Writer writer)
        {
            EmitSeed(writer);
        }

        /// <inheritdoc />
        internal override void EmitReadEpilogue(Writer writer, string qualifiedContract)
        {
            writer.Line("if (" + SeenFlag + ")" + Writer.Open);
            writer.Indent();

            string destination = ConstructAtEnd ? ReadLocal : "read." + Name;

            if (!Deferred)
            {
                writer.Line(destination + " = " + Commit(Accumulator) + ";");
                Close(writer);
                writer.Blank();
                return;
            }

            string target = "target" + Tag;
            bool fresh = _overwrite || Unseeded || SeedSuppressed;
            writer.Line(
                _accumulatorQualified
                    + " "
                    + target
                    + " = "
                    + (
                        fresh || SeedsByCopy
                            ? "new " + _accumulatorQualified + "()"
                            : ExistingOrFresh()
                    )
                    + ";"
            );

            if (!fresh && SeedsByCopy)
            {
                EmitCopyFromMember(writer, target);
            }

            writer.Line(
                "foreach ("
                    + PairType
                    + "<"
                    + _keyQualified
                    + ", "
                    + _valueQualified
                    + "> "
                    + PairLocal
                    + " in "
                    + Accumulator
                    + ")"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line(target + "[" + KeyAccess + "] = " + ValueAccess + ";");
            Close(writer);
            writer.Line(destination + " = " + Commit(target) + ";");

            Close(writer);
            writer.Blank();
        }

        /// <summary>The expression that turns a finished dictionary into the member's value.</summary>
        private string Commit(string accumulator)
        {
            return _commitGeneric == null
                ? accumulator
                : "new "
                    + _commitGeneric
                    + "<"
                    + _keyQualified
                    + ", "
                    + _valueQualified
                    + ">("
                    + accumulator
                    + ")";
        }
    }
}
