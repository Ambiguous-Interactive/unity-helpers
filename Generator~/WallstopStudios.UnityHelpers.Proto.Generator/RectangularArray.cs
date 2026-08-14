// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System.Text;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// One generated wrapper message for a rectangular array: a dimension header and the flat run of
    /// elements that fills it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A jagged array carries its own structure -- each row is a separate wrapper whose run length is
    /// on the wire -- so <c>int[][]</c> needed nothing but a wrapper per inner collection. A
    /// rectangular array does not: <c>int[2,3]</c> and <c>int[3,2]</c> deliver the same six values in
    /// the same order, and nothing in a repeated field says where a row ends. The shape therefore has
    /// to travel with the elements, which is what makes this a different question from #399 rather
    /// than the same one unfinished.
    /// </para>
    /// <para>
    /// The message is <c>message Rect { repeated int32 dims = 1; repeated T values = 2; }</c> -- a
    /// concrete proto3 definition, which is the owner's condition on diverging from an oracle that
    /// refuses the shape outright. <c>dims</c> holds one length per dimension in declaration order
    /// and <c>values</c> holds the elements in row-major order, which is the order C# gives
    /// <c>foreach</c> over an array of any rank.
    /// </para>
    /// <para>
    /// <b>The rank is compile-time.</b> It comes from the member's declared type, so the fill loops
    /// and the <c>new T[a, b]</c> that precedes them are emitted directly and this never needs
    /// <c>Array.CreateInstance</c>, a runtime rank, or a boxed <c>System.Array</c>. The dimension
    /// header is checked against that rank rather than trusted to define it.
    /// </para>
    /// </remarks>
    internal sealed class RectangularArray : IGeneratedMessage
    {
        /// <summary>The field number carrying the dimension header.</summary>
        private const int DimensionsTag = 1;

        /// <summary>The field number carrying the flat element run.</summary>
        internal const int ValuesTag = 2;

        /// <summary>The local holding how many dimensions the payload stated.</summary>
        private const string DimensionCount = "dimensionCount";

        /// <summary>The local holding one decoded dimension before it is filed.</summary>
        private const string DecodedDimension = "dimension";

        private readonly int _rank;
        private readonly string _elementQualified;
        private readonly string _creationPrefix;
        private readonly string _creationSuffix;
        private readonly string _contractName;

        internal RectangularArray(
            string formatterName,
            string qualified,
            IArrayTypeSymbol array,
            string contractName
        )
        {
            FormatterName = formatterName;
            Qualified = qualified;
            Display = TypeNaming.Display(array);
            _rank = array.Rank;
            _elementQualified = array.ElementType.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );
            _contractName = contractName;

            // `new T[a, b]` is only the syntax when T is not itself an array. C# puts every rank
            // specifier of the whole type on one creation expression, outermost first, so a
            // two-dimensional array of `int[]` is `new int[a, b][]` -- naming `int[]` as the element
            // and appending the lengths produces `new int[][a, b]`, which does not parse.
            ITypeSymbol baseElement = array.ElementType;
            StringBuilder suffix = new StringBuilder();
            while (baseElement is IArrayTypeSymbol inner)
            {
                suffix.Append('[').Append(new string(',', inner.Rank - 1)).Append(']');
                baseElement = inner.ElementType;
            }

            _creationPrefix =
                "new " + baseElement.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            _creationSuffix = suffix.ToString();
        }

        /// <inheritdoc />
        public string FormatterName { get; }

        /// <inheritdoc />
        public string Qualified { get; }

        /// <inheritdoc />
        public string Display { get; }

        /// <inheritdoc />
        public int Depth { get; set; }

        /// <inheritdoc />
        public Member Inner { get; set; }

        /// <inheritdoc />
        public string Instance => FormatterName + ".Instance";

        /// <inheritdoc />
        public void Emit(Writer writer)
        {
            writer.Line(
                "/// <summary>Generated WallstopProto wrapper message for the rectangular array <c>",
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

        private static string Dimension(int axis)
        {
            return "dimension" + axis;
        }

        /// <summary>
        /// The fill loop's index for one axis.
        /// </summary>
        /// <remarks>
        /// Named for the axis rather than <c>index</c>, because the element run's own emitter names
        /// its locals <c>index{tag}</c> -- and the run's tag is 2, so a rank-three array would
        /// otherwise be one emitter change away from declaring <c>index2</c> twice in one method.
        /// </remarks>
        private static string Axis(int axis)
        {
            return "axis" + axis;
        }

        /// <summary>
        /// Emits the dimension header's contribution to <c>Measure</c>, then the element run's.
        /// </summary>
        /// <remarks>
        /// The header is a fixed-length run -- one <c>int32</c> per dimension, and the rank is at
        /// least two -- so it is never empty and needs none of the omit-when-empty reasoning a
        /// collection's run carries. It is written even for <c>new int[0, 5]</c>, which is the whole
        /// point: that array has a real shape and no elements, and dropping the header would restore
        /// it as <c>int[0, 0]</c>.
        /// </remarks>
        private void EmitMeasure(Writer writer)
        {
            writer.Line("/// <inheritdoc />");
            writer.Line("public int Measure(in " + Qualified + " value)" + Writer.Open);
            writer.Indent();
            writer.Line("int size = 0;");
            writer.Blank();

            // A null never arrives here from generated code -- a member of this type is omitted when
            // null and an element of it is refused -- but Measure and Write are public, so the two
            // agree on writing nothing rather than dereferencing it.
            writer.Line("if (value != null)" + Writer.Open);
            writer.Indent();
            writer.Line("int headerSize = 0;");
            EmitRankLoop(writer);
            writer.Line(
                "headerSize += "
                    + NestedCollections.Proto
                    + ".WProtoSizes.Int32Size(value.GetLength(rank));"
            );
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
            writer.Line(
                "size += "
                    + NestedCollections.Proto
                    + ".WProtoSizes.TagSize("
                    + DimensionsTag
                    + ") + "
                    + NestedCollections.Proto
                    + ".WProtoSizes.LengthDelimitedSize(headerSize);"
            );
            writer.Outdent();
            writer.Line("}");
            writer.Blank();

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

            writer.Line("if (value != null)" + Writer.Open);
            writer.Indent();
            writer.Line(
                "if (!writer.TryBeginLengthDelimited("
                    + DimensionsTag
                    + ", false, out "
                    + NestedCollections.Proto
                    + ".WProtoLengthToken headerToken))"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line("return false;");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();

            EmitRankLoop(writer);

            // Refused here rather than on read because only a writer can produce one: nothing on the
            // wire can express a lower bound, and rebuilding with `new T[...]` starts every axis at
            // zero. Folded into the loop that already asks each axis for its length.
            writer.Line("if (value.GetLowerBound(rank) != 0)" + Writer.Open);
            writer.Indent();
            writer.Line(
                "throw "
                    + NestedCollections.Proto
                    + ".WProtoRectangular.NonZeroLowerBound(\""
                    + _contractName
                    + "\", \""
                    + Display
                    + "\", rank, value.GetLowerBound(rank));"
            );
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
            writer.Line("if (!writer.TryWriteInt32(value.GetLength(rank)))" + Writer.Open);
            writer.Indent();
            writer.Line("return false;");
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();

            writer.Line("if (!writer.TryCloseLengthDelimited(headerToken))" + Writer.Open);
            writer.Indent();
            writer.Line("return false;");
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();

            Inner.EmitWrite(writer);
            writer.Line("return true;");
            writer.Outdent();
            writer.Line("}");
        }

        /// <summary>Emits the loop header that visits each axis of the declared rank.</summary>
        private void EmitRankLoop(Writer writer)
        {
            writer.Line("for (int rank = 0; rank < " + _rank + "; rank++)" + Writer.Open);
            writer.Indent();
        }

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

            writer.Line(_elementQualified, "[] ", Inner.ReadLocal, " = null;");
            Inner.EmitReadLocals(writer);
            writer.Line("int " + DimensionCount + " = 0;");
            for (int axis = 0; axis < _rank; axis++)
            {
                writer.Line("int " + Dimension(axis) + " = 0;");
            }

            writer.Blank();

            writer.Line(
                "while (reader.TryReadTag(out int fieldNumber, out int wireType))" + Writer.Open
            );
            writer.Indent();
            writer.Line("switch (fieldNumber)" + Writer.Open);
            writer.Indent();

            EmitHeaderCases(writer);
            Inner.EmitReadCases(writer, Qualified);

            writer.Line("default:" + Writer.Open);
            writer.Indent();
            writer.Line("if (!reader.TrySkipField(fieldNumber, wireType))" + Writer.Open);
            writer.Indent();
            EmitFailure(writer);
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
            EmitFailure(writer);
            writer.Outdent();
            writer.Line("}");
            writer.Blank();

            // The same thing a nested collection's wrapper knows: this message was read, so its array
            // exists even when no element run followed. That is what lets `new int[0, 5]` round trip.
            writer.Line(
                "// This wrapper message was read, so its array exists even with no elements in it"
            );
            writer.Line("// -- which is what makes a zero-length dimension round trip.");
            Inner.EmitPresentSeed(writer);
            Inner.EmitReadEpilogue(writer);

            EmitShapeCheck(writer);
            EmitFill(writer);

            writer.Line("return true;");
            writer.Outdent();
            writer.Line("}");
        }

        /// <summary>
        /// Emits both spellings of the dimension header: the packed run this generator writes, and
        /// the one-key-per-dimension form <c>repeated int32</c> permits.
        /// </summary>
        /// <remarks>
        /// The unpacked case is not speculative generosity. <c>repeated int32 dims = 1</c> is an
        /// ordinary proto3 field, so anything generated from this message's schema by another toolkit
        /// may write it either way, and leniency on read cannot lose data. It is the same rule the
        /// element run already follows in both directions.
        /// </remarks>
        private void EmitHeaderCases(Writer writer)
        {
            writer.Line(
                "case "
                    + DimensionsTag
                    + " when wireType == "
                    + NestedCollections.Proto
                    + ".WProtoWireType.Varint:"
                    + Writer.Open
            );
            writer.Indent();
            EmitDimensionCapture(writer, "reader");
            writer.Line("break;");
            writer.Outdent();
            writer.Line("}");

            writer.Line(
                "case "
                    + DimensionsTag
                    + " when wireType == "
                    + NestedCollections.Proto
                    + ".WProtoWireType.LengthDelimited:"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line(
                "if (!reader.TryReadPackedRun(out "
                    + NestedCollections.Proto
                    + ".WProtoReader header))"
                    + Writer.Open
            );
            writer.Indent();
            EmitFailure(writer);
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
            writer.Line("while (!header.End)" + Writer.Open);
            writer.Indent();
            EmitDimensionCapture(writer, "header");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
            writer.Line("break;");
            writer.Outdent();
            writer.Line("}");
        }

        /// <summary>
        /// Emits the read of one dimension and the slot it lands in.
        /// </summary>
        /// <param name="writer">The destination.</param>
        /// <param name="source">The reader the dimension is read from.</param>
        /// <remarks>
        /// <para>
        /// A <c>switch</c> over rank-many locals rather than an <c>int[]</c>, so a read allocates
        /// nothing for a header whose size is a compile-time constant.
        /// </para>
        /// <para>
        /// The counter saturates one past the rank instead of climbing freely. A payload can repeat
        /// this field as many times as it has bytes for, and a counter that wrapped would eventually
        /// present an over-long header as an exactly-right one.
        /// </para>
        /// </remarks>
        private void EmitDimensionCapture(Writer writer, string source)
        {
            writer.Line(
                "if (!" + source + ".TryReadInt32(out int " + DecodedDimension + "))" + Writer.Open
            );
            writer.Indent();
            EmitFailure(writer);
            writer.Outdent();
            writer.Line("}");
            writer.Blank();

            // Refused where it is read rather than folded into the product below: two negative
            // dimensions multiply to a positive one, which could match the delivered count exactly
            // and would then fail inside `new T[-2, -3]`.
            writer.Line("if (" + DecodedDimension + " < 0)" + Writer.Open);
            writer.Indent();
            EmitFailure(writer);
            writer.Outdent();
            writer.Line("}");
            writer.Blank();

            writer.Line("switch (" + DimensionCount + ")" + Writer.Open);
            writer.Indent();
            for (int axis = 0; axis < _rank; axis++)
            {
                writer.Line("case " + axis + ":" + Writer.Open);
                writer.Indent();
                writer.Line(Dimension(axis) + " = " + DecodedDimension + ";");
                writer.Line("break;");
                writer.Outdent();
                writer.Line("}");
            }

            writer.Line("default:" + Writer.Open);
            writer.Indent();
            writer.Line("break;");
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();

            writer.Line("if (" + DimensionCount + " <= " + _rank + ")" + Writer.Open);
            writer.Indent();
            writer.Line(DimensionCount + "++;");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
        }

        /// <summary>
        /// Emits the one check that stands between a payload's stated shape and an allocation.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The product is required to equal what the run actually delivered. See
        /// <c>WProtoRectangular.TryAcceptShape</c> for why that is the whole defense.
        /// </para>
        /// <para>
        /// It is accumulated through <c>MultiplyDimensions</c> rather than multiplied inline,
        /// because <c>long</c> is not wide enough to hold three <c>int</c> dimensions and C#
        /// arithmetic wraps silently -- and a wrapped product is one an attacker picks.
        /// </para>
        /// </remarks>
        private void EmitShapeCheck(Writer writer)
        {
            string product = "(long)" + Dimension(0);
            for (int axis = 1; axis < _rank; axis++)
            {
                product =
                    NestedCollections.Proto
                    + ".WProtoRectangular.MultiplyDimensions("
                    + product
                    + ", "
                    + Dimension(axis)
                    + ")";
            }

            writer.Line("long declaredElements = " + product + ";");
            writer.Line(
                "if (!"
                    + NestedCollections.Proto
                    + ".WProtoRectangular.TryAcceptShape("
                    + _rank
                    + ", "
                    + DimensionCount
                    + ", declaredElements, "
                    + Inner.ReadLocal
                    + ".Length))"
                    + Writer.Open
            );
            writer.Indent();
            EmitFailure(writer);
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
        }

        /// <summary>Emits the allocation and the row-major fill, one loop per axis.</summary>
        private void EmitFill(Writer writer)
        {
            string lengths = Dimension(0);
            string indices = Axis(0);
            for (int axis = 1; axis < _rank; axis++)
            {
                lengths += ", " + Dimension(axis);
                indices += ", " + Axis(axis);
            }

            writer.Line("value = " + _creationPrefix + "[" + lengths + "]" + _creationSuffix + ";");
            writer.Line("int flat = 0;");
            for (int axis = 0; axis < _rank; axis++)
            {
                writer.Line(
                    "for (int "
                        + Axis(axis)
                        + " = 0; "
                        + Axis(axis)
                        + " < "
                        + Dimension(axis)
                        + "; "
                        + Axis(axis)
                        + "++)"
                        + Writer.Open
                );
                writer.Indent();
            }

            writer.Line("value[" + indices + "] = " + Inner.ReadLocal + "[flat];");
            writer.Line("flat++;");
            for (int axis = 0; axis < _rank; axis++)
            {
                writer.Outdent();
                writer.Line("}");
            }

            writer.Blank();
        }

        private void EmitFailure(Writer writer)
        {
            writer.Line("value = default(" + Qualified + ");");
            writer.Line("return false;");
        }
    }
}
