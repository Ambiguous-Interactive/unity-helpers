// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports a <c>struct</c> implementing <c>IDisposable</c> whose <c>Dispose</c> assigns to one
    /// of the struct's own fields or to a static.
    /// </summary>
    /// <remarks>
    /// It pairs with "a disposable struct is <c>readonly</c>" rather than replacing it: the two
    /// catch different halves, and the project this was measured on had the <c>readonly</c> half
    /// passing on all three offenders.
    /// <para>
    /// An operation action rather than a syntax one, because the question is what the assignment
    /// TARGETS -- a field of <c>this</c>, a static, or a member of some other object -- and only
    /// the semantic model can tell <c>_previous = x</c> from <c>_owner.previous = x</c> when both
    /// are written without a receiver in source. It deliberately looks no further than the
    /// assignment's own target: a write through a <c>ref</c> local that aliases a field is not
    /// reported, because tracking that is a dataflow question and a rule that guesses is a rule
    /// people route around (#627).
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DisposableStructAssignmentAnalyzer : DiagnosticAnalyzer
    {
        private const string DisposeName = "Dispose";
        private const string DisposableMetadataName = "System.IDisposable";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.DisposableStructDisposeAssigns);

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterOperationAction(
                AnalyzeAssignment,
                OperationKind.SimpleAssignment,
                OperationKind.CompoundAssignment,
                OperationKind.CoalesceAssignment,
                OperationKind.Increment,
                OperationKind.Decrement
            );
        }

        private static void AnalyzeAssignment(OperationAnalysisContext context)
        {
            if (!IsDisposeOfADisposableStruct(context.ContainingSymbol, context.Compilation))
            {
                return;
            }

            if (IsInsideANestedFunction(context.Operation))
            {
                return;
            }

            IOperation target = TargetOf(context.Operation);
            if (target == null)
            {
                return;
            }

            string described = DescribeIfReportable(target);
            if (described == null)
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.DisposableStructDisposeAssigns,
                    target.Syntax.GetLocation(),
                    context.ContainingSymbol.ContainingType.Name,
                    described
                )
            );
        }

        /// <summary>
        /// Whether <paramref name="containing"/> is the parameterless <c>Dispose</c> of a struct
        /// that implements <c>IDisposable</c>.
        /// </summary>
        /// <remarks>
        /// <c>Dispose(bool disposing)</c> is the BCL's own disposal protocol and a class's shape;
        /// only the release point the <c>using</c> statement calls is in scope here. An explicit
        /// <c>void IDisposable.Dispose()</c> is the same method under a different name, so it is
        /// matched through its explicit implementation list rather than by spelling.
        /// </remarks>
        private static bool IsDisposeOfADisposableStruct(
            ISymbol containing,
            Compilation compilation
        )
        {
            if (!(containing is IMethodSymbol method))
            {
                return false;
            }

            if (!method.ReturnsVoid || method.Parameters.Length != 0)
            {
                return false;
            }

            if (method.Name != DisposeName && !ImplementsDisposeExplicitly(method))
            {
                return false;
            }

            INamedTypeSymbol declaring = method.ContainingType;
            if (declaring == null || declaring.TypeKind != TypeKind.Struct)
            {
                return false;
            }

            INamedTypeSymbol disposable = compilation.GetTypeByMetadataName(DisposableMetadataName);
            if (disposable == null)
            {
                return false;
            }

            foreach (INamedTypeSymbol implemented in declaring.AllInterfaces)
            {
                if (SymbolEqualityComparer.Default.Equals(implemented, disposable))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ImplementsDisposeExplicitly(IMethodSymbol method)
        {
            foreach (IMethodSymbol explicitly in method.ExplicitInterfaceImplementations)
            {
                if (explicitly.Name == DisposeName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether this operation sits inside a lambda or a local function, whose body runs
        /// somewhere other than the disposal this rule is about.
        /// </summary>
        private static bool IsInsideANestedFunction(IOperation operation)
        {
            for (IOperation walked = operation; walked != null; walked = walked.Parent)
            {
                if (walked is IAnonymousFunctionOperation || walked is ILocalFunctionOperation)
                {
                    return true;
                }
            }

            return false;
        }

        private static IOperation TargetOf(IOperation operation)
        {
            if (operation is IAssignmentOperation assignment)
            {
                return assignment.Target;
            }

            if (operation is IIncrementOrDecrementOperation stepped)
            {
                return stepped.Target;
            }

            return null;
        }

        /// <summary>
        /// How the message should name <paramref name="target"/>, or <c>null</c> where writing to it
        /// is not the defect.
        /// </summary>
        /// <remarks>
        /// The three deliberate silences are a local (nothing outside the call sees it), an array or
        /// indexer element, and a member reached through another object. That last one is the
        /// important one: a struct that mutates something it holds a reference to is sharing state
        /// with every copy of itself, which is exactly where this state is supposed to live.
        /// </remarks>
        private static string DescribeIfReportable(IOperation target)
        {
            ISymbol assigned;
            IOperation instance;
            if (target is IFieldReferenceOperation field)
            {
                assigned = field.Field;
                instance = field.Instance;
            }
            else if (target is IPropertyReferenceOperation property)
            {
                assigned = property.Property;
                instance = property.Instance;
            }
            else if (target is IEventReferenceOperation subscription)
            {
                assigned = subscription.Event;
                instance = subscription.Instance;
            }
            else
            {
                return null;
            }

            if (assigned.IsStatic)
            {
                return "the global '"
                    + assigned.ContainingType.Name
                    + "."
                    + assigned.Name
                    + "', which outlives every copy of this struct";
            }

            if (instance is IInstanceReferenceOperation)
            {
                return "its own '"
                    + assigned.Name
                    + "', which every copy of this struct carries separately";
            }

            return null;
        }
    }
}
