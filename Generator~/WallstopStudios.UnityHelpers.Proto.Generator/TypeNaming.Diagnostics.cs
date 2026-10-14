// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System;
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    internal static partial class TypeNaming
    {
        /// <summary>
        /// Announces a closed construction the registrar has to skip, once per construction.
        /// </summary>
        /// <param name="type">The closed construction found in source.</param>
        /// <param name="compilation">The compilation the registrar is emitted into.</param>
        /// <param name="location">Where the construction was written.</param>
        /// <param name="report">Receives the warning.</param>
        /// <param name="announced">The constructions already reported in this compilation.</param>
        /// <returns><c>true</c> when the caller must skip it.</returns>
        /// <remarks>
        /// Deduplicated because the scan runs over every piece of type syntax: a closure written in
        /// ten places is one missing registration, not ten.
        /// </remarks>
        internal static bool ReportIfUnnameable(
            ITypeSymbol type,
            Compilation compilation,
            Location location,
            Action<Diagnostic> report,
            HashSet<string> announced
        )
        {
            if (IsOpen(type) || !TryFindUnnameable(type, compilation, out ITypeSymbol culprit))
            {
                return IsOpen(type);
            }

            string qualified = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (announced.Add(qualified))
            {
                report(
                    Diagnostic.Create(
                        WProtoDiagnostics.UnnameableClosure,
                        location,
                        qualified,
                        culprit.ToDisplayString()
                    )
                );
            }

            return true;
        }
    }
}
