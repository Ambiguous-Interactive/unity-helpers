// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>Suggests typed enum helpers where the replacement preserves the operation.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class EnumHelperAnalyzer : DiagnosticAnalyzer
    {
        private const string ExtensionsMetadataName =
            "WallstopStudios.UnityHelpers.Core.Extension.EnumExtensions";
        private const string FlagMethodName = "HasFlagNoAlloc";
        private const string NameMethodName = "ToCachedName";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.EnumHelperAvailable);

        private static void Analyze(
            OperationAnalysisContext context,
            bool flags,
            bool names,
            INamedTypeSymbol expression
        )
        {
            for (
                IOperation ancestor = context.Operation;
                ancestor != null;
                ancestor = ancestor.Parent
            )
            {
                if (
                    ancestor.Type is INamedTypeSymbol type
                    && expression != null
                    && SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, expression)
                )
                {
                    return;
                }
            }
            string replacement = null;
            if (context.Operation is IInvocationOperation call)
            {
                if (call.TargetMethod.ContainingType.SpecialType != SpecialType.System_Enum)
                {
                    return;
                }
                IOperation receiver = Unwrap(call.Instance);
                if (receiver?.Type?.TypeKind != TypeKind.Enum)
                {
                    return;
                }
                if (
                    flags
                    && string.Equals(
                        call.TargetMethod.Name,
                        nameof(Enum.HasFlag),
                        StringComparison.Ordinal
                    )
                    && call.Arguments.Length == 1
                )
                {
                    IOperation argument = Unwrap(call.Arguments[0].Value);
                    if (SymbolEqualityComparer.Default.Equals(receiver.Type, argument?.Type))
                    {
                        replacement = FlagMethodName;
                    }
                }
                else if (
                    names
                    && string.Equals(
                        call.TargetMethod.Name,
                        nameof(ToString),
                        StringComparison.Ordinal
                    )
                    && call.Arguments.Length == 0
                )
                {
                    replacement = NameMethodName;
                }
            }
            else if (
                flags
                && context.Operation is IBinaryOperation comparison
                && comparison.OperatorKind == BinaryOperatorKind.Equals
            )
            {
                if (
                    IsAllBitsComparison(comparison.LeftOperand, comparison.RightOperand)
                    || IsAllBitsComparison(comparison.RightOperand, comparison.LeftOperand)
                )
                {
                    replacement = FlagMethodName;
                }
            }
            if (replacement != null)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        UnityHelpersDiagnostics.EnumHelperAvailable,
                        context.Operation.Syntax.GetLocation(),
                        replacement
                    )
                );
            }
        }

        private static bool HasHelper(
            INamedTypeSymbol extensions,
            string name,
            int parameters,
            SpecialType result
        )
        {
            foreach (ISymbol symbol in extensions.GetMembers(name))
            {
                if (
                    symbol is IMethodSymbol method
                    && method.IsExtensionMethod
                    && method.IsStatic
                    && method.DeclaredAccessibility == Accessibility.Public
                    && method.Arity == 1
                    && method.Parameters.Length == parameters
                    && method.ReturnType.SpecialType == result
                    && method.TypeParameters[0].HasUnmanagedTypeConstraint
                )
                {
                    bool sameType = true;
                    foreach (IParameterSymbol parameter in method.Parameters)
                    {
                        if (
                            !SymbolEqualityComparer.Default.Equals(
                                parameter.Type,
                                method.TypeParameters[0]
                            )
                        )
                        {
                            sameType = false;
                            break;
                        }
                    }
                    if (sameType)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsAllBitsComparison(IOperation candidate, IOperation expected)
        {
            candidate = Unwrap(candidate);
            expected = Unwrap(expected);
            if (
                !(candidate is IBinaryOperation bits)
                || bits.OperatorKind != BinaryOperatorKind.And
                || bits.Type?.TypeKind != TypeKind.Enum
                || expected?.Type?.TypeKind != TypeKind.Enum
                || !expected.ConstantValue.HasValue
                || !SymbolEqualityComparer.Default.Equals(bits.Type, expected.Type)
            )
            {
                return false;
            }
            return IsSameConstant(Unwrap(bits.LeftOperand), expected)
                || IsSameConstant(Unwrap(bits.RightOperand), expected);
        }

        private static bool IsSameConstant(IOperation actual, IOperation expected)
        {
            return actual != null
                && actual.ConstantValue.HasValue
                && SymbolEqualityComparer.Default.Equals(actual.Type, expected.Type)
                && Equals(actual.ConstantValue.Value, expected.ConstantValue.Value);
        }

        private static IOperation Unwrap(IOperation operation)
        {
            while (operation is IConversionOperation conversion && conversion.IsImplicit)
            {
                operation = conversion.Operand;
            }
            return operation;
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(start =>
            {
                INamedTypeSymbol extensions = start.Compilation.GetTypeByMetadataName(
                    ExtensionsMetadataName
                );
                if (extensions == null || extensions.DeclaredAccessibility != Accessibility.Public)
                {
                    return;
                }
                bool flags = HasHelper(extensions, FlagMethodName, 2, SpecialType.System_Boolean);
                bool names = HasHelper(extensions, NameMethodName, 1, SpecialType.System_String);
                INamedTypeSymbol expression = start.Compilation.GetTypeByMetadataName(
                    "System.Linq.Expressions.Expression`1"
                );
                start.RegisterOperationAction(
                    operation => Analyze(operation, flags, names, expression),
                    OperationKind.Invocation,
                    OperationKind.Binary
                );
            });
        }
    }
}
