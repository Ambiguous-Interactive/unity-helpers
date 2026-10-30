// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>Reports bitwise operators on boolean operands.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class BooleanBitwiseAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.BooleanBitwiseOperation);

        private static void Analyze(OperationAnalysisContext context)
        {
            BinaryOperatorKind kind;
            ITypeSymbol left;
            ITypeSymbol right;
            IMethodSymbol method;
            if (context.Operation is IBinaryOperation binary)
            {
                kind = binary.OperatorKind;
                left = binary.LeftOperand.Type;
                right = binary.RightOperand.Type;
                method = binary.OperatorMethod;
            }
            else if (context.Operation is ICompoundAssignmentOperation assignment)
            {
                kind = assignment.OperatorKind;
                left = assignment.Target.Type;
                right = assignment.Value.Type;
                method = assignment.OperatorMethod;
            }
            else
            {
                return;
            }
            if (method != null || !IsBoolean(left) || !IsBoolean(right))
            {
                return;
            }
            string replacement;
            switch (kind)
            {
                case BinaryOperatorKind.And:
                    replacement = "&&";
                    break;
                case BinaryOperatorKind.Or:
                    replacement = "||";
                    break;
                case BinaryOperatorKind.ExclusiveOr:
                    replacement = "!= or an explicit conditional toggle";
                    break;
                default:
                    return;
            }
            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.BooleanBitwiseOperation,
                    context.Operation.Syntax.GetLocation(),
                    replacement
                )
            );
        }

        private static bool IsBoolean(ITypeSymbol type)
        {
            return type != null
                && (
                    type.SpecialType == SpecialType.System_Boolean
                    || type is INamedTypeSymbol named
                        && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                        && named.TypeArguments.Length == 1
                        && named.TypeArguments[0].SpecialType == SpecialType.System_Boolean
                );
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterOperationAction(
                Analyze,
                OperationKind.Binary,
                OperationKind.CompoundAssignment
            );
        }
    }
}
