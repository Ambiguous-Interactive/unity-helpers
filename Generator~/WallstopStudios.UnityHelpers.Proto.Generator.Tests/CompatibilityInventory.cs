// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    internal static class CompatibilityInventory
    {
        internal static CSharpCompilation Compile(IEnumerable<SyntaxTree> trees)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
            string trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            Assert.IsTrue(trustedAssemblies != null);
            foreach (string path in trustedAssemblies.Split(Path.PathSeparator))
            {
                paths.Add(path);
            }
            paths.Add(typeof(WProtoContractAttribute).Assembly.Location);
            paths.Add(typeof(ProtoBuf.ProtoContractAttribute).Assembly.Location);
            return CSharpCompilation.Create(
                nameof(CompatibilityInventory),
                trees,
                paths.Select(path => MetadataReference.CreateFromFile(path)),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
        }

        internal static string[] Capture(CSharpCompilation compilation)
        {
            SortedSet<string> entries = new SortedSet<string>(StringComparer.Ordinal);
            HashSet<ISymbol> seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (
                    TypeDeclarationSyntax declaration in tree.GetRoot()
                        .DescendantNodes()
                        .OfType<TypeDeclarationSyntax>()
                )
                {
                    INamedTypeSymbol type = model.GetDeclaredSymbol(declaration);
                    Assert.IsTrue(type != null, declaration.ToString());
                    CapturePublicSurface(type, entries);
                    if (!seen.Add(type) || !type.GetAttributes().Any(IsContract))
                    {
                        continue;
                    }
                    string identity = type.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    entries.Add(
                        $"contract {identity} kind={type.TypeKind} base={type.BaseType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}"
                    );
                    CaptureAttributes(type, identity, entries);
                    foreach (ISymbol member in type.GetMembers())
                    {
                        if (!member.GetAttributes().Any(IsWireAttribute))
                        {
                            continue;
                        }
                        string memberIdentity = identity + "." + member.Name;
                        CaptureAttributes(member, memberIdentity, entries);
                        ITypeSymbol memberType = member is IFieldSymbol field
                            ? field.Type
                            : (member as IPropertySymbol)?.Type;
                        if (memberType != null)
                        {
                            AssertBoundType(memberType, memberIdentity);
                            CaptureInitializer(member, compilation, memberIdentity, entries);
                            entries.Add(
                                $"member {memberIdentity} type={memberType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}"
                            );
                            CaptureEnums(memberType, entries, seen);
                        }
                    }
                }
            }
            CaptureAttributes(compilation.Assembly, "assembly", entries);
            return entries.ToArray();
        }

        private static void AssertBoundType(ITypeSymbol type, string identity)
        {
            Assert.AreNotEqual(
                TypeKind.Error,
                type.TypeKind,
                identity + ": unresolved member type"
            );
            if (type is IArrayTypeSymbol array)
            {
                AssertBoundType(array.ElementType, identity);
            }
            else if (type is INamedTypeSymbol named)
            {
                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    AssertBoundType(argument, identity);
                }
            }
        }

        private static void CaptureInitializer(
            ISymbol member,
            CSharpCompilation compilation,
            string identity,
            SortedSet<string> entries
        )
        {
            foreach (SyntaxReference reference in member.DeclaringSyntaxReferences)
            {
                SyntaxNode declaration = reference.GetSyntax();
                EqualsValueClauseSyntax initializer = declaration
                    is VariableDeclaratorSyntax variable
                    ? variable.Initializer
                    : (declaration as PropertyDeclarationSyntax)?.Initializer;
                if (initializer == null)
                {
                    continue;
                }
                Optional<object> constant = compilation
                    .GetSemanticModel(reference.SyntaxTree)
                    .GetConstantValue(initializer.Value);
                string value = constant.HasValue
                    ? JsonSerializer.Serialize(constant.Value)
                    : initializer.Value.WithoutTrivia().NormalizeWhitespace().ToFullString();
                entries.Add("initializer " + identity + "=" + value);
            }
        }

        private static void CapturePublicSurface(INamedTypeSymbol type, SortedSet<string> entries)
        {
            if (type.DeclaredAccessibility != Accessibility.Public)
            {
                return;
            }
            string space = type.ContainingNamespace.ToDisplayString();
            bool facade =
                string.Equals(type.Name, nameof(WProtoFacade), StringComparison.Ordinal)
                || string.Equals(
                    type.Name,
                    nameof(WProtoFormatterProvider),
                    StringComparison.Ordinal
                );
            bool extension =
                type.TypeKind == TypeKind.Interface
                && type.Name.StartsWith("IWProto", StringComparison.Ordinal);
            bool publicSerializer =
                string.Equals(
                    space,
                    "WallstopStudios.UnityHelpers.Core.Serialization",
                    StringComparison.Ordinal
                ) && string.Equals(type.Name, "Serializer", StringComparison.Ordinal);
            if (
                !publicSerializer
                && !(
                    string.Equals(space, typeof(WProtoFacade).Namespace, StringComparison.Ordinal)
                    && (facade || extension)
                )
            )
            {
                return;
            }
            entries.Add(
                "api-type "
                    + type.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat.WithGenericsOptions(
                            SymbolDisplayGenericsOptions.IncludeTypeParameters
                                | SymbolDisplayGenericsOptions.IncludeTypeConstraints
                        )
                    )
                    + " bases="
                    + string.Join(
                        ",",
                        type.Interfaces.Select(item =>
                            item.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        )
                    )
            );
            SymbolDisplayFormat format = SymbolDisplayFormat
                .FullyQualifiedFormat.WithMemberOptions(
                    SymbolDisplayMemberOptions.IncludeAccessibility
                        | SymbolDisplayMemberOptions.IncludeModifiers
                        | SymbolDisplayMemberOptions.IncludeType
                        | SymbolDisplayMemberOptions.IncludeParameters
                        | SymbolDisplayMemberOptions.IncludeContainingType
                        | SymbolDisplayMemberOptions.IncludeExplicitInterface
                )
                .WithParameterOptions(
                    SymbolDisplayParameterOptions.IncludeName
                        | SymbolDisplayParameterOptions.IncludeType
                        | SymbolDisplayParameterOptions.IncludeDefaultValue
                        | SymbolDisplayParameterOptions.IncludeParamsRefOut
                )
                .WithGenericsOptions(
                    SymbolDisplayGenericsOptions.IncludeTypeParameters
                        | SymbolDisplayGenericsOptions.IncludeTypeConstraints
                );
            foreach (ISymbol member in type.GetMembers())
            {
                if (
                    member.DeclaredAccessibility == Accessibility.Public
                    && !member.IsImplicitlyDeclared
                    && (
                        !publicSerializer
                        || member.Name.Contains("Proto", StringComparison.Ordinal)
                        || string.Equals(member.Name, "Serialize", StringComparison.Ordinal)
                        || string.Equals(member.Name, "Deserialize", StringComparison.Ordinal)
                        || string.Equals(member.Name, "TryDeserialize", StringComparison.Ordinal)
                    )
                )
                {
                    entries.Add("api " + member.ToDisplayString(format));
                }
            }
        }

        private static void CaptureEnums(
            ITypeSymbol type,
            SortedSet<string> entries,
            HashSet<ISymbol> seen
        )
        {
            if (type is IArrayTypeSymbol array)
            {
                CaptureEnums(array.ElementType, entries, seen);
            }
            if (type is INamedTypeSymbol named)
            {
                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    CaptureEnums(argument, entries, seen);
                }
                if (type.TypeKind == TypeKind.Enum && seen.Add(type))
                {
                    string identity = type.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    entries.Add($"enum {identity} underlying={named.EnumUnderlyingType}");
                    foreach (IFieldSymbol field in named.GetMembers().OfType<IFieldSymbol>())
                    {
                        if (field.HasConstantValue)
                        {
                            entries.Add(
                                $"enum-value {identity}.{field.Name}={Convert.ToString(field.ConstantValue, CultureInfo.InvariantCulture)}"
                            );
                        }
                    }
                }
            }
        }

        private static void CaptureAttributes(
            ISymbol symbol,
            string identity,
            SortedSet<string> entries
        )
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (!IsWireAttribute(attribute))
                {
                    continue;
                }
                Assert.IsTrue(
                    attribute.AttributeConstructor != null,
                    identity + ": unresolved attribute constructor"
                );
                List<string> arguments = new List<string>();
                foreach (TypedConstant argument in attribute.ConstructorArguments)
                {
                    arguments.Add(Constant(argument, identity));
                }
                SortedDictionary<string, string> options = new SortedDictionary<string, string>(
                    StringComparer.Ordinal
                );
                string attributeName = attribute.AttributeClass.Name;
                if (
                    string.Equals(
                        attributeName,
                        nameof(WProtoMemberAttribute),
                        StringComparison.Ordinal
                    )
                )
                {
                    options[nameof(WProtoMemberAttribute.IsPacked)] = "true";
                    options[nameof(WProtoMemberAttribute.IsRequired)] = "false";
                    options[nameof(WProtoMemberAttribute.OverwriteList)] = "false";
                    options[nameof(WProtoMemberAttribute.DataFormat)] = "0";
                }
                else if (
                    string.Equals(
                        attributeName,
                        nameof(WProtoContractAttribute),
                        StringComparison.Ordinal
                    )
                )
                {
                    options[nameof(WProtoContractAttribute.SkipConstructor)] = "false";
                    options[nameof(WProtoContractAttribute.IgnoreListHandling)] = "false";
                }
                foreach (KeyValuePair<string, TypedConstant> option in attribute.NamedArguments)
                {
                    options[option.Key] = Constant(option.Value, identity);
                }
                entries.Add(
                    $"attribute {identity} {attribute.AttributeClass.ToDisplayString()}({string.Join(",", arguments)}) {string.Join(",", options.Select(option => option.Key + "=" + option.Value))}".TrimEnd()
                );
            }
        }

        private static string Constant(TypedConstant constant, string identity)
        {
            Assert.AreNotEqual(
                TypedConstantKind.Error,
                constant.Kind,
                identity + ": unresolved wire metadata"
            );
            if (constant.IsNull)
            {
                return "null";
            }
            if (constant.Kind == TypedConstantKind.Array)
            {
                return "["
                    + string.Join(",", constant.Values.Select(value => Constant(value, identity)))
                    + "]";
            }
            if (constant.Value is ITypeSymbol type)
            {
                Assert.AreNotEqual(
                    TypeKind.Error,
                    type.TypeKind,
                    identity + ": unresolved wire type"
                );
                return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
            if (constant.Value is string text)
            {
                return SymbolDisplay.FormatLiteral(text, true);
            }
            if (constant.Value is bool boolean)
            {
                return boolean ? "true" : "false";
            }
            return Convert.ToString(constant.Value, CultureInfo.InvariantCulture);
        }

        private static bool IsContract(AttributeData attribute)
        {
            string name = attribute.AttributeClass?.ToDisplayString();
            return string.Equals(
                    name,
                    typeof(WProtoContractAttribute).FullName,
                    StringComparison.Ordinal
                )
                || string.Equals(
                    name,
                    typeof(ProtoBuf.ProtoContractAttribute).FullName,
                    StringComparison.Ordinal
                );
        }

        private static bool IsWireAttribute(AttributeData attribute)
        {
            string name = attribute.AttributeClass?.ToDisplayString();
            return name != null
                && (
                    name.StartsWith(
                        "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProto",
                        StringComparison.Ordinal
                    )
                    || name.StartsWith("ProtoBuf.Proto", StringComparison.Ordinal)
                    || string.Equals(
                        name,
                        "System.ComponentModel.DefaultValueAttribute",
                        StringComparison.Ordinal
                    )
                );
        }
    }
}
