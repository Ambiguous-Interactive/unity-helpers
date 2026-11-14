// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.FlowAnalysis;
    using Microsoft.CodeAnalysis.Operations;

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
    /// Nested calls use the method's control-flow graph, including normal successors and finally
    /// regions, so work on another branch is not mistaken for work after the call.
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
            System.StringComparer.Ordinal,
            "OnDestroy",
            "OnDisable",
            "OnApplicationQuit",
            DisposeHook
        );

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.TeardownBaseCallIsNotLast);

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
            for (int index = 0; index < statements.Count; ++index)
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

            ControlFlowGraph graph = null;
            bool graphAttempted = false;
            foreach (SyntaxNode node in body.DescendantNodes(DescendIntoTeardownBody))
            {
                if (!(node is StatementSyntax statement) || statement.Parent == body)
                {
                    continue;
                }
                InvocationExpressionSyntax invocation = AsBaseCall(statement, name);
                if (invocation == null)
                {
                    continue;
                }
                if (!graphAttempted)
                {
                    graphAttempted = true;
                    try
                    {
                        graph = ControlFlowGraph.Create(
                            method,
                            context.SemanticModel,
                            context.CancellationToken
                        );
                    }
                    catch (ArgumentException) { }
                    catch (InvalidOperationException) { }
                }
                if (graph != null && HasFollowingWork(graph, invocation))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            UnityHelpersDiagnostics.TeardownBaseCallIsNotLast,
                            invocation.GetLocation(),
                            invocation.ToString(),
                            1
                        )
                    );
                }
            }
        }

        private static bool DescendIntoTeardownBody(SyntaxNode node)
        {
            return !(node is AnonymousFunctionExpressionSyntax)
                && !(node is LocalFunctionStatementSyntax);
        }

        private static bool HasFollowingWork(
            ControlFlowGraph graph,
            InvocationExpressionSyntax invocation
        )
        {
            foreach (BasicBlock block in graph.Blocks)
            {
                if (!block.IsReachable)
                {
                    continue;
                }
                for (int index = 0; index < block.Operations.Length; ++index)
                {
                    foreach (IOperation operation in block.Operations[index].DescendantsAndSelf())
                    {
                        if (
                            operation is IInvocationOperation
                            && operation.Syntax.Span == invocation.Span
                        )
                        {
                            if (index + 1 < block.Operations.Length || block.BranchValue != null)
                            {
                                return true;
                            }
                            return HasReachableWork(graph, block);
                        }
                    }
                }
            }
            return false;
        }

        private static bool HasReachableWork(ControlFlowGraph graph, BasicBlock start)
        {
            Queue<BasicBlock> pending = new Queue<BasicBlock>();
            HashSet<int> visited = new HashSet<int>();
            pending.Enqueue(start);
            while (0 < pending.Count)
            {
                BasicBlock block = pending.Dequeue();
                if (!visited.Add(block.Ordinal))
                {
                    continue;
                }
                if (
                    VisitSuccessor(graph, block.FallThroughSuccessor, pending)
                    || VisitSuccessor(graph, block.ConditionalSuccessor, pending)
                )
                {
                    return true;
                }
            }
            return false;
        }

        private static bool VisitSuccessor(
            ControlFlowGraph graph,
            ControlFlowBranch branch,
            Queue<BasicBlock> pending
        )
        {
            if (branch == null)
            {
                return false;
            }
            foreach (ControlFlowRegion region in branch.FinallyRegions)
            {
                for (
                    int index = region.FirstBlockOrdinal;
                    index <= region.LastBlockOrdinal;
                    ++index
                )
                {
                    BasicBlock cleanup = graph.Blocks[index];
                    if (cleanup.Operations.Length != 0 || cleanup.BranchValue != null)
                    {
                        return true;
                    }
                }
            }
            BasicBlock destination = branch.Destination;
            if (destination != null && destination.IsReachable)
            {
                if (destination.Operations.Length != 0 || destination.BranchValue != null)
                {
                    return true;
                }
                pending.Enqueue(destination);
            }
            return false;
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
            return !string.Equals(name, DisposeHook, System.StringComparison.Ordinal)
                || method.ParameterList.Parameters.Count == 0;
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
                && string.Equals(
                    memberAccess.Name.Identifier.ValueText,
                    name,
                    System.StringComparison.Ordinal
                );
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
            for (int index = start; index < statements.Count; ++index)
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

                ++count;
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
    }
}
