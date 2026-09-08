// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports a same-object <c>GetComponent</c> call compared against null, where
    /// <c>TryGetComponent</c> answers the same question without Unity's Editor allocation.
    /// </summary>
    /// <remarks>
    /// The signal is the CALLEE, resolved from the symbol rather than read off the name, which is
    /// what keeps this out of a source linter's reach. Three different methods are spelled
    /// <c>GetComponent</c> in this repository alone -- Unity's generic form, Unity's
    /// <c>System.Type</c> form, and <c>Helpers.GetComponent&lt;T&gt;(this Object)</c> -- and only
    /// the first two have a <c>TryGetComponent</c> that means the same thing. The package's own
    /// extension hands back <c>default</c> for a target that is neither a <c>GameObject</c> nor a
    /// <c>Component</c>, so <c>Helpers.GetComponent&lt;T&gt;(target) == null</c> is a test of
    /// <c>target</c> and is correct as written.
    /// <para>
    /// <c>GetComponentInChildren</c> and <c>GetComponentInParent</c> are deliberately not reported.
    /// The rule the family holds itself to is that a diagnostic names a fix that exists, and for a
    /// hierarchy search there is none: Unity ships no <c>TryGetComponentInChildren</c>, and this
    /// package's <c>HasComponent</c> forwards to <c>TryGetComponent</c>, which searches one object.
    /// The same reason excludes <c>GetComponent(string)</c>, which has no <c>Try</c> counterpart.
    /// </para>
    /// <para>
    /// Both operand positions are matched, so <c>null != GetComponent&lt;T&gt;()</c> is reported
    /// alongside the usual spelling, and so are <c>is null</c> and <c>is not null</c> -- which
    /// <see cref="UnityObjectNullAnalyzer"/> also reports, for the different defect that a CLR null
    /// test cannot see a destroyed object. Its fix leaves the allocation in place; this one does not
    /// (#741).
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class GetComponentNullComparisonAnalyzer : DiagnosticAnalyzer
    {
        private const string ComponentMetadataName = "UnityEngine.Component";
        private const string GameObjectMetadataName = "UnityEngine.GameObject";
        private const string UnityObjectMetadataName = "UnityEngine.Object";
        private const string TypeMetadataName = "System.Type";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.GetComponentComparedAgainstNull);

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            INamedTypeSymbol component = context.Compilation.GetTypeByMetadataName(
                ComponentMetadataName
            );
            INamedTypeSymbol gameObject = context.Compilation.GetTypeByMetadataName(
                GameObjectMetadataName
            );
            if (component == null && gameObject == null)
            {
                return;
            }

            SameObjectComponentSearches searches = new SameObjectComponentSearches(
                component,
                gameObject,
                context.Compilation.GetTypeByMetadataName(UnityObjectMetadataName),
                context.Compilation.GetTypeByMetadataName(TypeMetadataName)
            );
            context.RegisterOperationAction(searches.OnComparison, OperationKind.BinaryOperator);
            context.RegisterOperationAction(searches.OnNullPattern, OperationKind.IsPattern);
        }

        /// <summary>
        /// The Unity symbols this analyzer matches against, resolved once per compilation.
        /// </summary>
        /// <remarks>
        /// A compilation that has never heard of Unity resolves none of them and registers no
        /// action, so it pays nothing. Each symbol is independently nullable: the tests compile
        /// against hand-written stubs, and a stub declaring only what one fixture needs must not
        /// crash the analyzer.
        /// </remarks>
        private sealed class SameObjectComponentSearches
        {
            private const string GetComponentMethodName = "GetComponent";

            /// <summary>
            /// The type <c>GetComponent(Type)</c> hands back, which is what its <c>out</c>
            /// replacement has to be declared as.
            /// </summary>
            private const string ComponentDisplayName = "Component";

            private readonly INamedTypeSymbol _component;
            private readonly INamedTypeSymbol _gameObject;
            private readonly INamedTypeSymbol _unityObject;
            private readonly INamedTypeSymbol _systemType;

            internal SameObjectComponentSearches(
                INamedTypeSymbol component,
                INamedTypeSymbol gameObject,
                INamedTypeSymbol unityObject,
                INamedTypeSymbol systemType
            )
            {
                _component = component;
                _gameObject = gameObject;
                _unityObject = unityObject;
                _systemType = systemType;
            }

            /// <summary>
            /// Reports <c>GetComponent(...) == null</c> and <c>!= null</c> in either operand order.
            /// </summary>
            /// <param name="context">The operation being analyzed.</param>
            /// <remarks>
            /// <c>UnityEngine.Object</c> overloads both operators, so the comparison arrives as a
            /// user-defined <see cref="IBinaryOperation"/> whose operands carry the conversion to
            /// <c>UnityEngine.Object</c> -- the null literal included, which is why the conversions
            /// come off before either side is examined.
            /// </remarks>
            internal void OnComparison(OperationAnalysisContext context)
            {
                IBinaryOperation operation = (IBinaryOperation)context.Operation;
                if (
                    operation.OperatorKind != BinaryOperatorKind.Equals
                    && operation.OperatorKind != BinaryOperatorKind.NotEquals
                )
                {
                    return;
                }

                IOperation left = WithoutConversions(operation.LeftOperand);
                IOperation right = WithoutConversions(operation.RightOperand);
                if (IsNull(left))
                {
                    Report(context, operation, right);
                }
                else if (IsNull(right))
                {
                    Report(context, operation, left);
                }
            }

            /// <summary>
            /// Reports <c>GetComponent(...) is null</c> and <c>is not null</c>.
            /// </summary>
            /// <param name="context">The operation being analyzed.</param>
            /// <remarks>
            /// A type pattern is not a null test, so there is nothing about it to correct here and
            /// it is not reported -- the same line <see cref="UnityObjectNullAnalyzer"/> draws.
            /// </remarks>
            internal void OnNullPattern(OperationAnalysisContext context)
            {
                IIsPatternOperation operation = (IIsPatternOperation)context.Operation;
                if (!IsNullPattern(operation.Pattern))
                {
                    return;
                }

                Report(context, operation, WithoutConversions(operation.Value));
            }

            private static bool IsNullPattern(IPatternOperation pattern)
            {
                IPatternOperation current = pattern;
                while (current is INegatedPatternOperation negated)
                {
                    current = negated.Pattern;
                }

                return current is IConstantPatternOperation constant
                    && constant.Value != null
                    && constant.Value.ConstantValue.HasValue
                    && constant.Value.ConstantValue.Value == null;
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
            /// The explicit argument bound to the parameter at <paramref name="ordinal"/>, exactly
            /// as the author wrote it.
            /// </summary>
            /// <remarks>
            /// The suggested replacement passes the same expression along, so the author's own
            /// spelling is what belongs in the message -- <c>componentType</c> rather than the
            /// <c>System.Type</c> it resolves to.
            /// </remarks>
            private static string ArgumentTextAt(IInvocationOperation invocation, int ordinal)
            {
                foreach (IArgumentOperation argument in invocation.Arguments)
                {
                    if (
                        argument.ArgumentKind != ArgumentKind.Explicit
                        || argument.Parameter == null
                    )
                    {
                        continue;
                    }

                    if (argument.Parameter.Ordinal == ordinal)
                    {
                        return argument.Value.Syntax.ToString();
                    }
                }

                return null;
            }

            /// <summary>
            /// The value bound to the parameter at <paramref name="ordinal"/>, for tests on the
            /// argument itself rather than on its spelling.
            /// </summary>
            private static IOperation ArgumentValueAt(IInvocationOperation invocation, int ordinal)
            {
                foreach (IArgumentOperation argument in invocation.Arguments)
                {
                    if (
                        argument.ArgumentKind != ArgumentKind.Explicit
                        || argument.Parameter == null
                    )
                    {
                        continue;
                    }

                    if (argument.Parameter.Ordinal == ordinal)
                    {
                        return argument.Value;
                    }
                }

                return null;
            }

            private void Report(
                OperationAnalysisContext context,
                IOperation comparison,
                IOperation candidate
            )
            {
                if (!(candidate is IInvocationOperation invocation))
                {
                    return;
                }

                if (
                    !TryDescribeReplacement(
                        invocation,
                        out string outForm,
                        out string existenceForm
                    )
                )
                {
                    return;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        UnityHelpersDiagnostics.GetComponentComparedAgainstNull,
                        comparison.Syntax.GetLocation(),
                        invocation.Syntax.ToString(),
                        outForm,
                        existenceForm
                    )
                );
            }

            /// <summary>
            /// Whether <paramref name="invocation"/> is a same-object <c>GetComponent</c>, and if so
            /// how the author should spell its replacement.
            /// </summary>
            /// <param name="invocation">The call standing on one side of the null comparison.</param>
            /// <param name="outForm">
            /// Receives the <c>TryGetComponent</c> call to write instead.
            /// </param>
            /// <param name="existenceForm">
            /// Receives the shortest spelling of "is one there", which is <c>HasComponent</c>
            /// wherever its <c>where T : Object</c> constraint permits it.
            /// </param>
            /// <returns><c>false</c> for every call this diagnostic cannot name a fix for.</returns>
            private bool TryDescribeReplacement(
                IInvocationOperation invocation,
                out string outForm,
                out string existenceForm
            )
            {
                outForm = null;
                existenceForm = null;

                IMethodSymbol target = invocation.TargetMethod;
                if (target == null || target.Name != GetComponentMethodName)
                {
                    return false;
                }

                if (!DeclaresSameObjectSearch(target.ContainingType))
                {
                    return false;
                }

                if (target.Parameters.Length == 0)
                {
                    if (target.TypeArguments.Length != 1)
                    {
                        return false;
                    }

                    ITypeSymbol searched = target.TypeArguments[0];
                    string searchedName = searched.ToDisplayString(
                        SymbolDisplayFormat.MinimallyQualifiedFormat
                    );
                    outForm = "TryGetComponent(out " + searchedName + " value)";
                    existenceForm = IsUnityObject(searched)
                        ? "HasComponent<" + searchedName + ">()"
                        : "TryGetComponent(out " + searchedName + " _)";
                    return true;
                }

                if (target.Parameters.Length != 1 || !IsSystemType(target.Parameters[0].Type))
                {
                    return false;
                }

                string searchedType = ArgumentTextAt(invocation, 0);
                if (searchedType == null)
                {
                    return false;
                }

                /*
                    GetComponent(null) asks for nothing and throws at runtime as written; the Try
                    spelling of it is the same throw with different syntax. The rule names a fix
                    that exists, and none does here, so it stays silent.
                */
                if (IsNull(ArgumentValueAt(invocation, 0)))
                {
                    return false;
                }

                outForm =
                    "TryGetComponent(" + searchedType + ", out " + ComponentDisplayName + " value)";
                existenceForm = "HasComponent(" + searchedType + ")";
                return true;
            }

            /// <summary>
            /// Whether <paramref name="type"/> is one of the two types that declare the same-object
            /// <c>GetComponent</c>.
            /// </summary>
            private bool DeclaresSameObjectSearch(INamedTypeSymbol type)
            {
                if (type == null)
                {
                    return false;
                }

                return SymbolEqualityComparer.Default.Equals(type, _component)
                    || SymbolEqualityComparer.Default.Equals(type, _gameObject);
            }

            private bool IsSystemType(ITypeSymbol type)
            {
                return _systemType != null
                    && SymbolEqualityComparer.Default.Equals(type, _systemType);
            }

            /// <summary>
            /// Whether <paramref name="type"/> satisfies <c>HasComponent</c>'s
            /// <c>where T : Object</c> constraint.
            /// </summary>
            /// <remarks>
            /// <c>GetComponent&lt;T&gt;()</c> is unconstrained and an interface is a normal type
            /// argument for it, so this is the question that decides whether <c>HasComponent</c> can
            /// be named at all.
            /// </remarks>
            private bool IsUnityObject(ITypeSymbol type)
            {
                if (type == null || _unityObject == null)
                {
                    return false;
                }

                if (type is ITypeParameterSymbol typeParameter)
                {
                    foreach (ITypeSymbol constraint in typeParameter.ConstraintTypes)
                    {
                        if (IsUnityObject(constraint))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                for (
                    ITypeSymbol candidate = type;
                    candidate != null;
                    candidate = candidate.BaseType
                )
                {
                    if (SymbolEqualityComparer.Default.Equals(candidate, _unityObject))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
