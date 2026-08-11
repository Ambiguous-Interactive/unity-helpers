// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// The build errors this generator reports instead of skipping a contract it cannot serialize.
    /// </summary>
    /// <remarks>
    /// Every one of these is an error rather than a warning, and every message names the type, the
    /// member and the fix. The failure mode being avoided is a contract that silently gets no
    /// formatter and surfaces as an <c>InvalidOperationException</c> -- or, before this serializer
    /// existed, an opaque <c>ExecutionEngineException</c> -- from inside a shipped player.
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
                "'{0}.{1}' has type '{2}', which this generator cannot serialize yet. Supported: the integer and floating-point primitives, bool, string, byte[], enums, another [WProtoContract] type, Nullable<T> of any of those, and a single-dimension array or List<T> whose element is any of them except Nullable<T>. Annotate '{2}' with [WProtoContract] if it is yours, or remove [WProtoMember], or write a formatter by hand and register it with WProtoFormatterProvider.Register<T>.",
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
