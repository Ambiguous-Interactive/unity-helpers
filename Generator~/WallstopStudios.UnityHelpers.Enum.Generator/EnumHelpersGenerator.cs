// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Enum.Generator
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Text;

    /// <summary>
    /// Emits per-enum extension overloads so concrete call sites bind directly to typed casts,
    /// literal tables, and reflection-free display names. The generic helpers remain the fallback.
    /// </summary>
    [Generator]
    public sealed class EnumHelpersGenerator : ISourceGenerator
    {
        private static bool HasAttribute(INamedTypeSymbol enumType, INamedTypeSymbol attribute)
        {
            foreach (AttributeData applied in enumType.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(applied.AttributeClass, attribute))
                {
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc />
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new EnumReceiver());
        }

        /// <inheritdoc />
        public void Execute(GeneratorExecutionContext context)
        {
            if (
                !(context.SyntaxReceiver is EnumReceiver receiver)
                || receiver.Candidates.Count == 0
            )
            {
                return;
            }

            INamedTypeSymbol attribute = context.Compilation.GetTypeByMetadataName(
                EnumGenerationPlan.AttributeMetadataName
            );
            if (attribute == null)
            {
                return;
            }

            HashSet<INamedTypeSymbol> seen = new(SymbolEqualityComparer.Default);
            HashSet<string> reservedClassNames = new(StringComparer.Ordinal);
            StringBuilder sourceBuilder = new();
            foreach (EnumDeclarationSyntax candidate in receiver.Candidates)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                SemanticModel model = context.Compilation.GetSemanticModel(candidate.SyntaxTree);
                if (
                    !(
                        model.GetDeclaredSymbol(candidate, context.CancellationToken)
                        is INamedTypeSymbol enumType
                    )
                )
                {
                    continue;
                }
                if (!seen.Add(enumType) || !HasAttribute(enumType, attribute))
                {
                    continue;
                }

                EnumGenerationPlan plan = EnumGenerationPlan.TryCreate(
                    enumType,
                    context.Compilation,
                    reservedClassNames,
                    out string reason
                );
                if (plan == null)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            WEnumDiagnostics.NotGenerated,
                            enumType.Locations.Length != 0 ? enumType.Locations[0] : Location.None,
                            enumType.ToDisplayString(),
                            reason
                        )
                    );
                    continue;
                }

                string source = EnumSourceWriter.Write(plan, sourceBuilder);
                context.AddSource(plan.HintName, SourceText.From(source, Encoding.UTF8));
            }
        }

        private sealed class EnumReceiver : ISyntaxReceiver
        {
            public List<EnumDeclarationSyntax> Candidates { get; } = new();

            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (
                    syntaxNode is EnumDeclarationSyntax declaration
                    && declaration.AttributeLists.Count != 0
                )
                {
                    Candidates.Add(declaration);
                }
            }
        }
    }
}
