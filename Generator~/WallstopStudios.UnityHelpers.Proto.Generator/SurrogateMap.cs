// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System;
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// The real-to-surrogate pairs visible to a compilation.
    /// </summary>
    /// <remarks>
    /// Built from <c>[assembly: WProtoSurrogate(real, surrogate)]</c> on the compilation itself and
    /// on every assembly it references. That placement is what makes the lookup affordable: a
    /// package's surrogates have to be visible while generating a <b>consumer's</b> code, and
    /// enumerating assembly attributes is cheap where walking every namespace of every reference to
    /// find annotated types would not be.
    /// An open generic pair is closed over the real type's arguments at each member that uses it,
    /// so one declaration can serve closures a package could not name before the consumer existed.
    /// </remarks>
    internal sealed class SurrogateMap
    {
        private const string SurrogateAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoSurrogateAttribute";

        private const string ContractAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoContractAttribute";

        private readonly Dictionary<INamedTypeSymbol, INamedTypeSymbol> _pairs;
        private readonly Compilation _compilation;

        private SurrogateMap(
            Dictionary<INamedTypeSymbol, INamedTypeSymbol> pairs,
            Compilation compilation
        )
        {
            _pairs = pairs;
            _compilation = compilation;
        }

        internal static SurrogateMap Build(Compilation compilation)
        {
            Dictionary<INamedTypeSymbol, INamedTypeSymbol> pairs = new Dictionary<
                INamedTypeSymbol,
                INamedTypeSymbol
            >(SymbolEqualityComparer.Default);

            Collect(compilation.Assembly, pairs);
            foreach (
                IAssemblySymbol reference in compilation.SourceModule.ReferencedAssemblySymbols
            )
            {
                Collect(reference, pairs);
            }

            return new SurrogateMap(pairs, compilation);
        }

        private static void Collect(
            IAssemblySymbol assembly,
            Dictionary<INamedTypeSymbol, INamedTypeSymbol> pairs
        )
        {
            foreach (AttributeData attribute in assembly.GetAttributes())
            {
                if (
                    attribute.AttributeClass == null
                    || attribute.AttributeClass.ToDisplayString() != SurrogateAttribute
                    || attribute.ConstructorArguments.Length < 2
                )
                {
                    continue;
                }

                if (
                    !(attribute.ConstructorArguments[0].Value is INamedTypeSymbol real)
                    || !(attribute.ConstructorArguments[1].Value is INamedTypeSymbol surrogate)
                )
                {
                    continue;
                }

                bool openPair = real.IsUnboundGenericType && surrogate.IsUnboundGenericType;
                if (
                    real.IsUnboundGenericType != surrogate.IsUnboundGenericType
                    || (openPair && real.Arity != surrogate.Arity)
                )
                {
                    continue;
                }

                /*
                 * Reading the compilation's own assembly first lets consumer surrogate declarations override
                 * referenced ones.
                 */
                INamedTypeSymbol realKey = openPair ? real.OriginalDefinition : real;
                INamedTypeSymbol surrogateValue = openPair
                    ? surrogate.OriginalDefinition
                    : surrogate;
                if (!pairs.ContainsKey(realKey))
                {
                    pairs[realKey] = surrogateValue;
                }
            }
        }

        /// <summary>
        /// Reports every pair declared by this compilation that cannot work, before anything is
        /// emitted against it.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <param name="report">Receives one diagnostic per unusable pair.</param>
        /// <remarks>
        /// <para>
        /// Only the compilation's OWN attributes are checked. A pair from a referenced assembly was
        /// validated when that assembly was built, and re-reporting it would blame a consumer for a
        /// declaration it cannot edit -- while still failing their build on someone else's mistake.
        /// </para>
        /// <para>
        /// Both failures are otherwise deferred to places that do not name the cause: a non-contract
        /// surrogate compiles and then finds no formatter at runtime, and a missing conversion
        /// surfaces as a cast error inside generated code the developer never wrote.
        /// </para>
        /// </remarks>
        internal static void Validate(Compilation compilation, Action<Diagnostic> report)
        {
            foreach (AttributeData attribute in compilation.Assembly.GetAttributes())
            {
                if (
                    attribute.AttributeClass == null
                    || attribute.AttributeClass.ToDisplayString() != SurrogateAttribute
                    || attribute.ConstructorArguments.Length < 2
                    || !(attribute.ConstructorArguments[0].Value is INamedTypeSymbol real)
                    || !(attribute.ConstructorArguments[1].Value is INamedTypeSymbol surrogate)
                )
                {
                    continue;
                }

                Location location =
                    attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? Location.None;

                if (!IsContract(surrogate))
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.SurrogateNotAContract,
                            location,
                            real.ToDisplayString(),
                            surrogate.ToDisplayString()
                        )
                    );
                    continue;
                }

                INamedTypeSymbol conversionReal = real;
                INamedTypeSymbol conversionSurrogate = surrogate;
                bool openPair = real.IsUnboundGenericType && surrogate.IsUnboundGenericType;
                if (
                    real.IsUnboundGenericType != surrogate.IsUnboundGenericType
                    || (openPair && real.Arity != surrogate.Arity)
                )
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.SurrogateShapeMismatch,
                            location,
                            real.ToDisplayString(),
                            surrogate.ToDisplayString()
                        )
                    );
                    continue;
                }
                else if (openPair)
                {
                    if (
                        !ClosureScan.Satisfies(
                            surrogate.OriginalDefinition,
                            System.Collections.Immutable.ImmutableArray.CreateRange<ITypeSymbol>(
                                real.OriginalDefinition.TypeParameters
                            ),
                            compilation
                        )
                    )
                    {
                        report(
                            Diagnostic.Create(
                                WProtoDiagnostics.SurrogateShapeMismatch,
                                location,
                                real.ToDisplayString(),
                                surrogate.ToDisplayString()
                            )
                        );
                        continue;
                    }

                    conversionReal = ClosureScan.Close(
                        real.OriginalDefinition,
                        surrogate.OriginalDefinition.TypeParameters
                    );
                    conversionSurrogate = ClosureScan.Close(
                        surrogate.OriginalDefinition,
                        surrogate.OriginalDefinition.TypeParameters
                    );
                }

                if (!ConvertsBothWays(conversionReal, conversionSurrogate))
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.SurrogateCannotConvert,
                            location,
                            real.ToDisplayString(),
                            surrogate.ToDisplayString()
                        )
                    );
                }
            }
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

        internal INamedTypeSymbol For(ITypeSymbol type)
        {
            if (!(type is INamedTypeSymbol named))
            {
                return null;
            }

            if (_pairs.TryGetValue(named, out INamedTypeSymbol surrogate))
            {
                return surrogate;
            }

            if (
                !named.IsGenericType
                || !_pairs.TryGetValue(named.OriginalDefinition, out INamedTypeSymbol definition)
                || definition.Arity != named.Arity
                || !ClosureScan.Satisfies(definition, named.TypeArguments, _compilation)
            )
            {
                return null;
            }

            return ClosureScan.Close(definition, named.TypeArguments);
        }

        /// <summary>
        /// Reports whether the two types convert to each other in both directions.
        /// </summary>
        /// <remarks>
        /// A surrogate that cannot be converted back is worse than an unsupported type: it produces
        /// bytes that look right and a value that never returns, so the conversion is checked at
        /// generate time rather than discovered as a compile error inside emitted code.
        /// </remarks>
        internal static bool ConvertsBothWays(INamedTypeSymbol real, INamedTypeSymbol surrogate)
        {
            return HasConversion(surrogate, real, surrogate)
                    && HasConversion(surrogate, surrogate, real)
                || (HasConversion(real, real, surrogate) && HasConversion(real, surrogate, real))
                || (
                    HasConversion(surrogate, real, surrogate)
                    && HasConversion(real, surrogate, real)
                )
                || (
                    HasConversion(real, real, surrogate)
                    && HasConversion(surrogate, surrogate, real)
                );
        }

        private static bool HasConversion(
            INamedTypeSymbol declaringType,
            ITypeSymbol from,
            ITypeSymbol to
        )
        {
            foreach (ISymbol member in declaringType.GetMembers())
            {
                if (
                    member is IMethodSymbol method
                    && method.MethodKind == MethodKind.Conversion
                    && method.Parameters.Length == 1
                    && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, from)
                    && SymbolEqualityComparer.Default.Equals(method.ReturnType, to)
                )
                {
                    return true;
                }
            }

            return false;
        }
    }
}
