// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>Reports string methods that leave their comparison or culture policy implicit.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class StringComparisonPolicyAnalyzer : DiagnosticAnalyzer
    {
        private static readonly ImmutableArray<DiagnosticDescriptor> SupportedRules =
            ImmutableArray.Create(UnityHelpersDiagnostics.StringMethodHasImplicitComparison);

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => SupportedRules;

        private static void AnalyzeInvocation(OperationAnalysisContext context)
        {
            IInvocationOperation invocation = (IInvocationOperation)context.Operation;
            IMethodSymbol method = invocation.TargetMethod;
            if (method.ContainingType.SpecialType != SpecialType.System_String)
            {
                return;
            }

            switch (method.Name)
            {
                case nameof(string.Equals):
                case nameof(string.Compare):
                case nameof(string.StartsWith):
                case nameof(string.EndsWith):
                case nameof(string.IndexOf):
                case nameof(string.LastIndexOf):
                case nameof(string.ToLower):
                case nameof(string.ToUpper):
                    break;
                default:
                    return;
            }

            bool hasStringParameter = false;
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                ITypeSymbol type = parameter.Type;
                if (type.SpecialType == SpecialType.System_String)
                {
                    hasStringParameter = true;
                }
                if (
                    (
                        string.Equals(type.Name, nameof(StringComparison), StringComparison.Ordinal)
                        && string.Equals(
                            type.ContainingNamespace.ToDisplayString(),
                            "System",
                            StringComparison.Ordinal
                        )
                    )
                    || (
                        string.Equals(
                            type.Name,
                            nameof(System.Globalization.CultureInfo),
                            StringComparison.Ordinal
                        )
                        && string.Equals(
                            type.ContainingNamespace.ToDisplayString(),
                            "System.Globalization",
                            StringComparison.Ordinal
                        )
                    )
                )
                {
                    return;
                }
            }

            if (
                !hasStringParameter
                && !string.Equals(method.Name, nameof(string.ToLower), StringComparison.Ordinal)
                && !string.Equals(method.Name, nameof(string.ToUpper), StringComparison.Ordinal)
            )
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.StringMethodHasImplicitComparison,
                    invocation.Syntax.GetLocation(),
                    method.Name
                )
            );
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterOperationAction(AnalyzeInvocation, OperationKind.Invocation);
        }
    }
}
