// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// A member whose type is one of the contract's own type parameters.
    /// </summary>
    /// <remarks>
    /// Nothing about its encoding is knowable at generate time. Measured against protobuf-net,
    /// <c>Box&lt;int&gt;.Value</c> is <c>08 01</c>, <c>Box&lt;double&gt;</c> is <c>09 …</c> and
    /// <c>Box&lt;string&gt;</c> is <c>0A …</c> -- the field key itself changes with the closure -- so
    /// emitting a wire-type constant here would be wrong for every closure but one. The whole
    /// per-field decision is deferred to <c>WProtoGeneric&lt;T&gt;</c>, a closed generic IL2CPP
    /// compiles ahead of time like any other.
    /// </remarks>
    internal sealed class GenericMember : Member
    {
        private readonly string _parameter;
        private readonly bool _required;

        private GenericMember(string name, int tag, string parameter, bool required)
            : base(name, tag)
        {
            _parameter = parameter;
            _required = required;
        }

        internal static GenericMember TryCreate(
            string name,
            int tag,
            ITypeSymbol type,
            bool isRequired
        )
        {
            return type is ITypeParameterSymbol parameter
                ? new GenericMember(name, tag, parameter.Name, isRequired)
                : null;
        }

        /// <summary>
        /// The literal passed to <c>WProtoGeneric&lt;T&gt;</c> for this member's IsRequired.
        /// </summary>
        /// <remarks>
        /// Passed as a runtime argument rather than resolved here, because what "required" does to a
        /// default depends on whether the closure is a value type -- a required int at 0 is written
        /// and a required null string is not -- and the closure is exactly what is unknown at this
        /// point.
        /// </remarks>
        private string Required => _required ? "true" : "false";

        private string Generic => Proto + ".WProtoGeneric<" + _parameter + ">";

        private string Local => ReadLocal;

        private string SeenFlag => "seen" + Tag;

        private string Occurrences => "occurrences" + Tag;

        /// <summary>Where a decoded value lands once the read loop has ended.</summary>
        /// <remarks>
        /// A contract built by a constructor has no instance to assign onto, so its value waits in a
        /// local. Everything else can be assigned directly here, because this runs after an include
        /// tag has settled which instance the member belongs to.
        /// </remarks>
        private string Destination => ConstructAtEnd ? Local : "read." + Name;

        /// <inheritdoc />
        internal override void EmitMeasure(Writer writer)
        {
            writer.Line(
                "size += ",
                Generic,
                ".MeasureField(",
                Tag.ToString(),
                ", ",
                Access,
                ", " + Required + ");"
            );
            writer.Blank();
        }

        /// <inheritdoc />
        internal override void EmitWrite(Writer writer)
        {
            writer.Line(
                "if (!",
                Generic,
                ".WriteField(ref writer, ",
                Tag.ToString(),
                ", ",
                Access,
                ", " + Required + "))" + Writer.Open
            );
            writer.Indent();
            writer.Line("return false;");
            Close(writer);
            writer.Blank();
        }

        /// <inheritdoc />
        internal override void EmitReadLocals(Writer writer)
        {
            // Unconditionally, unlike every other local here: whether this member is a sub-message
            // at all is a property of the closure, so a generator that emitted the accumulator only
            // when it could prove one was needed would never emit it.
            writer.Line(
                Proto,
                ".WProtoMessageAccumulator ",
                Occurrences,
                " = default(",
                Proto,
                ".WProtoMessageAccumulator);"
            );

            if (!Deferred)
            {
                return;
            }

            writer.Line("bool ", SeenFlag, " = false;");
            writer.Line(_parameter, " ", Local, " = default(", _parameter, ");");
        }

        /// <inheritdoc />
        internal override void EmitReadEpilogue(Writer writer, string qualifiedContract)
        {
            EmitMergeEpilogue(writer, qualifiedContract);

            if (!Deferred || ConstructAtEnd)
            {
                return;
            }

            writer.Line("if (" + SeenFlag + ")" + Writer.Open);
            writer.Indent();
            writer.Line("read." + Name + " = " + Local + ";");
            Close(writer);
            writer.Blank();
        }

        /// <inheritdoc />
        internal override void EmitReadCases(Writer writer, string qualifiedContract)
        {
            string decoded = "decoded" + Tag;

            // The wire type is a property of the closed type, so the case guard asks the closed type
            // rather than comparing against a constant the emitter could not have known.
            writer.Line("case " + Tag + " when " + Generic + ".Accepts(wireType):" + Writer.Open);
            writer.Indent();
            EmitMessageBranch(writer, qualifiedContract);
            writer.Line(
                "if (!"
                    + Generic
                    + ".TryReadValue(ref reader, out "
                    + _parameter
                    + " "
                    + decoded
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();
            writer.Line((Deferred ? Local : "read." + Name) + " = " + decoded + ";");
            if (Deferred)
            {
                writer.Line(SeenFlag + " = true;");
            }

            writer.Line("break;");
            Close(writer);
        }

        /// <summary>
        /// Emits the branch that keeps a message closure's bytes instead of decoding them.
        /// </summary>
        /// <remarks>
        /// protobuf merges repeated occurrences of a non-repeated sub-message and takes the last
        /// occurrence of every other shape, and which of the two a generic member is cannot be
        /// decided here -- <c>Box&lt;Child&gt;</c> merges while <c>Box&lt;string&gt;</c> does not,
        /// from one emitted body. So both paths are emitted and the closed type picks between them,
        /// on <c>IsMessage</c> rather than on the wire type the two share.
        /// </remarks>
        private void EmitMessageBranch(Writer writer, string qualifiedContract)
        {
            string chunk = "chunk" + Tag;

            writer.Line("if (" + Generic + ".IsMessage)" + Writer.Open);
            writer.Indent();
            writer.Line(
                "if (!reader.TryReadBytes(out global::System.ReadOnlySpan<byte> "
                    + chunk
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();
            writer.Line("if (!" + Occurrences + ".TryAdd(" + chunk + "))" + Writer.Open);
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();
            writer.Line("break;");
            Close(writer);
            writer.Blank();
        }

        /// <summary>
        /// Emits the single decode of every occurrence a message closure carried.
        /// </summary>
        /// <remarks>
        /// Once however many occurrences arrived, because the concatenation of two encodings IS
        /// their merge -- which is what keeps the decoded value's lifecycle hooks running exactly
        /// once rather than once per occurrence.
        /// </remarks>
        private void EmitMergeEpilogue(Writer writer, string qualifiedContract)
        {
            string merged = "merged" + Tag;

            writer.Line("if (" + Occurrences + ".HasValue)" + Writer.Open);
            writer.Indent();
            writer.Line(
                "if (!"
                    + Generic
                    + ".TryReadValue(ref reader, "
                    + Occurrences
                    + ".Payload, out "
                    + _parameter
                    + " "
                    + merged
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();
            writer.Line(Destination + " = " + merged + ";");
            Close(writer);
            writer.Blank();
        }
    }
}
