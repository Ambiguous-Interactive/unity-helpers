// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System;
    using System.Collections.Generic;
    using System.ComponentModel;
    using System.Globalization;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;

    /// <summary>Resolves scalar omission constants without runtime type conversion.</summary>
    internal static class DefaultValueExpression
    {
        internal static bool TryCreate(
            AttributeData attribute,
            ITypeSymbol type,
            out string expression
        )
        {
            ITypeSymbol scalar = type;
            if (
                type is INamedTypeSymbol nullable
                && nullable.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T
            )
            {
                scalar = nullable.TypeArguments[0];
            }
            try
            {
                TypedConstant constant =
                    attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0]
                    : attribute.ConstructorArguments.Length == 2 ? attribute.ConstructorArguments[1]
                    : default;
                if (
                    constant.Kind == TypedConstantKind.Array
                    || constant.Kind == TypedConstantKind.Type
                    || constant.Kind == TypedConstantKind.Error
                )
                {
                    expression = null;
                    return false;
                }
                object value = constant.Value;
                ITypeSymbol constantType = constant.Type;
                if (attribute.ConstructorArguments.Length == 2)
                {
                    if (
                        !(attribute.ConstructorArguments[0].Value is ITypeSymbol declared)
                        || !(value is string text)
                        || !TryConvertText(declared, text, out value)
                    )
                    {
                        expression = null;
                        return false;
                    }
                }
                if (attribute.ConstructorArguments.Length == 2)
                {
                    constantType = attribute.ConstructorArguments[0].Value as ITypeSymbol;
                }
                if (
                    scalar.SpecialType == SpecialType.System_String
                    && constantType is INamedTypeSymbol declaredEnumeration
                    && constantType.TypeKind == TypeKind.Enum
                )
                {
                    if (!TryEnumText(declaredEnumeration, value, out string enumText))
                    {
                        expression = null;
                        return false;
                    }
                    value = enumText;
                }
                if (
                    value is DateTime zoned
                    && zoned.Kind == DateTimeKind.Local
                    && scalar.SpecialType == SpecialType.System_String
                )
                {
                    DateTime instant = zoned.ToUniversalTime();
                    if (instant.ToLocalTime().Ticks != zoned.Ticks)
                    {
                        expression = null;
                        return false;
                    }
                    expression =
                        "new global::System.DateTime("
                        + instant.Ticks.ToString(CultureInfo.InvariantCulture)
                        + "L, global::System.DateTimeKind.Utc).ToLocalTime().ToString(global::System.Globalization.CultureInfo.InvariantCulture)";
                    return true;
                }
                if (value == null)
                {
                    expression = "null";
                    return scalar.IsReferenceType
                        || !SymbolEqualityComparer.Default.Equals(type, scalar);
                }
                if (scalar.TypeKind == TypeKind.Enum && scalar is INamedTypeSymbol enumeration)
                {
                    if (
                        value is string directName
                        && attribute.ConstructorArguments.Length == 1
                        && 0 <= directName.IndexOf(',')
                    )
                    {
                        foreach (string token in directName.Split(','))
                        {
                            if (
                                long.TryParse(
                                    token.Trim(),
                                    NumberStyles.Integer,
                                    CultureInfo.InvariantCulture,
                                    out long ignored
                                )
                                || ulong.TryParse(
                                    token.Trim(),
                                    NumberStyles.Integer,
                                    CultureInfo.InvariantCulture,
                                    out ulong ignoredUnsigned
                                )
                            )
                            {
                                expression = null;
                                return false;
                            }
                        }
                    }
                    if (value is string name && !TryConvertText(enumeration, name, out value))
                    {
                        expression = null;
                        return false;
                    }
                    if (value is float || value is double || value is decimal)
                    {
                        expression = null;
                        return false;
                    }
                    string numeric = value is ulong unsigned
                        ? unsigned.ToString(CultureInfo.InvariantCulture) + "UL"
                        : Convert
                            .ToInt64(value, CultureInfo.InvariantCulture)
                            .ToString(CultureInfo.InvariantCulture) + "L";
                    expression =
                        "unchecked(("
                        + scalar.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        + ")("
                        + numeric
                        + "))";
                    return true;
                }
                string literal;
                switch (scalar.SpecialType)
                {
                    case SpecialType.System_Boolean:
                        literal = Convert.ToBoolean(value, CultureInfo.InvariantCulture)
                            ? "true"
                            : "false";
                        break;
                    case SpecialType.System_Char:
                        literal = SymbolDisplay.FormatLiteral(
                            Convert.ToChar(value, CultureInfo.InvariantCulture),
                            true
                        );
                        break;
                    case SpecialType.System_String:
                        literal = SymbolDisplay.FormatLiteral(
                            Convert.ToString(value, CultureInfo.InvariantCulture),
                            true
                        );
                        break;
                    case SpecialType.System_SByte:
                        literal = Convert
                            .ToSByte(value, CultureInfo.InvariantCulture)
                            .ToString(CultureInfo.InvariantCulture);
                        break;
                    case SpecialType.System_Byte:
                        literal = Convert
                            .ToByte(value, CultureInfo.InvariantCulture)
                            .ToString(CultureInfo.InvariantCulture);
                        break;
                    case SpecialType.System_Int16:
                        literal = Convert
                            .ToInt16(value, CultureInfo.InvariantCulture)
                            .ToString(CultureInfo.InvariantCulture);
                        break;
                    case SpecialType.System_UInt16:
                        literal = Convert
                            .ToUInt16(value, CultureInfo.InvariantCulture)
                            .ToString(CultureInfo.InvariantCulture);
                        break;
                    case SpecialType.System_Int32:
                        literal = Convert
                            .ToInt32(value, CultureInfo.InvariantCulture)
                            .ToString(CultureInfo.InvariantCulture);
                        break;
                    case SpecialType.System_UInt32:
                        literal =
                            Convert
                                .ToUInt32(value, CultureInfo.InvariantCulture)
                                .ToString(CultureInfo.InvariantCulture) + "U";
                        break;
                    case SpecialType.System_Int64:
                        literal =
                            Convert
                                .ToInt64(value, CultureInfo.InvariantCulture)
                                .ToString(CultureInfo.InvariantCulture) + "L";
                        break;
                    case SpecialType.System_UInt64:
                        literal =
                            Convert
                                .ToUInt64(value, CultureInfo.InvariantCulture)
                                .ToString(CultureInfo.InvariantCulture) + "UL";
                        break;
                    case SpecialType.System_Single:
                        float single = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                        if (float.IsNaN(single))
                        {
                            expression = null;
                            return false;
                        }
                        literal =
                            float.IsPositiveInfinity(single)
                                ? "global::System.Single.PositiveInfinity"
                            : float.IsNegativeInfinity(single)
                                ? "global::System.Single.NegativeInfinity"
                            : single.ToString("R", CultureInfo.InvariantCulture) + "F";
                        break;
                    case SpecialType.System_Double:
                        double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        if (double.IsNaN(number))
                        {
                            expression = null;
                            return false;
                        }
                        literal =
                            double.IsPositiveInfinity(number)
                                ? "global::System.Double.PositiveInfinity"
                            : double.IsNegativeInfinity(number)
                                ? "global::System.Double.NegativeInfinity"
                            : number.ToString("R", CultureInfo.InvariantCulture) + "D";
                        break;
                    case SpecialType.System_Decimal:
                        literal =
                            Convert
                                .ToDecimal(value, CultureInfo.InvariantCulture)
                                .ToString(CultureInfo.InvariantCulture) + "M";
                        break;
                    case SpecialType.System_DateTime:
                        DateTime timestamp = value is DateTime existingTimestamp
                            ? existingTimestamp
                            : DateTime.Parse(
                                Convert.ToString(value, CultureInfo.InvariantCulture),
                                CultureInfo.InvariantCulture,
                                DateTimeStyles.None
                            );
                        if (
                            timestamp.Kind == DateTimeKind.Local
                            && timestamp.ToUniversalTime().ToLocalTime().Ticks != timestamp.Ticks
                        )
                        {
                            expression = null;
                            return false;
                        }
                        literal =
                            timestamp.Kind == DateTimeKind.Local
                                ? "new global::System.DateTime("
                                    + timestamp
                                        .ToUniversalTime()
                                        .Ticks.ToString(CultureInfo.InvariantCulture)
                                    + "L, global::System.DateTimeKind.Utc).ToLocalTime()"
                                : "new global::System.DateTime("
                                    + timestamp.Ticks.ToString(CultureInfo.InvariantCulture)
                                    + "L, (global::System.DateTimeKind)"
                                    + ((int)timestamp.Kind).ToString(CultureInfo.InvariantCulture)
                                    + ")";
                        break;
                    default:
                        string fullName = scalar.ToDisplayString();
                        if (string.Equals(fullName, "System.TimeSpan", StringComparison.Ordinal))
                        {
                            TimeSpan duration = TimeSpan.Parse(
                                Convert.ToString(value, CultureInfo.InvariantCulture),
                                CultureInfo.InvariantCulture
                            );
                            literal =
                                "new global::System.TimeSpan("
                                + duration.Ticks.ToString(CultureInfo.InvariantCulture)
                                + "L)";
                        }
                        else if (string.Equals(fullName, "System.Guid", StringComparison.Ordinal))
                        {
                            byte[] bytes = Guid.Parse(
                                    Convert.ToString(value, CultureInfo.InvariantCulture)
                                )
                                .ToByteArray();
                            literal =
                                "new global::System.Guid("
                                + BitConverter
                                    .ToInt32(bytes, 0)
                                    .ToString(CultureInfo.InvariantCulture)
                                + ", (short)"
                                + BitConverter
                                    .ToInt16(bytes, 4)
                                    .ToString(CultureInfo.InvariantCulture)
                                + ", (short)"
                                + BitConverter
                                    .ToInt16(bytes, 6)
                                    .ToString(CultureInfo.InvariantCulture);
                            for (int index = 8; index < bytes.Length; ++index)
                            {
                                literal +=
                                    ", (byte)"
                                    + bytes[index].ToString(CultureInfo.InvariantCulture);
                            }
                            literal += ")";
                        }
                        else
                        {
                            expression = null;
                            return false;
                        }
                        break;
                }
                expression = literal;
                return true;
            }
            catch (Exception error)
                when (error is ArgumentException
                    || error is FormatException
                    || error is InvalidCastException
                    || error is OverflowException
                )
            {
                expression = null;
                return false;
            }
        }

        private static bool TryConvertText(ITypeSymbol type, string text, out object value)
        {
            if (type is INamedTypeSymbol enumeration && type.TypeKind == TypeKind.Enum)
            {
                foreach (AttributeData attribute in enumeration.GetAttributes())
                {
                    if (
                        string.Equals(
                            attribute.AttributeClass?.ToDisplayString(),
                            "System.ComponentModel.TypeConverterAttribute",
                            StringComparison.Ordinal
                        )
                    )
                    {
                        value = null;
                        return false;
                    }
                }
                ulong combined = 0;
                foreach (string token in text.Split(','))
                {
                    bool found = false;
                    foreach (ISymbol member in enumeration.GetMembers())
                    {
                        if (
                            member is IFieldSymbol field
                            && field.HasConstantValue
                            && string.Equals(
                                field.Name,
                                token.Trim(),
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            if (found)
                            {
                                value = null;
                                return false;
                            }
                            combined |= field.ConstantValue is ulong unsigned
                                ? unsigned
                                : unchecked(
                                    (ulong)
                                        Convert.ToInt64(
                                            field.ConstantValue,
                                            CultureInfo.InvariantCulture
                                        )
                                );
                            found = true;
                        }
                    }
                    if (!found)
                    {
                        if (
                            ulong.TryParse(
                                token.Trim(),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out ulong unsigned
                            )
                        )
                        {
                            if (
                                !EnumBitsFit(enumeration.EnumUnderlyingType.SpecialType, unsigned)
                                || enumeration.EnumUnderlyingType.SpecialType
                                    != SpecialType.System_UInt64
                                    && long.MaxValue < unsigned
                            )
                            {
                                value = null;
                                return false;
                            }
                            combined |= unsigned;
                        }
                        else if (
                            long.TryParse(
                                token.Trim(),
                                NumberStyles.Integer,
                                CultureInfo.InvariantCulture,
                                out long signed
                            )
                        )
                        {
                            if (
                                !EnumBitsFit(
                                    enumeration.EnumUnderlyingType.SpecialType,
                                    unchecked((ulong)signed)
                                )
                                || enumeration.EnumUnderlyingType.SpecialType
                                    == SpecialType.System_UInt64
                                    && signed < 0
                            )
                            {
                                value = null;
                                return false;
                            }
                            combined |= unchecked((ulong)signed);
                        }
                        else
                        {
                            value = null;
                            return false;
                        }
                    }
                }
                switch (enumeration.EnumUnderlyingType.SpecialType)
                {
                    case SpecialType.System_SByte:
                        value = unchecked((sbyte)combined);
                        return true;
                    case SpecialType.System_Byte:
                        value = unchecked((byte)combined);
                        return true;
                    case SpecialType.System_Int16:
                        value = unchecked((short)combined);
                        return true;
                    case SpecialType.System_UInt16:
                        value = unchecked((ushort)combined);
                        return true;
                    case SpecialType.System_Int32:
                        value = unchecked((int)combined);
                        return true;
                    case SpecialType.System_UInt32:
                        value = unchecked((uint)combined);
                        return true;
                    case SpecialType.System_Int64:
                        value = unchecked((long)combined);
                        return true;
                    case SpecialType.System_UInt64:
                        value = combined;
                        return true;
                }
            }
            TypeConverter converter;
            switch (type.SpecialType)
            {
                case SpecialType.System_Boolean:
                    converter = new BooleanConverter();
                    break;
                case SpecialType.System_Char:
                    converter = new CharConverter();
                    break;
                case SpecialType.System_String:
                    converter = new StringConverter();
                    break;
                case SpecialType.System_SByte:
                    converter = new SByteConverter();
                    break;
                case SpecialType.System_Byte:
                    converter = new ByteConverter();
                    break;
                case SpecialType.System_Int16:
                    converter = new Int16Converter();
                    break;
                case SpecialType.System_UInt16:
                    converter = new UInt16Converter();
                    break;
                case SpecialType.System_Int32:
                    converter = new Int32Converter();
                    break;
                case SpecialType.System_UInt32:
                    converter = new UInt32Converter();
                    break;
                case SpecialType.System_Int64:
                    converter = new Int64Converter();
                    break;
                case SpecialType.System_UInt64:
                    converter = new UInt64Converter();
                    break;
                case SpecialType.System_Single:
                    converter = new SingleConverter();
                    break;
                case SpecialType.System_Double:
                    converter = new DoubleConverter();
                    break;
                case SpecialType.System_Decimal:
                    converter = new DecimalConverter();
                    break;
                case SpecialType.System_DateTime:
                    converter = new DateTimeConverter();
                    break;
                default:
                    string name = type.ToDisplayString();
                    if (string.Equals(name, "System.TimeSpan", StringComparison.Ordinal))
                    {
                        converter = new TimeSpanConverter();
                    }
                    else if (string.Equals(name, "System.Guid", StringComparison.Ordinal))
                    {
                        converter = new GuidConverter();
                    }
                    else
                    {
                        value = null;
                        return false;
                    }
                    break;
            }
            value = converter.ConvertFromInvariantString(text);
            return true;
        }

        private static bool EnumBitsFit(SpecialType type, ulong bits)
        {
            switch (type)
            {
                case SpecialType.System_SByte:
                    return bits <= (ulong)sbyte.MaxValue
                        || sbyte.MinValue <= unchecked((long)bits) && unchecked((long)bits) < 0;
                case SpecialType.System_Byte:
                    return bits <= byte.MaxValue;
                case SpecialType.System_Int16:
                    return bits <= (ulong)short.MaxValue
                        || short.MinValue <= unchecked((long)bits) && unchecked((long)bits) < 0;
                case SpecialType.System_UInt16:
                    return bits <= ushort.MaxValue;
                case SpecialType.System_Int32:
                    return bits <= int.MaxValue
                        || int.MinValue <= unchecked((long)bits) && unchecked((long)bits) < 0;
                case SpecialType.System_UInt32:
                    return bits <= uint.MaxValue;
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryEnumText(INamedTypeSymbol type, object number, out string text)
        {
            ulong bits = number is ulong unsigned
                ? unsigned
                : unchecked((ulong)Convert.ToInt64(number, CultureInfo.InvariantCulture));
            List<KeyValuePair<ulong, string>> names = new List<KeyValuePair<ulong, string>>();
            foreach (ISymbol member in type.GetMembers())
            {
                if (member is IFieldSymbol field && field.HasConstantValue)
                {
                    ulong key = field.ConstantValue is ulong unsignedField
                        ? unsignedField
                        : unchecked(
                            (ulong)
                                Convert.ToInt64(field.ConstantValue, CultureInfo.InvariantCulture)
                        );
                    foreach (KeyValuePair<ulong, string> existing in names)
                    {
                        if (existing.Key == key)
                        {
                            text = null;
                            return false;
                        }
                    }
                    names.Add(new KeyValuePair<ulong, string>(key, field.Name));
                }
            }
            foreach (KeyValuePair<ulong, string> name in names)
            {
                if (name.Key == bits)
                {
                    text = name.Value;
                    return true;
                }
            }
            bool flags = false;
            foreach (AttributeData attribute in type.GetAttributes())
            {
                flags |= string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    "System.FlagsAttribute",
                    StringComparison.Ordinal
                );
            }
            if (flags && bits != 0)
            {
                names.Sort((left, right) => left.Key.CompareTo(right.Key));
                List<string> selected = new List<string>();
                ulong remaining = bits;
                for (int index = names.Count - 1; 0 <= index; index--)
                {
                    ulong key = names[index].Key;
                    if (key != 0 && (remaining & key) == key)
                    {
                        selected.Insert(0, names[index].Value);
                        remaining &= ~key;
                    }
                }
                if (remaining == 0)
                {
                    text = string.Join(", ", selected);
                    return true;
                }
            }
            text = Convert.ToString(number, CultureInfo.InvariantCulture);
            return true;
        }
    }
}
