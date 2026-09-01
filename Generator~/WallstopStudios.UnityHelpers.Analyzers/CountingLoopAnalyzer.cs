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
    /// The discriminator is the sequence's type, which is why this cannot be a source linter:
    /// <c>foreach</c> over <c>List&lt;T&gt;</c> uses a struct enumerator and allocates nothing,
    /// while the identical loop over <c>IReadOnlyList&lt;T&gt;</c> boxes one. The two are the same
    /// tokens.
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
                    out ITypeSymbol sequenceType
                )
                || !WalksWithoutAllocating(sequenceType, list)
            )
            {
                return;
            }

            if (!OnlyIndexesThatSequence(loop.Body, index, sequence))
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
            out ITypeSymbol sequenceType
        )
        {
            sequence = null;
            sequenceType = null;

            if (
                !(loop.Condition is IBinaryOperation condition)
                || condition.OperatorKind != BinaryOperatorKind.LessThan
                || !IsLocal(condition.LeftOperand, index)
                || !TryGetCountedSequence(condition.RightOperand, out sequence, out sequenceType)
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
            out ITypeSymbol sequenceType
        )
        {
            sequence = null;
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
            ISymbol sequence
        )
        {
            foreach (IOperation operation in Descendants(body))
            {
                if (!IsLocal(operation, index))
                {
                    continue;
                }

                IOperation parent = operation.Parent;
                while (parent is IArgumentOperation || parent is IConversionOperation)
                {
                    parent = parent.Parent;
                }

                if (!IsIndexInto(parent, sequence))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsIndexInto(IOperation parent, ISymbol sequence)
        {
            if (parent is IArrayElementReferenceOperation array)
            {
                return NamesSequence(array.ArrayReference, sequence);
            }

            return parent is IPropertyReferenceOperation property
                && property.Property.IsIndexer
                && NamesSequence(property.Instance, sequence);
        }

        private static bool NamesSequence(IOperation instance, ISymbol sequence)
        {
            switch (instance)
            {
                case IFieldReferenceOperation field:
                    return SymbolEqualityComparer.Default.Equals(field.Field, sequence);
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
