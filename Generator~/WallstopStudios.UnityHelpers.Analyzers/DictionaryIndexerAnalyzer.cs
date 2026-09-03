// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using System.Collections.Immutable;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.Diagnostics;
    using Microsoft.CodeAnalysis.Operations;

    /// <summary>
    /// Reports a READ through a dictionary's key indexer, where <c>TryGetValue</c> answers the
    /// missing key instead of throwing on it.
    /// </summary>
    /// <remarks>
    /// <b>This analyzer's diagnostic is the only <c>WUH###</c> that is off by default.</b> Every
    /// other member of the family reports a shape that is wrong wherever it appears; this one
    /// reports a shape that is correct wherever the key is known present, which is most of the
    /// places it appears. On by default it would hand a consumer a wall of findings on their first
    /// build after a package upgrade and bury the nine rules that are not judgment calls, so
    /// enabling it is a deliberate <c>&lt;Rule Id="WUH010" Action="Warning" /&gt;</c> line in a
    /// ruleset. Severity is still capped at a warning (#652).
    /// <para>
    /// A WRITE (<c>map[key] = value</c>) is add-or-update, has no <c>Try</c> form worth
    /// recommending, and is not reported. A COMPOUND assignment (<c>map[key] += 1</c>,
    /// <c>map[key]++</c>, <c>map[key] ??= value</c>) IS reported: the read half runs first and
    /// throws on a key that is absent, so the fact that the same expression also writes does not
    /// make it safe. That is the whole hazard, only spelled shorter.
    /// </para>
    /// <para>
    /// A read guarded by <c>ContainsKey</c> is reported too, and that is a choice rather than a
    /// gap. Proving the guard covers the read needs dataflow -- the key expression has to be the
    /// same one, the branch has to be the true branch, and nothing between may have removed the
    /// key -- and the shape it would exempt is a DOUBLE LOOKUP that <c>TryGetValue</c> replaces
    /// with one. So the pair is worth reporting on its own merits and no attempt is made to detect
    /// it.
    /// </para>
    /// <para>
    /// The indexer is matched by its parameter type against the key type of the dictionary
    /// interface the receiver implements, rather than by resolving the interface member, so an
    /// explicitly implemented indexer is reached through the public one a call site actually binds.
    /// The cost is that a type keyed by <c>int</c> that also carries a positional <c>this[int]</c>
    /// would be reported on the positional one; no such type ships in the BCL, and the two are
    /// indistinguishable from a signature anyway.
    /// </para>
    /// <para>
    /// <c>GroupCollection</c> is named outright, and MEASURED it has to be. It implements
    /// <c>IReadOnlyDictionary&lt;string, Group&gt;</c> on .NET Core 3.0 and later, but NOT on the
    /// netstandard2.1 surface Unity and every check project compiles against -- there,
    /// <c>IReadOnlyDictionary&lt;string, Group&gt; groups = match.Groups;</c> is CS0266. So the
    /// interface test alone reported the site #652 was opened about in a unit test on net9.0 and
    /// nowhere a consumer builds. Its string indexer is the keyed one on every framework.
    /// </para>
    /// </remarks>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DictionaryIndexerAnalyzer : DiagnosticAnalyzer
    {
        /// <inheritdoc />
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(UnityHelpersDiagnostics.DictionaryIndexerReadThrowsOnMiss);

        /// <inheritdoc />
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.RegisterCompilationStartAction(OnCompilationStart);
        }

        private static void OnCompilationStart(CompilationStartAnalysisContext context)
        {
            INamedTypeSymbol dictionary = context.Compilation.GetTypeByMetadataName(
                "System.Collections.Generic.IDictionary`2"
            );
            INamedTypeSymbol readOnlyDictionary = context.Compilation.GetTypeByMetadataName(
                "System.Collections.Generic.IReadOnlyDictionary`2"
            );
            if (dictionary == null && readOnlyDictionary == null)
            {
                return;
            }

            DictionaryKeyIndexers indexers = new DictionaryKeyIndexers(
                dictionary,
                readOnlyDictionary
            );
            context.RegisterOperationAction(
                indexers.OnPropertyReference,
                OperationKind.PropertyReference
            );
        }

        /// <summary>
        /// The two dictionary interfaces, resolved once per compilation.
        /// </summary>
        private sealed class DictionaryKeyIndexers
        {
            private readonly INamedTypeSymbol _dictionary;
            private readonly INamedTypeSymbol _readOnlyDictionary;

            internal DictionaryKeyIndexers(
                INamedTypeSymbol dictionary,
                INamedTypeSymbol readOnlyDictionary
            )
            {
                _dictionary = dictionary;
                _readOnlyDictionary = readOnlyDictionary;
            }

            internal void OnPropertyReference(OperationAnalysisContext context)
            {
                IPropertyReferenceOperation reference = (IPropertyReferenceOperation)
                    context.Operation;
                if (!IsDictionaryKeyIndexer(reference) || IsWriteOnly(reference))
                {
                    return;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        UnityHelpersDiagnostics.DictionaryIndexerReadThrowsOnMiss,
                        reference.Syntax.GetLocation(),
                        reference.Property.ContainingType.ToDisplayString(
                            SymbolDisplayFormat.MinimallyQualifiedFormat
                        )
                    )
                );
            }

            /// <summary>
            /// Whether the whole expression writes and never reads.
            /// </summary>
            /// <remarks>
            /// A compound assignment reaches here as <see cref="ICompoundAssignmentOperation"/> or
            /// <see cref="ICoalesceAssignmentOperation"/> rather than
            /// <see cref="ISimpleAssignmentOperation"/>, and neither is matched, which is how the
            /// read half of <c>map[key] += 1</c> stays reported.
            /// </remarks>
            private static bool IsWriteOnly(IPropertyReferenceOperation reference)
            {
                IOperation assigned = reference;
                IOperation parent = reference.Parent;

                // `(a, map[key]) = pair` puts the write target inside a tuple.
                while (parent is ITupleOperation)
                {
                    assigned = parent;
                    parent = parent.Parent;
                }

                if (parent is ISimpleAssignmentOperation simple)
                {
                    return ReferenceEquals(simple.Target, assigned);
                }

                return parent is IDeconstructionAssignmentOperation deconstruction
                    && ReferenceEquals(deconstruction.Target, assigned);
            }

            private bool IsDictionaryKeyIndexer(IPropertyReferenceOperation reference)
            {
                IPropertySymbol property = reference.Property;
                if (property == null || !property.IsIndexer || property.Parameters.Length != 1)
                {
                    return false;
                }

                INamedTypeSymbol containing = property.ContainingType;
                if (containing == null)
                {
                    return false;
                }

                ITypeSymbol key = property.Parameters[0].Type;
                if (IsGroupCollectionNameIndexer(containing, key))
                {
                    return true;
                }

                // A receiver typed as the interface itself carries the indexer directly.
                if (IsKeyedBy(containing, key))
                {
                    return true;
                }

                foreach (INamedTypeSymbol implemented in containing.AllInterfaces)
                {
                    if (IsKeyedBy(implemented, key))
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// <c>match.Groups["name"]</c>, which the interface test does not reach on the
            /// framework Unity compiles against.
            /// </summary>
            /// <remarks>
            /// <c>GroupCollection</c> gained <c>IReadOnlyDictionary&lt;string, Group&gt;</c> in
            /// .NET Core 3.0 and does not carry it on netstandard2.1. Its string indexer is the
            /// keyed one either way, and it is the worst member of the class: a group name the
            /// pattern never declared comes back as an unsuccessful <c>Group</c> rather than as any
            /// kind of error.
            /// </remarks>
            private static bool IsGroupCollectionNameIndexer(
                INamedTypeSymbol containing,
                ITypeSymbol key
            )
            {
                return key.SpecialType == SpecialType.System_String
                    && containing.MetadataName == "GroupCollection"
                    && containing.ContainingNamespace != null
                    && containing.ContainingNamespace.ToDisplayString()
                        == "System.Text.RegularExpressions";
            }

            private bool IsKeyedBy(INamedTypeSymbol candidate, ITypeSymbol key)
            {
                INamedTypeSymbol definition = candidate.OriginalDefinition;
                if (
                    !SymbolEqualityComparer.Default.Equals(definition, _dictionary)
                    && !SymbolEqualityComparer.Default.Equals(definition, _readOnlyDictionary)
                )
                {
                    return false;
                }

                return candidate.TypeArguments.Length == 2
                    && SymbolEqualityComparer.Default.Equals(candidate.TypeArguments[0], key);
            }
        }
    }
}
