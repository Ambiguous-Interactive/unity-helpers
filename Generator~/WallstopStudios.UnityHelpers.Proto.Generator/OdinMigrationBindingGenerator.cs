// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Text;

    /// <summary>Captures compiler-resolved dictionary declarations for editor migrations.</summary>
    [Generator]
    public sealed class OdinMigrationBindingGenerator : ISourceGenerator
    {
        private const string BindingNamespace =
            "WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration";
        private const string Registry =
            "global::" + BindingNamespace + ".OdinMigrationCompiledBindings";
        private const string Binding =
            "global::" + BindingNamespace + ".OdinMigrationCompiledBinding";
        private const string Source =
            "global::" + BindingNamespace + ".OdinMigrationCompiledSource";
        private const string Field = "global::" + BindingNamespace + ".OdinMigrationCompiledField";

        private static string Literal(string value)
        {
            return SymbolDisplay.FormatLiteral(value, true);
        }

        private static string MetadataName(INamedTypeSymbol type)
        {
            return type.ContainingType != null
                ? MetadataName(type.ContainingType) + "+" + type.MetadataName
                : (
                    type.ContainingNamespace.IsGlobalNamespace
                        ? string.Empty
                        : type.ContainingNamespace.ToDisplayString() + "."
                ) + type.MetadataName;
        }

        private static bool IsUnityObject(ITypeSymbol type)
        {
            for (
                INamedTypeSymbol current = type as INamedTypeSymbol;
                current != null;
                current = current.BaseType
            )
            {
                if (
                    string.Equals(
                        MetadataName(current),
                        "UnityEngine.Object",
                        StringComparison.Ordinal
                    )
                )
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryDictionary(
            ITypeSymbol type,
            out INamedTypeSymbol key,
            out INamedTypeSymbol element
        )
        {
            key = null;
            element = null;
            if (
                !(type is INamedTypeSymbol dictionary)
                || !string.Equals(
                    MetadataName(dictionary.OriginalDefinition),
                    "System.Collections.Generic.Dictionary`2",
                    StringComparison.Ordinal
                )
                || !(dictionary.TypeArguments[0] is INamedTypeSymbol enumType)
                || enumType.TypeKind != TypeKind.Enum
                || enumType.EnumUnderlyingType.SpecialType != SpecialType.System_Int32
                || !(dictionary.TypeArguments[1] is INamedTypeSymbol list)
                || !string.Equals(
                    MetadataName(list.OriginalDefinition),
                    "System.Collections.Generic.List`1",
                    StringComparison.Ordinal
                )
                || !(list.TypeArguments[0] is INamedTypeSymbol objectType)
                || !IsUnityObject(objectType)
            )
            {
                return false;
            }
            key = enumType;
            element = objectType;
            return true;
        }

        private static string Checksum(SyntaxTree tree)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return Convert.ToBase64String(
                    hash.ComputeHash(Encoding.UTF8.GetBytes(tree.GetText().ToString()))
                );
            }
        }

        private static void CaptureDeclarations(
            INamedTypeSymbol type,
            SortedDictionary<string, SyntaxTree> sources
        )
        {
            for (INamedTypeSymbol current = type; current != null; current = current.ContainingType)
            {
                foreach (SyntaxReference part in current.DeclaringSyntaxReferences)
                {
                    sources[part.SyntaxTree.FilePath] = part.SyntaxTree;
                }
            }
        }

        /// <inheritdoc />
        public void Initialize(GeneratorInitializationContext context) { }

        /// <inheritdoc />
        public void Execute(GeneratorExecutionContext context)
        {
            if (
                context.Compilation.GetTypeByMetadataName(
                    BindingNamespace + ".OdinMigrationCompiledBindings"
                ) == null
                || context.Compilation.GetTypeByMetadataName(
                    "UnityEditor.InitializeOnLoadMethodAttribute"
                ) == null
            )
            {
                return;
            }
            bool editor = false;
            foreach (string symbol in context.ParseOptions.PreprocessorSymbolNames)
            {
                if (string.Equals(symbol, "UNITY_EDITOR", StringComparison.Ordinal))
                {
                    editor = true;
                    break;
                }
            }
            if (!editor)
            {
                return;
            }
            HashSet<INamedTypeSymbol> visited = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            SortedDictionary<string, string> registrations = new SortedDictionary<string, string>(
                StringComparer.Ordinal
            );
            foreach (SyntaxTree tree in context.Compilation.SyntaxTrees)
            {
                SemanticModel model = null;
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    if (!(node is ClassDeclarationSyntax declaration))
                    {
                        continue;
                    }
                    model = model ?? context.Compilation.GetSemanticModel(tree);
                    INamedTypeSymbol type = model.GetDeclaredSymbol(declaration);
                    if (
                        type == null
                        || !visited.Add(type)
                        || type.Arity != 0
                        || !IsUnityObject(type)
                        || !TypeNaming.IsNameable(type, context.Compilation)
                    )
                    {
                        continue;
                    }
                    List<string> fields = new List<string>();
                    SortedDictionary<string, SyntaxTree> sources = new SortedDictionary<
                        string,
                        SyntaxTree
                    >(StringComparer.Ordinal);
                    CaptureDeclarations(type, sources);
                    for (
                        INamedTypeSymbol owner = type;
                        owner != null && IsUnityObject(owner);
                        owner = owner.BaseType
                    )
                    {
                        CaptureDeclarations(owner, sources);
                        foreach (ISymbol member in owner.GetMembers())
                        {
                            if (
                                !(member is IFieldSymbol field)
                                || field.IsStatic
                                || field.IsImplicitlyDeclared
                                || !TryDictionary(
                                    field.Type,
                                    out INamedTypeSymbol key,
                                    out INamedTypeSymbol element
                                )
                                || field.DeclaringSyntaxReferences.Length != 1
                                || !(
                                    field.DeclaringSyntaxReferences[0].GetSyntax()
                                    is VariableDeclaratorSyntax variable
                                )
                                || !(variable.Parent is VariableDeclarationSyntax original)
                            )
                            {
                                continue;
                            }
                            SyntaxTree fieldTree = original.SyntaxTree;
                            sources[fieldTree.FilePath] = fieldTree;
                            CaptureDeclarations(key, sources);
                            for (
                                INamedTypeSymbol objectOwner = element;
                                objectOwner != null;
                                objectOwner = objectOwner.BaseType
                            )
                            {
                                CaptureDeclarations(objectOwner, sources);
                            }
                            fields.Add(
                                "new "
                                    + Field
                                    + "("
                                    + Literal(field.Name)
                                    + ", "
                                    + Literal(MetadataName(key))
                                    + ", "
                                    + Literal(MetadataName(element))
                                    + ", "
                                    + Literal(key.ContainingAssembly.Identity.ToString())
                                    + ", "
                                    + Literal(element.ContainingAssembly.Identity.ToString())
                                    + ", "
                                    + Literal(fieldTree.FilePath)
                                    + ", "
                                    + original.Type.Span.Start.ToString(
                                        CultureInfo.InvariantCulture
                                    )
                                    + ", "
                                    + original.Type.Span.Length.ToString(
                                        CultureInfo.InvariantCulture
                                    )
                                    + ")"
                            );
                        }
                    }
                    if (fields.Count == 0)
                    {
                        continue;
                    }
                    StringBuilder sourceArray = new StringBuilder("new " + Source + "[] {");
                    foreach (KeyValuePair<string, SyntaxTree> source in sources)
                    {
                        sourceArray
                            .Append("new ")
                            .Append(Source)
                            .Append('(')
                            .Append(Literal(source.Key))
                            .Append(", ")
                            .Append(Literal(Checksum(source.Value)))
                            .Append("),");
                    }
                    sourceArray.Append('}');
                    string qualified = type.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    registrations[qualified] =
                        Registry
                        + ".Register(new "
                        + Binding
                        + "(typeof("
                        + qualified
                        + "), "
                        + sourceArray
                        + ", new "
                        + Field
                        + "[] {"
                        + string.Join(",", fields)
                        + "}));";
                }
            }
            if (registrations.Count == 0)
            {
                return;
            }
            Writer writer = new Writer();
            writer.Line("// <auto-generated />");
            writer.Line("#pragma warning disable");
            writer.Line("namespace WallstopStudios.UnityHelpers.Generated" + Writer.Open);
            writer.Indent();
            writer.Line("internal static class OdinMigrationGeneratedBindings" + Writer.Open);
            writer.Indent();
            writer.Line("[global::UnityEditor.InitializeOnLoadMethod]");
            writer.Line("internal static void Register()" + Writer.Open);
            writer.Indent();
            foreach (string registration in registrations.Values)
            {
                writer.Line(registration);
            }
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            context.AddSource(
                "OdinMigrationGeneratedBindings.g.cs",
                SourceText.From(writer.ToString(), Encoding.UTF8)
            );
        }
    }
}
