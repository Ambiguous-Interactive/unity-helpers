// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;

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
        /// Resolves the closed generic a node constructs, from a written type or a tuple literal.
        /// </summary>
        /// <param name="model">The semantic model for the node's tree.</param>
        /// <param name="node">The node to resolve.</param>
        /// <param name="where">Receives the node's location, for diagnostics.</param>
        /// <returns>The closed construction, or <c>null</c> when the node is not one.</returns>
        /// <remarks>
        /// Scanning only <c>TypeSyntax</c> missed the most ordinary way a marshalled generic ever
        /// appears once <c>ValueTuple</c> became one: <c>Serializer.ProtoSerialize((7, 1.5f))</c>
        /// names no type, so nothing registered a formatter and the call fell through to the
        /// reflective path in a player -- the exact failure the marshal was added to close. The
        /// underlying tuple type is returned so <c>(int Count, float Weight)</c> and
        /// <c>(int, float)</c> are one closure rather than two spellings.
        /// </remarks>
        internal static INamedTypeSymbol Closure(
            SemanticModel model,
            SyntaxNode node,
            out Location where
        )
        {
            ITypeSymbol resolved;
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax type)
            {
                where = type.GetLocation();
                resolved =
                    model.GetTypeInfo(type).Type ?? model.GetSymbolInfo(type).Symbol as ITypeSymbol;
            }
            else if (node is Microsoft.CodeAnalysis.CSharp.Syntax.TupleExpressionSyntax tuple)
            {
                where = tuple.GetLocation();
                resolved = model.GetTypeInfo(tuple).Type;
                if (resolved is INamedTypeSymbol tupleType && tupleType.IsTupleType)
                {
                    resolved = tupleType.TupleUnderlyingType ?? tupleType;
                }
            }
            else
            {
                where = Location.None;
                return null;
            }

            /*
             * IsNameable also rejects still-open constructions because their type parameters have no concrete
             * names.
             */
            if (
                !(resolved is INamedTypeSymbol named)
                || !named.IsGenericType
                || named.IsUnboundGenericType
            )
            {
                return null;
            }

            return named;
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

            for (int index = 0; index < arguments.Length; index++)
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
            for (int index = 0; index < closed.Length; index++)
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
            for (int index = 0; index < arguments.Count; index++)
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
    }
}
