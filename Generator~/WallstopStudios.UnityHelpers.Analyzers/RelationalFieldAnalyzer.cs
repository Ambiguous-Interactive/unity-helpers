// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;

    /// <summary>Reports unsupported relational field declarations before binding.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RelationalFieldAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.UnsupportedRelationalField);

        private static void AnalyzeType(
            SymbolAnalysisContext context,
            INamedTypeSymbol relationalAttribute,
            INamedTypeSymbol component,
            INamedTypeSymbol list,
            INamedTypeSymbol set
        )
        {
            foreach (ISymbol member in ((INamedTypeSymbol)context.Symbol).GetMembers())
            {
                if (!(member is IFieldSymbol field) || field.IsStatic || field.IsConst)
                {
                    continue;
                }
                bool relational = false;
                foreach (AttributeData attribute in field.GetAttributes())
                {
                    if (IsDerivedFrom(attribute.AttributeClass, relationalAttribute))
                    {
                        relational = true;
                        break;
                    }
                }
                if (!relational || IsSupported(field.Type, component, list, set))
                {
                    continue;
                }
                foreach (Location location in field.Locations)
                {
                    if (location.IsInSource)
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                UnityHelpersDiagnostics.UnsupportedRelationalField,
                                location,
                                field.AssociatedSymbol?.Name ?? field.Name,
                                field.Type.ToDisplayString()
                            )
                        );
                    }
                }
            }
        }

        private static bool IsDerivedFrom(INamedTypeSymbol type, INamedTypeSymbol target)
        {
            for (INamedTypeSymbol current = type; current != null; current = current.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(current, target))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsSupported(
            ITypeSymbol type,
            INamedTypeSymbol component,
            INamedTypeSymbol list,
            INamedTypeSymbol set
        )
        {
            ITypeSymbol element = type;
            if (type is IArrayTypeSymbol array)
            {
                if (array.Rank != 1 || !array.IsSZArray)
                {
                    return false;
                }
                element = array.ElementType;
            }
            else if (
                type is INamedTypeSymbol named
                && named.IsGenericType
                && named.TypeArguments.Length == 1
                && (
                    SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, list)
                    || SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, set)
                )
            )
            {
                element = named.TypeArguments[0];
            }
            if (
                element.TypeKind == TypeKind.Error
                || element.TypeKind == TypeKind.TypeParameter
                || element.TypeKind == TypeKind.Interface
                || element.TypeKind == TypeKind.Dynamic
            )
            {
                return true;
            }
            return element is INamedTypeSymbol candidate
                && (IsDerivedFrom(candidate, component) || IsDerivedFrom(component, candidate));
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterCompilationStartAction(start =>
            {
                INamedTypeSymbol attribute = start.Compilation.GetTypeByMetadataName(
                    "WallstopStudios.UnityHelpers.Core.Attributes.BaseRelationalComponentAttribute"
                );
                INamedTypeSymbol component = start.Compilation.GetTypeByMetadataName(
                    "UnityEngine.Component"
                );
                if (attribute == null || component == null)
                {
                    return;
                }
                INamedTypeSymbol list = start.Compilation.GetTypeByMetadataName(
                    "System.Collections.Generic.List`1"
                );
                INamedTypeSymbol set = start.Compilation.GetTypeByMetadataName(
                    "System.Collections.Generic.HashSet`1"
                );
                start.RegisterSymbolAction(
                    symbol => AnalyzeType(symbol, attribute, component, list, set),
                    SymbolKind.NamedType
                );
            });
        }
    }
}
