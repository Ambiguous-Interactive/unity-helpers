// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System.Collections.Generic;
    using System.Linq;
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

            List<INamedTypeSymbol> contracts = new List<INamedTypeSymbol>();
            HashSet<INamedTypeSymbol> seen = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );

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

                ReportOrphanedHooks(context, symbol);
            }

            SurrogateMap surrogates = SurrogateMap.Build(context.Compilation);
            SurrogateMap.Validate(context.Compilation, context.ReportDiagnostic);

            List<string> registrations = new List<string>();
            foreach (INamedTypeSymbol contract in contracts)
            {
                string source = Emit(context, contract, surrogates, out string registration);
                if (source == null)
                {
                    continue;
                }

                context.AddSource(FileNameFor(contract), SourceText.From(source, Encoding.UTF8));
                if (registration != null)
                {
                    registrations.Add(registration);
                }
                else
                {
                    // A generic subtype's closures need the same entry point a non-generic one gets.
                    string entryPoint =
                        RootContract(contract) == null
                            ? ".WProtoFormatter.Instance"
                            : ".WProtoRootFormatter.Instance";
                    foreach (string closed in ClosedConstructions(context.Compilation, contract))
                    {
                        registrations.Add(closed + entryPoint);
                    }
                }
            }

            registrations.AddRange(ForeignClosures(context.Compilation));

            if (0 < registrations.Count)
            {
                context.AddSource(
                    "WProtoGeneratedRegistrar.g.cs",
                    SourceText.From(EmitRegistrar(context, registrations), Encoding.UTF8)
                );
            }
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
        private static IEnumerable<string> ForeignClosures(Compilation compilation)
        {
            HashSet<string> found = new HashSet<string>();
            List<string> registrations = new List<string>();

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    if (!(node is TypeSyntax type))
                    {
                        continue;
                    }

                    if (
                        !(Resolve(model, type) is INamedTypeSymbol named)
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
                        || !compilation.IsSymbolAccessibleWithin(named, compilation.Assembly)
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
            out string registration
        )
        {
            registration = null;

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

            List<Member> members = CollectMembers(context, contract, surrogates);
            if (members == null)
            {
                return null;
            }

            List<Include> includes = CollectIncludes(context, contract, members);
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

            foreach (Member member in members)
            {
                member.Deferred = constructAtEnd || 0 < includes.Count;
                member.ConstructAtEnd = constructAtEnd;
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

            // SkipConstructor means no constructor the author wrote may run, so the formatter reads
            // into an instance made by a private constructor emitted alongside it. Only meaningful
            // for a reference type that is created here at all: a struct has no constructor to skip,
            // an abstract contract creates nothing, and one that builds itself never calls `new`.
            //
            // The last condition is about not breaking the consumer's source. Emitting ANY
            // constructor into a type that declares none removes the implicit parameterless one, so
            // `new Theirs()` stops compiling -- an attribute silently breaking unrelated code. A type
            // that declares no constructor also has nothing to skip: the implicit one runs field
            // initializers and nothing else, which is exactly what the emitted one would do.
            bool skipConstructor =
                Shape.SkipsConstructor(contract)
                && !contract.IsValueType
                && !contract.IsAbstract
                && !constructAtEnd
                && DeclaresAConstructor(contract);

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
            if (root != null && !DeclaresInclude(contract.BaseType, contract))
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
                EmitRootFormatter(writer, qualified, root);
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
                skipConstructor
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

        private static void EmitFormatter(
            Writer writer,
            INamedTypeSymbol contract,
            string qualified,
            List<Member> members,
            List<Include> includes,
            Hooks hooks,
            bool constructAtEnd,
            bool skipConstructor
        )
        {
            writer.Line("/// <summary>Generated WallstopProto formatter. Do not edit.</summary>");
            writer.Line(
                "public sealed class WProtoFormatter : "
                    + Proto
                    + ".IWProtoFormatter<"
                    + qualified
                    + ">, "
                    + Proto
                    + ".IWProtoPolymorphicFormatter"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line(
                "/// <summary>The shared instance; the formatter holds no state.</summary>"
            );
            writer.Line("public static readonly WProtoFormatter Instance = new WProtoFormatter();");
            writer.Blank();

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
                skipConstructor
            );

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
                    + ".IWProtoPolymorphicFormatter"
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
            bool skipConstructor
        )
        {
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

            bool polymorphic = 0 < includes.Count;

            if (constructAtEnd)
            {
                // Deliberately not created here: a readonly member can only be assigned by a
                // constructor, so there is nothing to assign onto until the last value is in hand.
                writer.Line(qualified + " read = default(" + qualified + ");");
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
                member.EmitReadEpilogue(writer);
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

        private static string EmitRegistrar(
            GeneratorExecutionContext context,
            List<string> registrations
        )
        {
            string suffix = Sanitize(context.Compilation.AssemblyName ?? "Assembly");

            Writer writer = new Writer();
            writer.Line("// <auto-generated />");
            writer.Line("#pragma warning disable");
            writer.Line("namespace WallstopStudios.UnityHelpers.Generated" + Writer.Open);
            writer.Indent();
            writer.Line(
                "/// <summary>Registers every generated formatter in this assembly.</summary>"
            );
            writer.Line("internal static class WProtoGeneratedRegistrar_" + suffix + Writer.Open);
            writer.Indent();

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
            writer.Line("[global::System.Runtime.CompilerServices.ModuleInitializer]");
            writer.Line("#endif");
            writer.Line("internal static void Register()" + Writer.Open);
            writer.Indent();
            writer.Line(Proto + ".WProtoScalarFormatters.RegisterAll();");
            foreach (string registration in registrations)
            {
                writer.Line(Proto + ".WProtoFormatterProvider.Register(" + registration + ");");
            }

            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            writer.Outdent();
            writer.Line("}");
            return writer.ToString();
        }

        /// <summary>
        /// Reads and validates the contract's <c>[WProtoInclude]</c> list, deepest subtype first.
        /// </summary>
        /// <remarks>
        /// The ordering is load-bearing rather than cosmetic. <c>value is Beta</c> is true for a
        /// <c>Gamma</c>, so a dispatch chain that tested the shallower type first would write a
        /// <c>Gamma</c> under Beta's include tag and lose the Gamma level entirely -- a silent type
        /// downgrade. Sorting by inheritance depth, deepest first, makes the first matching test the
        /// most derived one.
        /// </remarks>
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
            INamedTypeSymbol contract
        )
        {
            HashSet<string> found = new HashSet<string>();

            foreach (SyntaxTree tree in compilation.SyntaxTrees)
            {
                SemanticModel model = compilation.GetSemanticModel(tree);
                foreach (SyntaxNode node in tree.GetRoot().DescendantNodes())
                {
                    if (!(node is TypeSyntax type))
                    {
                        continue;
                    }

                    if (
                        !(Resolve(model, type) is INamedTypeSymbol named)
                        || !named.IsGenericType
                        || named.IsUnboundGenericType
                    )
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
                    if (!IsOpen(named))
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
            switch (type)
            {
                case ITypeParameterSymbol _:
                    return true;
                case IArrayTypeSymbol array:
                    return IsOpen(array.ElementType);
                case IPointerTypeSymbol pointer:
                    return IsOpen(pointer.PointedAtType);
                case INamedTypeSymbol named:
                    // The containing type matters as much as the arguments: `Outer<T>.Inner<int>` has
                    // only closed arguments of its own and still cannot be named.
                    if (named.ContainingType != null && IsOpen(named.ContainingType))
                    {
                        return true;
                    }

                    foreach (ITypeSymbol argument in named.TypeArguments)
                    {
                        if (IsOpen(argument))
                        {
                            return true;
                        }
                    }

                    return false;
                default:
                    return false;
            }
        }

        private static List<Include> CollectIncludes(
            GeneratorExecutionContext context,
            INamedTypeSymbol contract,
            List<Member> members
        )
        {
            List<Include> includes = new List<Include>();
            HashSet<int> claimed = new HashSet<int>();
            foreach (Member member in members)
            {
                claimed.Add(member.Tag);
            }

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

                includes.Add(new Include(tag, subType));
            }

            // Direct subtypes of one type are mutually exclusive, so the chain's order cannot
            // change which branch matches; sorting by tag only makes the emitted code deterministic.
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
            SurrogateMap surrogates
        )
        {
            List<Member> members = new List<Member>();
            Dictionary<int, string> claimed = new Dictionary<int, string>();
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

                Member member = Member.Create(
                    contract.Name,
                    symbol.Name,
                    tag,
                    type,
                    NamedFlag(attribute, "IsRequired"),
                    NamedFlag(attribute, "OverwriteList"),
                    surrogates,
                    out bool ambiguous
                );
                if (member == null)
                {
                    Report(
                        context,
                        ambiguous
                            ? WProtoDiagnostics.AmbiguousListContract
                            : WProtoDiagnostics.UnsupportedMemberType,
                        symbol,
                        contract.Name,
                        symbol.Name,
                        type.ToDisplayString()
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
