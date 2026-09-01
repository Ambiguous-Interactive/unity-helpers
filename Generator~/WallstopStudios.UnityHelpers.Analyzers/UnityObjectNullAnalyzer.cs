// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports the two ways a CLR-null test walks past a destroyed <c>UnityEngine.Object</c>: the
    /// null-propagating operators, and an assertion that compares one against null.
    /// </summary>
    /// <remarks>
    /// The signal is the operand's TYPE, never the operator, which is what makes this an analyzer
    /// rather than a source linter. <c>Vector2? p; p?.x</c> is exactly what <c>?.</c> is for, a
    /// nullable value type reaches the same tokens as a <c>Component</c>, and an alias or a
    /// constrained type parameter hides the hierarchy from any regex -- so the question "is this
    /// assignable to <c>UnityEngine.Object</c>" has to be asked of the symbol (#621).
    /// <para>
    /// The assertion half covers <c>NUnit.Framework</c> and NOTHING else. Unity's own
    /// <c>UnityEngine.Assertions.Assert</c> is already destroyed-aware: measured in a Unity
    /// 6000.4.6f1 editor on a destroyed <c>GameObject</c>, with <c>Assert.raiseExceptions = true</c>
    /// and an <c>IsNotNull((string)null)</c> control that did fail,
    /// <c>UnityEngine.Assertions.Assert.IsNull(destroyed)</c> PASSED and <c>IsNotNull(destroyed)</c>
    /// FAILED -- the destroyed-aware answers, and the opposite of both for a live object. Its
    /// <c>IsNull&lt;T&gt;</c> / <c>IsNotNull&lt;T&gt;</c> forward to a <c>UnityEngine.Object</c>
    /// overload that compares through the <c>==</c> operator, where
    /// <c>NUnit.Framework.Assert.IsNull(object)</c> has no such overload and genuinely tests CLR
    /// null. Reporting Unity's was a false positive on correct code; do not add the namespace back.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnityObjectNullAnalyzer : DiagnosticAnalyzer
    {
        private const string UnityObjectMetadataName = "UnityEngine.Object";

        /// <summary>
        /// NUnit spells the same CLR-null comparison on several types (<c>Assert</c>,
        /// <c>CollectionAssert</c>, <c>StringAssert</c>), so the suffix is matched rather than the
        /// whole name -- inside the namespace below and nowhere else.
        /// </summary>
        private const string AssertionTypeSuffix = "Assert";

        private const string NullConditionalOperator = "?.";

        private const string IsNullPattern = "is null";

        private const string IsNotNullPattern = "is not null";
        private const string NullConditionalIndexOperator = "?[]";
        private const string NullCoalescingOperator = "??";
        private const string NullCoalescingAssignmentOperator = "??=";

        /// <summary>
        /// NUnit alone. <c>UnityEngine.Assertions</c> was measured destroyed-aware and is
        /// deliberately absent; see the remarks on the type.
        /// </summary>
        private static readonly ImmutableHashSet<string> AssertionNamespaces =
            ImmutableHashSet.Create("NUnit.Framework");

        /// <summary>
        /// Assertions whose first argument is the value under test; every later parameter is the
        /// failure message.
        /// </summary>
        private static readonly ImmutableHashSet<string> NullTestingAssertions =
            ImmutableHashSet.Create("IsNull", "IsNotNull", "Null", "NotNull");

        /// <summary>
        /// Assertions that reach the same comparison through a null literal in either operand.
        /// </summary>
        private static readonly ImmutableHashSet<string> EqualityAssertions =
            ImmutableHashSet.Create("AreEqual", "AreNotEqual");

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(
                UnityHelpersDiagnostics.NullPropagationOnUnityObject,
                UnityHelpersDiagnostics.NullAssertionOnUnityObject
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
            // Resolved once, and nothing at all is registered without it, so a compilation that has
            // never heard of Unity pays for one metadata lookup.
            INamedTypeSymbol unityObject = context.Compilation.GetTypeByMetadataName(
                UnityObjectMetadataName
            );
            if (unityObject == null)
            {
                return;
            }

            context.RegisterOperationAction(
                (OperationAnalysisContext operationContext) =>
                    AnalyzeNullPropagation(operationContext, unityObject),
                OperationKind.ConditionalAccess,
                OperationKind.Coalesce,
                OperationKind.CoalesceAssignment
            );
            context.RegisterOperationAction(
                (OperationAnalysisContext operationContext) =>
                    AnalyzeAssertion(operationContext, unityObject),
                OperationKind.Invocation
            );
            context.RegisterOperationAction(
                (OperationAnalysisContext operationContext) =>
                    AnalyzeNullPattern(operationContext, unityObject),
                OperationKind.IsPattern
            );
        }

        /// <summary>
        /// Reports <c>is null</c> and <c>is not null</c> applied to a <c>UnityEngine.Object</c>.
        /// </summary>
        /// <param name="context">The operation being analyzed.</param>
        /// <param name="unityObject">The resolved <c>UnityEngine.Object</c> symbol.</param>
        /// <remarks>
        /// A constant-null pattern is a CLR null test, so it walks past a destroyed object exactly
        /// as <c>?.</c> does. A type pattern is worse and is deliberately not reported here: it is
        /// not a null test at all, so there is no null test to correct.
        /// </remarks>
        private static void AnalyzeNullPattern(
            OperationAnalysisContext context,
            INamedTypeSymbol unityObject
        )
        {
            if (!(context.Operation is IIsPatternOperation operation))
            {
                return;
            }

            IOperation tested = WithoutConversions(operation.Value);
            if (tested == null || !IsUnityObject(tested.Type, unityObject))
            {
                return;
            }

            if (!TryDescribeNullPattern(operation.Pattern, out string writtenOperator))
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.NullPropagationOnUnityObject,
                    operation.Syntax.GetLocation(),
                    tested.Syntax.ToString(),
                    tested.Type.ToDisplayString(),
                    writtenOperator
                )
            );
        }

        /// <summary>Whether <paramref name="pattern"/> is a constant-null pattern.</summary>
        /// <param name="pattern">The pattern the value is matched against.</param>
        /// <param name="writtenOperator">Receives the operator the author typed.</param>
        /// <returns><c>false</c> for every pattern that is not a null test.</returns>
        private static bool TryDescribeNullPattern(
            IPatternOperation pattern,
            out string writtenOperator
        )
        {
            bool negated = false;
            IPatternOperation current = pattern;
            while (current is INegatedPatternOperation negatedPattern)
            {
                negated = !negated;
                current = negatedPattern.Pattern;
            }

            if (
                !(current is IConstantPatternOperation constant)
                || constant.Value == null
                || !constant.Value.ConstantValue.HasValue
                || constant.Value.ConstantValue.Value != null
            )
            {
                writtenOperator = null;
                return false;
            }

            writtenOperator = negated ? IsNotNullPattern : IsNullPattern;
            return true;
        }

        private static void AnalyzeNullPropagation(
            OperationAnalysisContext context,
            INamedTypeSymbol unityObject
        )
        {
            IOperation operation = context.Operation;
            IOperation tested;
            string writtenOperator;
            if (operation is IConditionalAccessOperation conditionalAccess)
            {
                tested = conditionalAccess.Operation;
                writtenOperator = ConditionalAccessOperatorOf(conditionalAccess);
            }
            else if (operation is ICoalesceOperation coalesce)
            {
                tested = coalesce.Value;
                writtenOperator = NullCoalescingOperator;
            }
            else if (operation is ICoalesceAssignmentOperation coalesceAssignment)
            {
                tested = coalesceAssignment.Target;
                writtenOperator = NullCoalescingAssignmentOperator;
            }
            else
            {
                return;
            }

            tested = WithoutConversions(tested);
            if (tested == null || !IsUnityObject(tested.Type, unityObject))
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.NullPropagationOnUnityObject,
                    operation.Syntax.GetLocation(),
                    tested.Syntax.ToString(),
                    tested.Type.ToDisplayString(),
                    writtenOperator
                )
            );
        }

        private static void AnalyzeAssertion(
            OperationAnalysisContext context,
            INamedTypeSymbol unityObject
        )
        {
            IInvocationOperation invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol target = invocation.TargetMethod;
            if (target == null || !IsAssertionType(target.ContainingType))
            {
                return;
            }

            if (NullTestingAssertions.Contains(target.Name))
            {
                ReportAssertion(context, invocation, ArgumentAt(invocation, 0), unityObject);
                return;
            }

            if (!EqualityAssertions.Contains(target.Name))
            {
                return;
            }

            IOperation expected = ArgumentAt(invocation, 0);
            IOperation actual = ArgumentAt(invocation, 1);
            if (IsNull(expected))
            {
                ReportAssertion(context, invocation, actual, unityObject);
            }
            else if (IsNull(actual))
            {
                ReportAssertion(context, invocation, expected, unityObject);
            }
        }

        private static void ReportAssertion(
            OperationAnalysisContext context,
            IInvocationOperation invocation,
            IOperation tested,
            INamedTypeSymbol unityObject
        )
        {
            if (tested == null || !IsUnityObject(tested.Type, unityObject))
            {
                return;
            }

            IMethodSymbol target = invocation.TargetMethod;
            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.NullAssertionOnUnityObject,
                    invocation.Syntax.GetLocation(),
                    target.ContainingType.Name + "." + target.Name,
                    tested.Type.ToDisplayString(),
                    tested.Syntax.ToString()
                )
            );
        }

        private static bool IsAssertionType(INamedTypeSymbol type)
        {
            if (type == null || type.ContainingNamespace == null)
            {
                return false;
            }

            return AssertionNamespaces.Contains(type.ContainingNamespace.ToDisplayString())
                && type.MetadataName.EndsWith(AssertionTypeSuffix, StringComparison.Ordinal);
        }

        /// <summary>
        /// The explicit argument bound to the parameter at <paramref name="ordinal"/>, with its
        /// conversions removed.
        /// </summary>
        /// <remarks>
        /// Arguments are ordered by parameter rather than by source, and an omitted optional
        /// parameter arrives here as a synthesized default -- which for a <c>string message</c> is a
        /// null literal, and would read as <c>AreEqual(x, null)</c> if it were not excluded. The
        /// conversion has to come off as well: <c>NUnit</c> declares these over <c>object</c>, so
        /// the argument's own type is what says whether a <c>UnityEngine.Object</c> was passed.
        /// </remarks>
        private static IOperation ArgumentAt(IInvocationOperation invocation, int ordinal)
        {
            foreach (IArgumentOperation argument in invocation.Arguments)
            {
                if (argument.ArgumentKind != ArgumentKind.Explicit || argument.Parameter == null)
                {
                    continue;
                }

                if (argument.Parameter.Ordinal == ordinal)
                {
                    return WithoutConversions(argument.Value);
                }
            }

            return null;
        }

        private static IOperation WithoutConversions(IOperation operation)
        {
            IOperation value = operation;
            while (value is IConversionOperation conversion)
            {
                value = conversion.Operand;
            }

            return value;
        }

        private static bool IsNull(IOperation operation)
        {
            return operation != null
                && operation.ConstantValue.HasValue
                && operation.ConstantValue.Value == null;
        }

        /// <summary>
        /// Whether <paramref name="type"/> is, or derives from, <c>UnityEngine.Object</c>.
        /// </summary>
        /// <remarks>
        /// A type parameter carries the hierarchy on its constraints rather than on a base type, so
        /// <c>T where T : Component</c> has to answer yes and an unconstrained <c>T</c> no.
        /// </remarks>
        private static bool IsUnityObject(ITypeSymbol type, INamedTypeSymbol unityObject)
        {
            if (type == null)
            {
                return false;
            }

            if (type is ITypeParameterSymbol typeParameter)
            {
                foreach (ITypeSymbol constraint in typeParameter.ConstraintTypes)
                {
                    if (IsUnityObject(constraint, unityObject))
                    {
                        return true;
                    }
                }

                return false;
            }

            for (ITypeSymbol candidate = type; candidate != null; candidate = candidate.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate, unityObject))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether the access was written <c>?.</c> or <c>?[]</c>.
        /// </summary>
        /// <remarks>
        /// Both are one <see cref="IConditionalAccessOperation"/>, so only the syntax says which the
        /// author typed -- and the message quotes the operator back at them.
        /// </remarks>
        private static string ConditionalAccessOperatorOf(IConditionalAccessOperation operation)
        {
            return
                operation.Syntax is ConditionalAccessExpressionSyntax syntax
                && syntax.WhenNotNull.GetFirstToken().IsKind(SyntaxKind.OpenBracketToken)
                ? NullConditionalIndexOperator
                : NullConditionalOperator;
        }
    }
}
