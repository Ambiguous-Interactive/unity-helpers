// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports string equality operators that do not state their comparison policy.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StringEqualityAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.StringEqualityHasImplicitComparison);

        private static bool IsNull(IOperation operation)
        {
            Optional<object> constant = operation.ConstantValue;
            return constant.HasValue && constant.Value == null;
        }

        private static void OnBinaryOperator(OperationAnalysisContext context)
        {
            IBinaryOperation binary = (IBinaryOperation)context.Operation;
            if (
                binary.OperatorKind != BinaryOperatorKind.Equals
                && binary.OperatorKind != BinaryOperatorKind.NotEquals
            )
            {
                return;
            }

            if (IsNull(binary.LeftOperand) || IsNull(binary.RightOperand))
            {
                return;
            }

            if (
                binary.LeftOperand.Type == null
                || binary.RightOperand.Type == null
                || binary.LeftOperand.Type.SpecialType != SpecialType.System_String
                || binary.RightOperand.Type.SpecialType != SpecialType.System_String
            )
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.StringEqualityHasImplicitComparison,
                    binary.Syntax.GetLocation(),
                    binary.OperatorKind == BinaryOperatorKind.Equals ? "==" : "!="
                )
            );
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterOperationAction(OnBinaryOperator, OperationKind.BinaryOperator);
        }
    }
}
