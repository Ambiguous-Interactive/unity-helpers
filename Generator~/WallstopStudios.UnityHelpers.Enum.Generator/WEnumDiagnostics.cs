// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Enum.Generator
{
    using Microsoft.CodeAnalysis;

    /// <summary>Diagnostics reported while generating per-enum fast-path overloads.</summary>
    internal static class WEnumDiagnostics
    {
        private const string Category = "WallstopStudios.UnityHelpers.EnumGeneration";

        /// <summary>An annotated enum cannot receive generated overloads; the generic helpers remain.</summary>
        public static readonly DiagnosticDescriptor NotGenerated = new(
            id: "WENUM001",
            title: "Enum fast-path helpers were not generated",
            messageFormat: "Enum helpers were not generated for '{0}': {1}. The generalized enum helpers remain available.",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true
        );
    }
}
