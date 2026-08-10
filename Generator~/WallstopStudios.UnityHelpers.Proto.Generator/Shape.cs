// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// How one value type is encoded, as code fragments with <c>$</c> standing in for the value.
    /// </summary>
    /// <remarks>
    /// The placeholder is what lets the same shape serve a member and an element of a repeated
    /// member: the first substitutes <c>value.Member</c>, the second a loop local. Baking the
    /// expression in -- which is what the first version did -- meant a repeated <c>int</c> could not
    /// reuse the rules that a plain <c>int</c> had already got right.
    /// </remarks>
    internal sealed class Shape
    {
        internal const string Placeholder = "$";

        private const string Proto =
            "global::WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto";

        /// <summary>The wire type constant a field of this shape carries in its key.</summary>
        internal string WireType;

        /// <summary>The condition under which a <b>member</b> of this shape appears at all.</summary>
        /// <remarks>
        /// Never consulted for an element of a repeated member: protobuf-net writes every element,
        /// including one equal to its type's default (measured -- <c>{0}</c> encodes as <c>08 00</c>,
        /// not as nothing).
        /// </remarks>
        internal string PresenceTest;

        /// <summary>The encoded size of the value, excluding the field key.</summary>
        internal string SizeExpression;

        internal string WriteMethod;
        internal string ReadMethod;
        internal string ReadLocalType;

        /// <summary>Converts the decoded local, <c>$</c>, back to the declared type.</summary>
        internal string AssignExpression;

        internal string WriteCast = string.Empty;
        internal string ReadArguments = string.Empty;

        /// <summary>
        /// Whether the write call emits its own field key, as a nested message must.
        /// </summary>
        internal bool WritesOwnTag;

        /// <summary>
        /// Whether a value of this shape can be <c>null</c>, which is what decides how far
        /// <c>IsRequired</c> is allowed to go and whether a repeated element needs a null check.
        /// </summary>
        internal bool IsReference;

        /// <summary>
        /// Reports whether protobuf-net would accept this shape in a packed run.
        /// </summary>
        /// <remarks>
        /// Only the fixed-width and varint wire types can be packed; a length-delimited value
        /// carries its own length and a packed run of them could not be parsed at all. This decides
        /// whether the generated reader grows the extra length-delimited case that accepts a packed
        /// payload for a member this package always writes unpacked.
        /// </remarks>
        internal bool Packable =>
            WireType == Proto + ".WProtoWireType.Varint"
            || WireType == Proto + ".WProtoWireType.Fixed32"
            || WireType == Proto + ".WProtoWireType.Fixed64";

        /// <summary>
        /// Substitutes <paramref name="value"/> for the placeholder in <paramref name="fragment"/>.
        /// </summary>
        internal static string Fill(string fragment, string value)
        {
            return fragment == null ? null : fragment.Replace(Placeholder, value);
        }

        /// <summary>
        /// Builds the write call for a value of this shape at <paramref name="tag"/>.
        /// </summary>
        internal string WriteCall(string value, int tag)
        {
            // A nested message writes its own key because the key, the length prefix and the payload
            // have to be one operation -- see WProtoWriter.TryWriteMessage. Splitting them would mean
            // producing the length before the payload exists, which is only possible by measuring the
            // sub-message a second time, and that is what breaks the lifecycle-hook contract.
            if (WritesOwnTag)
            {
                return "writer."
                    + WriteMethod
                    + "("
                    + tag
                    + ", "
                    + Fill(WriteCast, value)
                    + value
                    + ")";
            }

            return "writer.TryWriteTag("
                + tag
                + ", "
                + WireType
                + ") && writer."
                + WriteMethod
                + "("
                + Fill(WriteCast, value)
                + value
                + ")";
        }

        /// <summary>
        /// Builds the shape for <paramref name="type"/>, or <c>null</c> when it is not supported.
        /// </summary>
        internal static Shape For(ITypeSymbol type, string qualified)
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
                    PresenceTest = Placeholder + " != default(" + qualified + ")",
                    SizeExpression = wide
                        ? Proto + ".WProtoSizes.Int64Size((long)" + Placeholder + ")"
                        : Proto + ".WProtoSizes.Int32Size((int)" + Placeholder + ")",
                    WriteMethod = wide ? "TryWriteInt64" : "TryWriteInt32",
                    ReadMethod = wide ? "TryReadInt64" : "TryReadInt32",
                    ReadLocalType = wide ? "long" : "int",
                    AssignExpression = "(" + qualified + ")" + Placeholder,
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
                        PresenceTest = Placeholder,
                        SizeExpression = "1",
                        WriteMethod = "TryWriteBool",
                        ReadMethod = "TryReadBool",
                        ReadLocalType = "bool",
                        AssignExpression = Placeholder,
                    };
                }
                case SpecialType.System_SByte:
                case SpecialType.System_Int16:
                case SpecialType.System_Int32:
                {
                    return Integer(qualified, type.SpecialType == SpecialType.System_Int32);
                }
                case SpecialType.System_Byte:
                case SpecialType.System_UInt16:
                case SpecialType.System_UInt32:
                {
                    return Unsigned32(qualified, type.SpecialType == SpecialType.System_UInt32);
                }
                case SpecialType.System_Int64:
                {
                    return new Shape
                    {
                        WireType = Proto + ".WProtoWireType.Varint",
                        PresenceTest = Placeholder + " != 0",
                        SizeExpression = Proto + ".WProtoSizes.Int64Size(" + Placeholder + ")",
                        WriteMethod = "TryWriteInt64",
                        ReadMethod = "TryReadInt64",
                        ReadLocalType = "long",
                        AssignExpression = Placeholder,
                    };
                }
                case SpecialType.System_UInt64:
                {
                    return new Shape
                    {
                        WireType = Proto + ".WProtoWireType.Varint",
                        PresenceTest = Placeholder + " != 0",
                        SizeExpression = Proto + ".WProtoSizes.Varint64Size(" + Placeholder + ")",
                        WriteMethod = "TryWriteVarint64",
                        ReadMethod = "TryReadVarint64",
                        ReadLocalType = "ulong",
                        AssignExpression = Placeholder,
                    };
                }
                case SpecialType.System_Single:
                {
                    return new Shape
                    {
                        WireType = Proto + ".WProtoWireType.Fixed32",
                        PresenceTest = Placeholder + " != 0f",
                        SizeExpression = "4",
                        WriteMethod = "TryWriteSingle",
                        ReadMethod = "TryReadSingle",
                        ReadLocalType = "float",
                        AssignExpression = Placeholder,
                    };
                }
                case SpecialType.System_Double:
                {
                    return new Shape
                    {
                        WireType = Proto + ".WProtoWireType.Fixed64",
                        PresenceTest = Placeholder + " != 0d",
                        SizeExpression = "8",
                        WriteMethod = "TryWriteDouble",
                        ReadMethod = "TryReadDouble",
                        ReadLocalType = "double",
                        AssignExpression = Placeholder,
                    };
                }
                case SpecialType.System_String:
                {
                    return new Shape
                    {
                        WireType = Proto + ".WProtoWireType.LengthDelimited",
                        PresenceTest = Placeholder + " != null",
                        SizeExpression = Proto + ".WProtoSizes.StringSize(" + Placeholder + ")",
                        WriteMethod = "TryWriteString",
                        ReadMethod = "TryReadString",
                        ReadLocalType = "string",
                        AssignExpression = Placeholder,
                        IsReference = true,
                    };
                }
                default:
                {
                    break;
                }
            }

            if (IsByteArray(type))
            {
                return new Shape
                {
                    WireType = Proto + ".WProtoWireType.LengthDelimited",
                    PresenceTest = Placeholder + " != null",
                    SizeExpression =
                        Proto + ".WProtoSizes.LengthDelimitedSize(" + Placeholder + ".Length)",
                    WriteMethod = "TryWriteBytes",
                    ReadMethod = "TryReadBytes",
                    ReadLocalType = "global::System.ReadOnlySpan<byte>",
                    AssignExpression = Placeholder + ".ToArray()",
                    IsReference = true,
                };
            }

            if (IsContract(type))
            {
                string formatter = Proto + ".WProtoFormatterProvider.Get<" + qualified + ">()";
                return new Shape
                {
                    WireType = Proto + ".WProtoWireType.LengthDelimited",

                    // Measured against protobuf-net 3.2.56 rather than assumed, because the two
                    // halves disagree: a null reference sub-message is omitted, but a struct one is
                    // written even when every member equals its default -- `default(Point)` emits
                    // `12 00`, a zero-length payload, where a null `Point` reference emits nothing.
                    PresenceTest = type.IsValueType ? "true" : Placeholder + " != null",
                    SizeExpression =
                        Proto + ".WProtoSizes.MessageSize(" + formatter + ", " + Placeholder + ")",
                    WriteMethod = "TryWriteMessage",
                    WriteCast = formatter + ", ",
                    WritesOwnTag = true,
                    ReadMethod = "TryReadMessage",
                    ReadArguments = formatter + ", ",
                    ReadLocalType = qualified,
                    AssignExpression = Placeholder,
                    IsReference = !type.IsValueType,
                };
            }

            return null;
        }

        /// <summary>
        /// Reports whether <paramref name="type"/> is <c>byte[]</c>, the one array that is a scalar.
        /// </summary>
        internal static bool IsByteArray(ITypeSymbol type)
        {
            return type is IArrayTypeSymbol array
                && array.Rank == 1
                && array.ElementType.SpecialType == SpecialType.System_Byte;
        }

        /// <summary>
        /// Reports whether <paramref name="type"/> carries <c>[WProtoContract]</c>.
        /// </summary>
        /// <remarks>
        /// The attribute is matched by name rather than by symbol identity so a contract declared in
        /// a referenced assembly counts too -- which is the whole point, since a consumer nesting one
        /// of this package's contracts inside one of its own is the case the generator exists for.
        /// </remarks>
        internal static bool IsContract(ITypeSymbol type)
        {
            return FindContractAttribute(type) != null;
        }

        /// <summary>
        /// Reports whether the contract on <paramref name="type"/> sets <c>IgnoreListHandling</c>,
        /// which declares it a message even though it is also a collection.
        /// </summary>
        internal static bool IgnoresListHandling(ITypeSymbol type)
        {
            AttributeData contract = FindContractAttribute(type);
            if (contract == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, TypedConstant> argument in contract.NamedArguments)
            {
                if (argument.Key == "IgnoreListHandling" && argument.Value.Value is bool ignore)
                {
                    return ignore;
                }
            }

            return false;
        }

        private static AttributeData FindContractAttribute(ITypeSymbol type)
        {
            if (type.TypeKind != TypeKind.Class && type.TypeKind != TypeKind.Struct)
            {
                return null;
            }

            foreach (AttributeData attribute in type.GetAttributes())
            {
                if (
                    attribute.AttributeClass != null
                    && attribute.AttributeClass.ToDisplayString()
                        == "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoContractAttribute"
                )
                {
                    return attribute;
                }
            }

            return null;
        }

        private static Shape Integer(string qualified, bool exact)
        {
            string widened = exact ? Placeholder : "(int)" + Placeholder;
            return new Shape
            {
                WireType = Proto + ".WProtoWireType.Varint",
                PresenceTest = Placeholder + " != 0",
                SizeExpression = Proto + ".WProtoSizes.Int32Size(" + widened + ")",
                WriteMethod = "TryWriteInt32",
                ReadMethod = "TryReadInt32",
                ReadLocalType = "int",
                AssignExpression = exact ? Placeholder : "(" + qualified + ")" + Placeholder,
                WriteCast = exact ? string.Empty : "(int)",
            };
        }

        private static Shape Unsigned32(string qualified, bool exact)
        {
            string widened = exact ? Placeholder : "(uint)" + Placeholder;
            return new Shape
            {
                WireType = Proto + ".WProtoWireType.Varint",
                PresenceTest = Placeholder + " != 0",
                SizeExpression = Proto + ".WProtoSizes.Varint32Size(" + widened + ")",
                WriteMethod = "TryWriteVarint32",
                ReadMethod = "TryReadVarint32",
                ReadLocalType = "uint",
                AssignExpression = exact ? Placeholder : "(" + qualified + ")" + Placeholder,
                WriteCast = exact ? string.Empty : "(uint)",
            };
        }
    }
}
