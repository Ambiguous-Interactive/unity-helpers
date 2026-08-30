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
        private const string BeforeSerialization =
            AttributeNamespace + ".WProtoBeforeSerializationAttribute";
        private const string AfterSerialization =
            AttributeNamespace + ".WProtoAfterSerializationAttribute";
        private const string BeforeDeserialization =
            AttributeNamespace + ".WProtoBeforeDeserializationAttribute";
        private const string AfterDeserialization =
            AttributeNamespace + ".WProtoAfterDeserializationAttribute";

        private const string Proto = "global::" + AttributeNamespace;

        // protobuf-net's own names, as string literals rather than typeof(): the generator
        // deliberately does not reference protobuf-net -- an analyzer that did would drag it into
        // every consumer's compiler -- and the vendored-rename case has no compile-time name at
        // all, because a rename moves the namespace and keeps only the type name.
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

            // Answered at most once per compilation: the search walks referenced assemblies, and a
            // [DataContract]-heavy compilation would otherwise ask it per type.
            bool? referencesProtobufNet = null;

            // Read before any subtype declaration is looked at, because a tag-less one is only
            // meaningful against it. Validated here as well so a corrupt entry is reported once,
            // at the entry, rather than once per type that happened to depend on it.
            SubtypeTagManifest manifest = SubtypeTagManifest.Build(context.Compilation);
            SubtypeTagManifest.Validate(context.Compilation, context.ReportDiagnostic);
            bool editorCompilation = IsEditorCompilation(context);

            foreach (TypeDeclarationSyntax declaration in receiver.Types)
            {
                SemanticModel model = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                if (!(model.GetDeclaredSymbol(declaration) is INamedTypeSymbol symbol))
                {
                    continue;
                }

                if (!seen.Add(symbol))
                {
                    continue;
                }

                bool isContract = HasAttribute(symbol, ContractAttribute);
                if (isContract)
                {
                    contracts.Add(symbol);
                    continue;
                }

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

                // A [WProtoSubtype] here names a base that will never write it: nothing is
                // generated for a type with no contract of its own.
                SubtypeMap.Validate(
                    context.ReportDiagnostic,
                    symbol,
                    true,
                    manifest,
                    editorCompilation
                );
            }

            // Built before anything is emitted, because the base's formatter has to carry the
            // includes its subtypes declared and a base is routinely compiled before them.
            SubtypeMap subtypes = SubtypeMap.Build(contracts, manifest);

            SurrogateMap surrogates = SurrogateMap.Build(context.Compilation);
            SurrogateMap.Validate(context.Compilation, context.ReportDiagnostic);

            MarshalMap marshals = MarshalMap.Build(context.Compilation);
            MarshalMap.Validate(context.Compilation, context.ReportDiagnostic);

            JsonConverterMap jsonConverters = JsonConverterMap.Build(context.Compilation);
            JsonConverterMap.Validate(context.Compilation, context.ReportDiagnostic);

            DeclaredRootMap.Validate(context.Compilation, context.ReportDiagnostic);

            // Shared across every scan below: a closure the registrar cannot name is one missing
            // registration however many scans trip over it.
            HashSet<string> announced = new HashSet<string>();

            List<string> registrations = new List<string>();
            HashSet<INamedTypeSymbol> emittedContracts = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            HashSet<INamedTypeSymbol> enumClosures = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );
            // THE INVARIANT: no published formatter names a formatter that was not published.
            //
            // Only two kinds of generated code hard-name another contract's nested formatter, and
            // they run in OPPOSITE directions: a base's include dispatch (and the CanWrite built
            // from the same list) names each subtype's, and a subtype's root formatter names the
            // ROOT's. Everything else goes through WProtoFormatterProvider.Get<T>(), which is a
            // generic call that compiles whether or not T got a formatter -- measured, and it is
            // why a refused contract held as a MEMBER of another was never a problem.
            //
            // A WPROTO### already fails the build, so nothing ships from a compilation that has
            // one. Withholding therefore has exactly one job: stop a CS error inside generated code
            // burying the diagnostic that says what to fix. It must withhold what would name a
            // missing formatter and NOTHING else -- withdrawing a whole hierarchy over one refused
            // sibling unpublishes every valid formatter in it, which for AbstractRandom's 21
            // subtypes means a consumer's one undeclared subclass silently drops all 21 onto the
            // reflection path that does not run under IL2CPP.
            //
            // So: a refused contract is withheld; so is anything whose ROOT is refused, because of
            // that reverse reference; and every contract that IS published has its include set
            // filtered to drop entries naming a withheld type. Contracts are emitted deepest-first
            // precisely so that filter can be applied as the source is written -- an include names
            // a DIRECT subtype, so by the time a base is reached every one of its subtypes has
            // already been decided.
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
                // The reverse edge, and it reaches further than the root: CanServe walks EVERY
                // contract from this one up the chain and names each one's formatter, so a refused
                // ancestor at any level is a name this would emit. It binds anyway -- a nested type
                // is inherited, so `Middle.WProtoFormatter` silently resolves to `Root`'s and the
                // chain asks the root twice instead of the missing level. Measured: no CS error,
                // which is worse than one, because only the build failing on the real WPROTO error
                // stops it. Withhold the descendants instead; siblings are untouched either way.
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
                    // A generic subtype's closures need the same entry point a non-generic one gets.
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

            // A registrar of its own rather than a block in that one. JSON and protobuf are
            // independent choices -- either can be switched off with a define, and a build using
            // neither should emit no file at all -- so a single registrar would make one feature's
            // opt-out delete the other's registrations.
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
                // A host that supplies none cannot be shown to be the editor, and the safe answer
                // for an unnumbered subtype is the one that refuses to ship it.
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

                // The mistake this catches shipped inert for two years in Runtime/Tags/Attribute.cs
                // (#370): an attribute that advertises a hook nothing is wired to call.
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

            // Before anything else this contract might report, because a subtype declaration the
            // map refused is missing from the base's include set, and every later diagnostic
            // would then describe a consequence rather than the cause.
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

            // A contract nested INSIDE a generic type is still refused, and the reason is
            // registration rather than emission. `Holder<T>.Inner` is not itself generic, so there is
            // no construction of it to scan for -- the closures live on the enclosing type, and a
            // registrar that cannot name `Holder<int>.Inner` would emit a formatter nothing ever
            // registers. A refusal is better than a formatter that silently never resolves.
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

            // Two reasons a member reads into a local and is committed after the loop. A polymorphic
            // contract can have its instance replaced by an include tag, which protobuf-net allows
            // in either position. And a contract with a `readonly` member cannot be assigned at all
            // -- it has to be BUILT -- so every value has to be in hand before construction.
            bool constructAtEnd = false;
            foreach (Member member in members)
            {
                constructAtEnd |= member.RequiresConstruction;
            }

            if (constructAtEnd && 0 < includes.Count)
            {
                // Both mechanisms want to own the instance: one replaces it when an include arrives,
                // the other cannot create it until the last member is read. Refusing beats picking.
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.ImmutableWithIncludes,
                        contract.Locations.FirstOrDefault(),
                        contract.Name
                    )
                );
                return null;
            }

            // Every diagnostic above reads the set the author DECLARED. What gets emitted is that
            // set minus the subtypes this compilation refused, so the dispatch chain and the
            // CanWrite built from it never name a formatter that was not published. Filtering after
            // the checks rather than before keeps a refused sibling from turning into a second,
            // misleading error on this type -- WPROTO014 in particular would fire on an abstract
            // contract whose subtypes were all refused elsewhere.
            int declaredIncludeCount = includes.Count;
            includes.RemoveAll(include => refused.Contains(include.SubType));

            // ...and an abstract contract with nothing left to dispatch to cannot be emitted at all:
            // the read path would have to `new` an abstract type. Withheld silently, because the
            // subtype that caused it has already reported the error worth acting on.
            if (contract.IsAbstract && includes.Count == 0 && 0 < declaredIncludeCount)
            {
                return null;
            }

            // Measured against protobuf-net 2.4.9 and 3.2.56, both the same: an immutable contract
            // is CONSTRUCTED, read into, and then its readonly members assigned by reflection -- so
            // every member the payload does not overwrite keeps what the author's constructor gave
            // it, a sub-message merges into it, a repeated member appends to it and a map merges by
            // key. Holding every value in a local that starts at `default` loses all of that in
            // silence. The seed instance costs one construction per read, so it is built only where
            // construction is possible and could actually set something.
            // Measured, and it is the exclusion that matters most: a contract declaring
            // SkipConstructor is allocated UNINITIALIZED by protobuf-net whether or not it is
            // immutable, so no constructor runs and there is no seed at all -- a sub-message
            // replaces, and an absent scalar comes back at its type's default rather than at what
            // the constructor would have set. `PcgRandom` is exactly this shape, and seeding it
            // would run `Guid.NewGuid()` on every read to produce an answer the oracle disagrees
            // with.
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

            // SkipConstructor means no constructor the author wrote may run. Only meaningful for a
            // reference type that is created here at all: a struct has no constructor to skip, an
            // abstract contract creates nothing, and one that builds itself never calls `new`.
            //
            // This is what the AUTHOR asked for, and it is the flag every SEEDING decision reads:
            // protobuf-net honours it by allocating the instance uninitialized, so no field
            // initializer has run and no member has anything to merge into or append to.
            bool declaredSkipConstructor =
                Shape.SkipsConstructor(contract)
                && !contract.IsValueType
                && !contract.IsAbstract
                && !constructAtEnd;

            // A narrower, separate question: whether a private constructor is emitted and called.
            // A type that declares none must not get one -- emitting ANY constructor into it removes
            // the implicit parameterless one, so `new Theirs()` stops compiling in the consumer's own
            // source, an attribute silently breaking unrelated code.
            //
            // These two were ONE flag until session 202, on the reasoning that a type declaring no
            // constructor has nothing to skip, because the implicit one runs field initializers and
            // nothing else. That is true of what this generator emits and false of what the oracle
            // does -- an uninitialized allocation runs no initializer at all -- so every such
            // contract had been seeding its members from initializers the oracle never had.
            bool skipConstructor = declaredSkipConstructor && DeclaresAConstructor(contract);

            foreach (Member member in members)
            {
                member.SkipConstructor = declaredSkipConstructor;
            }

            ReportInitializersSkipConstructorDiscards(context, contract);

            // Not asked of a contract that builds itself. The diagnostic exists because the formatter
            // normally calls `new T()` to have something to read into; a contract with a member that
            // cannot be assigned after construction never takes that path, holding every value in a
            // local and passing them to the constructor emitted just below. Requiring a parameterless
            // constructor as well rejected the canonical immutable class -- one parameterized
            // constructor, all-readonly members -- for a reason that had stopped applying to it.
            // SkipConstructor is the same argument: the instance comes from a constructor emitted
            // here, so what the author declared is not consulted.
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

            // A subtype's ENTRY POINT is not the formatter that writes its own members. Measured
            // against protobuf-net 3.2.56: serializing a subtype under its own declared type produces
            // exactly the bytes serializing it as its base does -- the include wrapping its members,
            // then the base's members. Registering the own-members formatter wrote only the subtype's
            // half, which protobuf-net then read as the BASE's fields, silently and with no error.
            INamedTypeSymbol root = RootContract(contract);

            // A subtype is written as its base writes it, so the base has to have a tag to write it
            // under. Without the declaration there is none: serializing one reaches the base's
            // dispatch chain, matches no branch, and fails at run time in a shipped player. The
            // alternative -- writing this type's members alone -- is what protobuf-net would read
            // back as the BASE's fields, so refusing is the only answer that is not silently wrong.
            //
            // Either end may declare it. A [WProtoSubtype] that is present but unusable has
            // already been refused above, so asking only whether one was WRITTEN keeps the error
            // on the declaration the developer got wrong instead of adding a second one saying
            // nothing declared the relationship.
            if (
                root != null
                && !DeclaresInclude(contract.BaseType, contract)
                && !SubtypeMap.Declares(contract)
            )
            {
                context.ReportDiagnostic(
                    Diagnostic.Create(
                        WProtoDiagnostics.SubtypeNotIncluded,
                        contract.Locations.FirstOrDefault(),
                        contract.Name,
                        contract.BaseType.Name
                    )
                );
                return null;
            }

            string entryPoint =
                root == null ? ".WProtoFormatter.Instance" : ".WProtoRootFormatter.Instance";

            // An open generic has no formatter to register; each closed construction the compilation
            // actually uses gets one. That scan is what makes `Deque<TheirStruct>` work at the
            // CONSUMER's build, which is the property this whole generator was chosen for.
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
            // A contract with an instance to assign onto can be merged into, which is what makes the
            // FIRST occurrence of a sub-message field keep whatever its constructor seeded --
            // protobuf's MergeFrom semantics, and what protobuf-net does. The two exclusions have no
            // instance to merge into at all: a contract built by a constructor at the end of the
            // read has none until the last value is in hand, and one whose instance an include tag
            // chooses may not be this type. SkipConstructor is NOT one of them -- it decides how an
            // instance is CREATED, and a caller that already holds one is not creating anything.
            bool mergeable = !constructAtEnd && includes.Count == 0 && !contract.IsAbstract;

            // SkipConstructor suppresses seeding only for the instance THIS formatter creates. A
            // mergeable formatter can also be handed one, and that one is the caller's -- so the
            // decision moves to run time. `SkipConstructor` is already the declared flag, so it is
            // read back here rather than recomputed.
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

            // Last, and nested inside this formatter rather than beside it: a wrapper message exists
            // only to give one of this contract's members an encoding, it is never looked up by
            // type, and keeping it here is what lets two contracts each hold a List<int[]> without
            // colliding over a generated name.
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

            // The BASE CHAIN, not just the contract. An uninitialized allocation zeroes the whole
            // object, inherited fields included, so a base's declaration initializer is dropped
            // exactly as the contract's own is. That is the shape this diagnostic was written for
            // and the one it originally missed: `AbstractRandom._guidBytes` is declared on the base
            // while `SkipConstructor` sits on each of the twelve concrete generators, so a check
            // that asks only the contract would never have reported the defect it exists to catch.
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

                // An auto-property's backing field carries the initializer and is what the
                // uninitialized allocation leaves at its default, so the property it belongs to is
                // what the developer has to be pointed at. Its own attributes are the ones that
                // matter, including the [WProtoMember] that would put it on the wire.
                // ReferenceEquals, not `!=`: the question is identity -- did the null-coalesce
                // above hand back the associated property rather than the field itself -- and
                // RS1024 rejects `==`/`!=` on symbols because it cannot tell that apart from a
                // value comparison across compilations.
                ISymbol declared = field.AssociatedSymbol ?? field;
                if (!ReferenceEquals(declared, field) && HasAttribute(declared, MemberAttribute))
                {
                    continue;
                }

                // The property's references, not the backing field's: an auto-property's backing
                // field is implicitly declared and has none, so asking it reports every such
                // property as clean.
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

            // Asked of the ENTRY POINT, answered by EVERY contract from this one up to the chain
            // root, because serializing this declared type writes all of their members: the include
            // holding this type's, then each ancestor's. Asking only the root missed a generic
            // SUBTYPE's own parameters, which is the half that fails inside `Measure`.
            //
            // Each formatter is asked rather than re-deriving the chain's encoded type parameters
            // here, which would drift the first time the chain changed.
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

                // Through `object`: each formatter is a SEALED type, so a direct pattern match
                // against an interface it does not implement is CS8121 rather than a false answer.
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
            // Both halves are load-bearing. The root's chain covers every type under the ROOT, which
            // includes this contract's siblings -- values this formatter's declared type could never
            // hold. The facade only ever asks about a value it already holds as this type, but the
            // answer has to be right on its own terms rather than because of where it is asked from.
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
            // Declaring ANY constructor removes the implicit parameterless one, so a contract whose
            // author declared none loses `new Theirs()` -- in the consumer's own source, from an
            // attribute that says nothing about constructors. protobuf-net loses the type entirely
            // at the same moment ("No parameterless constructor found"), which is the whole of the
            // WALLSTOP_PROTO-off build. Emitting the one the compiler would have is what keeps both.
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
                // First statement of Measure and never repeated in Write: a hook that projects live
                // state into serialized members has to run before the length prefix is computed, and
                // one that rents pooled scratch would leak if it ran twice.
                writer.Line("value." + hooks.BeforeSerialization + "();");
            }

            writer.Line("int size = 0;");

            // Includes first, and not in field-number order. Measured against protobuf-net 3.2.56:
            // the subtype's include field precedes every one of this contract's own members whatever
            // its tag, confirmed with an include at tag 3 emitted ahead of members at tags 1 and 5.
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
                // One body, entered two ways. TryRead is the no-seed case rather than a second copy
                // of the loop, because a duplicated read body is a duplicated wire format: the two
                // would be free to drift, and the compiled size of every contract would double for
                // a difference of one statement.
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
                // The seed IS the instance being read into, so a member the payload never mentions
                // keeps what the contract's constructor gave it. A reference seed of null is the
                // ordinary case -- nothing to merge -- and gets the instance TryRead used to make.
                writer.Line(qualified + " read = seed;");
                if (guardedSeeding)
                {
                    // Before the instance is created, because creating one is exactly what makes
                    // its members artifacts rather than seeds.
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
                // Nothing is assigned onto this -- a readonly member can only be assigned by a
                // constructor, and the one at the end of the read overwrites it. It exists so every
                // member's read local can START at what the author's constructor left there, which
                // is what protobuf-net reads into and what a merge, an append and a map union all
                // combine with. Where construction could not set anything, `default` says so and
                // costs nothing.
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
                // An abstract contract has no instance of its own; the payload's include tag is the
                // only thing that can produce one, and a payload without one is malformed rather
                // than an empty base.
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
                // Last include wins. A payload naming two sibling subtypes is nonsense either way,
                // and this is the branch where protobuf-net 3.2.56 recurses until the stack runs
                // out -- a crash that cannot be caught, from an untrusted save file. A plain
                // assignment cannot.
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
            // Forward compatibility: a payload from a newer build carries fields this one has no
            // member for, and they are stepped over exactly rather than guessed at.
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
                // Deliberately here rather than at the top. The hook's contract is "after the
                // instance exists and before any member is assigned", and for a polymorphic contract
                // the instance does not exist until an include tag has been seen. Every member of
                // such a contract is deferred, so nothing has been assigned yet either.
                writer.Line("read." + hooks.BeforeDeserialization + "();");
                writer.Blank();
            }

            // After the malformed check, deliberately: a collection accumulated from a payload that
            // turned out to be truncated must not be committed onto the instance the caller gets
            // back, for the same reason the after-deserialization hook does not run on a failed read.
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
                    // The instance did not exist any earlier, so this is the first moment the hook
                    // could run. Its contract -- "after the instance exists, before any member is
                    // assigned" -- cannot be honoured literally for a type whose members ARE its
                    // construction; the closest true statement is that nothing has been assigned
                    // since, because nothing can be.
                    writer.Line("read." + hooks.BeforeDeserialization + "();");
                    writer.Blank();
                }
            }

            if (hooks.AfterDeserialization != null)
            {
                // Only on a successful read: rebuilding derived state from half-populated members
                // produces a plausible-looking wrong object instead of a reported failure.
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

            // SubsystemRegistration rather than the formatter registrar's BeforeSceneLoad: a
            // converter has to be in the registry before anything deserializes settings, and
            // nothing here can be replaced by a later registration the way a formatter can.
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
            // The registrar is internal, so every generated assembly can expose the same stable
            // compile-time name without conflicting with a referenced assembly's registrar.
            writer.Line("internal static class WProtoGeneratedRegistrar" + Writer.Open);
            writer.Indent();

            writer.Line("#if UNITY_INCLUDE_TESTS");
            writer.Line(
                "internal static long FirstRegistrationElapsedTimestampTicks { get; private set; }"
            );
            writer.Line("internal static bool HasRecordedFirstRegistration { get; private set; }");
            writer.Line("#endif");

            // BeforeSceneLoad, deliberately: this package registers its built-ins at
            // SubsystemRegistration, the earlier phase, so anything generated here -- including a
            // consumer's replacement for a type this package also ships -- wins.
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

            // A separate registry, not a second batch into the same one: WProtoGeneric<T> reads the
            // formatter provider for every member whose type a closure decides, so a marshal
            // registered there would escape the root and rewrite that member's encoding.
            foreach (string marshal in rootMarshals)
            {
                writer.Line(Proto + ".WProtoRootMarshalProvider.Register(" + marshal + ");");
            }

            // Into a registry of its own, like a marshal and for the same reason: WProtoGeneric
            // reads the formatter provider for every member a closure decides, and asks it no
            // CanServe or CanWrite question, so an adapter registered there would make
            // Deque<IRandom> encodable and drop an element outside the root chain in silence.
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

                    // Recursive, not a scan of the direct arguments. `Box<Wrapper<T>>` has no type
                    // parameter among its own arguments -- `Wrapper<T>` is a named type -- yet T is
                    // still unbound, and a registrar cannot name it. Recording it as closed emitted a
                    // registration that fails the CONSUMER's build, which is a worse failure than the
                    // missing registration it was trying to avoid.
                    // Nameability is the second half of the same question. `Box<int>` is fine;
                    // `Box<SomeFixture.PrivatePayload>` is a name the registrar cannot write, and
                    // emitting it fails the build of the assembly that declared the private type.
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
            // Beside IsNameable rather than duplicated here: the two answer halves of one question
            // -- can the registrar write this name -- and the marshal and declared-root maps ask
            // both as well.
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

            // Which subtype took each field number, so a collision between the two declaration
            // forms can name both of them rather than only the one that arrived second.
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
                    // Measured: protobuf-net 3.2.56 refuses a grandchild declared on the grandparent
                    // with "Unexpected sub-type", so an include names a DIRECT subtype and a deeper
                    // type is declared on the type it actually derives from.
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
                    // The two declaration forms share one field-number space, so a rule that
                    // covered only [WProtoSubtype] would be one an author steps around by
                    // accident (#606).
                    problem = SubtypeMap.RetiredProblem(tag, contract, retiredBy);
                }
                else if (reserved.ReservesNumber(tag))
                {
                    // Checked before claimed.Add so a refused include does not spend the number it
                    // was refused for.
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

            // Direct subtypes of one type are mutually exclusive, so the chain's order cannot
            // change which branch matches; sorting by tag only makes the emitted code deterministic
            // -- and it is what makes the two declaration forms interchangeable, because the order
            // subtype declarations are discovered in is not a property of the source.
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
            // The guard is needed whether or not this contract declares includes: a subtype nobody
            // declared reaches its nearest ANNOTATED ancestor's formatter, which for a leaf contract
            // has no dispatch chain at all. A sealed class and a struct cannot be subclassed, so
            // they pay nothing.
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
                // Not a fall-through: a value whose runtime type is a subtype nothing declares would
                // otherwise be written under its nearest declared ancestor's tag and read back as
                // that ancestor -- a level of type identity gone from saved data with nothing to
                // report it. protobuf-net raises "Unexpected sub-type" on the same value.
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

                // Checked after the duplicate, because a member colliding with a LIVE sibling has a
                // fix the author can see in front of them; a collision with something deleted needs
                // the reservation explained.
                if (reserved.ReservesNumber(tag) || reserved.ReservesName(symbol.Name))
                {
                    Report(
                        context,
                        WProtoDiagnostics.ReservedTag,
                        symbol,
                        contract.Name,
                        symbol.Name,
                        reserved.ReservesNumber(tag)
                            ? reserved.ReservesName(symbol.Name)
                                ? "field number " + tag + " and the name '" + symbol.Name + "'"
                                : "field number " + tag
                            : "the name '" + symbol.Name + "'"
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
                    // "Unsupported" would be true but unhelpful for a member whose only problem is
                    // how far its collections nest: the shape IS supported, up to the depth the
                    // reader can read back, and the fix is a different one.
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

                // Measured against both oracles, and they disagree with each other, which is why
                // this is a warning rather than a behaviour change. protobuf-net 3.2.56 invokes the
                // callbacks of the type that owns the wire shape -- the ROOT of the include chain --
                // and none of a subtype's, so a hook written here is silently dead in any build the
                // fallback serves. 2.4.9 invokes every level, outermost first, where this generator
                // emits innermost first. Only the root is a moment all three agree on.
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

            public void OnVisitSyntaxNode(SyntaxNode node)
            {
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
