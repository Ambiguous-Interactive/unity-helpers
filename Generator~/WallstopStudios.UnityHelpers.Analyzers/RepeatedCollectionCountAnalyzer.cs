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

    /// <summary>Reports repeated collection size observations on uninterrupted evaluation paths.</summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class RepeatedCollectionCountAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.RepeatedCollectionCount);

        private static Dictionary<ISymbol, ISymbol> NewState()
        {
            return new Dictionary<ISymbol, ISymbol>(SymbolEqualityComparer.Default);
        }

        private static Dictionary<ISymbol, ISymbol> CopyState(Dictionary<ISymbol, ISymbol> state)
        {
            return new Dictionary<ISymbol, ISymbol>(state, SymbolEqualityComparer.Default);
        }

        private static Dictionary<ISymbol, ISymbol> Intersect(
            Dictionary<ISymbol, ISymbol> first,
            Dictionary<ISymbol, ISymbol> second
        )
        {
            Dictionary<ISymbol, ISymbol> common = NewState();
            foreach (KeyValuePair<ISymbol, ISymbol> entry in first)
            {
                if (
                    second.TryGetValue(entry.Key, out ISymbol other)
                    && SymbolEqualityComparer.Default.Equals(entry.Value, other)
                )
                {
                    common.Add(entry.Key, entry.Value);
                }
            }
            return common;
        }

        private static bool IsCollectionInterface(INamedTypeSymbol type, Compilation compilation)
        {
            if (
                SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly)
            )
            {
                return false;
            }
            return SymbolEqualityComparer.Default.Equals(
                    type.OriginalDefinition,
                    compilation.GetTypeByMetadataName("System.Collections.ICollection")
                )
                || SymbolEqualityComparer.Default.Equals(
                    type.OriginalDefinition,
                    compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1")
                )
                || SymbolEqualityComparer.Default.Equals(
                    type.OriginalDefinition,
                    compilation.GetTypeByMetadataName(
                        "System.Collections.Generic.IReadOnlyCollection`1"
                    )
                );
        }

        private static bool IsCollectionCount(
            IPropertyReferenceOperation property,
            Compilation compilation
        )
        {
            if (
                property.Instance == null
                || !string.Equals(property.Property.Name, "Count", StringComparison.Ordinal)
                || property.Arguments.Length != 0
                || property.Type.SpecialType != SpecialType.System_Int32
            )
            {
                return false;
            }
            if (IsCollectionInterface(property.Property.ContainingType, compilation))
            {
                return true;
            }
            return ImplementsCount(
                property.Instance.Type,
                property.Property,
                compilation,
                new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default)
            );
        }

        private static bool ImplementsCount(
            ITypeSymbol receiverType,
            IPropertySymbol property,
            Compilation compilation,
            HashSet<ITypeSymbol> visited
        )
        {
            if (!visited.Add(receiverType))
            {
                return false;
            }
            if (receiverType is ITypeParameterSymbol parameter)
            {
                foreach (ITypeSymbol constraint in parameter.ConstraintTypes)
                {
                    if (ImplementsCount(constraint, property, compilation, visited))
                    {
                        return true;
                    }
                }
                return false;
            }
            if (!(receiverType is INamedTypeSymbol type))
            {
                return false;
            }
            foreach (INamedTypeSymbol contract in type.AllInterfaces)
            {
                if (!IsCollectionInterface(contract, compilation))
                {
                    continue;
                }
                foreach (ISymbol member in contract.GetMembers("Count"))
                {
                    if (
                        SymbolEqualityComparer.Default.Equals(
                            type.FindImplementationForInterfaceMember(member),
                            property
                        )
                    )
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static ISymbol ReceiverSymbol(IOperation operation)
        {
            if (operation == null)
            {
                return null;
            }
            while (
                operation is IConversionOperation conversion
                && conversion.OperatorMethod == null
                && (conversion.Conversion.IsIdentity || conversion.Conversion.IsReference)
            )
            {
                operation = conversion.Operand;
            }
            if (operation is ILocalReferenceOperation local)
            {
                return local.Local;
            }
            if (operation is IParameterReferenceOperation parameter)
            {
                return parameter.Parameter;
            }
            if (
                operation is IFieldReferenceOperation field
                && (
                    field.Instance == null
                    || field.Instance is IInstanceReferenceOperation instance
                        && instance.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance
                )
            )
            {
                return field.Field;
            }
            return null;
        }

        private static IPropertySymbol ObservedProperty(IPropertyReferenceOperation property)
        {
            IOperation receiver = property.Instance;
            while (
                receiver is IConversionOperation conversion
                && conversion.OperatorMethod == null
                && (conversion.Conversion.IsIdentity || conversion.Conversion.IsReference)
            )
            {
                receiver = conversion.Operand;
            }
            if (
                receiver.Type is INamedTypeSymbol type
                && type.IsSealed
                && type.TypeKind != TypeKind.Interface
                && property.Property.ContainingType.TypeKind == TypeKind.Interface
            )
            {
                return type.FindImplementationForInterfaceMember(property.Property)
                        as IPropertySymbol
                    ?? property.Property;
            }
            return property.Property;
        }

        private static bool IsFrameworkType(
            INamedTypeSymbol owner,
            string metadataName,
            Compilation compilation
        )
        {
            return !SymbolEqualityComparer.Default.Equals(
                    owner.ContainingAssembly,
                    compilation.Assembly
                )
                && SymbolEqualityComparer.Default.Equals(
                    owner.OriginalDefinition,
                    compilation.GetTypeByMetadataName(metadataName)
                );
        }

        private static bool IsKnownLength(
            IPropertyReferenceOperation property,
            Compilation compilation
        )
        {
            if (property.Instance == null || property.Arguments.Length != 0)
            {
                return false;
            }
            bool length = string.Equals(property.Property.Name, "Length", StringComparison.Ordinal);
            bool longLength = string.Equals(
                property.Property.Name,
                "LongLength",
                StringComparison.Ordinal
            );
            if (!length && !longLength)
            {
                return false;
            }
            INamedTypeSymbol owner = property.Property.ContainingType;
            if (IsFrameworkType(owner, "System.Array", compilation))
            {
                return property.Type.SpecialType
                    == (longLength ? SpecialType.System_Int64 : SpecialType.System_Int32);
            }
            if (!length)
            {
                return false;
            }
            if (IsFrameworkType(owner, "System.Buffers.ReadOnlySequence`1", compilation))
            {
                return property.Type.SpecialType == SpecialType.System_Int64;
            }
            if (property.Type.SpecialType != SpecialType.System_Int32)
            {
                return false;
            }
            return IsFrameworkType(owner, "System.String", compilation)
                || IsFrameworkType(owner, "System.Span`1", compilation)
                || IsFrameworkType(owner, "System.ReadOnlySpan`1", compilation)
                || IsFrameworkType(owner, "System.Memory`1", compilation)
                || IsFrameworkType(owner, "System.ReadOnlyMemory`1", compilation)
                || IsFrameworkType(owner, "System.Text.StringBuilder", compilation)
                || IsFrameworkType(
                    owner,
                    "System.Collections.Immutable.ImmutableArray`1",
                    compilation
                );
        }

        private static IMethodSymbol EnumerableSizeMethod(
            IInvocationOperation invocation,
            Compilation compilation
        )
        {
            IMethodSymbol method = invocation.TargetMethod;
            IMethodSymbol fullMethod = method.ReducedFrom ?? method;
            if (
                !fullMethod.IsStatic
                || !fullMethod.IsExtensionMethod
                || fullMethod.Arity != 1
                || fullMethod.Parameters.Length != 1
                || !IsFrameworkType(
                    fullMethod.ContainingType,
                    "System.Linq.Enumerable",
                    compilation
                )
            )
            {
                return null;
            }
            bool count = string.Equals(fullMethod.Name, "Count", StringComparison.Ordinal);
            bool longCount = string.Equals(fullMethod.Name, "LongCount", StringComparison.Ordinal);
            if (
                (!count && !longCount)
                || fullMethod.ReturnType.SpecialType
                    != (longCount ? SpecialType.System_Int64 : SpecialType.System_Int32)
                || !(fullMethod.Parameters[0].Type is INamedTypeSymbol sourceType)
                || !IsFrameworkType(
                    sourceType,
                    "System.Collections.Generic.IEnumerable`1",
                    compilation
                )
            )
            {
                return null;
            }
            ITypeSymbol[] arguments = new ITypeSymbol[method.TypeArguments.Length];
            for (int index = 0; index < arguments.Length; ++index)
            {
                arguments[index] = method.TypeArguments[index];
            }
            return fullMethod.OriginalDefinition.Construct(arguments);
        }

        private static void RecordObservation(
            IOperation operation,
            ISymbol receiver,
            ISymbol observed,
            bool mayInvokeCallbacks,
            Dictionary<ISymbol, ISymbol> state,
            OperationBlockAnalysisContext context
        )
        {
            if (
                state.TryGetValue(receiver, out ISymbol earlier)
                && SymbolEqualityComparer.Default.Equals(earlier, observed)
            )
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        UnityHelpersDiagnostics.RepeatedCollectionCount,
                        operation.Syntax.GetLocation(),
                        operation.Syntax.ToString()
                    )
                );
            }
            if (mayInvokeCallbacks)
            {
                state.Clear();
            }
            state[receiver] = observed;
        }

        private static bool IsKnownPureGetter(
            IPropertyReferenceOperation property,
            Compilation compilation
        )
        {
            if (IsKnownLength(property, compilation))
            {
                return true;
            }
            INamedTypeSymbol owner = property.Property.ContainingType.OriginalDefinition;
            if (
                SymbolEqualityComparer.Default.Equals(
                    owner.ContainingAssembly,
                    compilation.Assembly
                )
            )
            {
                return false;
            }
            string metadataName =
                owner.ContainingNamespace.ToDisplayString() + "." + owner.MetadataName;
            if (
                !SymbolEqualityComparer.Default.Equals(
                    owner,
                    compilation.GetTypeByMetadataName(metadataName)
                )
            )
            {
                return false;
            }
            if (
                string.Equals(metadataName, "System.Buffers.ArrayPool`1", StringComparison.Ordinal)
                && string.Equals(property.Property.Name, "Shared", StringComparison.Ordinal)
            )
            {
                return true;
            }
            if (!string.Equals(property.Property.Name, "Count", StringComparison.Ordinal))
            {
                return false;
            }
            switch (metadataName)
            {
                case "System.Collections.Generic.List`1":
                case "System.Collections.Generic.Dictionary`2":
                case "System.Collections.Generic.HashSet`1":
                case "System.Collections.Generic.Queue`1":
                case "System.Collections.Generic.Stack`1":
                case "System.Collections.Generic.LinkedList`1":
                case "System.Collections.Generic.SortedList`2":
                case "System.Collections.Concurrent.ConcurrentQueue`1":
                case "System.Collections.Concurrent.ConcurrentStack`1":
                case "System.Collections.Concurrent.ConcurrentBag`1":
                case "System.Collections.Concurrent.ConcurrentDictionary`2":
                    return true;
                default:
                    return false;
            }
        }

        private static bool MayInvokeUserOperator(IOperation operation)
        {
            if (operation is IBinaryOperation binary)
            {
                return binary.OperatorMethod != null
                    || binary.LeftOperand.Type?.TypeKind == TypeKind.Dynamic
                    || binary.RightOperand.Type?.TypeKind == TypeKind.Dynamic;
            }
            if (operation is IUnaryOperation unary)
            {
                return unary.OperatorMethod != null
                    || unary.Operand.Type?.TypeKind == TypeKind.Dynamic;
            }
            if (operation is IConversionOperation conversion)
            {
                return conversion.OperatorMethod != null
                    || conversion.Operand.Type?.TypeKind == TypeKind.Dynamic;
            }
            return false;
        }

        /* Unknown operation shapes may hide callbacks between their children. */
        private static bool HasKnownEvaluationOrder(IOperation operation)
        {
            return operation is IBlockOperation
                || operation is IMethodBodyOperation
                || operation is IConstructorBodyOperation
                || operation is IVariableDeclarationGroupOperation
                || operation is IVariableDeclarationOperation
                || operation is IVariableDeclaratorOperation
                || operation is IVariableInitializerOperation
                || operation is IExpressionStatementOperation
                || operation is IArgumentOperation
                || operation is ILiteralOperation
                || operation is ILocalReferenceOperation
                || operation is IParameterReferenceOperation
                || operation is IInstanceReferenceOperation
                || operation is IFieldReferenceOperation
                || operation is IArrayElementReferenceOperation
                || operation is IParenthesizedOperation
                || operation is IBinaryOperation
                || operation is IUnaryOperation
                || operation is IConversionOperation
                || operation is IInvocationOperation
                || operation is IArrayCreationOperation
                || operation is IArrayInitializerOperation
                || operation is IAwaitOperation
                || operation is IReturnOperation
                || operation is IThrowOperation
                || operation is IBranchOperation
                || operation is IAssignmentOperation
                || operation is IIncrementOrDecrementOperation
                || operation is IEventAssignmentOperation
                || operation is IDynamicInvocationOperation
                || operation is IDynamicIndexerAccessOperation
                || operation is IDynamicMemberReferenceOperation;
        }

        private static void VisitCondition(
            IOperation condition,
            Dictionary<ISymbol, ISymbol> state,
            OperationBlockAnalysisContext context,
            out Dictionary<ISymbol, ISymbol> whenTrue,
            out Dictionary<ISymbol, ISymbol> whenFalse
        )
        {
            if (
                condition is IBinaryOperation binary
                && !MayInvokeUserOperator(binary)
                && (
                    binary.OperatorKind == BinaryOperatorKind.ConditionalAnd
                    || binary.OperatorKind == BinaryOperatorKind.ConditionalOr
                )
            )
            {
                VisitCondition(
                    binary.LeftOperand,
                    state,
                    context,
                    out Dictionary<ISymbol, ISymbol> leftTrue,
                    out Dictionary<ISymbol, ISymbol> leftFalse
                );
                bool conjunction = binary.OperatorKind == BinaryOperatorKind.ConditionalAnd;
                VisitCondition(
                    binary.RightOperand,
                    conjunction ? leftTrue : leftFalse,
                    context,
                    out Dictionary<ISymbol, ISymbol> rightTrue,
                    out Dictionary<ISymbol, ISymbol> rightFalse
                );
                whenTrue = conjunction ? rightTrue : Intersect(leftTrue, rightTrue);
                whenFalse = conjunction ? Intersect(leftFalse, rightFalse) : rightFalse;
                return;
            }
            if (
                condition is IUnaryOperation unary
                && !MayInvokeUserOperator(unary)
                && unary.OperatorKind == UnaryOperatorKind.Not
            )
            {
                VisitCondition(unary.Operand, state, context, out whenFalse, out whenTrue);
                return;
            }
            Visit(condition, state, context);
            whenTrue = CopyState(state);
            whenFalse = CopyState(state);
        }

        private static void VisitAssignmentTarget(
            IOperation target,
            Dictionary<ISymbol, ISymbol> state,
            OperationBlockAnalysisContext context
        )
        {
            if (target is IPropertyReferenceOperation property)
            {
                Visit(property.Instance, state, context);
                foreach (IArgumentOperation argument in property.Arguments)
                {
                    Visit(argument, state, context);
                }
            }
            else
            {
                Visit(target, state, context);
            }
        }

        private static void Visit(
            IOperation operation,
            Dictionary<ISymbol, ISymbol> state,
            OperationBlockAnalysisContext context
        )
        {
            if (operation == null || operation is INameOfOperation)
            {
                return;
            }
            if (
                operation is IFieldReferenceOperation staticField
                && staticField.Field.IsStatic
                && !staticField.Field.HasConstantValue
                && !state.ContainsKey(staticField.Field)
            )
            {
                state.Clear();
            }
            if (operation is IAnonymousFunctionOperation anonymous)
            {
                Visit(anonymous.Body, NewState(), context);
                return;
            }
            if (operation is ILocalFunctionOperation localFunction)
            {
                Visit(localFunction.Body, NewState(), context);
                return;
            }
            if (operation is IObjectCreationOperation creation)
            {
                foreach (IArgumentOperation argument in creation.Arguments)
                {
                    Visit(argument, state, context);
                }
                state.Clear();
                Visit(creation.Initializer, state, context);
                state.Clear();
                return;
            }
            if (operation is IDynamicObjectCreationOperation dynamicCreation)
            {
                foreach (IOperation argument in dynamicCreation.Arguments)
                {
                    Visit(argument, state, context);
                }
                state.Clear();
                Visit(dynamicCreation.Initializer, state, context);
                state.Clear();
                return;
            }
            if (operation is ITypeParameterObjectCreationOperation genericCreation)
            {
                state.Clear();
                Visit(genericCreation.Initializer, state, context);
                state.Clear();
                return;
            }
            if (
                operation is IBinaryOperation conditionalOperator
                && MayInvokeUserOperator(conditionalOperator)
                && (
                    conditionalOperator.OperatorKind == BinaryOperatorKind.ConditionalAnd
                    || conditionalOperator.OperatorKind == BinaryOperatorKind.ConditionalOr
                )
            )
            {
                Visit(conditionalOperator.LeftOperand, state, context);
                state.Clear();
                Visit(conditionalOperator.RightOperand, state, context);
                state.Clear();
                return;
            }
            if (operation is IConditionalOperation conditional)
            {
                VisitCondition(
                    conditional.Condition,
                    state,
                    context,
                    out Dictionary<ISymbol, ISymbol> whenTrue,
                    out Dictionary<ISymbol, ISymbol> whenFalse
                );
                Visit(conditional.WhenTrue, whenTrue, context);
                Visit(conditional.WhenFalse, whenFalse, context);
                state.Clear();
                return;
            }
            if (
                operation is IBinaryOperation binary
                && !MayInvokeUserOperator(binary)
                && (
                    binary.OperatorKind == BinaryOperatorKind.ConditionalAnd
                    || binary.OperatorKind == BinaryOperatorKind.ConditionalOr
                )
            )
            {
                VisitCondition(
                    operation,
                    state,
                    context,
                    out Dictionary<ISymbol, ISymbol> whenTrue,
                    out Dictionary<ISymbol, ISymbol> whenFalse
                );
                Dictionary<ISymbol, ISymbol> common = Intersect(whenTrue, whenFalse);
                state.Clear();
                foreach (KeyValuePair<ISymbol, ISymbol> entry in common)
                {
                    state.Add(entry.Key, entry.Value);
                }
                return;
            }
            if (
                operation is ILoopOperation
                || operation is ITryOperation
                || operation is ILockOperation
                || operation is IUsingOperation
                || operation is ISwitchOperation
                || operation is ISwitchExpressionOperation
                || operation is IConditionalAccessOperation
            )
            {
                foreach (IOperation child in operation.Children)
                {
                    Visit(child, NewState(), context);
                }
                state.Clear();
                return;
            }
            if (operation is ISimpleAssignmentOperation assignment)
            {
                VisitAssignmentTarget(assignment.Target, state, context);
                Visit(assignment.Value, state, context);
                state.Clear();
                return;
            }
            if (operation is IPropertyReferenceOperation propertyReference)
            {
                Visit(propertyReference.Instance, state, context);
                foreach (IArgumentOperation argument in propertyReference.Arguments)
                {
                    Visit(argument, state, context);
                }
                bool pureGetter = IsKnownPureGetter(propertyReference, context.Compilation);
                if (
                    IsCollectionCount(propertyReference, context.Compilation)
                    || IsKnownLength(propertyReference, context.Compilation)
                )
                {
                    ISymbol receiver = ReceiverSymbol(propertyReference.Instance);
                    if (receiver != null)
                    {
                        RecordObservation(
                            propertyReference,
                            receiver,
                            ObservedProperty(propertyReference),
                            !pureGetter,
                            state,
                            context
                        );
                        return;
                    }
                }
                if (!pureGetter)
                {
                    state.Clear();
                }
                return;
            }
            if (operation is IInvocationOperation invocation)
            {
                IMethodSymbol observed = EnumerableSizeMethod(invocation, context.Compilation);
                if (observed != null)
                {
                    Visit(invocation.Instance, state, context);
                    foreach (IArgumentOperation argument in invocation.Arguments)
                    {
                        Visit(argument, state, context);
                    }
                    IOperation source =
                        invocation.TargetMethod.ReducedFrom != null ? invocation.Instance
                        : invocation.Arguments.Length == 1 ? invocation.Arguments[0].Value
                        : null;
                    ISymbol receiver = ReceiverSymbol(source);
                    if (receiver != null)
                    {
                        RecordObservation(invocation, receiver, observed, true, state, context);
                    }
                    else
                    {
                        state.Clear();
                    }
                    return;
                }
            }
            if (!HasKnownEvaluationOrder(operation))
            {
                foreach (IOperation child in operation.Children)
                {
                    Visit(child, NewState(), context);
                }
                state.Clear();
                return;
            }
            foreach (IOperation child in operation.Children)
            {
                Visit(child, state, context);
            }
            if (
                operation is IInvocationOperation
                || operation is IObjectCreationOperation
                || operation is IArrayCreationOperation
                || operation is IAwaitOperation
                || operation is IReturnOperation
                || operation is IThrowOperation
                || operation is IBranchOperation
                || operation is IAssignmentOperation
                || operation is IIncrementOrDecrementOperation
                || operation is IEventAssignmentOperation
                || operation is IDynamicInvocationOperation
                || operation is IDynamicIndexerAccessOperation
                || operation is IDynamicMemberReferenceOperation
                || operation is IInterpolationOperation
                || MayInvokeUserOperator(operation)
            )
            {
                state.Clear();
            }
        }

        private static void AnalyzeBlock(OperationBlockAnalysisContext context)
        {
            foreach (IOperation block in context.OperationBlocks)
            {
                Visit(block, NewState(), context);
            }
        }

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterOperationBlockAction(AnalyzeBlock);
        }
    }
}
