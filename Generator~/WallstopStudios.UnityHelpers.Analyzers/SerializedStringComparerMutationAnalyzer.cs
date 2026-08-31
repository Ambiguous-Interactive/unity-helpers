// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports a write to <c>SerializedStringComparer.compareMode</c> after the same comparer has
    /// been passed to a collection constructor in the current operation block.
    /// </summary>
    /// <remarks>
    /// This deliberately follows one local, parameter, or directly referenced field within one
    /// lexical block. Requiring the collection use and write in that same block avoids guessing
    /// across branches and loops; aliases and cross-method ownership need broader dataflow. Events
    /// are resolved in evaluation order so Roslyn's callback order cannot change the answer.
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SerializedStringComparerMutationAnalyzer : DiagnosticAnalyzer
    {
        private const string ComparerMetadataName =
            "WallstopStudios.UnityHelpers.Utils.SerializedStringComparer";
        private const string EqualityComparerMetadataName = "IEqualityComparer`1";
        private const string CollectionsNamespace = "System.Collections.Generic";
        private const string ConcurrentCollectionsNamespace = "System.Collections.Concurrent";
        private const string CompareModeFieldName = "compareMode";
        private const string FreezeMethodName = "Freeze";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.ComparerModeChangesAfterCollectionUse);

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterOperationBlockStartAction(OnOperationBlockStart);
        }

        private static void OnOperationBlockStart(OperationBlockStartAnalysisContext context)
        {
            BlockState state = new BlockState();
            context.RegisterOperationAction(state.OnObjectCreation, OperationKind.ObjectCreation);
            context.RegisterOperationAction(state.OnInvocation, OperationKind.Invocation);
            context.RegisterOperationAction(
                state.OnSimpleAssignment,
                OperationKind.SimpleAssignment
            );
            context.RegisterOperationAction(
                state.OnCompoundAssignment,
                OperationKind.CompoundAssignment
            );
            context.RegisterOperationAction(
                state.OnIncrementOrDecrement,
                OperationKind.Increment,
                OperationKind.Decrement
            );
            context.RegisterOperationAction(state.OnArgument, OperationKind.Argument);
            context.RegisterOperationAction(
                state.OnDeconstructionAssignment,
                OperationKind.DeconstructionAssignment
            );
            context.RegisterOperationBlockEndAction(state.Flush);
        }

        private static bool IsSerializedStringComparer(ITypeSymbol type)
        {
            return type != null
                && type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                    == "global::" + ComparerMetadataName;
        }

        private static bool IsStringEqualityComparer(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol named) || !named.IsGenericType)
            {
                return false;
            }

            return named.ConstructedFrom.MetadataName == EqualityComparerMetadataName
                && named.ContainingNamespace.ToDisplayString() == CollectionsNamespace
                && named.TypeArguments.Length == 1
                && named.TypeArguments[0].SpecialType == SpecialType.System_String;
        }

        private static bool IsHashCollection(INamedTypeSymbol type)
        {
            if (type == null)
            {
                return false;
            }

            INamedTypeSymbol definition = type.OriginalDefinition;
            string namespaceName = definition.ContainingNamespace.ToDisplayString();
            return (
                    namespaceName == CollectionsNamespace
                    && (
                        definition.MetadataName == "Dictionary`2"
                        || definition.MetadataName == "HashSet`1"
                    )
                )
                || (
                    namespaceName == ConcurrentCollectionsNamespace
                    && definition.MetadataName == "ConcurrentDictionary`2"
                );
        }

        private static bool TryGetTrackedComparer(IOperation operation, out ISymbol symbol)
        {
            symbol = null;
            IOperation current = operation;
            while (current is IConversionOperation conversion)
            {
                current = conversion.Operand;
            }

            if (current is IConditionalAccessInstanceOperation)
            {
                IOperation ancestor = current.Parent;
                while (ancestor != null && !(ancestor is IConditionalAccessOperation))
                {
                    ancestor = ancestor.Parent;
                }

                if (ancestor is IConditionalAccessOperation conditionalAccess)
                {
                    current = conditionalAccess.Operation;
                }
            }

            if (current is IInvocationOperation invocation && IsFreeze(invocation))
            {
                current = invocation.Instance;
                while (current is IConversionOperation receiverConversion)
                {
                    current = receiverConversion.Operand;
                }
            }

            if (current is ILocalReferenceOperation local)
            {
                symbol = local.Local;
            }
            else if (current is IParameterReferenceOperation parameter)
            {
                symbol = parameter.Parameter;
            }
            else if (current is IFieldReferenceOperation field)
            {
                if (field.Instance != null && !(field.Instance is IInstanceReferenceOperation))
                {
                    return false;
                }

                symbol = field.Field;
            }

            return symbol != null && IsSerializedStringComparer(SymbolType(symbol));
        }

        private static ITypeSymbol SymbolType(ISymbol symbol)
        {
            if (symbol is ILocalSymbol local)
            {
                return local.Type;
            }

            if (symbol is IParameterSymbol parameter)
            {
                return parameter.Type;
            }

            return symbol is IFieldSymbol field ? field.Type : null;
        }

        private static bool TryGetModeReceiver(IOperation operation, out ISymbol receiver)
        {
            receiver = null;
            IOperation current = operation;
            while (current is IConversionOperation conversion)
            {
                current = conversion.Operand;
            }

            if (!(current is IFieldReferenceOperation field))
            {
                return false;
            }

            return field.Field != null
                && field.Field.Name == CompareModeFieldName
                && IsSerializedStringComparer(field.Field.ContainingType)
                && TryGetTrackedComparer(field.Instance, out receiver);
        }

        private static bool IsSameModeReference(IOperation left, IOperation right)
        {
            return TryGetModeReceiver(left, out ISymbol leftReceiver)
                && TryGetModeReceiver(right, out ISymbol rightReceiver)
                && SymbolEqualityComparer.Default.Equals(leftReceiver, rightReceiver);
        }

        private static SyntaxNode LexicalRegion(IOperation operation)
        {
            SyntaxNode current = operation?.Syntax;
            while (current?.Parent != null)
            {
                SyntaxNode parent = current.Parent;
                if (parent is BlockSyntax || parent is SwitchSectionSyntax)
                {
                    return parent;
                }

                if (parent is BinaryExpressionSyntax binary)
                {
                    bool rightIsConditional =
                        binary.IsKind(SyntaxKind.LogicalAndExpression)
                        || binary.IsKind(SyntaxKind.LogicalOrExpression)
                        || binary.IsKind(SyntaxKind.CoalesceExpression);
                    if (rightIsConditional && binary.Right == current)
                    {
                        return current;
                    }
                }

                if (parent is ConditionalExpressionSyntax conditional)
                {
                    if (conditional.WhenTrue == current || conditional.WhenFalse == current)
                    {
                        return current;
                    }
                }

                if (
                    parent is AssignmentExpressionSyntax coalesceAssignment
                    && coalesceAssignment.IsKind(SyntaxKind.CoalesceAssignmentExpression)
                    && coalesceAssignment.Right == current
                )
                {
                    return current;
                }

                if (
                    parent is SwitchExpressionArmSyntax
                    || parent is ConditionalAccessExpressionSyntax conditionalAccess
                        && conditionalAccess.WhenNotNull == current
                    || parent is IfStatementSyntax ifStatement && ifStatement.Statement == current
                    || parent is ElseClauseSyntax
                    || parent is ForStatementSyntax
                    || parent is ForEachStatementSyntax
                    || parent is ForEachVariableStatementSyntax
                    || parent is WhileStatementSyntax
                    || parent is DoStatementSyntax
                    || parent is CatchFilterClauseSyntax
                )
                {
                    return current;
                }

                current = current.Parent;
            }

            return null;
        }

        private static bool IsFreeze(IInvocationOperation invocation)
        {
            return invocation != null
                && invocation.Instance != null
                && invocation.TargetMethod != null
                && invocation.TargetMethod.Name == FreezeMethodName
                && invocation.TargetMethod.Parameters.Length == 0
                && IsSerializedStringComparer(invocation.TargetMethod.ContainingType);
        }

        private enum EventKind
        {
            Rebind = 0,
            CollectionUse = 1,
            Freeze = 2,
            Write = 3,
        }

        private sealed class ComparerEvent
        {
            public ComparerEvent(
                ISymbol symbol,
                SyntaxNode block,
                IOperation operation,
                EventKind kind,
                Location location,
                int order = 0,
                int position = -1
            )
            {
                Symbol = symbol;
                Block = block;
                Position = position < 0 ? operation.Syntax.Span.End : position;
                Start = operation.Syntax.Span.Start;
                Kind = kind;
                Location = location;
                Order = order;
            }

            public ISymbol Symbol { get; }

            public SyntaxNode Block { get; }

            public int Position { get; }

            public int Start { get; }

            public EventKind Kind { get; }

            public Location Location { get; }

            public int Order { get; }
        }

        private sealed class ComparerState
        {
            public bool IsUsed { get; set; }

            public bool IsFrozen { get; set; }
        }

        private sealed class BlockState
        {
            private readonly object gate = new object();
            private readonly List<ComparerEvent> events = new List<ComparerEvent>();

            public void OnObjectCreation(OperationAnalysisContext context)
            {
                IObjectCreationOperation creation = (IObjectCreationOperation)context.Operation;
                if (!IsHashCollection(creation.Constructor?.ContainingType))
                {
                    return;
                }

                foreach (IArgumentOperation argument in creation.Arguments)
                {
                    if (
                        argument.Parameter == null
                        || !IsStringEqualityComparer(argument.Parameter.Type)
                        || !TryGetTrackedComparer(argument.Value, out ISymbol symbol)
                    )
                    {
                        continue;
                    }

                    Add(
                        new ComparerEvent(
                            symbol,
                            LexicalRegion(creation),
                            creation,
                            EventKind.CollectionUse,
                            null,
                            position: ConstructorEnd(creation)
                        )
                    );
                }
            }

            public void OnInvocation(OperationAnalysisContext context)
            {
                IInvocationOperation invocation = (IInvocationOperation)context.Operation;
                if (
                    IsFreeze(invocation)
                    && TryGetTrackedComparer(invocation.Instance, out ISymbol symbol)
                )
                {
                    IOperation regionOperation = ConditionalReceiver(invocation) ?? invocation;
                    Add(
                        new ComparerEvent(
                            symbol,
                            LexicalRegion(regionOperation),
                            invocation,
                            EventKind.Freeze,
                            null
                        )
                    );
                }
            }

            public void OnSimpleAssignment(OperationAnalysisContext context)
            {
                ISimpleAssignmentOperation assignment = (ISimpleAssignmentOperation)
                    context.Operation;
                if (TryGetTrackedComparer(assignment.Target, out ISymbol rebound))
                {
                    if (
                        TryGetTrackedComparer(assignment.Value, out ISymbol assigned)
                        && SymbolEqualityComparer.Default.Equals(rebound, assigned)
                    )
                    {
                        return;
                    }

                    Add(
                        new ComparerEvent(
                            rebound,
                            LexicalRegion(assignment),
                            assignment,
                            EventKind.Rebind,
                            null
                        )
                    );
                    return;
                }

                if (!(assignment.Target is IFieldReferenceOperation field))
                {
                    return;
                }

                if (IsSameModeReference(assignment.Target, assignment.Value))
                {
                    return;
                }

                if (
                    field.Field == null
                    || field.Field.Name != CompareModeFieldName
                    || !IsSerializedStringComparer(field.Field.ContainingType)
                    || !TryGetTrackedComparer(field.Instance, out ISymbol symbol)
                )
                {
                    return;
                }

                AddModeWrite(symbol, assignment, field.Syntax.GetLocation());
            }

            public void OnCompoundAssignment(OperationAnalysisContext context)
            {
                ICompoundAssignmentOperation assignment = (ICompoundAssignmentOperation)
                    context.Operation;
                RecordModeWrite(
                    assignment.Target,
                    assignment,
                    assignment.Target.Syntax.GetLocation()
                );
            }

            public void OnIncrementOrDecrement(OperationAnalysisContext context)
            {
                IIncrementOrDecrementOperation mutation = (IIncrementOrDecrementOperation)
                    context.Operation;
                RecordModeWrite(mutation.Target, mutation, mutation.Target.Syntax.GetLocation());
            }

            public void OnArgument(OperationAnalysisContext context)
            {
                IArgumentOperation argument = (IArgumentOperation)context.Operation;
                RefKind refKind =
                    argument.Parameter == null ? RefKind.None : argument.Parameter.RefKind;
                if (refKind != RefKind.Ref && refKind != RefKind.Out)
                {
                    return;
                }

                if (TryGetTrackedComparer(argument.Value, out ISymbol rebound))
                {
                    AddRebind(rebound, argument.Parent ?? argument, order: 1);
                    return;
                }

                RecordModeWrite(
                    argument.Value,
                    argument.Parent ?? argument,
                    argument.Value.Syntax.GetLocation()
                );
            }

            public void OnDeconstructionAssignment(OperationAnalysisContext context)
            {
                IDeconstructionAssignmentOperation assignment = (IDeconstructionAssignmentOperation)
                    context.Operation;
                int order = 0;
                RecordDeconstructionTarget(
                    assignment.Target,
                    assignment.Value,
                    assignment,
                    ref order
                );
            }

            public void Flush(OperationBlockAnalysisContext context)
            {
                List<ComparerEvent> ordered;
                lock (gate)
                {
                    ordered = new List<ComparerEvent>(events);
                }

                ordered.Sort(
                    (left, right) =>
                    {
                        int position = left.Position.CompareTo(right.Position);
                        if (position != 0)
                        {
                            return position;
                        }

                        int nesting = right.Start.CompareTo(left.Start);
                        if (nesting != 0)
                        {
                            return nesting;
                        }

                        int order = left.Order.CompareTo(right.Order);
                        return order != 0 ? order : left.Kind.CompareTo(right.Kind);
                    }
                );

                Dictionary<SyntaxNode, Dictionary<ISymbol, ComparerState>> statesByBlock =
                    new Dictionary<SyntaxNode, Dictionary<ISymbol, ComparerState>>();
                foreach (ComparerEvent comparerEvent in ordered)
                {
                    if (comparerEvent.Block == null)
                    {
                        continue;
                    }

                    if (
                        !statesByBlock.TryGetValue(
                            comparerEvent.Block,
                            out Dictionary<ISymbol, ComparerState> states
                        )
                    )
                    {
                        states = new Dictionary<ISymbol, ComparerState>(
                            SymbolEqualityComparer.Default
                        );
                        statesByBlock.Add(comparerEvent.Block, states);
                    }

                    if (!states.TryGetValue(comparerEvent.Symbol, out ComparerState state))
                    {
                        state = new ComparerState();
                        states.Add(comparerEvent.Symbol, state);
                    }

                    if (comparerEvent.Kind == EventKind.Rebind)
                    {
                        state.IsUsed = false;
                        state.IsFrozen = false;
                    }
                    else if (comparerEvent.Kind == EventKind.CollectionUse)
                    {
                        state.IsUsed = true;
                    }
                    else if (comparerEvent.Kind == EventKind.Freeze)
                    {
                        state.IsFrozen = true;
                    }
                    else if (state.IsUsed && !state.IsFrozen)
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                UnityHelpersDiagnostics.ComparerModeChangesAfterCollectionUse,
                                comparerEvent.Location,
                                comparerEvent.Symbol.Name
                            )
                        );
                    }
                }
            }

            private void RecordModeWrite(
                IOperation target,
                IOperation mutation,
                Location location,
                int order = 0
            )
            {
                if (!(target is IFieldReferenceOperation field))
                {
                    return;
                }

                if (
                    field.Field == null
                    || field.Field.Name != CompareModeFieldName
                    || !IsSerializedStringComparer(field.Field.ContainingType)
                    || !TryGetTrackedComparer(field.Instance, out ISymbol symbol)
                )
                {
                    return;
                }

                AddModeWrite(symbol, mutation, location, order);
            }

            private void RecordDeconstructionTarget(
                IOperation target,
                IOperation value,
                IOperation assignment,
                ref int order
            )
            {
                if (target is ITupleOperation tuple)
                {
                    IOperation unwrappedValue = value;
                    while (unwrappedValue is IConversionOperation conversion)
                    {
                        unwrappedValue = conversion.Operand;
                    }

                    ITupleOperation valueTuple = unwrappedValue as ITupleOperation;
                    for (int index = 0; index < tuple.Elements.Length; index++)
                    {
                        IOperation elementValue =
                            valueTuple != null && index < valueTuple.Elements.Length
                                ? valueTuple.Elements[index]
                                : null;
                        RecordDeconstructionTarget(
                            tuple.Elements[index],
                            elementValue,
                            assignment,
                            ref order
                        );
                    }
                    return;
                }

                if (TryGetTrackedComparer(target, out ISymbol rebound))
                {
                    if (
                        TryGetTrackedComparer(value, out ISymbol assigned)
                        && SymbolEqualityComparer.Default.Equals(rebound, assigned)
                    )
                    {
                        order++;
                        return;
                    }

                    AddRebind(rebound, assignment, order);
                    order++;
                    return;
                }

                if (IsSameModeReference(target, value))
                {
                    order++;
                    return;
                }

                RecordModeWrite(target, assignment, target.Syntax.GetLocation(), order);
                order++;
            }

            private void AddRebind(ISymbol symbol, IOperation mutation, int order = 0)
            {
                Add(
                    new ComparerEvent(
                        symbol,
                        LexicalRegion(mutation),
                        mutation,
                        EventKind.Rebind,
                        null,
                        order
                    )
                );
            }

            private void AddModeWrite(
                ISymbol symbol,
                IOperation mutation,
                Location location,
                int order = 0
            )
            {
                Add(
                    new ComparerEvent(
                        symbol,
                        LexicalRegion(mutation),
                        mutation,
                        EventKind.Write,
                        location,
                        order
                    )
                );
            }

            private void Add(ComparerEvent comparerEvent)
            {
                lock (gate)
                {
                    events.Add(comparerEvent);
                }
            }

            private static int ConstructorEnd(IObjectCreationOperation creation)
            {
                if (
                    creation.Syntax is ObjectCreationExpressionSyntax objectCreation
                    && objectCreation.ArgumentList != null
                )
                {
                    return objectCreation.ArgumentList.CloseParenToken.Span.End;
                }

                if (
                    creation.Syntax is ImplicitObjectCreationExpressionSyntax implicitCreation
                    && implicitCreation.ArgumentList != null
                )
                {
                    return implicitCreation.ArgumentList.CloseParenToken.Span.End;
                }

                return creation.Syntax.Span.End;
            }

            private static IOperation ConditionalReceiver(IInvocationOperation invocation)
            {
                if (!(invocation.Instance is IConditionalAccessInstanceOperation))
                {
                    return null;
                }

                IOperation ancestor = invocation.Parent;
                while (ancestor != null && !(ancestor is IConditionalAccessOperation))
                {
                    ancestor = ancestor.Parent;
                }

                return ancestor is IConditionalAccessOperation conditionalAccess
                    ? conditionalAccess.Operation
                    : null;
            }
        }
    }
}
