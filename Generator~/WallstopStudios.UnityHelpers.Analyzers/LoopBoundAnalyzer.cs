// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>Reports counting loops that repeatedly read a stable sequence bound.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class LoopBoundAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.RepeatedStableLoopBound);

        private static void AnalyzeLoop(OperationAnalysisContext context)
        {
            if (!(context.Operation is IForLoopOperation loop) || loop.Condition == null)
            {
                return;
            }

            foreach (IOperation operation in Descendants(loop.Condition))
            {
                if (
                    !TryGetBound(operation, out IOperation receiver, out bool immutableSize)
                    || IsGuarded(operation, loop.Condition)
                    || !TryGetReceiverSymbols(receiver, out List<ISymbol> symbols)
                    || !RemainsStable(loop, symbols, immutableSize, operation)
                )
                {
                    continue;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        UnityHelpersDiagnostics.RepeatedStableLoopBound,
                        operation.Syntax.GetLocation(),
                        operation.Syntax.ToString()
                    )
                );
            }
        }

        private static bool TryGetBound(
            IOperation operation,
            out IOperation receiver,
            out bool immutableSize
        )
        {
            if (
                operation is IPropertyReferenceOperation property
                && IsSequenceBound(property, out bool propertyImmutableSize)
            )
            {
                receiver = property.Instance;
                immutableSize = propertyImmutableSize;
                return true;
            }
            if (
                operation is IInvocationOperation invocation
                && invocation.Instance != null
                && (
                    invocation.Instance.Type is IArrayTypeSymbol
                    || string.Equals(
                        invocation.Instance.Type.ToDisplayString(),
                        "System.Array",
                        StringComparison.Ordinal
                    )
                )
                && (
                    string.Equals(
                        invocation.TargetMethod.Name,
                        nameof(Array.GetLength),
                        StringComparison.Ordinal
                    )
                    || string.Equals(
                        invocation.TargetMethod.Name,
                        nameof(Array.GetLongLength),
                        StringComparison.Ordinal
                    )
                )
                && invocation.Arguments.Length == 1
                && invocation.Arguments[0].Value.ConstantValue.HasValue
            )
            {
                receiver = invocation.Instance;
                immutableSize = true;
                return true;
            }
            receiver = null;
            immutableSize = false;
            return false;
        }

        private static bool IsGuarded(IOperation bound, IOperation condition)
        {
            IOperation current = bound;
            while (current != condition && current.Parent != null)
            {
                IOperation parent = current.Parent;
                if (
                    parent is IConditionalOperation
                    || parent is ICoalesceOperation
                    || parent is IBinaryOperation binary
                        && (
                            binary.OperatorKind == BinaryOperatorKind.ConditionalAnd
                            || binary.OperatorKind == BinaryOperatorKind.ConditionalOr
                        )
                        && binary.RightOperand == current
                )
                {
                    return true;
                }
                current = parent;
            }
            return false;
        }

        private static bool IsSequenceBound(
            IPropertyReferenceOperation bound,
            out bool immutableSize
        )
        {
            immutableSize = false;
            if (
                bound.Instance == null
                || (
                    bound.Type.SpecialType != SpecialType.System_Int32
                    && bound.Type.SpecialType != SpecialType.System_Int64
                )
            )
            {
                return false;
            }

            string name = bound.Property.Name;
            ITypeSymbol type = bound.Instance.Type;
            if (
                type is IArrayTypeSymbol
                || type.SpecialType == SpecialType.System_String
                || string.Equals(type.ToDisplayString(), "System.Array", StringComparison.Ordinal)
            )
            {
                immutableSize = true;
                return string.Equals(name, "Length", StringComparison.Ordinal)
                    || string.Equals(name, "LongLength", StringComparison.Ordinal);
            }

            string metadataName = type.OriginalDefinition.ToDisplayString();
            if (metadataName.StartsWith("System.Collections.Concurrent.", StringComparison.Ordinal))
            {
                return false;
            }
            if (
                metadataName.StartsWith("System.Span<", StringComparison.Ordinal)
                || metadataName.StartsWith("System.ReadOnlySpan<", StringComparison.Ordinal)
                || metadataName.StartsWith("System.Memory<", StringComparison.Ordinal)
                || metadataName.StartsWith("System.ReadOnlyMemory<", StringComparison.Ordinal)
                || metadataName.StartsWith("System.ArraySegment<", StringComparison.Ordinal)
            )
            {
                immutableSize = true;
                return string.Equals(name, "Length", StringComparison.Ordinal)
                    || string.Equals(name, "Count", StringComparison.Ordinal);
            }

            if (string.Equals(name, "childCount", StringComparison.Ordinal))
            {
                return string.Equals(
                    metadataName,
                    "UnityEngine.Transform",
                    StringComparison.Ordinal
                );
            }
            if (string.Equals(name, "arraySize", StringComparison.Ordinal))
            {
                return string.Equals(
                    metadataName,
                    "UnityEditor.SerializedProperty",
                    StringComparison.Ordinal
                );
            }
            if (!string.Equals(name, "Count", StringComparison.Ordinal))
            {
                return false;
            }

            if (
                bound.Property.IsVirtual
                || bound.Property.IsAbstract
                || bound.Property.ContainingType.TypeKind == TypeKind.Interface
            )
            {
                return false;
            }
            if (!IsSystemNamespace(bound.Property.ContainingNamespace))
            {
                return false;
            }
            if (IsCollection(type))
            {
                return true;
            }
            foreach (INamedTypeSymbol implemented in type.AllInterfaces)
            {
                if (IsCollection(implemented))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsCollection(ITypeSymbol type)
        {
            string name = type.OriginalDefinition.ToDisplayString();
            return string.Equals(name, "System.Collections.ICollection", StringComparison.Ordinal)
                || name.StartsWith(
                    "System.Collections.Generic.ICollection<",
                    StringComparison.Ordinal
                )
                || name.StartsWith(
                    "System.Collections.Generic.IReadOnlyCollection<",
                    StringComparison.Ordinal
                );
        }

        private static bool TryGetReceiverSymbols(IOperation receiver, out List<ISymbol> symbols)
        {
            List<ISymbol> result = new List<ISymbol>();
            while (receiver != null)
            {
                switch (receiver)
                {
                    case ILocalReferenceOperation local:
                        result.Add(local.Local);
                        symbols = result;
                        return true;
                    case IParameterReferenceOperation parameter:
                        result.Add(parameter.Parameter);
                        symbols = result;
                        return true;
                    case IFieldReferenceOperation field:
                        result.Add(field.Field);
                        receiver = field.Instance;
                        break;
                    case IConversionOperation conversion:
                        receiver = conversion.Operand;
                        break;
                    case IInstanceReferenceOperation _:
                        symbols = result;
                        return result.Count != 0;
                    default:
                        symbols = null;
                        return false;
                }
            }
            symbols = result;
            return result.Count != 0;
        }

        private static bool RemainsStable(
            IForLoopOperation loop,
            List<ISymbol> symbols,
            bool immutableSize,
            IOperation bound
        )
        {
            IOperation scope = loop;
            while (
                scope.Parent != null
                && !(scope is IMethodBodyOperation)
                && !(scope is IAnonymousFunctionOperation)
                && !(scope is ILocalFunctionOperation)
            )
            {
                scope = scope.Parent;
            }
            foreach (IOperation operation in Descendants(scope))
            {
                if (operation is IAssignmentOperation capturedWrite)
                {
                    ISymbol target = GetSymbol(capturedWrite.Target);
                    if (target != null && ContainsSymbol(symbols, target))
                    {
                        IOperation owner = capturedWrite.Parent;
                        while (owner != null && owner != scope)
                        {
                            if (
                                owner is IAnonymousFunctionOperation
                                || owner is ILocalFunctionOperation
                            )
                            {
                                return false;
                            }
                            owner = owner.Parent;
                        }
                    }
                }
                if (!immutableSize && operation is IAssignmentOperation aliasAssignment)
                {
                    IOperation value = aliasAssignment.Value;
                    while (value is IConversionOperation conversion)
                    {
                        value = conversion.Operand;
                    }
                    ISymbol source = GetSymbol(value);
                    if (source != null && ContainsSymbol(symbols, source))
                    {
                        return false;
                    }
                }
                if (operation is IVariableInitializerOperation initializer)
                {
                    IOperation value = initializer.Value;
                    while (value is IConversionOperation conversion)
                    {
                        value = conversion.Operand;
                    }
                    ISymbol source = GetSymbol(value);
                    if (
                        source != null
                        && ContainsSymbol(symbols, source)
                        && (
                            !immutableSize
                            || initializer.Parent is IVariableDeclaratorOperation declaration
                                && declaration.Symbol.RefKind != RefKind.None
                        )
                    )
                    {
                        return false;
                    }
                }
            }
            foreach (IOperation before in loop.Before)
            {
                foreach (IOperation operation in Descendants(before))
                {
                    if (!SafeOperation(operation, symbols, immutableSize, bound))
                    {
                        return false;
                    }
                }
            }
            foreach (IOperation operation in Descendants(loop.Body))
            {
                if (!SafeOperation(operation, symbols, immutableSize, bound))
                {
                    return false;
                }
            }
            foreach (IOperation step in loop.AtLoopBottom)
            {
                foreach (IOperation operation in Descendants(step))
                {
                    if (!SafeOperation(operation, symbols, immutableSize, bound))
                    {
                        return false;
                    }
                }
            }
            foreach (IOperation operation in Descendants(loop.Condition))
            {
                if (!SafeOperation(operation, symbols, immutableSize, bound))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool SafeOperation(
            IOperation operation,
            List<ISymbol> symbols,
            bool immutableSize,
            IOperation bound
        )
        {
            bool callbackSensitive = !immutableSize || ContainsFieldReceiver(symbols);
            if (callbackSensitive && !HasKnownPureShape(operation, bound))
            {
                return false;
            }

            if (
                callbackSensitive
                && (
                    operation is IBinaryOperation binary && binary.OperatorMethod != null
                    || operation is IUnaryOperation unary && unary.OperatorMethod != null
                    || operation is IConversionOperation conversion
                        && conversion.OperatorMethod != null
                )
            )
            {
                return false;
            }
            if (
                callbackSensitive
                && operation is IPropertyReferenceOperation mutableGetter
                && operation != bound
                && !IsKnownPureProperty(mutableGetter.Property)
            )
            {
                return false;
            }
            if (
                operation is IPropertyReferenceOperation getter
                && !IsSystemNamespace(getter.Property.ContainingNamespace)
                && !IsSequenceBound(getter, out bool _)
            )
            {
                return false;
            }
            if (
                operation is IInvocationOperation callback
                && (
                    callback.TargetMethod.MethodKind == MethodKind.DelegateInvoke
                    || callback.TargetMethod.MethodKind == MethodKind.LocalFunction
                )
            )
            {
                return false;
            }
            if (
                !immutableSize
                && (
                    operation.Kind == OperationKind.YieldReturn
                    || operation.Kind == OperationKind.Await
                )
            )
            {
                return false;
            }
            if (!immutableSize && operation is IInvocationOperation)
            {
                return false;
            }
            if (
                operation is IInvocationOperation
                || operation.Kind == OperationKind.Await
                || operation.Kind == OperationKind.YieldReturn
            )
            {
                foreach (ISymbol receiverSymbol in symbols)
                {
                    if (receiverSymbol is IFieldSymbol)
                    {
                        return false;
                    }
                }
            }
            ISymbol symbol = GetSymbol(operation);
            if (symbol == null || !ContainsSymbol(symbols, symbol))
            {
                return true;
            }
            IOperation current = operation;
            while (current.Parent is IConversionOperation || current.Parent is ITupleOperation)
            {
                current = current.Parent;
            }
            switch (current.Parent)
            {
                case IAssignmentOperation assignment:
                    return assignment.Target != current;
                case IIncrementOrDecrementOperation increment:
                    return increment.Target != current;
                case IArgumentOperation argument:
                    return immutableSize
                        && (
                            argument.Parameter == null || argument.Parameter.RefKind == RefKind.None
                        );
                case IInvocationOperation invocation:
                    return immutableSize || invocation.Instance != current;
                case IVariableInitializerOperation _:
                    return immutableSize;
                case IPropertyReferenceOperation property:
                    return immutableSize
                        || !IsPropertyWrite(property)
                        || property.Property.IsIndexer && IsKnownPureProperty(property.Property);
                default:
                    return true;
            }
        }

        private static bool HasKnownPureShape(IOperation operation, IOperation bound)
        {
            switch (operation)
            {
                case IBlockOperation _:
                case IExpressionStatementOperation _:
                case IVariableDeclarationGroupOperation _:
                case IVariableDeclarationOperation _:
                case IVariableDeclaratorOperation _:
                case IVariableInitializerOperation _:
                case ILocalReferenceOperation _:
                case IParameterReferenceOperation _:
                case IFieldReferenceOperation _:
                case IInstanceReferenceOperation _:
                case IArrayElementReferenceOperation _:
                case IArrayCreationOperation _:
                case IArrayInitializerOperation _:
                case ILiteralOperation _:
                case IDefaultValueOperation _:
                case IArgumentOperation _:
                case IConditionalOperation _:
                case IConditionalAccessOperation _:
                case IConditionalAccessInstanceOperation _:
                case ICoalesceOperation _:
                case IReturnOperation _:
                case IBranchOperation _:
                case ILabeledOperation _:
                case IEmptyOperation _:
                case ITypeOfOperation _:
                case ISizeOfOperation _:
                case INameOfOperation _:
                case IIsTypeOperation _:
                case IIsPatternOperation _:
                case IDeclarationPatternOperation _:
                case IConstantPatternOperation _:
                    return true;
                case ILoopOperation loop:
                    return loop is IForLoopOperation || loop is IWhileLoopOperation;
                case IPropertyReferenceOperation property:
                    return operation == bound || IsKnownPureProperty(property.Property);
                case IBinaryOperation binary:
                    return binary.OperatorMethod == null
                        && (
                            binary.Type?.SpecialType != SpecialType.System_String
                            || binary.OperatorKind != BinaryOperatorKind.Add
                            || binary.LeftOperand.Type?.SpecialType == SpecialType.System_String
                                && binary.RightOperand.Type?.SpecialType
                                    == SpecialType.System_String
                        );
                case IUnaryOperation unary:
                    return unary.OperatorMethod == null;
                case IConversionOperation conversion:
                    return conversion.OperatorMethod == null;
                case IIncrementOrDecrementOperation increment:
                    return increment.OperatorMethod == null
                        && IsIntrinsicNumeric(increment.Target.Type);
                case IAssignmentOperation assignment:
                    return assignment is ISimpleAssignmentOperation
                        || assignment is ICompoundAssignmentOperation compound
                            && compound.OperatorMethod == null
                            && compound.InConversion.MethodSymbol == null
                            && compound.OutConversion.MethodSymbol == null
                            && compound.Type?.SpecialType != SpecialType.System_String;
                default:
                    return false;
            }
        }

        private static bool IsIntrinsicNumeric(ITypeSymbol type)
        {
            if (
                type is INamedTypeSymbol nullable
                && nullable.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
                && nullable.TypeArguments.Length == 1
            )
            {
                type = nullable.TypeArguments[0];
            }
            if (type == null)
            {
                return false;
            }
            if (type.TypeKind == TypeKind.Enum)
            {
                return true;
            }
            switch (type.SpecialType)
            {
                case SpecialType.System_SByte:
                case SpecialType.System_Byte:
                case SpecialType.System_Int16:
                case SpecialType.System_UInt16:
                case SpecialType.System_Int32:
                case SpecialType.System_UInt32:
                case SpecialType.System_Int64:
                case SpecialType.System_UInt64:
                case SpecialType.System_Char:
                case SpecialType.System_Single:
                case SpecialType.System_Double:
                case SpecialType.System_Decimal:
                case SpecialType.System_IntPtr:
                case SpecialType.System_UIntPtr:
                    return true;
                default:
                    return false;
            }
        }

        private static bool ContainsFieldReceiver(List<ISymbol> symbols)
        {
            foreach (ISymbol symbol in symbols)
            {
                if (symbol is IFieldSymbol)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsKnownPureProperty(IPropertySymbol property)
        {
            if (
                property.IsVirtual
                || property.IsAbstract
                || property.ContainingType.TypeKind == TypeKind.Interface
            )
            {
                return false;
            }
            string typeName = property.ContainingType.OriginalDefinition.ToDisplayString();
            return string.Equals(typeName, "System.Array", StringComparison.Ordinal)
                || string.Equals(typeName, "string", StringComparison.Ordinal)
                || typeName.StartsWith("System.Collections.Generic.List<", StringComparison.Ordinal)
                || typeName.StartsWith("System.Span<", StringComparison.Ordinal)
                || typeName.StartsWith("System.ReadOnlySpan<", StringComparison.Ordinal);
        }

        private static bool IsSystemNamespace(INamespaceSymbol namespaceSymbol)
        {
            string name = namespaceSymbol.ToDisplayString();
            return string.Equals(name, "System", StringComparison.Ordinal)
                || name.StartsWith("System.", StringComparison.Ordinal);
        }

        private static bool ContainsSymbol(List<ISymbol> symbols, ISymbol sought)
        {
            foreach (ISymbol symbol in symbols)
            {
                if (SymbolEqualityComparer.Default.Equals(symbol, sought))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsPropertyWrite(IPropertyReferenceOperation property)
        {
            IOperation parent = property.Parent;
            return parent is IAssignmentOperation assignment && assignment.Target == property
                || parent is IIncrementOrDecrementOperation;
        }

        private static ISymbol GetSymbol(IOperation operation)
        {
            switch (operation)
            {
                case ILocalReferenceOperation local:
                    return local.Local;
                case IParameterReferenceOperation parameter:
                    return parameter.Parameter;
                case IFieldReferenceOperation field:
                    return field.Field;
                default:
                    return null;
            }
        }

        private static IEnumerable<IOperation> Descendants(IOperation root)
        {
            yield return root;
            foreach (IOperation child in root.Children)
            {
                foreach (IOperation descendant in Descendants(child))
                {
                    yield return descendant;
                }
            }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterOperationAction(AnalyzeLoop, OperationKind.Loop);
        }
    }
}
