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
    using Microsoft.CodeAnalysis.Operations;

    internal static class StackAllocationPolicy
    {
        private const int MaximumConditionalSymbols = 12;
        private const int MaximumCachedContexts = 8;

        internal static int Run()
        {
            using JsonDocument document = JsonDocument.Parse(Console.In.ReadToEnd());
            List<StackAllocationSource> sources = new();
            foreach (JsonElement item in document.RootElement.EnumerateArray())
            {
                sources.Add(
                    new StackAllocationSource(
                        item.GetProperty(nameof(StackAllocationSource.Path)).GetString(),
                        item.GetProperty(nameof(StackAllocationSource.Text)).GetString()
                    )
                );
            }
            (List<string> Failures, int Inspected) scan = Analyze(sources);
            using Utf8JsonWriter writer = new(Console.OpenStandardOutput());
            writer.WriteStartObject();
            writer.WriteStartArray(nameof(scan.Failures));
            foreach (string failure in scan.Failures)
            {
                writer.WriteStringValue(failure);
            }
            writer.WriteEndArray();
            writer.WriteNumber(nameof(scan.Inspected), scan.Inspected);
            writer.WriteEndObject();
            return 0;
        }

        private static (List<string> Failures, int Inspected) Analyze(
            List<StackAllocationSource> sources
        )
        {
            SortedSet<string> allSymbols = new(StringComparer.Ordinal);
            Dictionary<string, StackAllocationSource> byPath = new(StringComparer.Ordinal);
            foreach (StackAllocationSource source in sources)
            {
                byPath.Add(source.Path, source);
                allSymbols.UnionWith(source.Symbols);
            }
            Dictionary<string, SyntaxTree> baselineTrees = new(StringComparer.Ordinal);
            foreach (StackAllocationSource source in sources)
            {
                baselineTrees.Add(source.Path, Parse(source, allSymbols));
            }
            CSharpCompilation baseline = CSharpCompilation.Create(
                nameof(StackAllocationPolicy),
                baselineTrees.Values,
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    concurrentBuild: false
                )
            );
            Dictionary<
                string,
                (CSharpCompilation Compilation, Dictionary<string, SyntaxTree> Trees)
            > contexts = new(StringComparer.Ordinal);
            Dictionary<string, string> findings = new(StringComparer.Ordinal);
            HashSet<string> inspected = new(StringComparer.Ordinal);
            foreach (StackAllocationSource source in sources)
            {
                if (!source.Text.Contains("stackalloc", StringComparison.Ordinal))
                {
                    continue;
                }
                HashSet<string> relevant = new(source.Symbols, StringComparer.Ordinal);
                bool expanded;
                do
                {
                    expanded = false;
                    List<string> symbols = new(relevant);
                    symbols.Sort(StringComparer.Ordinal);
                    if (MaximumConditionalSymbols < symbols.Count)
                    {
                        throw new InvalidOperationException(
                            "Stack allocation has more than twelve dependent conditional symbols: "
                                + source.Path
                        );
                    }
                    for (int combination = 0; combination < (1 << symbols.Count); ++combination)
                    {
                        HashSet<string> disabled = new(StringComparer.Ordinal);
                        for (int index = 0; index < symbols.Count; ++index)
                        {
                            if ((combination & (1 << index)) == 0)
                            {
                                disabled.Add(symbols[index]);
                            }
                        }
                        SortedSet<string> enabled = new(allSymbols, StringComparer.Ordinal);
                        enabled.ExceptWith(disabled);
                        string contextKey = string.Join(";", enabled);
                        if (
                            !contexts.TryGetValue(
                                contextKey,
                                out (
                                    CSharpCompilation Compilation,
                                    Dictionary<string, SyntaxTree> Trees
                                ) context
                            )
                        )
                        {
                            CSharpCompilation compilation = baseline;
                            Dictionary<string, SyntaxTree> trees = new(
                                baselineTrees,
                                StringComparer.Ordinal
                            );
                            foreach (StackAllocationSource candidate in sources)
                            {
                                if (!Overlaps(candidate.Symbols, disabled))
                                {
                                    continue;
                                }
                                SyntaxTree tree = Parse(candidate, enabled);
                                if (
                                    !baselineTrees.TryGetValue(candidate.Path, out SyntaxTree prior)
                                )
                                {
                                    throw new InvalidOperationException(
                                        "Missing source tree: " + candidate.Path
                                    );
                                }
                                compilation = compilation.ReplaceSyntaxTree(prior, tree);
                                trees[candidate.Path] = tree;
                            }
                            context = (compilation, trees);
                            if (MaximumCachedContexts <= contexts.Count)
                            {
                                contexts.Clear();
                            }
                            contexts.Add(contextKey, context);
                        }
                        if (!context.Trees.TryGetValue(source.Path, out SyntaxTree subject))
                        {
                            throw new InvalidOperationException(
                                "Missing source tree: " + source.Path
                            );
                        }
                        SemanticModel model = context.Compilation.GetSemanticModel(subject);
                        int priorSymbols = relevant.Count;
                        foreach (SyntaxNode node in subject.GetRoot().DescendantNodes())
                        {
                            ExpressionSyntax allocation;
                            ExpressionSyntax length;
                            bool implicitLength;
                            if (node is StackAllocArrayCreationExpressionSyntax explicitAllocation)
                            {
                                allocation = explicitAllocation;
                                length =
                                    explicitAllocation.Type is ArrayTypeSyntax array
                                    && array.RankSpecifiers.Count == 1
                                    && array.RankSpecifiers[0].Sizes.Count == 1
                                        ? array.RankSpecifiers[0].Sizes[0]
                                        : null;
                                implicitLength =
                                    length is OmittedArraySizeExpressionSyntax
                                    && explicitAllocation.Initializer != null;
                            }
                            else if (
                                node
                                is ImplicitStackAllocArrayCreationExpressionSyntax implicitAllocation
                            )
                            {
                                allocation = implicitAllocation;
                                length = null;
                                implicitLength = true;
                            }
                            else
                            {
                                continue;
                            }
                            string key = source.Path + ":" + allocation.SpanStart;
                            inspected.Add(key);
                            bool bounded =
                                !allocation.ContainsDiagnostics
                                && (
                                    implicitLength
                                    || (
                                        length != null
                                        && IsBounded(allocation, length, model, byPath, relevant)
                                    )
                                );
                            if (!bounded)
                            {
                                int line =
                                    subject.GetLineSpan(allocation.Span).StartLinePosition.Line + 1;
                                string expression =
                                    length == null
                                        ? "invalid length"
                                        : length.ToString().Replace('\r', ' ').Replace('\n', ' ');
                                findings[key] =
                                    source.Path
                                    + ":"
                                    + line
                                    + ": stackalloc of '"
                                    + expression
                                    + "', which no compiler-bound constant bounds. Use a direct ternary with a constant bound and rent above it; see issue #637.";
                            }
                        }
                        expanded |= priorSymbols != relevant.Count;
                    }
                } while (expanded);
            }
            List<string> failures = new(findings.Values);
            failures.Sort(StringComparer.Ordinal);
            return (failures, inspected.Count);
        }

        private static SyntaxTree Parse(StackAllocationSource source, IEnumerable<string> symbols)
        {
            return CSharpSyntaxTree.ParseText(
                source.Text,
                new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: symbols),
                source.Path
            );
        }

        private static bool Overlaps(List<string> symbols, HashSet<string> disabled)
        {
            foreach (string symbol in symbols)
            {
                if (disabled.Contains(symbol))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsBounded(
            ExpressionSyntax allocation,
            ExpressionSyntax length,
            SemanticModel model,
            Dictionary<string, StackAllocationSource> sources,
            HashSet<string> relevant
        )
        {
            TrackDependencies(length, model, sources, relevant);
            if (IsConstantInteger(length, model))
            {
                return true;
            }
            ExpressionSyntax count = Unwrap(length);
            ISymbol countSymbol = model.GetSymbolInfo(count).Symbol;
            if (
                count is not IdentifierNameSyntax
                || (countSymbol is not ILocalSymbol && countSymbol is not IParameterSymbol)
                || !IsIntegerType(model.GetTypeInfo(count).Type)
                || (countSymbol is IParameterSymbol parameter && parameter.RefKind != RefKind.None)
                || (countSymbol is ILocalSymbol local && local.RefKind != RefKind.None)
            )
            {
                return false;
            }
            ExpressionSyntax branch = allocation;
            while (branch.Parent is ParenthesizedExpressionSyntax parenthesized)
            {
                branch = parenthesized;
            }
            if (branch.Parent is not ConditionalExpressionSyntax conditional)
            {
                return false;
            }
            ExpressionSyntax assigned = conditional;
            while (assigned.Parent is ParenthesizedExpressionSyntax parenthesized)
            {
                assigned = parenthesized;
            }
            if (
                assigned.Parent is not EqualsValueClauseSyntax
                && !(
                    assigned.Parent is AssignmentExpressionSyntax assignment
                    && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)
                )
            )
            {
                return false;
            }
            if (
                Unwrap(conditional.Condition) is not BinaryExpressionSyntax comparison
                || (model.GetOperation(comparison) is not IBinaryOperation operation)
                || operation.OperatorMethod != null
            )
            {
                return false;
            }
            bool upperWhenTrue;
            ExpressionSyntax bound;
            if (
                SymbolEqualityComparer.Default.Equals(
                    countSymbol,
                    model.GetSymbolInfo(Unwrap(comparison.Left)).Symbol
                )
            )
            {
                upperWhenTrue =
                    comparison.IsKind(SyntaxKind.LessThanExpression)
                    || comparison.IsKind(SyntaxKind.LessThanOrEqualExpression);
                bound = comparison.Right;
            }
            else if (
                SymbolEqualityComparer.Default.Equals(
                    countSymbol,
                    model.GetSymbolInfo(Unwrap(comparison.Right)).Symbol
                )
            )
            {
                upperWhenTrue =
                    comparison.IsKind(SyntaxKind.GreaterThanExpression)
                    || comparison.IsKind(SyntaxKind.GreaterThanOrEqualExpression);
                bound = comparison.Left;
            }
            else
            {
                return false;
            }
            if (
                !comparison.IsKind(SyntaxKind.LessThanExpression)
                && !comparison.IsKind(SyntaxKind.LessThanOrEqualExpression)
                && !comparison.IsKind(SyntaxKind.GreaterThanExpression)
                && !comparison.IsKind(SyntaxKind.GreaterThanOrEqualExpression)
            )
            {
                return false;
            }
            TrackDependencies(bound, model, sources, relevant);
            return IsConstantInteger(bound, model)
                && upperWhenTrue == ReferenceEquals(branch, conditional.WhenTrue);
        }

        private static bool IsConstantInteger(ExpressionSyntax expression, SemanticModel model)
        {
            Optional<object> constant = model.GetConstantValue(expression);
            return constant.HasValue
                && constant.Value
                    is sbyte
                        or byte
                        or short
                        or ushort
                        or int
                        or uint
                        or long
                        or ulong
                        or char;
        }

        private static bool IsIntegerType(ITypeSymbol type)
        {
            return type?.SpecialType
                is SpecialType.System_SByte
                    or SpecialType.System_Byte
                    or SpecialType.System_Int16
                    or SpecialType.System_UInt16
                    or SpecialType.System_Int32
                    or SpecialType.System_UInt32
                    or SpecialType.System_Int64
                    or SpecialType.System_UInt64
                    or SpecialType.System_Char;
        }

        private static ExpressionSyntax Unwrap(ExpressionSyntax expression)
        {
            while (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }
            return expression;
        }

        private static void TrackDependencies(
            ExpressionSyntax expression,
            SemanticModel model,
            Dictionary<string, StackAllocationSource> sources,
            HashSet<string> relevant
        )
        {
            INamedTypeSymbol enclosing = model
                .GetEnclosingSymbol(expression.SpanStart)
                ?.ContainingType;
            foreach (SyntaxNode node in expression.DescendantNodesAndSelf())
            {
                if (node is not ExpressionSyntax candidate)
                {
                    continue;
                }
                ISymbol symbol = model.GetSymbolInfo(candidate).Symbol;
                if (symbol == null)
                {
                    continue;
                }
                TrackDeclarations(symbol, sources, relevant);
                if (symbol is INamedTypeSymbol namedType)
                {
                    TrackType(namedType, sources, relevant);
                }
                if (candidate is MemberAccessExpressionSyntax access)
                {
                    TrackType(
                        model.GetTypeInfo(access.Expression).Type as INamedTypeSymbol,
                        sources,
                        relevant
                    );
                }
                if (
                    candidate is IdentifierNameSyntax identifier
                    && (symbol is IFieldSymbol || symbol is IPropertySymbol)
                    && !(
                        identifier.Parent is MemberAccessExpressionSyntax qualified
                        && ReferenceEquals(qualified.Name, identifier)
                    )
                    && !SymbolEqualityComparer.Default.Equals(symbol.ContainingType, enclosing)
                )
                {
                    for (
                        INamedTypeSymbol scope = enclosing;
                        scope != null;
                        scope = scope.ContainingType
                    )
                    {
                        TrackType(scope, sources, relevant);
                    }
                }
            }
        }

        private static void TrackType(
            INamedTypeSymbol type,
            Dictionary<string, StackAllocationSource> sources,
            HashSet<string> relevant
        )
        {
            HashSet<ISymbol> visited = new(SymbolEqualityComparer.Default);
            while (type != null && visited.Add(type))
            {
                TrackDeclarations(type, sources, relevant);
                type = type.BaseType;
            }
        }

        private static void TrackDeclarations(
            ISymbol symbol,
            Dictionary<string, StackAllocationSource> sources,
            HashSet<string> relevant
        )
        {
            foreach (SyntaxReference declaration in symbol.DeclaringSyntaxReferences)
            {
                if (
                    sources.TryGetValue(
                        declaration.SyntaxTree.FilePath,
                        out StackAllocationSource source
                    )
                )
                {
                    relevant.UnionWith(source.Symbols);
                }
            }
        }
    }
}
