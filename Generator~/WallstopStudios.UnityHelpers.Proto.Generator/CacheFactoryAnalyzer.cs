// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports a method group handed to a concurrent cache's fill factory, which allocates a
    /// delegate on every call rather than only on a miss.
    /// </summary>
    /// <remarks>
    /// This is the half of the cache-fill rules a source linter cannot enforce.
    /// <c>scripts/lint-concurrent-cache-fill.ps1</c> makes every <b>lambda</b> handed to one of
    /// these methods <c>static</c>, so the compiler itself rejects a capture. A method group is not
    /// decidable that way: <c>GetOrAdd(key, CreateAccessors)</c> and <c>GetOrAdd(key, factory)</c>
    /// are both a bare identifier in argument position, and telling them apart needs symbol
    /// resolution. A casing heuristic would be wrong the first time a field is named
    /// <c>Factory</c> (#538).
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CacheFactoryAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>
        /// <c>LanguageVersion.CSharp11</c> as an integer, because this analyzer compiles against
        /// Roslyn 3.8 -- the version Unity 2021.3 can load -- whose enum stops at C# 9.
        /// </summary>
        /// <remarks>
        /// C# 11 caches a method-group conversion in a compiler-generated field, so from that
        /// version on the diagnostic would be simply false. Unity pins C# 9 on every version this
        /// package supports, which is why the shape is worth reporting at all; the guard is here so
        /// the analyzer stays correct if it is ever loaded by a compiler that does not.
        /// </remarks>
        private const int FirstLanguageVersionThatCachesMethodGroups = 1100;

        /// <summary>
        /// Cache types whose fill factories run on every lookup rather than once. Matched on
        /// <see cref="ISymbol.MetadataName"/>, which carries the arity, so a consumer type that
        /// merely shares the name is not caught.
        /// </summary>
        private static readonly ImmutableHashSet<string> CacheTypes = ImmutableHashSet.Create(
            "System.Collections.Concurrent.ConcurrentDictionary`2",
            "System.Runtime.CompilerServices.ConditionalWeakTable`2"
        );

        /// <summary>
        /// Members of <see cref="CacheTypes"/> that take a factory. A name absent from one of those
        /// types simply never matches, so the set is shared rather than split per type.
        /// </summary>
        private static readonly ImmutableHashSet<string> CacheFillMethods = ImmutableHashSet.Create(
            "GetOrAdd",
            "AddOrUpdate",
            "GetValue"
        );

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(WProtoDiagnostics.CacheFactoryAllocatesPerCall);

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            if (
                context.Compilation is CSharpCompilation compilation
                && (int)compilation.LanguageVersion >= FirstLanguageVersionThatCachesMethodGroups
            )
            {
                return;
            }

            context.RegisterOperationAction(OnInvocation, OperationKind.Invocation);
        }

        private static void OnInvocation(OperationAnalysisContext context)
        {
            IInvocationOperation invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol target = invocation.TargetMethod;
            if (target == null || !IsCacheFill(target))
            {
                return;
            }

            foreach (IArgumentOperation argument in invocation.Arguments)
            {
                // The parameter check is what keeps this to factories: `AddOrUpdate` also takes a
                // plain value in the same position on one of its overloads.
                if (argument.Parameter?.Type?.TypeKind != TypeKind.Delegate)
                {
                    continue;
                }

                // A method group in argument position arrives as an IDelegateCreationOperation
                // whose Target is the method reference -- NOT as an IConversionOperation, which is
                // what an unwrap written from the C# spec rather than from the operation tree would
                // look for, and which finds nothing at all. A `static` lambda reaches the same node
                // with an IAnonymousFunctionOperation target, so unwrapping does not widen what is
                // reported.
                IOperation value = argument.Value;
                while (true)
                {
                    if (value is IConversionOperation conversion)
                    {
                        value = conversion.Operand;
                        continue;
                    }

                    if (value is IDelegateCreationOperation delegateCreation)
                    {
                        value = delegateCreation.Target;
                        continue;
                    }

                    break;
                }

                if (!(value is IMethodReferenceOperation methodReference))
                {
                    continue;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.CacheFactoryAllocatesPerCall,
                        value.Syntax.GetLocation(),
                        methodReference.Method.Name,
                        target.Name
                    )
                );
            }
        }

        private static bool IsCacheFill(IMethodSymbol method)
        {
            if (!CacheFillMethods.Contains(method.Name))
            {
                return false;
            }

            INamedTypeSymbol containing = method.ContainingType?.OriginalDefinition;
            if (containing == null || containing.ContainingNamespace == null)
            {
                return false;
            }

            return CacheTypes.Contains(
                containing.ContainingNamespace.ToDisplayString() + "." + containing.MetadataName
            );
        }
    }
}
