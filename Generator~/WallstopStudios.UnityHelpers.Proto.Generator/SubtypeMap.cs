// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// The <c>[WProtoSubtype(typeof(Base), tag)]</c> declarations a compilation contains, indexed by
    /// the base they name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The inverse of <c>[WProtoInclude]</c>, and deliberately nothing more: a declaration read here
    /// becomes an ordinary <see cref="Include"/> on the base's list, so every consumer of that list
    /// -- the dispatch chain, <c>CanWrite</c>, the measure and read paths -- sees one kind of thing
    /// and the two forms produce identical bytes. Which end declares the relationship is a source
    /// choice; the wire cannot tell.
    /// </para>
    /// <para>
    /// Built from the contracts of ONE compilation, because that is all a per-assembly generator can
    /// honour. A subtype in another assembly is refused rather than merged: the base's formatter was
    /// emitted when the base's assembly was compiled, so a declaration made later could never have
    /// reached its dispatch chain, and accepting it would produce a build that throws on the first
    /// save instead of one that fails to compile.
    /// </para>
    /// </remarks>
    internal sealed class SubtypeMap
    {
        private const string SubtypeAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoSubtypeAttribute";

        private static readonly List<Include> None = new List<Include>();

        private readonly Dictionary<INamedTypeSymbol, List<Include>> _byBase;

        private SubtypeMap(Dictionary<INamedTypeSymbol, List<Include>> byBase)
        {
            _byBase = byBase;
        }

        /// <summary>
        /// Indexes every usable subtype declaration among <paramref name="contracts"/>.
        /// </summary>
        /// <param name="contracts">The compilation's <c>[WProtoContract]</c> types.</param>
        /// <returns>The map, empty when nothing declares a subtype relationship this way.</returns>
        /// <remarks>
        /// A declaration this rejects is one <see cref="Validate"/> reports at the type that wrote
        /// it, so the base's include set never carries an entry the developer was not told about.
        /// </remarks>
        internal static SubtypeMap Build(List<INamedTypeSymbol> contracts)
        {
            Dictionary<INamedTypeSymbol, List<Include>> byBase = new Dictionary<
                INamedTypeSymbol,
                List<Include>
            >(SymbolEqualityComparer.Default);

            foreach (INamedTypeSymbol contract in contracts)
            {
                foreach (AttributeData attribute in contract.GetAttributes())
                {
                    if (
                        !TryRead(
                            attribute,
                            contract,
                            out int tag,
                            out INamedTypeSymbol baseType,
                            out string problem
                        )
                        || problem != null
                    )
                    {
                        continue;
                    }

                    if (!byBase.TryGetValue(baseType, out List<Include> declared))
                    {
                        declared = new List<Include>();
                        byBase[baseType] = declared;
                    }

                    declared.Add(new Include(tag, contract));
                }
            }

            // Attribute discovery follows syntax-visit order, which is not a property of the source
            // a developer can see. Ordering here makes both the emitted dispatch chain and the
            // duplicate-tag diagnostic depend only on what was written.
            foreach (List<Include> declared in byBase.Values)
            {
                declared.Sort(Compare);
            }

            return new SubtypeMap(byBase);
        }

        /// <summary>
        /// The subtypes that declared themselves against <paramref name="baseType"/>.
        /// </summary>
        /// <param name="baseType">The contract whose include set is being built.</param>
        /// <returns>The declarations, ordered by field number; never <c>null</c>.</returns>
        internal List<Include> For(INamedTypeSymbol baseType)
        {
            return _byBase.TryGetValue(baseType, out List<Include> declared) ? declared : None;
        }

        /// <summary>
        /// Reports whether <paramref name="subType"/> declares its own relationship to a base.
        /// </summary>
        /// <param name="subType">The contract to inspect.</param>
        /// <returns><c>true</c> when it carries any <c>[WProtoSubtype]</c>.</returns>
        /// <remarks>
        /// Asked without validating, so a malformed declaration suppresses <c>WPROTO018</c> as a
        /// well-formed one does: the developer has already been told what is wrong with it, and a
        /// second error saying the relationship was never declared would name the wrong problem.
        /// </remarks>
        internal static bool Declares(INamedTypeSymbol subType)
        {
            foreach (AttributeData attribute in subType.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == SubtypeAttribute)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reports every unusable <c>[WProtoSubtype]</c> on <paramref name="subType"/>.
        /// </summary>
        /// <param name="report">The diagnostic sink.</param>
        /// <param name="subType">The type carrying the declarations.</param>
        /// <param name="orphaned">
        /// <c>true</c> when <paramref name="subType"/> has no <c>[WProtoContract]</c>, which makes
        /// every declaration on it unusable whatever else it says.
        /// </param>
        /// <returns><c>true</c> when every declaration is usable.</returns>
        internal static bool Validate(
            System.Action<Diagnostic> report,
            INamedTypeSymbol subType,
            bool orphaned
        )
        {
            bool usable = true;
            foreach (AttributeData attribute in subType.GetAttributes())
            {
                if (
                    !TryRead(
                        attribute,
                        subType,
                        out int tag,
                        out INamedTypeSymbol baseType,
                        out string problem
                    )
                )
                {
                    continue;
                }

                if (orphaned)
                {
                    problem =
                        "'"
                        + subType.Name
                        + "' has no [WProtoContract] of its own, so no formatter is generated for it "
                        + "and there would be nothing for the base to write under that field number";
                }

                if (problem == null)
                {
                    continue;
                }

                report(
                    Diagnostic.Create(
                        WProtoDiagnostics.BadSubtype,
                        LocationOf(attribute, subType),
                        subType.Name,
                        baseType == null ? "?" : baseType.Name,
                        tag,
                        problem
                    )
                );
                usable = false;
            }

            return usable;
        }

        /// <summary>
        /// Reads one attribute as a subtype declaration.
        /// </summary>
        /// <param name="attribute">The attribute to inspect.</param>
        /// <param name="subType">The type it was written on.</param>
        /// <param name="tag">The declared field number.</param>
        /// <param name="baseType">The declared base, or <c>null</c> when unresolvable.</param>
        /// <param name="problem">Why the declaration cannot be honoured, or <c>null</c>.</param>
        /// <returns><c>false</c> when the attribute is not a subtype declaration at all.</returns>
        private static bool TryRead(
            AttributeData attribute,
            INamedTypeSymbol subType,
            out int tag,
            out INamedTypeSymbol baseType,
            out string problem
        )
        {
            tag = 0;
            baseType = null;
            problem = null;

            if (
                attribute.AttributeClass == null
                || attribute.AttributeClass.ToDisplayString() != SubtypeAttribute
                || attribute.ConstructorArguments.Length < 2
            )
            {
                return false;
            }

            baseType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
            tag = (int)(attribute.ConstructorArguments[1].Value ?? 0);

            if (baseType == null)
            {
                problem = "the base type could not be resolved";
                return true;
            }

            // A field number identifies ONE type on the wire, and a generic subtype is as many
            // types as it has closures. There is no answer to which of them tag 5 means, and the
            // dispatch chain lives in the base's formatter where the subtype's type parameters
            // are not even in scope.
            if (IsGenericAnywhere(subType))
            {
                problem =
                    "'"
                    + subType.Name
                    + "' is generic, or is nested inside a generic type, so one field number "
                    + "cannot identify it -- each closed construction would be a different type "
                    + "under the same number";
                return true;
            }

            // The formatter is emitted once, for the generic DEFINITION, so a chain naming a
            // subtype of one closure would run for every closure. [WProtoInclude] refuses the
            // same arrangement (WPROTO013); this refuses it from the other end rather than
            // dropping the declaration where no formatter would ever carry it.
            if (!SymbolEqualityComparer.Default.Equals(baseType, baseType.OriginalDefinition))
            {
                problem =
                    "'"
                    + baseType.Name
                    + "' is named as a constructed generic type. One formatter is emitted for the "
                    + "generic definition and serves every closure of it, so a subtype declared "
                    + "against one closure would be written for all of them";
                return true;
            }

            if (!SymbolEqualityComparer.Default.Equals(subType.BaseType, baseType))
            {
                // Measured: protobuf-net 3.2.56 refuses a grandchild declared on the grandparent
                // with "Unexpected sub-type". Re-parenting the declaration onto whichever ancestor
                // was named would write the value under that level's tag and read it back as that
                // level, so the deeper type is silently lost rather than refused.
                problem =
                    "'"
                    + subType.Name
                    + "' does not derive DIRECTLY from '"
                    + baseType.Name
                    + "'; name the type it actually derives from, '"
                    + (subType.BaseType == null ? "object" : subType.BaseType.Name)
                    + "'";
                return true;
            }

            if (!Shape.IsContract(baseType))
            {
                problem = "'" + baseType.Name + "' is not itself a [WProtoContract]";
                return true;
            }

            if (
                !SymbolEqualityComparer.Default.Equals(
                    baseType.ContainingAssembly,
                    subType.ContainingAssembly
                )
            )
            {
                problem =
                    "'"
                    + baseType.Name
                    + "' is compiled into assembly '"
                    + (baseType.ContainingAssembly == null ? "?" : baseType.ContainingAssembly.Name)
                    + "' and '"
                    + subType.Name
                    + "' into '"
                    + (subType.ContainingAssembly == null ? "?" : subType.ContainingAssembly.Name)
                    + "'. The base's dispatch chain was generated when its own assembly was compiled "
                    + "and nothing added later can appear in it, so this subtype would compile and "
                    + "then throw on the first save. Move '"
                    + subType.Name
                    + "' into '"
                    + (baseType.ContainingAssembly == null ? "?" : baseType.ContainingAssembly.Name)
                    + "', or hold it behind a contract of its own rather than as its base";
                return true;
            }

            if (tag < 1 || 536870911 < tag || (19000 <= tag && tag <= 19999))
            {
                problem =
                    "field number "
                    + tag
                    + " is outside 1-536870911 or inside the reserved 19000-19999 range";
            }

            return true;
        }

        /// <summary>
        /// Reports whether <paramref name="type"/> or anything enclosing it takes type arguments.
        /// </summary>
        /// <param name="type">The type to inspect.</param>
        /// <returns><c>true</c> when it has no single closed identity.</returns>
        private static bool IsGenericAnywhere(INamedTypeSymbol type)
        {
            for (INamedTypeSymbol current = type; current != null; current = current.ContainingType)
            {
                if (0 < current.Arity)
                {
                    return true;
                }
            }

            return false;
        }

        private static int Compare(Include left, Include right)
        {
            int byTag = left.Tag.CompareTo(right.Tag);
            return byTag != 0 ? byTag : string.CompareOrdinal(left.Qualified, right.Qualified);
        }

        private static Location LocationOf(AttributeData attribute, INamedTypeSymbol subType)
        {
            SyntaxReference reference = attribute.ApplicationSyntaxReference;
            return reference == null
                ? subType.Locations.Length == 0
                    ? Location.None
                    : subType.Locations[0]
                : reference.GetSyntax().GetLocation();
        }
    }
}
