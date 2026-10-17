// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.SyntaxPolicy
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal static class Il2CppCodePolicy
    {
        internal static int Run()
        {
            using JsonDocument document = JsonDocument.Parse(Console.In.ReadToEnd());
            using Utf8JsonWriter writer = new(Console.OpenStandardOutput());
            writer.WriteStartObject();
            writer.WriteStartArray("Variants");
            foreach (JsonElement source in document.RootElement.EnumerateArray())
            {
                string path = source.GetProperty(nameof(StackAllocationSource.Path)).GetString();
                string text = source.GetProperty(nameof(StackAllocationSource.Text)).GetString();
                foreach (SyntaxTree tree in SafetySyntaxTrees.Parse(path, text))
                {
                    writer.WriteStartObject();
                    writer.WriteString(nameof(StackAllocationSource.Path), path);
                    writer.WriteString(nameof(StackAllocationSource.Text), Normalize(tree, text));
                    writer.WriteStartArray("SetOptionStarts");
                    foreach (int start in FindSetOptionStarts(tree))
                    {
                        writer.WriteNumberValue(start);
                    }
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
            return 0;
        }

        private static SortedSet<int> FindSetOptionStarts(SyntaxTree tree)
        {
            HashSet<string> names = new(StringComparer.Ordinal)
            {
                "Il2CppSetOption",
                "Il2CppSetOptionAttribute",
            };
            SyntaxNode root = tree.GetRoot();
            foreach (SyntaxNode node in root.DescendantNodes())
            {
                if (
                    node is UsingDirectiveSyntax directive
                    && directive.Alias != null
                    && names.Contains(SafetySyntaxTrees.LastName(directive.Name))
                )
                {
                    string alias = directive.Alias.Name.Identifier.ValueText;
                    names.Add(alias);
                    if (
                        alias.EndsWith("Attribute", StringComparison.Ordinal)
                        && "Attribute".Length < alias.Length
                    )
                    {
                        names.Add(alias.Substring(0, alias.Length - "Attribute".Length));
                    }
                }
            }
            SortedSet<int> starts = new();
            foreach (SyntaxNode node in root.DescendantNodes())
            {
                if (
                    node is AttributeSyntax attribute
                    && attribute.ArgumentList != null
                    && names.Contains(SafetySyntaxTrees.LastName(attribute.Name))
                )
                {
                    starts.Add(attribute.ArgumentList.OpenParenToken.SpanStart);
                }
                else if (
                    node is ObjectCreationExpressionSyntax creation
                    && creation.ArgumentList != null
                    && names.Contains(SafetySyntaxTrees.LastName(creation.Type))
                )
                {
                    starts.Add(creation.ArgumentList.OpenParenToken.SpanStart);
                }
            }
            return starts;
        }

        private static string Normalize(SyntaxTree tree, string text)
        {
            char[] code = new char[text.Length];
            for (int index = 0; index < text.Length; ++index)
            {
                char character = text[index];
                code[index] = character == '\n' || character == '\r' ? character : ' ';
            }
            foreach (SyntaxToken token in tree.GetRoot().DescendantTokens())
            {
                if (
                    token.Parent is ConstructorDeclarationSyntax constructor
                        && token == constructor.Identifier
                    || token.Parent is MethodDeclarationSyntax method && token == method.Identifier
                )
                {
                    continue;
                }
                if (
                    token.Parent is LiteralExpressionSyntax literal
                    && (
                        literal.IsKind(SyntaxKind.StringLiteralExpression)
                        || literal.IsKind(SyntaxKind.Utf8StringLiteralExpression)
                        || literal.IsKind(SyntaxKind.CharacterLiteralExpression)
                    )
                )
                {
                    code[token.SpanStart] = '"';
                    code[token.SpanStart + 1] = '"';
                    continue;
                }
                if (token.Parent is InterpolatedStringExpressionSyntax interpolation)
                {
                    if (token == interpolation.StringStartToken)
                    {
                        code[token.SpanStart] = '"';
                        code[token.SpanStart + 1] = '"';
                    }
                    continue;
                }
                if (
                    token.Parent is InterpolatedStringTextSyntax
                    || token.Parent is InterpolationFormatClauseSyntax
                )
                {
                    continue;
                }
                string value = token.Text;
                if (token.IsKind(SyntaxKind.IdentifierToken))
                {
                    value = token.ValueText;
                    if (
                        token.Text.StartsWith("@", StringComparison.Ordinal)
                        || SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None
                    )
                    {
                        value = "@" + value;
                    }
                }
                if (token.Span.Length < value.Length)
                {
                    throw new InvalidOperationException(
                        "Normalized token exceeds its source span."
                    );
                }
                for (int index = 0; index < value.Length; ++index)
                {
                    code[token.SpanStart + index] = value[index];
                }
            }
            return new string(code);
        }
    }
}
