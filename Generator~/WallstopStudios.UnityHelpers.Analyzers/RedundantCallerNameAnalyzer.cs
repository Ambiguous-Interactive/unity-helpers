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

    /// <summary>Reports explicit caller names that the compiler can supply without changing overloads.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RedundantCallerNameAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.RedundantCallerName);

        private static void Analyze(OperationAnalysisContext context)
        {
            IArgumentOperation argument = (IArgumentOperation)context.Operation;
            if (
                argument.IsImplicit
                || argument.Parameter == null
                || argument.Parameter.Type.SpecialType != SpecialType.System_String
                || context.ContainingSymbol is not IMethodSymbol caller
                || caller.MethodKind != MethodKind.Ordinary
                || !argument.Value.ConstantValue.HasValue
                || argument.Value.ConstantValue.Value is not string name
                || !string.Equals(name, caller.Name, StringComparison.Ordinal)
                || argument.Syntax is not ArgumentSyntax syntax
                || syntax.Parent is not ArgumentListSyntax arguments
                || arguments.Parent is not InvocationExpressionSyntax invocation
            )
            {
                return;
            }
            INamedTypeSymbol callerAttribute = context.Compilation.GetTypeByMetadataName(
                "System.Runtime.CompilerServices.CallerMemberNameAttribute"
            );
            if (callerAttribute == null)
            {
                return;
            }
            bool hasCallerAttribute = false;
            foreach (AttributeData attribute in argument.Parameter.GetAttributes())
            {
                if (
                    SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, callerAttribute)
                )
                {
                    hasCallerAttribute = true;
                    break;
                }
            }
            if (!hasCallerAttribute)
            {
                return;
            }
            InvocationExpressionSyntax omitted = invocation.WithArgumentList(
                arguments.WithArguments(arguments.Arguments.Remove(syntax))
            );
            SemanticModel model = argument.SemanticModel;
            if (model == null)
            {
                return;
            }
            ISymbol original = model.GetSymbolInfo(invocation, context.CancellationToken).Symbol;
            ISymbol replacement = model
                .GetSpeculativeSymbolInfo(
                    invocation.SpanStart,
                    omitted,
                    SpeculativeBindingOption.BindAsExpression
                )
                .Symbol;
            if (original == null || !SymbolEqualityComparer.Default.Equals(original, replacement))
            {
                return;
            }
            INamedTypeSymbol expressionAttribute = context.Compilation.GetTypeByMetadataName(
                "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute"
            );
            if (expressionAttribute != null && original is IMethodSymbol method)
            {
                foreach (IParameterSymbol parameter in method.Parameters)
                {
                    foreach (AttributeData attribute in parameter.GetAttributes())
                    {
                        if (
                            SymbolEqualityComparer.Default.Equals(
                                attribute.AttributeClass,
                                expressionAttribute
                            )
                            && attribute.ConstructorArguments.Length == 1
                            && attribute.ConstructorArguments[0].Value is string referencedParameter
                            && string.Equals(
                                referencedParameter,
                                argument.Parameter.Name,
                                StringComparison.Ordinal
                            )
                        )
                        {
                            return;
                        }
                    }
                }
            }
            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.RedundantCallerName,
                    syntax.GetLocation(),
                    argument.Parameter.Name
                )
            );
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterOperationAction(Analyze, OperationKind.Argument);
        }
    }
}
