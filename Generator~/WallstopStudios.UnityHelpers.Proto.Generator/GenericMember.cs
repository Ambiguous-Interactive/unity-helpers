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

        private GenericMember(string name, int tag, string parameter)
            : base(name, tag)
        {
            _parameter = parameter;
        }

        internal static GenericMember TryCreate(string name, int tag, ITypeSymbol type)
        {
            return type is ITypeParameterSymbol parameter
                ? new GenericMember(name, tag, parameter.Name)
                : null;
        }

        private string Generic => Proto + ".WProtoGeneric<" + _parameter + ">";

        private string Local => "value" + Tag;

        private string SeenFlag => "seen" + Tag;

        /// <inheritdoc />
        internal override void EmitMeasure(Writer writer)
        {
            writer.Line("size += " + Generic + ".MeasureField(" + Tag + ", " + Access + ");");
            writer.Blank();
        }

        /// <inheritdoc />
        internal override void EmitWrite(Writer writer)
        {
            writer.Line(
                "if (!"
                    + Generic
                    + ".WriteField(ref writer, "
                    + Tag
                    + ", "
                    + Access
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line("return false;");
            Close(writer);
            writer.Blank();
        }

        /// <inheritdoc />
        internal override void EmitReadLocals(Writer writer)
        {
            if (!Deferred)
            {
                return;
            }

            writer.Line("bool " + SeenFlag + " = false;");
            writer.Line(_parameter + " " + Local + " = default(" + _parameter + ");");
        }

        /// <inheritdoc />
        internal override void EmitReadEpilogue(Writer writer)
        {
            if (!Deferred)
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
    }
}
