// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports a call that throws away the handle which is the only way to undo it: an
    /// <c>EffectHandle</c> (WUH006) or a coroutine handle (WUH007).
    /// </summary>
    /// <remarks>
    /// One analyzer because it is one shape -- a returned handle discarded at the call site -- and
    /// the two halves differ only in how the handle is identified.
    /// <para>
    /// The coroutine half matches the RETURN TYPE rather than a method-name list. In the tree this
    /// was measured on, the package's own periodic-job and delay helpers each outnumbered raw
    /// <c>StartCoroutine</c>, so a name-only rule saw 9 of 44 call sites; matching
    /// <c>UnityEngine.Coroutine</c> covers those four helpers, <c>StartCoroutine</c> itself, and a
    /// consumer's own starter, with no list to keep in sync (#626).
    /// </para>
    /// <para>
    /// The effect half is name-gated, because there the return type alone is too wide:
    /// <c>Attribute.Add</c>, <c>Attribute.Subtract</c>, <c>EnsureHandle</c> and friends also hand
    /// back an <c>EffectHandle</c> without any undo obligation attached to it. <c>ApplyEffect</c> is
    /// the member that applies something, so it is the member whose handle has to be kept;
    /// <c>ForceApplyEffect</c> is the deliberate no-handle overload and returns <c>void</c>, so it
    /// never reaches this at all (#623).
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DiscardedHandleAnalyzer : DiagnosticAnalyzer
    {
        private const string CoroutineMetadataName = "UnityEngine.Coroutine";
        private const string EffectHandleMetadataName =
            "WallstopStudios.UnityHelpers.Tags.EffectHandle";
        private const string ApplyEffectMethodName = "ApplyEffect";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                UnityHelpersDiagnostics.DiscardedEffectHandle,
                UnityHelpersDiagnostics.DiscardedCoroutineHandle
            );

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            INamedTypeSymbol coroutine = context.Compilation.GetTypeByMetadataName(
                CoroutineMetadataName
            );
            INamedTypeSymbol effectHandle = context.Compilation.GetTypeByMetadataName(
                EffectHandleMetadataName
            );

            // A compilation that references neither type cannot contain either handle, so it pays
            // nothing beyond these two lookups.
            if (coroutine == null && effectHandle == null)
            {
                return;
            }

            context.RegisterOperationAction(
                operationContext => OnInvocation(operationContext, coroutine, effectHandle),
                OperationKind.Invocation
            );
        }

        private static void OnInvocation(
            OperationAnalysisContext context,
            INamedTypeSymbol coroutine,
            INamedTypeSymbol effectHandle
        )
        {
            IInvocationOperation invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol target = invocation.TargetMethod;
            if (target == null || !IsDiscarded(invocation))
            {
                return;
            }

            ITypeSymbol returnType = target.ReturnType;
            if (returnType == null)
            {
                return;
            }

            if (coroutine != null && SymbolEqualityComparer.Default.Equals(returnType, coroutine))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        UnityHelpersDiagnostics.DiscardedCoroutineHandle,
                        invocation.Syntax.GetLocation(),
                        target.Name
                    )
                );
                return;
            }

            if (effectHandle == null || target.Name != ApplyEffectMethodName)
            {
                return;
            }

            if (!SymbolEqualityComparer.Default.Equals(UnwrapNullable(returnType), effectHandle))
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.DiscardedEffectHandle,
                    invocation.Syntax.GetLocation(),
                    target.Name
                )
            );
        }

        /// <summary>
        /// Whether the value this call produced goes nowhere.
        /// </summary>
        /// <remarks>
        /// Anything else -- a local, a field, an argument, a <c>return</c>, a <c>yield return</c>, a
        /// further member access -- reaches this with some other parent operation and is left alone,
        /// which is what keeps the rule to the sites where the handle is genuinely unrecoverable.
        /// </remarks>
        private static bool IsDiscarded(IOperation operation)
        {
            IOperation parent = operation.Parent;
            while (parent is IConversionOperation)
            {
                parent = parent.Parent;
            }

            if (parent is IExpressionStatementOperation)
            {
                return true;
            }

            if (
                parent is ISimpleAssignmentOperation assignment
                && assignment.Target is IDiscardOperation
            )
            {
                return true;
            }

            return parent is IIsPatternOperation isPattern
                && isPattern.Pattern is IDiscardPatternOperation;
        }

        private static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        {
            if (
                type is INamedTypeSymbol named
                && named.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T
                && named.TypeArguments.Length == 1
            )
            {
                return named.TypeArguments[0];
            }

            return type;
        }
    }
}
