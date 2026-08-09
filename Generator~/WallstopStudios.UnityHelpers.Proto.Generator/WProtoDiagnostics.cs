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
                "'{0}.{1}' has type '{2}', which this generator cannot serialize yet. Supported: the integer and floating-point primitives, bool, string, byte[], enums, and Nullable<T> of any of those. Remove [WProtoMember], or write a formatter by hand and register it with WProtoFormatterProvider.Register<T>.",
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
            "WallstopProto cannot generate for a generic contract yet",
            "'{0}' is a generic [WProtoContract]. Closing a generic at the assembly that uses it is planned, but not implemented; write a formatter for each closed construction by hand and register it with WProtoFormatterProvider.Register<T> in the meantime.",
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
