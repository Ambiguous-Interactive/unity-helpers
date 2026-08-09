// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// One <c>[WProtoMember]</c>, reduced to the code fragments that encode and decode it.
    /// </summary>
    /// <remarks>
    /// The encoding rules here are protobuf-net's at CompatibilityLevel 200, not proto3's, because
    /// wire compatibility with data already on disk is the whole point. Two of them are
    /// counter-intuitive and were measured against the oracle rather than reasoned about: a member
    /// equal to its type's default is omitted -- which silently turns <c>-0.0</c> into <c>+0.0</c>,
    /// since <c>-0.0 == 0.0</c> -- while an empty-but-non-null <c>string</c> or <c>byte[]</c> is
    /// written as a tag and a zero length. Only null is absent.
    /// </remarks>
    internal sealed class Member
    {
        private const string Proto =
            "global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto";

        private readonly string _name;
        private readonly string _valueExpression;
        private readonly string _sizeExpression;
        private readonly string _writeMethod;
        private readonly string _readMethod;
        private readonly string _readLocalType;
        private readonly string _assignExpression;
        private readonly string _writeCast;

        private Member(
            string name,
            int tag,
            string wireType,
            string presenceTest,
            string valueExpression,
            string sizeExpression,
            string writeMethod,
            string readMethod,
            string readLocalType,
            string assignExpression,
            string writeCast
        )
        {
            _name = name;
            Tag = tag;
            WireType = wireType;
            PresenceTest = presenceTest;
            _valueExpression = valueExpression;
            _sizeExpression = sizeExpression;
            _writeMethod = writeMethod;
            _readMethod = readMethod;
            _readLocalType = readLocalType;
            _assignExpression = assignExpression;
            _writeCast = writeCast;
        }

        internal int Tag { get; }

        internal string WireType { get; }

        /// <summary>The condition under which this member appears on the wire at all.</summary>
        internal string PresenceTest { get; }

        internal string SizeExpression => _sizeExpression;

        internal string WriteCall => _writeMethod + "(" + _writeCast + _valueExpression + ")";

        internal IEnumerable<string> ReadStatements(string target, string qualifiedContract)
        {
            string local = "decoded" + Tag;
            yield return "if (!reader."
                + _readMethod
                + "(out "
                + _readLocalType
                + " "
                + local
                + "))"
                + Writer.Open;
            yield return "    value = default(" + qualifiedContract + ");";
            yield return "    return false;";
            yield return "}";
            yield return string.Empty;
            yield return target + "." + _name + " = " + _assignExpression.Replace("$", local) + ";";
        }

        /// <summary>
        /// Builds the fragments for <paramref name="type"/>, or <c>null</c> when it is not supported.
        /// </summary>
        internal static Member Create(string name, int tag, ITypeSymbol type, bool isRequired)
        {
            string access = "value." + name;
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

            string valueExpression = nullable ? access + ".Value" : access;
            string qualifiedUnderlying = underlying.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat
            );

            Shape shape = ShapeFor(underlying, valueExpression, qualifiedUnderlying);
            if (shape == null)
            {
                return null;
            }

            string presence;
            if (isRequired)
            {
                presence = nullable ? access + ".HasValue" : "true";
            }
            else if (nullable)
            {
                presence = access + ".HasValue";
            }
            else
            {
                presence = shape.PresenceTest;
            }

            string assign = nullable
                ? "(" + qualifiedUnderlying + "?)(" + shape.AssignExpression + ")"
                : shape.AssignExpression;

            return new Member(
                name,
                tag,
                shape.WireType,
                presence,
                valueExpression,
                shape.SizeExpression,
                shape.WriteMethod,
                shape.ReadMethod,
                shape.ReadLocalType,
                assign,
                shape.WriteCast
            );
        }

        private static Shape ShapeFor(ITypeSymbol type, string valueExpression, string qualified)
        {
            if (type.TypeKind == TypeKind.Enum && type is INamedTypeSymbol enumType)
            {
                bool wide =
                    enumType.EnumUnderlyingType != null
                    && (
                        enumType.EnumUnderlyingType.SpecialType == SpecialType.System_Int64
                        || enumType.EnumUnderlyingType.SpecialType == SpecialType.System_UInt64
                    );
                return new Shape
                {
                    WireType = Proto + ".WProtoWireType.Varint",
                    PresenceTest = valueExpression + " != default(" + qualified + ")",
                    SizeExpression = wide
                        ? Proto + ".WProtoSizes.Int64Size((long)" + valueExpression + ")"
                        : Proto + ".WProtoSizes.Int32Size((int)" + valueExpression + ")",
                    WriteMethod = wide ? "TryWriteInt64" : "TryWriteInt32",
                    ReadMethod = wide ? "TryReadInt64" : "TryReadInt32",
                    ReadLocalType = wide ? "long" : "int",
                    AssignExpression = "(" + qualified + ")$",
                    WriteCast = wide ? "(long)" : "(int)",
                };
            }

            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                {
                    return new Shape
                    {
                        WireType = Proto + ".WProtoWireType.Varint",
                        PresenceTest = valueExpression,
                        SizeExpression = "1",
                        WriteMethod = "TryWriteBool",
                        ReadMethod = "TryReadBool",
                        ReadLocalType = "bool",
                        AssignExpression = "$",
                    };
                }
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                {
                    return Integer(
                        valueExpression,
                        qualified,
                        type.SpecialType == SpecialType.System_Int32
                    );
                }
                case SpecialType.System_Byte:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                {
                    return Unsigned32(
                        valueExpression,
                        qualified,
                        type.SpecialType == SpecialType.System_UInt32
                    );
                }
                case SpecialType.System_Int64:
                {
                    return new Shape
                    {
                        WireType = Proto + ".WProtoWireType.Varint",
                        PresenceTest = valueExpression + " != 0",
                        SizeExpression = Proto + ".WProtoSizes.Int64Size(" + valueExpression + ")",
                        WriteMethod = "TryWriteInt64",
                        ReadMethod = "TryReadInt64",
                        ReadLocalType = "long",
                        AssignExpression = "$",
                    };
                }
                case SpecialType.System_UInt64:
                {
                    return new Shape
                    {
                        WireType = Proto + ".WProtoWireType.Varint",
                        PresenceTest = valueExpression + " != 0",
                        SizeExpression =
                            Proto + ".WProtoSizes.Varint64Size(" + valueExpression + ")",
                        WriteMethod = "TryWriteVarint64",
                        ReadMethod = "TryReadVarint64",
                        ReadLocalType = "ulong",
                        AssignExpression = "$",
                    };
                }
                case SpecialType.System_Single:
                {
                    return new Shape
                    {
                        WireType = Proto + ".WProtoWireType.Fixed32",
                        PresenceTest = valueExpression + " != 0f",
                        SizeExpression = "4",
                        WriteMethod = "TryWriteSingle",
                        ReadMethod = "TryReadSingle",
                        ReadLocalType = "float",
                        AssignExpression = "$",
                    };
                }
                case SpecialType.System_Double:
                {
                    return new Shape
                    {
                        WireType = Proto + ".WProtoWireType.Fixed64",
                        PresenceTest = valueExpression + " != 0d",
                        SizeExpression = "8",
                        WriteMethod = "TryWriteDouble",
                        ReadMethod = "TryReadDouble",
                        ReadLocalType = "double",
                        AssignExpression = "$",
                    };
                }
                case SpecialType.System_String:
                {
                    return new Shape
                    {
                        WireType = Proto + ".WProtoWireType.LengthDelimited",
                        PresenceTest = valueExpression + " != null",
                        SizeExpression = Proto + ".WProtoSizes.StringSize(" + valueExpression + ")",
                        WriteMethod = "TryWriteString",
                        ReadMethod = "TryReadString",
                        ReadLocalType = "string",
                        AssignExpression = "$",
                    };
                }
                default:
                {
                    break;
                }
            }

            if (
                type is IArrayTypeSymbol array
                && array.Rank == 1
                && array.ElementType.SpecialType == SpecialType.System_Byte
            )
            {
                return new Shape
                {
                    WireType = Proto + ".WProtoWireType.LengthDelimited",
                    PresenceTest = valueExpression + " != null",
                    SizeExpression =
                        Proto + ".WProtoSizes.LengthDelimitedSize(" + valueExpression + ".Length)",
                    WriteMethod = "TryWriteBytes",
                    ReadMethod = "TryReadBytes",
                    ReadLocalType = "global::System.ReadOnlySpan<byte>",
                    AssignExpression = "$.ToArray()",
                };
            }

            return null;
        }

        private static Shape Integer(string valueExpression, string qualified, bool exact)
        {
            string widened = exact ? valueExpression : "(int)" + valueExpression;
            return new Shape
            {
                WireType = Proto + ".WProtoWireType.Varint",
                PresenceTest = valueExpression + " != 0",
                SizeExpression = Proto + ".WProtoSizes.Int32Size(" + widened + ")",
                WriteMethod = "TryWriteInt32",
                ReadMethod = "TryReadInt32",
                ReadLocalType = "int",
                AssignExpression = exact ? "$" : "(" + qualified + ")$",
                WriteCast = exact ? string.Empty : "(int)",
            };
        }

        private static Shape Unsigned32(string valueExpression, string qualified, bool exact)
        {
            string widened = exact ? valueExpression : "(uint)" + valueExpression;
            return new Shape
            {
                WireType = Proto + ".WProtoWireType.Varint",
                PresenceTest = valueExpression + " != 0",
                SizeExpression = Proto + ".WProtoSizes.Varint32Size(" + widened + ")",
                WriteMethod = "TryWriteVarint32",
                ReadMethod = "TryReadVarint32",
                ReadLocalType = "uint",
                AssignExpression = exact ? "$" : "(" + qualified + ")$",
                WriteCast = exact ? string.Empty : "(uint)",
            };
        }

        private sealed class Shape
        {
            internal string WireType;
            internal string PresenceTest;
            internal string SizeExpression;
            internal string WriteMethod;
            internal string ReadMethod;
            internal string ReadLocalType;
            internal string AssignExpression;
            internal string WriteCast = string.Empty;
        }
    }
}
