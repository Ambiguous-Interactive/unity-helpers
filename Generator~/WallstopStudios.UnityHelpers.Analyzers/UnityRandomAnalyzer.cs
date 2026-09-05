// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports any use of <c>UnityEngine.Random</c>, whose process-global state a test can neither
    /// set nor read without disturbing every other caller.
    /// </summary>
    /// <remarks>
    /// The match is semantic rather than textual because the two dodges are free: a
    /// <c>using R = UnityEngine.Random;</c> alias and a <c>using static UnityEngine.Random;</c> both
    /// leave no <c>Random.</c> token to grep for, and the second leaves no qualifier at all.
    /// <c>System.Random</c> is a different mistake and is deliberately out of scope -- conflating
    /// the two makes the fix text ("use <c>PRNG.Instance</c>") wrong half the time it appears
    /// (#622).
    /// <para>
    /// Naming the nested <c>State</c> type reports under the same id, but it is not the same
    /// mistake: <c>UnityEngine.Random.State snapshot;</c> draws nothing, so the message speaks of
    /// being tied to the engine generator rather than of reading it, and offers
    /// <c>RandomState</c> beside <c>PRNG.Instance</c>. A second id was rejected because it would
    /// have escaped every <c>#pragma warning disable WUH005</c> a consumer had already written
    /// around a deliberate engine save/restore.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class UnityRandomAnalyzer : DiagnosticAnalyzer
    {
        private const string UnityRandomMetadataName = "UnityEngine.Random";

        /// <summary>
        /// The package's own <c>IRandom</c> adapter over the engine generator, which is the one type
        /// whose entire job is to call <c>UnityEngine.Random</c>.
        /// </summary>
        /// <remarks>
        /// Scoped to the type rather than the namespace, so the twenty seedable generators that sit
        /// beside it in <c>WallstopStudios.UnityHelpers.Core.Random</c> are still covered.
        /// </remarks>
        private const string EngineWrapperTypeName =
            "WallstopStudios.UnityHelpers.Core.Random.UnityRandom";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.UnityRandomIsNotReplayable);

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            INamedTypeSymbol unityRandom = context.Compilation.GetTypeByMetadataName(
                UnityRandomMetadataName
            );
            if (unityRandom == null)
            {
                return;
            }

            context.RegisterOperationAction(
                operationContext => OnOperation(operationContext, unityRandom),
                OperationKind.Invocation,
                OperationKind.PropertyReference,
                OperationKind.FieldReference,
                OperationKind.ObjectCreation
            );

            context.RegisterSyntaxNodeAction(
                syntaxContext => OnName(syntaxContext, unityRandom),
                SyntaxKind.IdentifierName
            );
        }

        private static void OnOperation(
            OperationAnalysisContext context,
            INamedTypeSymbol unityRandom
        )
        {
            IOperation operation = context.Operation;
            ISymbol member;
            switch (operation)
            {
                case IInvocationOperation invocation:
                    member = invocation.TargetMethod;
                    break;
                case IPropertyReferenceOperation property:
                    member = property.Property;
                    break;
                case IFieldReferenceOperation field:
                    member = field.Field;
                    break;
                case IObjectCreationOperation creation:

                    member = creation.Type;
                    break;
                default:
                    return;
            }

            if (
                member == null
                || !SymbolEqualityComparer.Default.Equals(member.ContainingType, unityRandom)
                || IsExempt(context.ContainingSymbol, unityRandom)
            )
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.UnityRandomIsNotReplayable,
                    MemberLocation(operation.Syntax),
                    member.Name
                )
            );
        }

        private static void OnName(SyntaxNodeAnalysisContext context, INamedTypeSymbol unityRandom)
        {
            // Nested types in type positions have no operation node.
            if (
                !(
                    context
                        .SemanticModel.GetSymbolInfo(context.Node, context.CancellationToken)
                        .Symbol
                    is INamedTypeSymbol named
                ) || !SymbolEqualityComparer.Default.Equals(named.ContainingType, unityRandom)
            )
            {
                return;
            }

            // Object creation already reports this type; reporting its name would duplicate the diagnostic.
            if (
                IsObjectCreationType(context.Node)
                || IsExempt(context.ContainingSymbol, unityRandom)
            )
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.UnityRandomIsNotReplayable,
                    context.Node.GetLocation(),
                    named.Name
                )
            );
        }

        private static bool IsObjectCreationType(SyntaxNode node)
        {
            SyntaxNode outermost = node;
            while (outermost.Parent is NameSyntax)
            {
                outermost = outermost.Parent;
            }

            return outermost.Parent is ObjectCreationExpressionSyntax creation
                && creation.Type == outermost;
        }

        /// <summary>
        /// Reports the tightest location that names the member, rather than the whole expression the
        /// member happens to sit inside.
        /// </summary>
        private static Location MemberLocation(SyntaxNode syntax)
        {
            SyntaxNode node = syntax;
            if (node is InvocationExpressionSyntax invocation)
            {
                node = invocation.Expression;
            }

            if (node is MemberAccessExpressionSyntax memberAccess)
            {
                return memberAccess.Name.GetLocation();
            }

            if (node is ObjectCreationExpressionSyntax creation)
            {
                return creation.Type.GetLocation();
            }

            return node.GetLocation();
        }

        /// <summary>
        /// Whether the code doing the drawing is allowed to.
        /// </summary>
        /// <remarks>
        /// Two types are: the package's adapter, and <c>UnityEngine.Random</c> itself, which a
        /// source-declared stand-in makes reachable and which would otherwise report every member it
        /// declares against itself.
        /// </remarks>
        private static bool IsExempt(ISymbol containing, INamedTypeSymbol unityRandom)
        {
            INamedTypeSymbol type = containing as INamedTypeSymbol;
            if (type == null)
            {
                type = containing?.ContainingType;
            }

            while (type != null)
            {
                if (
                    SymbolEqualityComparer.Default.Equals(type, unityRandom)
                    || EngineWrapperTypeName == FullName(type)
                )
                {
                    return true;
                }

                type = type.ContainingType;
            }

            return false;
        }

        private static string FullName(INamedTypeSymbol type)
        {
            INamespaceSymbol containingNamespace = type.ContainingNamespace;
            if (containingNamespace == null || containingNamespace.IsGlobalNamespace)
            {
                return type.MetadataName;
            }

            return containingNamespace.ToDisplayString() + "." + type.MetadataName;
        }
    }
}
