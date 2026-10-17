// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.SyntaxPolicy
{
    using System;
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal static class SafetySyntaxTrees
    {
        private const int MaximumConditionalSymbols = 12;

        internal static IEnumerable<SyntaxTree> Parse(string path, string text)
        {
            List<string> symbols = Program.FindSymbols(text);
            if (MaximumConditionalSymbols < symbols.Count)
            {
                throw new InvalidOperationException(
                    "Safety source has more than twelve conditional symbols: " + path
                );
            }
            for (int combination = 0; combination < (1 << symbols.Count); ++combination)
            {
                List<string> enabled = new();
                for (int index = 0; index < symbols.Count; ++index)
                {
                    if ((combination & (1 << index)) != 0)
                    {
                        enabled.Add(symbols[index]);
                    }
                }
                yield return CSharpSyntaxTree.ParseText(
                    text,
                    new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: enabled),
                    path
                );
            }
        }

        internal static string LastName(SyntaxNode node)
        {
            switch (node)
            {
                case SimpleNameSyntax simple:
                    return simple.Identifier.ValueText;
                case QualifiedNameSyntax qualified:
                    return qualified.Right.Identifier.ValueText;
                case AliasQualifiedNameSyntax alias:
                    return alias.Name.Identifier.ValueText;
                case MemberAccessExpressionSyntax member:
                    return member.Name.Identifier.ValueText;
                default:
                    return null;
            }
        }
    }
}
