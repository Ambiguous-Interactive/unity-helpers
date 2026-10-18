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
        internal static CSharpCompilation Compile(
            IEnumerable<SyntaxTree> trees,
            bool includeCompiledRuntime = true
        )
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.Ordinal);
            string trustedAssemblies = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            Assert.IsTrue(trustedAssemblies != null);
            foreach (string path in trustedAssemblies.Split(Path.PathSeparator))
            {
                paths.Add(path);
            }
            string compiledRuntime = typeof(WProtoContractAttribute).Assembly.Location;
            if (includeCompiledRuntime)
            {
                paths.Add(compiledRuntime);
            }
            else
            {
                paths.Remove(compiledRuntime);
            }
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
                    MemberDeclarationSyntax declaration in tree.GetRoot()
                        .DescendantNodes()
                        .OfType<MemberDeclarationSyntax>()
                        .Where(node =>
                            node is BaseTypeDeclarationSyntax || node is DelegateDeclarationSyntax
                        )
                )
                {
                    INamedTypeSymbol type =
                        model.GetDeclaredSymbol(declaration) as INamedTypeSymbol;
                    Assert.IsTrue(type != null, declaration.ToString());
                    CapturePublicSurface(type, compilation, entries);
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
                if (named.ContainingType != null)
                {
                    AssertBoundType(named.ContainingType, identity);
                }
                if (!named.IsUnboundGenericType)
                {
                    foreach (ITypeSymbol argument in named.TypeArguments)
                    {
                        AssertBoundType(argument, identity);
                    }
                }
            }
            else if (type is IPointerTypeSymbol pointer)
            {
                AssertBoundType(pointer.PointedAtType, identity);
            }
            else if (type is IFunctionPointerTypeSymbol function)
            {
                AssertBoundMethod(function.Signature, identity);
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

        private static void CapturePublicSurface(
            INamedTypeSymbol type,
            CSharpCompilation compilation,
            SortedSet<string> entries
        )
        {
            if (!IsExternallyAccessible(type))
            {
                return;
            }
            string space = type.ContainingNamespace.ToDisplayString();
            bool publicSerializer =
                string.Equals(
                    space,
                    "WallstopStudios.UnityHelpers.Core.Serialization",
                    StringComparison.Ordinal
                ) && string.Equals(type.Name, "Serializer", StringComparison.Ordinal);
            if (
                !publicSerializer
                && !string.Equals(space, typeof(WProtoFacade).Namespace, StringComparison.Ordinal)
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
            string identity = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            CaptureApiAttributes(type.GetAttributes(), identity, compilation, entries);
            CaptureAttributeUsage(type, identity, compilation, entries);
            if (type.BaseType != null)
            {
                AssertBoundType(type.BaseType, identity);
            }
            foreach (INamedTypeSymbol implemented in type.Interfaces)
            {
                AssertBoundType(implemented, identity);
            }
            AssertBoundConstraints(type.TypeParameters, identity);
            entries.Add(
                $"api-shape {identity} kind={type.TypeKind} accessibility={type.DeclaredAccessibility} static={type.IsStatic} abstract={type.IsAbstract} sealed={type.IsSealed} readonly={type.IsReadOnly} ref-like={type.IsRefLikeType} base={type.BaseType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} enum={type.EnumUnderlyingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}"
            );
            CaptureTypeParameters(type.TypeParameters, identity, compilation, entries);
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
                    IsExternallyAccessible(member)
                    && (
                        !publicSerializer
                        || member.Name.Contains("Proto", StringComparison.Ordinal)
                        || string.Equals(member.Name, "Serialize", StringComparison.Ordinal)
                        || string.Equals(member.Name, "Deserialize", StringComparison.Ordinal)
                        || string.Equals(member.Name, "TryDeserialize", StringComparison.Ordinal)
                    )
                )
                {
                    string signature = member.ToDisplayString(format);
                    entries.Add("api " + signature);
                    CaptureApiMember(member, signature, compilation, entries);
                }
            }
        }

        private static bool IsExternallyAccessible(ISymbol symbol)
        {
            for (
                ISymbol current = symbol;
                current is not INamespaceSymbol && current != null;
                current = current.ContainingSymbol
            )
            {
                if (
                    current.DeclaredAccessibility != Accessibility.Public
                    && current.DeclaredAccessibility != Accessibility.Protected
                    && current.DeclaredAccessibility != Accessibility.ProtectedOrInternal
                )
                {
                    return false;
                }
            }
            return true;
        }

        private static void AssertBoundConstraints(
            IEnumerable<ITypeParameterSymbol> parameters,
            string identity
        )
        {
            foreach (ITypeParameterSymbol parameter in parameters)
            {
                foreach (ITypeSymbol constraint in parameter.ConstraintTypes)
                {
                    AssertBoundType(constraint, identity);
                }
            }
        }

        private static void AssertBoundParameters(
            IEnumerable<IParameterSymbol> parameters,
            string identity
        )
        {
            foreach (IParameterSymbol parameter in parameters)
            {
                AssertBoundType(parameter.Type, identity);
            }
        }

        private static void AssertValidDefaults(
            IEnumerable<IParameterSymbol> parameters,
            CSharpCompilation compilation,
            string identity
        )
        {
            foreach (IParameterSymbol parameter in parameters)
            {
                foreach (SyntaxReference reference in parameter.DeclaringSyntaxReferences)
                {
                    if (
                        reference.GetSyntax() is ParameterSyntax declaration
                        && declaration.Default != null
                    )
                    {
                        Assert.IsTrue(
                            parameter.HasExplicitDefaultValue,
                            identity + ": unresolved parameter default"
                        );
                        Assert.IsEmpty(
                            compilation
                                .GetSemanticModel(reference.SyntaxTree)
                                .GetDiagnostics(declaration.Default.Span)
                                .Where(item => item.Severity == DiagnosticSeverity.Error),
                            identity + ": invalid parameter default"
                        );
                    }
                }
            }
        }

        private static void AssertBoundMethod(IMethodSymbol method, string identity)
        {
            AssertBoundType(method.ReturnType, identity);
            AssertBoundParameters(method.Parameters, identity);
            AssertBoundConstraints(method.TypeParameters, identity);
        }

        private static void CaptureApiMember(
            ISymbol member,
            string signature,
            CSharpCompilation compilation,
            SortedSet<string> entries
        )
        {
            CaptureApiAttributes(member.GetAttributes(), signature, compilation, entries);
            if (member is IMethodSymbol method)
            {
                AssertBoundMethod(method, signature);
                AssertValidDefaults(method.Parameters, compilation, signature);
                CaptureApiAttributes(
                    method.GetReturnTypeAttributes(),
                    signature + " return",
                    compilation,
                    entries
                );
                CaptureParameters(method.Parameters, signature, compilation, entries);
                CaptureTypeParameters(method.TypeParameters, signature, compilation, entries);
                entries.Add(
                    $"api-method {signature} kind={method.MethodKind} return-ref={method.RefKind} readonly={method.IsReadOnly} extension={method.IsExtensionMethod} nullable={method.ReturnNullableAnnotation} return-type={SourceType(method.ReturnType)}"
                );
            }
            else if (member is IPropertySymbol property)
            {
                AssertBoundType(property.Type, signature);
                AssertBoundParameters(property.Parameters, signature);
                AssertValidDefaults(property.Parameters, compilation, signature);
                CaptureParameters(property.Parameters, signature, compilation, entries);
                entries.Add(
                    $"api-accessor {signature} return-ref={property.RefKind} get={property.GetMethod?.DeclaredAccessibility.ToString() ?? "absent"} set={property.SetMethod?.DeclaredAccessibility.ToString() ?? "absent"} init={property.SetMethod?.IsInitOnly ?? false} nullable={property.NullableAnnotation} type={SourceType(property.Type)}"
                );
            }
            else if (member is IFieldSymbol field)
            {
                AssertBoundType(field.Type, signature);
                entries.Add(
                    $"api-field {signature} nullable={field.NullableAnnotation} type={SourceType(field.Type)}"
                );
                if (field.IsConst)
                {
                    Assert.IsTrue(field.HasConstantValue, signature + ": unresolved API constant");
                }
                if (field.HasConstantValue)
                {
                    entries.Add(
                        "api-constant "
                            + signature
                            + "="
                            + JsonSerializer.Serialize(field.ConstantValue)
                    );
                }
            }
            else if (member is IEventSymbol eventSymbol)
            {
                AssertBoundType(eventSymbol.Type, signature);
                entries.Add(
                    $"api-event {signature} add={eventSymbol.AddMethod?.DeclaredAccessibility.ToString() ?? "absent"} remove={eventSymbol.RemoveMethod?.DeclaredAccessibility.ToString() ?? "absent"} nullable={eventSymbol.NullableAnnotation} type={SourceType(eventSymbol.Type)}"
                );
            }
        }

        private static string SourceType(ITypeSymbol type)
        {
            return type.ToDisplayString(
                SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
                    SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
                )
            );
        }

        private static void CaptureParameters(
            IEnumerable<IParameterSymbol> parameters,
            string identity,
            CSharpCompilation compilation,
            SortedSet<string> entries
        )
        {
            foreach (IParameterSymbol parameter in parameters)
            {
                string parameterIdentity = identity + " parameter=" + parameter.Name;
                entries.Add(
                    $"api-parameter {parameterIdentity} optional={parameter.IsOptional} nullable={parameter.NullableAnnotation} type={SourceType(parameter.Type)}"
                );
                CaptureApiAttributes(
                    parameter.GetAttributes(),
                    parameterIdentity,
                    compilation,
                    entries
                );
            }
        }

        private static void CaptureTypeParameters(
            IEnumerable<ITypeParameterSymbol> parameters,
            string identity,
            CSharpCompilation compilation,
            SortedSet<string> entries
        )
        {
            foreach (ITypeParameterSymbol parameter in parameters)
            {
                string parameterIdentity = identity + " type-parameter=" + parameter.Name;
                entries.Add(
                    $"api-type-parameter {parameterIdentity} variance={parameter.Variance} reference={parameter.HasReferenceTypeConstraint} reference-nullable={parameter.ReferenceTypeConstraintNullableAnnotation} value={parameter.HasValueTypeConstraint} unmanaged={parameter.HasUnmanagedTypeConstraint} notnull={parameter.HasNotNullConstraint} constructor={parameter.HasConstructorConstraint} constraints={string.Join(",", parameter.ConstraintTypes.Select(SourceType))}"
                );
                CaptureApiAttributes(
                    parameter.GetAttributes(),
                    parameterIdentity,
                    compilation,
                    entries
                );
            }
        }

        private static void CaptureApiAttributes(
            IEnumerable<AttributeData> attributes,
            string identity,
            CSharpCompilation compilation,
            SortedSet<string> entries
        )
        {
            List<string> records = new List<string>();
            foreach (AttributeData attribute in attributes)
            {
                string name = attribute.AttributeClass?.ToDisplayString();
                if (
                    string.Equals(
                        name,
                        typeof(System.Runtime.CompilerServices.MethodImplAttribute).FullName,
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        name,
                        typeof(AttributeUsageAttribute).FullName,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }
                records.Add(ApiAttribute(attribute, identity, compilation));
            }
            foreach (
                IGrouping<string, string> group in records.GroupBy(
                    record => record,
                    StringComparer.Ordinal
                )
            )
            {
                entries.Add($"api-attribute {identity} {group.Key} count={group.Count()}");
            }
        }

        private static string ApiAttribute(
            AttributeData attribute,
            string identity,
            CSharpCompilation compilation
        )
        {
            SyntaxReference reference = attribute.ApplicationSyntaxReference;
            if (reference != null)
            {
                Assert.IsEmpty(
                    compilation
                        .GetSemanticModel(reference.SyntaxTree)
                        .GetDiagnostics(reference.Span)
                        .Where(item => item.Severity == DiagnosticSeverity.Error),
                    identity + ": invalid API attribute"
                );
            }
            Assert.IsTrue(
                attribute.AttributeClass != null,
                identity + ": unresolved API attribute"
            );
            AssertBoundType(attribute.AttributeClass, identity);
            Assert.IsTrue(
                attribute.AttributeConstructor != null,
                identity + ": unresolved API attribute constructor"
            );
            AssertBoundMethod(attribute.AttributeConstructor, identity);
            string name = attribute.AttributeClass.ToDisplayString();
            SortedDictionary<string, string> options = new SortedDictionary<string, string>(
                StringComparer.Ordinal
            );
            if (
                string.Equals(
                    name,
                    typeof(AttributeUsageAttribute).FullName,
                    StringComparison.Ordinal
                )
            )
            {
                options[nameof(AttributeUsageAttribute.AllowMultiple)] = "bool:false";
                options[nameof(AttributeUsageAttribute.Inherited)] = "bool:true";
            }
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                options[argument.Key] = ApiConstant(argument.Value, identity);
            }
            string constructorParameters = string.Join(
                ",",
                attribute.AttributeConstructor.Parameters.Select(parameter =>
                    SourceType(parameter.Type)
                )
            );
            return $"{name} constructor=({constructorParameters}) ({string.Join(",", attribute.ConstructorArguments.Select(argument => ApiConstant(argument, identity)))}) {string.Join(",", options.Select(option => option.Key + "=" + option.Value))}".TrimEnd();
        }

        private static string ApiConstant(TypedConstant constant, string identity)
        {
            Assert.IsTrue(constant.Type != null, identity + ": unresolved API argument type");
            AssertBoundType(constant.Type, identity);
            string value =
                constant.Kind == TypedConstantKind.Array && !constant.IsNull
                    ? "["
                        + string.Join(
                            ",",
                            constant.Values.Select(item => ApiConstant(item, identity))
                        )
                        + "]"
                    : Constant(constant, identity);
            return SourceType(constant.Type) + ":" + value;
        }

        private static void CaptureAttributeUsage(
            INamedTypeSymbol type,
            string identity,
            CSharpCompilation compilation,
            SortedSet<string> entries
        )
        {
            AttributeData usage = null;
            bool isAttribute = false;
            for (INamedTypeSymbol current = type; current != null; current = current.BaseType)
            {
                if (usage == null)
                {
                    usage = current
                        .GetAttributes()
                        .FirstOrDefault(attribute =>
                            string.Equals(
                                attribute.AttributeClass?.ToDisplayString(),
                                typeof(AttributeUsageAttribute).FullName,
                                StringComparison.Ordinal
                            )
                        );
                }
                isAttribute |= string.Equals(
                    current.ToDisplayString(),
                    typeof(Attribute).FullName,
                    StringComparison.Ordinal
                );
            }
            if (isAttribute)
            {
                string record =
                    usage == null
                        ? $"{typeof(AttributeUsageAttribute).FullName} constructor=(global::System.AttributeTargets) (global::System.AttributeTargets:{(int)AttributeTargets.All}) AllowMultiple=bool:false,Inherited=bool:true"
                        : ApiAttribute(usage, identity, compilation);
                entries.Add($"api-attribute {identity} {record} count=1");
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
                AssertBoundType(type, identity);
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
