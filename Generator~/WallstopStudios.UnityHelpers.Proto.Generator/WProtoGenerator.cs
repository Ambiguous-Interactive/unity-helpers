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

            List<string> registrations = new List<string>();
            foreach (INamedTypeSymbol contract in contracts)
            {
                string source = Emit(context, contract, out string registration);
                if (source == null)
                {
                    continue;
                }

                context.AddSource(FileNameFor(contract), SourceText.From(source, Encoding.UTF8));
                registrations.Add(registration);
            }

            if (0 < registrations.Count)
            {
                context.AddSource(
                    "WProtoGeneratedRegistrar.g.cs",
                    SourceText.From(EmitRegistrar(context, registrations), Encoding.UTF8)
                );
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

            if (IsGenericAnywhere(contract))
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

            List<Member> members = CollectMembers(context, contract);
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

            // Every member of a polymorphic contract reads into a local and is assigned after the
            // loop: an include tag can replace the instance, and protobuf-net allows that tag in
            // either position even though it always writes it first.
            foreach (Member member in members)
            {
                member.Deferred = 0 < includes.Count;
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

            if (
                !contract.IsValueType
                && !contract.IsAbstract
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
            registration = qualified + ".WProtoFormatter.Instance";

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
                    "partial " + KeywordFor(container) + " " + container.Name + Writer.Open
                );
                writer.Indent();
            }

            EmitFormatter(writer, contract, qualified, members, includes, hooks);

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
            Hooks hooks
        )
        {
            writer.Line("/// <summary>Generated WallstopProto formatter. Do not edit.</summary>");
            writer.Line(
                "public sealed class WProtoFormatter : "
                    + Proto
                    + ".IWProtoFormatter<"
                    + qualified
                    + ">"
                    + Writer.Open
            );
            writer.Indent();
            writer.Line(
                "/// <summary>The shared instance; the formatter holds no state.</summary>"
            );
            writer.Line("public static readonly WProtoFormatter Instance = new WProtoFormatter();");
            writer.Blank();

            EmitMeasure(writer, contract, qualified, members, includes, hooks);
            writer.Blank();
            EmitWrite(writer, contract, qualified, members, includes, hooks);
            writer.Blank();
            EmitRead(writer, contract, qualified, members, includes, hooks);

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
            Hooks hooks
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

            if (contract.IsValueType)
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
            else
            {
                writer.Line(qualified + " read = new " + qualified + "();");
            }

            if (hooks.BeforeDeserialization != null && !polymorphic)
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
            INamedTypeSymbol contract
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

                if (!assignable)
                {
                    Report(
                        context,
                        WProtoDiagnostics.MemberNotAssignable,
                        symbol,
                        contract.Name,
                        symbol.Name
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
