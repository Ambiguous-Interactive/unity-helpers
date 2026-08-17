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
        /// The expression naming the formatter that decodes this value, for a sub-message shape.
        /// </summary>
        /// <remarks>
        /// The same text <see cref="ReadArguments"/> carries, without the trailing separator, so a
        /// caller that decodes a payload it has already carved out can name the formatter directly.
        /// </remarks>
        internal string ReadFormatter;

        /// <summary>
        /// Whether the value is encoded as a length-delimited sub-message, which is the one shape a
        /// duplicated field has to merge rather than replace.
        /// </summary>
        internal bool IsMessage;

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
        /// Builds the write call for a value of this shape with <b>no field key</b>.
        /// </summary>
        /// <remarks>
        /// The form a packed run needs: the key and length belong to the run, and the elements
        /// inside it are bare values. Only ever valid for a <see cref="Packable"/> shape, which is
        /// also the only kind that never writes its own tag.
        /// </remarks>
        internal string RawWriteCall(string value)
        {
            return "writer." + WriteMethod + "(" + Fill(WriteCast, value) + value + ")";
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
        /// The shape of a value encoded as a length-delimited sub-message.
        /// </summary>
        /// <param name="formatter">The expression naming the formatter that serves it.</param>
        /// <param name="qualified">The value's fully qualified type.</param>
        /// <param name="isValueType">Whether the type is a struct.</param>
        /// <returns>The shape.</returns>
        /// <remarks>
        /// Shared by a contract member and a nested collection's wrapper so the two are the same
        /// bytes by construction. The presence rule is the one that was measured rather than
        /// assumed, and its two halves disagree: a null reference sub-message is omitted, but a
        /// struct one is written even when every member equals its default -- <c>default(Point)</c>
        /// emits <c>12 00</c>, a zero-length payload, where a null <c>Point</c> reference emits
        /// nothing at all.
        /// </remarks>
        internal static Shape Message(string formatter, string qualified, bool isValueType)
        {
            return new Shape
            {
                WireType = Proto + ".WProtoWireType.LengthDelimited",
                PresenceTest = isValueType ? "true" : Placeholder + " != null",
                SizeExpression =
                    Proto + ".WProtoSizes.MessageSize(" + formatter + ", " + Placeholder + ")",
                WriteMethod = "TryWriteMessage",
                WriteCast = formatter + ", ",
                WritesOwnTag = true,
                ReadMethod = "TryReadMessage",
                ReadArguments = formatter + ", ",
                ReadFormatter = formatter,
                IsMessage = true,
                ReadLocalType = qualified,
                AssignExpression = Placeholder,
                IsReference = !isValueType,
            };
        }

        /// <summary>
        /// Builds the shape for <paramref name="type"/>, or <c>null</c> when it is not supported.
        /// </summary>
        /// <param name="type">The value's type.</param>
        /// <param name="qualified">Its fully qualified name.</param>
        /// <param name="surrogates">The assembly's surrogate registrations.</param>
        /// <param name="nested">
        /// The contract's wrapper-message registry, or <c>null</c> where a collection has no
        /// encoding in this position. Passing it is what makes a collection of collections
        /// expressible; a map <b>key</b> is the one position that passes <c>null</c>, because a
        /// proto3 map key is a scalar and a collection has no scalar spelling to offer.
        /// </param>
        /// <param name="memberName">The declaring member, for a null element's runtime message.</param>
        internal static Shape For(
            ITypeSymbol type,
            string qualified,
            SurrogateMap surrogates,
            NestedCollections nested = null,
            string memberName = null
        )
        {
            // Before anything else: a surrogated type has no wire shape of its own, and its values
            // are converted at the boundary. Measured against protobuf-net -- a surrogated member is
            // byte-identical to a member of the surrogate type, including `0A 00` for a default
            // struct, so this is a substitution rather than a new encoding.
            INamedTypeSymbol surrogate = surrogates?.For(type);
            if (surrogate != null)
            {
                string surrogateQualified = surrogate.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                string surrogateFormatter =
                    Proto + ".WProtoFormatterProvider.Get<" + surrogateQualified + ">()";
                return new Shape
                {
                    WireType = Proto + ".WProtoWireType.LengthDelimited",
                    PresenceTest = type.IsValueType ? "true" : Placeholder + " != null",
                    SizeExpression =
                        Proto
                        + ".WProtoSizes.MessageSize("
                        + surrogateFormatter
                        + ", ("
                        + surrogateQualified
                        + ")"
                        + Placeholder
                        + ")",
                    WriteMethod = "TryWriteMessage",
                    WriteCast = surrogateFormatter + ", (" + surrogateQualified + ")",
                    WritesOwnTag = true,
                    ReadMethod = "TryReadMessage",
                    ReadArguments = surrogateFormatter + ", ",
                    ReadFormatter = surrogateFormatter,
                    IsMessage = true,
                    ReadLocalType = surrogateQualified,
                    AssignExpression = "(" + qualified + ")" + Placeholder,
                    IsReference = !type.IsValueType,
                };
            }

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

            // Deliberately ahead of the nested-collection question below. A type that is both a
            // contract and a collection -- Deque, CyclicBuffer, the four Serializable* collections --
            // already answers as a message here and has done since session 172, and reading it as a
            // wrapped run instead would silently discard its [WProtoMember]s and change bytes
            // already on disk.
            if (IsContract(type))
            {
                return Message(
                    Proto + ".WProtoFormatterProvider.Get<" + qualified + ">()",
                    qualified,
                    type.IsValueType
                );
            }

            // Last, because every shape above is a value protobuf itself has a form for. A
            // collection reaching this point is one protobuf cannot express in this position at all,
            // and a wrapper message is what gives it one.
            return nested?.TryShape(type, qualified, memberName);
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
            return ContractFlag(type, "IgnoreListHandling");
        }

        /// <summary>
        /// Reports whether the contract on <paramref name="type"/> sets <c>SkipConstructor</c>,
        /// which declares that no constructor the author wrote may run when reading one.
        /// </summary>
        /// <param name="type">The contract type.</param>
        /// <returns><c>true</c> when the flag is set.</returns>
        /// <remarks>
        /// Load-bearing rather than an optimization. <c>DotNetRandom</c>'s parameterless constructor
        /// seeds a live generator from a fresh <c>Guid</c>, and its after-deserialization hook
        /// returns early when one already exists -- so a formatter that called it would hand back a
        /// generator on a random stream instead of the saved one, silently, and only for a save the
        /// player had already made.
        /// </remarks>
        internal static bool SkipsConstructor(ITypeSymbol type)
        {
            return ContractFlag(type, "SkipConstructor");
        }

        private static bool ContractFlag(ITypeSymbol type, string name)
        {
            AttributeData contract = FindContractAttribute(type);
            if (contract == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, TypedConstant> argument in contract.NamedArguments)
            {
                if (argument.Key == name && argument.Value.Value is bool flag)
                {
                    return flag;
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
