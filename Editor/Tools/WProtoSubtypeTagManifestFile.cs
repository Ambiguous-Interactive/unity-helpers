// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Where an assembly's subtype tag manifest lives, what it currently says, and whether what
    /// assignment produces differs from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately free of Unity, like <see cref="WProtoSubtypeTagPlan"/> beside it, and for the
    /// same reason: three of the four defects this file exists to prevent are decided here, and a
    /// rule that can only be exercised by launching an editor is a rule nothing exercises.
    /// <see cref="WProtoSubtypeTagAssigner"/> supplies the editor half -- <c>TypeCache</c>, the
    /// asmdef lookup and the file system -- and everything else is names, text and numbers.
    /// </para>
    /// <para>
    /// The reading half is what makes retirement possible at all. An entry names its subtype as a
    /// string, so an entry whose type has been deleted still parses, still holds its number, and
    /// still reaches the planner -- where it is retired. Filtering such an entry out (which is what
    /// resolving a <c>typeof</c> did) silently freed its number for the next subtype to take.
    /// </para>
    /// </remarks>
    public static class WProtoSubtypeTagManifestFile
    {
        /// <summary>The file each assembly's manifest is written to.</summary>
        public const string FileName = "WProtoSubtypeTags.cs";

        private const string AssetsRoot = "Assets";

        /// <summary>
        /// The directory a predefined (asmdef-less) assembly's manifest has to live in.
        /// </summary>
        /// <param name="assemblyName">The compiled assembly's name.</param>
        /// <returns>The project-relative directory, or <c>null</c> when there is no safe one.</returns>
        /// <remarks>
        /// Unity's four predefined assemblies are compiled from four DISJOINT sets of directories,
        /// so one path cannot serve them. Writing every one of them to <c>Assets/</c> put
        /// <c>Assembly-CSharp</c> and <c>Assembly-CSharp-Editor</c> in the same file, which compiles
        /// into the runtime assembly only: the editor assembly never saw its own entries, so a
        /// tag-less editor declaration could not be repaired, and whichever assembly ran last
        /// overwrote the other's numbers.
        /// </remarks>
        public static string DirectoryForPredefinedAssembly(string assemblyName)
        {
            if (string.Equals(assemblyName, "Assembly-CSharp", StringComparison.Ordinal))
            {
                return AssetsRoot;
            }

            if (string.Equals(assemblyName, "Assembly-CSharp-Editor", StringComparison.Ordinal))
            {
                return AssetsRoot + "/Editor";
            }

            if (string.Equals(assemblyName, "Assembly-CSharp-firstpass", StringComparison.Ordinal))
            {
                return AssetsRoot + "/Plugins";
            }

            if (
                string.Equals(
                    assemblyName,
                    "Assembly-CSharp-Editor-firstpass",
                    StringComparison.Ordinal
                )
            )
            {
                return AssetsRoot + "/Plugins/Editor";
            }

            return null;
        }

        /// <summary>
        /// The shallowest directory whose <c>.asmdef</c> could take
        /// <paramref name="assemblyName"/>'s manifest directory away from it.
        /// </summary>
        /// <param name="assemblyName">The predefined assembly being placed.</param>
        /// <returns>The floor for the ancestor walk, or <c>null</c> when there is none.</returns>
        /// <remarks>
        /// <para>
        /// An <c>.asmdef</c> claims its own folder and everything under it, so the search for one
        /// has to walk upwards -- but not without limit. Unity compiles <c>Assets/Plugins</c> (and
        /// <c>Standard Assets</c>, <c>Pro Standard Assets</c>) into the firstpass assemblies in an
        /// earlier phase, so an <c>.asmdef</c> sitting at <c>Assets</c> does NOT take them. Walking
        /// past the firstpass root reported that outer <c>.asmdef</c> as the claimant and refused a
        /// firstpass manifest whose scripts still compile into the firstpass assembly.
        /// </para>
        /// <para>
        /// The two non-firstpass assemblies have no such root, so their floor is
        /// <c>Assets</c> itself.
        /// </para>
        /// </remarks>
        public static string ClaimFloorForPredefinedAssembly(string assemblyName)
        {
            if (
                string.Equals(assemblyName, "Assembly-CSharp-firstpass", StringComparison.Ordinal)
                || string.Equals(
                    assemblyName,
                    "Assembly-CSharp-Editor-firstpass",
                    StringComparison.Ordinal
                )
            )
            {
                return AssetsRoot + "/Plugins";
            }

            if (
                string.Equals(assemblyName, "Assembly-CSharp", StringComparison.Ordinal)
                || string.Equals(assemblyName, "Assembly-CSharp-Editor", StringComparison.Ordinal)
            )
            {
                return AssetsRoot;
            }

            return null;
        }

        /// <summary>
        /// Every predefined assembly this tool knows how to place a manifest for.
        /// </summary>
        /// <returns>The assembly names, in a fixed order.</returns>
        public static IReadOnlyList<string> PredefinedAssemblyNames()
        {
            return new[]
            {
                "Assembly-CSharp",
                "Assembly-CSharp-Editor",
                "Assembly-CSharp-firstpass",
                "Assembly-CSharp-Editor-firstpass",
            };
        }

        /// <summary>
        /// Which predefined assembly a project-relative asset directory compiles into.
        /// </summary>
        /// <param name="directory">The project-relative directory, using either slash.</param>
        /// <returns>The assembly name, or <c>null</c> when the directory is not under Assets.</returns>
        /// <remarks>
        /// Unity's own rule, restated so it can be checked rather than assumed: anything under
        /// <c>Plugins</c>, <c>Standard Assets</c> or <c>Pro Standard Assets</c> at the top of
        /// <c>Assets</c> is a first-pass assembly, and anything with an <c>Editor</c> directory
        /// anywhere in its path is an editor assembly. A path that does not round-trip through
        /// <see cref="DirectoryForPredefinedAssembly"/> is a manifest compiled into an assembly it
        /// does not describe, which is the defect this pair exists to make testable.
        /// </remarks>
        public static string PredefinedAssemblyForDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            string[] segments = directory.Replace('\\', '/').Trim('/').Split('/');
            if (
                segments.Length == 0
                || !string.Equals(segments[0], AssetsRoot, StringComparison.Ordinal)
            )
            {
                return null;
            }

            bool firstPass =
                1 < segments.Length
                && (
                    string.Equals(segments[1], "Plugins", StringComparison.Ordinal)
                    || string.Equals(segments[1], "Standard Assets", StringComparison.Ordinal)
                    || string.Equals(segments[1], "Pro Standard Assets", StringComparison.Ordinal)
                );

            bool editor = false;
            for (int index = 1; index < segments.Length; index++)
            {
                if (string.Equals(segments[index], "Editor", StringComparison.Ordinal))
                {
                    editor = true;
                    break;
                }
            }

            if (editor)
            {
                return firstPass ? "Assembly-CSharp-Editor-firstpass" : "Assembly-CSharp-Editor";
            }

            return firstPass ? "Assembly-CSharp-firstpass" : "Assembly-CSharp";
        }

        /// <summary>
        /// The <c>.asmdef</c> that would take a file written into a directory, if one would.
        /// </summary>
        /// <param name="directory">The project-relative directory, using either slash.</param>
        /// <param name="assemblyDefinitionPaths">
        /// Every <c>.asmdef</c> path worth considering; only the ones at or above
        /// <paramref name="directory"/> can claim it, so the rest are ignored.
        /// </param>
        /// <returns>The claiming <c>.asmdef</c> path, or <c>null</c> when none claims it.</returns>
        /// <remarks>
        /// <para>
        /// Unity assigns a script to the NEAREST ancestor <c>.asmdef</c> and only falls back to a
        /// predefined assembly when there is none, so
        /// <see cref="DirectoryForPredefinedAssembly"/> naming a path is not the same as that path
        /// compiling into the assembly it names. A project with an <c>.asmdef</c> in
        /// <c>Assets/Editor</c> -- or anywhere above it -- gets a manifest compiled into THAT
        /// assembly: the generator never sees the entries, <c>WPROTO041</c> keeps firing, and every
        /// later pass reads the on-disk file as already current, so assignment never recovers.
        /// Refusing is the only outcome that stays recoverable.
        /// </para>
        /// <para>
        /// The nearest claimant wins, matching Unity, and ties inside one directory are broken in
        /// ordinal order so two runs name the same file.
        /// </para>
        /// </remarks>
        public static string AssemblyDefinitionClaiming(
            string directory,
            IReadOnlyList<string> assemblyDefinitionPaths
        )
        {
            if (string.IsNullOrEmpty(directory) || assemblyDefinitionPaths == null)
            {
                return null;
            }

            string target = Normalize(directory);
            if (string.IsNullOrEmpty(target))
            {
                return null;
            }

            string claimant = null;
            int deepest = -1;
            foreach (string path in assemblyDefinitionPaths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                string normalized = Normalize(path);
                int separator = normalized.LastIndexOf('/');
                string owner = separator < 0 ? string.Empty : normalized.Substring(0, separator);
                if (!Claims(owner, target))
                {
                    continue;
                }

                int depth = Depth(owner);
                if (depth < deepest)
                {
                    continue;
                }

                if (depth == deepest && 0 <= string.CompareOrdinal(normalized, claimant))
                {
                    continue;
                }

                deepest = depth;
                claimant = normalized;
            }

            return claimant;
        }

        /// <summary>
        /// The message shown when an <c>.asmdef</c> owns the only directory a predefined assembly's
        /// manifest could go in.
        /// </summary>
        /// <param name="assemblyName">The predefined assembly that could not be served.</param>
        /// <param name="directory">The directory its manifest would have gone in.</param>
        /// <param name="assemblyDefinitionPath">The <c>.asmdef</c> that takes that directory.</param>
        /// <returns>The failure text, naming the offending <c>.asmdef</c> and what to do.</returns>
        public static string DescribeClaimedPredefinedDirectory(
            string assemblyName,
            string directory,
            string assemblyDefinitionPath
        )
        {
            return "The manifest for '"
                + assemblyName
                + "' can only live in '"
                + directory
                + "', but '"
                + assemblyDefinitionPath
                + "' compiles that directory into its own assembly, so the file would never be read "
                + "by '"
                + assemblyName
                + "' -- WPROTO041 would keep firing and every later pass would read the file as "
                + "already current. Nothing was written. Move '"
                + assemblyDefinitionPath
                + "' so no .asmdef sits at or above '"
                + directory
                + "', give the unnumbered types an .asmdef of their own, or add the "
                + "[assembly: WProtoSubtypeTag] entries to '"
                + assemblyName
                + "' by hand.";
        }

        /// <summary>
        /// The message shown when an assembly's manifest has nowhere safe to go.
        /// </summary>
        /// <param name="assemblyName">The assembly that could not be placed.</param>
        /// <returns>The failure text, naming what to do about it.</returns>
        public static string DescribeUnplaceableAssembly(string assemblyName)
        {
            return "Could not decide where to write the manifest for '"
                + assemblyName
                + "'. It has no .asmdef to sit beside and is not one of Unity's predefined "
                + "assemblies ("
                + string.Join(", ", (string[])PredefinedAssemblyNames())
                + "), so any path chosen for it would compile into a different assembly and its "
                + "entries would never be read. Give those types an .asmdef, or add the "
                + "[assembly: WProtoSubtypeTag] entries to that assembly by hand.";
        }

        /// <summary>
        /// The message shown when a predefined assembly's directory cannot be created.
        /// </summary>
        /// <param name="assemblyName">The assembly that could not be placed.</param>
        /// <param name="directory">The directory its manifest belongs in.</param>
        /// <param name="parent">The parent that does not exist.</param>
        /// <returns>The failure text.</returns>
        public static string DescribeMissingDirectory(
            string assemblyName,
            string directory,
            string parent
        )
        {
            return "The manifest for '"
                + assemblyName
                + "' belongs in '"
                + directory
                + "', which does not exist and cannot be created because '"
                + parent
                + "' does not exist either. Writing it anywhere else would compile it into a "
                + "different assembly, where its entries would never be read. Create '"
                + parent
                + "' first, or give those types an .asmdef.";
        }

        /// <summary>
        /// Reads the field numbers an assembly's manifest currently assigns.
        /// </summary>
        /// <param name="assembly">The compiled assembly carrying the manifest.</param>
        /// <returns>One entry per usable <c>[assembly: WProtoSubtypeTag]</c>; never <c>null</c>.</returns>
        /// <remarks>
        /// An entry naming a type that no longer exists is KEPT, which is the whole point of the
        /// string key. It reaches the planner as an existing entry with no matching declaration and
        /// is retired there. Dropping it instead freed its number, and the next subtype added took
        /// the number every payload written before the deletion still means the old type by.
        /// </remarks>
        public static List<WProtoSubtypeTagPlan.Entry> ReadAssigned(Assembly assembly)
        {
            List<WProtoSubtypeTagPlan.Entry> entries = new List<WProtoSubtypeTagPlan.Entry>();
            if (assembly == null)
            {
                return entries;
            }

            foreach (
                WProtoSubtypeTagAttribute entry in assembly.GetCustomAttributes<WProtoSubtypeTagAttribute>()
            )
            {
                if (string.IsNullOrEmpty(entry.SubTypeName) || entry.BaseType == null)
                {
                    continue;
                }

                entries.Add(
                    new WProtoSubtypeTagPlan.Entry(
                        entry.SubTypeName,
                        NameOf(entry.BaseType),
                        entry.Tag
                    )
                );
            }

            return entries;
        }

        /// <summary>
        /// Reads the field numbers an assembly's manifest holds against reuse.
        /// </summary>
        /// <param name="assembly">The compiled assembly carrying the manifest.</param>
        /// <returns>One entry per usable <c>[assembly: WProtoRetiredSubtypeTag]</c>.</returns>
        public static List<WProtoSubtypeTagPlan.Entry> ReadRetired(Assembly assembly)
        {
            List<WProtoSubtypeTagPlan.Entry> entries = new List<WProtoSubtypeTagPlan.Entry>();
            if (assembly == null)
            {
                return entries;
            }

            foreach (
                WProtoRetiredSubtypeTagAttribute entry in assembly.GetCustomAttributes<WProtoRetiredSubtypeTagAttribute>()
            )
            {
                if (string.IsNullOrEmpty(entry.SubTypeName) || entry.BaseType == null)
                {
                    continue;
                }

                entries.Add(
                    new WProtoSubtypeTagPlan.Entry(
                        entry.SubTypeName,
                        NameOf(entry.BaseType),
                        entry.Tag
                    )
                );
            }

            return entries;
        }

        /// <summary>
        /// The name the manifest writes, which has to be the one the generator resolves.
        /// </summary>
        /// <param name="type">The type to name.</param>
        /// <returns>The fully qualified name, with a dot before a nested type.</returns>
        /// <remarks>
        /// A nested type's reflection name spells the separator as <c>+</c>, which is neither what
        /// C# accepts in a <c>typeof</c> nor what Roslyn's <c>ToDisplayString</c> produces, and the
        /// generator compares against the latter. Generic and array shapes cannot reach here -- the
        /// generator refuses a generic subtype -- so the dot is the whole conversion.
        /// </remarks>
        public static string NameOf(Type type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            string full = type.FullName;
            return string.IsNullOrEmpty(full) ? type.Name : full.Replace('+', '.');
        }

        /// <summary>
        /// Whether the manifest on disk differs from what assignment produces.
        /// </summary>
        /// <param name="existingText">The file's current text, or <c>null</c> when it is absent.</param>
        /// <param name="rendered">What assignment would write.</param>
        /// <param name="empty">Whether the plan assigns and retires nothing at all.</param>
        /// <returns><c>true</c> when the file has to be rewritten.</returns>
        /// <remarks>
        /// <para>
        /// This is the whole of the idempotency guarantee, and it is a function rather than a
        /// comparison inlined into the writer so a test can run it twice. An automatic pass that
        /// answered <c>true</c> for an unchanged project would write a file, trigger a reimport, run
        /// again after the reload, and never settle. An assembly whose subtypes all write their own
        /// numbers is left with no manifest at all rather than an empty one, so adopting this
        /// package does not put a file into every project that only ever used the explicit form.
        /// </para>
        /// <para>
        /// Only the ENTRIES are compared, not the comment header. The header is documentation, and
        /// a package upgrade that reworded it would otherwise mark every manifest in the project
        /// stale -- including the ones inside read-only packages, where the rewrite cannot even be
        /// performed. Comparing what the compiler reads keeps the answer about the wire.
        /// </para>
        /// </remarks>
        public static bool NeedsWrite(string existingText, string rendered, bool empty)
        {
            if (existingText == null)
            {
                return !empty;
            }

            return !string.Equals(
                Entries(existingText),
                Entries(rendered),
                StringComparison.Ordinal
            );
        }

        /// <summary>
        /// A manifest reduced to the lines the compiler reads.
        /// </summary>
        /// <param name="text">The manifest text, or <c>null</c>.</param>
        /// <returns>The non-blank, non-comment lines, trimmed and newline separated.</returns>
        private static string Entries(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            List<string> lines = new List<string>();
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                lines.Add(trimmed);
            }

            return string.Join("\n", lines.ToArray());
        }

        private static string Normalize(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/');
        }

        private static bool Claims(string ownerDirectory, string targetDirectory)
        {
            if (string.IsNullOrEmpty(ownerDirectory))
            {
                return false;
            }

            if (string.Equals(ownerDirectory, targetDirectory, StringComparison.Ordinal))
            {
                return true;
            }

            return targetDirectory.StartsWith(ownerDirectory + "/", StringComparison.Ordinal);
        }

        private static int Depth(string directory)
        {
            int depth = 0;
            foreach (char character in directory)
            {
                if (character == '/')
                {
                    depth++;
                }
            }

            return depth;
        }
    }
}
