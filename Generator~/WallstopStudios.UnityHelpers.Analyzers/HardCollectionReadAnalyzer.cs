// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports stack and queue reads with an available non-throwing alternative.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class HardCollectionReadAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.HardCollectionRead);

        private static bool IsKnownCollection(INamedTypeSymbol type, Compilation compilation)
        {
            string metadataName;
            switch (type.Name)
            {
                case "Stack":
                    metadataName = "System.Collections.Generic.Stack`1";
                    break;
                case "Queue":
                    metadataName = "System.Collections.Generic.Queue`1";
                    break;
                case "ConcurrentStack":
                    metadataName = "System.Collections.Concurrent.ConcurrentStack`1";
                    break;
                case "ConcurrentQueue":
                    metadataName = "System.Collections.Concurrent.ConcurrentQueue`1";
                    break;
                default:
                    return false;
            }
            return SymbolEqualityComparer.Default.Equals(
                type.OriginalDefinition,
                compilation.GetTypeByMetadataName(metadataName)
            );
        }

        private static void OnInvocation(OperationAnalysisContext context)
        {
            IInvocationOperation invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol method = invocation.TargetMethod;
            if (
                invocation.Instance == null
                || method.IsStatic
                || method.Arity != 0
                || method.Parameters.Length != 0
                || method.ReturnsVoid
                || !IsKnownCollection(method.ContainingType, context.Compilation)
            )
            {
                return;
            }

            string tryMethodName;
            switch (method.Name)
            {
                case "Pop":
                    tryMethodName = "TryPop";
                    break;
                case "Dequeue":
                    tryMethodName = "TryDequeue";
                    break;
                case "Peek":
                    tryMethodName = "TryPeek";
                    break;
                default:
                    return;
            }

            ITypeSymbol receiverType = invocation.Instance.Type;
            if (receiverType == null)
            {
                return;
            }
            SemanticModel model = invocation.SemanticModel;
            if (model == null)
            {
                return;
            }
            foreach (
                ISymbol candidate in model.LookupSymbols(
                    invocation.Syntax.SpanStart,
                    receiverType,
                    tryMethodName
                )
            )
            {
                if (
                    candidate is IMethodSymbol alternative
                    && !alternative.IsStatic
                    && alternative.Arity == 0
                    && alternative.ReturnType.SpecialType == SpecialType.System_Boolean
                    && alternative.Parameters.Length == 1
                    && alternative.Parameters[0].RefKind == RefKind.Out
                    && SymbolEqualityComparer.Default.Equals(
                        alternative.Parameters[0].Type,
                        method.ReturnType
                    )
                    && model.IsAccessible(invocation.Syntax.SpanStart, alternative)
                )
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            UnityHelpersDiagnostics.HardCollectionRead,
                            invocation.Syntax.GetLocation(),
                            method.ContainingType.ToDisplayString(
                                SymbolDisplayFormat.MinimallyQualifiedFormat
                            ),
                            method.Name,
                            tryMethodName
                        )
                    );
                    return;
                }
            }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterOperationAction(OnInvocation, OperationKind.Invocation);
        }
    }
}
