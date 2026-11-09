// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>Reports immediate clears of fresh collections from the package's clearing pools.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RedundantPooledClearAnalyzer : DiagnosticAnalyzer
    {
        private const string PoolNamespace = "WallstopStudios.UnityHelpers.Utils.";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.RedundantPooledClear);

        private static void Analyze(SyntaxNodeAnalysisContext context)
        {
            InvocationExpressionSyntax syntax = (InvocationExpressionSyntax)context.Node;
            if (
                syntax.ArgumentList.Arguments.Count != 0
                || syntax.Expression is not MemberAccessExpressionSyntax access
                || !string.Equals(
                    access.Name.Identifier.ValueText,
                    nameof(List<int>.Clear),
                    StringComparison.Ordinal
                )
                || syntax.Parent is not ExpressionStatementSyntax statement
                || context.SemanticModel.GetOperation(syntax, context.CancellationToken)
                    is not IInvocationOperation clear
            )
            {
                return;
            }
            bool leaseValue = false;
            IOperation receiver = clear.Instance;
            if (
                receiver is IPropertyReferenceOperation property
                && string.Equals(property.Property.Name, "Value", StringComparison.Ordinal)
                && IsType(property.Property.ContainingType, "PooledResource`1", context.Compilation)
            )
            {
                receiver = property.Instance;
                leaseValue = true;
            }
            if (receiver is not ILocalReferenceOperation target)
            {
                return;
            }
            SyntaxNode previous = PreviousRental(statement);
            ExpressionSyntax expression = previous as ExpressionSyntax;
            ILocalSymbol owner = null;
            if (
                previous is VariableDeclarationSyntax declaration
                && declaration.Variables.Count == 1
            )
            {
                VariableDeclaratorSyntax variable = declaration.Variables[0];
                expression = variable.Initializer?.Value;
                owner =
                    context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken)
                    as ILocalSymbol;
            }
            if (
                expression == null
                || context.SemanticModel.GetOperation(expression, context.CancellationToken)
                    is not IInvocationOperation rental
                || !IsClearingRental(rental, context.Compilation)
            )
            {
                return;
            }
            if (leaseValue)
            {
                if (!SymbolEqualityComparer.Default.Equals(owner, target.Local))
                {
                    return;
                }
            }
            else
            {
                bool matches = false;
                foreach (IArgumentOperation argument in rental.Arguments)
                {
                    if (argument.Parameter == null || argument.Parameter.RefKind != RefKind.Out)
                    {
                        continue;
                    }
                    IOperation value = argument.Value;
                    if (value is IDeclarationExpressionOperation declared)
                    {
                        value = declared.Expression;
                    }
                    if (
                        value is ILocalReferenceOperation local
                        && SymbolEqualityComparer.Default.Equals(local.Local, target.Local)
                    )
                    {
                        matches = true;
                        break;
                    }
                }
                if (!matches)
                {
                    return;
                }
            }
            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.RedundantPooledClear,
                    syntax.GetLocation()
                )
            );
        }

        private static SyntaxNode PreviousRental(StatementSyntax statement)
        {
            if (statement.Parent is BlockSyntax block)
            {
                int index = block.Statements.IndexOf(statement);
                if (index > 0)
                {
                    StatementSyntax previous = block.Statements[index - 1];
                    if (previous is LocalDeclarationStatementSyntax local)
                    {
                        return local.Declaration;
                    }
                    return previous is ExpressionStatementSyntax expression
                        ? expression.Expression
                        : null;
                }
                return block.Parent is UsingStatementSyntax scope ? RentalIn(scope) : null;
            }
            return statement.Parent is UsingStatementSyntax single ? RentalIn(single) : null;
        }

        private static SyntaxNode RentalIn(UsingStatementSyntax scope)
        {
            return (SyntaxNode)scope.Declaration ?? scope.Expression;
        }

        private static bool IsClearingRental(IInvocationOperation rental, Compilation compilation)
        {
            IMethodSymbol method = rental.TargetMethod;
            if (method.IsStatic)
            {
                return (
                        string.Equals(method.Name, "GetList", StringComparison.Ordinal)
                        && IsType(method.ContainingType, "Buffers`1", compilation)
                    )
                    || (
                        string.Equals(method.Name, "GetStringBuilder", StringComparison.Ordinal)
                        && IsType(method.ContainingType, "Buffers", compilation)
                    );
            }
            if (
                !string.Equals(method.Name, "Get", StringComparison.Ordinal)
                || !IsType(method.ContainingType, "WallstopGenericPool`1", compilation)
            )
            {
                return false;
            }
            if (
                rental.Instance is IFieldReferenceOperation field
                && field.Field.IsStatic
                && field.Field.IsReadOnly
            )
            {
                INamedTypeSymbol type = field.Field.ContainingType;
                string name = field.Field.Name;
                return (
                        IsType(type, "Buffers`1", compilation)
                        && (
                            string.Equals(name, "List", StringComparison.Ordinal)
                            || string.Equals(name, "HashSet", StringComparison.Ordinal)
                            || string.Equals(name, "Queue", StringComparison.Ordinal)
                            || string.Equals(name, "Stack", StringComparison.Ordinal)
                        )
                    )
                    || (
                        IsType(type, "Buffers", compilation)
                        && string.Equals(name, "StringBuilder", StringComparison.Ordinal)
                    )
                    || (
                        IsType(type, "SetBuffers`1", compilation)
                        && string.Equals(name, "SortedSet", StringComparison.Ordinal)
                    )
                    || (
                        IsType(type, "LinkedListBuffer`1", compilation)
                        && string.Equals(name, "LinkedList", StringComparison.Ordinal)
                    )
                    || (
                        IsType(type, "DictionaryBuffer`2", compilation)
                        && (
                            string.Equals(name, "Dictionary", StringComparison.Ordinal)
                            || string.Equals(name, "SortedDictionary", StringComparison.Ordinal)
                        )
                    );
            }
            if (rental.Instance is IInvocationOperation factory && factory.TargetMethod.IsStatic)
            {
                IMethodSymbol provider = factory.TargetMethod;
                return (
                        IsType(provider.ContainingType, "SetBuffers`1", compilation)
                        && (
                            string.Equals(provider.Name, "GetHashSetPool", StringComparison.Ordinal)
                            || string.Equals(
                                provider.Name,
                                "GetSortedSetPool",
                                StringComparison.Ordinal
                            )
                        )
                    )
                    || (
                        IsType(provider.ContainingType, "DictionaryBuffer`2", compilation)
                        && (
                            string.Equals(
                                provider.Name,
                                "GetDictionaryPool",
                                StringComparison.Ordinal
                            )
                            || string.Equals(
                                provider.Name,
                                "GetSortedDictionaryPool",
                                StringComparison.Ordinal
                            )
                        )
                    );
            }
            return false;
        }

        private static bool IsType(
            INamedTypeSymbol type,
            string metadataName,
            Compilation compilation
        )
        {
            return type != null
                && string.Equals(type.MetadataName, metadataName, StringComparison.Ordinal)
                && SymbolEqualityComparer.Default.Equals(
                    type.OriginalDefinition,
                    compilation.GetTypeByMetadataName(PoolNamespace + metadataName)
                );
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.InvocationExpression);
        }
    }
}
