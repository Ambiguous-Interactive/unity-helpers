// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Text;

    /// <summary>
    /// Emits a WallstopProto formatter for every <c>[WProtoContract]</c> type in a compilation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Implemented as an <see cref="ISourceGenerator"/> rather than an
    /// <c>IIncrementalGenerator</c>. Unity 2021.3 -- which this package supports and CI builds --
    /// ships Roslyn 3.9, where the incremental interface does not exist and an analyzer compiled
    /// against Microsoft.CodeAnalysis 4.x does not load at all. The v2 interface runs on every host
    /// from 3.8 upward, which is every Unity version in the matrix.
    /// </para>
    /// <para>
    /// The formatter is emitted as a type nested inside the contract, which is how generated code
    /// reaches private fields and private lifecycle hooks with no reflection at all -- the property
    /// the whole serializer exists for. That is also why a non-partial contract is an error rather
    /// than a skip.
    /// </para>
    /// </remarks>
    [Generator]
    public sealed class WProtoGenerator : ISourceGenerator
    {
        private const string AttributeNamespace =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto";
        private const string ContractAttribute = AttributeNamespace + ".WProtoContractAttribute";
        private const string MemberAttribute = AttributeNamespace + ".WProtoMemberAttribute";
        private const string IgnoreAttribute = AttributeNamespace + ".WProtoIgnoreAttribute";
        private const string IncludeAttribute = AttributeNamespace + ".WProtoIncludeAttribute";
        private const string NotSerializedAttribute =
            AttributeNamespace + ".WProtoNotSerializedAttribute";
        private const string SubtypeAttribute = AttributeNamespace + ".WProtoSubtypeAttribute";
        private const string BeforeSerialization =
            AttributeNamespace + ".WProtoBeforeSerializationAttribute";
        private const string AfterSerialization =
            AttributeNamespace + ".WProtoAfterSerializationAttribute";
        private const string BeforeDeserialization =
            AttributeNamespace + ".WProtoBeforeDeserializationAttribute";
        private const string AfterDeserialization =
            AttributeNamespace + ".WProtoAfterDeserializationAttribute";

        private const string Proto = "global::" + AttributeNamespace;

        // String names avoid loading protobuf-net into the compiler and support vendored namespace renames.
        private const string ProtobufNamespace = "ProtoBuf";
        private const string ProtobufContractAttributeName = "ProtoContractAttribute";
        private const string ProtobufMemberAttributeName = "ProtoMemberAttribute";
        private const string ProtobufContractAttribute =
            ProtobufNamespace + "." + ProtobufContractAttributeName;

        private static readonly string AttributeBaseName = typeof(Attribute).FullName;
        private static readonly string DataContractAttributeName =
            typeof(DataContractAttribute).FullName;
        private static readonly string DataMemberAttributeName =
            typeof(DataMemberAttribute).FullName;
        private const string DataMemberOrder = nameof(DataMemberAttribute.Order);

        /// <inheritdoc />
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new Receiver());
        }

        /// <inheritdoc />
        public void Execute(GeneratorExecutionContext context)
        {
            if (!(context.SyntaxReceiver is Receiver receiver))
            {
                return;
            }

            bool disableModuleInitializer = DisableModuleInitializer(context);

            List<INamedTypeSymbol> contracts = new List<INamedTypeSymbol>();
            HashSet<INamedTypeSymbol> seen = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );

            // Cache the reference search once per compilation instead of repeating it for every DataContract.
            bool? referencesProtobufNet = null;

            /*
             * Validate the manifest before resolving tagless subtypes so each corrupt entry reports once at
             * its source.
             */
            SubtypeTagManifest manifest = SubtypeTagManifest.Build(context.Compilation);
            SubtypeTagManifest.Validate(context.Compilation, context.ReportDiagnostic);
            bool editorCompilation = IsEditorCompilation(context);

            // Reuse one semantic model per tree because ordinary subclasses also enter this scan.
            Dictionary<SyntaxTree, SemanticModel> models =
                new Dictionary<SyntaxTree, SemanticModel>();

            foreach (TypeDeclarationSyntax declaration in receiver.Types)
            {
                SemanticModel model = ModelFor(context.Compilation, models, declaration.SyntaxTree);
                if (!(model.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol))
                {
                    continue;
                }

                if (!seen.Add(symbol))
                {
                    continue;
                }

                bool isContract = IsContract(symbol);
                if (HasAttribute(symbol, NotSerializedAttribute))
                {
                    /*
                     * Contradictory opt-out and contract declarations must report before traversal order can
                     * choose one.
                     */
                    string contradiction =
                        HasAttribute(symbol, ContractAttribute) ? "[WProtoContract]"
                        : HasAttribute(symbol, SubtypeAttribute) ? "[WProtoSubtype]"
                        : null;
                    if (contradiction != null)
                    {
                        context.ReportDiagnostic(
                            Diagnostic.Create(
                                WProtoDiagnostics.ContradictoryNotSerialized,
                                symbol.Locations.FirstOrDefault(),
                                symbol.Name,
                                "carries " + contradiction
                            )
                        );
                    }
                }

                if (isContract)
                {
                    contracts.Add(symbol);
                    continue;
                }

                ReportUndeclaredSubclass(context, symbol);

                if (
                    TryFindUnportedProtobufContract(
                        context.Compilation,
                        symbol,
                        ref referencesProtobufNet,
                        out AttributeData protobufContract,
                        out string matchedBecause
                    )
                )
                {
                    Location location =
                        protobufContract.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                        ?? symbol.Locations.FirstOrDefault();
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            WProtoDiagnostics.UnportedProtobufContract,
                            location,
                            symbol.ToDisplayString(),
                            matchedBecause
                        )
                    );
                }

                ReportOrphanedHooks(context, symbol);

                SubtypeMap.Validate(
                    context.ReportDiagnostic,
                    symbol,
                    true,
                    manifest,
                    editorCompilation
                );
            }

            foreach (TypeDeclarationSyntax derived in receiver.Derived)
            {
                SemanticModel model = ModelFor(context.Compilation, models, derived.SyntaxTree);
                if (!(model.GetDeclaredSymbol(derived) is INamedTypeSymbol symbol))
                {
                    continue;
                }

                // Another partial declaration may already have registered this contract.
                if (!seen.Add(symbol))
                {
                    continue;
                }

                if (IsContract(symbol))
                {
                    contracts.Add(symbol);
                    continue;
                }

                ReportUndeclaredSubclass(context, symbol);
            }

            foreach (EnumDeclarationSyntax enumeration in receiver.Enums)
            {
                SemanticModel model = ModelFor(context.Compilation, models, enumeration.SyntaxTree);
                if (
                    model.GetDeclaredSymbol(enumeration) is INamedTypeSymbol enumSymbol
                    && seen.Add(enumSymbol)
                )
                {
                    ReportReservedEnumMembers(context, enumSymbol);
                }
            }

            // Collect subtype declarations before emitting bases, regardless of source order.
            SubtypeMap subtypes = SubtypeMap.Build(contracts, manifest);

            SurrogateMap surrogates = SurrogateMap.Build(context.Compilation);
            SurrogateMap.Validate(context.Compilation, context.ReportDiagnostic);

            MarshalMap marshals = MarshalMap.Build(context.Compilation);
            MarshalMap.Validate(context.Compilation, context.ReportDiagnostic);

            JsonConverterMap jsonConverters = JsonConverterMap.Build(context.Compilation);
            JsonConverterMap.Validate(context.Compilation, context.ReportDiagnostic);

            DeclaredRootMap.Validate(context.Compilation, context.ReportDiagnostic);

            // All scans share diagnostics so one unnameable closure reports only once.
            HashSet<string> announced = new HashSet<string>();

            List<string> registrations = new List<string>();
            HashSet<INamedTypeSymbol> emittedContracts = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            HashSet<INamedTypeSymbol> enumClosures = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            /*
             * Emit deepest-first and filter refused includes so no published formatter names an unpublished
             * formatter.
             */
            List<Emission> emissions = new List<Emission>();
            HashSet<INamedTypeSymbol> refused = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            foreach (INamedTypeSymbol contract in DeepestFirst(contracts))
            {
                string source = Emit(
                    context,
                    contract,
                    surrogates,
                    subtypes,
                    refused,
                    out string registration
                );
                if (source == null)
                {
                    refused.Add(contract);
                    continue;
                }

                emissions.Add(new Emission(contract, source, registration));
            }

            foreach (Emission emission in emissions)
            {
                /*
                 * CanServe names every ancestor formatter; withhold descendants of a refused ancestor even if
                 * inherited nested types let the reference compile.
                 */
                if (HasRefusedAncestor(emission.Contract, refused))
                {
                    continue;
                }

                context.AddSource(
                    FileNameFor(emission.Contract),
                    SourceText.From(emission.Source, Encoding.UTF8)
                );
                emittedContracts.Add(emission.Contract);
                if (emission.Registration != null)
                {
                    registrations.Add(emission.Registration);
                }
                else
                {
                    string entryPoint =
                        RootContract(emission.Contract) == null
                            ? ".WProtoFormatter.Instance"
                            : ".WProtoRootFormatter.Instance";
                    foreach (
                        string closed in ClosedConstructions(
                            context.Compilation,
                            emission.Contract,
                            context.ReportDiagnostic,
                            announced
                        )
                    )
                    {
                        registrations.Add(closed + entryPoint);
                    }
                }
            }

            foreach (
                string surrogateRegistration in SurrogateClosures(
                    context.Compilation,
                    surrogates,
                    emittedContracts,
                    enumClosures,
                    context.ReportDiagnostic,
                    announced
                )
            )
            {
                if (!registrations.Contains(surrogateRegistration))
                {
                    registrations.Add(surrogateRegistration);
                }
            }

            foreach (
                string foreignRegistration in ForeignClosures(
                    context.Compilation,
                    context.ReportDiagnostic,
                    announced
                )
            )
            {
                if (!registrations.Contains(foreignRegistration))
                {
                    registrations.Add(foreignRegistration);
                }
            }

            List<string> rootMarshals = new List<string>(
                marshals.Registrations(context.Compilation, context.ReportDiagnostic, announced)
            );

            List<string> declaredRoots = new List<string>(
                DeclaredRootMap.Registrations(
                    context.Compilation,
                    context.ReportDiagnostic,
                    announced
                )
            );

            if (0 < registrations.Count || 0 < rootMarshals.Count || 0 < declaredRoots.Count)
            {
                context.AddSource(
                    "WProtoGeneratedRegistrar.g.cs",
                    SourceText.From(
                        EmitRegistrar(
                            context,
                            registrations,
                            rootMarshals,
                            declaredRoots,
                            enumClosures,
                            disableModuleInitializer
                        ),
                        Encoding.UTF8
                    )
                );
            }

            // Separate registrars keep JSON and protobuf opt-outs independent.
            List<string> jsonRegistrations = new List<string>(
                jsonConverters.Registrations(
                    context.Compilation,
                    context.ReportDiagnostic,
                    announced
                )
            );

            if (0 < jsonRegistrations.Count)
            {
                context.AddSource(
                    "WJsonGeneratedRegistrar.g.cs",
                    SourceText.From(
                        EmitJsonRegistrar(jsonRegistrations, disableModuleInitializer),
                        Encoding.UTF8
                    )
                );
            }
        }

        /// <summary>
        /// Whether this compilation is one the editor performs, rather than one a player ships.
        /// </summary>
        /// <param name="context">The generator's execution context.</param>
        /// <returns><c>true</c> when <c>UNITY_EDITOR</c> is defined for the compilation.</returns>
        /// <remarks>
        /// <para>
        /// Read from the preprocessor symbols rather than inferred from an assembly name, because
        /// that is the same set <c>#if UNITY_EDITOR</c> is evaluated against and Unity decides it
        /// per assembly rather than per project.
        /// </para>
        /// <para>
        /// Measured in editor 6000.4.6f1 through
        /// <c>CompilationPipeline.GetAssemblies(...).defines</c>: <c>UNITY_EDITOR</c> is present for
        /// every <c>AssembliesType.Editor</c> assembly -- runtime asmdefs, editor asmdefs,
        /// <c>Assembly-CSharp</c> and the test assemblies alike -- and absent for every
        /// <c>AssembliesType.Player</c> and <c>PlayerWithoutTestAssemblies</c> assembly.
        /// </para>
        /// </remarks>
        private static bool IsEditorCompilation(GeneratorExecutionContext context)
        {
            ParseOptions options = context.ParseOptions;
            if (options == null)
            {
                // Without editor symbols, missing tags must use the player-safe refusal.
                return false;
            }

            foreach (string symbol in options.PreprocessorSymbolNames)
            {
                if (string.Equals(symbol, "UNITY_EDITOR", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DisableModuleInitializer(GeneratorExecutionContext context)
        {
            return context.AnalyzerConfigOptions.GlobalOptions.TryGetValue(
                    "build_property.WProtoDisableModuleInitializer",
                    out string value
                ) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Registers the generic contract closure substituted for each closed surrogated type the
        /// compilation names.
        /// </summary>
        /// <remarks>
        /// An open surrogate pair creates a closure that does not appear in consumer source. The
        /// ordinary contract scan therefore cannot discover it: a member names
        /// <c>ValueTuple&lt;int, string&gt;</c>, while its generated formatter asks the provider for
        /// <c>SerializableValueTuple&lt;int, string&gt;</c>. This scan follows the same substitution the
        /// member generator performs and makes that synthesized closure available without runtime
        /// reflection.
        /// </remarks>
        private static IEnumerable<string> SurrogateClosures(
            Compilation compilation,
            SurrogateMap surrogates,
            HashSet<INamedTypeSymbol> emittedContracts,
            HashSet<INamedTypeSymbol> enumClosures,
            Action<Diagnostic> report,
            HashSet<string> announced
        )
        {
            HashSet<string> found = new HashSet<string>();
            HashSet<ITypeSymbol> visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            HashSet<ITypeSymbol> visitedWithDependencies = new HashSet<ITypeSymbol>(
                SymbolEqualityComparer.Default
            );

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    INamedTypeSymbol named = ConstructedTypeAt(model, node, out Location where);
                    if (named == null || named.IsUnboundGenericType || IsOpen(named))
                    {
                        continue;
                    }

                    CollectSurrogateClosures(
                        named,
                        where,
                        compilation,
                        surrogates,
                        emittedContracts,
                        enumClosures,
                        report,
                        announced,
                        visited,
                        visitedWithDependencies,
                        false,
                        found
                    );
                }
            }

            return found;
        }

        private static void CollectSurrogateClosures(
            ITypeSymbol type,
            Location where,
            Compilation compilation,
            SurrogateMap surrogates,
            HashSet<INamedTypeSymbol> emittedContracts,
            HashSet<INamedTypeSymbol> enumClosures,
            Action<Diagnostic> report,
            HashSet<string> announced,
            HashSet<ITypeSymbol> visited,
            HashSet<ITypeSymbol> visitedWithDependencies,
            bool followDependencies,
            HashSet<string> found
        )
        {
            if (type is IArrayTypeSymbol array)
            {
                CollectSurrogateClosures(
                    array.ElementType,
                    where,
                    compilation,
                    surrogates,
                    emittedContracts,
                    enumClosures,
                    report,
                    announced,
                    visited,
                    visitedWithDependencies,
                    followDependencies,
                    found
                );
                return;
            }

            if (!(type is INamedTypeSymbol named) || IsOpen(named))
            {
                return;
            }

            HashSet<ITypeSymbol> activeVisited = followDependencies
                ? visitedWithDependencies
                : visited;
            if (!activeVisited.Add(named))
            {
                return;
            }

            if (named.IsTupleType)
            {
                named = named.TupleUnderlyingType ?? named;
            }

            if (named.TypeKind == TypeKind.Enum)
            {
                if (
                    followDependencies
                    && !TypeNaming.ReportIfUnnameable(named, compilation, where, report, announced)
                )
                {
                    enumClosures.Add(named);
                }
                return;
            }

            if (followDependencies)
            {
                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    CollectSurrogateClosures(
                        argument,
                        where,
                        compilation,
                        surrogates,
                        emittedContracts,
                        enumClosures,
                        report,
                        announced,
                        visited,
                        visitedWithDependencies,
                        true,
                        found
                    );
                }
            }

            INamedTypeSymbol surrogate = surrogates.For(named);
            if (surrogate != null && !surrogate.IsUnboundGenericType && !IsOpen(surrogate))
            {
                INamedTypeSymbol definition = surrogate.ConstructedFrom;
                bool local = SymbolEqualityComparer.Default.Equals(
                    definition.ContainingAssembly,
                    compilation.Assembly
                );
                string formatter = local
                    ? RootContract(definition) == null
                        ? "WProtoFormatter"
                        : "WProtoRootFormatter"
                    : FormatterNameFor(definition);
                if (
                    formatter != null
                    && (!local || emittedContracts.Contains(definition))
                    && !TypeNaming.ReportIfUnnameable(
                        surrogate,
                        compilation,
                        where,
                        report,
                        announced
                    )
                )
                {
                    string qualified = surrogate.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    found.Add(qualified + "." + formatter + ".Instance");
                }

                CollectSurrogateClosures(
                    surrogate,
                    where,
                    compilation,
                    surrogates,
                    emittedContracts,
                    enumClosures,
                    report,
                    announced,
                    visited,
                    visitedWithDependencies,
                    true,
                    found
                );
            }

            if (!HasAttribute(named.OriginalDefinition, ContractAttribute))
            {
                return;
            }

            if (followDependencies && named.IsGenericType)
            {
                INamedTypeSymbol definition = named.ConstructedFrom;
                bool local = SymbolEqualityComparer.Default.Equals(
                    definition.ContainingAssembly,
                    compilation.Assembly
                );
                string formatter = local
                    ? RootContract(definition) == null
                        ? "WProtoFormatter"
                        : "WProtoRootFormatter"
                    : FormatterNameFor(definition);
                if (
                    formatter != null
                    && (!local || emittedContracts.Contains(definition))
                    && !TypeNaming.ReportIfUnnameable(named, compilation, where, report, announced)
                )
                {
                    found.Add(
                        named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                            + "."
                            + formatter
                            + ".Instance"
                    );
                }
            }

            foreach (ISymbol member in named.GetMembers())
            {
                if (!HasAttribute(member, MemberAttribute) || HasAttribute(member, IgnoreAttribute))
                {
                    continue;
                }

                ITypeSymbol memberType = MemberType(member);
                if (memberType == null)
                {
                    continue;
                }

                CollectSurrogateClosures(
                    memberType,
                    where,
                    compilation,
                    surrogates,
                    emittedContracts,
                    enumClosures,
                    report,
                    announced,
                    visited,
                    visitedWithDependencies,
                    true,
                    found
                );
            }

            CollectSurrogateClosures(
                named.BaseType,
                where,
                compilation,
                surrogates,
                emittedContracts,
                enumClosures,
                report,
                announced,
                visited,
                visitedWithDependencies,
                true,
                found
            );
        }

        /// <summary>
        /// Registers closures of generic contracts that another assembly declares.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <returns>One registration expression per closure found here and not declared here.</returns>
        /// <remarks>
        /// <para>
        /// This is the consumer story, and without it the story is only half true. A generic
        /// contract's formatter is emitted once, open, into the assembly that declares it; only a
        /// <b>closed</b> construction can be registered, and the assembly that declares the contract
        /// usually never mentions the closure a consumer cares about. <c>Deque&lt;TheirStruct&gt;</c>
        /// cannot appear in this package's own sources by construction -- the struct does not exist
        /// yet -- so nothing registered it and it threw on its first serialization.
        /// </para>
        /// <para>
        /// The scan runs from the closures rather than from the references. Walking every namespace
        /// of every referenced assembly looking for annotations would cost more than the whole
        /// generator; asking "is the type this construction closes a contract" costs one attribute
        /// lookup per constructed generic already in the syntax.
        /// </para>
        /// <para>
        /// Two guards keep this from breaking a build it was meant to help. The formatter has to be
        /// <b>accessible</b> from here, since an internal contract in a reference without
        /// <c>InternalsVisibleTo</c> cannot be named; and it has to <b>exist</b>, because a
        /// referenced assembly compiled without this analyzer has the attribute and no formatter, and
        /// naming one that is not there would fail the consumer's build rather than the absent
        /// registration it is replacing.
        /// </para>
        /// </remarks>
        private static IEnumerable<string> ForeignClosures(
            Compilation compilation,
            Action<Diagnostic> report,
            HashSet<string> announced
        )
        {
            HashSet<string> found = new HashSet<string>();
            List<string> registrations = new List<string>();

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    INamedTypeSymbol named = ConstructedTypeAt(model, node, out Location where);
                    if (
                        named == null
                        || !named.IsGenericType
                        || named.IsUnboundGenericType
                        || IsOpen(named)
                    )
                    {
                        continue;
                    }

                    INamedTypeSymbol definition = named.ConstructedFrom;
                    if (
                        definition == null
                        || SymbolEqualityComparer.Default.Equals(
                            definition.ContainingAssembly,
                            compilation.Assembly
                        )
                        || !HasAttribute(definition, ContractAttribute)
                    )
                    {
                        continue;
                    }

                    string entryPoint = FormatterNameFor(definition);
                    if (
                        entryPoint == null
                        || TypeNaming.ReportIfUnnameable(
                            named,
                            compilation,
                            where,
                            report,
                            announced
                        )
                    )
                    {
                        continue;
                    }

                    string qualified = named.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat
                    );
                    if (found.Add(qualified))
                    {
                        registrations.Add(qualified + "." + entryPoint + ".Instance");
                    }
                }
            }

            return registrations;
        }

        /// <summary>
        /// Names the nested formatter a referenced contract actually carries, or <c>null</c>.
        /// </summary>
        /// <param name="definition">The generic contract's unbound definition.</param>
        /// <returns>The nested type name to register, or <c>null</c> when there is none.</returns>
        private static string FormatterNameFor(INamedTypeSymbol definition)
        {
            foreach (string candidate in new[] { "WProtoRootFormatter", "WProtoFormatter" })
            {
                foreach (INamedTypeSymbol nested in definition.GetTypeMembers(candidate))
                {
                    if (nested.DeclaredAccessibility == Accessibility.Public)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the semantic model for a tree, building each one at most once.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <param name="models">The per-tree cache.</param>
        /// <param name="tree">The tree a declaration was found in.</param>
        /// <returns>The model to ask for declared symbols.</returns>
        private static SemanticModel ModelFor(
            Compilation compilation,
            Dictionary<SyntaxTree, SemanticModel> models,
            SyntaxTree tree
        )
        {
            if (!models.TryGetValue(tree, out SemanticModel model))
            {
                model = compilation.GetSemanticModel(tree);
                models[tree] = model;
            }

            return model;
        }

        /// <summary>
        /// Whether a formatter should be generated for a type.
        /// </summary>
        /// <param name="symbol">The type to classify.</param>
        /// <returns><c>true</c> when it is a contract, declared or inherited.</returns>
        /// <remarks>
        /// One line, delegating to <see cref="SubtypeMap.IsSerializedContract"/>, because the same
        /// question decides whether an include is synthesized. Repeating the conditions here drifted
        /// once: a walk that stepped past a generic base demanded <c>partial</c> on every
        /// <c>SerializableDictionary.Cache&lt;T&gt;</c> box.
        /// </remarks>
        private static bool IsContract(INamedTypeSymbol symbol)
        {
            return SubtypeMap.IsSerializedContract(symbol);
        }

        /// <summary>
        /// The nearest ancestor that carries <c>[WProtoContract]</c>, or <c>null</c>.
        /// </summary>
        /// <param name="symbol">The type to walk up from.</param>
        /// <returns>The declared contract this type inherits its serialization from.</returns>
        private static INamedTypeSymbol DeclaredContractAncestor(INamedTypeSymbol symbol)
        {
            for (INamedTypeSymbol current = symbol; current != null; current = current.BaseType)
            {
                if (HasAttribute(current, ContractAttribute))
                {
                    return current;
                }
            }

            return null;
        }

        /// <summary>
        /// Refuses a subclass whose contract base is in another assembly.
        /// </summary>
        /// <param name="context">The generation context to report through.</param>
        /// <param name="symbol">A type this compilation is not generating a formatter for.</param>
        /// <remarks>
        /// <para>
        /// Deriving from a contract is now the declaration, so the only subclass left to refuse is
        /// the one no per-assembly generator can honour: the base's dispatch chain was emitted when
        /// the base's OWN assembly compiled, so a subclass declared afterwards could never have
        /// reached it. Accepting it would compile and then throw on the first save.
        /// </para>
        /// <para>
        /// A generic base is excluded for the reason <c>WPROTO040</c> gives: one field number cannot
        /// identify a type that is really as many types as it has closures.
        /// <c>SerializableDictionary.Cache&lt;T&gt;</c> is that shape and every consumer subclasses
        /// one, so reporting there would be noise about a hazard that does not exist.
        /// </para>
        /// </remarks>
        private static void ReportUndeclaredSubclass(
            GeneratorExecutionContext context,
            INamedTypeSymbol symbol
        )
        {
            if (HasAttribute(symbol, NotSerializedAttribute))
            {
                return;
            }

            INamedTypeSymbol declared = DeclaredContractAncestor(symbol);
            if (
                declared == null
                || declared.OriginalDefinition.TypeParameters.Length != 0
                || SymbolEqualityComparer.Default.Equals(
                    declared.ContainingAssembly,
                    symbol.ContainingAssembly
                )
            )
            {
                return;
            }

            // Explicit foreign-base declarations already report WPROTO040 at their source.
            if (SubtypeMap.Declares(symbol))
            {
                return;
            }

            context.ReportDiagnostic(
                Diagnostic.Create(
                    WProtoDiagnostics.UndeclaredSubclass,
                    symbol.Locations.FirstOrDefault(),
                    symbol.Name,
                    declared.Name,
                    declared.ContainingAssembly == null ? "?" : declared.ContainingAssembly.Name
                )
            );
        }

        /// <summary>
        /// Refuses an enum member that takes a value or a name the enum reserves.
        /// </summary>
        /// <param name="context">The generation context to report through.</param>
        /// <param name="enumSymbol">An enum declaration carrying at least one attribute.</param>
        /// <remarks>
        /// <para>
        /// Every member is checked, not only some annotated subset: an enum has no per-member
        /// declaration to opt one in, and its underlying value goes on the wire whichever member
        /// carries it (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/609">#609</see>).
        /// </para>
        /// <para>
        /// A reservation is written as an <c>int</c>, so a member whose value does not fit one
        /// cannot collide with it and is skipped rather than converted -- a <c>ulong</c> enum may
        /// legally hold a value above <c>long.MaxValue</c>, and forcing it through a signed
        /// conversion would throw inside the compiler.
        /// </para>
        /// </remarks>
        private static void ReportReservedEnumMembers(
            GeneratorExecutionContext context,
            INamedTypeSymbol enumSymbol
        )
        {
            if (enumSymbol.TypeKind != TypeKind.Enum)
            {
                return;
            }

            ReservedMap reserved = ReservedMap.Build(enumSymbol);
            if (reserved.IsEmpty)
            {
                return;
            }

            foreach (ISymbol member in enumSymbol.GetMembers())
            {
                if (!(member is IFieldSymbol field) || !field.HasConstantValue)
                {
                    continue;
                }

                bool reservedName = reserved.ReservesName(field.Name);
                bool reservedValue =
                    TryAsInt32(field.ConstantValue, out int value)
                    && reserved.ReservesNumber(value);
                if (!reservedName && !reservedValue)
                {
                    continue;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.ReservedEnumValue,
                        member.Locations.FirstOrDefault(),
                        enumSymbol.Name,
                        field.Name,
                        reservedValue
                            ? reservedName
                                ? "the value " + value + " and the name '" + field.Name + "'"
                                : "the value " + value
                            : "the name '" + field.Name + "'"
                    )
                );
            }
        }

        /// <summary>
        /// Narrows an enum member's constant to the <c>int</c> a reservation is written as.
        /// </summary>
        /// <param name="constant">The member's constant value, in its underlying type.</param>
        /// <param name="value">The narrowed value; meaningful only when this returns true.</param>
        /// <returns><c>true</c> when the constant is exactly representable as an <c>int</c>.</returns>
        /// <remarks>
        /// Widened once and narrowed once, with the <c>out</c> written on the line before the exit.
        /// A <c>value = 0</c> at the top would satisfy definite assignment forever after, so a later
        /// case that forgot the real value would return zero and compile clean -- and zero is a
        /// legal enum value, so it would collide with a reservation of 0 rather than being ignored.
        /// </remarks>
        private static bool TryAsInt32(object constant, out int value)
        {
            long? widened = AsInt64(constant);
            bool fits =
                widened.HasValue && int.MinValue <= widened.Value && widened.Value <= int.MaxValue;
            value = fits ? (int)widened.Value : 0;
            return fits;
        }

        /// <summary>
        /// Widens an enum member's constant to <c>long</c>, or reports that it does not fit one.
        /// </summary>
        /// <param name="constant">The member's constant value, in its underlying type.</param>
        /// <returns>The value, or <c>null</c> when it is not an integral constant that fits.</returns>
        /// <remarks>
        /// A <c>ulong</c> enum may legally hold a value above <c>long.MaxValue</c>, which no
        /// reservation can name, so it answers <c>null</c> rather than being forced through a signed
        /// conversion that throws inside the compiler.
        /// </remarks>
        private static long? AsInt64(object constant)
        {
            switch (constant)
            {
                case sbyte narrow:
                    return narrow;
                case byte narrow:
                    return narrow;
                case short narrow:
                    return narrow;
                case ushort narrow:
                    return narrow;
                case int narrow:
                    return narrow;
                case uint narrow:
                    return narrow;
                case long wide:
                    return wide;
                case ulong wide:
                    return wide <= long.MaxValue ? (long?)wide : null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// Advises a type that inherits its contract and declares wire members of its own.
        /// </summary>
        /// <param name="context">The generation context to report through.</param>
        /// <param name="contract">A type a formatter is being generated for.</param>
        /// <remarks>
        /// Reported before the refusals below rather than after, so an author who is going to add
        /// the attribute anyway sees the advice on the same build as anything else this type needs.
        /// </remarks>
        private static void ReportInheritedContract(
            GeneratorExecutionContext context,
            INamedTypeSymbol contract
        )
        {
            if (HasAttribute(contract, ContractAttribute))
            {
                return;
            }

            INamedTypeSymbol declared = DeclaredContractAncestor(contract);
            if (declared == null)
            {
                return;
            }

            foreach (ISymbol member in contract.GetMembers())
            {
                if (FindAttribute(member, MemberAttribute) == null)
                {
                    continue;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.InheritedContractNotDeclared,
                        contract.Locations.FirstOrDefault(),
                        contract.Name,
                        member.Name,
                        declared.Name
                    )
                );
                return;
            }
        }

        private static void ReportOrphanedHooks(
            GeneratorExecutionContext context,
            INamedTypeSymbol symbol
        )
        {
            foreach (ISymbol member in symbol.GetMembers())
            {
                if (!(member is IMethodSymbol method))
                {
                    continue;
                }

                if (
                    !HasAttribute(method, BeforeSerialization)
                    && !HasAttribute(method, AfterSerialization)
                    && !HasAttribute(method, BeforeDeserialization)
                    && !HasAttribute(method, AfterDeserialization)
                )
                {
                    continue;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.HookWithoutContract,
                        method.Locations.FirstOrDefault(),
                        symbol.Name,
                        method.Name
                    )
                );
            }
        }

        private static string Emit(
            GeneratorExecutionContext context,
            INamedTypeSymbol contract,
            SurrogateMap surrogates,
            SubtypeMap subtypes,
            HashSet<INamedTypeSymbol> refused,
            out string registration
        )
        {
            registration = null;

            // Report refused subtype declarations first so later checks do not report their consequences.
            if (
                !SubtypeMap.Validate(
                    context.ReportDiagnostic,
                    contract,
                    false,
                    subtypes.Manifest,
                    IsEditorCompilation(context)
                )
            )
            {
                return null;
            }

            if (
                !SubtypeMap.ValidateImplicit(
                    context.ReportDiagnostic,
                    contract,
                    subtypes.Manifest,
                    IsEditorCompilation(context)
                )
            )
            {
                return null;
            }

            ReportInheritedContract(context, contract);

            if (!IsPartialEverywhere(contract))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.ContractMustBePartial,
                        contract.Locations.FirstOrDefault(),
                        contract.Name
                    )
                );
                return null;
            }

            // Nested types in open generic owners have no independently discoverable closure to register.
            if (contract.ContainingType != null && IsGenericAnywhere(contract.ContainingType))
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.GenericContract,
                        contract.Locations.FirstOrDefault(),
                        contract.Name
                    )
                );
                return null;
            }

            NestedCollections nested = new NestedCollections(contract.Name, surrogates);
            List<Member> members = CollectMembers(context, contract, surrogates, nested);
            if (members == null)
            {
                return null;
            }

            List<Include> includes = CollectIncludes(context, contract, members, subtypes);
            if (includes == null)
            {
                return null;
            }

            if (contract.IsAbstract && includes.Count == 0)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.AbstractWithoutIncludes,
                        contract.Locations.FirstOrDefault(),
                        contract.Name
                    )
                );
                return null;
            }

            /*
             * Includes can replace the instance, while readonly members require construction after decoding;
             * both need deferred locals.
             */
            bool constructAtEnd = false;
            foreach (Member member in members)
            {
                constructAtEnd |= member.RequiresConstruction;
            }

            if (constructAtEnd && 0 < includes.Count)
            {
                /*
                 * Include dispatch and immutable construction both control instance creation and cannot be
                 * combined here.
                 */
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.ImmutableWithIncludes,
                        contract.Locations.FirstOrDefault(),
                        contract.Name
                    )
                );
                return null;
            }

            /*
             * Validate declared includes before filtering refused formatters to avoid misleading secondary
             * diagnostics.
             */
            int declaredIncludeCount = includes.Count;
            includes.RemoveAll(include => refused.Contains(include.SubType));

            /*
             * An abstract contract with no remaining subtype cannot emit a read path that constructs an
             * instance.
             */
            if (contract.IsAbstract && includes.Count == 0 && 0 < declaredIncludeCount)
            {
                return null;
            }

            /*
             * Immutable reads preserve constructor seeds unless SkipConstructor requests uninitialized
             * values.
             */
            bool seedsFromInstance =
                constructAtEnd
                && !Shape.SkipsConstructor(contract)
                && CanConstructParameterlessly(contract)
                && ConstructionCouldSeedAMember(contract);

            foreach (Member member in members)
            {
                member.Deferred = constructAtEnd || 0 < includes.Count;
                member.ConstructAtEnd = constructAtEnd;
                member.SeedsFromInstance = seedsFromInstance;
            }

            Hooks hooks = CollectHooks(context, contract);
            if (hooks == null)
            {
                return null;
            }

            if (contract.IsValueType && hooks.Any)
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.HookOnValueType,
                        contract.Locations.FirstOrDefault(),
                        contract.Name
                    )
                );
                return null;
            }

            /*
             * SkipConstructor controls every seeding decision because uninitialized allocation runs no field
             * initializers.
             */
            bool declaredSkipConstructor =
                Shape.SkipsConstructor(contract)
                && !contract.IsValueType
                && !contract.IsAbstract
                && !constructAtEnd;

            /*
             * Emitting any constructor removes the implicit parameterless constructor; keep this separate
             * from SkipConstructor seeding.
             */
            bool skipConstructor = declaredSkipConstructor && DeclaresAConstructor(contract);

            foreach (Member member in members)
            {
                member.SkipConstructor = declaredSkipConstructor;
            }

            ReportInitializersSkipConstructorDiscards(context, contract);

            /*
             * Immutable and SkipConstructor paths emit their own construction and do not require an
             * author-provided parameterless constructor.
             */
            if (
                !contract.IsValueType
                && !contract.IsAbstract
                && !constructAtEnd
                && !skipConstructor
                && !HasAccessibleParameterlessConstructor(contract)
            )
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.NoParameterlessConstructor,
                        contract.Locations.FirstOrDefault(),
                        contract.Name
                    )
                );
                return null;
            }

            members.Sort((left, right) => left.Tag.CompareTo(right.Tag));

            string qualified = contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            /*
             * Subtype entry points need the whole root wire shape; own-member formatters would misread as
             * base fields.
             */
            INamedTypeSymbol root = RootContract(contract);

            string entryPoint =
                root == null ? ".WProtoFormatter.Instance" : ".WProtoRootFormatter.Instance";

            // Open generics register only their source-visible closed constructions.
            registration = IsGenericAnywhere(contract) ? null : qualified + entryPoint;

            Writer writer = new Writer();
            writer.Line("// <auto-generated />");
            writer.Line("#pragma warning disable");

            List<INamedTypeSymbol> nesting = new List<INamedTypeSymbol>();
            for (
                INamedTypeSymbol container = contract;
                container != null;
                container = container.ContainingType
            )
            {
                nesting.Insert(0, container);
            }

            bool hasNamespace = !contract.ContainingNamespace.IsGlobalNamespace;
            if (hasNamespace)
            {
                writer.Line(
                    "namespace " + contract.ContainingNamespace.ToDisplayString() + Writer.Open
                );
                writer.Indent();
            }

            foreach (INamedTypeSymbol container in nesting)
            {
                writer.Line(
                    "partial "
                        + KeywordFor(container)
                        + " "
                        + container.Name
                        + TypeParameterList(container)
                        + Writer.Open
                );
                writer.Indent();
            }

            if (constructAtEnd)
            {
                EmitConstructor(writer, contract, members);
                writer.Blank();
            }
            else if (skipConstructor)
            {
                EmitSkippingConstructor(writer, contract);
                writer.Blank();
            }

            if (root != null)
            {
                EmitRootFormatter(writer, contract, qualified, root);
                writer.Blank();
            }

            EmitFormatter(
                writer,
                contract,
                qualified,
                members,
                includes,
                hooks,
                constructAtEnd,
                seedsFromInstance,
                skipConstructor,
                EncodedTypeParameters(contract),
                nested
            );

            foreach (INamedTypeSymbol unused in nesting)
            {
                writer.Outdent();
                writer.Line("}");
            }

            if (hasNamespace)
            {
                writer.Outdent();
                writer.Line("}");
            }

            return writer.ToString();
        }

        /// <summary>
        /// Names the contract's own type parameters that end up as encoded values.
        /// </summary>
        /// <param name="contract">The contract being emitted.</param>
        /// <returns>Each parameter's name, once, in declaration order.</returns>
        /// <remarks>
        /// A member typed as one of these is encoded through <c>WProtoGeneric&lt;T&gt;</c>, which
        /// resolves at the closure rather than here. The dependency scan registers closed surrogate
        /// contracts and concrete enum scalar formatters for every nameable closure it can derive;
        /// the conditional formatter still has to decline an unsupported or unnameable argument.
        /// </remarks>
        private static List<string> EncodedTypeParameters(INamedTypeSymbol contract)
        {
            List<string> found = new List<string>();
            if (!contract.IsGenericType)
            {
                return found;
            }

            HashSet<string> seen = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (ISymbol member in contract.GetMembers())
            {
                if (!HasAttribute(member, MemberAttribute))
                {
                    continue;
                }

                ITypeSymbol type = MemberType(member);
                if (type == null)
                {
                    continue;
                }

                foreach (ITypeParameterSymbol parameter in TypeParameters(type))
                {
                    if (
                        SymbolEqualityComparer.Default.Equals(
                            parameter.ContainingType?.OriginalDefinition,
                            contract.OriginalDefinition
                        ) && seen.Add(parameter.Name)
                    )
                    {
                        found.Add(parameter.Name);
                    }
                }
            }

            return found;
        }

        private static ITypeSymbol MemberType(ISymbol member)
        {
            switch (member)
            {
                case IFieldSymbol field:
                    return field.Type;
                case IPropertySymbol property:
                    return property.Type;
                default:
                    return null;
            }
        }

        private static IEnumerable<ITypeParameterSymbol> TypeParameters(ITypeSymbol type)
        {
            switch (type)
            {
                case ITypeParameterSymbol parameter:
                    yield return parameter;
                    break;
                case IArrayTypeSymbol array:
                    foreach (ITypeParameterSymbol nested in TypeParameters(array.ElementType))
                    {
                        yield return nested;
                    }

                    break;
                case INamedTypeSymbol named:
                    foreach (ITypeSymbol argument in named.TypeArguments)
                    {
                        foreach (ITypeParameterSymbol nested in TypeParameters(argument))
                        {
                            yield return nested;
                        }
                    }

                    break;
            }
        }

        /// <summary>
        /// Emits the answer to "can this formatter encode the closure it was registered for".
        /// </summary>
        /// <remarks>
        /// Asked by <c>WProtoFacade</c> before any hook runs and before a byte is written, so an
        /// element with no formatter is reported as "not mine" and falls back to protobuf-net --
        /// rather than throwing from inside <c>Measure</c>, which is where the missing registration
        /// used to surface, on real data, from a shipped player.
        /// </remarks>
        private static void EmitCanServe(Writer writer, List<string> encodedTypeParameters)
        {
            if (encodedTypeParameters.Count == 0)
            {
                return;
            }

            writer.Line("/// <inheritdoc />");
            writer.Line("public bool CanServe()" + Writer.Open);
            writer.Indent();

            StringBuilder condition = new StringBuilder();
            for (int index = 0; index < encodedTypeParameters.Count; index++)
            {
                if (0 < index)
                {
                    condition.Append(" && ");
                }

                condition
                    .Append(Proto)
                    .Append(".WProtoGeneric<")
                    .Append(encodedTypeParameters[index])
                    .Append(">.CanEncode");
            }

            writer.Line("return " + condition + ";");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
        }

        private static void EmitFormatter(
            Writer writer,
            INamedTypeSymbol contract,
            string qualified,
            List<Member> members,
            List<Include> includes,
            Hooks hooks,
            bool constructAtEnd,
            bool seedsFromInstance,
            bool skipConstructor,
            List<string> encodedTypeParameters,
            NestedCollections nested
        )
        {
            /*
             * Only stable existing instances support merging; SkipConstructor governs creation and does not
             * invalidate caller-provided seeds.
             */
            bool mergeable = !constructAtEnd && includes.Count == 0 && !contract.IsAbstract;

            /*
             * SkipConstructor suppresses generated seeds, while caller-provided instances retain their
             * members.
             */
            bool guardedSeeding = mergeable && members.Exists(member => member.SkipConstructor);
            foreach (Member member in members)
            {
                member.SeedGuard = guardedSeeding ? Member.SeedGuardLocal : null;
            }

            writer.Line("/// <summary>Generated WallstopProto formatter. Do not edit.</summary>");
            writer.Line(
                "public sealed class WProtoFormatter : "
                    + Proto
                    + ".IWProtoFormatter<"
                    + qualified
                    + ">, "
                    + Proto
                    + ".IWProtoPolymorphicFormatter"
                    + (
                        mergeable
                            ? ", " + Proto + ".IWProtoMergeFormatter<" + qualified + ">"
                            : string.Empty
                    )
                    + (
                        0 < encodedTypeParameters.Count
                            ? ", " + Proto + ".IWProtoConditionalFormatter"
                            : string.Empty
                    )
                    + Writer.Open
            );
            writer.Indent();
            writer.Line(
                "/// <summary>The shared instance; the formatter holds no state.</summary>"
            );
            writer.Line("public static readonly WProtoFormatter Instance = new WProtoFormatter();");
            writer.Blank();

            EmitCanServe(writer, encodedTypeParameters);
            EmitCanWrite(writer, qualified, includes);
            writer.Blank();
            EmitMeasure(writer, contract, qualified, members, includes, hooks);
            writer.Blank();
            EmitWrite(writer, contract, qualified, members, includes, hooks);
            writer.Blank();
            EmitRead(
                writer,
                contract,
                qualified,
                members,
                includes,
                hooks,
                constructAtEnd,
                seedsFromInstance,
                skipConstructor,
                mergeable,
                guardedSeeding
            );

            /*
             * Nest wrappers inside their contract formatter to avoid collisions between contracts using the
             * same collection type.
             */
            nested.Emit(writer);

            writer.Outdent();
            writer.Line("}");
        }

        /// <summary>
        /// Emits <c>CanWrite</c>, the question the facade asks before serving a value whose runtime
        /// type is not its declared one.
        /// </summary>
        /// <param name="writer">The destination.</param>
        /// <param name="qualified">The contract's fully-qualified name.</param>
        /// <param name="includes">The subtypes this contract declares.</param>
        /// <remarks>
        /// <para>
        /// The shape mirrors <see cref="EmitIncludeDispatch"/> branch for branch, deliberately:
        /// <c>IsAssignableFrom</c> is what <c>value is Sub</c> compiles to, and each branch recurses
        /// into the same formatter the dispatch chain would have entered. Answering from a separate
        /// list of subtypes would be a second description of the same thing, free to drift.
        /// </para>
        /// <para>
        /// A type the chain would refuse therefore answers <c>false</c> here, before any hook runs.
        /// That ordering is the point: the refusal is an exception thrown from inside
        /// <c>Measure</c>, whose first statement is the before-serialization hook, so discovering
        /// the refusal by catching it would leave that hook run with no matching after-serialization
        /// hook -- the pooled-scratch leak the hook contract exists to prevent.
        /// </para>
        /// </remarks>
        private static void EmitCanWrite(Writer writer, string qualified, List<Include> includes)
        {
            writer.Line("/// <inheritdoc />");
            writer.Line("public bool CanWrite(System.Type runtimeType)" + Writer.Open);
            writer.Indent();
            writer.Line("if (runtimeType == typeof(" + qualified + "))" + Writer.Open);
            writer.Indent();
            writer.Line("return true;");
            writer.Outdent();
            writer.Line("}");

            foreach (Include include in includes)
            {
                writer.Blank();
                writer.Line(
                    "if (typeof("
                        + include.Qualified
                        + ").IsAssignableFrom(runtimeType))"
                        + Writer.Open
                );
                writer.Indent();
                writer.Line("return " + include.Formatter + ".CanWrite(runtimeType);");
                writer.Outdent();
                writer.Line("}");
            }

            writer.Blank();
            writer.Line("return false;");
            writer.Outdent();
            writer.Line("}");
        }

        /// <summary>
        /// Reports whether <paramref name="baseType"/> declares <paramref name="subType"/> with
        /// <c>[WProtoInclude]</c>.
        /// </summary>
        /// <param name="baseType">The immediate base contract.</param>
        /// <param name="subType">The contract that derives from it.</param>
        /// <returns><c>true</c> when the base names it.</returns>
        /// <remarks>
        /// The IMMEDIATE base, deliberately. protobuf-net refuses a grandchild declared on the
        /// grandparent with "Unexpected sub-type" (measured), so each level declares only what
        /// derives directly from it.
        /// <para>
        /// One of the two halves of the same question; <see cref="SubtypeMap.Declares"/> is the
        /// other, asked of the subtype instead of the base.
        /// </para>
        /// </remarks>
        private static bool DeclaresInclude(INamedTypeSymbol baseType, INamedTypeSymbol subType)
        {
            foreach (AttributeData attribute in baseType.GetAttributes())
            {
                if (
                    attribute.AttributeClass?.ToDisplayString() != IncludeAttribute
                    || attribute.ConstructorArguments.Length < 2
                )
                {
                    continue;
                }

                if (
                    attribute.ConstructorArguments[1].Value is INamedTypeSymbol declared
                    && SymbolEqualityComparer.Default.Equals(
                        declared.OriginalDefinition,
                        subType.OriginalDefinition
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reports whether <paramref name="contract"/> writes any constructor of its own.
        /// </summary>
        /// <param name="contract">The contract to inspect.</param>
        /// <returns><c>true</c> when at least one instance constructor appears in source.</returns>
        private static bool DeclaresAConstructor(INamedTypeSymbol contract)
        {
            foreach (IMethodSymbol constructor in contract.InstanceConstructors)
            {
                if (!constructor.IsImplicitlyDeclared)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Warns about each field whose value exists only because a constructor ran, on a contract
        /// that asks for one never to run.
        /// </summary>
        /// <param name="context">The generator context, for reporting.</param>
        /// <param name="contract">The contract being emitted.</param>
        /// <remarks>
        /// The shape that shipped for five releases. <c>AbstractRandom._guidBytes</c> is a scratch
        /// buffer, not a <c>[WProtoMember]</c>, and its only guarantee was its field initializer --
        /// so every one of twelve generators restored through protobuf-net threw from
        /// <c>NextGuid()</c>. Invisible through this package's own API, because the constructor
        /// WallstopProto emits for <c>SkipConstructor</c> does run initializers and protobuf-net's
        /// uninitialized allocation runs none.
        ///
        /// Asked of <see cref="Shape.SkipsConstructor"/> rather than of the flag the emitter ends up
        /// with: an immutable contract makes this generator ignore <c>SkipConstructor</c>, and
        /// protobuf-net honours it regardless.
        /// </remarks>
        private static void ReportInitializersSkipConstructorDiscards(
            GeneratorExecutionContext context,
            INamedTypeSymbol contract
        )
        {
            if (!Shape.SkipsConstructor(contract) || contract.IsValueType)
            {
                return;
            }

            // Uninitialized allocation also clears inherited initializers, so inspect the entire base chain.
            for (
                INamedTypeSymbol declaring = contract;
                declaring != null && declaring.SpecialType != SpecialType.System_Object;
                declaring = declaring.BaseType
            )
            {
                ReportDroppedInitializers(context, contract, declaring);
            }
        }

        /// <summary>
        /// Reports each field of <paramref name="declaring"/> whose value exists only because a
        /// constructor ran, against the contract that asked for one never to.
        /// </summary>
        /// <param name="context">The generator context, for reporting.</param>
        /// <param name="contract">The contract declaring <c>SkipConstructor</c>.</param>
        /// <param name="declaring">The contract itself, or one of its base types.</param>
        private static void ReportDroppedInitializers(
            GeneratorExecutionContext context,
            INamedTypeSymbol contract,
            INamedTypeSymbol declaring
        )
        {
            foreach (ISymbol member in declaring.GetMembers())
            {
                if (
                    member.IsStatic
                    || member is not IFieldSymbol field
                    || field.IsConst
                    || HasAttribute(member, MemberAttribute)
                )
                {
                    continue;
                }

                /*
                 * Report auto-property initializers at the property. ReferenceEquals tests field/property
                 * identity without symbol value comparison.
                 */
                ISymbol declared = field.AssociatedSymbol ?? field;
                if (!ReferenceEquals(declared, field) && HasAttribute(declared, MemberAttribute))
                {
                    continue;
                }

                // Implicit backing fields have no syntax references; inspect the associated property.
                bool initialized = false;
                foreach (SyntaxReference reference in declared.DeclaringSyntaxReferences)
                {
                    SyntaxNode syntax = reference.GetSyntax();
                    if (
                        (
                            syntax is VariableDeclaratorSyntax declarator
                            && declarator.Initializer != null
                        )
                        || (
                            syntax is PropertyDeclarationSyntax property
                            && property.Initializer != null
                        )
                    )
                    {
                        initialized = true;
                        break;
                    }
                }

                if (!initialized)
                {
                    continue;
                }

                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.SkipConstructorDropsAnInitializer,
                        declared.Locations.FirstOrDefault(),
                        contract.Name,
                        SymbolEqualityComparer.Default.Equals(declaring, contract)
                            ? declared.Name
                            : declaring.Name + "." + declared.Name
                    )
                );
            }
        }

        /// <summary>
        /// Reports whether <c>new T()</c> compiles inside the emitted formatter, given the
        /// constructors this generator is about to add.
        /// </summary>
        /// <param name="contract">The contract being emitted.</param>
        /// <remarks>
        /// A struct always has one and it cannot be taken away. A class's IMPLICIT parameterless
        /// constructor does not survive the read constructor emitted alongside this -- declaring any
        /// constructor removes it -- which is why one is emitted back in its place; see
        /// <see cref="EmitConstructor"/>. What cannot be recovered is a class whose author declared
        /// only parameterized constructors: adding a parameterless one there would invent a public
        /// API and let a caller past invariants the author's constructor enforces.
        /// </remarks>
        private static bool CanConstructParameterlessly(INamedTypeSymbol contract)
        {
            return contract.IsValueType
                || !DeclaresAConstructor(contract)
                || HasAccessibleParameterlessConstructor(contract);
        }

        /// <summary>
        /// Reports whether <c>new T()</c> could leave any serialized member at something other than
        /// its type's default.
        /// </summary>
        /// <param name="contract">The contract being emitted.</param>
        /// <remarks>
        /// The whole cost of seeding an immutable contract is one construction per read, so it is
        /// paid only where it can change an answer. Two syntactic things can set a member before the
        /// read loop starts: an initializer on the member itself, and the body of a parameterless
        /// constructor. A constructor that chains to another (<c>: this(...)</c>) counts as a body,
        /// because the constructor it chains to is one.
        /// </remarks>
        private static bool ConstructionCouldSeedAMember(INamedTypeSymbol contract)
        {
            foreach (ISymbol member in contract.GetMembers())
            {
                if (member is IMethodSymbol candidate)
                {
                    if (
                        candidate.MethodKind != MethodKind.Constructor
                        || candidate.IsStatic
                        || candidate.Parameters.Length != 0
                        || candidate.IsImplicitlyDeclared
                    )
                    {
                        continue;
                    }

                    foreach (SyntaxReference reference in candidate.DeclaringSyntaxReferences)
                    {
                        if (
                            reference.GetSyntax() is ConstructorDeclarationSyntax declaration
                            && (
                                declaration.Initializer != null
                                || declaration.ExpressionBody != null
                                || (
                                    declaration.Body != null
                                    && 0 < declaration.Body.Statements.Count
                                )
                            )
                        )
                        {
                            return true;
                        }
                    }

                    continue;
                }

                if (member.IsStatic || (!(member is IFieldSymbol) && !(member is IPropertySymbol)))
                {
                    continue;
                }

                foreach (SyntaxReference reference in member.DeclaringSyntaxReferences)
                {
                    SyntaxNode syntax = reference.GetSyntax();
                    if (
                        syntax is VariableDeclaratorSyntax declarator
                        && declarator.Initializer != null
                    )
                    {
                        return true;
                    }

                    if (
                        syntax is PropertyDeclarationSyntax property
                        && property.Initializer != null
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Finds the outermost contract in <paramref name="contract"/>'s base chain, or <c>null</c>
        /// when it is the outermost itself.
        /// </summary>
        /// <param name="contract">The contract to walk up from.</param>
        /// <returns>The contract that owns the wire shape, or <c>null</c>.</returns>
        /// <remarks>
        /// The chain stops at the first ancestor that is not a contract, because that is where
        /// protobuf-net's model stops too: a contract deriving from a plain class owns its own shape.
        /// </remarks>
        private static INamedTypeSymbol RootContract(INamedTypeSymbol contract)
        {
            INamedTypeSymbol root = null;
            for (
                INamedTypeSymbol current = contract.BaseType;
                current != null && Shape.IsContract(current);
                current = current.BaseType
            )
            {
                root = current;
            }

            return root;
        }

        /// <summary>
        /// Reports whether any contract between <paramref name="contract"/> and its chain root was
        /// refused.
        /// </summary>
        /// <param name="contract">The contract whose ancestry is walked.</param>
        /// <param name="refused">The contracts this compilation declined to emit.</param>
        /// <returns><c>true</c> when a formatter this type would name was not published.</returns>
        /// <remarks>
        /// Walks the same chain <c>CanServe</c> emits, so the two cannot disagree about which
        /// ancestors are named.
        /// </remarks>
        private static bool HasRefusedAncestor(
            INamedTypeSymbol contract,
            HashSet<INamedTypeSymbol> refused
        )
        {
            for (
                INamedTypeSymbol current = Shape.IsContract(contract.BaseType)
                    ? contract.BaseType
                    : null;
                current != null;
                current = Shape.IsContract(current.BaseType) ? current.BaseType : null
            )
            {
                if (refused.Contains(current))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Orders contracts so that every subtype precedes the base it derives from.
        /// </summary>
        /// <param name="contracts">The compilation's contracts, in discovery order.</param>
        /// <returns>The same contracts, deepest inheritance level first.</returns>
        /// <remarks>
        /// What lets a base's include set be filtered as its source is written: an include names a
        /// DIRECT subtype, so once every deeper contract has been attempted, the outcome of each of
        /// a base's subtypes is already known. Ordering by depth is enough -- a subtype is strictly
        /// deeper than its base -- and <see cref="Enumerable.OrderByDescending{T, TKey}"/> is
        /// stable, so contracts at one depth keep discovery order and the output stays
        /// deterministic.
        /// </remarks>
        private static List<INamedTypeSymbol> DeepestFirst(List<INamedTypeSymbol> contracts)
        {
            return contracts.OrderByDescending(ContractDepth).ToList();
        }

        /// <summary>
        /// How many contracts sit above <paramref name="contract"/> in its base chain.
        /// </summary>
        /// <param name="contract">The contract to measure.</param>
        /// <returns>Zero when nothing above it is a contract.</returns>
        private static int ContractDepth(INamedTypeSymbol contract)
        {
            int depth = 0;
            for (
                INamedTypeSymbol current = contract.BaseType;
                current != null && Shape.IsContract(current);
                current = current.BaseType
            )
            {
                depth++;
            }

            return depth;
        }

        /// <summary>
        /// Emits the formatter registered for a subtype: the one that writes the whole hierarchy.
        /// </summary>
        /// <param name="writer">The output.</param>
        /// <param name="qualified">The subtype's fully qualified name.</param>
        /// <param name="root">The outermost contract in its base chain.</param>
        /// <remarks>
        /// <para>
        /// Serializing a subtype under its own declared type must produce the same bytes as
        /// serializing it as its base, because that is what protobuf-net does -- the root's formatter
        /// dispatches on the runtime type, writes the include holding the subtype's members, then
        /// writes its own. The nested <c>WProtoFormatter</c> beside this one writes only the include's
        /// payload and is what the root reaches for; this is the entry point.
        /// </para>
        /// <para>
        /// <b>Read narrows rather than assumes.</b> The root produces whatever type the payload's
        /// include names, which need not be this one -- a save written by a build where the member
        /// held a sibling is ordinary input, not corruption. Handing that back through a blind cast
        /// would throw from inside generated code; refusing lets the caller see a failed read.
        /// </para>
        /// </remarks>
        private static void EmitRootFormatter(
            Writer writer,
            INamedTypeSymbol contract,
            string qualified,
            INamedTypeSymbol root
        )
        {
            string rootQualified = root.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            writer.Line("/// <summary>Generated WallstopProto entry point. Do not edit.</summary>");
            writer.Line(
                "public sealed class WProtoRootFormatter : "
                    + Proto
                    + ".IWProtoFormatter<"
                    + qualified
                    + ">, "
                    + Proto
                    + ".IWProtoPolymorphicFormatter, "
                    + Proto
                    + ".IWProtoConditionalFormatter"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line(
                "/// <summary>The shared instance; the formatter holds no state.</summary>"
            );
            writer.Line(
                "public static readonly WProtoRootFormatter Instance = new WProtoRootFormatter();"
            );
            writer.Blank();

            /*
             * Every ancestor contributes members to the entry point, so every formatter must validate its
             * closure parameters.
             */
            writer.Line("/// <inheritdoc />");
            writer.Line("public bool CanServe()" + Writer.Open);
            writer.Indent();

            StringBuilder chain = new StringBuilder();
            int index = 0;
            for (
                INamedTypeSymbol current = contract;
                current != null;
                current = Shape.IsContract(current.BaseType) ? current.BaseType : null
            )
            {
                if (0 < index)
                {
                    chain.Append(" && ");
                }

                // Sealed formatters need an object cast before testing interfaces they may not implement.
                chain
                    .Append("(!((object)")
                    .Append(current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                    .Append(".WProtoFormatter.Instance is ")
                    .Append(Proto)
                    .Append(".IWProtoConditionalFormatter conditional")
                    .Append(index)
                    .Append(") || conditional")
                    .Append(index)
                    .Append(".CanServe())");
                index++;
            }

            writer.Line("return " + chain + ";");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
            writer.Line("/// <inheritdoc />");
            writer.Line("public bool CanWrite(System.Type runtimeType)" + Writer.Open);
            writer.Indent();
            /*
             * Root delegation also covers sibling subtypes, so this entry point must narrow its accepted
             * subtree.
             */
            writer.Line(
                "return typeof("
                    + qualified
                    + ").IsAssignableFrom(runtimeType) && "
                    + rootQualified
                    + ".WProtoFormatter.Instance.CanWrite(runtimeType);"
            );
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
            writer.Line("/// <inheritdoc />");
            writer.Line("public int Measure(in " + qualified + " value)" + Writer.Open);
            writer.Indent();
            writer.Line("return " + rootQualified + ".WProtoFormatter.Instance.Measure(value);");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
            writer.Line("/// <inheritdoc />");
            writer.Line(
                "public bool Write(ref "
                    + Proto
                    + ".WProtoWriter writer, in "
                    + qualified
                    + " value)"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line(
                "return " + rootQualified + ".WProtoFormatter.Instance.Write(ref writer, value);"
            );
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
            writer.Line("/// <inheritdoc />");
            writer.Line(
                "public bool TryRead(ref "
                    + Proto
                    + ".WProtoReader reader, out "
                    + qualified
                    + " value)"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line(
                "if (!"
                    + rootQualified
                    + ".WProtoFormatter.Instance.TryRead(ref reader, out "
                    + rootQualified
                    + " read))"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line("value = default(" + qualified + ");");
            writer.Line("return false;");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
            writer.Line("value = read as " + qualified + ";");
            writer.Line("return value != null;");
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
        }

        /// <summary>
        /// Emits a private constructor that runs no user code, for a contract declared
        /// <c>SkipConstructor</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// protobuf-net implements the same flag by allocating the object uninitialized. That is a
        /// reflection call, which is the one thing this serializer exists to avoid, so the instance
        /// comes from a constructor emitted into the contract's own <c>partial</c> declaration
        /// instead -- statically dispatched, and AOT-compiled like any other.
        /// </para>
        /// <para>
        /// <b>The difference is stated rather than hidden.</b> C# field initializers and base
        /// constructors still run here and do not under protobuf-net. That makes the resulting object
        /// MORE initialized, never less: <c>AbstractRandom</c>'s sixteen-byte scratch buffer exists
        /// after this and is <c>null</c> after protobuf-net's. Every such field is then overwritten
        /// by the payload or left at a value the type's own author chose, which is the outcome the
        /// flag is reached for in the first place.
        /// </para>
        /// </remarks>
        private static void EmitSkippingConstructor(Writer writer, INamedTypeSymbol contract)
        {
            writer.Line(
                "/// <summary>Generated WallstopProto constructor. Do not edit or call.</summary>"
            );
            writer.Line(
                "private "
                    + contract.Name
                    + "("
                    + Proto
                    + ".WProtoConstruct wprotoMarker)"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line("_ = wprotoMarker;");
            writer.Outdent();
            writer.Line("}");
        }

        /// <summary>
        /// Emits a private constructor that assigns every member, for a contract that cannot be
        /// assigned after construction.
        /// </summary>
        /// <remarks>
        /// <para>
        /// This is what lets a type keep <c>readonly</c> fields and still be read. C# permits a
        /// readonly field to be assigned only in a constructor of its declaring type -- a nested
        /// formatter is not enough, but the generator reopens the contract as <c>partial</c>, and a
        /// constructor emitted there IS one. No public surface changes: the constructor is private,
        /// and the type keeps the immutability its author chose.
        /// </para>
        /// <para>
        /// The <c>WProtoConstruct</c> first parameter exists only so the signature cannot collide
        /// with a constructor the author already wrote -- a two-int type very plausibly has an
        /// <c>(int, int)</c> constructor of its own.
        /// </para>
        /// <para>
        /// A struct assigns <c>this = default</c> first, because C# requires every field to be
        /// definitely assigned and a contract may hold fields no <c>[WProtoMember]</c> covers.
        /// </para>
        /// </remarks>
        private static void EmitConstructor(
            Writer writer,
            INamedTypeSymbol contract,
            List<Member> members
        )
        {
            /*
             * Adding a generated constructor removes the implicit default one; restore it to preserve
             * consumer construction and oracle compatibility.
             */
            if (!contract.IsValueType && !DeclaresAConstructor(contract))
            {
                writer.Line(
                    "/// <summary>The parameterless constructor this type would have had.</summary>"
                );
                writer.Line("public " + contract.Name + "() { }");
                writer.Blank();
            }

            writer.Line(
                "/// <summary>Generated WallstopProto constructor. Do not edit or call.</summary>"
            );

            StringBuilder parameters = new StringBuilder(Proto + ".WProtoConstruct wprotoMarker");
            foreach (Member member in members)
            {
                parameters.Append(", ");
                parameters.Append(member.DeclaredType);
                parameters.Append(" wproto_");
                parameters.Append(member.MemberName);
            }

            writer.Line("private " + contract.Name + "(" + parameters + ")" + Writer.Open);
            writer.Indent();
            writer.Line("_ = wprotoMarker;");

            if (contract.IsValueType)
            {
                writer.Line("this = default(" + contract.Name + ");");
            }

            foreach (Member member in members)
            {
                writer.Line("this." + member.MemberName + " = wproto_" + member.MemberName + ";");
            }

            writer.Outdent();
            writer.Line("}");
        }

        private static void EmitMeasure(
            Writer writer,
            INamedTypeSymbol contract,
            string qualified,
            List<Member> members,
            List<Include> includes,
            Hooks hooks
        )
        {
            writer.Line("/// <inheritdoc />");
            writer.Line("public int Measure(in " + qualified + " value)" + Writer.Open);
            writer.Indent();

            if (hooks.BeforeSerialization != null)
            {
                /*
                 * Run before-serialization hooks only during Measure to avoid repeated pooled-state
                 * acquisition.
                 */
                writer.Line("value." + hooks.BeforeSerialization + "();");
            }

            writer.Line("int size = 0;");

            // The oracle emits includes before all ordinary members regardless of tag order.
            EmitIncludeDispatch(
                writer,
                contract,
                qualified,
                includes,
                include =>
                    "size += "
                    + Proto
                    + ".WProtoSizes.TagSize("
                    + include.Tag
                    + ") + "
                    + Proto
                    + ".WProtoSizes.MessageSize("
                    + include.Formatter
                    + ", "
                    + include.Local
                    + ");"
            );

            foreach (Member member in members)
            {
                member.EmitMeasure(writer);
            }

            writer.Line("return size;");
            writer.Outdent();
            writer.Line("}");
        }

        private static void EmitWrite(
            Writer writer,
            INamedTypeSymbol contract,
            string qualified,
            List<Member> members,
            List<Include> includes,
            Hooks hooks
        )
        {
            writer.Line("/// <inheritdoc />");
            writer.Line(
                "public bool Write(ref "
                    + Proto
                    + ".WProtoWriter writer, in "
                    + qualified
                    + " value)"
                    + Writer.Open
            );
            writer.Indent();

            EmitIncludeDispatch(
                writer,
                contract,
                qualified,
                includes,
                include =>
                    "if (!writer.TryWriteMessage("
                    + include.Tag
                    + ", "
                    + include.Formatter
                    + ", "
                    + include.Local
                    + "))"
            );

            foreach (Member member in members)
            {
                member.EmitWrite(writer);
            }

            if (hooks.AfterSerialization != null)
            {
                writer.Line("value." + hooks.AfterSerialization + "();");
            }

            writer.Line("return true;");
            writer.Outdent();
            writer.Line("}");
        }

        private static void EmitRead(
            Writer writer,
            INamedTypeSymbol contract,
            string qualified,
            List<Member> members,
            List<Include> includes,
            Hooks hooks,
            bool constructAtEnd,
            bool seedsFromInstance,
            bool skipConstructor,
            bool mergeable,
            bool guardedSeeding
        )
        {
            bool polymorphic = 0 < includes.Count;

            if (mergeable)
            {
                // Share the read body to prevent seeded and unseeded wire handling from diverging.
                writer.Line("/// <inheritdoc />");
                writer.Line(
                    "public bool TryRead(ref "
                        + Proto
                        + ".WProtoReader reader, out "
                        + qualified
                        + " value)"
                        + Writer.Open
                );
                writer.Indent();
                writer.Line(
                    "return TryReadInto(ref reader, default(" + qualified + "), out value);"
                );
                writer.Outdent();
                writer.Line("}");
                writer.Blank();
            }

            writer.Line("/// <inheritdoc />");
            writer.Line(
                mergeable
                    ? "public bool TryReadInto(ref "
                        + Proto
                        + ".WProtoReader reader, in "
                        + qualified
                        + " seed, out "
                        + qualified
                        + " value)"
                        + Writer.Open
                    : "public bool TryRead(ref "
                        + Proto
                        + ".WProtoReader reader, out "
                        + qualified
                        + " value)"
                        + Writer.Open
            );
            writer.Indent();

            if (mergeable)
            {
                writer.Line(qualified + " read = seed;");
                if (guardedSeeding)
                {
                    /*
                     * Determine seed ownership before creating an instance whose initialized members must be
                     * ignored.
                     */
                    writer.Line("bool " + Member.SeedGuardLocal + " = read != null;");
                }

                if (!contract.IsValueType)
                {
                    writer.Line("if (read == null)" + Writer.Open);
                    writer.Indent();
                    writer.Line(
                        skipConstructor
                            ? "read = new "
                                + qualified
                                + "(default("
                                + Proto
                                + ".WProtoConstruct));"
                            : "read = new " + qualified + "();"
                    );
                    writer.Outdent();
                    writer.Line("}");
                }
            }
            else if (constructAtEnd)
            {
                /*
                 * The temporary instance supplies constructor defaults for immutable read locals; final
                 * construction uses the decoded values.
                 */
                writer.Line(
                    seedsFromInstance
                        ? qualified + " read = new " + qualified + "();"
                        : qualified + " read = default(" + qualified + ");"
                );
            }
            else if (contract.IsValueType)
            {
                writer.Line(qualified + " read = default(" + qualified + ");");
            }
            else if (contract.IsAbstract)
            {
                // An abstract base requires an include to produce any instance.
                writer.Line(qualified + " read = null;");
            }
            else if (skipConstructor)
            {
                writer.Line(
                    qualified
                        + " read = new "
                        + qualified
                        + "(default("
                        + Proto
                        + ".WProtoConstruct));"
                );
            }
            else
            {
                writer.Line(qualified + " read = new " + qualified + "();");
            }

            if (hooks.BeforeDeserialization != null && !polymorphic && !constructAtEnd)
            {
                writer.Line("read." + hooks.BeforeDeserialization + "();");
            }

            writer.Blank();
            foreach (Member member in members)
            {
                member.EmitReadLocals(writer);
            }

            writer.Line(
                "while (reader.TryReadTag(out int fieldNumber, out int wireType))" + Writer.Open
            );
            writer.Indent();
            writer.Line("switch (fieldNumber)" + Writer.Open);
            writer.Indent();

            foreach (Include include in includes)
            {
                writer.Line(
                    "case "
                        + include.Tag
                        + " when wireType == "
                        + Proto
                        + ".WProtoWireType.LengthDelimited:"
                        + Writer.Open
                );
                writer.Indent();
                writer.Line(
                    "if (!reader.TryReadMessage("
                        + include.Formatter
                        + ", out "
                        + include.Qualified
                        + " "
                        + include.Local
                        + "))"
                        + Writer.Open
                );
                writer.Indent();
                writer.Line("value = default(" + qualified + ");");
                writer.Line("return false;");
                writer.Outdent();
                writer.Line("}");
                writer.Blank();
                /*
                 * Last-include assignment avoids the oracle's recursive sibling replacement and stack
                 * overflow.
                 */
                writer.Line("read = " + include.Local + ";");
                writer.Line("break;");
                writer.Outdent();
                writer.Line("}");
            }

            foreach (Member member in members)
            {
                member.EmitReadCases(writer, qualified);
            }

            writer.Line("default:" + Writer.Open);
            writer.Indent();

            writer.Line("if (!reader.TrySkipField(fieldNumber, wireType))" + Writer.Open);
            writer.Indent();
            writer.Line("value = default(" + qualified + ");");
            writer.Line("return false;");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();
            writer.Line("break;");
            writer.Outdent();
            writer.Line("}");

            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();

            writer.Line("if (reader.Malformed)" + Writer.Open);
            writer.Indent();
            writer.Line("value = default(" + qualified + ");");
            writer.Line("return false;");
            writer.Outdent();
            writer.Line("}");
            writer.Blank();

            if (contract.IsAbstract)
            {
                writer.Line("if (read == null)" + Writer.Open);
                writer.Indent();
                writer.Line("value = default(" + qualified + ");");
                writer.Line("return false;");
                writer.Outdent();
                writer.Line("}");
                writer.Blank();
            }

            if (hooks.BeforeDeserialization != null && polymorphic)
            {
                /*
                 * Polymorphic instances exist only after include dispatch; deferred assignments keep the
                 * before-read hook early enough.
                 */
                writer.Line("read." + hooks.BeforeDeserialization + "();");
                writer.Blank();
            }

            // Do not commit accumulated collections after a malformed payload.
            foreach (Member member in members)
            {
                member.EmitReadEpilogue(writer, qualified);
            }

            if (constructAtEnd)
            {
                StringBuilder arguments = new StringBuilder(
                    "default(" + Proto + ".WProtoConstruct)"
                );
                foreach (Member member in members)
                {
                    arguments.Append(", ");
                    arguments.Append(member.ReadLocal);
                }

                writer.Line("read = new " + qualified + "(" + arguments + ");");
                writer.Blank();

                if (hooks.BeforeDeserialization != null)
                {
                    // Immutable construction assigns members before any instance hook can run.
                    writer.Line("read." + hooks.BeforeDeserialization + "();");
                    writer.Blank();
                }
            }

            if (hooks.AfterDeserialization != null)
            {
                // Only successful reads may rebuild derived state from decoded members.
                writer.Line("read." + hooks.AfterDeserialization + "();");
            }

            writer.Line("value = read;");
            writer.Line("return true;");
            writer.Outdent();
            writer.Line("}");
        }

        /// <summary>
        /// Emits the module initializer that hands every closed JSON converter to the registry.
        /// </summary>
        /// <param name="registrations">One <c>typeof(x), new Converter&lt;..&gt;()</c> per closure.</param>
        /// <returns>The generated source.</returns>
        /// <remarks>
        /// <para>
        /// The whole point is the <c>new</c>. IL2CPP compiles a generic closure when source
        /// references it, and nothing in a player references
        /// <c>DequeConverter&lt;TheirStruct&gt;</c> -- the factory that would have built it does so
        /// through <c>MakeGenericType</c>, which is not source and is not compiled. Writing the
        /// constructor into the assembly that named the closure is what makes it exist.
        /// </para>
        /// <para>
        /// Registration failures are swallowed by <c>TryRegister</c> rather than thrown: this runs
        /// during Unity startup, where an exception takes the whole registrar with it, and a
        /// duplicate closure across two assemblies is expected rather than exceptional.
        /// </para>
        /// </remarks>
        private static string EmitJsonRegistrar(
            List<string> registrations,
            bool disableModuleInitializer
        )
        {
            Writer writer = new Writer();
            writer.Line("// <auto-generated />");
            writer.Line("#pragma warning disable");
            writer.Line("namespace WallstopStudios.UnityHelpers.Generated" + Writer.Open);
            writer.Indent();
            writer.Line(
                "/// <summary>Registers a JSON converter for every closure in this assembly.</summary>"
            );
            writer.Line("internal static class WJsonGeneratedRegistrar" + Writer.Open);
            writer.Indent();

            // JSON converters must register before settings deserialization, during SubsystemRegistration.
            writer.Line("#if UNITY_5_3_OR_NEWER");
            writer.Line("#if UNITY_EDITOR");
            writer.Line("[global::UnityEditor.InitializeOnLoadMethod]");
            writer.Line("#endif");
            writer.Line(
                "[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]"
            );
            writer.Line("#else");
            if (!disableModuleInitializer)
            {
                writer.Line("[global::System.Runtime.CompilerServices.ModuleInitializer]");
            }
            writer.Line("#endif");
            writer.Line("internal static void Register()" + Writer.Open);
            writer.Indent();
            foreach (string registration in registrations)
            {
                writer.Line(
                    "global::WallstopStudios.UnityHelpers.Core.Serialization.JsonConverters.WJsonConverterRegistry.TryRegister("
                        + registration
                        + ");"
                );
            }

            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            return writer.ToString();
        }

        private static string EmitRegistrar(
            GeneratorExecutionContext context,
            List<string> registrations,
            List<string> rootMarshals,
            List<string> declaredRoots,
            HashSet<INamedTypeSymbol> enumClosures,
            bool disableModuleInitializer
        )
        {
            Writer writer = new Writer();
            writer.Line("// <auto-generated />");
            writer.Line("#pragma warning disable");
            writer.Line("namespace WallstopStudios.UnityHelpers.Generated" + Writer.Open);
            writer.Indent();
            writer.Line(
                "/// <summary>Registers every generated formatter in this assembly.</summary>"
            );
            // Internal registrars can reuse a stable name without conflicting across assemblies.
            writer.Line("internal static class WProtoGeneratedRegistrar" + Writer.Open);
            writer.Indent();

            writer.Line("#if UNITY_INCLUDE_TESTS");
            writer.Line(
                "internal static long FirstRegistrationElapsedTimestampTicks { get; private set; }"
            );
            writer.Line("internal static bool HasRecordedFirstRegistration { get; private set; }");
            writer.Line("#endif");

            // BeforeSceneLoad follows built-in SubsystemRegistration so consumer formatter replacements win.
            writer.Line("#if UNITY_5_3_OR_NEWER");
            writer.Line("#if UNITY_EDITOR");
            writer.Line("[global::UnityEditor.InitializeOnLoadMethod]");
            writer.Line("#endif");
            writer.Line(
                "[global::UnityEngine.RuntimeInitializeOnLoadMethod(global::UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]"
            );
            writer.Line("#else");
            if (!disableModuleInitializer)
            {
                writer.Line("[global::System.Runtime.CompilerServices.ModuleInitializer]");
            }
            writer.Line("#endif");
            writer.Line("internal static void Register()" + Writer.Open);
            writer.Indent();
            writer.Line("#if UNITY_INCLUDE_TESTS");
            writer.Line(
                "long registrationStarted = global::System.Diagnostics.Stopwatch.GetTimestamp();"
            );
            writer.Line("#endif");
            writer.Line(Proto + ".WProtoScalarFormatters.RegisterAll();");
            foreach (INamedTypeSymbol enumClosure in enumClosures)
            {
                INamedTypeSymbol underlying = enumClosure.EnumUnderlyingType;
                int size =
                    underlying?.SpecialType == SpecialType.System_SByte
                    || underlying?.SpecialType == SpecialType.System_Byte
                        ? 1
                    : underlying?.SpecialType == SpecialType.System_Int16
                    || underlying?.SpecialType == SpecialType.System_UInt16
                        ? 2
                    : underlying?.SpecialType == SpecialType.System_Int64
                    || underlying?.SpecialType == SpecialType.System_UInt64
                        ? 8
                    : 4;
                bool signed =
                    underlying?.SpecialType == SpecialType.System_SByte
                    || underlying?.SpecialType == SpecialType.System_Int16
                    || underlying?.SpecialType == SpecialType.System_Int32
                    || underlying?.SpecialType == SpecialType.System_Int64;
                writer.Line(
                    Proto
                        + ".WProtoScalarFormatterProvider.Register("
                        + Proto
                        + ".WProtoScalarFormatters.Enum<"
                        + enumClosure.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        + ">("
                        + size
                        + ", "
                        + (signed ? "true" : "false")
                        + ")"
                        + ");"
                );
            }
            foreach (string registration in registrations)
            {
                writer.Line(Proto + ".WProtoFormatterProvider.Register(" + registration + ");");
            }

            // Root marshals need a separate registry or generic members would inherit root-only encodings.
            foreach (string marshal in rootMarshals)
            {
                writer.Line(Proto + ".WProtoRootMarshalProvider.Register(" + marshal + ");");
            }

            /*
             * Declared-root adapters need a separate registry because generic members do not perform their
             * CanServe guards.
             */
            foreach (string declaredRoot in declaredRoots)
            {
                writer.Line(Proto + ".WProtoDeclaredRootProvider.Register" + declaredRoot + "();");
            }

            writer.Line("#if UNITY_INCLUDE_TESTS");
            writer.Line("if (!HasRecordedFirstRegistration)" + Writer.Open);
            writer.Indent();
            writer.Line(
                "FirstRegistrationElapsedTimestampTicks = global::System.Diagnostics.Stopwatch.GetTimestamp() - registrationStarted;"
            );
            writer.Line("HasRecordedFirstRegistration = true;");
            writer.Outdent();
            writer.Line("}");
            writer.Line("#endif");

            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            return writer.ToString();
        }

        /// <summary>
        /// Returns the type parameter list to reopen <paramref name="symbol"/> with, or empty.
        /// </summary>
        /// <remarks>
        /// A reopened <c>partial</c> declaration that drops its type parameters does not compile, so
        /// this is not cosmetic. Constraints are deliberately omitted: C# forbids restating them on a
        /// secondary partial declaration when the primary already carries them.
        /// </remarks>
        private static string TypeParameterList(INamedTypeSymbol symbol)
        {
            if (!symbol.IsGenericType || symbol.TypeParameters.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder("<");
            for (int index = 0; index < symbol.TypeParameters.Length; index++)
            {
                if (0 < index)
                {
                    builder.Append(", ");
                }

                builder.Append(symbol.TypeParameters[index].Name);
            }

            builder.Append('>');
            return builder.ToString();
        }

        /// <summary>
        /// Resolves the closed generic a node constructs, whether it is spelled as a type or built
        /// by a tuple literal.
        /// </summary>
        /// <param name="model">The semantic model for the node's tree.</param>
        /// <param name="node">The node to resolve.</param>
        /// <param name="location">Receives the node's location, for diagnostics.</param>
        /// <remarks>
        /// A <c>TypeSyntax</c> scan alone misses the most ordinary way a tuple ever appears.
        /// <c>Serializer.ProtoSerialize((7, 1.5f))</c> names no type at all -- the argument is a
        /// <c>TupleExpressionSyntax</c> and the closure is inferred -- so a consumer who writes only
        /// that call got no registration and fell through to the reflective path in a player, which
        /// is the failure the ValueTuple marshal exists to close.
        ///
        /// The underlying type is returned rather than the tuple type, so <c>(int Count, float Weight)</c>
        /// and <c>(int, float)</c> are one closure and the registrar writes <c>ValueTuple&lt;int, float&gt;</c>
        /// rather than a name carrying element labels.
        /// </remarks>
        private static INamedTypeSymbol ConstructedTypeAt(
            SemanticModel model,
            SyntaxNode node,
            out Location location
        )
        {
            if (node is TypeSyntax type)
            {
                location = type.GetLocation();
                return Resolve(model, type) as INamedTypeSymbol;
            }

            if (node is TupleExpressionSyntax tuple)
            {
                location = tuple.GetLocation();
                if (!(model.GetTypeInfo(tuple).Type is INamedTypeSymbol named))
                {
                    return null;
                }

                return named.IsTupleType ? named.TupleUnderlyingType ?? named : named;
            }

            location = Location.None;
            return null;
        }

        /// <summary>
        /// Finds every closed construction of <paramref name="contract"/> the compilation names.
        /// </summary>
        /// <remarks>
        /// A registrar cannot register an open generic, and nothing can construct one at runtime
        /// without <c>MakeGenericType</c> -- the exact call IL2CPP cannot compile. So the
        /// constructions are discovered from the source the compiler is already parsing: every type
        /// the semantic model resolves anywhere in this compilation, deduplicated. A construction
        /// that appears in no source cannot be reached at runtime either.
        /// </remarks>
        private static IEnumerable<string> ClosedConstructions(
            Compilation compilation,
            INamedTypeSymbol contract,
            Action<Diagnostic> report,
            HashSet<string> announced
        )
        {
            HashSet<string> found = new HashSet<string>();

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    INamedTypeSymbol named = ConstructedTypeAt(model, node, out Location where);
                    if (named == null || !named.IsGenericType || named.IsUnboundGenericType)
                    {
                        continue;
                    }

                    if (
                        !SymbolEqualityComparer.Default.Equals(
                            named.ConstructedFrom,
                            contract.ConstructedFrom
                        )
                    )
                    {
                        continue;
                    }

                    /*
                     * Nested arguments may hide unbound parameters or inaccessible types, so registration
                     * checks must recurse.
                     */
                    if (
                        !TypeNaming.ReportIfUnnameable(named, compilation, where, report, announced)
                    )
                    {
                        found.Add(named.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// Resolves the type a piece of type syntax names, however it is spelled.
        /// </summary>
        /// <param name="model">The semantic model for the syntax's tree.</param>
        /// <param name="type">The type syntax.</param>
        /// <returns>The named type, or <c>null</c> when the syntax names none.</returns>
        /// <remarks>
        /// <c>GetTypeInfo</c> alone is not enough, and the gap has a specific shape:
        /// <c>new Box&lt;int&gt;()</c> binds its type syntax to a CONSTRUCTOR, so the type info is
        /// empty and the closure went undiscovered -- silently, and only until the first
        /// serialization in a shipped player. Object creation is the most natural way to name a
        /// closure and can easily be the only one in a consumer's assembly, so both questions are
        /// asked.
        /// </remarks>
        private static ITypeSymbol Resolve(SemanticModel model, TypeSyntax type)
        {
            return model.GetTypeInfo(type).Type ?? model.GetSymbolInfo(type).Symbol as ITypeSymbol;
        }

        /// <summary>
        /// Reports whether <paramref name="type"/> still mentions a type parameter anywhere within
        /// it, at any depth.
        /// </summary>
        /// <param name="type">The type to inspect.</param>
        /// <returns><c>true</c> when the type cannot be named as a closed construction.</returns>
        private static bool IsOpen(ITypeSymbol type)
        {
            return TypeNaming.IsOpen(type);
        }

        /// <summary>
        /// Reads and validates the contract's subtype list, however each entry was declared.
        /// </summary>
        /// <param name="context">The generator context, for reporting.</param>
        /// <param name="contract">The contract whose subtypes are being collected.</param>
        /// <param name="members">Its own members, whose field numbers are already claimed.</param>
        /// <param name="subtypes">Every <c>[WProtoSubtype]</c> declaration in the compilation.</param>
        /// <returns>The merged list ordered by field number, or <c>null</c> when one was refused.</returns>
        /// <remarks>
        /// <para>
        /// <c>[WProtoInclude(tag, typeof(Sub))]</c> on the base and <c>[WProtoSubtype(typeof(Base),
        /// tag)]</c> on the subtype are the same declaration written from either end, and they merge
        /// into one list here so that everything downstream -- the dispatch chain, <c>CanWrite</c>,
        /// the measure and read paths -- has a single description of the hierarchy and the two forms
        /// produce identical bytes.
        /// </para>
        /// <para>
        /// The ordering is load-bearing rather than cosmetic. <c>value is Beta</c> is true for a
        /// <c>Gamma</c>, so a dispatch chain that tested the shallower type first would write a
        /// <c>Gamma</c> under Beta's include tag and lose the Gamma level entirely -- a silent type
        /// downgrade. Sorting by inheritance depth, deepest first, makes the first matching test the
        /// most derived one.
        /// </para>
        /// </remarks>
        private static List<Include> CollectIncludes(
            GeneratorExecutionContext context,
            INamedTypeSymbol contract,
            List<Member> members,
            SubtypeMap subtypes
        )
        {
            List<Include> includes = new List<Include>();
            HashSet<int> claimed = new HashSet<int>();
            ReservedMap reserved = ReservedMap.Build(contract);
            foreach (Member member in members)
            {
                claimed.Add(member.Tag);
            }

            Dictionary<int, INamedTypeSymbol> owners = new Dictionary<int, INamedTypeSymbol>();
            bool failed = false;
            foreach (AttributeData attribute in contract.GetAttributes())
            {
                if (
                    attribute.AttributeClass == null
                    || attribute.AttributeClass.ToDisplayString() != IncludeAttribute
                    || attribute.ConstructorArguments.Length < 2
                )
                {
                    continue;
                }

                int tag = (int)(attribute.ConstructorArguments[0].Value ?? 0);
                INamedTypeSymbol subType =
                    attribute.ConstructorArguments[1].Value as INamedTypeSymbol;
                string name = subType == null ? "?" : subType.Name;

                string problem = null;
                if (subType == null)
                {
                    problem = "the subtype could not be resolved";
                }
                else if (!SymbolEqualityComparer.Default.Equals(subType.BaseType, contract))
                {
                    // The oracle requires direct subtype declarations; grandparent includes fail at runtime.
                    problem =
                        "'"
                        + name
                        + "' does not derive DIRECTLY from '"
                        + contract.Name
                        + "'; declare it on its immediate base type instead";
                }
                else if (!Shape.IsContract(subType))
                {
                    problem = "'" + name + "' is not itself a [WProtoContract]";
                }
                else if (tag < 1 || 536870911 < tag || (19000 <= tag && tag <= 19999))
                {
                    problem =
                        "field number "
                        + tag
                        + " is outside 1-536870911 or inside the reserved 19000-19999 range";
                }
                else if (
                    subtypes.Manifest.TryRetired(contract, tag, out string retiredBy)
                    && retiredBy != subType.ToDisplayString()
                )
                {
                    // Both include declaration forms share the same retirement constraints.
                    problem = SubtypeMap.RetiredProblem(tag, contract, retiredBy);
                }
                else if (reserved.ReservesNumber(tag))
                {
                    // Validate before claiming the tag so refused includes do not reserve it.
                    problem = ReservedMap.ReservedProblem(tag, contract.Name);
                }
                else if (!claimed.Add(tag))
                {
                    problem =
                        "field number " + tag + " is already claimed on '" + contract.Name + "'";
                }

                if (problem != null)
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            WProtoDiagnostics.BadInclude,
                            contract.Locations.FirstOrDefault(),
                            contract.Name,
                            tag,
                            name,
                            problem
                        )
                    );
                    failed = true;
                    continue;
                }

                owners[tag] = subType;
                includes.Add(new Include(tag, subType));
            }

            foreach (Include declared in subtypes.For(contract))
            {
                if (owners.TryGetValue(declared.Tag, out INamedTypeSymbol taken))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            WProtoDiagnostics.DuplicateSubtypeTag,
                            declared.SubType.Locations.FirstOrDefault(),
                            declared.SubType.Name,
                            taken.Name,
                            declared.Tag,
                            contract.Name
                        )
                    );
                    failed = true;
                    continue;
                }

                if (reserved.ReservesNumber(declared.Tag))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            WProtoDiagnostics.BadSubtype,
                            declared.SubType.Locations.FirstOrDefault(),
                            declared.SubType.Name,
                            SubtypeMap.Written(contract, declared.Tag, declared.TagFromManifest),
                            ReservedMap.ReservedProblem(declared.Tag, contract.Name)
                        )
                    );
                    failed = true;
                    continue;
                }

                if (!claimed.Add(declared.Tag))
                {
                    context.ReportDiagnostic(
                        Diagnostic.Create(
                            WProtoDiagnostics.BadSubtype,
                            declared.SubType.Locations.FirstOrDefault(),
                            declared.SubType.Name,
                            SubtypeMap.Written(contract, declared.Tag, declared.TagFromManifest),
                            "field number "
                                + declared.Tag
                                + " is already claimed by a member of '"
                                + contract.Name
                                + "'"
                        )
                    );
                    failed = true;
                    continue;
                }

                owners[declared.Tag] = declared.SubType;
                includes.Add(declared);
            }

            // Direct sibling types are mutually exclusive; tag ordering makes emitted dispatch deterministic.
            includes.Sort((left, right) => left.Tag.CompareTo(right.Tag));

            return failed ? null : includes;
        }

        /// <summary>
        /// Emits the runtime-type dispatch chain shared by measuring and writing.
        /// </summary>
        private static void EmitIncludeDispatch(
            Writer writer,
            INamedTypeSymbol contract,
            string qualified,
            List<Include> includes,
            System.Func<Include, string> body
        )
        {
            /*
             * Leaf contracts also need runtime-subtype guards; sealed classes and structs cannot have
             * undeclared descendants.
             */
            bool guard = !contract.IsValueType && !contract.IsSealed;
            if (includes.Count == 0 && !guard)
            {
                return;
            }

            bool first = true;
            foreach (Include include in includes)
            {
                writer.Line(
                    (first ? "if (" : "else if (")
                        + "value is "
                        + include.Qualified
                        + " "
                        + include.Local
                        + ")"
                        + Writer.Open
                );
                writer.Indent();

                string emitted = body(include);
                if (emitted.StartsWith("if (", System.StringComparison.Ordinal))
                {
                    writer.Line(emitted + Writer.Open);
                    writer.Indent();
                    writer.Line("return false;");
                    writer.Outdent();
                    writer.Line("}");
                }
                else
                {
                    writer.Line(emitted);
                }

                writer.Outdent();
                writer.Line("}");
                first = false;
            }

            if (guard)
            {
                // Undeclared subtypes must fail instead of silently losing identity under an ancestor tag.
                writer.Line(
                    (first ? "if (" : "else if (")
                        + "value != null && value.GetType() != typeof("
                        + qualified
                        + "))"
                        + Writer.Open
                );
                writer.Indent();
                writer.Line(
                    "throw "
                        + Proto
                        + ".WProtoFormatterProvider.UnexpectedSubtype(typeof("
                        + qualified
                        + "), value.GetType());"
                );
                writer.Outdent();
                writer.Line("}");
            }

            writer.Blank();
        }

        private static List<Member> CollectMembers(
            GeneratorExecutionContext context,
            INamedTypeSymbol contract,
            SurrogateMap surrogates,
            NestedCollections nested
        )
        {
            List<Member> members = new List<Member>();
            Dictionary<int, string> claimed = new Dictionary<int, string>();
            ReservedMap reserved = ReservedMap.Build(contract);
            bool failed = false;

            foreach (ISymbol symbol in contract.GetMembers())
            {
                AttributeData attribute = FindAttribute(symbol, MemberAttribute);
                if (attribute == null || HasAttribute(symbol, IgnoreAttribute))
                {
                    continue;
                }

                ITypeSymbol type;
                bool assignable;
                if (symbol is IFieldSymbol field)
                {
                    type = field.Type;
                    assignable = !field.IsReadOnly && !field.IsConst;
                }
                else if (symbol is IPropertySymbol property)
                {
                    type = property.Type;
                    assignable = property.SetMethod != null;
                }
                else
                {
                    continue;
                }

                int tag =
                    attribute.ConstructorArguments.Length == 0
                        ? 0
                        : (int)(attribute.ConstructorArguments[0].Value ?? 0);
                if (tag < 1 || 536870911 < tag || (19000 <= tag && tag <= 19999))
                {
                    Report(
                        context,
                        WProtoDiagnostics.TagOutOfRange,
                        symbol,
                        contract.Name,
                        symbol.Name,
                        tag
                    );
                    failed = true;
                    continue;
                }

                if (claimed.TryGetValue(tag, out string other))
                {
                    Report(
                        context,
                        WProtoDiagnostics.DuplicateTag,
                        symbol,
                        contract.Name,
                        other,
                        symbol.Name,
                        tag
                    );
                    failed = true;
                    continue;
                }

                /*
                 * Prefer live-collision diagnostics, then check reservations against the schema name rather
                 * than the C# identifier.
                 */
                string schemaName = SchemaNameOf(attribute) ?? symbol.Name;
                bool reservedName = reserved.ReservesName(schemaName);
                if (reserved.ReservesNumber(tag) || reservedName)
                {
                    Report(
                        context,
                        WProtoDiagnostics.ReservedTag,
                        symbol,
                        contract.Name,
                        symbol.Name,
                        reserved.ReservesNumber(tag)
                            ? reservedName
                                ? "field number " + tag + " and the name '" + schemaName + "'"
                                : "field number " + tag
                            : "the name '" + schemaName + "'"
                    );
                    failed = true;
                    continue;
                }

                bool zigZag = AsksForZigZag(attribute);
                if (zigZag && !Shape.SupportsZigZag(type))
                {
                    Report(
                        context,
                        WProtoDiagnostics.DataFormatNotApplicable,
                        symbol,
                        contract.Name,
                        symbol.Name,
                        TypeNaming.Display(type)
                    );
                    failed = true;
                    continue;
                }

                int depthRefusals = nested.DepthRefusals;
                Member member = Member.Create(
                    contract.Name,
                    symbol.Name,
                    tag,
                    type,
                    NamedFlag(attribute, "IsRequired"),
                    NamedFlag(attribute, "OverwriteList"),
                    zigZag,
                    surrogates,
                    nested,
                    out bool ambiguous
                );
                if (member == null)
                {
                    /*
                     * Depth refusals need a distinct diagnostic because the collection shape itself may be
                     * supported.
                     */
                    Report(
                        context,
                        ambiguous ? WProtoDiagnostics.AmbiguousListContract
                            : depthRefusals < nested.DepthRefusals
                                ? WProtoDiagnostics.NestedCollectionTooDeep
                            : WProtoDiagnostics.UnsupportedMemberType,
                        symbol,
                        contract.Name,
                        symbol.Name,
                        TypeNaming.Display(type)
                    );
                    failed = true;
                    continue;
                }

                member.DeclaredType = type.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat
                );
                member.RequiresConstruction = !assignable;
                claimed[tag] = symbol.Name;
                members.Add(member);
            }

            return failed ? null : members;
        }

        private static Hooks CollectHooks(
            GeneratorExecutionContext context,
            INamedTypeSymbol contract
        )
        {
            Hooks hooks = new Hooks();
            bool failed = false;
            INamedTypeSymbol root = RootContract(contract);

            foreach (ISymbol symbol in contract.GetMembers())
            {
                if (!(symbol is IMethodSymbol method))
                {
                    continue;
                }

                string kind = null;
                if (HasAttribute(method, BeforeSerialization))
                {
                    kind = nameof(Hooks.BeforeSerialization);
                }
                else if (HasAttribute(method, AfterSerialization))
                {
                    kind = nameof(Hooks.AfterSerialization);
                }
                else if (HasAttribute(method, BeforeDeserialization))
                {
                    kind = nameof(Hooks.BeforeDeserialization);
                }
                else if (HasAttribute(method, AfterDeserialization))
                {
                    kind = nameof(Hooks.AfterDeserialization);
                }

                if (kind == null)
                {
                    continue;
                }

                if (method.IsStatic || 0 < method.Parameters.Length)
                {
                    Report(
                        context,
                        WProtoDiagnostics.HookSignature,
                        method,
                        contract.Name,
                        method.Name
                    );
                    failed = true;
                    continue;
                }

                if (!hooks.Assign(kind, method.Name))
                {
                    Report(context, WProtoDiagnostics.DuplicateHook, method, contract.Name, kind);
                    failed = true;
                    continue;
                }

                /*
                 * Only root hooks agree across this generator and both oracle majors; subtype hooks differ in
                 * presence and order.
                 */
                if (root != null)
                {
                    Report(
                        context,
                        WProtoDiagnostics.HookOnSubtype,
                        method,
                        contract.Name,
                        method.Name,
                        root.Name
                    );
                }
            }

            return failed ? null : hooks;
        }

        private static void Report(
            GeneratorExecutionContext context,
            DiagnosticDescriptor descriptor,
            ISymbol symbol,
            params object[] arguments
        )
        {
            context.ReportDiagnostic(
                Diagnostic.Create(descriptor, symbol.Locations.FirstOrDefault(), arguments)
            );
        }

        /// <summary>
        /// The schema name a member declared for itself, or <c>null</c> when it declared none.
        /// </summary>
        /// <param name="attribute">The member's <c>[WProtoMember]</c>.</param>
        /// <returns>The declared name, or <c>null</c>.</returns>
        /// <remarks>
        /// Never written to the wire -- protobuf identifies fields by number -- but it is what a
        /// generated schema, a payload dump and anything matching by name see, which is exactly
        /// what a reserved name protects.
        /// </remarks>
        private static string SchemaNameOf(AttributeData attribute)
        {
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Key == "Name" && argument.Value.Value is string declared)
                {
                    return string.IsNullOrEmpty(declared) ? null : declared;
                }
            }

            return null;
        }

        /// <summary>
        /// Reports whether the attribute asks for <c>DataFormat = ZigZag</c>.
        /// </summary>
        /// <remarks>
        /// The member's value is read off the enum's <b>own declaration</b> rather than compared
        /// against a constant here. An enum argument arrives as its underlying integer, so a
        /// hard-coded <c>1</c> would work right up until someone renumbered
        /// <c>WProtoDataFormat</c> -- after which every annotated member would silently go back to
        /// writing <c>int32</c>, which is a different payload and not a build error. The generator
        /// cannot reference the runtime assembly, but it can read the symbol the argument is typed
        /// as, which is the same declaration.
        /// </remarks>
        private static bool AsksForZigZag(AttributeData attribute)
        {
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Key != "DataFormat" || argument.Value.Value == null)
                {
                    continue;
                }

                if (argument.Value.Type is not INamedTypeSymbol format)
                {
                    continue;
                }

                foreach (ISymbol member in format.GetMembers("ZigZag"))
                {
                    if (
                        member is IFieldSymbol { HasConstantValue: true } declared
                        && Equals(declared.ConstantValue, argument.Value.Value)
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool NamedFlag(AttributeData attribute, string name)
        {
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Key == name && argument.Value.Value is bool flag)
                {
                    return flag;
                }
            }

            return false;
        }

        private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol contract)
        {
            foreach (IMethodSymbol constructor in contract.InstanceConstructors)
            {
                if (constructor.Parameters.Length == 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reports whether the contract, or any type enclosing it, takes type parameters.
        /// </summary>
        /// <remarks>
        /// The enclosing types matter as much as the contract: the formatter is emitted by reopening
        /// every one of them as <c>partial</c>, and a reopened declaration that drops its type
        /// parameters does not compile. A diagnostic beats a generated file that breaks the build
        /// somewhere the developer never wrote.
        /// </remarks>
        private static bool IsGenericAnywhere(INamedTypeSymbol symbol)
        {
            for (
                INamedTypeSymbol current = symbol;
                current != null;
                current = current.ContainingType
            )
            {
                if (current.IsGenericType)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPartialEverywhere(INamedTypeSymbol symbol)
        {
            for (
                INamedTypeSymbol current = symbol;
                current != null;
                current = current.ContainingType
            )
            {
                bool partial = false;
                foreach (SyntaxReference reference in current.DeclaringSyntaxReferences)
                {
                    if (
                        reference.GetSyntax() is TypeDeclarationSyntax declaration
                        && declaration.Modifiers.Any(modifier => modifier.ValueText == "partial")
                    )
                    {
                        partial = true;
                        break;
                    }
                }

                if (!partial)
                {
                    return false;
                }
            }

            return true;
        }

        private static string KeywordFor(INamedTypeSymbol symbol)
        {
            foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is RecordDeclarationSyntax)
                {
                    return "record";
                }
            }

            return symbol.IsValueType ? "struct" : "class";
        }

        private static string FileNameFor(INamedTypeSymbol contract)
        {
            return Sanitize(contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                + ".WProtoFormatter.g.cs";
        }

        private static string Sanitize(string value)
        {
            StringBuilder builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                builder.Append(char.IsLetterOrDigit(character) ? character : '_');
            }

            return builder.ToString();
        }

        private static bool HasAttribute(ISymbol symbol, string fullName)
        {
            return FindAttribute(symbol, fullName) != null;
        }

        private static AttributeData FindAttribute(ISymbol symbol, string fullName)
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (
                    attribute.AttributeClass != null
                    && attribute.AttributeClass.ToDisplayString() == fullName
                )
                {
                    return attribute;
                }
            }

            return null;
        }

        /// <summary>
        /// Decides whether a type with no <c>[WProtoContract]</c> is one protobuf-net would
        /// serialize, and reports which discriminator matched it.
        /// </summary>
        /// <param name="compilation">The consumer compilation being generated for.</param>
        /// <param name="symbol">The candidate type.</param>
        /// <param name="referencesProtobufNet">
        /// Memo for the compilation-wide protobuf-net probe, computed on first use.
        /// </param>
        /// <param name="contract">The attribute the diagnostic should be reported at.</param>
        /// <param name="matchedBecause">
        /// The discriminator, phrased for the diagnostic message, so a consumer who disagrees knows
        /// what to suppress and why.
        /// </param>
        /// <returns><c>true</c> when WPROTO030 should be reported for <paramref name="symbol"/>.</returns>
        /// <remarks>
        /// <para>
        /// Three shapes count. The second and third exist because a survey of four public Unity
        /// consumers found 80 of 485 protobuf-net contracts (16.5%) that an exact match on
        /// <c>ProtoBuf.ProtoContractAttribute</c> could not see -- every contract in the most
        /// prominent of them.
        /// </para>
        /// <para>
        /// <c>[ProtoBuf.ProtoContract]</c> is protobuf-net's own attribute and needs no
        /// corroboration. A <c>ProtoContractAttribute</c> in any OTHER namespace counts only when
        /// that same namespace also declares an attribute named <c>ProtoMemberAttribute</c>:
        /// vendoring protobuf-net under a renamed namespace moves the namespace and keeps the whole
        /// vocabulary, while one type that happens to share a name is evidence of nothing.
        /// </para>
        /// <para>
        /// <c>[DataContract]</c> is not evidence on its own -- it is equally
        /// <c>DataContractSerializer</c>'s, <c>DataContractJsonSerializer</c>'s and WCF's attribute
        /// -- so it counts only when BOTH discriminators hold: the compilation actually references
        /// protobuf-net, and at least one member declares <c>[DataMember(Order = n)]</c>.
        /// protobuf-net requires that order because it is the field number; WCF does not use it for
        /// wire identity and most WCF contracts omit it. Requiring both is what keeps a WCF-only
        /// project silent, and a false WPROTO030 on a WCF type would break the family's promise that
        /// a <c>WPROTO###</c> names a serialization contract that cannot be honoured.
        /// </para>
        /// </remarks>
        private static bool TryFindUnportedProtobufContract(
            Compilation compilation,
            INamedTypeSymbol symbol,
            ref bool? referencesProtobufNet,
            out AttributeData contract,
            out string matchedBecause
        )
        {
            foreach (AttributeData candidate in symbol.GetAttributes())
            {
                INamedTypeSymbol attributeClass = candidate.AttributeClass;
                if (
                    attributeClass == null
                    || !string.Equals(
                        attributeClass.Name,
                        ProtobufContractAttributeName,
                        StringComparison.Ordinal
                    )
                )
                {
                    continue;
                }

                string display = attributeClass.ToDisplayString();
                if (string.Equals(display, ProtobufContractAttribute, StringComparison.Ordinal))
                {
                    contract = candidate;
                    matchedBecause =
                        "it carries ["
                        + ProtobufContractAttribute
                        + "], protobuf-net's own contract attribute";
                    return true;
                }

                if (DeclaresProtobufVocabulary(attributeClass.ContainingNamespace))
                {
                    contract = candidate;
                    matchedBecause =
                        "it carries ["
                        + display
                        + "], and that namespace also declares an attribute named "
                        + ProtobufMemberAttributeName
                        + ", which is protobuf-net's vocabulary vendored under a renamed namespace";
                    return true;
                }
            }

            AttributeData dataContract = FindAttribute(symbol, DataContractAttributeName);
            if (dataContract == null || !HasOrderedDataMember(symbol))
            {
                contract = null;
                matchedBecause = null;
                return false;
            }

            referencesProtobufNet = referencesProtobufNet ?? ReferencesProtobufNet(compilation);
            if (referencesProtobufNet != true)
            {
                contract = null;
                matchedBecause = null;
                return false;
            }

            contract = dataContract;
            matchedBecause =
                "it carries ["
                + DataContractAttributeName
                + "] with at least one ["
                + DataMemberAttributeName
                + "] member declaring "
                + DataMemberOrder
                + ", which is the protobuf-net contract style, and this compilation references protobuf-net";
            return true;
        }

        /// <summary>
        /// Whether a namespace declares protobuf-net's member attribute, the corroboration a
        /// renamed <c>ProtoContractAttribute</c> needs before it is believed.
        /// </summary>
        /// <param name="candidate">The namespace declaring the contract attribute.</param>
        /// <returns><c>true</c> when the namespace carries protobuf-net's vocabulary.</returns>
        private static bool DeclaresProtobufVocabulary(INamespaceSymbol candidate)
        {
            if (candidate == null || candidate.IsGlobalNamespace)
            {
                return false;
            }

            foreach (
                INamedTypeSymbol member in candidate.GetTypeMembers(ProtobufMemberAttributeName)
            )
            {
                if (member.Arity == 0 && IsAttributeType(member))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a type derives from <see cref="Attribute"/>.
        /// </summary>
        /// <param name="candidate">The type to walk.</param>
        /// <returns><c>true</c> when the type is an attribute.</returns>
        private static bool IsAttributeType(INamedTypeSymbol candidate)
        {
            for (INamedTypeSymbol current = candidate; current != null; current = current.BaseType)
            {
                if (
                    string.Equals(
                        current.ToDisplayString(),
                        AttributeBaseName,
                        StringComparison.Ordinal
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether any member of a <c>[DataContract]</c> declares an explicit
        /// <c>[DataMember(Order = n)]</c>, which protobuf-net requires and WCF usually omits.
        /// </summary>
        /// <param name="symbol">The contract type.</param>
        /// <returns><c>true</c> when at least one member states an order.</returns>
        private static bool HasOrderedDataMember(INamedTypeSymbol symbol)
        {
            foreach (ISymbol member in symbol.GetMembers())
            {
                AttributeData dataMember = FindAttribute(member, DataMemberAttributeName);
                if (dataMember == null)
                {
                    continue;
                }

                foreach (KeyValuePair<string, TypedConstant> argument in dataMember.NamedArguments)
                {
                    if (string.Equals(argument.Key, DataMemberOrder, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Whether protobuf-net is on the compilation at all, under its own namespace or a vendored
        /// rename.
        /// </summary>
        /// <param name="compilation">The consumer compilation.</param>
        /// <returns><c>true</c> when protobuf-net's attribute vocabulary is reachable.</returns>
        /// <remarks>
        /// The namespace walk is gated behind <see cref="IAssemblySymbol.TypeNames"/>, a hashed
        /// lookup, so the recursion only ever runs for an assembly that already declares both names.
        /// </remarks>
        private static bool ReferencesProtobufNet(Compilation compilation)
        {
            if (compilation.GetTypeByMetadataName(ProtobufContractAttribute) != null)
            {
                return true;
            }

            foreach (IAssemblySymbol assembly in EnumerateAssemblies(compilation))
            {
                ICollection<string> names = assembly.TypeNames;
                if (
                    !names.Contains(ProtobufContractAttributeName)
                    || !names.Contains(ProtobufMemberAttributeName)
                )
                {
                    continue;
                }

                if (DeclaresProtobufVocabularyAnywhere(assembly.GlobalNamespace))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<IAssemblySymbol> EnumerateAssemblies(Compilation compilation)
        {
            yield return compilation.Assembly;

            foreach (MetadataReference reference in compilation.References)
            {
                if (compilation.GetAssemblyOrModuleSymbol(reference) is IAssemblySymbol assembly)
                {
                    yield return assembly;
                }
            }
        }

        private static bool DeclaresProtobufVocabularyAnywhere(INamespaceSymbol candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            if (
                !candidate.IsGlobalNamespace
                && DeclaresProtobufVocabulary(candidate)
                && candidate
                    .GetTypeMembers(ProtobufContractAttributeName)
                    .Any(member => member.Arity == 0 && IsAttributeType(member))
            )
            {
                return true;
            }

            foreach (INamespaceSymbol nested in candidate.GetNamespaceMembers())
            {
                if (DeclaresProtobufVocabularyAnywhere(nested))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One contract's generated source, held until it is known whether it may be published.
        /// </summary>
        private sealed class Emission
        {
            internal Emission(INamedTypeSymbol contract, string source, string registration)
            {
                Contract = contract;
                Source = source;
                Registration = registration;
            }

            internal INamedTypeSymbol Contract { get; }

            internal string Source { get; }

            internal string Registration { get; }
        }

        private sealed class Receiver : ISyntaxReceiver
        {
            internal List<TypeDeclarationSyntax> Types { get; } = new List<TypeDeclarationSyntax>();

            /// <summary>
            /// Class declarations that name a base and carry no attribute of their own.
            /// </summary>
            /// <remarks>
            /// Kept apart from <see cref="Types"/> because it is the larger list by far -- in a
            /// Unity project every MonoBehaviour is in it -- and the only question asked of it is
            /// whether the base is a <c>[WProtoContract]</c>
            /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/613">#613</see>).
            /// A subclass with an attribute is already in <see cref="Types"/>; this list exists
            /// because the fixture that fails at run time is the one written with no attributes at
            /// all, which nothing here used to look at.
            /// </remarks>
            internal List<TypeDeclarationSyntax> Derived { get; } =
                new List<TypeDeclarationSyntax>();

            /// <summary>
            /// Enum declarations carrying at least one attribute.
            /// </summary>
            /// <remarks>
            /// An <c>EnumDeclarationSyntax</c> is a <c>BaseTypeDeclarationSyntax</c> and NOT a
            /// <c>TypeDeclarationSyntax</c>, so nothing above ever saw one -- which is why
            /// <c>[WProtoReserved]</c> on an enum needed a collection of its own rather than a
            /// widened filter
            /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/609">#609</see>).
            /// </remarks>
            internal List<EnumDeclarationSyntax> Enums { get; } = new List<EnumDeclarationSyntax>();

            public void OnVisitSyntaxNode(SyntaxNode node)
            {
                if (node is EnumDeclarationSyntax enumeration)
                {
                    if (0 < enumeration.AttributeLists.Count)
                    {
                        Enums.Add(enumeration);
                    }

                    return;
                }

                if (
                    node is TypeDeclarationSyntax declaration
                    && 0 < declaration.AttributeLists.Count
                )
                {
                    Types.Add(declaration);
                }
                else if (
                    node is TypeDeclarationSyntax bare
                    && bare.Members.OfType<MethodDeclarationSyntax>()
                        .Any(m => 0 < m.AttributeLists.Count)
                )
                {
                    Types.Add(bare);
                }
                else if (node is TypeDeclarationSyntax derived && derived.BaseList != null)
                {
                    Derived.Add(derived);
                }
            }
        }

        private sealed class Hooks
        {
            internal string BeforeSerialization { get; private set; }
            internal string AfterSerialization { get; private set; }
            internal string BeforeDeserialization { get; private set; }
            internal string AfterDeserialization { get; private set; }

            internal bool Any =>
                BeforeSerialization != null
                || AfterSerialization != null
                || BeforeDeserialization != null
                || AfterDeserialization != null;

            internal bool Assign(string kind, string methodName)
            {
                switch (kind)
                {
                    case nameof(BeforeSerialization):
                    {
                        if (BeforeSerialization != null)
                        {
                            return false;
                        }

                        BeforeSerialization = methodName;
                        return true;
                    }
                    case nameof(AfterSerialization):
                    {
                        if (AfterSerialization != null)
                        {
                            return false;
                        }

                        AfterSerialization = methodName;
                        return true;
                    }
                    case nameof(BeforeDeserialization):
                    {
                        if (BeforeDeserialization != null)
                        {
                            return false;
                        }

                        BeforeDeserialization = methodName;
                        return true;
                    }
                    default:
                    {
                        if (AfterDeserialization != null)
                        {
                            return false;
                        }

                        AfterDeserialization = methodName;
                        return true;
                    }
                }
            }
        }
    }
}
