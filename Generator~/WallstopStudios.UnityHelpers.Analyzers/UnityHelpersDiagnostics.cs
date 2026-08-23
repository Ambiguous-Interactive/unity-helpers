// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Analyzers
{
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// The <c>WUH###</c> family: diagnostics about code that already compiles and already works.
    /// </summary>
    /// <remarks>
    /// Distinct from <c>WPROTO###</c> in both prefix and policy. A WallstopProto diagnostic reports
    /// a serialization contract that cannot be honoured, so it is an error -- the alternative is an
    /// exception from inside a shipped player. A <c>WUH###</c> diagnostic reports an allocation or a
    /// footgun in code that is otherwise correct, so it is capped at a warning: a consumer taking a
    /// package upgrade must never find their build failing over one. Every member of this family is
    /// on by default (a consumer should get the safety without discovering it) and suppressible.
    /// </remarks>
    internal static class UnityHelpersDiagnostics
    {
        /// <summary>
        /// A method group handed to a lookup's value factory allocates a delegate on every call,
        /// cache hit included, on every C# version Unity ships.
        /// </summary>
        /// <remarks>
        /// The shape is invisible without a semantic model: <c>GetOrAdd(key, Factory)</c> and
        /// <c>GetOrAdd(key, cachedFactory)</c> are the same token in argument position, so
        /// <c>scripts/lint-concurrent-cache-fill.ps1</c> -- which does enforce that every
        /// <b>lambda</b> handed to one of these is <c>static</c> -- cannot tell them apart. A casing
        /// heuristic would be wrong the first time a field is named <c>Factory</c> (#538).
        /// </remarks>
        internal static readonly DiagnosticDescriptor CacheFactoryAllocatesPerCall =
            new DiagnosticDescriptor(
                "WUH001",
                "Lookup factory method group allocates on every call",
                "'{0}' is passed to '{1}' as a method group, so a new delegate is built on every call -- including the calls that never invoke it, which is every lookup that already has the key. Measured at 106 bytes per call over 400,000 warm hits. Hold it in a 'static readonly' delegate field and pass that field, or use an overload that takes the state separately with a 'static' lambda.",
                "Performance",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true
            );
    }
}
