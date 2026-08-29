// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;

    /// <summary>
    /// The field numbers an assembly's subtype tag manifest should contain, computed from the
    /// declarations it holds and the manifest it already has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately free of Unity and of the file system: everything here is names and numbers, so
    /// the rules that make the manifest safe -- never renumber, never reuse a retired number,
    /// restore a re-added type's own number -- are decided by a pure function that a test can call
    /// directly and run twice. <see cref="WProtoSubtypeTagAssigner"/> supplies the inputs from
    /// <c>TypeCache</c> and writes the result out.
    /// </para>
    /// <para>
    /// Determinism is a correctness property here, not tidiness. The whole point of committing the
    /// numbers is that two machines, two checkouts and two runs agree, so nothing may depend on the
    /// order types were discovered in: fresh numbers are handed out in ordinal name order and the
    /// rendered file is sorted by base type and then by number.
    /// </para>
    /// </remarks>
    public sealed class WProtoSubtypeTagPlan
    {
        private const int MaxFieldNumber = 536870911;
        private const int ReservedRangeStart = 19000;
        private const int ReservedRangeEnd = 19999;

        private WProtoSubtypeTagPlan(
            IReadOnlyList<Entry> assigned,
            IReadOnlyList<Entry> retired,
            IReadOnlyList<Entry> freshlyAssigned
        )
        {
            Assigned = assigned;
            Retired = retired;
            FreshlyAssigned = freshlyAssigned;
        }

        /// <summary>The field number every tag-less subtype should be written under.</summary>
        public IReadOnlyList<Entry> Assigned { get; }

        /// <summary>The field numbers no subtype of their base may ever take again.</summary>
        public IReadOnlyList<Entry> Retired { get; }

        /// <summary>
        /// The subtypes this run had to invent a number for, because nothing held one.
        /// </summary>
        /// <remarks>
        /// Exactly the set that is <c>WPROTO041</c> right now: a tag-less declaration with neither
        /// a manifest entry nor a retired number to restore. The automatic pass runs only when this
        /// is non-empty, so an assembly that has merely drifted -- a promoted subtype, a stale
        /// comment -- is left to the menu item and its reviewable diff, and the build gate refuses a
        /// player exactly when this is non-empty for an assembly the player contains.
        /// </remarks>
        public IReadOnlyList<Entry> FreshlyAssigned { get; }

        /// <summary>Whether the plan would write no entry of either kind.</summary>
        public bool IsEmpty => Assigned.Count == 0 && Retired.Count == 0;

        /// <summary>
        /// Computes the manifest an assembly should carry.
        /// </summary>
        /// <param name="declarations">Every <c>[WProtoSubtype]</c> the assembly declares.</param>
        /// <param name="reserved">Field numbers the bases already spend on members and includes.</param>
        /// <param name="existing">The manifest entries the assembly currently declares.</param>
        /// <param name="retired">The retired entries the assembly currently declares.</param>
        /// <returns>The plan; never <c>null</c>, and empty when there is nothing to assign.</returns>
        /// <remarks>
        /// Null and duplicate inputs are tolerated rather than refused, because this runs from an
        /// editor menu over whatever the project happens to contain and a thrown exception there
        /// tells the developer nothing they can act on.
        /// </remarks>
        public static WProtoSubtypeTagPlan Create(
            IReadOnlyList<Declaration> declarations,
            IReadOnlyList<Entry> reserved,
            IReadOnlyList<Entry> existing,
            IReadOnlyList<Entry> retired
        )
        {
            List<Declaration> tagless = new List<Declaration>();
            Dictionary<string, HashSet<int>> taken = new Dictionary<string, HashSet<int>>(
                StringComparer.Ordinal
            );
            HashSet<string> taglessPairs = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, int> explicitTags = new Dictionary<string, int>(
                StringComparer.Ordinal
            );

            foreach (Declaration declaration in Safe(declarations))
            {
                if (!declaration.IsUsable)
                {
                    continue;
                }

                if (declaration.HasTag)
                {
                    Claim(taken, declaration.BaseTypeName, declaration.Tag);
                    explicitTags[PairKey(declaration.SubTypeName, declaration.BaseTypeName)] =
                        declaration.Tag;
                    continue;
                }

                if (taglessPairs.Add(PairKey(declaration.SubTypeName, declaration.BaseTypeName)))
                {
                    tagless.Add(declaration);
                }
            }

            foreach (Entry entry in Safe(reserved))
            {
                if (entry.IsUsable)
                {
                    Claim(taken, entry.BaseTypeName, entry.Tag);
                }
            }

            Dictionary<string, Entry> retiredByPair = new Dictionary<string, Entry>(
                StringComparer.Ordinal
            );
            foreach (Entry entry in Safe(retired))
            {
                if (!entry.IsUsable)
                {
                    continue;
                }

                Claim(taken, entry.BaseTypeName, entry.Tag);
                string key = PairKey(entry.SubTypeName, entry.BaseTypeName);
                if (!retiredByPair.ContainsKey(key))
                {
                    retiredByPair[key] = entry;
                }
            }

            List<Entry> assignments = new List<Entry>();
            List<Entry> fresh = new List<Entry>();
            Dictionary<string, Entry> retirements = new Dictionary<string, Entry>(
                StringComparer.Ordinal
            );
            HashSet<string> keptPairs = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> restoredPairs = new HashSet<string>(StringComparer.Ordinal);

            foreach (Entry entry in Safe(existing))
            {
                if (!entry.IsUsable)
                {
                    continue;
                }

                string key = PairKey(entry.SubTypeName, entry.BaseTypeName);
                if (!keptPairs.Add(key))
                {
                    continue;
                }

                Claim(taken, entry.BaseTypeName, entry.Tag);

                if (taglessPairs.Contains(key))
                {
                    // Never recomputed. A number already written is the wire contract for every
                    // payload saved since it was written, and "the tool would pick a smaller one
                    // now" is not a reason a save from last year can survive.
                    assignments.Add(entry);
                    continue;
                }

                // The subtype pinned its own number and pinned the same one, so the manifest entry
                // is simply redundant. Retiring it would forbid the very declaration that now holds
                // it, and the next build would refuse a hierarchy that changed in no way at all.
                if (
                    explicitTags.TryGetValue(key, out int pinned)
                    && pinned == entry.Tag
                    && !retiredByPair.ContainsKey(key)
                )
                {
                    continue;
                }

                retirements[RetirementKey(entry)] = entry;
            }

            foreach (Entry entry in retiredByPair.Values)
            {
                string key = PairKey(entry.SubTypeName, entry.BaseTypeName);
                if (keptPairs.Contains(key) || !taglessPairs.Contains(key))
                {
                    continue;
                }

                // Remove-then-re-add, which is the case the whole design exists for: the number the
                // type had is still held for it, so it comes back rather than being handed out.
                assignments.Add(entry);
                restoredPairs.Add(key);
                keptPairs.Add(key);
            }

            tagless.Sort(CompareDeclarations);
            foreach (Declaration declaration in tagless)
            {
                string key = PairKey(declaration.SubTypeName, declaration.BaseTypeName);
                if (keptPairs.Contains(key))
                {
                    continue;
                }

                if (!TryNextFree(taken, declaration.BaseTypeName, out int next))
                {
                    continue;
                }

                keptPairs.Add(key);
                Entry assignment = new Entry(
                    declaration.SubTypeName,
                    declaration.BaseTypeName,
                    next
                );
                assignments.Add(assignment);
                fresh.Add(assignment);
            }

            foreach (Entry entry in retiredByPair.Values)
            {
                if (!restoredPairs.Contains(PairKey(entry.SubTypeName, entry.BaseTypeName)))
                {
                    retirements[RetirementKey(entry)] = entry;
                }
            }

            List<Entry> retiredOut = new List<Entry>(retirements.Values);
            assignments.Sort(CompareEntries);
            retiredOut.Sort(CompareEntries);
            fresh.Sort(CompareEntries);

            return new WProtoSubtypeTagPlan(assignments, retiredOut, fresh);
        }

        /// <summary>
        /// Renders the plan as the committed C# manifest file.
        /// </summary>
        /// <param name="assemblyName">The assembly the manifest belongs to, named in the header.</param>
        /// <returns>The complete file text, with CRLF line endings.</returns>
        /// <remarks>
        /// Assembly attributes cannot sit inside a namespace, so every name is written fully
        /// qualified rather than imported -- which also keeps the file immune to a <c>using</c> a
        /// later edit removes.
        /// </remarks>
        public string Render(string assemblyName)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("// MIT License - Copyright (c) 2026 wallstop\r\n");
            builder.Append(
                "// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE\r\n"
            );
            builder.Append("\r\n");
            builder.Append("// WallstopProto subtype tag manifest for ");
            builder.Append(string.IsNullOrEmpty(assemblyName) ? "this assembly" : assemblyName);
            builder.Append(".\r\n");
            builder.Append(
                "// Written by Tools > Wallstop Studios > Unity Helpers > Assign WallstopProto\r\n"
            );
            builder.Append(
                "// Subtype Tags. Commit it: these numbers are the wire contract for every\r\n"
            );
            builder.Append(
                "// [WProtoSubtype] declared without one, so a payload saved today is read back by\r\n"
            );
            builder.Append(
                "// this file. Do not renumber an entry, and do not delete a retired one -- a\r\n"
            );
            builder.Append(
                "// retired number is held so a later subtype cannot be given a number old saves\r\n"
            );
            builder.Append("// already mean something else by.\r\n");
            builder.Append(
                "//\r\n// The editor rewrites this file automatically after an assembly reload that finds a\r\n"
            );
            builder.Append(
                "// [WProtoSubtype] with no number and no entry here, so adding a subtype is one\r\n"
            );
            builder.Append("// attribute and a recompile.\r\n");

            if (0 < Assigned.Count)
            {
                builder.Append("\r\n");
            }

            foreach (Entry entry in Assigned)
            {
                // The subtype is a STRING and the base is a typeof, which is not an inconsistency:
                // the base still exists whenever the pair does, while the subtype is exactly the
                // half that can be deleted -- and a typeof for a deleted type stops the manifest
                // compiling, whose only cheap repair is to delete the line and free the number.
                builder.Append(
                    "[assembly: WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoSubtypeTag(\r\n"
                );
                builder.Append("    \"");
                builder.Append(entry.SubTypeName);
                builder.Append("\",\r\n    typeof(");
                builder.Append(entry.BaseTypeName);
                builder.Append("),\r\n    ");
                builder.Append(entry.Tag.ToString(CultureInfo.InvariantCulture));
                builder.Append("\r\n)]\r\n");
            }

            if (0 < Retired.Count)
            {
                builder.Append("\r\n");
            }

            foreach (Entry entry in Retired)
            {
                builder.Append(
                    "[assembly: WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto.WProtoRetiredSubtypeTag(\r\n"
                );
                builder.Append("    \"");
                builder.Append(entry.SubTypeName);
                builder.Append("\",\r\n    typeof(");
                builder.Append(entry.BaseTypeName);
                builder.Append("),\r\n    ");
                builder.Append(entry.Tag.ToString(CultureInfo.InvariantCulture));
                builder.Append("\r\n)]\r\n");
            }

            return builder.ToString();
        }

        private static IEnumerable<T> Safe<T>(IReadOnlyList<T> values)
        {
            return values ?? (IReadOnlyList<T>)Array.Empty<T>();
        }

        private static void Claim(Dictionary<string, HashSet<int>> taken, string baseName, int tag)
        {
            if (!taken.TryGetValue(baseName, out HashSet<int> claimed))
            {
                claimed = new HashSet<int>();
                taken[baseName] = claimed;
            }

            claimed.Add(tag);
        }

        private static bool TryNextFree(
            Dictionary<string, HashSet<int>> taken,
            string baseName,
            out int next
        )
        {
            if (!taken.TryGetValue(baseName, out HashSet<int> claimed))
            {
                claimed = new HashSet<int>();
                taken[baseName] = claimed;
            }

            // The SMALLEST free number, so tags stay inside the two-byte varint window that
            // ordinary hierarchies live in. A number derived from a hash would land above 2^25 and
            // cost three extra bytes on every polymorphic message, forever.
            for (int candidate = 1; candidate <= MaxFieldNumber; candidate++)
            {
                if (ReservedRangeStart <= candidate && candidate <= ReservedRangeEnd)
                {
                    candidate = ReservedRangeEnd;
                    continue;
                }

                if (claimed.Add(candidate))
                {
                    next = candidate;
                    return true;
                }
            }

            next = 0;
            return false;
        }

        private static string PairKey(string subTypeName, string baseTypeName)
        {
            return subTypeName + "|" + baseTypeName;
        }

        private static string RetirementKey(Entry entry)
        {
            return entry.SubTypeName + "|" + entry.BaseTypeName + "|" + entry.Tag;
        }

        private static int CompareEntries(Entry left, Entry right)
        {
            int byBase = string.CompareOrdinal(left.BaseTypeName, right.BaseTypeName);
            if (byBase != 0)
            {
                return byBase;
            }

            int byTag = left.Tag.CompareTo(right.Tag);
            return byTag != 0 ? byTag : string.CompareOrdinal(left.SubTypeName, right.SubTypeName);
        }

        private static int CompareDeclarations(Declaration left, Declaration right)
        {
            int byBase = string.CompareOrdinal(left.BaseTypeName, right.BaseTypeName);
            return byBase != 0
                ? byBase
                : string.CompareOrdinal(left.SubTypeName, right.SubTypeName);
        }

        /// <summary>
        /// One <c>[WProtoSubtype]</c> as written, whether or not it stated a field number.
        /// </summary>
        public readonly struct Declaration
        {
            /// <summary>
            /// Initializes the declaration.
            /// </summary>
            /// <param name="subTypeName">The fully qualified subtype name.</param>
            /// <param name="baseTypeName">The fully qualified base type name.</param>
            /// <param name="hasTag">Whether the declaration stated its own field number.</param>
            /// <param name="tag">The stated field number, ignored when <paramref name="hasTag"/> is <c>false</c>.</param>
            public Declaration(string subTypeName, string baseTypeName, bool hasTag, int tag)
            {
                SubTypeName = subTypeName;
                BaseTypeName = baseTypeName;
                HasTag = hasTag;
                Tag = tag;
            }

            /// <summary>The fully qualified subtype name.</summary>
            public string SubTypeName { get; }

            /// <summary>The fully qualified base type name.</summary>
            public string BaseTypeName { get; }

            /// <summary>Whether the declaration stated its own field number.</summary>
            public bool HasTag { get; }

            /// <summary>The stated field number, or <c>0</c>.</summary>
            public int Tag { get; }

            /// <summary>Whether both names are present and any stated number is in range.</summary>
            public bool IsUsable =>
                !string.IsNullOrEmpty(SubTypeName)
                && !string.IsNullOrEmpty(BaseTypeName)
                && (!HasTag || (1 <= Tag && Tag <= MaxFieldNumber));
        }

        /// <summary>
        /// One field number belonging to one subtype on one base.
        /// </summary>
        public readonly struct Entry
        {
            /// <summary>
            /// Initializes the entry.
            /// </summary>
            /// <param name="subTypeName">The fully qualified subtype name.</param>
            /// <param name="baseTypeName">The fully qualified base type name.</param>
            /// <param name="tag">The field number.</param>
            public Entry(string subTypeName, string baseTypeName, int tag)
            {
                SubTypeName = subTypeName;
                BaseTypeName = baseTypeName;
                Tag = tag;
            }

            /// <summary>The fully qualified subtype name.</summary>
            public string SubTypeName { get; }

            /// <summary>The fully qualified base type name.</summary>
            public string BaseTypeName { get; }

            /// <summary>The field number.</summary>
            public int Tag { get; }

            /// <summary>Whether the entry names both types and holds an in-range number.</summary>
            public bool IsUsable =>
                !string.IsNullOrEmpty(SubTypeName)
                && !string.IsNullOrEmpty(BaseTypeName)
                && 1 <= Tag
                && Tag <= MaxFieldNumber;
        }
    }
}
