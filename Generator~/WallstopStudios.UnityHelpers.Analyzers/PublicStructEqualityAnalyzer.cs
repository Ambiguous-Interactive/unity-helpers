// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    /// <summary>Reports public value types that lack typed equality.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class PublicStructEqualityAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.PublicStructMissingTypedEquality);

        private static void AnalyzeType(SymbolAnalysisContext context, INamedTypeSymbol equatable)
        {
            INamedTypeSymbol type = (INamedTypeSymbol)context.Symbol;
            if (type.TypeKind != TypeKind.Struct || type.IsRefLikeType || type.IsImplicitlyDeclared)
            {
                return;
            }
            for (INamedTypeSymbol owner = type; owner != null; owner = owner.ContainingType)
            {
                if (owner.DeclaredAccessibility != Accessibility.Public)
                {
                    return;
                }
            }
            foreach (INamedTypeSymbol implemented in type.AllInterfaces)
            {
                if (
                    SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, equatable)
                    && SymbolEqualityComparer.Default.Equals(implemented.TypeArguments[0], type)
                )
                {
                    return;
                }
            }
            foreach (Location location in type.Locations)
            {
                if (location.IsInSource)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            UnityHelpersDiagnostics.PublicStructMissingTypedEquality,
                            location,
                            type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)
                        )
                    );
                    return;
                }
            }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(start =>
            {
                INamedTypeSymbol equatable = start
                    .Compilation.GetSpecialType(SpecialType.System_Object)
                    .ContainingAssembly.GetTypeByMetadataName("System.IEquatable`1");
                if (equatable == null)
                {
                    return;
                }
                start.RegisterSymbolAction(
                    symbol => AnalyzeType(symbol, equatable),
                    SymbolKind.NamedType
                );
            });
        }
    }
}
