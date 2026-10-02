// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>Reports disposable boxing conversions that can allocate.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DisposableStructBoxingAnalyzer : DiagnosticAnalyzer
    {
        private const string DisposableMetadataName = "System.IDisposable";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.DisposableStructBoxing);

        private static void AnalyzeConversion(OperationAnalysisContext context)
        {
            IConversionOperation operation = (IConversionOperation)context.Operation;
            if (IsRequiredEnumeratorReturn(operation, context))
            {
                return;
            }
            ITypeSymbol source = operation.Operand.Type;
            ITypeSymbol destination = operation.Type;
            if (source == null || destination == null || operation.Conversion.IsUserDefined)
            {
                return;
            }

            if (
                !((CSharpCompilation)context.Compilation)
                    .ClassifyConversion(source, destination)
                    .IsBoxing
            )
            {
                return;
            }

            if (
                source is INamedTypeSymbol nullable
                && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            )
            {
                source = nullable.TypeArguments[0];
            }

            INamedTypeSymbol disposable = context.Compilation.GetTypeByMetadataName(
                DisposableMetadataName
            );
            if (
                disposable == null
                || !context.Compilation.ClassifyCommonConversion(source, disposable).IsImplicit
            )
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.DisposableStructBoxing,
                    operation.Syntax.GetLocation(),
                    source.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                )
            );
        }

        private static bool IsRequiredEnumeratorReturn(
            IConversionOperation operation,
            OperationAnalysisContext context
        )
        {
            if (
                !(operation.Parent is IReturnOperation)
                || !(context.ContainingSymbol is IMethodSymbol method)
            )
            {
                return false;
            }
            foreach (IMethodSymbol implemented in method.ExplicitInterfaceImplementations)
            {
                if (
                    !string.Equals(
                        implemented.Name,
                        nameof(System.Collections.IEnumerable.GetEnumerator),
                        System.StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }
                INamedTypeSymbol owner = implemented.ContainingType.OriginalDefinition;
                if (
                    SymbolEqualityComparer.Default.Equals(
                        owner,
                        context.Compilation.GetTypeByMetadataName("System.Collections.IEnumerable")
                    )
                    || SymbolEqualityComparer.Default.Equals(
                        owner,
                        context.Compilation.GetTypeByMetadataName(
                            "System.Collections.Generic.IEnumerable`1"
                        )
                    )
                    || SymbolEqualityComparer.Default.Equals(
                        owner,
                        context.Compilation.GetTypeByMetadataName("System.Collections.IDictionary")
                    )
                )
                {
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
        }
    }
}
