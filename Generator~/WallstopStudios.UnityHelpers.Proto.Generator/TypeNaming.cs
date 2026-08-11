// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
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
            switch (type)
            {
                case IArrayTypeSymbol array:
                    return IsNameable(array.ElementType, compilation);
                case INamedTypeSymbol named:
                {
                    // Each container's OWN arguments count too. `Outer<Hidden>.Inner<int>` has only
                    // `int` among its own, and the name still cannot be written, so walking the
                    // containers for accessibility alone let that one through.
                    for (
                        INamedTypeSymbol current = named;
                        current != null;
                        current = current.ContainingType
                    )
                    {
                        if (!IsReachable(current, compilation))
                        {
                            return false;
                        }

                        foreach (ITypeSymbol argument in current.TypeArguments)
                        {
                            if (!IsNameable(argument, compilation))
                            {
                                return false;
                            }
                        }
                    }

                    return true;
                }
                default:
                    // A type parameter or a pointer has no name the registrar could write.
                    return false;
            }
        }

        private static bool IsReachable(INamedTypeSymbol type, Compilation compilation)
        {
            switch (type.DeclaredAccessibility)
            {
                case Accessibility.Public:
                    return true;
                case Accessibility.Internal:
                case Accessibility.ProtectedOrInternal:
                    // Internal to ANOTHER assembly needs InternalsVisibleTo, which this answers.
                    return compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);
                default:
                    return false;
            }
        }
    }
}
