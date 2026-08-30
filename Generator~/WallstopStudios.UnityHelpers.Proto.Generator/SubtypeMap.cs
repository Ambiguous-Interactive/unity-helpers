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
        private const string ContractAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoContractAttribute";
        private const string IncludeAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoIncludeAttribute";
        private const string NotSerializedAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoNotSerializedAttribute";

        private static readonly List<Include> None = new List<Include>();

        private readonly Dictionary<INamedTypeSymbol, List<Include>> _byBase;

        private SubtypeMap(
            Dictionary<INamedTypeSymbol, List<Include>> byBase,
            SubtypeTagManifest manifest
        )
        {
            _byBase = byBase;
            Manifest = manifest;
        }

        /// <summary>The committed field numbers the tag-less declarations were resolved from.</summary>
        internal SubtypeTagManifest Manifest { get; }

        /// <summary>
        /// Indexes every usable subtype declaration among <paramref name="contracts"/>.
        /// </summary>
        /// <param name="contracts">The compilation's <c>[WProtoContract]</c> types.</param>
        /// <param name="manifest">The assembly's committed field numbers, for tag-less declarations.</param>
        /// <returns>The map, empty when nothing declares a subtype relationship this way.</returns>
        /// <remarks>
        /// A declaration this rejects is one <see cref="Validate"/> reports at the type that wrote
        /// it, so the base's include set never carries an entry the developer was not told about.
        /// </remarks>
        internal static SubtypeMap Build(
            List<INamedTypeSymbol> contracts,
            SubtypeTagManifest manifest
        )
        {
            Dictionary<INamedTypeSymbol, List<Include>> byBase = new Dictionary<
                INamedTypeSymbol,
                List<Include>
            >(SymbolEqualityComparer.Default);
            HashSet<INamedTypeSymbol> explicitlyDeclared = new HashSet<INamedTypeSymbol>(
                SymbolEqualityComparer.Default
            );

            foreach (INamedTypeSymbol contract in contracts)
            {
                foreach (AttributeData attribute in contract.GetAttributes())
                {
                    if (
                        !TryRead(
                            attribute,
                            contract,
                            manifest,
                            out int tag,
                            out INamedTypeSymbol baseType,
                            out bool fromManifest,
                            out bool unassigned,
                            out string problem
                        )
                        || problem != null
                        || unassigned
                    )
                    {
                        continue;
                    }

                    if (!byBase.TryGetValue(baseType, out List<Include> declared))
                    {
                        declared = new List<Include>();
                        byBase[baseType] = declared;
                    }

                    declared.Add(new Include(tag, contract, fromManifest));
                    explicitlyDeclared.Add(contract);
                }
            }

            // Deriving from a contract IS the declaration (#613). A subclass that wrote nothing gets
            // the same include an explicit [WProtoSubtype] would have produced, numbered from the
            // same manifest -- so the two forms remain one thing to every consumer of this list, and
            // an attribute nobody can forget to write has replaced one they could.
            foreach (INamedTypeSymbol contract in contracts)
            {
                INamedTypeSymbol baseType = contract.BaseType;
                if (
                    baseType == null
                    || explicitlyDeclared.Contains(contract)
                    || DeclaredByInclude(baseType, contract)
                    || !IsSerializedBase(baseType, contract)
                )
                {
                    continue;
                }

                // Unassigned is not an error here: WPROTO041 already reports it, as a warning in the
                // editor so the assignment tool can see the type at all, and as an error anywhere
                // that can reach a player.
                if (!manifest.TryResolve(contract, baseType, out int tag))
                {
                    continue;
                }

                if (!byBase.TryGetValue(baseType, out List<Include> declared))
                {
                    declared = new List<Include>();
                    byBase[baseType] = declared;
                }

                declared.Add(new Include(tag, contract, true));
            }

            // Attribute discovery follows syntax-visit order, which is not a property of the source
            // a developer can see. Ordering here makes both the emitted dispatch chain and the
            // duplicate-tag diagnostic depend only on what was written.
            foreach (List<Include> declared in byBase.Values)
            {
                declared.Sort(Compare);
            }

            return new SubtypeMap(byBase, manifest);
        }

        /// <summary>
        /// Reports an implicit subtype the manifest has no field number for.
        /// </summary>
        /// <param name="report">The diagnostic sink.</param>
        /// <param name="subType">A contract whose base is a serialized base.</param>
        /// <param name="manifest">The assembly's committed field numbers.</param>
        /// <param name="editorCompilation">Whether <c>UNITY_EDITOR</c> is defined.</param>
        /// <returns><c>false</c> when the subtype has no number, so nothing may be emitted for it.</returns>
        /// <remarks>
        /// The same refusal a tag-less <c>[WProtoSubtype]</c> gets, and it has to be: an implicit
        /// subtype with no number has no wire representation at all, so omitting it from the base's
        /// chain silently would compile a build that throws on the first save -- exactly what
        /// deriving-is-declaring was introduced to stop. Split severity for the same reason
        /// <c>WPROTO041</c> already has one: an error in the editor would keep the assignment tool
        /// from ever seeing the type it has to number.
        /// </remarks>
        internal static bool ValidateImplicit(
            System.Action<Diagnostic> report,
            INamedTypeSymbol subType,
            SubtypeTagManifest manifest,
            bool editorCompilation
        )
        {
            INamedTypeSymbol baseType = subType.BaseType;
            if (
                baseType == null
                || Declares(subType)
                || DeclaredByInclude(baseType, subType)
                || !IsSerializedBase(baseType, subType)
                || manifest.TryResolve(subType, baseType, out int _)
            )
            {
                return true;
            }

            report(
                Diagnostic.Create(
                    WProtoDiagnostics.SubtypeTagUnassigned,
                    subType.Locations.Length == 0 ? Location.None : subType.Locations[0],
                    editorCompilation ? DiagnosticSeverity.Warning : DiagnosticSeverity.Error,
                    (IEnumerable<Location>)null,
                    (System.Collections.Immutable.ImmutableDictionary<string, string>)null,
                    subType.ToDisplayString(),
                    baseType.ToDisplayString(),
                    editorCompilation
                        ? WProtoDiagnostics.SubtypeTagUnassignedInEditor
                        : WProtoDiagnostics.SubtypeTagUnassignedInPlayer,
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        WProtoDiagnostics.SubtypeTagUnassignedInherited,
                        subType.ToDisplayString(),
                        baseType.ToDisplayString()
                    )
                )
            );
            return false;
        }

        /// <summary>
        /// Whether a base is one this compilation can add an implicit subtype to.
        /// </summary>
        /// <param name="baseType">The candidate base.</param>
        /// <param name="subType">The type that derives from it.</param>
        /// <returns><c>true</c> when an implicit include is honourable.</returns>
        /// <remarks>
        /// <para>
        /// Three refusals, and none of them is policy. A base in another assembly had its dispatch
        /// chain emitted when that assembly compiled, so nothing declared later could reach it. A
        /// generic base cannot be identified by one field number, because it is as many types as it
        /// has closures. And a base that carries no contract at all is not serialized.
        /// </para>
        /// <para>
        /// <b>The generator asks this same method</b> before deciding a subclass is an implicit
        /// contract, rather than repeating the conditions. They drifted once already: a
        /// classification that walked past a generic base demanded <c>partial</c> on every
        /// <c>SerializableDictionary.Cache&lt;T&gt;</c> box, for a type no dispatch chain could ever
        /// hold.
        /// </para>
        /// </remarks>
        internal static bool IsSerializedBase(INamedTypeSymbol baseType, INamedTypeSymbol subType)
        {
            return IsSerializedContract(baseType) && CanCarrySubtype(baseType, subType);
        }

        /// <summary>
        /// Whether a type is serialized by WallstopProto: it says so, or it inherits it.
        /// </summary>
        /// <param name="symbol">The type to classify; <c>null</c> is not serialized.</param>
        /// <returns><c>true</c> when a formatter belongs to it.</returns>
        /// <remarks>
        /// <para>
        /// Deriving from a contract IS the declaration. An attribute you can forget to write is not
        /// a pit of success, and forgetting this one produced a throw from a shipped player: the
        /// base's dispatch chain ends in a guard refusing any runtime type it does not declare
        /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/613">#613</see>).
        /// The wire never needed the attribute -- it needs a NUMBER, and the assigner mints and
        /// commits one from whatever it discovers.
        /// </para>
        /// <para>
        /// Inherited transitively, so a subclass of an IMPLICIT subtype is one too; and
        /// <c>[WProtoNotSerialized]</c> stops the walk rather than excluding one type, because a
        /// subclass of an opted-out type has no serialized ancestor between it and the contract
        /// either. The walk also stops where an include could not be honoured -- a generic step, or
        /// one across an assembly boundary -- so a type nothing could ever dispatch to is not
        /// classified as serialized and never asked for <c>partial</c>.
        /// </para>
        /// </remarks>
        internal static bool IsSerializedContract(INamedTypeSymbol symbol)
        {
            for (INamedTypeSymbol current = symbol; current != null; current = current.BaseType)
            {
                if (HasNotSerialized(current))
                {
                    return false;
                }

                if (HasContract(current))
                {
                    return true;
                }

                if (current.BaseType == null || !CanCarrySubtype(current.BaseType, current))
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether a base could hold this subtype in its dispatch chain at all, contract or not.
        /// </summary>
        /// <param name="baseType">The candidate base.</param>
        /// <param name="subType">The type that derives from it.</param>
        /// <returns><c>true</c> when the relationship is structurally expressible.</returns>
        /// <remarks>
        /// The structural half, kept apart from <see cref="IsSerializedContract"/> so the walk can
        /// step through an IMPLICIT base -- one that carries no attribute of its own -- without
        /// asking whether that base is a contract before it has been classified.
        /// </remarks>
        private static bool CanCarrySubtype(INamedTypeSymbol baseType, INamedTypeSymbol subType)
        {
            return baseType.OriginalDefinition.TypeParameters.Length == 0
                && subType.OriginalDefinition.TypeParameters.Length == 0
                && SymbolEqualityComparer.Default.Equals(
                    baseType.ContainingAssembly,
                    subType.ContainingAssembly
                );
        }

        private static bool HasNotSerialized(INamedTypeSymbol symbol)
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == NotSerializedAttribute)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether <paramref name="baseType"/> already names <paramref name="subType"/> in an
        /// <c>[WProtoInclude]</c>.
        /// </summary>
        /// <param name="baseType">The base to read.</param>
        /// <param name="subType">The subtype to look for.</param>
        /// <returns><c>true</c> when the relationship is declared from the base's end.</returns>
        /// <remarks>
        /// The third way a relationship can already exist, and the one an implicit include must not
        /// duplicate: a type the base names carries the base's own number, not a manifest entry, so
        /// synthesizing one would claim a second number for one type and demanding one would refuse
        /// a contract that is perfectly well declared.
        /// </remarks>
        private static bool DeclaredByInclude(INamedTypeSymbol baseType, INamedTypeSymbol subType)
        {
            foreach (AttributeData attribute in baseType.GetAttributes())
            {
                if (
                    attribute.AttributeClass?.ToDisplayString() != IncludeAttribute
                    || attribute.ConstructorArguments.Length < 2
                )
                {
                    continue;
                }

                if (
                    attribute.ConstructorArguments[1].Value is INamedTypeSymbol named
                    && SymbolEqualityComparer.Default.Equals(
                        named.OriginalDefinition,
                        subType.OriginalDefinition
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasContract(INamedTypeSymbol symbol)
        {
            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() == ContractAttribute)
                {
                    return true;
                }
            }

            return false;
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
        /// <param name="manifest">The assembly's committed field numbers, for tag-less declarations.</param>
        /// <param name="editorCompilation">
        /// Whether <c>UNITY_EDITOR</c> is defined for this compilation, which decides whether an
        /// unnumbered subtype is a warning the editor can repair or an error that cannot ship.
        /// </param>
        /// <returns><c>true</c> when every declaration is usable.</returns>
        internal static bool Validate(
            System.Action<Diagnostic> report,
            INamedTypeSymbol subType,
            bool orphaned,
            SubtypeTagManifest manifest,
            bool editorCompilation
        )
        {
            bool usable = true;
            foreach (AttributeData attribute in subType.GetAttributes())
            {
                if (
                    !TryRead(
                        attribute,
                        subType,
                        manifest,
                        out int tag,
                        out INamedTypeSymbol baseType,
                        out bool fromManifest,
                        out bool unassigned,
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

                if (problem == null && unassigned)
                {
                    // Reported as its own code rather than folded into WPROTO040: nothing is wrong
                    // with the declaration, and the fix is to run a tool rather than to edit it.
                    //
                    // An editor compilation gets a WARNING, because an error here is a deadlock:
                    // the assembly would not compile, the type would not exist, and the tool that
                    // discovers declarations through TypeCache could never see the very type it
                    // has to number. A compilation without UNITY_EDITOR can reach a player, where
                    // an unnumbered subtype is a save that throws, so there it stays an error.
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.SubtypeTagUnassigned,
                            LocationOf(attribute, subType),
                            editorCompilation
                                ? DiagnosticSeverity.Warning
                                : DiagnosticSeverity.Error,
                            (IEnumerable<Location>)null,
                            (System.Collections.Immutable.ImmutableDictionary<string, string>)null,
                            subType.ToDisplayString(),
                            baseType.ToDisplayString(),
                            editorCompilation
                                ? WProtoDiagnostics.SubtypeTagUnassignedInEditor
                                : WProtoDiagnostics.SubtypeTagUnassignedInPlayer,
                            string.Format(
                                System.Globalization.CultureInfo.InvariantCulture,
                                WProtoDiagnostics.SubtypeTagUnassignedDeclared,
                                subType.ToDisplayString(),
                                baseType.ToDisplayString()
                            )
                        )
                    );
                    // Refused whatever the severity. Emitting a formatter for a subtype the base's
                    // chain cannot reach would put a half-wired type into the assembly; withholding
                    // it leaves exactly the shape an error already produced, which is the one this
                    // suite has proven emits no CS diagnostics of its own.
                    usable = false;
                    continue;
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
                        Written(baseType, tag, fromManifest || unassigned),
                        problem
                    )
                );
                usable = false;
            }

            return usable;
        }

        /// <summary>
        /// Renders the declaration's argument list the way the developer wrote it.
        /// </summary>
        /// <param name="baseType">The base the declaration named.</param>
        /// <param name="tag">The field number in force.</param>
        /// <param name="tagless">Whether the source omitted the number.</param>
        /// <returns>The text between the attribute's parentheses.</returns>
        /// <remarks>
        /// A diagnostic that quoted a manifest number back at a developer who never typed one sends
        /// them looking through their own source for a number that is not in it.
        /// </remarks>
        internal static string Written(INamedTypeSymbol baseType, int tag, bool tagless)
        {
            string named = "typeof(" + (baseType == null ? "?" : baseType.Name) + ")";
            return tagless ? named : named + ", " + tag;
        }

        /// <summary>
        /// Reads one attribute as a subtype declaration.
        /// </summary>
        /// <param name="attribute">The attribute to inspect.</param>
        /// <param name="subType">The type it was written on.</param>
        /// <param name="manifest">The assembly's committed field numbers.</param>
        /// <param name="tag">The declared or committed field number.</param>
        /// <param name="baseType">The declared base, or <c>null</c> when unresolvable.</param>
        /// <param name="fromManifest">Whether <paramref name="tag"/> came from the manifest.</param>
        /// <param name="unassigned">
        /// <c>true</c> when the declaration omitted its number and the manifest has none for it, so
        /// the relationship is well-formed but has nothing to be written under.
        /// </param>
        /// <param name="problem">Why the declaration cannot be honoured, or <c>null</c>.</param>
        /// <returns><c>false</c> when the attribute is not a subtype declaration at all.</returns>
        /// <remarks>
        /// The manifest is consulted LAST, after everything about the pair of types has been
        /// checked. A tag-less declaration naming a base in another assembly has two things wrong
        /// with it, and "no field number is assigned" is the one that would send the developer to
        /// the assignment tool instead of to the real defect.
        /// </remarks>
        private static bool TryRead(
            AttributeData attribute,
            INamedTypeSymbol subType,
            SubtypeTagManifest manifest,
            out int tag,
            out INamedTypeSymbol baseType,
            out bool fromManifest,
            out bool unassigned,
            out string problem
        )
        {
            tag = 0;
            baseType = null;
            fromManifest = false;
            unassigned = false;
            problem = null;

            if (
                attribute.AttributeClass == null
                || attribute.AttributeClass.ToDisplayString() != SubtypeAttribute
                || attribute.ConstructorArguments.Length < 1
                || 2 < attribute.ConstructorArguments.Length
            )
            {
                return false;
            }

            bool tagless = attribute.ConstructorArguments.Length == 1;
            baseType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
            tag = tagless ? 0 : (int)(attribute.ConstructorArguments[1].Value ?? 0);

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

            // Asked the implicit-aware way. Reading only the attribute rejected a base that
            // inherits its own contract, so the fix WPROTO041 suggests -- write [WProtoSubtype]
            // yourself -- failed on exactly the hierarchies deriving-is-declaring introduced.
            if (!IsSerializedContract(baseType))
            {
                problem =
                    "'"
                    + baseType.Name
                    + "' is not serialized: it carries no [WProtoContract] and inherits none";
                return true;
            }

            if (
                !SymbolEqualityComparer.Default.Equals(
                    baseType.ContainingAssembly,
                    subType.ContainingAssembly
                )
            )
            {
                // A per-assembly generator emits the base's dispatch chain when the base's assembly
                // compiles, so a subtype declared afterwards in a referencing assembly is not late
                // to a list -- it is outside the compilation that built the list. That is a fact
                // about THIS mechanism, and the message says only that.
                //
                // A runtime registry would close the gap and is refused: unordered registrars, two
                // packages claiming one tag, and a lookup stripping under IL2CPP are all silent
                // data corruption rather than build errors
                // (https://github.com/Ambiguous-Interactive/unity-helpers/issues/603). Emitting the
                // base's chain in the EXTENDING assembly instead is neither a registry nor
                // refused, and is tracked on
                // https://github.com/Ambiguous-Interactive/unity-helpers/issues/612 -- so the
                // message must not tell a developer the feature can never exist.
                problem =
                    "'"
                    + baseType.Name
                    + "' is compiled into assembly '"
                    + (baseType.ContainingAssembly == null ? "?" : baseType.ContainingAssembly.Name)
                    + "' and '"
                    + subType.Name
                    + "' into '"
                    + (subType.ContainingAssembly == null ? "?" : subType.ContainingAssembly.Name)
                    + "'. The base's dispatch chain is generated when its own assembly is compiled, "
                    + "so a subtype declared afterwards in an assembly that references it cannot "
                    + "appear in that chain, and accepting the declaration would compile and then "
                    + "throw on the first save. Either move '"
                    + subType.Name
                    + "' into '"
                    + (baseType.ContainingAssembly == null ? "?" : baseType.ContainingAssembly.Name)
                    + "', or give '"
                    + subType.Name
                    + "' a [WProtoContract] of its own and hold a '"
                    + baseType.Name
                    + "' in it as a [WProtoMember] instead of deriving from it -- a member of a "
                    + "type from another assembly is generated normally, and the base writes its "
                    + "own subtypes through its own chain";
                return true;
            }

            if (tagless)
            {
                // The number is looked up, never derived. A generator sees whatever subset of the
                // project the current compilation contains and runs again on every keystroke, so
                // anything it computed here would be a wire contract that moves.
                if (!manifest.TryResolve(subType, baseType, out tag))
                {
                    unassigned = true;
                    return true;
                }

                fromManifest = true;
            }

            if (tag < 1 || 536870911 < tag || (19000 <= tag && tag <= 19999))
            {
                problem =
                    "field number "
                    + tag
                    + " is outside 1-536870911 or inside the reserved 19000-19999 range";
                return true;
            }

            // A hand-written number is checked against the retirement record, and a manifest one is
            // not: an entry that collides with a retirement is WPROTO042 at the manifest line that
            // holds it, and reporting the same collision twice sends the developer to the
            // declaration rather than to the file the number actually lives in. The name is what
            // decides, not the number -- re-adding the type the number belonged to is the case
            // retirement exists to serve (#606).
            if (
                !tagless
                && manifest.TryRetired(baseType, tag, out string retiredBy)
                && retiredBy != subType.ToDisplayString()
            )
            {
                problem = RetiredProblem(tag, baseType, retiredBy);
            }

            return true;
        }

        /// <summary>
        /// Explains why a retired field number cannot be handed to another subtype.
        /// </summary>
        /// <param name="tag">The field number being claimed.</param>
        /// <param name="baseType">The base it lives on.</param>
        /// <param name="retiredBy">The fully qualified name of the type that held it.</param>
        /// <returns>The clause a subtype or include diagnostic appends.</returns>
        internal static string RetiredProblem(int tag, INamedTypeSymbol baseType, string retiredBy)
        {
            return "field number "
                + tag
                + " on '"
                + (baseType == null ? "?" : baseType.Name)
                + "' is retired, having belonged to '"
                + retiredBy
                + "'. Payloads written before that type was removed still carry it under this "
                + "number, so handing it to another type reads those saves back as the wrong "
                + "type. Give this one a free number, or restore the deleted type under its own "
                + "name";
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
