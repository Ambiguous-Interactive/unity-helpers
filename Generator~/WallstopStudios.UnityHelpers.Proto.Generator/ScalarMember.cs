// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// A member that occupies its field number at most once: a primitive, a string, a
    /// <c>byte[]</c>, an enum, a nested contract, or a <see cref="System.Nullable{T}"/> of any of
    /// those.
    /// </summary>
    internal sealed class ScalarMember : Member
    {
        private readonly Shape _shape;
        private readonly string _presence;
        private readonly string _value;
        private readonly string _assign;
        private readonly string _declared;

        private ScalarMember(
            string name,
            int tag,
            Shape shape,
            string presence,
            string value,
            string assign,
            string declared
        )
            : base(name, tag)
        {
            _shape = shape;
            _presence = presence;
            _value = value;
            _assign = assign;
            _declared = declared;
        }

        private string Local => "value" + Tag;

        private string SeenFlag => "seen" + Tag;

        internal static ScalarMember TryCreate(
            string name,
            int tag,
            ITypeSymbol type,
            bool isRequired
        )
        {
            ITypeSymbol underlying = type;
            bool nullable = false;

            if (
                type is INamedTypeSymbol named
                && named.IsGenericType
                && named.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T
            )
            {
                nullable = true;
                underlying = named.TypeArguments[0];
            }

            string access = "value." + name;
            string value = nullable ? access + ".Value" : access;
            string qualifiedUnderlying = underlying.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );

            Shape shape = Shape.For(underlying, qualifiedUnderlying);
            if (shape == null)
            {
                return null;
            }

            string presence;
            if (isRequired)
            {
                // IsRequired forces a VALUE onto the wire even when it equals its default; it does
                // not invent one. Measured against protobuf-net 3.2.56: a required int at 0 and a
                // required struct sub-message at default are both written, while a required null
                // string, byte[] or message reference is still absent. Treating "required" as
                // "always present" writes an empty string where protobuf-net wrote nothing -- and,
                // for a message, hands Measure a null to dereference.
                presence =
                    nullable ? access + ".HasValue"
                    : shape.IsReference ? Shape.Fill(shape.PresenceTest, value)
                    : "true";
            }
            else if (nullable)
            {
                presence = access + ".HasValue";
            }
            else
            {
                presence = Shape.Fill(shape.PresenceTest, value);
            }

            string assign = nullable
                ? "(" + qualifiedUnderlying + "?)(" + shape.AssignExpression + ")"
                : shape.AssignExpression;

            return new ScalarMember(
                name,
                tag,
                shape,
                presence,
                value,
                assign,
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            );
        }

        /// <inheritdoc />
        internal override void EmitMeasure(Writer writer)
        {
            writer.Line("if (" + _presence + ")" + Writer.Open);
            writer.Indent();
            writer.Line(
                "size += "
                    + Proto
                    + ".WProtoSizes.TagSize("
                    + Tag
                    + ") + "
                    + Shape.Fill(_shape.SizeExpression, _value)
                    + ";"
            );
            Close(writer);
            writer.Blank();
        }

        /// <inheritdoc />
        internal override void EmitWrite(Writer writer)
        {
            writer.Line("if (" + _presence + ")" + Writer.Open);
            writer.Indent();
            writer.Line("if (!(" + _shape.WriteCall(_value, Tag) + "))" + Writer.Open);
            writer.Indent();
            writer.Line("return false;");
            Close(writer);
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
            writer.Line(_declared + " " + Local + " = default(" + _declared + ");");
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
            string local = "decoded" + Tag;
            OpenCase(writer, _shape.WireType);
            writer.Line(
                "if (!reader."
                    + _shape.ReadMethod
                    + "("
                    + _shape.ReadArguments
                    + "out "
                    + _shape.ReadLocalType
                    + " "
                    + local
                    + "))"
                    + Writer.Open
            );
            writer.Indent();
            EmitReadFailure(writer, qualifiedContract);
            Close(writer);
            writer.Blank();
            if (Deferred)
            {
                // The instance this lands on is not known yet: an include tag later in the payload
                // can replace it with a subtype. Assigning now would write onto an object that is
                // about to be thrown away, and protobuf-net permits the include in either position.
                writer.Line(Local + " = " + Shape.Fill(_assign, local) + ";");
                writer.Line(SeenFlag + " = true;");
            }
            else
            {
                writer.Line("read." + Name + " = " + Shape.Fill(_assign, local) + ";");
            }

            writer.Line("break;");
            Close(writer);
        }
    }
}
