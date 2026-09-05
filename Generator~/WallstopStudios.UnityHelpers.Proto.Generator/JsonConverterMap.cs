// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System;
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// The JSON converter pairs visible to a compilation, and the registrations they produce.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Built from <c>[assembly: WJsonConverter(serialized, converter)]</c> on the compilation and on
    /// every assembly it references, exactly as <see cref="MarshalMap"/> is, and for the same
    /// reason: a consumer's build has to find the converters this package ships without naming any
    /// of them.
    /// </para>
    /// <para>
    /// It exists because <c>JsonConverterFactory</c> is unusable under IL2CPP for a closure nothing
    /// names. A factory builds its converter with <c>MakeGenericType</c> and
    /// <c>Activator.CreateInstance</c>, and IL2CPP compiles only what it can see statically -- so
    /// <c>Deque&lt;TheirStruct&gt;</c>'s converter does not exist in the player, and the first save
    /// throws <c>ExecutionEngineException</c>. The package's own tests never catch it because the
    /// closures they exercise are named right here.
    /// </para>
    /// <para>
    /// Only generic pairs are meaningful. A non-generic converter has a closure of exactly one, so
    /// the assembly that declares it can add it to <c>JsonSerializerOptions.Converters</c> directly,
    /// and nothing about it needs generating.
    /// </para>
    /// </remarks>
    internal sealed class JsonConverterMap
    {
        private const string ConverterAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters.WJsonConverterAttribute";

        private const string ConverterBase = "System.Text.Json.Serialization.JsonConverter";

        private readonly Dictionary<INamedTypeSymbol, INamedTypeSymbol> _pairs;

        private JsonConverterMap(Dictionary<INamedTypeSymbol, INamedTypeSymbol> pairs)
        {
            _pairs = pairs;
        }

        /// <summary>
        /// Reports whether this compilation can see System.Text.Json at all.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <returns><c>false</c> when nothing here may name a <c>JsonConverter</c>.</returns>
        /// <remarks>
        /// An assembly can reference this package, write <c>SerializableDictionary&lt;byte,
        /// decimal&gt;</c>, and never reference System.Text.Json -- which is the default for a Unity
        /// assembly definition with <c>overrideReferences</c>, where every precompiled DLL has to be
        /// listed by hand. Emitting a registrar there is <c>CS0012</c> in a generated file the
        /// developer never wrote, an attribute silently breaking an assembly that was compiling
        /// fine. Measured against a live editor: eighteen of this repository's own test assemblies
        /// are that shape, and every one of them stopped compiling.
        /// </remarks>
        internal static bool Available(Compilation compilation)
        {
            return compilation.GetTypeByMetadataName(ConverterBase) != null;
        }

        internal static JsonConverterMap Build(Compilation compilation)
        {
            Dictionary<INamedTypeSymbol, INamedTypeSymbol> pairs = new Dictionary<
                INamedTypeSymbol,
                INamedTypeSymbol
            >(SymbolEqualityComparer.Default);

            if (!Available(compilation))
            {
                return new JsonConverterMap(pairs);
            }

            foreach (Pair pair in Pairs(compilation.Assembly))
            {
                if (!pairs.ContainsKey(pair.Serialized))
                {
                    pairs[pair.Serialized] = pair.Converter;
                }
            }

            foreach (
                IAssemblySymbol reference in compilation.SourceModule.ReferencedAssemblySymbols
            )
            {
                foreach (Pair pair in Pairs(reference))
                {
                    if (!pairs.ContainsKey(pair.Serialized))
                    {
                        pairs[pair.Serialized] = pair.Converter;
                    }
                }
            }

            return new JsonConverterMap(pairs);
        }

        /// <summary>
        /// Reports every pair declared by this compilation that cannot work.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <param name="report">Receives one diagnostic per unusable pair.</param>
        /// <remarks>
        /// Only this compilation's own attributes are checked, matching <see cref="MarshalMap"/>: a
        /// pair from a referenced assembly was validated when that assembly was built, and
        /// re-reporting it would fail a consumer's build over a declaration they cannot edit.
        /// </remarks>
        internal static void Validate(Compilation compilation, Action<Diagnostic> report)
        {
            if (!Available(compilation))
            {
                /*
                 * Without JSON support, declarations cannot be resolved or emitted and should not report
                 * unused-feature warnings.
                 */
                return;
            }

            HashSet<INamedTypeSymbol> seen = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );

            foreach (Pair pair in Pairs(compilation.Assembly))
            {
                Location location =
                    pair.Attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                    ?? Location.None;

                if (!seen.Add(pair.Serialized))
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.DuplicateJsonConverter,
                            location,
                            pair.Serialized.ToDisplayString(),
                            pair.Converter.ToDisplayString()
                        )
                    );
                    continue;
                }

                if (
                    pair.Serialized.Arity == 0
                    || pair.Serialized.Arity != pair.Converter.Arity
                    || !ClosureScan.Satisfies(
                        pair.Converter,
                        System.Collections.Immutable.ImmutableArray.CreateRange<ITypeSymbol>(
                            pair.Serialized.TypeParameters
                        ),
                        compilation
                    )
                )
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.JsonConverterNotUsable,
                            location,
                            pair.Serialized.ToDisplayString(),
                            pair.Converter.ToDisplayString()
                        )
                    );
                    continue;
                }

                // Using the serialized type's own parameters also detects transposed converter arguments.
                INamedTypeSymbol serialized = ClosureScan.Close(
                    pair.Serialized,
                    pair.Serialized.TypeParameters
                );
                INamedTypeSymbol converter = ClosureScan.Close(
                    pair.Converter,
                    pair.Serialized.TypeParameters
                );

                if (
                    !Converts(converter, serialized)
                    || !ClosureScan.HasPublicParameterlessConstructor(converter)
                )
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.JsonConverterNotUsable,
                            location,
                            pair.Serialized.ToDisplayString(),
                            pair.Converter.ToDisplayString()
                        )
                    );
                }
            }
        }

        /// <summary>
        /// Returns one <c>(type, converter)</c> registration pair per closure this compilation
        /// writes.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <param name="report">Receives a diagnostic per closure that cannot be named.</param>
        /// <param name="announced">Closures already reported by another scan.</param>
        /// <returns>Registration argument lists, ready to be emitted.</returns>
        internal IEnumerable<string> Registrations(
            Compilation compilation,
            Action<Diagnostic> report,
            HashSet<string> announced
        )
        {
            HashSet<string> found = new HashSet<string>();
            List<string> registrations = new List<string>();

            if (_pairs.Count == 0)
            {
                return registrations;
            }

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    INamedTypeSymbol closure = ClosureScan.Closure(model, node, out Location where);
                    if (
                        closure == null
                        || !_pairs.TryGetValue(
                            closure.OriginalDefinition,
                            out INamedTypeSymbol definition
                        )
                    )
                    {
                        continue;
                    }

                    INamedTypeSymbol converter = ClosureScan.Close(
                        definition,
                        closure.TypeArguments
                    );
                    if (
                        converter == null
                        || !ClosureScan.Satisfies(definition, closure.TypeArguments, compilation)
                    )
                    {
                        continue;
                    }

                    /*
                     * Check the user-written closure before its converter; otherwise shared unnameable
                     * arguments suppress the useful diagnostic.
                     */
                    if (
                        TypeNaming.ReportIfUnnameable(
                            closure,
                            compilation,
                            where,
                            report,
                            announced
                        ) || !TypeNaming.IsNameable(converter, compilation)
                    )
                    {
                        continue;
                    }

                    string qualified = closure.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    if (found.Add(qualified))
                    {
                        registrations.Add(
                            "typeof("
                                + qualified
                                + "), new "
                                + converter.ToDisplayString(
                                    SymbolDisplayFormat.FullyQualifiedFormat
                                )
                                + "()"
                        );
                    }
                }
            }

            return registrations;
        }

        private static bool Converts(INamedTypeSymbol converter, INamedTypeSymbol serialized)
        {
            if (converter == null || serialized == null)
            {
                return false;
            }

            for (
                INamedTypeSymbol current = converter.BaseType;
                current != null;
                current = current.BaseType
            )
            {
                if (
                    current.Arity != 1
                    || current.ConstructedFrom?.ToDisplayString() != ConverterBase + "<T>"
                )
                {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(current.TypeArguments[0], serialized))
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
                    || attribute.AttributeClass.ToDisplayString() != ConverterAttribute
                    || attribute.ConstructorArguments.Length < 2
                )
                {
                    continue;
                }

                if (
                    !(attribute.ConstructorArguments[0].Value is INamedTypeSymbol serialized)
                    || !(attribute.ConstructorArguments[1].Value is INamedTypeSymbol converter)
                )
                {
                    continue;
                }

                // Normalize unbound typeof arguments to definitions so they match source closures.
                yield return new Pair(
                    serialized.OriginalDefinition,
                    converter.OriginalDefinition,
                    attribute
                );
            }
        }

        private readonly struct Pair
        {
            internal Pair(
                INamedTypeSymbol serialized,
                INamedTypeSymbol converter,
                AttributeData attribute
            )
            {
                Serialized = serialized;
                Converter = converter;
                Attribute = attribute;
            }

            internal INamedTypeSymbol Serialized { get; }

            internal INamedTypeSymbol Converter { get; }

            internal AttributeData Attribute { get; }
        }
    }
}
