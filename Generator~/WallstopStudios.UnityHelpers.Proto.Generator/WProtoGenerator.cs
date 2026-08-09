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

            if (contract.IsGenericType)
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

            if (!contract.IsValueType && !HasAccessibleParameterlessConstructor(contract))
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

            EmitFormatter(writer, contract, qualified, members, hooks);

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

            EmitMeasure(writer, qualified, members, hooks);
            writer.Blank();
            EmitWrite(writer, qualified, members, hooks);
            writer.Blank();
            EmitRead(writer, contract, qualified, members, hooks);

            writer.Outdent();
            writer.Line("}");
        }

        private static void EmitMeasure(
            Writer writer,
            string qualified,
            List<Member> members,
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
            foreach (Member member in members)
            {
                writer.Line("if (" + member.PresenceTest + ")" + Writer.Open);
                writer.Indent();
                writer.Line(
                    "size += "
                        + Proto
                        + ".WProtoSizes.TagSize("
                        + member.Tag
                        + ") + "
                        + member.SizeExpression
                        + ";"
                );
                writer.Outdent();
                writer.Line("}");
                writer.Blank();
            }

            writer.Line("return size;");
            writer.Outdent();
            writer.Line("}");
        }

        private static void EmitWrite(
            Writer writer,
            string qualified,
            List<Member> members,
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

            foreach (Member member in members)
            {
                writer.Line("if (" + member.PresenceTest + ")" + Writer.Open);
                writer.Indent();
                writer.Line(
                    "if (!writer.TryWriteTag("
                        + member.Tag
                        + ", "
                        + member.WireType
                        + ") || !writer."
                        + member.WriteCall
                        + ")"
                        + Writer.Open
                );
                writer.Indent();
                writer.Line("return false;");
                writer.Outdent();
                writer.Line("}");
                writer.Outdent();
                writer.Line("}");
                writer.Blank();
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

            writer.Line(
                contract.IsValueType
                    ? qualified + " read = default(" + qualified + ");"
                    : qualified + " read = new " + qualified + "();"
            );

            if (hooks.BeforeDeserialization != null)
            {
                writer.Line("read." + hooks.BeforeDeserialization + "();");
            }

            writer.Blank();
            writer.Line(
                "while (reader.TryReadTag(out int fieldNumber, out int wireType))" + Writer.Open
            );
            writer.Indent();
            writer.Line("switch (fieldNumber)" + Writer.Open);
            writer.Indent();

            foreach (Member member in members)
            {
                writer.Line(
                    "case "
                        + member.Tag
                        + " when wireType == "
                        + member.WireType
                        + ":"
                        + Writer.Open
                );
                writer.Indent();
                foreach (string line in member.ReadStatements("read", qualified))
                {
                    writer.Line(line);
                }

                writer.Line("break;");
                writer.Outdent();
                writer.Line("}");
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

                Member member = Member.Create(symbol.Name, tag, type, IsRequired(attribute));
                if (member == null)
                {
                    Report(
                        context,
                        WProtoDiagnostics.UnsupportedMemberType,
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

        private static bool IsRequired(AttributeData attribute)
        {
            foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
            {
                if (argument.Key == "IsRequired" && argument.Value.Value is bool required)
                {
                    return required;
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
