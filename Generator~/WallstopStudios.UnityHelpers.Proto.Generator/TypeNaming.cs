// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System;
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// Whether the registrar can write a type's name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registrar is a type of its own, so "accessible somewhere in this assembly" is not the
    /// question -- a <c>private</c> nested type is accessible only to its container, and naming one
    /// from the registrar is <c>CS0122</c> in the consumer's own build. That is a worse failure than
    /// the missing registration it replaces: their code stops compiling because of a type they
    /// declared privately and never asked us to serialize.
    /// </para>
    /// <para>
    /// <see cref="Compilation.IsSymbolAccessibleWithin"/> does not answer it. Asked with an assembly
    /// as the context it reports on <c>internal</c> visibility alone and says <c>true</c> for a
    /// private nested type, which is how <c>SerializableDictionary&lt;string,
    /// SomeFixture.PrivatePayload&gt;</c> reached a registrar and failed to compile.
    /// </para>
    /// </remarks>
    internal static class TypeNaming
    {
        /// <summary>
        /// The name of <paramref name="type"/> as a developer would have written it.
        /// </summary>
        /// <param name="type">The type to name.</param>
        /// <returns>The name, for a message a human reads.</returns>
        /// <remarks>
        /// Roslyn spells a rectangular array <c>int[*,*]</c>, which is the C# compiler's own
        /// disambiguating form rather than anything anybody typed. A diagnostic or a runtime refusal
        /// that names a member's type has to name it the way its declaration does, or the reader has
        /// to translate the message before they can search for what it is about.
        /// </remarks>
        internal static string Display(ITypeSymbol type)
        {
            return type.ToDisplayString().Replace("[*", "[").Replace(",*", ",");
        }

        /// <summary>
        /// Reports whether <paramref name="type"/> can be named from anywhere in the compilation.
        /// </summary>
        /// <param name="type">The type to name.</param>
        /// <param name="compilation">The compilation the registrar is emitted into.</param>
        /// <returns><c>true</c> when every part of the name is reachable from the registrar.</returns>
        /// <remarks>
        /// Every part has to be nameable: the type, each type it is nested in, and each type
        /// argument, at any depth. A public generic closed over a private type is as unnameable as a
        /// private one.
        /// </remarks>
        internal static bool IsNameable(ITypeSymbol type, Compilation compilation)
        {
            return !TryFindUnnameable(type, compilation, out ITypeSymbol _);
        }

        /// <summary>
        /// Finds the part of <paramref name="type"/>'s name the registrar cannot write.
        /// </summary>
        /// <param name="type">The type to name.</param>
        /// <param name="compilation">The compilation the registrar is emitted into.</param>
        /// <param name="culprit">Receives the offending part, or <c>null</c> when there is none.</param>
        /// <returns><c>true</c> when the name cannot be written.</returns>
        /// <remarks>
        /// The part is reported rather than just the answer because the skip it causes is silent:
        /// the closure gets no formatter and throws on its first serialization, and a message that
        /// named only the closure would leave the reader to work out which of its pieces was the
        /// problem.
        /// </remarks>
        internal static bool TryFindUnnameable(
            ITypeSymbol type,
            Compilation compilation,
            out ITypeSymbol culprit
        )
        {
            switch (type)
            {
                case IArrayTypeSymbol array:
                    return TryFindUnnameable(array.ElementType, compilation, out culprit);
                case INamedTypeSymbol named:
                {
                    // Containing generic arguments also affect nameability, as in Outer<Hidden>.Inner<int>.
                    for (
                        INamedTypeSymbol current = named;
                        current != null;
                        current = current.ContainingType
                    )
                    {
                        if (!IsReachable(current, compilation))
                        {
                            culprit = current;
                            return true;
                        }

                        foreach (ITypeSymbol argument in current.TypeArguments)
                        {
                            if (TryFindUnnameable(argument, compilation, out culprit))
                            {
                                return true;
                            }
                        }
                    }

                    culprit = null;
                    return false;
                }
                case IDynamicTypeSymbol _:
                    // dynamic is nameable syntax even though it is not an accessible declaration.
                    culprit = null;
                    return false;
                default:

                    culprit = type;
                    return true;
            }
        }

        /// <summary>
        /// Reports whether any part of <paramref name="type"/> is still a type parameter.
        /// </summary>
        /// <param name="type">The type to inspect.</param>
        /// <returns><c>true</c> when the construction is not closed.</returns>
        /// <remarks>
        /// An open construction is unnameable too, and deliberately silent: it is the generic
        /// declaration itself rather than a closure someone will serialize, so warning about it
        /// would fire on every generic contract in the source that declares it.
        /// </remarks>
        internal static bool IsOpen(ITypeSymbol type)
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
                    // Containing types can remain open even when the nested type's own arguments are closed.
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

        private static bool IsReachable(INamedTypeSymbol type, Compilation compilation)
        {
            /*
             * Roslyn 3.8 lacks IsFileLocal; its generated metadata prefix identifies types inaccessible from
             * registrar files.
             */
            if (type.MetadataName.StartsWith("<", StringComparison.Ordinal))
            {
                return false;
            }

            switch (type.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    return true;
                case Accessibility.Internal:
                case Accessibility.ProtectedOrInternal:
                    // Cross-assembly internal access requires InternalsVisibleTo.
                    return compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);
                default:
                    return false;
            }
        }
    }
}
