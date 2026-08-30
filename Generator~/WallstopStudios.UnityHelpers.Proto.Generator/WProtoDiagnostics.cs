// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// The diagnostics this generator reports instead of silently skipping serialization work.
    /// </summary>
    /// <remarks>
    /// Refused contracts are errors and name the type, member, and fix. Migration and skipped-
    /// registration diagnostics use lower severities when existing consumer code can remain valid.
    /// The failure mode being avoided is a contract that silently gets no formatter and surfaces as
    /// an <c>InvalidOperationException</c> -- or, before this serializer existed, an opaque
    /// <c>ExecutionEngineException</c> -- from inside a shipped player.
    /// </remarks>
    internal static class WProtoDiagnostics
    {
        internal static readonly DiagnosticDescriptor ContractMustBePartial =
            new DiagnosticDescriptor(
                "WPROTO001",
                "WallstopProto contract must be partial",
                "'{0}' is a [WProtoContract] but is not declared partial. The generated formatter is emitted as a nested type so it can reach private members and hooks without reflection, which requires a partial declaration. Add 'partial' to '{0}' and to every type that encloses it.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor DuplicateTag = new DiagnosticDescriptor(
            "WPROTO002",
            "WallstopProto field number is used twice",
            "'{0}.{1}' and '{0}.{2}' both claim field number {3}. Field numbers are the wire contract; two members cannot share one.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        internal static readonly DiagnosticDescriptor UnsupportedMemberType =
            new DiagnosticDescriptor(
                "WPROTO003",
                "WallstopProto cannot serialize this member type",
                "'{0}.{1}' has type '{2}', which this generator cannot serialize. Supported: the integer and floating-point primitives, bool, string, char, byte[], enums, DateTime, TimeSpan, Guid, decimal, Uri, another [WProtoContract] type, Nullable<T> of any of those; an array of any rank, LinkedList<T>, Queue<T>, Stack<T>, ReadOnlyCollection<T>, IList<T>, ICollection<T>, IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T>, ISet<T>, or any type implementing ICollection<T> once with a public parameterless constructor and Add; and a dictionary, IDictionary<K,V>, IReadOnlyDictionary<K,V> or ReadOnlyDictionary<K,V>. The element or value must itself be a supported type other than Nullable<T>. Refused deliberately, with the reasons in the serialization guide: DateTimeOffset (no protobuf-net encoding in either major), IntPtr and UIntPtr (no 2.x serializer exists, and a pointer value names nothing once its process ends), and Type (the bytes are a runtime-bound assembly-qualified name that another runtime cannot resolve). Annotate '{2}' with [WProtoContract] if it is yours, or declare the member as a concrete collection rather than your own interface, or remove [WProtoMember], or write a formatter by hand and register it with WProtoFormatterProvider.Register<T>.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor TagOutOfRange = new DiagnosticDescriptor(
            "WPROTO004",
            "WallstopProto field number is out of range",
            "'{0}.{1}' claims field number {2}. Protobuf field numbers run from 1 to 536870911, and 19000-19999 are reserved.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        internal static readonly DiagnosticDescriptor HookWithoutContract =
            new DiagnosticDescriptor(
                "WPROTO005",
                "WallstopProto lifecycle hook on a type with no contract",
                "'{0}.{1}' carries a WallstopProto lifecycle attribute but '{0}' has no [WProtoContract], so nothing will ever call it. Add [WProtoContract] to '{0}', or remove the attribute from '{1}'.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor DuplicateHook = new DiagnosticDescriptor(
            "WPROTO006",
            "WallstopProto lifecycle hook declared twice",
            "'{0}' declares more than one method with [{1}]. Hook ordering is a contract and a second method of the same kind has no defined position in it.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        internal static readonly DiagnosticDescriptor MemberNotAssignable =
            new DiagnosticDescriptor(
                "WPROTO007",
                "WallstopProto member cannot be assigned when reading",
                "'{0}.{1}' is read-only, so a decoded value cannot be written back to it. Give it a setter, make the field non-readonly, or remove [WProtoMember].",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor GenericContract = new DiagnosticDescriptor(
            "WPROTO009",
            "WallstopProto cannot generate for a contract nested inside a generic type",
            "'{0}' is a [WProtoContract] nested inside a generic type. A generic contract itself is supported -- its closed constructions are discovered and registered -- but a contract nested inside one is not: it is not generic, so there is no construction of it to find, and its formatter would be emitted and never registered. Move '{0}' out of its generic container, or make it generic itself.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        internal static readonly DiagnosticDescriptor HookOnValueType = new DiagnosticDescriptor(
            "WPROTO010",
            "WallstopProto lifecycle hook on a value type",
            "'{0}' is a struct with a WallstopProto lifecycle hook. The formatter receives the value as 'in', so calling a hook on it copies the struct first and every mutation the hook makes is discarded. Make '{0}' a class, or remove the hook.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        internal static readonly DiagnosticDescriptor NoParameterlessConstructor =
            new DiagnosticDescriptor(
                "WPROTO011",
                "WallstopProto contract has no parameterless constructor",
                "'{0}' is a [WProtoContract] class with no parameterless constructor, so the formatter cannot create an instance to read into. Add one -- it may be private, since the formatter is nested inside '{0}'.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor AmbiguousListContract =
            new DiagnosticDescriptor(
                "WPROTO012",
                "WallstopProto cannot tell whether this member is a message or a collection",
                "'{0}.{1}' has type '{2}', which is both a [WProtoContract] and a collection, and nothing says which encoding it should get. Writing it as a repeated field discards its [WProtoMember]s; writing it as a message discards its elements. Set [WProtoContract(IgnoreListHandling = true)] on '{2}' to write it as a message, or remove [WProtoContract] from it to write it as a repeated field.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor BadInclude = new DiagnosticDescriptor(
            "WPROTO013",
            "WallstopProto include is not usable",
            "'{0}' declares [WProtoInclude({1}, typeof({2}))], but {3}. An include names a subtype the wire can identify, so it must be a [WProtoContract] that derives from '{0}' and its field number must be free.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        internal static readonly DiagnosticDescriptor AbstractWithoutIncludes =
            new DiagnosticDescriptor(
                "WPROTO014",
                "WallstopProto cannot instantiate this contract",
                "'{0}' is an abstract [WProtoContract] with no [WProtoInclude], so reading it can never produce an instance. Declare an include for each concrete subtype, or move [WProtoContract] to the subtypes.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor ImmutableWithIncludes =
            new DiagnosticDescriptor(
                "WPROTO015",
                "WallstopProto cannot combine immutable members with subtypes",
                "'{0}' has a member that cannot be assigned after construction AND declares [WProtoInclude]. The first needs the instance built once every value is read; the second replaces the instance when an include tag arrives. Give the member a setter, or move the polymorphism to a type whose members are assignable.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor SurrogateNotAContract =
            new DiagnosticDescriptor(
                "WPROTO016",
                "WallstopProto surrogate is not a contract",
                "[assembly: WProtoSurrogate(typeof({0}), typeof({1}))] names '{1}' as the surrogate for '{0}', but '{1}' is not a [WProtoContract]. A surrogate is what actually gets written, so it needs a formatter of its own; annotate '{1}' with [WProtoContract] and give its members [WProtoMember].",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor SurrogateCannotConvert =
            new DiagnosticDescriptor(
                "WPROTO017",
                "WallstopProto surrogate cannot convert both ways",
                "[assembly: WProtoSurrogate(typeof({0}), typeof({1}))] needs conversion operators in BOTH directions, and '{0}' to '{1}' or '{1}' to '{0}' is missing. A surrogate that cannot be converted back writes bytes that look correct and reads a value that never returns, so declare 'public static implicit operator {1}({0} value)' and its inverse on either type.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        /*
         * WPROTO018 is retired. It reported a subtype neither end declared, which was the right
         * refusal while the relationship had to be written down -- and it is exactly the situation
         * deriving-is-declaring now handles without asking
         * (https://github.com/Ambiguous-Interactive/unity-helpers/issues/613). What is left of that
         * case is the NUMBER, which WPROTO041 reports and the assigner supplies.
         *
         * The code is not reused. A consumer's suppression, an .editorconfig entry or an old build
         * log naming WPROTO018 should keep meaning what it meant, and a recycled identifier would
         * make a stale suppression silence something new.
         */

        internal static readonly DiagnosticDescriptor MarshalArityMismatch =
            new DiagnosticDescriptor(
                "WPROTO019",
                "WallstopProto root marshal does not match its type's arity",
                "[assembly: WProtoRootMarshal(typeof({0}), typeof({1}))] pairs types of different generic arity. The generator closes the formatter with the SAME type arguments it finds on a construction of '{0}', so the two must take the same number of them -- write both unbound, as typeof(Real<>) and typeof(Formatter<>), or both closed.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor MarshalNotAFormatter =
            new DiagnosticDescriptor(
                "WPROTO020",
                "WallstopProto root marshal is not a formatter for its type",
                "[assembly: WProtoRootMarshal(typeof({0}), typeof({1}))] names '{1}' as the root formatter for '{0}', but '{1}' does not implement IWProtoFormatter<{0}> with a public parameterless constructor. The registrar emits 'new {1}()' and hands it to WProtoRootMarshalProvider.Register, so anything else is a build error inside generated code the developer never wrote.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor MarshalOnContract = new DiagnosticDescriptor(
            "WPROTO021",
            "WallstopProto root marshal names a contract",
            "[assembly: WProtoRootMarshal(typeof({0}), typeof({1}))] names '{0}', which is also a [WProtoContract]. A contract's own formatter answers first at the root, so the marshal would never run and '{0}' would be written as its own members rather than as '{1}' -- two wire shapes for one type, decided by which registration is looked up first. Remove one of the two.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        internal static readonly DiagnosticDescriptor DuplicateMarshal = new DiagnosticDescriptor(
            "WPROTO022",
            "WallstopProto root marshal is declared twice",
            "[assembly: WProtoRootMarshal(typeof({0}), typeof({1}))] is the second marshal this assembly declares for '{0}'. Only the first is registered, so the other's wire shape is silently unreachable -- and which one wins is the order the attributes happen to be read in. Delete one.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        internal static readonly DiagnosticDescriptor DeclaredRootNotAssignable =
            new DiagnosticDescriptor(
                "WPROTO023",
                "WallstopProto declared root is not assignable to its declared type",
                "[assembly: WProtoDeclaredRoot(typeof({0}), typeof({1}))] names '{1}' as the contract serving '{0}', but a '{1}' cannot be held as a '{0}'. The registration is emitted as WProtoDeclaredRootProvider.Register<{0}, {1}>(), whose constraint is 'TRoot : TDeclared', so a root that does not derive from its declared type is a compiler error inside generated code that never names this attribute.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor SelfDeclaredRoot = new DiagnosticDescriptor(
            "WPROTO024",
            "WallstopProto declared root is its own root",
            "[assembly: WProtoDeclaredRoot(typeof({0}), typeof({0}))] names '{0}' as the contract serving itself. The adapter registered for a declared type resolves its root through the same provider, so it would find itself and recurse. A type that has its own formatter needs no declared root; remove the attribute.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        internal static readonly DiagnosticDescriptor DeclaredRootOnContract =
            new DiagnosticDescriptor(
                "WPROTO025",
                "WallstopProto declared root names a contract as the declared type",
                "[assembly: WProtoDeclaredRoot(typeof({0}), typeof({1}))] names '{0}', which is also a [WProtoContract]. Registering the adapter would replace '{0}'s own generated formatter, so every value written as a '{0}' would silently become '{1}'s message instead. A declared root is for a type that has no encoding of its own -- an interface, or an abstract type carrying no contract. Use [WProtoInclude] on '{0}' if '{1}' is meant to be a subtype of it.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor GenericDeclaredRoot =
            new DiagnosticDescriptor(
                "WPROTO026",
                "WallstopProto declared root names a generic type",
                "[assembly: WProtoDeclaredRoot(typeof({0}), typeof({1}))] names a generic type. Unlike a root marshal, a declared root is registered exactly once for the pair it names and is never closed over a construction found in source, so an open generic has nothing to register. Declare one pair per closed construction you need.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor DuplicateDeclaredRoot =
            new DiagnosticDescriptor(
                "WPROTO027",
                "WallstopProto declared root is declared twice",
                "[assembly: WProtoDeclaredRoot(typeof({0}), typeof({1}))] is the second root this assembly declares for '{0}'. A declared type has exactly one root -- a payload does not name the contract that wrote it, so a reader that had two answers would have to guess -- and only the first is registered. Delete one.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        /// <summary>
        /// The warning that reports a skipped registration rather than a refused declaration.
        /// </summary>
        /// <remarks>
        /// Skipping is the right behaviour: naming a consumer's private nested type from the
        /// registrar is <c>CS0122</c> in their own build, which is worse than the missing
        /// registration. But the skip is otherwise invisible until the type is serialized in a
        /// shipped player, so it is announced. A warning rather than an error because the developer
        /// may never serialize that closure, and failing their build over a type they declared
        /// privately would be the very outcome the skip exists to avoid.
        /// </remarks>
        internal static readonly DiagnosticDescriptor UnnameableClosure = new DiagnosticDescriptor(
            "WPROTO028",
            "WallstopProto cannot register a closed construction it cannot name",
            "'{0}' is a closed construction WallstopProto would register a formatter for, but the generated registrar cannot write its name: '{1}' cannot be named from the generated registrar, which is a type of its own. It is skipped, so serializing one throws at run time rather than failing this build. Widen '{1}' to internal or public -- or, if it is `file`-local or otherwise unnameable, register the formatter yourself from code that can name it.",
            "WallstopProto",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

        internal static readonly DiagnosticDescriptor DeclaredRootOnInstantiableType =
            new DiagnosticDescriptor(
                "WPROTO029",
                "WallstopProto declared root names a type that can be instantiated",
                "[assembly: WProtoDeclaredRoot(typeof({0}), typeof({1}))] names '{0}', which is neither an interface nor abstract. A declared root exists for a type that has no encoding of its own; a '{0}' that is actually a '{0}' would be served by the adapter, fail to narrow to '{1}', and encode to nothing -- measured as a populated value writing zero bytes and reading back as '{1}'. Make '{0}' abstract, or give it its own [WProtoContract] -- and if it is a value type or an array, which can be neither, remove the attribute.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor UnportedProtobufContract =
            new DiagnosticDescriptor(
                "WPROTO030",
                "protobuf-net contract has not been ported to WallstopProto",
                "'{0}' has no [WProtoContract], so it has no generated WallstopProto formatter. WPROTO030 matched it because {1}. Unless it is deliberately served through a surrogate, root marshal, or hand-written formatter, Serializer falls back to protobuf-net's reflection path, which does not work under IL2CPP. Add [WProtoContract] and a [WProtoMember] beside each protobuf-net member with matching field numbers, or suppress WPROTO030 at the contract declaration when another formatter serves it.",
                "WallstopProto",
                DiagnosticSeverity.Info,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor ConflictingReferencedDeclaredRoot =
            new DiagnosticDescriptor(
                "WPROTO031",
                "WallstopProto declared root conflicts with a referenced assembly",
                "Assembly '{3}' declares '{1}' as the root serving '{0}', while referenced assembly '{4}' declares '{2}'. Both generated registrars run in Unity's unordered startup phase, so assembly load order would choose the WallstopProto adapter and wire shape. Remove one declaration; if the conflict is deliberate, suppress WPROTO031 only after ensuring every build registers the intended adapter.",
                "WallstopProto",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor NestedCollectionTooDeep =
            new DiagnosticDescriptor(
                "WPROTO032",
                "WallstopProto nested collection is too deeply nested",
                "'{0}.{1}' has type '{2}', whose collections nest more than 64 deep. Each level is encoded as a wrapper message, and WProtoReader refuses to read past 64 levels of nesting, so a deeper member could be written and never read back. Reduce the nesting, or hold the inner levels in a [WProtoContract] of their own.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor SkipConstructorDropsAnInitializer =
            new DiagnosticDescriptor(
                "WPROTO033",
                "WallstopProto SkipConstructor discards this field's initializer",
                "'{0}.{1}' is initialized where it is declared and is not a [WProtoMember], so its value exists only because a constructor ran. '{0}' declares SkipConstructor, which asks protobuf-net to allocate the instance UNINITIALIZED -- no constructor runs, no initializer runs, and a member the payload does not carry cannot restore it, so '{1}' arrives at its type's default on every deserialized instance. Allocate it where it is used instead, or give it a [WProtoMember] so the wire carries it. A [WProtoAfterDeserialization] hook is only enough when every reader runs it, which is what WPROTO034 is about. Suppress WPROTO033 at the declaration when the default really is a valid value for '{1}'.",
                "WallstopProto",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor JsonConverterNotUsable =
            new DiagnosticDescriptor(
                "WPROTO035",
                "WallstopProto JSON converter declaration cannot be closed",
                "[assembly: WJsonConverter(typeof({0}), typeof({1}))] cannot be used. The generator closes both over the arguments of each construction it finds in source and registers the converter, so '{0}' must be an unbound generic, '{1}' must be an unbound generic of the same arity whose constraints the same arguments satisfy, and the closed '{1}' must derive from JsonConverter<{0}> and have a public parameterless constructor. Without all four the registration would not compile, so the declaration is ignored and the closure falls back to the reflective JsonConverterFactory path, which throws under IL2CPP.",
                "WallstopProto",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor DuplicateJsonConverter =
            new DiagnosticDescriptor(
                "WPROTO036",
                "WallstopProto JSON converter is declared twice for one type",
                "This assembly declares more than one [assembly: WJsonConverter] for '{0}'. Only the first is used, and which one that is depends on attribute order rather than on anything you can read at the declaration. Remove all but the intended pairing with '{1}'.",
                "WallstopProto",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor HookOnSubtype = new DiagnosticDescriptor(
            "WPROTO034",
            "WallstopProto lifecycle hook on a subtype does not run under every reader",
            "'{0}.{1}' is a lifecycle hook on '{0}', which is a subtype of the [WProtoInclude] chain rooted at '{2}'. Only '{2}' owns the wire shape, and a reader invokes the callbacks it finds there: protobuf-net 3.2.56 runs the root's and NONE of a subtype's, so '{1}' never runs wherever the protobuf-net fallback serves this type -- a WALLSTOP_PROTO-off build, or any type Serializer reaches reflectively. protobuf-net 2.4.9 does run it, root-first; WallstopProto runs it subtype-first. Declare the hook on '{2}' and have it call a protected virtual method '{0}' overrides, which runs once, in one order, under all three. Suppress WPROTO034 at the declaration when the hook is an optimization every other path repeats.",
            "WallstopProto",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );

        internal static readonly DiagnosticDescriptor DataFormatNotApplicable =
            new DiagnosticDescriptor(
                "WPROTO037",
                "WallstopProto DataFormat does not apply to this member",
                "'{0}.{1}' has type '{2}' and asks for DataFormat = ZigZag, which protobuf spells sint32 and sint64 and has for no other type. Only sbyte, short, int and long -- including as a Nullable<T> -- have that encoding; an unsigned integer, a float, a string, a message, a collection and a map have none. This is an error rather than an ignored annotation because the two readings are different bytes: dropping it silently would write the int32 the member explicitly declined. Remove DataFormat, or hold the value in one of the four types that has it.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor SurrogateShapeMismatch =
            new DiagnosticDescriptor(
                "WPROTO038",
                "WallstopProto surrogate generic shape is incompatible",
                "[assembly: WProtoSurrogate(typeof({0}), typeof({1}))] must name either two closed types, or two unbound generic types with the same arity whose constraints accept every type argument allowed by '{0}'. Otherwise the generator cannot close '{1}' wherever '{0}' is used. Match their openness, arity, and generic constraints.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor DuplicateSubtypeTag =
            new DiagnosticDescriptor(
                "WPROTO039",
                "WallstopProto subtype field number is claimed twice",
                "'{0}' and '{1}' both claim field number {2} on '{3}'. A payload resolves a subtype by that number alone, so two types under one number is a value that reads back as whichever the dispatch chain happens to test first. [WProtoInclude] on the base and [WProtoSubtype] on the subtype are the same declaration written two ways and share one field-number space, so give one of them a free number -- and never renumber one that has already shipped.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor BadSubtype = new DiagnosticDescriptor(
            "WPROTO040",
            "WallstopProto subtype declaration is not usable",
            "'{0}' declares [WProtoSubtype({1})], but {2}. A subtype declaration names the immediate base it is written as, so it must name a [WProtoContract] that '{0}' derives DIRECTLY from, in the same assembly, with a free field number.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        /// <summary>
        /// A tag-less subtype declaration the assembly's manifest has no entry for.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Declared as an error, and reported as one for any compilation that can reach a player.
        /// An unnumbered subtype has no wire representation at all, so shipping one is a save that
        /// throws on the machine of whoever installed the build.
        /// </para>
        /// <para>
        /// It is reported as a WARNING when <c>UNITY_EDITOR</c> is defined, and that is not a
        /// softening -- it is what makes the fix reachable. The assignment tool discovers
        /// declarations through <c>TypeCache</c>, which indexes types in assemblies that COMPILED;
        /// an error here fails the assembly, the new type never exists, and the tool whose only job
        /// is to number it cannot see it. The escape was to hand-write the number, which is the
        /// thing the manifest exists to remove. Measured in 6000.4.6f1: a numberless subtype gave
        /// <c>compilationFailed=True</c> and the type was absent from every assembly in the
        /// AppDomain. As a warning the assembly compiles, the editor's automatic pass assigns the
        /// number, and the player build is still refused -- here by severity, and again by
        /// <c>WProtoSubtypeTagBuildGate</c>.
        /// </para>
        /// </remarks>
        internal static readonly DiagnosticDescriptor SubtypeTagUnassigned =
            new DiagnosticDescriptor(
                "WPROTO041",
                "WallstopProto subtype has no field number",
                "{3}, and this assembly's manifest has no entry for it, so there is nothing to write it under. The number is not derived, because a number a generator invented would depend on which types that run happened to see and would change under data already saved. {2} Nothing is written under a guessed number in the meantime: serializing a '{0}' throws rather than writing it as a '{1}', so no save can lose the subtype silently.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        /// <summary>How an explicitly declared subtype's missing number is introduced.</summary>
        internal const string SubtypeTagUnassignedDeclared =
            "'{0}' declares [WProtoSubtype(typeof({1}))] without a field number";

        /// <summary>
        /// How an INHERITED subtype's missing number is introduced.
        /// </summary>
        /// <remarks>
        /// A separate opening, because the other one would name an attribute the author never wrote.
        /// Deriving from a contract is the declaration
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/613">#613</see>),
        /// so the message describes the inheritance the developer can see rather than a declaration
        /// they did not make.
        /// </remarks>
        internal const string SubtypeTagUnassignedInherited =
            "'{0}' derives from '{1}', which is a [WProtoContract], so it is written as one of that type's subtypes and needs a field number of its own";

        /// <summary>The editor half of <see cref="SubtypeTagUnassigned"/>'s message.</summary>
        internal const string SubtypeTagUnassignedInEditor =
            "The editor assigns it for you: the number is written to this assembly's WProtoSubtypeTags.cs on the next assembly reload, and this is a warning rather than an error so that the assembly compiles and the assignment tool can see the type at all. Run Tools > Wallstop Studios > Unity Helpers > Assign WallstopProto Subtype Tags if it has not.";

        /// <summary>The player half of <see cref="SubtypeTagUnassigned"/>'s message.</summary>
        internal const string SubtypeTagUnassignedInPlayer =
            "UNITY_EDITOR is not defined for this compilation, so it can reach a player and cannot be allowed to. Open the project in the editor, which assigns the number automatically, or run Tools > Wallstop Studios > Unity Helpers > Assign WallstopProto Subtype Tags (headless: -executeMethod WallstopStudios.UnityHelpers.Editor.Tools.WProtoSubtypeTagAssigner.AssignFromCommandLine), then commit the [assembly: WProtoSubtypeTag] entry it writes. Writing the number yourself as [WProtoSubtype(typeof(Base), tag)] also works.";

        internal static readonly DiagnosticDescriptor BadSubtypeTagManifest =
            new DiagnosticDescriptor(
                "WPROTO042",
                "WallstopProto subtype tag manifest entry is not usable",
                "The subtype tag manifest entry {0} cannot be honoured: {1}. The manifest is the wire contract for every [WProtoSubtype] that omits its field number, so an entry that cannot be read is a subtype with no number rather than a number that is merely wrong. Re-run Tools > Wallstop Studios > Unity Helpers > Assign WallstopProto Subtype Tags to rewrite the file, and never hand-edit a number that has already shipped.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        /// <summary>
        /// A member claiming a field number or a name the contract has reserved.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Its own code rather than part of <c>WPROTO002</c>, whose subject is two members that
        /// exist at once. This one is about a member that no longer exists: the declaration that
        /// spent the number was deleted with it, so nothing but the reservation records that the
        /// number was ever used
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/608">#608</see>).
        /// </para>
        /// <para>
        /// It is also the answer to a reservation that contradicts a live member, because that is
        /// the same state seen from the other side. Which of the two is wrong cannot be decided
        /// here -- the member may be the removed one coming back unchanged -- so the message offers
        /// both fixes rather than a second diagnostic that could never fire alongside this one.
        /// </para>
        /// </remarks>
        internal static readonly DiagnosticDescriptor ReservedTag = new DiagnosticDescriptor(
            "WPROTO043",
            "WallstopProto member takes something the contract reserved",
            "'{0}.{1}' claims {2}, which '{0}' reserves with [WProtoReserved]. A reservation records what a removed member held, because the declaration that spent it was deleted along with it -- so every payload written before the removal still carries that field, and giving it to another member reads those saves back as the wrong thing. Use a free field number and an unreserved name, or, if this really is the removed member coming back unchanged, delete the matching [WProtoReserved] in the same commit.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        /// <summary>
        /// A subclass of a contract that carries no <c>[WProtoContract]</c>, declares no subtype
        /// relationship, and has not been opted out.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every other way of getting a subtype wrong is a build error. This one was not, and it is
        /// the only path in the subtype surface that failed at run time instead
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/613">#613</see>).
        /// <c>WPROTO018</c> is the same situation with <c>[WProtoContract]</c> present, and its own
        /// message used to recommend removing that attribute -- which traded a build error for a
        /// runtime one and said nothing about the trade.
        /// </para>
        /// <para>
        /// An error rather than a warning, and it names the opt-out in the same sentence. A contract
        /// that is neither sealed nor a value type carries the closing guard unconditionally, so
        /// there is no such thing as a subclass this cannot reach: the only question is whether an
        /// instance ever meets the serializer, and that is a fact about the program that only its
        /// author knows. <c>[WProtoNotSerialized]</c> is where the author records it, so the
        /// decision lives beside the declaration instead of in the absence of one.
        /// </para>
        /// </remarks>
        internal static readonly DiagnosticDescriptor UndeclaredSubclass = new DiagnosticDescriptor(
            "WPROTO044",
            "WallstopProto subtype derives from a contract in another assembly",
            "'{0}' derives from '{1}', a [WProtoContract] compiled into assembly '{2}'. Deriving from a contract is normally all it takes -- the subtype joins the base's dispatch chain and the assigner commits its field number -- but that chain is generated when the BASE's own assembly is compiled, so a subtype declared afterwards, in an assembly that merely references it, could never appear in it. Accepting this would compile and then throw on the first save. Move '{0}' into '{2}', or give it a [WProtoContract] of its own and hold a '{1}' in it as a [WProtoMember] instead of deriving from it -- a member of a type from another assembly is generated normally, and the base writes its own subtypes through its own chain. If a '{0}' is never meant to reach the serializer, write [WProtoNotSerialized] on it.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        /// <summary>
        /// A type carrying both <c>[WProtoNotSerialized]</c> and a declaration that says the
        /// opposite.
        /// </summary>
        /// <remarks>
        /// The opt-out is a statement that no instance reaches the serializer;
        /// <c>[WProtoContract]</c> and <c>[WProtoSubtype]</c> both state that one does. Left
        /// unreported, whichever the generator happened to read first would decide, and the losing
        /// declaration would read as honoured.
        /// </remarks>
        internal static readonly DiagnosticDescriptor ContradictoryNotSerialized =
            new DiagnosticDescriptor(
                "WPROTO045",
                "WallstopProto opt-out contradicts a serialization declaration",
                "'{0}' carries [WProtoNotSerialized] and also {1}. The opt-out says no instance of '{0}' ever reaches the serializer; the other declaration says one does and describes how it is written. Delete whichever is wrong -- the opt-out exists for a subclass that is NOT serialized, and it does not suppress a contract of its own.",
                "WallstopProto",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true
            );

        /// <summary>
        /// An enum member taking a value, or a name, that the enum has reserved.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Its own code rather than part of <c>WPROTO043</c>, whose subject is a
        /// <c>[WProtoMember]</c> on a contract: that message is about field numbers inside one
        /// message and would not read correctly about an enum member, whose number is the VALUE on
        /// the wire rather than a field it sits in
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/609">#609</see>).
        /// </para>
        /// <para>
        /// Reachability from a contract is not consulted, and that is a simplification rather than
        /// an omission: a reservation is the only thing that can make this fire, so an enum nothing
        /// serializes is silent whether or not it is walked. The pass a reachability gate would
        /// have protected against noise does not exist, and adding the walk would only have hidden
        /// a genuine collision on an enum this compilation happens not to reach -- which the next
        /// assembly to use it would then hit instead.
        /// </para>
        /// </remarks>
        internal static readonly DiagnosticDescriptor ReservedEnumValue = new DiagnosticDescriptor(
            "WPROTO046",
            "WallstopProto enum member takes something the enum reserved",
            "'{0}.{1}' claims {2}, which '{0}' reserves with [WProtoReserved]. WallstopProto writes an enum as a varint of its underlying value, so that value is the wire contract: a reservation records what a removed member held, and every payload written before the removal still carries it. Giving it to another member reads those saves back as the wrong member, silently. Use a free value and an unreserved name, or, if this really is the removed member coming back unchanged, delete the matching [WProtoReserved] in the same commit.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

        /// <summary>
        /// A type that inherits its serialization and carries members of its own, without saying so.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A <b>warning</b>, and it has to be one: the code works. Deriving from a
        /// <c>[WProtoContract]</c> is the declaration, so this type is already generated, numbered
        /// and round-tripping
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/613">#613</see>).
        /// Nothing on the wire depends on the attribute, so refusing the build would be refusing
        /// working code over a matter of legibility -- and taking a package upgrade must never fail
        /// a consumer for that.
        /// </para>
        /// <para>
        /// It is still worth saying. A reader of this type sees <c>[WProtoMember(1)]</c> on a field
        /// -- a durable wire number -- with nothing on the type explaining why it has one. They have
        /// to open the base to learn that this class is serialized at all.
        /// </para>
        /// <para>
        /// Gated on the type declaring at least one <c>[WProtoMember]</c>, deliberately. A subclass
        /// that adds only behaviour is the ordinary reason to derive from anything, and asking every
        /// one of them for an attribute would be the noise this design removed. A member is the
        /// author stating a wire contract, and that is the thing worth writing down.
        /// </para>
        /// </remarks>
        internal static readonly DiagnosticDescriptor InheritedContractNotDeclared =
            new DiagnosticDescriptor(
                "WPROTO047",
                "WallstopProto contract is inherited rather than declared",
                "'{0}' declares [WProtoMember] on '{1}', so it has a wire contract of its own, but carries no [WProtoContract]. This works: '{0}' derives from '{2}', which is one, so a formatter is generated for it and its field number is committed to the assembly's manifest. Add [WProtoContract] to '{0}' anyway, so a reader can see that its members are on the wire without opening '{2}'. Suppress WPROTO047 at the declaration if inheriting the contract is meant to be the whole statement.",
                "WallstopProto",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );

        internal static readonly DiagnosticDescriptor HookSignature = new DiagnosticDescriptor(
            "WPROTO008",
            "WallstopProto lifecycle hook has the wrong signature",
            "'{0}.{1}' is a WallstopProto lifecycle hook, so it must be a non-static method taking no parameters.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );
    }
}
