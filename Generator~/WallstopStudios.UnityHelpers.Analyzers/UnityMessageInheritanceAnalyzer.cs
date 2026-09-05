// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    /// <summary>
    /// Reports Unity callbacks that hide a callback declared by an ancestor.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnityMessageInheritanceAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>
        /// Gets the callback inheritance diagnostic.
        /// </summary>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.HiddenUnityCallback);

        /// <summary>
        /// Registers semantic callback inheritance analysis.
        /// </summary>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                INamedTypeSymbol monoBehaviour = start.Compilation.GetTypeByMetadataName(
                    "UnityEngine.MonoBehaviour"
                );
                INamedTypeSymbol scriptableObject = start.Compilation.GetTypeByMetadataName(
                    "UnityEngine.ScriptableObject"
                );
                INamedTypeSymbol suppression = start.Compilation.GetTypeByMetadataName(
                    "WallstopStudios.UnityHelpers.Tests.Core.SuppressAnalyzerAttribute"
                );
                start.RegisterSymbolAction(
                    symbolContext =>
                    {
                        IMethodSymbol method = (IMethodSymbol)symbolContext.Symbol;
                        bool isMonoBehaviour = UnityLifecycleAnalyzer.DerivesFrom(
                            method.ContainingType,
                            monoBehaviour
                        );
                        if (
                            method.MethodKind != MethodKind.Ordinary
                            || method.IsOverride
                            || method.IsStatic
                            || method.IsImplicitlyDeclared
                            || method.Arity != 0
                            || (
                                !isMonoBehaviour
                                && !UnityLifecycleAnalyzer.DerivesFrom(
                                    method.ContainingType,
                                    scriptableObject
                                )
                            )
                            || !UnityLifecycleAnalyzer.IsCallback(
                                method.Name,
                                isMonoBehaviour,
                                UnityLifecycleAnalyzer.DerivesFrom(
                                    method.ContainingType,
                                    start.Compilation.GetTypeByMetadataName(
                                        "UnityEditor.EditorWindow"
                                    )
                                ),
                                UnityLifecycleAnalyzer.DerivesFrom(
                                    method.ContainingType,
                                    start.Compilation.GetTypeByMetadataName("UnityEditor.Editor")
                                )
                            )
                            || !UnityLifecycleAnalyzer.HasValidSignature(method, start.Compilation)
                            || UnityLifecycleAnalyzer.IsSuppressed(method, suppression)
                            || UnityLifecycleAnalyzer.IsSuppressed(
                                method.ContainingType,
                                suppression
                            )
                        )
                        {
                            return;
                        }

                        for (
                            INamedTypeSymbol ancestor = method.ContainingType.BaseType;
                            ancestor != null;
                            ancestor = ancestor.BaseType
                        )
                        {
                            foreach (ISymbol member in ancestor.GetMembers(method.Name))
                            {
                                if (
                                    member is not IMethodSymbol inherited
                                    || inherited.IsStatic
                                    || inherited.MethodKind != MethodKind.Ordinary
                                    || inherited.Arity != 0
                                    || !UnityLifecycleAnalyzer.HasValidSignature(
                                        inherited,
                                        start.Compilation
                                    )
                                    || !SameParameters(method, inherited)
                                )
                                {
                                    continue;
                                }
                                symbolContext.ReportDiagnostic(
                                    Diagnostic.Create(
                                        UnityHelpersDiagnostics.HiddenUnityCallback,
                                        method.Locations[0],
                                        method.ToDisplayString(
                                            SymbolDisplayFormat.CSharpErrorMessageFormat
                                        ),
                                        inherited.ToDisplayString(
                                            SymbolDisplayFormat.CSharpErrorMessageFormat
                                        )
                                    )
                                );
                                return;
                            }
                        }
                    },
                    SymbolKind.Method
                );
            });
        }

        private static bool SameParameters(IMethodSymbol first, IMethodSymbol second)
        {
            if (first.Parameters.Length != second.Parameters.Length)
            {
                return false;
            }
            for (int index = 0; index < first.Parameters.Length; index++)
            {
                IParameterSymbol left = first.Parameters[index];
                IParameterSymbol right = second.Parameters[index];
                if (
                    left.RefKind != right.RefKind
                    || !SymbolEqualityComparer.Default.Equals(left.Type, right.Type)
                )
                {
                    return false;
                }
            }
            return true;
        }
    }
}
