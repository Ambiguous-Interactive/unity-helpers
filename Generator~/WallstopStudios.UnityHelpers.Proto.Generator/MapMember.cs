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

        private readonly Shape _key;
        private readonly Shape _value;
        private readonly string _keyQualified;
        private readonly string _valueQualified;
        private readonly string _mapQualified;
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
            _overwrite = overwrite;
            _mapIsValueType = mapIsValueType;
            _keyIsString = keyIsString;
            _valueIsString = valueIsString;
        }

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
            SurrogateMap surrogates
        )
        {
            if (!(type is INamedTypeSymbol named))
            {
                return null;
            }

            if (named.TypeKind != TypeKind.Class && named.TypeKind != TypeKind.Struct)
            {
                return null;
            }

            if (named.IsAbstract)
            {
                return null;
            }

            ITypeSymbol keyType = null;
            ITypeSymbol valueType = null;
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

            string keyQualified = keyType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string valueQualified = valueType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );

            Shape key = Shape.For(keyType, keyQualified, surrogates);
            Shape value = Shape.For(valueType, valueQualified, surrogates);
            if (key == null || value == null)
            {
                return null;
            }

            // Only REFERENCE keys other than string are refused, and that is narrower than the
            // protobuf spec on purpose. The spec restricts map keys to the integral types, bool and
            // string; protobuf-net does not, and this package's contract is byte-compatibility with
            // protobuf-net. Measured against 3.2.56: float, double and enum keys all encode without
            // complaint, so refusing them would break parity rather than protect anyone -- see
            // ExoticKeyContract, which pins those bytes.
            //
            // What stays refused is a `byte[]` or a message key: expressible in C#, and with no
            // stable identity to key on once it has been round-tripped.
            bool keyIsString = keyType.SpecialType == SpecialType.System_String;
            if (key.IsReference && !keyIsString)
            {
                return null;
            }

            return new MapMember(
                contractName,
                name,
                tag,
                key,
                value,
                keyQualified,
                valueQualified,
                named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                overwriteList,
                named.IsValueType,
                keyIsString,
                valueType.SpecialType == SpecialType.System_String
            );
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

            // The payload is written first and its length back-filled, rather than measured up front
            // as the Measure pass does. A map VALUE may be any contract, hooks included -- the earlier
            // version justified a second measurement on the grounds that "an entry is two scalars",
            // which a Dictionary<int, SomeContract> is not -- and measuring a contract runs its
            // before-serialization hook. Sizing the entry here ran that hook once per pass instead of
            // once per serialize, which is exactly what WProtoWriter.TryWriteMessage exists to stop.
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

            EmitHalfWrite(writer, _key, KeyAccess, 1);
            EmitHalfWrite(writer, _value, ValueAccess, 2);

            writer.Line("if (!writer.TryCloseLengthDelimited(" + token + "))" + Writer.Open);
            writer.Indent();
            writer.Line("return false;");
            Close(writer);

            CloseAll(writer, open);
            writer.Blank();
        }

        private void EmitHalfWrite(Writer writer, Shape shape, string access, int tag)
        {
            writer.Line("if (" + Shape.Fill(shape.PresenceTest, access) + ")" + Writer.Open);
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
        /// Emits the entry's payload size, which both halves obey the ordinary omission rules for.
        /// </summary>
        private void EmitEntrySize(Writer writer)
        {
            writer.Line("int " + EntrySize + " = 0;");
            EmitHalfSize(writer, _key, KeyAccess, 1);
            EmitHalfSize(writer, _value, ValueAccess, 2);
            writer.Blank();
        }

        private void EmitHalfSize(Writer writer, Shape shape, string access, int tag)
        {
            writer.Line("if (" + Shape.Fill(shape.PresenceTest, access) + ")" + Writer.Open);
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
                        DeclaredType + " " + ReadLocal + " = default(" + DeclaredType + ");"
                    );
                }

                return;
            }

            writer.Line(_mapQualified + " " + Accumulator + " = default(" + _mapQualified + ");");
        }

        /// <inheritdoc />
        internal override void EmitReadCases(Writer writer, string qualifiedContract)
        {
            string entry = "entry" + Tag;
            string keyLocal = "key" + Tag;
            string valueLocal = "value" + Tag;
            string decodedKey = "decodedKey" + Tag;
            string decodedValue = "decodedValue" + Tag;

            OpenCase(writer, Proto + ".WProtoWireType.LengthDelimited");
            EmitSeed(writer);

            // A real sub-message, so it spends a nesting level -- unlike a packed run, an entry can
            // contain another message when the value type is a contract.
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

            // Last-wins through the indexer. `Add` would throw on a key a hostile payload repeated,
            // and a throwing reader is a worse failure than a decided one.
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
            else if (_overwrite || SkipConstructor)
            {
                writer.Line(Accumulator + " = new " + _mapQualified + "();");
            }
            else
            {
                writer.Line(Accumulator + " = " + ExistingOrFresh() + ";");
            }

            Close(writer);
            writer.Blank();
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
            return _mapIsValueType
                ? "read." + Name
                : "read." + Name + " ?? new " + _mapQualified + "()";
        }

        /// <inheritdoc />
        internal override void EmitReadEpilogue(Writer writer)
        {
            writer.Line("if (" + SeenFlag + ")" + Writer.Open);
            writer.Indent();

            if (Deferred)
            {
                string target = "target" + Tag;
                writer.Line(
                    _mapQualified
                        + " "
                        + target
                        + " = "
                        + (
                            _overwrite || SkipConstructor
                                ? "new " + _mapQualified + "()"
                                : ExistingOrFresh()
                        )
                        + ";"
                );
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
                writer.Line((ConstructAtEnd ? ReadLocal : "read." + Name) + " = " + target + ";");
            }
            else
            {
                writer.Line(
                    (ConstructAtEnd ? ReadLocal : "read." + Name) + " = " + Accumulator + ";"
                );
            }

            Close(writer);
            writer.Blank();
        }
    }
}
