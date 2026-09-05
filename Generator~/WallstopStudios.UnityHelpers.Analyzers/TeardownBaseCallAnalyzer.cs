// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    /// <summary>
    /// Reports a teardown override -- <c>OnDestroy</c>, <c>OnDisable</c>,
    /// <c>OnApplicationQuit</c>, <c>Dispose</c> -- whose <c>base</c> call is followed by more of the
    /// body.
    /// </summary>
    /// <remarks>
    /// Setup chains base-FIRST and teardown chains base-LAST, and this analyzer covers only the
    /// teardown half. That asymmetry is the whole point: base-first is correct in <c>Awake</c> and
    /// <c>OnEnable</c> a few lines up the same file, so writing it in <c>OnDestroy</c> is a natural
    /// mistake rather than a careless one, and "always call base first" is wrong advice (#630).
    /// <para>
    /// There is deliberately no allow-list for a body that "only logs afterwards". Moving one line
    /// is cheaper than writing a suppression, and an exception list reads as permission to leave
    /// the call where it is.
    /// </para>
    /// <para>
    /// Only a <c>base</c> call that is a DIRECT top-level statement of the method body is
    /// considered. One nested inside an <c>if</c>, <c>try</c> or <c>using</c> is genuinely harder to
    /// judge -- what follows it at its own nesting level may or may not be what follows it on the
    /// executed path -- so it is left alone rather than reported on a guess.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class TeardownBaseCallAnalyzer : DiagnosticAnalyzer
    {
        private const string DisposeHook = "Dispose";

        /// <summary>
        /// The teardown hooks whose base call has to come last.
        /// </summary>
        /// <remarks>
        /// <c>OnDestroy</c> and <c>OnApplicationQuit</c> are what this package itself declares
        /// <c>protected virtual</c> (<c>Runtime/Utils/RuntimeSingleton.cs</c>,
        /// <c>Runtime/Tags/AttributesComponent.cs</c>, <c>Runtime/Tags/CosmeticEffectComponent.cs</c>),
        /// which is what makes the defect reachable through this package's own base classes.
        /// <c>OnDisable</c> and <c>Dispose</c> are here for a consumer's own hierarchy: both are
        /// release points by contract, and neither needs a package type to be wrong.
        /// <para>
        /// <c>Dispose</c> counts only at the PARAMETERLESS arity. <c>Dispose(bool disposing)</c> is
        /// the BCL's own disposal protocol -- <c>Stream</c>, <c>HttpContent</c>,
        /// <c>DbConnection</c> -- where chaining <c>base.Dispose(disposing)</c> FIRST is the
        /// documented convention rather than a mistake, so every consumer override of it was being
        /// reported for being written correctly.
        /// </para>
        /// </remarks>
        private static readonly ImmutableHashSet<string> TeardownHooks = ImmutableHashSet.Create(
            "OnDestroy",
            "OnDisable",
            "OnApplicationQuit",
            DisposeHook
        );

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.TeardownBaseCallIsNotLast);

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            /*
             * Syntax actions still locate trailing statements when missing Unity references prevent operation
             * binding.
             */
            context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
        }

        private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
        {
            MethodDeclarationSyntax method = (MethodDeclarationSyntax)context.Node;
            BlockSyntax body = method.Body;

            if (body == null || !method.Modifiers.Any(SyntaxKind.OverrideKeyword))
            {
                return;
            }

            string name = method.Identifier.ValueText;
            if (!TeardownHooks.Contains(name) || !HasTeardownArity(name, method))
            {
                return;
            }

            SyntaxList<StatementSyntax> statements = body.Statements;
            for (int index = 0; index < statements.Count; index++)
            {
                InvocationExpressionSyntax invocation = AsBaseCall(statements[index], name);
                if (invocation == null)
                {
                    continue;
                }

                int following = CountExecutedStatements(statements, index + 1);
                if (following <= 0)
                {
                    continue;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        UnityHelpersDiagnostics.TeardownBaseCallIsNotLast,
                        invocation.GetLocation(),
                        invocation.ToString(),
                        following
                    )
                );
            }
        }

        /// <summary>
        /// Whether this declaration is the teardown hook rather than a same-named member of another
        /// protocol.
        /// </summary>
        /// <remarks>
        /// <c>Dispose(bool disposing)</c> is the BCL's disposal pattern, whose convention is
        /// base-FIRST; only <c>Dispose()</c> is the release point this rule is about.
        /// </remarks>
        private static bool HasTeardownArity(string name, MethodDeclarationSyntax method)
        {
            return name != DisposeHook || method.ParameterList.Parameters.Count == 0;
        }

        /// <summary>
        /// The invocation in <paramref name="statement"/> when it is <c>base.{name}(...)</c> and
        /// nothing else, otherwise <c>null</c>.
        /// </summary>
        private static InvocationExpressionSyntax AsBaseCall(StatementSyntax statement, string name)
        {
            if (!(statement is ExpressionStatementSyntax expressionStatement))
            {
                return null;
            }

            if (!(expressionStatement.Expression is InvocationExpressionSyntax invocation))
            {
                return null;
            }

            if (!(invocation.Expression is MemberAccessExpressionSyntax memberAccess))
            {
                return null;
            }

            // A differently named base call does not chain this lifecycle method.
            bool isSameHook =
                memberAccess.Expression is BaseExpressionSyntax
                && memberAccess.Name.Identifier.ValueText == name;
            return isSameHook ? invocation : null;
        }

        /// <summary>
        /// How many of the statements from <paramref name="start"/> onwards actually run.
        /// </summary>
        /// <remarks>
        /// A local function after the base call is a declaration, not work the base call was moved
        /// ahead of; the same goes for a stray <c>;</c> and for a bare trailing <c>return;</c>,
        /// which runs nothing. Counting any of them would report a body whose base call already is
        /// last.
        /// </remarks>
        private static int CountExecutedStatements(
            SyntaxList<StatementSyntax> statements,
            int start
        )
        {
            int count = 0;
            for (int index = start; index < statements.Count; index++)
            {
                StatementSyntax statement = statements[index];
                if (
                    statement is LocalFunctionStatementSyntax
                    || statement is EmptyStatementSyntax
                    || IsBareReturn(statement)
                )
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        /// <summary>
        /// Whether <paramref name="statement"/> is <c>return;</c> with no value, which executes
        /// nothing the base call could have been moved ahead of.
        /// </summary>
        private static bool IsBareReturn(StatementSyntax statement)
        {
            return statement is ReturnStatementSyntax returnStatement
                && returnStatement.Expression == null;
        }
    }
}
