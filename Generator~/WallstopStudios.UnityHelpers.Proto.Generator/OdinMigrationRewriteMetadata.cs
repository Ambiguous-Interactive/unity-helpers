// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal static class OdinMigrationRewriteMetadata
    {
        private const string EditType =
            "global::WallstopStudios.UnityHelpers.Core.Serialization.OdinMigration.OdinMigrationCompiledSourceEdit";
        private const string DictionaryType =
            "global::WallstopStudios.UnityHelpers.Core.DataStructure.Adapters.SerializableDictionary";

        internal static string ArrayExpression(IReadOnlyList<string> edits)
        {
            return "new " + EditType + "[] {" + string.Join(",", edits) + "}";
        }

        internal static List<string> FieldEdits(
            IFieldSymbol field,
            VariableDeclaratorSyntax variable,
            Compilation compilation,
            out string failure
        )
        {
            List<string> edits = new List<string>();
            failure = null;
            VariableDeclarationSyntax declaration = (VariableDeclarationSyntax)variable.Parent;
            FieldDeclarationSyntax member = declaration.Parent as FieldDeclarationSyntax;
            INamedTypeSymbol original = field.OriginalDefinition.Type as INamedTypeSymbol;
            if (member == null || original == null || original.TypeArguments.Length != 2)
            {
                failure = "The original dictionary declaration cannot be rewritten.";
                return edits;
            }
            string replacement =
                DictionaryType
                + "<"
                + original
                    .TypeArguments[0]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                + ", "
                + original
                    .TypeArguments[1]
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                + ">";
            edits.Add(Edit(declaration.Type, replacement));
            bool serialized = false;
            foreach (AttributeData attribute in field.GetAttributes())
            {
                if (
                    string.Equals(
                        attribute.AttributeClass?.ToDisplayString(),
                        "UnityEngine.SerializeField",
                        StringComparison.Ordinal
                    )
                )
                {
                    serialized = true;
                }
            }
            foreach (AttributeData attribute in field.GetAttributes())
            {
                string name = attribute.AttributeClass?.ToDisplayString();
                if (
                    string.Equals(name, "System.NonSerializedAttribute", StringComparison.Ordinal)
                    || string.Equals(
                        name,
                        "UnityEngine.SerializeReference",
                        StringComparison.Ordinal
                    )
                )
                {
                    failure =
                        "NonSerialized and SerializeReference fields require a separate source migration.";
                    return edits;
                }
                if (string.Equals(name, "UnityEngine.SerializeField", StringComparison.Ordinal))
                {
                    serialized = true;
                }
                if (
                    !string.Equals(
                        name,
                        "Sirenix.Serialization.OdinSerializeAttribute",
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }
                if (
                    !(attribute.ApplicationSyntaxReference?.GetSyntax() is AttributeSyntax syntax)
                    || syntax.ArgumentList != null && syntax.ArgumentList.Arguments.Count != 0
                )
                {
                    failure = "An OdinSerialize attribute argument requires review.";
                    return edits;
                }
                if (serialized)
                {
                    edits.Add(RemoveAttribute(syntax));
                }
                else
                {
                    edits.Add(Edit(syntax, "global::UnityEngine.SerializeField"));
                }
                serialized = true;
            }
            bool addSerializationAttribute = !serialized;
            foreach (SyntaxToken modifier in member.Modifiers)
            {
                if (modifier.IsKind(SyntaxKind.ReadOnlyKeyword))
                {
                    string modifierReplacement = string.Empty;
                    if (addSerializationAttribute && modifier.SpanStart == member.SpanStart)
                    {
                        modifierReplacement = "[global::UnityEngine.SerializeField] ";
                        addSerializationAttribute = false;
                    }
                    edits.Add(
                        Edit(
                            member.SyntaxTree.FilePath,
                            modifier.SpanStart,
                            modifier.Span.Length,
                            modifierReplacement
                        )
                    );
                }
            }
            if (addSerializationAttribute)
            {
                if (member.SpanStart == declaration.Type.SpanStart)
                {
                    edits[0] = Edit(
                        declaration.Type,
                        "[global::UnityEngine.SerializeField] " + replacement
                    );
                }
                else
                {
                    edits.Add(
                        Edit(
                            member.SyntaxTree.FilePath,
                            member.SpanStart,
                            0,
                            "[global::UnityEngine.SerializeField] "
                        )
                    );
                }
            }
            if (variable.Initializer == null)
            {
                return edits;
            }
            ExpressionSyntax value = variable.Initializer.Value;
            SemanticModel model = compilation.GetSemanticModel(variable.SyntaxTree);
            if (
                !(model.GetSymbolInfo(value).Symbol is IMethodSymbol constructor)
                || !SymbolEqualityComparer.Default.Equals(constructor.ContainingType, original)
            )
            {
                failure =
                    "A dictionary initializer must construct the original dictionary directly.";
                return edits;
            }
            if (
                constructor.Parameters.Length != 0
                && (
                    constructor.Parameters.Length != 1
                    || !(constructor.Parameters[0].Type is INamedTypeSymbol parameter)
                    || !string.Equals(
                        parameter.OriginalDefinition.ToDisplayString(),
                        "System.Collections.Generic.IDictionary<TKey, TValue>",
                        StringComparison.Ordinal
                    )
                )
            )
            {
                failure =
                    "A dictionary initializer must use an empty or dictionary-copy constructor.";
                return edits;
            }
            if (value is ObjectCreationExpressionSyntax creation)
            {
                if (creation.ArgumentList != null && 1 < creation.ArgumentList.Arguments.Count)
                {
                    failure =
                        "A dictionary initializer with multiple constructor arguments requires review.";
                    return edits;
                }
                edits.Add(Edit(creation.Type, replacement));
            }
            else if (!(value is ImplicitObjectCreationExpressionSyntax))
            {
                failure =
                    "A dictionary initializer must construct the original dictionary directly.";
            }
            return edits;
        }

        internal static void CaptureBaseEdits(
            INamedTypeSymbol owner,
            Compilation compilation,
            List<string> edits
        )
        {
            string baseName = owner.BaseType?.ToDisplayString();
            string replacement =
                string.Equals(
                    baseName,
                    "Sirenix.OdinInspector.SerializedMonoBehaviour",
                    StringComparison.Ordinal
                )
                    ? "global::UnityEngine.MonoBehaviour"
                : string.Equals(
                    baseName,
                    "Sirenix.OdinInspector.SerializedScriptableObject",
                    StringComparison.Ordinal
                )
                    ? "global::UnityEngine.ScriptableObject"
                : null;
            if (replacement == null)
            {
                return;
            }
            foreach (SyntaxReference part in owner.DeclaringSyntaxReferences)
            {
                if (
                    !(part.GetSyntax() is ClassDeclarationSyntax declaration)
                    || declaration.BaseList == null
                )
                {
                    continue;
                }
                SemanticModel model = compilation.GetSemanticModel(part.SyntaxTree);
                foreach (BaseTypeSyntax original in declaration.BaseList.Types)
                {
                    if (
                        SymbolEqualityComparer.Default.Equals(
                            model.GetTypeInfo(original.Type).Type,
                            owner.BaseType
                        )
                    )
                    {
                        edits.Add(Edit(original.Type, replacement));
                    }
                }
            }
        }

        private static string Edit(SyntaxNode node, string replacement)
        {
            return Edit(node.SyntaxTree.FilePath, node.SpanStart, node.Span.Length, replacement);
        }

        private static string RemoveAttribute(AttributeSyntax attribute)
        {
            AttributeListSyntax list = (AttributeListSyntax)attribute.Parent;
            if (list.Attributes.Count == 1)
            {
                return Edit(list, string.Empty);
            }
            int index = list.Attributes.IndexOf(attribute);
            SyntaxToken separator =
                index == 0
                    ? list.Attributes.GetSeparator(0)
                    : list.Attributes.GetSeparator(index - 1);
            int start = index == 0 ? attribute.SpanStart : separator.SpanStart;
            int end = index == 0 ? separator.Span.End : attribute.Span.End;
            return Edit(attribute.SyntaxTree.FilePath, start, end - start, string.Empty);
        }

        private static string Edit(string sourcePath, int start, int length, string replacement)
        {
            return "new "
                + EditType
                + "("
                + SymbolDisplay.FormatLiteral(sourcePath, true)
                + ", "
                + start.ToString(CultureInfo.InvariantCulture)
                + ", "
                + length.ToString(CultureInfo.InvariantCulture)
                + ", "
                + SymbolDisplay.FormatLiteral(replacement, true)
                + ")";
        }
    }
}
