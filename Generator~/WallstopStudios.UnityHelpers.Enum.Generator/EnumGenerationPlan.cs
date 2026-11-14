// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Enum.Generator
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Microsoft.CodeAnalysis;

    /// <summary>All compiler-resolved inputs needed to emit one enum's fast-path class.</summary>
    internal sealed class EnumGenerationPlan
    {
        public const string AttributeMetadataName =
            "WallstopStudios.UnityHelpers.Core.Attributes.WEnumHelpersAttribute";
        public const string DisplayNameMetadataName =
            "WallstopStudios.UnityHelpers.Core.Attributes.EnumDisplayNameAttribute";
        public const int MaximumArrayLength = 256;
        public const int MaximumSwitchEntries = 16;

        public bool HasDisplayTable => HasDisplayNames || HasAliases;

        public readonly INamedTypeSymbol EnumType;
        public readonly string EnumTypeName;
        public readonly string Namespace;
        public readonly string ClassName;
        public readonly string HintName;
        public readonly string AccessibilityModifier;
        public readonly string UnsignedCastType;
        public readonly bool IsSigned;
        public readonly EnumNameEntry[] Entries;
        public readonly bool UseArray;
        public readonly bool UseSwitch;
        public readonly ulong Minimum;
        public readonly int ArrayLength;
        public readonly bool HasDisplayNames;
        public readonly bool HasAliases;

        private EnumGenerationPlan(
            INamedTypeSymbol enumType,
            string enumTypeName,
            string namespaceName,
            string className,
            string hintName,
            string accessibilityModifier,
            string unsignedCastType,
            bool isSigned,
            EnumNameEntry[] entries,
            bool useArray,
            bool useSwitch,
            ulong minimum,
            int arrayLength,
            bool hasDisplayNames,
            bool hasAliases
        )
        {
            EnumType = enumType;
            EnumTypeName = enumTypeName;
            Namespace = namespaceName;
            ClassName = className;
            HintName = hintName;
            AccessibilityModifier = accessibilityModifier;
            UnsignedCastType = unsignedCastType;
            IsSigned = isSigned;
            Entries = entries;
            UseArray = useArray;
            UseSwitch = useSwitch;
            Minimum = minimum;
            ArrayLength = arrayLength;
            HasDisplayNames = hasDisplayNames;
            HasAliases = hasAliases;
        }

        public static EnumGenerationPlan TryCreate(
            INamedTypeSymbol enumType,
            Compilation compilation,
            out string reason
        )
        {
            reason = null;
            if (enumType.TypeKind != TypeKind.Enum)
            {
                reason = "the target is not an enum";
                return null;
            }
            if (IsObsoleteError(enumType))
            {
                reason = "the enum is obsolete with error severity";
                return null;
            }
            if (!TryResolveAccessibility(enumType, out string accessibility, out reason))
            {
                return null;
            }
            if (!TryResolveUnderlying(enumType, out string unsignedCastType, out bool isSigned))
            {
                reason = "the enum's underlying type is not one of the eight integral types";
                return null;
            }

            List<INamedTypeSymbol> owners = new();
            for (
                INamedTypeSymbol owner = enumType.ContainingType;
                owner != null;
                owner = owner.ContainingType
            )
            {
                if (owner.Arity != 0)
                {
                    reason = "the enum is nested in a generic type";
                    return null;
                }
                owners.Add(owner);
            }

            string namespaceName = enumType.ContainingNamespace.IsGlobalNamespace
                ? null
                : enumType.ContainingNamespace.ToDisplayString();
            string className = BuildClassName(enumType, owners);
            string fullClassName =
                namespaceName == null ? className : namespaceName + "." + className;
            if (compilation.GetTypeByMetadataName(fullClassName) != null)
            {
                className += "_" + StableHash(enumType.ToDisplayString());
                fullClassName = namespaceName == null ? className : namespaceName + "." + className;
            }

            EnumNameEntry[] entries = CollectEntries(
                enumType,
                out bool hasDisplayNames,
                out bool hasAliases
            );
            bool useArray = false;
            bool useSwitch = false;
            ulong minimum = 0;
            int arrayLength = 0;
            if (entries.Length != 0)
            {
                ulong minimumKey = entries[0].Key;
                ulong maximumKey = entries[0].Key;
                for (int index = 1; index < entries.Length; ++index)
                {
                    ulong key = entries[index].Key;
                    if (key < minimumKey)
                    {
                        minimumKey = key;
                    }
                    if (maximumKey < key)
                    {
                        maximumKey = key;
                    }
                }
                ulong span = unchecked(maximumKey - minimumKey);
                if (span < MaximumArrayLength)
                {
                    useArray = true;
                    minimum = minimumKey;
                    arrayLength = (int)span + 1;
                }
                else if (entries.Length <= MaximumSwitchEntries)
                {
                    useSwitch = true;
                }
            }

            return new EnumGenerationPlan(
                enumType,
                enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                namespaceName,
                className,
                BuildHintName(enumType),
                accessibility,
                unsignedCastType,
                isSigned,
                entries,
                useArray,
                useSwitch,
                minimum,
                arrayLength,
                hasDisplayNames,
                hasAliases
            );
        }

        private static EnumNameEntry[] CollectEntries(
            INamedTypeSymbol enumType,
            out bool hasDisplayNames,
            out bool hasAliases
        )
        {
            hasDisplayNames = false;
            hasAliases = false;
            List<IFieldSymbol> fields = new();
            foreach (ISymbol member in enumType.GetMembers())
            {
                if (
                    member is IFieldSymbol field
                    && field.IsStatic
                    && field.HasConstantValue
                    && !string.Equals(field.Name, "value__", StringComparison.Ordinal)
                )
                {
                    fields.Add(field);
                }
            }

            Dictionary<ulong, int> counts = new();
            List<EnumNameEntry> candidates = new(fields.Count);
            foreach (IFieldSymbol field in fields)
            {
                if (!TryConvertKey(field.ConstantValue, enumType, out ulong key))
                {
                    continue;
                }
                if (counts.TryGetValue(key, out int count))
                {
                    counts[key] = count + 1;
                }
                else
                {
                    counts[key] = 1;
                }

                string displayName = field.Name;
                foreach (AttributeData attribute in field.GetAttributes())
                {
                    if (
                        attribute.AttributeClass != null
                        && string.Equals(
                            attribute.AttributeClass.ToDisplayString(),
                            DisplayNameMetadataName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        hasDisplayNames = true;
                        if (
                            attribute.ConstructorArguments.Length != 0
                            && attribute.ConstructorArguments[0].Value is string text
                            && text != null
                        )
                        {
                            displayName = text;
                        }
                    }
                }
                candidates.Add(new EnumNameEntry(key, field.Name, displayName));
            }

            List<EnumNameEntry> entries = new(candidates.Count);
            foreach (EnumNameEntry candidate in candidates)
            {
                /*
                    Duplicate values are aliases. The runtime owns which alias ToString and display
                    names report, so generated tables leave the value out and the generalized path
                    answers it.
                */
                if (counts.TryGetValue(candidate.Key, out int occurrences) && occurrences == 1)
                {
                    entries.Add(candidate);
                }
                else
                {
                    hasAliases = true;
                }
            }
            entries.Sort((left, right) => left.Key.CompareTo(right.Key));
            return entries.ToArray();
        }

        private static bool TryConvertKey(object value, INamedTypeSymbol enumType, out ulong key)
        {
            try
            {
                switch (enumType.EnumUnderlyingType.SpecialType)
                {
                    case SpecialType.System_SByte:
                    case SpecialType.System_Int16:
                    case SpecialType.System_Int32:
                    case SpecialType.System_Int64:
                        key = unchecked(
                            (ulong)Convert.ToInt64(value, CultureInfo.InvariantCulture)
                        );
                        return true;
                    case SpecialType.System_Byte:
                    case SpecialType.System_UInt16:
                    case SpecialType.System_UInt32:
                    case SpecialType.System_UInt64:
                        key = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                        return true;
                    default:
                        key = 0;
                        return false;
                }
            }
            catch (Exception exception)
                when (exception is FormatException
                    || exception is InvalidCastException
                    || exception is OverflowException
                )
            {
                key = 0;
                return false;
            }
        }

        private static bool TryResolveAccessibility(
            INamedTypeSymbol enumType,
            out string accessibility,
            out string reason
        )
        {
            reason = null;
            accessibility = "internal";
            Accessibility effective = enumType.DeclaredAccessibility;
            for (
                INamedTypeSymbol owner = enumType.ContainingType;
                owner != null;
                owner = owner.ContainingType
            )
            {
                if (owner.DeclaredAccessibility == Accessibility.Private)
                {
                    reason = "the enum or a containing type is private";
                    return false;
                }
                if (owner.DeclaredAccessibility == Accessibility.Protected)
                {
                    reason = "the enum or a containing type is protected";
                    return false;
                }
                if (owner.DeclaredAccessibility == Accessibility.ProtectedAndInternal)
                {
                    reason = "the enum or a containing type is private protected";
                    return false;
                }
                if (owner.DeclaredAccessibility == Accessibility.NotApplicable)
                {
                    reason = "the enum is nested in a file-local type";
                    return false;
                }
                if (owner.DeclaredAccessibility != Accessibility.Public)
                {
                    effective = Accessibility.Internal;
                }
            }
            if (effective == Accessibility.Private || effective == Accessibility.Protected)
            {
                reason = "the enum is not accessible outside its containing type";
                return false;
            }
            if (effective == Accessibility.ProtectedAndInternal)
            {
                reason = "the enum is private protected";
                return false;
            }
            if (effective == Accessibility.NotApplicable)
            {
                reason = "the enum is file-local";
                return false;
            }
            if (
                effective == Accessibility.Public
                && enumType.DeclaredAccessibility == Accessibility.Public
            )
            {
                accessibility = "public";
            }
            return true;
        }

        private static bool TryResolveUnderlying(
            INamedTypeSymbol enumType,
            out string unsignedCastType,
            out bool isSigned
        )
        {
            switch (enumType.EnumUnderlyingType.SpecialType)
            {
                case SpecialType.System_SByte:
                    unsignedCastType = "byte";
                    isSigned = true;
                    return true;
                case SpecialType.System_Byte:
                    unsignedCastType = "byte";
                    isSigned = false;
                    return true;
                case SpecialType.System_Int16:
                    unsignedCastType = "ushort";
                    isSigned = true;
                    return true;
                case SpecialType.System_UInt16:
                    unsignedCastType = "ushort";
                    isSigned = false;
                    return true;
                case SpecialType.System_Int32:
                    unsignedCastType = "uint";
                    isSigned = true;
                    return true;
                case SpecialType.System_UInt32:
                    unsignedCastType = "uint";
                    isSigned = false;
                    return true;
                case SpecialType.System_Int64:
                    unsignedCastType = "ulong";
                    isSigned = true;
                    return true;
                case SpecialType.System_UInt64:
                    unsignedCastType = "ulong";
                    isSigned = false;
                    return true;
                default:
                    unsignedCastType = null;
                    isSigned = false;
                    return false;
            }
        }

        private static bool IsObsoleteError(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol current = type; current != null; current = current.ContainingType)
            {
                foreach (AttributeData attribute in current.GetAttributes())
                {
                    if (
                        attribute.AttributeClass != null
                        && string.Equals(
                            attribute.AttributeClass.ToDisplayString(),
                            "System.ObsoleteAttribute",
                            StringComparison.Ordinal
                        )
                        && HasErrorArgument(attribute)
                    )
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool HasErrorArgument(AttributeData attribute)
        {
            foreach (TypedConstant argument in attribute.ConstructorArguments)
            {
                if (argument.Value is bool isError && isError)
                {
                    return true;
                }
            }
            return false;
        }

        private static string BuildClassName(
            INamedTypeSymbol enumType,
            List<INamedTypeSymbol> owners
        )
        {
            string name = enumType.Name;
            for (int index = owners.Count - 1; index >= 0; --index)
            {
                name = owners[index].Name + "_" + name;
            }
            return name + "WEnumHelpers";
        }

        private static string BuildHintName(INamedTypeSymbol enumType)
        {
            string fullName = enumType.ToDisplayString();
            char[] characters = new char[fullName.Length];
            for (int index = 0; index < fullName.Length; ++index)
            {
                char character = fullName[index];
                characters[index] =
                    (character >= 'a' && character <= 'z')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= '0' && character <= '9')
                        ? character
                        : '_';
            }
            return new string(characters) + "_" + StableHash(fullName) + ".WEnumHelpers.g.cs";
        }

        private static string StableHash(string text)
        {
            uint hash = 2166136261;
            foreach (char character in text)
            {
                hash ^= character;
                hash *= 16777619;
            }
            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
