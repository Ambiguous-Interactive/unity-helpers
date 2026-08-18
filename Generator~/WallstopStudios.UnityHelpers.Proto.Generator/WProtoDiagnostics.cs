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
                "'{0}.{1}' has type '{2}', which this generator cannot serialize yet. Supported: the integer and floating-point primitives, bool, string, byte[], enums, another [WProtoContract] type, Nullable<T> of any of those; an array of any rank, LinkedList<T>, Queue<T>, Stack<T>, ReadOnlyCollection<T>, IList<T>, ICollection<T>, IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T>, ISet<T>, or any type implementing ICollection<T> once with a public parameterless constructor and Add; and a dictionary, IDictionary<K,V>, IReadOnlyDictionary<K,V> or ReadOnlyDictionary<K,V>. The element or value must itself be a supported type other than Nullable<T>. Annotate '{2}' with [WProtoContract] if it is yours, or declare the member as a concrete collection rather than your own interface, or remove [WProtoMember], or write a formatter by hand and register it with WProtoFormatterProvider.Register<T>.",
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

        internal static readonly DiagnosticDescriptor SubtypeNotIncluded = new DiagnosticDescriptor(
            "WPROTO018",
            "WallstopProto subtype is not declared by its base",
            "'{0}' is a [WProtoContract] whose base '{1}' is one too, but '{1}' does not declare it with [WProtoInclude]. A subtype is written as its base writes it -- the include holding this type's members, then the base's -- so without the declaration there is no tag to write it under, and serializing one fails at run time in a shipped player. Add [WProtoInclude(tag, typeof({0}))] to '{1}', or remove [WProtoContract] from '{0}' if it is not meant to be serialized on its own.",
            "WallstopProto",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true
        );

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
                "'{0}' is a [ProtoContract] with no [WProtoContract], so it has no generated WallstopProto formatter. Unless it is deliberately served through a surrogate, root marshal, or hand-written formatter, Serializer falls back to protobuf-net's reflection path, which does not work under IL2CPP. Add [WProtoContract] and a [WProtoMember] beside each [ProtoMember] with matching field numbers, or suppress WPROTO030 at the [ProtoContract] declaration when another formatter serves it.",
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
                "'{0}.{1}' is initialized where it is declared and is not a [WProtoMember], so its value exists only because a constructor ran. '{0}' declares SkipConstructor, which asks protobuf-net to allocate the instance UNINITIALIZED -- no constructor runs, no initializer runs, and a member the payload does not carry cannot restore it, so '{1}' arrives at its type's default on every deserialized instance. Allocate it where it is used instead, or give it a [WProtoMember] so the wire carries it. A [WProtoAfterDeserialization] hook is not enough on its own: protobuf-net does not invoke [ProtoAfterDeserialization] on a SkipConstructor contract. Suppress WPROTO033 at the declaration when the default really is a valid value for '{1}'.",
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
