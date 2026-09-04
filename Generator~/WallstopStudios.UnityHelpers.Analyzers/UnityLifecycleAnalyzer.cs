// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    /// <summary>
    /// Checks Unity lifecycle callback signatures against their resolved types.
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnityLifecycleAnalyzer : DiagnosticAnalyzer
    {
        /// <summary>
        /// Gets the lifecycle signature diagnostic.
        /// </summary>
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.InvalidUnityLifecycleSignature);

        /// <summary>
        /// Registers lifecycle callback analysis.
        /// </summary>
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(startContext =>
            {
                INamedTypeSymbol monoBehaviour = startContext.Compilation.GetTypeByMetadataName(
                    "UnityEngine.MonoBehaviour"
                );
                INamedTypeSymbol scriptableObject = startContext.Compilation.GetTypeByMetadataName(
                    "UnityEngine.ScriptableObject"
                );
                INamedTypeSymbol enumerator = startContext.Compilation.GetTypeByMetadataName(
                    "System.Collections.IEnumerator"
                );
                INamedTypeSymbol suppressionAttribute =
                    startContext.Compilation.GetTypeByMetadataName(
                        "WallstopStudios.UnityHelpers.Tests.Core.SuppressAnalyzerAttribute"
                    );
                if (monoBehaviour == null && scriptableObject == null)
                {
                    return;
                }

                startContext.RegisterSymbolAction(
                    symbolContext =>
                    {
                        IMethodSymbol method = (IMethodSymbol)symbolContext.Symbol;
                        if (
                            method.MethodKind != MethodKind.Ordinary
                            || method.IsImplicitlyDeclared
                            || method.IsOverride
                            || IsSuppressed(method, suppressionAttribute)
                            || IsSuppressed(method.ContainingType, suppressionAttribute)
                        )
                        {
                            return;
                        }

                        bool isMonoBehaviour = DerivesFrom(method.ContainingType, monoBehaviour);
                        if (
                            !isMonoBehaviour
                            && !DerivesFrom(method.ContainingType, scriptableObject)
                        )
                        {
                            return;
                        }

                        if (!IsCallback(method.Name, isMonoBehaviour))
                        {
                            return;
                        }

                        bool allowsCoroutine = isMonoBehaviour && method.Name == "Start";
                        bool validReturn =
                            method.ReturnsVoid
                            || (
                                allowsCoroutine
                                && SymbolEqualityComparer.Default.Equals(
                                    method.ReturnType,
                                    enumerator
                                )
                            );
                        if (
                            !method.IsStatic
                            && method.Arity == 0
                            && method.Parameters.Length == 0
                            && !method.ReturnsByRef
                            && !method.ReturnsByRefReadonly
                            && validReturn
                        )
                        {
                            return;
                        }

                        symbolContext.ReportDiagnostic(
                            Diagnostic.Create(
                                UnityHelpersDiagnostics.InvalidUnityLifecycleSignature,
                                method.Locations[0],
                                method.Name,
                                allowsCoroutine
                                    ? "return void or System.Collections.IEnumerator"
                                    : "return void"
                            )
                        );
                    },
                    SymbolKind.Method
                );
            });
        }

        private static bool DerivesFrom(INamedTypeSymbol candidate, INamedTypeSymbol expected)
        {
            if (expected == null)
            {
                return false;
            }

            for (
                INamedTypeSymbol ancestor = candidate.BaseType;
                ancestor != null;
                ancestor = ancestor.BaseType
            )
            {
                if (SymbolEqualityComparer.Default.Equals(ancestor, expected))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsSuppressed(ISymbol symbol, INamedTypeSymbol suppressionAttribute)
        {
            if (suppressionAttribute == null)
            {
                return false;
            }

            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (
                    SymbolEqualityComparer.Default.Equals(
                        attribute.AttributeClass,
                        suppressionAttribute
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCallback(string name, bool isMonoBehaviour)
        {
            switch (name)
            {
                case "Awake":
                case "OnEnable":
                case "OnDisable":
                case "OnDestroy":
                case "OnValidate":
                case "Reset":
                    return true;
                case "Start":
                case "Update":
                case "FixedUpdate":
                case "LateUpdate":
                case "OnApplicationQuit":
                    return isMonoBehaviour;
                default:
                    return false;
            }
        }
    }
}
