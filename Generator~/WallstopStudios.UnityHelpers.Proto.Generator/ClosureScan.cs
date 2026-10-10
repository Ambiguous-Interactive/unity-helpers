// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Finds the closed generic constructions a compilation writes, and answers whether a generic
    /// stand-in can be closed over the same arguments.
    /// </summary>
    /// <remarks>
    /// Shared by every map that pairs an open generic with an open generic of its own --
    /// <see cref="MarshalMap"/> for protobuf formatters, <see cref="JsonConverterMap"/> for JSON
    /// converters. Both ask the same three questions of a closure a developer wrote, and both exist
    /// because <c>MakeGenericType</c> is the one call IL2CPP cannot compile, so the closure has to
    /// be named in source that the consumer's own build compiles.
    /// </remarks>
    internal static class ClosureScan
    {
        /// <summary>
        /// Resolves source type uses once for every registration scan in this compilation.
        /// </summary>
        /// <remarks>
        /// Keep source occurrences in syntax order, including duplicates; append required wrapper uses
        /// after propagation. Each consumer owns its filtering
        /// and diagnostic precedence. Tuple literals and factory results count even when no type
        /// is written explicitly.
        /// Reusing discovery's semantic models avoids rebinding already inspected declarations.
        /// Release them per tree because later registration needs only symbols and locations.
        /// </remarks>
        internal static IReadOnlyList<TypeUse> Types(
            Compilation compilation,
            Dictionary<SyntaxTree, SemanticModel> models
        )
        {
            List<TypeUse> uses = new List<TypeUse>();
            List<MethodUse> calls = new List<MethodUse>();
            Dictionary<IMethodSymbol, SerializationRequirements> requirements = new Dictionary<
                IMethodSymbol,
                SerializationRequirements
            >(SymbolEqualityComparer.Default);
            INamedTypeSymbol[] serializationApis =
            {
                compilation.GetTypeByMetadataName(
                    "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoFacade"
                ),
                compilation.GetTypeByMetadataName(
                    "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoGeneric`1"
                ),
            };
            HashSet<IMethodSymbol> serializationMethods = new HashSet<IMethodSymbol>(
                SymbolEqualityComparer.Default
            );
            foreach (INamedTypeSymbol api in serializationApis)
            {
                if (api == null)
                {
                    continue;
                }
                foreach (ISymbol member in api.GetMembers())
                {
                    if (
                        member is IMethodSymbol method
                        && method.DeclaredAccessibility == Accessibility.Public
                        && method.IsStatic
                        && !string.Equals(method.Name, "Reset", System.StringComparison.Ordinal)
                    )
                    {
                        serializationMethods.Add(method.OriginalDefinition);
                    }
                }
            }
            INamedTypeSymbol serializer = compilation.GetTypeByMetadataName(
                "WallstopStudios.UnityHelpers.Core.Serialization.Serializer"
            );
            if (serializer != null)
            {
                foreach (
                    string name in new[]
                    {
                        "ProtoSerialize",
                        "ProtoDeserialize",
                        "TryProtoDeserialize",
                    }
                )
                {
                    foreach (ISymbol member in serializer.GetMembers(name))
                    {
                        if (member is IMethodSymbol method)
                        {
                            serializationMethods.Add(method.OriginalDefinition);
                        }
                    }
                }
            }
            INamedTypeSymbol[] memberAttributes =
            {
                compilation.GetTypeByMetadataName(
                    "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoMemberAttribute"
                ),
                compilation.GetTypeByMetadataName("ProtoBuf.ProtoMemberAttribute"),
                compilation.GetTypeByMetadataName(
                    "System.Runtime.Serialization.DataMemberAttribute"
                ),
            };
            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                if (!models.TryGetValue(tree, out SemanticModel model))
                {
                    model = compilation.GetSemanticModel(tree);
                }

                models.Remove(tree);

                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    ITypeSymbol resolved;
                    bool serializationDependency = false;
                    if (node is Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax type)
                    {
                        serializationDependency = IsSerializedMemberType(
                            type,
                            model,
                            memberAttributes
                        );
                        resolved =
                            model.GetTypeInfo(type).Type
                            ?? model.GetSymbolInfo(type).Symbol as ITypeSymbol;
                    }
                    else if (
                        node is Microsoft.CodeAnalysis.CSharp.Syntax.TupleExpressionSyntax tuple
                    )
                    {
                        resolved = model.GetTypeInfo(tuple).Type;
                        if (resolved is INamedTypeSymbol tupleType && tupleType.IsTupleType)
                        {
                            resolved = tupleType.TupleUnderlyingType ?? tupleType;
                        }
                    }
                    else if (
                        node
                        is Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax invocation
                    )
                    {
                        resolved = model.GetTypeInfo(invocation).Type;
                        if (model.GetSymbolInfo(invocation).Symbol is IMethodSymbol method)
                        {
                            method =
                                (
                                    model.GetOperation(invocation) as IInvocationOperation
                                )?.TargetMethod
                                ?? method;
                            bool api = serializationMethods.Contains(method.OriginalDefinition);
                            if (
                                api
                                || method.OriginalDefinition.DeclaringSyntaxReferences.Length != 0
                            )
                            {
                                MethodUse call = DescribeCall(invocation, method, model);
                                calls.Add(call);
                                if (api)
                                {
                                    requirements[method.OriginalDefinition] = ApiRequirements(
                                        method,
                                        compilation.GetTypeByMetadataName("System.Type")
                                    );
                                    serializationDependency = true;
                                }
                            }
                        }
                    }
                    else
                    {
                        continue;
                    }

                    CollectTypes(resolved, node.GetLocation(), uses, serializationDependency);
                }
            }

            PropagateSerializationRequirements(calls, requirements, uses);
            return uses;
        }

        internal static bool IsIncidentalUnnameableTuple(TypeUse use, Compilation compilation)
        {
            if (use.IsSerializationDependency || TypeNaming.IsNameable(use.Type, compilation))
            {
                return false;
            }
            INamedTypeSymbol named = use.Type.TupleUnderlyingType ?? use.Type;
            if (named.Arity != 2 && named.Arity != 3)
            {
                return false;
            }
            INamedTypeSymbol tuple = compilation.GetTypeByMetadataName(
                named.Arity == 2 ? "System.ValueTuple`2" : "System.ValueTuple`3"
            );
            return tuple != null
                && SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, tuple);
        }

        /// <summary>
        /// Reports whether the stand-in's type parameters accept these arguments.
        /// </summary>
        /// <param name="definition">The stand-in's unbound definition.</param>
        /// <param name="arguments">The arguments the closure supplies.</param>
        /// <param name="compilation">The compilation used to classify constraint conversions.</param>
        /// <returns><c>false</c> when closing the stand-in would not compile.</returns>
        /// <remarks>
        /// <para>
        /// A stand-in may be declared with constraints its subject does not have --
        /// <c>Formatter&lt;T&gt; where T : struct</c> against an unconstrained <c>Ring&lt;T&gt;</c> --
        /// and <see cref="INamedTypeSymbol.Construct(ITypeSymbol[])"/> does not enforce them. Emitting
        /// the registration anyway is <c>CS0453</c> inside generated code the developer never wrote,
        /// which is exactly what the diagnostics for these pairs exist to prevent.
        /// </para>
        /// <para>
        /// Constraint types are substituted over the same arguments before Roslyn classifies the
        /// conversion. This matters when a stand-in is stricter than its subject, such as
        /// <c>where T : IComparable&lt;T&gt;</c> on an otherwise unconstrained pair: constructing the
        /// symbol succeeds, but naming it in generated source would produce CS0311.
        /// </para>
        /// </remarks>
        internal static bool Satisfies(
            INamedTypeSymbol definition,
            ImmutableArray<ITypeSymbol> arguments,
            Compilation compilation
        )
        {
            if (definition.TypeParameters.Length != arguments.Length)
            {
                return false;
            }

            for (int index = 0; index < arguments.Length; ++index)
            {
                ITypeParameterSymbol parameter = definition.TypeParameters[index];
                ITypeSymbol argument = arguments[index];

                if (parameter.HasReferenceTypeConstraint && !argument.IsReferenceType)
                {
                    return false;
                }

                if (parameter.HasValueTypeConstraint && !argument.IsValueType)
                {
                    return false;
                }

                if (parameter.HasUnmanagedTypeConstraint && !argument.IsUnmanagedType)
                {
                    return false;
                }

                if (parameter.HasConstructorConstraint && !HasParameterlessConstructor(argument))
                {
                    return false;
                }

                foreach (ITypeSymbol constraint in parameter.ConstraintTypes)
                {
                    ITypeSymbol closedConstraint = Substitute(
                        constraint,
                        definition,
                        arguments,
                        compilation
                    );
                    if (
                        closedConstraint == null
                        || !(compilation is CSharpCompilation csharpCompilation)
                        || !csharpCompilation
                            .ClassifyConversion(argument, closedConstraint)
                            .IsImplicit
                    )
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>Closes a definition over the given arguments, or returns <c>null</c>.</summary>
        /// <typeparam name="TArgument">The argument symbol kind.</typeparam>
        /// <param name="definition">The unbound definition.</param>
        /// <param name="arguments">The arguments to close it over.</param>
        /// <returns>The closed construction, or <c>null</c> on an arity mismatch.</returns>
        internal static INamedTypeSymbol Close<TArgument>(
            INamedTypeSymbol definition,
            IReadOnlyList<TArgument> arguments
        )
            where TArgument : ITypeSymbol
        {
            if (definition.Arity == 0)
            {
                return definition;
            }

            if (definition.Arity != arguments.Count)
            {
                return null;
            }

            ITypeSymbol[] closed = new ITypeSymbol[arguments.Count];
            for (int index = 0; index < arguments.Count; ++index)
            {
                closed[index] = arguments[index];
            }

            return definition.Construct(closed);
        }

        /// <summary>
        /// Reports whether <c>new</c> on this type compiles from anywhere.
        /// </summary>
        /// <param name="type">The type to construct.</param>
        /// <returns><c>true</c> when it has a public parameterless constructor.</returns>
        internal static bool HasPublicParameterlessConstructor(INamedTypeSymbol type)
        {
            if (type == null || type.IsAbstract || type.IsStatic)
            {
                return false;
            }

            foreach (IMethodSymbol constructor in type.InstanceConstructors)
            {
                if (
                    constructor.Parameters.Length == 0
                    && constructor.DeclaredAccessibility == Accessibility.Public
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static MethodUse DescribeCall(
            InvocationExpressionSyntax invocation,
            IMethodSymbol method,
            SemanticModel model
        )
        {
            ImmutableArray<ArgumentUse>.Builder arguments =
                ImmutableArray.CreateBuilder<ArgumentUse>();
            if (model.GetOperation(invocation) is IInvocationOperation operation)
            {
                foreach (IArgumentOperation argument in operation.Arguments)
                {
                    if (argument.Parameter == null)
                    {
                        continue;
                    }
                    IOperation value = argument.Value;
                    while (
                        value is IConversionOperation conversion
                        && conversion.OperatorMethod == null
                    )
                    {
                        value = conversion.Operand;
                    }
                    arguments.Add(
                        new ArgumentUse(
                            argument.Parameter.Ordinal,
                            value.Type,
                            (value as ITypeOfOperation)?.TypeOperand,
                            (value as IParameterReferenceOperation)?.Parameter
                        )
                    );
                }
            }
            return new MethodUse(
                method,
                model.GetEnclosingSymbol(invocation.SpanStart) as IMethodSymbol,
                invocation.GetLocation(),
                arguments.ToImmutable()
            );
        }

        private static SerializationRequirements ApiRequirements(
            IMethodSymbol method,
            INamedTypeSymbol runtimeType
        )
        {
            SerializationRequirements result = new SerializationRequirements(
                SymbolEqualityComparer.Default
            );
            foreach (ITypeParameterSymbol parameter in method.OriginalDefinition.TypeParameters)
            {
                result.Types.Add(parameter);
            }
            for (
                INamedTypeSymbol type = method.ContainingType;
                type != null;
                type = type.ContainingType
            )
            {
                foreach (ITypeParameterSymbol parameter in type.OriginalDefinition.TypeParameters)
                {
                    result.Types.Add(parameter);
                }
            }
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                if (
                    SymbolEqualityComparer.Default.Equals(
                        parameter.OriginalDefinition.Type,
                        runtimeType
                    )
                )
                {
                    result.TypeValues.Add(parameter.Ordinal);
                }
                foreach (ITypeParameterSymbol root in result.Types)
                {
                    if (
                        SymbolEqualityComparer.Default.Equals(
                            parameter.OriginalDefinition.Type,
                            root
                        )
                    )
                    {
                        result.Values.Add(parameter.Ordinal);
                        break;
                    }
                }
            }
            return result;
        }

        private static void PropagateSerializationRequirements(
            List<MethodUse> calls,
            Dictionary<IMethodSymbol, SerializationRequirements> requirements,
            List<TypeUse> uses
        )
        {
            bool changed;
            do
            {
                changed = false;
                foreach (MethodUse call in calls)
                {
                    if (
                        call.Caller == null
                        || !requirements.TryGetValue(
                            call.Method.OriginalDefinition,
                            out SerializationRequirements required
                        )
                    )
                    {
                        continue;
                    }
                    for (
                        IMethodSymbol enclosing = call.Caller;
                        enclosing != null;
                        enclosing = enclosing.ContainingSymbol as IMethodSymbol
                    )
                    {
                        IMethodSymbol caller = enclosing.OriginalDefinition;
                        if (
                            !requirements.TryGetValue(
                                caller,
                                out SerializationRequirements propagated
                            )
                        )
                        {
                            propagated = new SerializationRequirements(
                                SymbolEqualityComparer.Default
                            );
                            requirements.Add(caller, propagated);
                        }
                        IEnumerable<ITypeParameterSymbol> requiredTypes = object.ReferenceEquals(
                            required.Types,
                            propagated.Types
                        )
                            ? ImmutableArray.CreateRange(required.Types)
                            : (IEnumerable<ITypeParameterSymbol>)required.Types;
                        foreach (ITypeParameterSymbol parameter in requiredTypes)
                        {
                            changed |= AddTypeRequirements(
                                ResolveParameter(call.Method, parameter),
                                caller,
                                propagated
                            );
                        }
                        foreach (ArgumentUse argument in call.Arguments)
                        {
                            if (required.TypeValues.Contains(argument.Ordinal))
                            {
                                changed |= AddTypeRequirements(
                                    argument.TypeOperand,
                                    caller,
                                    propagated
                                );
                                if (
                                    argument.Parameter != null
                                    && SymbolEqualityComparer.Default.Equals(
                                        argument.Parameter.ContainingSymbol,
                                        caller
                                    )
                                )
                                {
                                    changed |= propagated.TypeValues.Add(
                                        argument.Parameter.Ordinal
                                    );
                                }
                            }
                            if (!required.Values.Contains(argument.Ordinal))
                            {
                                continue;
                            }
                            changed |= AddTypeRequirements(argument.Type, caller, propagated);
                            if (
                                argument.Parameter != null
                                && SymbolEqualityComparer.Default.Equals(
                                    argument.Parameter.ContainingSymbol,
                                    caller
                                )
                            )
                            {
                                changed |= propagated.Values.Add(argument.Parameter.Ordinal);
                            }
                        }
                    }
                }
            } while (changed);

            foreach (MethodUse call in calls)
            {
                if (
                    !requirements.TryGetValue(
                        call.Method.OriginalDefinition,
                        out SerializationRequirements required
                    )
                )
                {
                    continue;
                }
                foreach (ITypeParameterSymbol parameter in required.Types)
                {
                    CollectTypes(
                        ResolveParameter(call.Method, parameter),
                        call.Location,
                        uses,
                        true
                    );
                }
                foreach (ArgumentUse argument in call.Arguments)
                {
                    if (required.TypeValues.Contains(argument.Ordinal))
                    {
                        CollectTypes(argument.TypeOperand, call.Location, uses, true);
                    }
                    if (required.Values.Contains(argument.Ordinal))
                    {
                        CollectTypes(argument.Type, call.Location, uses, true);
                    }
                }
            }
        }

        private static ITypeSymbol ResolveParameter(
            IMethodSymbol method,
            ITypeParameterSymbol parameter
        )
        {
            if (
                SymbolEqualityComparer.Default.Equals(
                    parameter.ContainingSymbol,
                    method.OriginalDefinition
                )
            )
            {
                return method.TypeArguments[parameter.Ordinal];
            }
            for (
                INamedTypeSymbol type = method.ContainingType;
                type != null;
                type = type.ContainingType
            )
            {
                if (
                    SymbolEqualityComparer.Default.Equals(
                        parameter.ContainingSymbol,
                        type.OriginalDefinition
                    )
                )
                {
                    return type.TypeArguments[parameter.Ordinal];
                }
            }
            return null;
        }

        private static bool AddTypeRequirements(
            ITypeSymbol type,
            IMethodSymbol caller,
            SerializationRequirements requirements
        )
        {
            if (type is ITypeParameterSymbol parameter)
            {
                if (SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, caller))
                {
                    return requirements.Types.Add(parameter);
                }
                for (
                    INamedTypeSymbol enclosing = caller.ContainingType;
                    enclosing != null;
                    enclosing = enclosing.ContainingType
                )
                {
                    if (
                        SymbolEqualityComparer.Default.Equals(
                            parameter.ContainingSymbol,
                            enclosing.OriginalDefinition
                        )
                    )
                    {
                        return requirements.Types.Add(parameter);
                    }
                }
                return false;
            }
            if (type is IArrayTypeSymbol array)
            {
                return AddTypeRequirements(array.ElementType, caller, requirements);
            }
            if (!(type is INamedTypeSymbol named))
            {
                return false;
            }
            bool changed = AddTypeRequirements(named.ContainingType, caller, requirements);
            foreach (ITypeSymbol argument in named.TypeArguments)
            {
                changed |= AddTypeRequirements(argument, caller, requirements);
            }
            return changed;
        }

        private static bool IsSerializedMemberType(
            TypeSyntax type,
            SemanticModel model,
            INamedTypeSymbol[] attributes
        )
        {
            for (SyntaxNode node = type; node != null; node = node.Parent)
            {
                ISymbol member = null;
                if (
                    node is FieldDeclarationSyntax field
                    && field.Declaration.Type.Span.Contains(type.Span)
                    && 0 < field.Declaration.Variables.Count
                )
                {
                    if (field.AttributeLists.Count == 0)
                    {
                        return false;
                    }
                    member = model.GetDeclaredSymbol(field.Declaration.Variables[0]);
                }
                else if (
                    node is PropertyDeclarationSyntax property
                    && property.Type.Span.Contains(type.Span)
                )
                {
                    if (property.AttributeLists.Count == 0)
                    {
                        return false;
                    }
                    member = model.GetDeclaredSymbol(property);
                }
                if (member != null)
                {
                    foreach (AttributeData attribute in member.GetAttributes())
                    {
                        foreach (INamedTypeSymbol expected in attributes)
                        {
                            if (
                                expected != null
                                && SymbolEqualityComparer.Default.Equals(
                                    attribute.AttributeClass,
                                    expected
                                )
                            )
                            {
                                return true;
                            }
                        }
                    }
                    return false;
                }
                if (node is MemberDeclarationSyntax)
                {
                    return false;
                }
            }
            return false;
        }

        private static void CollectTypes(
            ITypeSymbol type,
            Location location,
            List<TypeUse> uses,
            bool serializationDependency
        )
        {
            if (type is IArrayTypeSymbol array)
            {
                CollectTypes(array.ElementType, location, uses, serializationDependency);
                return;
            }

            if (!(type is INamedTypeSymbol named))
            {
                return;
            }

            uses.Add(new TypeUse(named, location, serializationDependency));
            CollectTypes(named.ContainingType, location, uses, serializationDependency);
            foreach (ITypeSymbol argument in named.TypeArguments)
            {
                CollectTypes(argument, location, uses, serializationDependency);
            }
        }

        private static ITypeSymbol Substitute(
            ITypeSymbol type,
            INamedTypeSymbol definition,
            ImmutableArray<ITypeSymbol> arguments,
            Compilation compilation
        )
        {
            if (type is ITypeParameterSymbol parameter)
            {
                if (
                    SymbolEqualityComparer.Default.Equals(
                        parameter.ContainingType?.OriginalDefinition,
                        definition.OriginalDefinition
                    )
                    && 0 <= parameter.Ordinal
                    && parameter.Ordinal < arguments.Length
                )
                {
                    return arguments[parameter.Ordinal];
                }

                return type;
            }

            if (type is IArrayTypeSymbol array)
            {
                ITypeSymbol element = Substitute(
                    array.ElementType,
                    definition,
                    arguments,
                    compilation
                );
                return compilation.CreateArrayTypeSymbol(element, array.Rank);
            }

            if (!(type is INamedTypeSymbol named) || !named.IsGenericType)
            {
                return type;
            }

            INamedTypeSymbol definitionToClose = named.OriginalDefinition;
            if (named.ContainingType != null)
            {
                INamedTypeSymbol closedContaining =
                    Substitute(named.ContainingType, definition, arguments, compilation)
                    as INamedTypeSymbol;
                if (closedContaining == null)
                {
                    return null;
                }

                definitionToClose = null;
                foreach (
                    INamedTypeSymbol candidate in closedContaining.GetTypeMembers(
                        named.Name,
                        named.Arity
                    )
                )
                {
                    if (
                        SymbolEqualityComparer.Default.Equals(
                            candidate.OriginalDefinition,
                            named.OriginalDefinition
                        )
                    )
                    {
                        definitionToClose = candidate;
                        break;
                    }
                }

                if (definitionToClose == null)
                {
                    return null;
                }
            }

            if (named.Arity == 0)
            {
                return definitionToClose;
            }

            ITypeSymbol[] closed = new ITypeSymbol[named.TypeArguments.Length];
            for (int index = 0; index < closed.Length; ++index)
            {
                closed[index] = Substitute(
                    named.TypeArguments[index],
                    definition,
                    arguments,
                    compilation
                );
            }

            return definitionToClose.Construct(closed);
        }

        private static bool HasParameterlessConstructor(ITypeSymbol type)
        {
            if (type.IsValueType)
            {
                return true;
            }

            // Type parameters expose new() through constraints, not constructor symbols.
            if (type is ITypeParameterSymbol parameter)
            {
                return parameter.HasConstructorConstraint || parameter.HasValueTypeConstraint;
            }

            return type is INamedTypeSymbol named && HasPublicParameterlessConstructor(named);
        }

        internal readonly struct TypeUse
        {
            internal INamedTypeSymbol Type { get; }
            internal Location Location { get; }
            internal bool IsSerializationDependency { get; }

            internal TypeUse(INamedTypeSymbol type, Location location, bool serializationDependency)
            {
                Type = type;
                Location = location;
                IsSerializationDependency = serializationDependency;
            }
        }

        private readonly struct SerializationRequirements
        {
            public readonly HashSet<ITypeParameterSymbol> Types;
            public readonly HashSet<int> Values;
            public readonly HashSet<int> TypeValues;

            public SerializationRequirements(IEqualityComparer<ITypeParameterSymbol> comparer)
            {
                Types = new HashSet<ITypeParameterSymbol>(comparer);
                Values = new HashSet<int>();
                TypeValues = new HashSet<int>();
            }
        }

        private readonly struct MethodUse
        {
            public readonly IMethodSymbol Method;
            public readonly IMethodSymbol Caller;
            public readonly Location Location;
            public readonly ImmutableArray<ArgumentUse> Arguments;

            public MethodUse(
                IMethodSymbol method,
                IMethodSymbol caller,
                Location location,
                ImmutableArray<ArgumentUse> arguments
            )
            {
                Method = method;
                Caller = caller;
                Location = location;
                Arguments = arguments;
            }
        }

        private readonly struct ArgumentUse
        {
            public readonly int Ordinal;
            public readonly ITypeSymbol Type;
            public readonly IParameterSymbol Parameter;
            public readonly ITypeSymbol TypeOperand;

            public ArgumentUse(
                int ordinal,
                ITypeSymbol type,
                ITypeSymbol typeOperand,
                IParameterSymbol parameter
            )
            {
                Ordinal = ordinal;
                Type = type;
                Parameter = parameter;
                TypeOperand = typeOperand;
            }
        }
    }
}
