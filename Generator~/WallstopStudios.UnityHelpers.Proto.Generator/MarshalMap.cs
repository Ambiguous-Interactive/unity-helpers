// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System;
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// The root-marshal pairs visible to a compilation, and the registrations they produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built from <c>[assembly: WProtoRootMarshal(real, formatter)]</c> on the compilation and on
    /// every assembly it references, for the same reason <see cref="SurrogateMap"/> is: a consumer's
    /// build has to find the marshals this package ships, and reading assembly attributes is the one
    /// cross-reference lookup that costs nothing.
    /// </para>
    /// <para>
    /// A marshal differs from a surrogate in where it applies. A surrogate substitutes a type
    /// everywhere, so it is consulted while members are emitted; a marshal applies only when the
    /// type is the ROOT of a serialization, so it never reaches member emission at all and appears
    /// exclusively as a registration. That is why this map produces registrations rather than
    /// answering questions about types.
    /// </para>
    /// </remarks>
    internal sealed class MarshalMap
    {
        private const string MarshalAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoRootMarshalAttribute";

        private const string ContractAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoContractAttribute";

        private const string FormatterInterface = "IWProtoFormatter";

        private const string FormatterNamespace =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto";

        private readonly Dictionary<INamedTypeSymbol, INamedTypeSymbol> _pairs;

        private readonly HashSet<INamedTypeSymbol> _declaredHere;

        private MarshalMap(
            Dictionary<INamedTypeSymbol, INamedTypeSymbol> pairs,
            HashSet<INamedTypeSymbol> declaredHere
        )
        {
            _pairs = pairs;
            _declaredHere = declaredHere;
        }

        internal static MarshalMap Build(Compilation compilation)
        {
            Dictionary<INamedTypeSymbol, INamedTypeSymbol> pairs = new Dictionary<
                INamedTypeSymbol,
                INamedTypeSymbol
            >(SymbolEqualityComparer.Default);
            HashSet<INamedTypeSymbol> declaredHere = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );

            foreach (Pair pair in Pairs(compilation.Assembly))
            {
                if (pairs.ContainsKey(pair.Real))
                {
                    continue;
                }

                pairs[pair.Real] = pair.Formatter;
                declaredHere.Add(pair.Real);
            }

            foreach (
                IAssemblySymbol reference in compilation.SourceModule.ReferencedAssemblySymbols
            )
            {
                foreach (Pair pair in Pairs(reference))
                {
                    // First declaration wins and the compilation's own assembly was read first, so a
                    // consumer's pair for a type this package also marshals is the one this
                    // compilation emits. Which registration survives at RUNTIME is a separate
                    // question -- both registrars run in the same Unity phase, unordered -- and
                    // WProtoRootMarshalProvider says so.
                    if (!pairs.ContainsKey(pair.Real))
                    {
                        pairs[pair.Real] = pair.Formatter;
                    }
                }
            }

            return new MarshalMap(pairs, declaredHere);
        }

        /// <summary>
        /// Reports every pair declared by this compilation that cannot work.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <param name="report">Receives one diagnostic per unusable pair.</param>
        /// <remarks>
        /// Only the compilation's OWN attributes are checked, matching <see cref="SurrogateMap"/>: a
        /// pair from a referenced assembly was validated when that assembly was built, and
        /// re-reporting it would fail a consumer's build over a declaration they cannot edit.
        /// </remarks>
        internal static void Validate(Compilation compilation, Action<Diagnostic> report)
        {
            HashSet<INamedTypeSymbol> seen = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );

            foreach (Pair pair in Pairs(compilation.Assembly))
            {
                Location location =
                    pair.Attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? Location.None;

                if (!seen.Add(pair.Real))
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.DuplicateMarshal,
                            location,
                            pair.Real.ToDisplayString(),
                            pair.Formatter.ToDisplayString()
                        )
                    );
                    continue;
                }

                if (IsContract(pair.Real))
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.MarshalOnContract,
                            location,
                            pair.Real.ToDisplayString(),
                            pair.Formatter.ToDisplayString()
                        )
                    );
                    continue;
                }

                if (pair.Real.Arity != pair.Formatter.Arity)
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.MarshalArityMismatch,
                            location,
                            pair.Real.ToDisplayString(),
                            pair.Formatter.ToDisplayString()
                        )
                    );
                    continue;
                }

                // Closed with the real type's OWN type parameters, which is the only closure that
                // exists at this point: `DequeMarshalFormatter<T>` has to implement
                // `IWProtoFormatter<Deque<T>>` for the same T, and checking it that way catches a
                // pair whose parameters are transposed as well as one that implements nothing.
                INamedTypeSymbol real = Close(pair.Real, pair.Real.TypeParameters);
                INamedTypeSymbol formatter = Close(pair.Formatter, pair.Real.TypeParameters);

                if (!Formats(formatter, real) || !HasPublicParameterlessConstructor(formatter))
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.MarshalNotAFormatter,
                            location,
                            pair.Real.ToDisplayString(),
                            pair.Formatter.ToDisplayString()
                        )
                    );
                }
            }
        }

        /// <summary>
        /// Returns one registration expression per marshal this compilation can register.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <returns>Constructor expressions, one per registerable marshal.</returns>
        /// <remarks>
        /// <para>
        /// A non-generic marshal is registered by the assembly that DECLARES it and by no other. The
        /// registry is a static field per closed type, shared by everything in the process, so a
        /// consumer re-registering the same formatter would be a duplicate rather than a fix.
        /// </para>
        /// <para>
        /// A generic one cannot work that way: a registrar cannot register an open generic, and
        /// <c>Deque&lt;TheirStruct&gt;</c> cannot appear in this package's own sources because the
        /// struct does not exist yet. So each closed construction named anywhere in this
        /// compilation's source produces a registration here -- the same discovery-from-syntax the
        /// generic contracts use, and the property that makes a consumer's own closure work at all.
        /// </para>
        /// </remarks>
        internal IEnumerable<string> Registrations(
            Compilation compilation,
            Action<Diagnostic> report,
            HashSet<string> announced
        )
        {
            HashSet<string> found = new HashSet<string>();
            List<string> registrations = new List<string>();

            foreach (KeyValuePair<INamedTypeSymbol, INamedTypeSymbol> pair in _pairs)
            {
                if (0 < pair.Key.Arity || !_declaredHere.Contains(pair.Key))
                {
                    continue;
                }

                string qualified = pair.Value.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                if (found.Add(qualified))
                {
                    registrations.Add("new " + qualified + "()");
                }
            }

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    if (!(node is Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax type))
                    {
                        continue;
                    }

                    INamedTypeSymbol closure = Closure(model, type);
                    if (closure == null)
                    {
                        continue;
                    }

                    if (
                        !_pairs.TryGetValue(
                            closure.OriginalDefinition,
                            out INamedTypeSymbol definition
                        )
                    )
                    {
                        continue;
                    }

                    INamedTypeSymbol formatter = Close(definition, closure.TypeArguments);
                    if (formatter == null || !Satisfies(definition, closure.TypeArguments))
                    {
                        continue;
                    }

                    // The closure is what a developer wrote and can act on; the formatter is named
                    // by an attribute they may not own, so an unnameable one is skipped quietly.
                    if (
                        !TypeNaming.IsNameable(formatter, compilation)
                        || TypeNaming.ReportIfUnnameable(
                            closure,
                            compilation,
                            type.GetLocation(),
                            report,
                            announced
                        )
                    )
                    {
                        continue;
                    }

                    string qualified = formatter.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    if (found.Add(qualified))
                    {
                        registrations.Add("new " + qualified + "()");
                    }
                }
            }

            return registrations;
        }

        private static INamedTypeSymbol Closure(
            SemanticModel model,
            Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax type
        )
        {
            ITypeSymbol resolved =
                model.GetTypeInfo(type).Type ?? model.GetSymbolInfo(type).Symbol as ITypeSymbol;
            // Still-open constructions need no separate test: a type parameter has no name, so
            // TypeNaming.IsNameable refuses `StandInRing<T>` for the same reason it refuses a
            // private nested one. Two checks for one question is how half a rule goes stale.
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
        /// Reports whether the formatter's type parameters accept these arguments.
        /// </summary>
        /// <param name="definition">The formatter's unbound definition.</param>
        /// <param name="arguments">The arguments the closure supplies.</param>
        /// <returns><c>false</c> when closing the formatter would not compile.</returns>
        /// <remarks>
        /// <para>
        /// A formatter may be declared with constraints its collection does not have --
        /// <c>Formatter&lt;T&gt; where T : struct</c> against an unconstrained <c>Ring&lt;T&gt;</c> --
        /// and <see cref="INamedTypeSymbol.Construct(ITypeSymbol[])"/> does not enforce them. Emitting
        /// the registration anyway is <c>CS0453</c> inside generated code the developer never wrote,
        /// which is exactly what the diagnostics for these pairs exist to prevent.
        /// </para>
        /// <para>
        /// The three constraint <b>kinds</b> are checked against the real argument, which needs no
        /// substitution and no compilation context. Constraint <b>types</b>
        /// (<c>where T : IComparable&lt;T&gt;</c>) are not: satisfying them means substituting the
        /// closure's arguments through the constraint, and getting that subtly wrong would drop
        /// registrations that do compile. A mismatch there still fails the build, and names the
        /// formatter while doing so.
        /// </para>
        /// </remarks>
        private static bool Satisfies(
            INamedTypeSymbol definition,
            System.Collections.Immutable.ImmutableArray<ITypeSymbol> arguments
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

                if (parameter.HasConstructorConstraint && !HasParameterlessConstructor(argument))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasParameterlessConstructor(ITypeSymbol type)
        {
            if (type.IsValueType)
            {
                return true;
            }

            if (!(type is INamedTypeSymbol named) || named.IsAbstract)
            {
                return false;
            }

            foreach (IMethodSymbol constructor in named.InstanceConstructors)
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

        private static INamedTypeSymbol Close<TArgument>(
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

        private static bool Formats(INamedTypeSymbol formatter, INamedTypeSymbol real)
        {
            if (formatter == null || real == null)
            {
                return false;
            }

            foreach (INamedTypeSymbol candidate in formatter.AllInterfaces)
            {
                if (
                    candidate.Name != FormatterInterface
                    || candidate.Arity != 1
                    || candidate.ContainingNamespace?.ToDisplayString() != FormatterNamespace
                )
                {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], real))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasPublicParameterlessConstructor(INamedTypeSymbol formatter)
        {
            if (formatter == null || formatter.IsAbstract || formatter.IsStatic)
            {
                return false;
            }

            foreach (IMethodSymbol constructor in formatter.InstanceConstructors)
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

        private static bool IsContract(INamedTypeSymbol type)
        {
            foreach (AttributeData attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == ContractAttribute)
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<Pair> Pairs(IAssemblySymbol assembly)
        {
            foreach (AttributeData attribute in assembly.GetAttributes())
            {
                if (
                    attribute.AttributeClass == null
                    || attribute.AttributeClass.ToDisplayString() != MarshalAttribute
                    || attribute.ConstructorArguments.Length < 2
                )
                {
                    continue;
                }

                if (
                    !(attribute.ConstructorArguments[0].Value is INamedTypeSymbol real)
                    || !(attribute.ConstructorArguments[1].Value is INamedTypeSymbol formatter)
                )
                {
                    continue;
                }

                // `typeof(Deque<>)` arrives as the UNBOUND construction, whose type arguments are the
                // parameters themselves. Normalizing to the definition is what makes it comparable to
                // the `ConstructedFrom` of a closure found in source.
                yield return new Pair(
                    real.OriginalDefinition,
                    formatter.OriginalDefinition,
                    attribute
                );
            }
        }

        private readonly struct Pair
        {
            internal Pair(
                INamedTypeSymbol real,
                INamedTypeSymbol formatter,
                AttributeData attribute
            )
            {
                Real = real;
                Formatter = formatter;
                Attribute = attribute;
            }

            internal INamedTypeSymbol Real { get; }

            internal INamedTypeSymbol Formatter { get; }

            internal AttributeData Attribute { get; }
        }
    }
}
