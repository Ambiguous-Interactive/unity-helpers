// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.SyntaxPolicy
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text.Json;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal static class UnsafeAccessPolicy
    {
        private static readonly HashSet<string> AccessMembers = new(StringComparer.Ordinal)
        {
            "As",
            "AsRef",
            "AsPointer",
            "Unbox",
            "NullRef",
            "SkipInit",
            "Add",
            "Subtract",
            "AddByteOffset",
            "SubtractByteOffset",
            "Read",
            "ReadUnaligned",
            "Write",
            "WriteUnaligned",
            "Copy",
            "CopyBlock",
            "CopyBlockUnaligned",
            "InitBlock",
            "InitBlockUnaligned",
        };

        internal static int Run()
        {
            using JsonDocument document = JsonDocument.Parse(Console.In.ReadToEnd());
            List<string> failures = new();
            int inspected = 0;
            foreach (JsonElement source in document.RootElement.EnumerateArray())
            {
                string path = source.GetProperty(nameof(StackAllocationSource.Path)).GetString();
                string text = source.GetProperty(nameof(StackAllocationSource.Text)).GetString();
                Dictionary<int, int> accesses = new();
                Dictionary<int, int> imports = new();
                foreach (SyntaxTree tree in SafetySyntaxTrees.Parse(path, text))
                {
                    foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                    {
                        if (
                            node is UsingDirectiveSyntax directive
                            && (
                                directive.Alias != null
                                || directive.StaticKeyword.IsKind(SyntaxKind.StaticKeyword)
                            )
                            && string.Equals(
                                SafetySyntaxTrees.LastName(directive.Name),
                                "Unsafe",
                                StringComparison.Ordinal
                            )
                        )
                        {
                            imports[directive.SpanStart] =
                                tree.GetLineSpan(directive.Span).StartLinePosition.Line + 1;
                        }
                        if (
                            node is MemberAccessExpressionSyntax member
                            && string.Equals(
                                SafetySyntaxTrees.LastName(member.Expression),
                                "Unsafe",
                                StringComparison.Ordinal
                            )
                            && AccessMembers.Contains(member.Name.Identifier.ValueText)
                        )
                        {
                            accesses[member.SpanStart] =
                                tree.GetLineSpan(member.Span).StartLinePosition.Line + 1;
                        }
                    }
                }
                foreach (int line in imports.Values)
                {
                    failures.Add(
                        path
                            + ":"
                            + line
                            + ": aliases and static imports hide Unsafe member names. Use explicit member names so the memory-access rule can inspect them, see issue #637."
                    );
                }
                inspected += accesses.Count;
                if (accesses.Count != 0)
                {
                    int firstLine = int.MaxValue;
                    foreach (int line in accesses.Values)
                    {
                        firstLine = Math.Min(firstLine, line);
                    }
                    failures.Add(
                        path
                            + ":"
                            + firstLine
                            + ": "
                            + accesses.Count
                            + " memory-access Unsafe member(s). Reinterpreting memory is undefined behaviour on IL2CPP if the type, layout or index assumption is wrong; use a checked cast, see issue #637."
                    );
                }
            }
            failures.Sort(StringComparer.Ordinal);
            using Utf8JsonWriter writer = new(Console.OpenStandardOutput());
            writer.WriteStartObject();
            writer.WriteStartArray("Failures");
            foreach (string failure in failures)
            {
                writer.WriteStringValue(failure);
            }
            writer.WriteEndArray();
            writer.WriteNumber("Inspected", inspected);
            writer.WriteEndObject();
            return 0;
        }
    }
}
