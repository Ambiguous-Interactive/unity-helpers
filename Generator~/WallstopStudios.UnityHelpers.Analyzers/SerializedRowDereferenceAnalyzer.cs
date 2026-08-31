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
    /// Reports a walk of a Unity-serialized collection of object references that dereferences a row
    /// without testing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The class hierarchy is resolved rather than matched against a list of Unity base types. A
    /// first draft of this rule elsewhere carried its own twenty-two-entry root list and missed a
    /// real instance entirely; sharing the resolver with the destroyed-object diagnostic found it
    /// immediately, and -- more importantly -- two hierarchies would drift, and the one that drifted
    /// would be the one reporting clean.
    /// </para>
    /// <para>
    /// A null test anywhere in the walk's body clears it, not only one that dominates the
    /// dereference. The rule is about the author never having considered the row as an input; once
    /// they have, deciding where the test belongs is theirs. Compaction clears it too, for the same
    /// reason: where a list is walked more than once the right repair is to drop the null rows once
    /// up front, and a rule that only accepts a per-loop test forces the worse of the two repairs.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class SerializedRowDereferenceAnalyzer : DiagnosticAnalyzer
    {
        private const string UnityObjectMetadataName = "UnityEngine.Object";

        private const string SerializeFieldMetadataName = "UnityEngine.SerializeField";

        private const string SerializeReferenceMetadataName = "UnityEngine.SerializeReference";

        private const string NonSerializedMetadataName = "System.NonSerializedAttribute";

        private const string ListMetadataName = "System.Collections.Generic.List`1";

        private const string CompactionMethodName = "RemoveAll";

        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.SerializedRowDereferencedWithoutTest);

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            INamedTypeSymbol unityObject = context.Compilation.GetTypeByMetadataName(
                UnityObjectMetadataName
            );
            if (unityObject == null)
            {
                return;
            }

            INamedTypeSymbol list = context.Compilation.GetTypeByMetadataName(ListMetadataName);
            INamedTypeSymbol serializeField = context.Compilation.GetTypeByMetadataName(
                SerializeFieldMetadataName
            );
            INamedTypeSymbol serializeReference = context.Compilation.GetTypeByMetadataName(
                SerializeReferenceMetadataName
            );
            INamedTypeSymbol nonSerialized = context.Compilation.GetTypeByMetadataName(
                NonSerializedMetadataName
            );

            context.RegisterOperationAction(
                (OperationAnalysisContext operationContext) =>
                    AnalyzeLoop(
                        operationContext,
                        unityObject,
                        list,
                        serializeField,
                        serializeReference,
                        nonSerialized
                    ),
                OperationKind.Loop
            );
        }

        private static void AnalyzeLoop(
            OperationAnalysisContext context,
            INamedTypeSymbol unityObject,
            INamedTypeSymbol list,
            INamedTypeSymbol serializeField,
            INamedTypeSymbol serializeReference,
            INamedTypeSymbol nonSerialized
        )
        {
            if (!(context.Operation is IForEachLoopOperation loop))
            {
                return;
            }

            IFieldSymbol field = SerializedRowSourceOf(
                loop.Collection,
                unityObject,
                list,
                serializeField,
                serializeReference,
                nonSerialized
            );

            if (field == null)
            {
                return;
            }

            ILocalSymbol row = RowOf(loop);
            if (row == null)
            {
                return;
            }

            HashSet<ISymbol> rowAndItsAliases = AliasesOf(row, loop.Body);
            IOperation dereference = null;
            bool tested = false;
            foreach (IOperation operation in Descendants(loop.Body))
            {
                if (IsNullTestOf(operation, rowAndItsAliases))
                {
                    tested = true;
                    break;
                }

                if (dereference == null && IsDereferenceOf(operation, rowAndItsAliases))
                {
                    dereference = operation;
                }
            }

            /*
                Compaction is asked last because answering it walks the whole declaring type, and a
                walk that never dereferences its row has nothing to report whatever the answer is.
            */
            if (tested || dereference == null || IsCompacted(context, field))
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    UnityHelpersDiagnostics.SerializedRowDereferencedWithoutTest,
                    dereference.Syntax.GetLocation(),
                    field.Name,
                    row.Name
                )
            );
        }

        private static ILocalSymbol RowOf(IForEachLoopOperation loop)
        {
            if (
                loop.LoopControlVariable is IVariableDeclaratorOperation declarator
                && declarator.Symbol != null
            )
            {
                return declarator.Symbol;
            }

            return (loop.LoopControlVariable as ILocalReferenceOperation)?.Local;
        }

        private static IFieldSymbol SerializedRowSourceOf(
            IOperation collection,
            INamedTypeSymbol unityObject,
            INamedTypeSymbol list,
            INamedTypeSymbol serializeField,
            INamedTypeSymbol serializeReference,
            INamedTypeSymbol nonSerialized
        )
        {
            IOperation source = WithoutConversions(collection);
            if (
                !(source is IFieldReferenceOperation reference)
                || !(reference.Field is IFieldSymbol field)
            )
            {
                return null;
            }

            if (!IsUnitySerialized(field, serializeField, serializeReference, nonSerialized))
            {
                return null;
            }

            ITypeSymbol element = ElementTypeOf(field.Type, list);
            return IsUnityObject(element, unityObject) ? field : null;
        }

        private static ITypeSymbol ElementTypeOf(ITypeSymbol type, INamedTypeSymbol list)
        {
            if (type is IArrayTypeSymbol array)
            {
                return array.ElementType;
            }

            if (
                list != null
                && type is INamedTypeSymbol named
                && named.IsGenericType
                && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, list)
            )
            {
                return named.TypeArguments[0];
            }

            return null;
        }

        private static bool IsUnitySerialized(
            IFieldSymbol field,
            INamedTypeSymbol serializeField,
            INamedTypeSymbol serializeReference,
            INamedTypeSymbol nonSerialized
        )
        {
            if (field.IsStatic || field.IsConst)
            {
                return false;
            }

            bool refused = false;
            foreach (AttributeData attribute in field.GetAttributes())
            {
                INamedTypeSymbol attributeClass = attribute.AttributeClass;
                if (
                    SymbolEqualityComparer.Default.Equals(attributeClass, serializeField)
                    || SymbolEqualityComparer.Default.Equals(attributeClass, serializeReference)
                )
                {
                    return true;
                }

                if (SymbolEqualityComparer.Default.Equals(attributeClass, nonSerialized))
                {
                    refused = true;
                }
            }

            return !refused && field.DeclaredAccessibility == Accessibility.Public;
        }

        /// <summary>
        /// The row and every local in <paramref name="body"/> that is a straight copy of it.
        /// </summary>
        /// <remarks>
        /// Measured against this package's own <c>EffectHandler</c>, which copies the row into a
        /// local and tests the copy: without following the alias the rule reports correct code, and
        /// a rule that reports correct code is one people turn off.
        /// </remarks>
        private static HashSet<ISymbol> AliasesOf(ILocalSymbol row, IOperation body)
        {
            HashSet<ISymbol> aliases = new HashSet<ISymbol>(SymbolEqualityComparer.Default) { row };

            List<KeyValuePair<ISymbol, ISymbol>> copies =
                new List<KeyValuePair<ISymbol, ISymbol>>();
            foreach (IOperation operation in Descendants(body))
            {
                if (
                    operation is IVariableDeclaratorOperation declarator
                    && declarator.Symbol != null
                    && WithoutConversions(declarator.Initializer?.Value)
                        is ILocalReferenceOperation source
                )
                {
                    copies.Add(new KeyValuePair<ISymbol, ISymbol>(declarator.Symbol, source.Local));
                }
            }

            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int index = 0; index < copies.Count; ++index)
                {
                    KeyValuePair<ISymbol, ISymbol> copy = copies[index];
                    if (aliases.Contains(copy.Value) && aliases.Add(copy.Key))
                    {
                        grew = true;
                    }
                }
            }

            return aliases;
        }

        private static bool IsDereferenceOf(IOperation operation, HashSet<ISymbol> row)
        {
            if (operation is IInvocationOperation invocation)
            {
                return IsRowReference(invocation.Instance, row);
            }

            if (operation is IMemberReferenceOperation member)
            {
                return IsRowReference(member.Instance, row);
            }

            return false;
        }

        private static bool IsRowReference(IOperation instance, HashSet<ISymbol> row)
        {
            return WithoutConversions(instance) is ILocalReferenceOperation local
                && row.Contains(local.Local);
        }

        /// <summary>
        /// Whether <paramref name="operation"/> tests <paramref name="row"/> against null, in any of
        /// the spellings a Unity author reaches for.
        /// </summary>
        /// <remarks>
        /// The bare-truth form matters as much as the comparison. <c>UnityEngine.Object</c> declares
        /// an implicit conversion to <c>bool</c> that is the same native aliveness check
        /// <c>==</c> performs, so <c>if (!row) { continue; }</c> is an ordinary and correct guard.
        /// </remarks>
        private static bool IsNullTestOf(IOperation operation, HashSet<ISymbol> row)
        {
            if (operation is IBinaryOperation binary)
            {
                bool comparison =
                    binary.OperatorKind == BinaryOperatorKind.Equals
                    || binary.OperatorKind == BinaryOperatorKind.NotEquals;

                if (
                    comparison
                    && (
                        (IsRowReference(binary.LeftOperand, row) && IsNull(binary.RightOperand))
                        || (IsRowReference(binary.RightOperand, row) && IsNull(binary.LeftOperand))
                    )
                )
                {
                    return true;
                }
            }

            if (operation is IIsPatternOperation pattern && IsRowReference(pattern.Value, row))
            {
                return true;
            }

            if (
                operation is IConversionOperation conversion
                && conversion.Type != null
                && conversion.Type.SpecialType == SpecialType.System_Boolean
                && IsRowReference(conversion.Operand, row)
            )
            {
                return true;
            }

            return operation is IUnaryOperation unary
                && unary.OperatorKind == UnaryOperatorKind.Not
                && IsRowReference(unary.Operand, row);
        }

        private static bool IsNull(IOperation operation)
        {
            IOperation value = WithoutConversions(operation);
            return value != null
                && value.ConstantValue.HasValue
                && value.ConstantValue.Value == null;
        }

        /// <summary>
        /// Whether the field's null rows are dropped once, somewhere in the type that declares the
        /// walk.
        /// </summary>
        /// <remarks>
        /// Type-wide rather than before-the-loop, because the compaction usually happens in
        /// <c>Awake</c> and the walk that would be reported is in <c>Update</c>. An assignment whose
        /// value mentions null counts as well, which is how an array is compacted.
        /// </remarks>
        private static bool IsCompacted(OperationAnalysisContext context, IFieldSymbol field)
        {
            SyntaxNode declaration =
                context.Operation.Syntax.FirstAncestorOrSelf<TypeDeclarationSyntax>();
            SemanticModel model = context.Operation.SemanticModel;
            if (declaration == null || model == null)
            {
                return false;
            }

            foreach (SyntaxNode node in declaration.DescendantNodes())
            {
                if (
                    node is InvocationExpressionSyntax invocation
                    && invocation.Expression is MemberAccessExpressionSyntax access
                    && access.Name.Identifier.ValueText == CompactionMethodName
                    && NamesField(model, access.Expression, field)
                )
                {
                    return true;
                }

                if (
                    node is AssignmentExpressionSyntax assignment
                    && NamesField(model, assignment.Left, field)
                    && MentionsNull(assignment.Right)
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NamesField(
            SemanticModel model,
            SyntaxNode expression,
            IFieldSymbol field
        )
        {
            return SymbolEqualityComparer.Default.Equals(
                model.GetSymbolInfo(expression).Symbol,
                field
            );
        }

        private static bool MentionsNull(SyntaxNode expression)
        {
            if (expression.IsKind(SyntaxKind.NullLiteralExpression))
            {
                return true;
            }

            foreach (SyntaxNode node in expression.DescendantNodes())
            {
                if (node.IsKind(SyntaxKind.NullLiteralExpression))
                {
                    return true;
                }
            }

            return false;
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

        private static IOperation WithoutConversions(IOperation operation)
        {
            IOperation current = operation;
            while (current is IConversionOperation conversion)
            {
                current = conversion.Operand;
            }

            return current;
        }

        private static bool IsUnityObject(ITypeSymbol type, INamedTypeSymbol unityObject)
        {
            if (type == null)
            {
                return false;
            }

            if (type is ITypeParameterSymbol typeParameter)
            {
                foreach (ITypeSymbol constraint in typeParameter.ConstraintTypes)
                {
                    if (IsUnityObject(constraint, unityObject))
                    {
                        return true;
                    }
                }

                return false;
            }

            for (ITypeSymbol candidate = type; candidate != null; candidate = candidate.BaseType)
            {
                if (SymbolEqualityComparer.Default.Equals(candidate, unityObject))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
