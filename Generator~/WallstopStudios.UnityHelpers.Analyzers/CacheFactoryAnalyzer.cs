// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports a method group handed to a lookup's value factory, which allocates a delegate on
    /// every call rather than only when the factory runs.
    /// </summary>
    /// <remarks>
    /// This is the half of the cache-fill rules a source linter cannot enforce.
    /// <c>scripts/lint-concurrent-cache-fill.ps1</c> makes every <b>lambda</b> handed to one of
    /// these methods <c>static</c>, so the compiler itself rejects a capture. A method group is not
    /// decidable that way: <c>GetOrAdd(key, CreateAccessors)</c> and <c>GetOrAdd(key, factory)</c>
    /// are both a bare identifier in argument position, and telling them apart needs symbol
    /// resolution (#538).
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
        /// BCL types with a factory-taking member, matched on <see cref="ISymbol.MetadataName"/> so
        /// the arity is part of the match and a consumer type sharing a name is not caught.
        /// </summary>
        /// <remarks>
        /// These are name-gated by <see cref="BclFactoryTakingMethods"/>, because the types also
        /// carry plenty of members that take a delegate for unrelated reasons.
        /// </remarks>
        private static readonly ImmutableHashSet<string> BclFactoryTakingTypes =
            ImmutableHashSet.Create(
                "System.Collections.Concurrent.ConcurrentDictionary`2",
                "System.Runtime.CompilerServices.ConditionalWeakTable`2"
            );

        /// <summary>
        /// Members of <see cref="BclFactoryTakingTypes"/> that take a value factory.
        /// </summary>
        private static readonly ImmutableHashSet<string> BclFactoryTakingMethods =
            ImmutableHashSet.Create("GetOrAdd", "AddOrUpdate", "GetValue");

        /// <summary>
        /// This package's own extension types, where EVERY delegate-typed parameter counts and no
        /// member name is consulted.
        /// </summary>
        /// <remarks>
        /// A name list was the first shape here and it was the wrong one. It carried
        /// <c>GetOrAdd</c>, <c>GetOrElse</c> and <c>AddOrUpdate</c>, and review found <c>TryAdd</c>
        /// missing -- whose <c>creator</c> runs only when the key is absent, which is precisely the
        /// defect. A sweep of the same file then found three more (<c>Merge</c>,
        /// <c>Difference</c>, <c>Reverse</c>, each taking an optional <c>Func</c> creator). Matching
        /// the delegate parameter instead of the name closes the class permanently, so the next
        /// factory-taking extension added to this package is covered the day it is written rather
        /// than the day someone notices.
        /// <c>Dictionary&lt;K, V&gt;</c> reaches all of this through these extensions; the BCL gives
        /// it no factory-taking member of its own.
        /// </remarks>
        private static readonly ImmutableHashSet<string> PackageFactoryTakingTypes =
            ImmutableHashSet.Create(
                "WallstopStudios.UnityHelpers.Core.Extension.DictionaryExtensions"
            );

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.CacheFactoryAllocatesPerCall);

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
                && FirstLanguageVersionThatCachesMethodGroups <= (int)compilation.LanguageVersion
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
            if (target == null || !IsFactoryTakingLookup(target))
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

                // A method group in argument position arrives as an IDelegateCreationOperation whose
                // Target is the method reference -- NOT as an IConversionOperation, which is what an
                // unwrap written from the C# specification rather than from the operation tree would
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
                        UnityHelpersDiagnostics.CacheFactoryAllocatesPerCall,
                        value.Syntax.GetLocation(),
                        methodReference.Method.Name,
                        target.Name
                    )
                );
            }
        }

        private static bool IsFactoryTakingLookup(IMethodSymbol method)
        {
            // An extension method called in reduced form (`dictionary.GetOrAdd(...)`) reports the
            // static class that declares it only through ReducedFrom.
            IMethodSymbol declared = method.ReducedFrom ?? method;
            INamedTypeSymbol containing = declared.ContainingType?.OriginalDefinition;
            if (containing == null || containing.ContainingNamespace == null)
            {
                return false;
            }

            string fullName =
                containing.ContainingNamespace.ToDisplayString() + "." + containing.MetadataName;

            if (PackageFactoryTakingTypes.Contains(fullName))
            {
                return true;
            }

            return BclFactoryTakingTypes.Contains(fullName)
                && BclFactoryTakingMethods.Contains(method.Name);
        }
    }
}
