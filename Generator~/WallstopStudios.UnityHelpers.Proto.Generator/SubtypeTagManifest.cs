// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator
{
    using System;
    using System.Collections.Generic;
    using Microsoft.CodeAnalysis;

    /// <summary>
    /// The committed field numbers a compilation's <c>[assembly: WProtoSubtypeTag]</c> entries
    /// assign, and the retired numbers its <c>[assembly: WProtoRetiredSubtypeTag]</c> entries hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the whole of the zero-touch mechanism on the generator side, and its most important
    /// property is what it does NOT do: it never invents a number. A generator runs in memory,
    /// repeatedly, inside an IDE, against whatever subset of the project happens to be open, so a
    /// number it derived would depend on which types it could see -- and a field number that depends
    /// on anything but a committed decision is a wire contract that changes under saved data. A
    /// declaration with no entry is <c>WPROTO041</c>, which names the tool that writes one.
    /// </para>
    /// <para>
    /// Read from the compilation's own assembly only. A subtype and its base must already share an
    /// assembly (<see cref="SubtypeMap"/> refuses anything else), so an entry for that pair can only
    /// have been written by that assembly, and honouring one from a reference would let an
    /// unrelated package's manifest decide this one's wire.
    /// </para>
    /// </remarks>
    internal sealed class SubtypeTagManifest
    {
        internal const string TagAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoSubtypeTagAttribute";

        internal const string RetiredAttribute =
            "WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoRetiredSubtypeTagAttribute";

        private static readonly SubtypeTagManifest EmptyManifest = new SubtypeTagManifest(
            new Dictionary<string, int>(StringComparer.Ordinal)
        );

        private readonly Dictionary<string, int> _assigned;

        private SubtypeTagManifest(Dictionary<string, int> assigned)
        {
            _assigned = assigned;
        }

        /// <summary>A manifest with no entries, for a compilation that declares none.</summary>
        internal static SubtypeTagManifest Empty => EmptyManifest;

        /// <summary>
        /// Indexes the compilation's own manifest entries.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <returns>The manifest; empty when the assembly declares nothing.</returns>
        /// <remarks>
        /// The FIRST entry for a pair wins, and <see cref="Validate"/> reports every later one. A
        /// duplicate resolved by "last wins" would make the emitted field number depend on attribute
        /// order, which is the one thing the manifest exists to take out of the picture.
        /// </remarks>
        internal static SubtypeTagManifest Build(Compilation compilation)
        {
            Dictionary<string, int> assigned = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (AttributeData attribute in compilation.Assembly.GetAttributes())
            {
                if (
                    !TryReadEntry(
                        attribute,
                        out INamedTypeSymbol subType,
                        out INamedTypeSymbol baseType,
                        out int tag
                    )
                )
                {
                    continue;
                }

                string key = KeyOf(subType, baseType);
                if (!assigned.ContainsKey(key))
                {
                    assigned[key] = tag;
                }
            }

            return assigned.Count == 0 ? EmptyManifest : new SubtypeTagManifest(assigned);
        }

        /// <summary>
        /// Looks up the number committed for one subtype-base pair.
        /// </summary>
        /// <param name="subType">The subtype whose declaration omitted a number.</param>
        /// <param name="baseType">The base it named.</param>
        /// <param name="tag">The committed field number.</param>
        /// <returns><c>false</c> when the manifest has no entry for the pair.</returns>
        internal bool TryResolve(INamedTypeSymbol subType, INamedTypeSymbol baseType, out int tag)
        {
            if (subType == null || baseType == null)
            {
                tag = 0;
                return false;
            }

            return _assigned.TryGetValue(KeyOf(subType, baseType), out tag);
        }

        /// <summary>
        /// Reports every manifest entry this compilation declares that cannot be honoured.
        /// </summary>
        /// <param name="compilation">The compilation being generated for.</param>
        /// <param name="report">Receives one diagnostic per unusable entry.</param>
        /// <remarks>
        /// Only the compilation's OWN attributes are checked, matching <see cref="SurrogateMap"/>
        /// and <see cref="MarshalMap"/>: an entry in a referenced assembly was validated when that
        /// assembly was built, and this compilation never reads it.
        /// </remarks>
        internal static void Validate(Compilation compilation, Action<Diagnostic> report)
        {
            HashSet<string> pairs = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, string> tagOwners = new Dictionary<string, string>(
                StringComparer.Ordinal
            );
            Dictionary<string, string> retiredOwners = new Dictionary<string, string>(
                StringComparer.Ordinal
            );

            foreach (AttributeData attribute in compilation.Assembly.GetAttributes())
            {
                if (
                    attribute.AttributeClass == null
                    || attribute.AttributeClass.ToDisplayString() != RetiredAttribute
                    || attribute.ConstructorArguments.Length < 3
                )
                {
                    continue;
                }

                string retiredName = attribute.ConstructorArguments[0].Value as string;
                INamedTypeSymbol retiredBase =
                    attribute.ConstructorArguments[1].Value as INamedTypeSymbol;
                int retiredTag = (int)(attribute.ConstructorArguments[2].Value ?? 0);
                string written =
                    "[assembly: WProtoRetiredSubtypeTag(\""
                    + (retiredName ?? "?")
                    + "\", typeof("
                    + (retiredBase == null ? "?" : retiredBase.Name)
                    + "), "
                    + retiredTag
                    + ")]";

                string retiredProblem = null;
                if (string.IsNullOrEmpty(retiredName))
                {
                    retiredProblem =
                        "it names no type. A retired number belongs to the type that held it, and an "
                        + "unnamed one cannot be restored when that type is added back";
                }
                else if (retiredBase == null)
                {
                    retiredProblem = "the base type could not be resolved";
                }
                else if (!IsUsableFieldNumber(retiredTag))
                {
                    retiredProblem = OutOfRange(retiredTag);
                }

                if (retiredProblem != null)
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.BadSubtypeTagManifest,
                            LocationOf(attribute),
                            written,
                            retiredProblem
                        )
                    );
                    continue;
                }

                retiredOwners[TagKeyOf(retiredBase, retiredTag)] = retiredName;
            }

            foreach (AttributeData attribute in compilation.Assembly.GetAttributes())
            {
                if (
                    attribute.AttributeClass == null
                    || attribute.AttributeClass.ToDisplayString() != TagAttribute
                    || attribute.ConstructorArguments.Length < 3
                )
                {
                    continue;
                }

                INamedTypeSymbol subType =
                    attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
                INamedTypeSymbol baseType =
                    attribute.ConstructorArguments[1].Value as INamedTypeSymbol;
                int tag = (int)(attribute.ConstructorArguments[2].Value ?? 0);
                string written =
                    "[assembly: WProtoSubtypeTag(typeof("
                    + (subType == null ? "?" : subType.Name)
                    + "), typeof("
                    + (baseType == null ? "?" : baseType.Name)
                    + "), "
                    + tag
                    + ")]";

                string problem = null;
                if (subType == null || baseType == null)
                {
                    problem = "one of the two types could not be resolved";
                }
                else if (!IsUsableFieldNumber(tag))
                {
                    problem = OutOfRange(tag);
                }
                else if (!pairs.Add(KeyOf(subType, baseType)))
                {
                    problem =
                        "'"
                        + subType.Name
                        + "' already has a number on '"
                        + baseType.Name
                        + "'. Only the first entry is read, so the other one is a field number "
                        + "nothing writes -- and which of the two that is would be attribute order";
                }
                else if (
                    tagOwners.TryGetValue(TagKeyOf(baseType, tag), out string taken)
                    && taken != subType.ToDisplayString()
                )
                {
                    problem =
                        "field number "
                        + tag
                        + " on '"
                        + baseType.Name
                        + "' is already assigned to '"
                        + taken
                        + "'. A payload resolves a subtype by that number alone, so one number "
                        + "cannot name two types";
                }
                else if (
                    retiredOwners.TryGetValue(TagKeyOf(baseType, tag), out string retiredBy)
                    && retiredBy != subType.ToDisplayString()
                )
                {
                    problem =
                        "field number "
                        + tag
                        + " on '"
                        + baseType.Name
                        + "' is retired, having belonged to '"
                        + retiredBy
                        + "'. Payloads written before that type was removed still carry it under "
                        + "this number, so handing it to another type reads those saves back as the "
                        + "wrong type";
                }

                if (problem != null)
                {
                    report(
                        Diagnostic.Create(
                            WProtoDiagnostics.BadSubtypeTagManifest,
                            LocationOf(attribute),
                            written,
                            problem
                        )
                    );
                    continue;
                }

                tagOwners[TagKeyOf(baseType, tag)] = subType.ToDisplayString();
            }
        }

        private static bool TryReadEntry(
            AttributeData attribute,
            out INamedTypeSymbol subType,
            out INamedTypeSymbol baseType,
            out int tag
        )
        {
            subType = null;
            baseType = null;
            tag = 0;

            if (
                attribute.AttributeClass == null
                || attribute.AttributeClass.ToDisplayString() != TagAttribute
                || attribute.ConstructorArguments.Length < 3
            )
            {
                return false;
            }

            subType = attribute.ConstructorArguments[0].Value as INamedTypeSymbol;
            baseType = attribute.ConstructorArguments[1].Value as INamedTypeSymbol;
            tag = (int)(attribute.ConstructorArguments[2].Value ?? 0);

            return subType != null && baseType != null && IsUsableFieldNumber(tag);
        }

        private static bool IsUsableFieldNumber(int tag)
        {
            return 1 <= tag && tag <= 536870911 && (tag < 19000 || 19999 < tag);
        }

        private static string OutOfRange(int tag)
        {
            return "field number "
                + tag
                + " is outside 1-536870911 or inside the reserved 19000-19999 range";
        }

        private static string KeyOf(INamedTypeSymbol subType, INamedTypeSymbol baseType)
        {
            return subType.ToDisplayString() + "|" + baseType.ToDisplayString();
        }

        private static string TagKeyOf(INamedTypeSymbol baseType, int tag)
        {
            return baseType.ToDisplayString() + "|" + tag;
        }

        private static Location LocationOf(AttributeData attribute)
        {
            SyntaxReference reference = attribute.ApplicationSyntaxReference;
            return reference == null ? Location.None : reference.GetSyntax().GetLocation();
        }
    }
}
