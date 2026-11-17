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

        private static readonly ImmutableArray<DiagnosticDescriptor> SupportedRules =
            ImmutableArray.Create(UnityHelpersDiagnostics.TeardownBaseCallIsNotLast);

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => SupportedRules;

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
                if (HasRegionWork(graph, region))
                {
                    return true;
                }
            }
            if (
                branch.Semantics == ControlFlowBranchSemantics.Rethrow
                || branch.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling
            )
            {
                if (
                    HasUnwindingCleanup(
                        graph,
                        branch.Source,
                        branch.Semantics == ControlFlowBranchSemantics.Rethrow,
                        pending,
                        exceptionType: RethrownExceptionType(branch.Source)
                    )
                )
                {
                    return true;
                }
                if (branch.Semantics == ControlFlowBranchSemantics.StructuredExceptionHandling)
                {
                    for (
                        ControlFlowRegion region = branch.Source.EnclosingRegion;
                        region != null;
                        region = region.EnclosingRegion
                    )
                    {
                        if (region.Kind == ControlFlowRegionKind.Finally)
                        {
                            return HasFinallyContinuationWork(graph, region, pending);
                        }
                    }
                }
            }
            return QueueDestination(branch.Destination, pending);
        }

        private static bool HasRegionWork(ControlFlowGraph graph, ControlFlowRegion region)
        {
            for (int index = region.FirstBlockOrdinal; index <= region.LastBlockOrdinal; ++index)
            {
                BasicBlock cleanup = graph.Blocks[index];
                if (cleanup.Operations.Length != 0 || cleanup.BranchValue != null)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool HasUnwindingCleanup(
            ControlFlowGraph graph,
            BasicBlock source,
            bool includeHandlers,
            Queue<BasicBlock> pending,
            ControlFlowRegion completedFinally = null,
            ITypeSymbol exceptionType = null,
            ITypeSymbol alternativeExceptionType = null
        )
        {
            for (
                ControlFlowRegion region = completedFinally?.EnclosingRegion
                    ?? source.EnclosingRegion;
                region != null;
                region = region.EnclosingRegion
            )
            {
                if (includeHandlers && region.Kind == ControlFlowRegionKind.TryAndCatch)
                {
                    ControlFlowRegion body = region.NestedRegions[0];
                    if (
                        body.FirstBlockOrdinal <= source.Ordinal
                        && source.Ordinal <= body.LastBlockOrdinal
                    )
                    {
                        for (int index = 1; index < region.NestedRegions.Length; ++index)
                        {
                            ControlFlowRegion handler = region.NestedRegions[index];
                            ITypeSymbol handlerType = handler.ExceptionType;
                            if (
                                AreProvablyUnrelated(exceptionType, handlerType)
                                && (
                                    alternativeExceptionType == null
                                    || AreProvablyUnrelated(alternativeExceptionType, handlerType)
                                )
                            )
                            {
                                continue;
                            }
                            ControlFlowRegion destination = handler;
                            if (
                                completedFinally != null
                                && handler.Kind == ControlFlowRegionKind.FilterAndHandler
                            )
                            {
                                foreach (ControlFlowRegion nested in handler.NestedRegions)
                                {
                                    if (nested.Kind == ControlFlowRegionKind.Catch)
                                    {
                                        destination = nested;
                                        break;
                                    }
                                }
                            }
                            if (
                                QueueDestination(
                                    graph.Blocks[destination.FirstBlockOrdinal],
                                    pending
                                )
                            )
                            {
                                return true;
                            }
                            if (
                                handler.Kind == ControlFlowRegionKind.Catch
                                && ConsumeHandledExceptions(
                                    ref exceptionType,
                                    ref alternativeExceptionType,
                                    handlerType
                                )
                            )
                            {
                                return false;
                            }
                        }
                    }
                }
                if (region.Kind != ControlFlowRegionKind.TryAndFinally)
                {
                    continue;
                }
                foreach (ControlFlowRegion nested in region.NestedRegions)
                {
                    if (
                        nested.Kind == ControlFlowRegionKind.Finally
                        && nested != completedFinally
                        && (
                            source.Ordinal < nested.FirstBlockOrdinal
                            || nested.LastBlockOrdinal < source.Ordinal
                        )
                        && HasRegionWork(graph, nested)
                    )
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool HasFinallyContinuationWork(
            ControlFlowGraph graph,
            ControlFlowRegion region,
            Queue<BasicBlock> pending
        )
        {
            foreach (BasicBlock block in graph.Blocks)
            {
                if (
                    block.IsReachable
                    && (
                        ContinueAfterFinally(graph, block.FallThroughSuccessor, region, pending)
                        || ContinueAfterFinally(graph, block.ConditionalSuccessor, region, pending)
                    )
                )
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContinueAfterFinally(
            ControlFlowGraph graph,
            ControlFlowBranch branch,
            ControlFlowRegion region,
            Queue<BasicBlock> pending
        )
        {
            if (branch == null)
            {
                return false;
            }
            ControlFlowRegion body = region.EnclosingRegion?.NestedRegions[0];
            if (
                body != null
                && body.FirstBlockOrdinal <= branch.Source.Ordinal
                && branch.Source.Ordinal <= body.LastBlockOrdinal
                && (
                    branch.Semantics == ControlFlowBranchSemantics.Throw
                    || branch.Semantics == ControlFlowBranchSemantics.Rethrow
                )
            )
            {
                ITypeSymbol exceptionType = ThrownExceptionType(
                    graph,
                    branch,
                    out ITypeSymbol alternativeExceptionType
                );
                if (
                    !IsInterceptedBeforeFinally(
                        graph,
                        branch.Source,
                        region,
                        ref exceptionType,
                        ref alternativeExceptionType
                    )
                    && HasUnwindingCleanup(
                        graph,
                        branch.Source,
                        true,
                        pending,
                        region,
                        exceptionType,
                        alternativeExceptionType
                    )
                )
                {
                    return true;
                }
            }
            for (int index = 0; index < branch.FinallyRegions.Length; ++index)
            {
                if (branch.FinallyRegions[index] != region)
                {
                    continue;
                }
                for (
                    int following = index + 1;
                    following < branch.FinallyRegions.Length;
                    ++following
                )
                {
                    if (HasRegionWork(graph, branch.FinallyRegions[following]))
                    {
                        return true;
                    }
                }
                return QueueDestination(branch.Destination, pending);
            }
            return false;
        }

        private static bool IsInterceptedBeforeFinally(
            ControlFlowGraph graph,
            BasicBlock source,
            ControlFlowRegion completedFinally,
            ref ITypeSymbol exceptionType,
            ref ITypeSymbol alternativeExceptionType
        )
        {
            for (
                ControlFlowRegion region = source.EnclosingRegion;
                region != null && region != completedFinally.EnclosingRegion;
                region = region.EnclosingRegion
            )
            {
                if (region.Kind == ControlFlowRegionKind.Filter)
                {
                    return true;
                }
                if (region.Kind == ControlFlowRegionKind.TryAndFinally)
                {
                    ControlFlowRegion finallyBody = region.NestedRegions[0];
                    if (
                        finallyBody.FirstBlockOrdinal <= source.Ordinal
                        && source.Ordinal <= finallyBody.LastBlockOrdinal
                    )
                    {
                        foreach (ControlFlowRegion nested in region.NestedRegions)
                        {
                            if (
                                nested.Kind == ControlFlowRegionKind.Finally
                                && !HasNormalFinallyExit(graph, nested)
                            )
                            {
                                return true;
                            }
                        }
                    }
                    continue;
                }
                if (region.Kind != ControlFlowRegionKind.TryAndCatch)
                {
                    continue;
                }
                ControlFlowRegion body = region.NestedRegions[0];
                if (
                    source.Ordinal < body.FirstBlockOrdinal
                    || body.LastBlockOrdinal < source.Ordinal
                )
                {
                    continue;
                }
                for (int index = 1; index < region.NestedRegions.Length; ++index)
                {
                    ControlFlowRegion handler = region.NestedRegions[index];
                    ITypeSymbol handlerType = handler.ExceptionType;
                    if (
                        handler.Kind == ControlFlowRegionKind.Catch
                        && ConsumeHandledExceptions(
                            ref exceptionType,
                            ref alternativeExceptionType,
                            handlerType
                        )
                    )
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool HasNormalFinallyExit(ControlFlowGraph graph, ControlFlowRegion region)
        {
            for (int index = region.FirstBlockOrdinal; index <= region.LastBlockOrdinal; ++index)
            {
                BasicBlock block = graph.Blocks[index];
                if (
                    block.IsReachable
                    && (
                        IsFinallyExit(block.FallThroughSuccessor, region)
                        || IsFinallyExit(block.ConditionalSuccessor, region)
                    )
                )
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsFinallyExit(ControlFlowBranch branch, ControlFlowRegion target)
        {
            if (
                branch == null
                || branch.Semantics != ControlFlowBranchSemantics.StructuredExceptionHandling
            )
            {
                return false;
            }
            for (
                ControlFlowRegion region = branch.Source.EnclosingRegion;
                region != null;
                region = region.EnclosingRegion
            )
            {
                if (region.Kind == ControlFlowRegionKind.Filter)
                {
                    return false;
                }
                if (region.Kind == ControlFlowRegionKind.Finally)
                {
                    return region == target;
                }
            }
            return false;
        }

        private static ITypeSymbol ThrownExceptionType(
            ControlFlowGraph graph,
            ControlFlowBranch branch,
            out ITypeSymbol alternativeExceptionType
        )
        {
            alternativeExceptionType = null;
            if (branch.Semantics == ControlFlowBranchSemantics.Rethrow)
            {
                return RethrownExceptionType(branch.Source);
            }
            IOperation thrown = branch.Source.BranchValue;
            while (
                thrown is IConversionOperation conversion
                && conversion.IsImplicit
                && conversion.OperatorMethod == null
            )
            {
                thrown = conversion.Operand;
            }
            if (
                thrown != null
                && thrown.ConstantValue.HasValue
                && thrown.ConstantValue.Value == null
            )
            {
                return graph.OriginalOperation.SemanticModel?.Compilation.GetTypeByMetadataName(
                    "System.NullReferenceException"
                );
            }
            ITypeSymbol type = thrown?.Type;
            if (thrown is IObjectCreationOperation creation)
            {
                IAssemblySymbol coreAssembly = graph
                    .OriginalOperation.SemanticModel?.Compilation.GetTypeByMetadataName(
                        "System.Exception"
                    )
                    ?.ContainingAssembly;
                return
                    creation.Arguments.IsDefaultOrEmpty
                    && creation.Initializer == null
                    && coreAssembly != null
                    && SymbolEqualityComparer.Default.Equals(type?.ContainingAssembly, coreAssembly)
                    ? type
                    : null;
            }
            if (thrown is IInstanceReferenceOperation)
            {
                return type;
            }
            if (!(thrown is ILocalReferenceOperation) && !(thrown is IParameterReferenceOperation))
            {
                return null;
            }
            if (type == null || type.TypeKind != TypeKind.Class)
            {
                return null;
            }
            alternativeExceptionType =
                graph.OriginalOperation.SemanticModel?.Compilation.GetTypeByMetadataName(
                    "System.NullReferenceException"
                );
            if (alternativeExceptionType == null)
            {
                return null;
            }
            if (SymbolEqualityComparer.Default.Equals(type, alternativeExceptionType))
            {
                alternativeExceptionType = null;
            }
            return type;
        }

        private static bool ConsumeHandledExceptions(
            ref ITypeSymbol exceptionType,
            ref ITypeSymbol alternativeExceptionType,
            ITypeSymbol handlerType
        )
        {
            bool handlesAlternative =
                alternativeExceptionType != null
                && HandlerCatchesType(alternativeExceptionType, handlerType);
            if (HandlerCatchesType(exceptionType, handlerType))
            {
                if (alternativeExceptionType == null || handlesAlternative)
                {
                    return true;
                }
                exceptionType = alternativeExceptionType;
                alternativeExceptionType = null;
            }
            else if (handlesAlternative)
            {
                alternativeExceptionType = null;
            }
            return false;
        }

        private static bool HandlerCatchesType(ITypeSymbol type, ITypeSymbol handlerType)
        {
            return handlerType == null
                || handlerType.SpecialType == SpecialType.System_Object
                || IsSameOrDerived(type, handlerType)
                || (
                    string.Equals(handlerType.Name, nameof(Exception), StringComparison.Ordinal)
                    && string.Equals(
                        handlerType.ContainingNamespace.ToDisplayString(),
                        "System",
                        StringComparison.Ordinal
                    )
                );
        }

        private static ITypeSymbol RethrownExceptionType(BasicBlock source)
        {
            for (
                ControlFlowRegion region = source.EnclosingRegion;
                region != null;
                region = region.EnclosingRegion
            )
            {
                if (region.Kind == ControlFlowRegionKind.Catch)
                {
                    return region.ExceptionType;
                }
            }
            return null;
        }

        private static bool IsSameOrDerived(ITypeSymbol type, ITypeSymbol ancestor)
        {
            if (ancestor == null)
            {
                return false;
            }
            for (
                ITypeSymbol current = type;
                current != null;
                current = (current as INamedTypeSymbol)?.BaseType
            )
            {
                if (SymbolEqualityComparer.Default.Equals(current, ancestor))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool AreProvablyUnrelated(ITypeSymbol thrown, ITypeSymbol caught)
        {
            return thrown != null
                && caught != null
                && thrown.TypeKind == TypeKind.Class
                && caught.TypeKind == TypeKind.Class
                && !IsSameOrDerived(thrown, caught)
                && !IsSameOrDerived(caught, thrown);
        }

        private static bool QueueDestination(BasicBlock destination, Queue<BasicBlock> pending)
        {
            if (destination == null || !destination.IsReachable)
            {
                return false;
            }
            foreach (IOperation operation in destination.Operations)
            {
                if (
                    operation is ISimpleAssignmentOperation assignment
                    && assignment.IsImplicit
                    && assignment.Value.Kind == OperationKind.CaughtException
                )
                {
                    continue;
                }
                return true;
            }
            if (destination.BranchValue != null)
            {
                return true;
            }
            pending.Enqueue(destination);
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
