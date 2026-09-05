// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports a counting <c>for</c> loop that only ever uses its index to walk a sequence
    /// <c>foreach</c> would walk without allocating.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The discriminator is the sequence's type, which is why this cannot be a source linter:
    /// <c>foreach</c> over <c>List&lt;T&gt;</c> uses a struct enumerator and allocates nothing,
    /// while the identical loop over <c>IReadOnlyList&lt;T&gt;</c> boxes one. The two are the same
    /// tokens.
    /// </para>
    /// <para>
    /// Every false positive found in review was the same mistake: asking <b>what</b> an operation
    /// touched and not <b>how</b>. An indexer write read as a read, and a store through a struct
    /// element's member read as an access. Both would have advised a rewrite that drops the
    /// mutation, so a use of the index is assumed to require the counting loop until it is shown to
    /// be a read.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class CountingLoopAnalyzer : DiagnosticAnalyzer
    {
        private const string ListMetadataName = "System.Collections.Generic.List`1";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.CountingLoopOverAllocationFreeSequence);

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            INamedTypeSymbol list = context.Compilation.GetTypeByMetadataName(ListMetadataName);
            context.RegisterOperationAction(
                (OperationAnalysisContext operationContext) => AnalyzeLoop(operationContext, list),
                OperationKind.Loop
            );
        }

        private static void AnalyzeLoop(OperationAnalysisContext context, INamedTypeSymbol list)
        {
            if (
                !(context.Operation is IForLoopOperation loop)
                || !TryGetSingleIndex(loop, out ILocalSymbol index)
                || !IsSimpleForwardWalk(
                    loop,
                    index,
                    out ISymbol sequence,
                    out ISymbol receiver,
                    out ITypeSymbol sequenceType
                )
                || !WalksWithoutAllocating(sequenceType, list)
            )
            {
                return;
            }

            if (!OnlyIndexesThatSequence(loop.Body, index, sequence, receiver))
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.CountingLoopOverAllocationFreeSequence,
                    loop.Syntax.GetLocation(),
                    sequence.Name,
                    sequenceType.ToDisplayString(),
                    index.Name
                )
            );
        }

        /// <summary>The loop's single declared counter, when it declares exactly one.</summary>
        /// <param name="loop">The loop to inspect.</param>
        /// <param name="index">Receives the counter.</param>
        /// <returns><c>false</c> when the loop declares zero counters or more than one.</returns>
        private static bool TryGetSingleIndex(IForLoopOperation loop, out ILocalSymbol index)
        {
            List<ILocalSymbol> declared = new List<ILocalSymbol>();
            foreach (IOperation before in loop.Before)
            {
                if (!(before is IVariableDeclarationGroupOperation group))
                {
                    continue;
                }

                foreach (IVariableDeclarationOperation declaration in group.Declarations)
                {
                    foreach (IVariableDeclaratorOperation declarator in declaration.Declarators)
                    {
                        if (
                            declarator.Symbol != null
                            && declarator.Symbol.Type.SpecialType == SpecialType.System_Int32
                            && IsZero(declarator.Initializer?.Value)
                        )
                        {
                            declared.Add(declarator.Symbol);
                        }
                    }
                }
            }

            if (declared.Count != 1)
            {
                index = null;
                return false;
            }

            index = declared[0];
            return true;
        }

        /// <summary>
        /// Whether the loop is the ordinary forward walk: <c>index &lt; sequence.Length</c> or
        /// <c>.Count</c>, stepping by one.
        /// </summary>
        /// <param name="loop">The loop to inspect.</param>
        /// <param name="index">The loop counter.</param>
        /// <param name="sequence">Receives the sequence being walked.</param>
        /// <param name="sequenceType">Receives the sequence's type.</param>
        /// <returns><c>false</c> for any other shape, which is left alone.</returns>
        private static bool IsSimpleForwardWalk(
            IForLoopOperation loop,
            ILocalSymbol index,
            out ISymbol sequence,
            out ISymbol receiver,
            out ITypeSymbol sequenceType
        )
        {
            sequence = null;
            receiver = null;
            sequenceType = null;

            if (
                !(loop.Condition is IBinaryOperation condition)
                || condition.OperatorKind != BinaryOperatorKind.LessThan
                || !IsLocal(condition.LeftOperand, index)
                || !TryGetCountedSequence(
                    condition.RightOperand,
                    out sequence,
                    out receiver,
                    out sequenceType
                )
            )
            {
                return false;
            }

            if (loop.AtLoopBottom.Length != 1)
            {
                return false;
            }

            IOperation step = loop.AtLoopBottom[0];
            if (step is IExpressionStatementOperation statement)
            {
                step = statement.Operation;
            }

            return step is IIncrementOrDecrementOperation increment
                && increment.Kind == OperationKind.Increment
                && IsLocal(increment.Target, index);
        }

        /// <summary>The sequence behind a <c>.Length</c> or <c>.Count</c> read.</summary>
        /// <param name="bound">The loop's upper bound expression.</param>
        /// <param name="sequence">Receives the sequence symbol.</param>
        /// <param name="sequenceType">Receives the sequence's type.</param>
        /// <returns><c>false</c> when the bound is anything else.</returns>
        private static bool TryGetCountedSequence(
            IOperation bound,
            out ISymbol sequence,
            out ISymbol receiver,
            out ITypeSymbol sequenceType
        )
        {
            sequence = null;
            receiver = null;
            sequenceType = null;
            if (
                !(bound is IPropertyReferenceOperation property)
                || (property.Property.Name != "Length" && property.Property.Name != "Count")
            )
            {
                return false;
            }

            IOperation instance = property.Instance;
            if (instance is IFieldReferenceOperation field)
            {
                /*
                    Distinct receivers can expose different arrays through the same field symbol.
                    Keep their parallel index and refuse receiver shapes that cannot be compared.
                */
                if (!TryGetReceiver(field.Instance, out receiver))
                {
                    return false;
                }

                sequence = field.Field;
                sequenceType = field.Type;
                return true;
            }

            if (instance is ILocalReferenceOperation local)
            {
                sequence = local.Local;
                sequenceType = local.Type;
                return true;
            }

            if (instance is IParameterReferenceOperation parameter)
            {
                sequence = parameter.Parameter;
                sequenceType = parameter.Type;
                return true;
            }

            return false;
        }

        /// <summary>Whether <c>foreach</c> over this type allocates no enumerator.</summary>
        /// <param name="sequenceType">The sequence's type.</param>
        /// <param name="list">The resolved <c>List&lt;T&gt;</c> symbol.</param>
        /// <returns><c>true</c> for an array or a concrete <c>List&lt;T&gt;</c> only.</returns>
        private static bool WalksWithoutAllocating(ITypeSymbol sequenceType, INamedTypeSymbol list)
        {
            if (sequenceType is IArrayTypeSymbol)
            {
                return true;
            }

            return list != null
                && sequenceType is INamedTypeSymbol named
                && named.IsGenericType
                && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, list);
        }

        /// <summary>
        /// Whether every read of the counter in the body is an index into that same sequence.
        /// </summary>
        /// <param name="body">The loop body.</param>
        /// <param name="index">The loop counter.</param>
        /// <param name="sequence">The sequence being walked.</param>
        /// <returns><c>false</c> as soon as the counter is used for anything else.</returns>
        /// <remarks>
        /// A body that reports the index, offsets it, or indexes a second collection with it needs
        /// the number, so rewriting it as <c>foreach</c> would lose something. An indexer wraps its
        /// argument in an <see cref="IArgumentOperation"/> where an array does not, so the parent
        /// chain is unwrapped before it is asked.
        /// </remarks>
        private static bool OnlyIndexesThatSequence(
            IOperation body,
            ILocalSymbol index,
            ISymbol sequence,
            ISymbol receiver
        )
        {
            foreach (IOperation operation in Descendants(body))
            {
                if (
                    (
                        NamesSequence(operation, sequence, receiver)
                        && IsPassedToUnknownCode(operation)
                    )
                    || (
                        receiver != null
                        && NamesSymbol(operation, receiver)
                        && IsWrittenThrough(operation)
                    )
                )
                {
                    return false;
                }

                if (
                    (
                        (
                            IsIndexInto(operation, sequence, receiver)
                            || NamesSequence(operation, sequence, receiver)
                        ) && IsWrittenThrough(operation)
                    )
                    || (
                        operation is IInvocationOperation invocation
                        && NamesSequence(invocation.Instance, sequence, receiver)
                        && MutatesList(invocation.TargetMethod)
                    )
                )
                {
                    return false;
                }

                if (!IsLocal(operation, index))
                {
                    continue;
                }

                IOperation parent = operation.Parent;
                while (parent is IArgumentOperation || parent is IConversionOperation)
                {
                    parent = parent.Parent;
                }

                if (!IsIndexInto(parent, sequence, receiver) || IsWrittenThrough(parent))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsPassedToUnknownCode(IOperation operation)
        {
            IOperation current = operation;
            while (current.Parent is IConversionOperation conversion)
            {
                current = conversion;
            }

            return current.Parent is IArgumentOperation;
        }

        private static bool NamesSymbol(IOperation operation, ISymbol symbol)
        {
            switch (operation)
            {
                case IFieldReferenceOperation field:
                    return SymbolEqualityComparer.Default.Equals(field.Field, symbol);
                case ILocalReferenceOperation local:
                    return SymbolEqualityComparer.Default.Equals(local.Local, symbol);
                case IParameterReferenceOperation parameter:
                    return SymbolEqualityComparer.Default.Equals(parameter.Parameter, symbol);
                default:
                    return false;
            }
        }

        private static bool MutatesList(IMethodSymbol method)
        {
            switch (method.Name)
            {
                case nameof(List<int>.Add):
                case nameof(List<int>.AddRange):
                case nameof(List<int>.Clear):
                case nameof(List<int>.ForEach):
                case nameof(List<int>.Insert):
                case nameof(List<int>.InsertRange):
                case nameof(List<int>.Remove):
                case nameof(List<int>.RemoveAt):
                case nameof(List<int>.RemoveAll):
                case nameof(List<int>.RemoveRange):
                case nameof(List<int>.Reverse):
                case nameof(List<int>.Sort):
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Whether the element reference is written to rather than read.</summary>
        /// <param name="element">The array element or indexer reference.</param>
        /// <returns><c>true</c> when the loop updates that slot.</returns>
        /// <remarks>
        /// <c>foreach</c> cannot assign back into the sequence, for an array or a
        /// <c>List&lt;T&gt;</c> alike, so advising it for <c>rows[index] = value</c> or
        /// <c>rows[index]++</c> would silently drop the mutation. A write is the one use of the
        /// index that the counting loop is required for. A store or a mutating call through a
        /// struct element's member counts, because that member is part of the slot.
        /// </remarks>
        private static bool IsWrittenThrough(IOperation element)
        {
            /*
                A struct member store writes the array slot; a class member store writes through
                the reference and remains valid in foreach.
            */
            IOperation current = element;
            while (
                current.Parent is IMemberReferenceOperation member
                && member.Instance == current
                && IsValueType(current.Type)
            )
            {
                current = member;
            }

            /*
                Calls on struct elements may mutate their slots. Conservatively retain indexed
                traversal rather than redirecting an unknown mutation to a foreach copy.
            */
            if (
                IsValueType(element.Type)
                && current.Parent is IInvocationOperation call
                && call.Instance == current
            )
            {
                return true;
            }

            while (current.Parent is ITupleOperation tuple)
            {
                current = tuple;
            }

            IOperation parent = current.Parent;
            if (
                parent is IVariableInitializerOperation initializer
                && initializer.Parent is IVariableDeclaratorOperation declaration
                && declaration.Symbol.RefKind != RefKind.None
            )
            {
                return true;
            }

            switch (parent)
            {
                case IAssignmentOperation assignment:
                    return assignment.Target == current;
                case IIncrementOrDecrementOperation increment:
                    return increment.Target == current;
                case IArgumentOperation argument:
                    return argument.Parameter != null && argument.Parameter.RefKind != RefKind.None;
                default:
                    return false;
            }
        }

        /// <summary>Whether the element is a struct, so a member store lands in the slot.</summary>
        /// <param name="type">The element's type.</param>
        /// <returns><c>true</c> for a value type.</returns>
        /// <remarks>
        /// The distinction is the whole point: <c>points[i].x = 1f</c> on a struct array writes
        /// into the array, and <c>foreach</c> hands out a copy. The same shape on a class array
        /// writes through a reference, which <c>foreach</c> does perfectly well.
        /// </remarks>
        private static bool IsValueType(ITypeSymbol type)
        {
            return type != null && type.IsValueType;
        }

        private static bool IsIndexInto(IOperation parent, ISymbol sequence, ISymbol receiver)
        {
            if (parent is IArrayElementReferenceOperation array)
            {
                return NamesSequence(array.ArrayReference, sequence, receiver);
            }

            return parent is IPropertyReferenceOperation property
                && property.Property.IsIndexer
                && NamesSequence(property.Instance, sequence, receiver);
        }

        /// <summary>
        /// The symbol a member is read through, or <c>null</c> for <c>this</c>, a static, a local
        /// or a parameter.
        /// </summary>
        /// <param name="instance">The receiver operation, which may be null.</param>
        /// <param name="receiver">Receives the receiver's symbol, or null.</param>
        /// <returns><c>false</c> for a shape this cannot name, so the caller can refuse.</returns>
        private static bool TryGetReceiver(IOperation instance, out ISymbol receiver)
        {
            receiver = null;
            switch (instance)
            {
                case null:
                case IInstanceReferenceOperation _:
                    return true;
                case IFieldReferenceOperation field:
                    if (field.Instance != null && !(field.Instance is IInstanceReferenceOperation))
                    {
                        return false;
                    }

                    receiver = field.Field;
                    return true;
                case ILocalReferenceOperation local:
                    receiver = local.Local;
                    return true;
                case IParameterReferenceOperation parameter:
                    receiver = parameter.Parameter;
                    return true;
                default:
                    return false;
            }
        }

        private static bool NamesSequence(IOperation instance, ISymbol sequence, ISymbol receiver)
        {
            switch (instance)
            {
                case IFieldReferenceOperation field:
                    return SymbolEqualityComparer.Default.Equals(field.Field, sequence)
                        && TryGetReceiver(field.Instance, out ISymbol fieldReceiver)
                        && SymbolEqualityComparer.Default.Equals(fieldReceiver, receiver);
                case ILocalReferenceOperation local:
                    return SymbolEqualityComparer.Default.Equals(local.Local, sequence);
                case IParameterReferenceOperation parameter:
                    return SymbolEqualityComparer.Default.Equals(parameter.Parameter, sequence);
                default:
                    return false;
            }
        }

        private static bool IsLocal(IOperation operation, ILocalSymbol index)
        {
            return operation is ILocalReferenceOperation local
                && SymbolEqualityComparer.Default.Equals(local.Local, index);
        }

        private static bool IsZero(IOperation operation)
        {
            return operation != null
                && operation.ConstantValue.HasValue
                && operation.ConstantValue.Value is int value
                && value == 0;
        }

        private static IEnumerable<IOperation> Descendants(IOperation root)
        {
            if (root == null)
            {
                yield break;
            }

            Stack<IOperation> pending = new Stack<IOperation>();
            pending.Push(root);
            while (0 < pending.Count)
            {
                IOperation current = pending.Pop();
                yield return current;
                foreach (IOperation child in current.Children)
                {
                    if (child != null)
                    {
                        pending.Push(child);
                    }
                }
            }
        }
    }
}
