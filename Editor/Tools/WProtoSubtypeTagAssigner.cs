// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
    using System.Text;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Assigns and commits the field numbers that <c>[WProtoSubtype(typeof(Base))]</c> declarations
    /// take, one manifest file per assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Assignment is an editor act rather than a generator one. A generator runs in memory, on every
    /// keystroke, over whichever compilation the IDE happens to be holding, so a number it chose
    /// would depend on what it could see at that moment -- and a field number that moves is saved
    /// data that reads back as the wrong type. The number is therefore decided once, written to a
    /// committed file, and never recomputed.
    /// </para>
    /// <para>
    /// It is not, however, something the developer has to remember. <see cref="WProtoSubtypeTagAutoAssign"/>
    /// runs this after every assembly reload that finds a declaration with no number, so the whole
    /// interaction is "add the attribute, let it recompile". The menu item and the two command-line
    /// entry points remain for a project that wants the act to be explicit, and for CI.
    /// </para>
    /// <para>
    /// Discovery is <see cref="TypeCache"/>, Unity's own index, rather than a scan of every loaded
    /// assembly: it answers the question directly and is what the editor already maintains. It can
    /// only see types in assemblies that COMPILED, which is why an unnumbered subtype is a warning
    /// in the editor rather than an error -- an error would fail the assembly and hide the very type
    /// that has to be numbered.
    /// </para>
    /// <para>
    /// Undo policy: Tier C. This writes one C# file per affected assembly and triggers a script
    /// reimport. Neither is reversible through Unity's undo system; the file is source under version
    /// control, and the diff is the intended review surface.
    /// </para>
    /// </remarks>
    public static class WProtoSubtypeTagAssigner
    {
        /// <summary>The file each assembly's manifest is written to.</summary>
        public const string ManifestFileName = WProtoSubtypeTagManifestFile.FileName;

        private const string MenuPath =
            "Tools/Wallstop Studios/Unity Helpers/Assign WallstopProto Subtype Tags";

        private const string AssetsRoot = "Assets";

        /// <summary>
        /// Assigns any missing field numbers and rewrites the manifests that changed.
        /// </summary>
        /// <remarks>
        /// Reports through the console rather than a dialog, so the same call is usable from
        /// <c>-executeMethod</c>.
        /// </remarks>
        [MenuItem(MenuPath)]
        public static void AssignFromMenu()
        {
            Report report = Run(true);
            Debug.Log(report.Describe("Assigned"));
        }

        /// <summary>
        /// Assigns any missing field numbers and rewrites the manifests that changed, then exits.
        /// </summary>
        /// <remarks>
        /// The headless entry point: <c>-batchmode -quit</c> is not used, because this exits itself
        /// with a status a build step can branch on.
        /// </remarks>
        public static void AssignFromCommandLine()
        {
            Report report = Run(true);
            Debug.Log(report.Describe("Assigned"));
            EditorApplication.Exit(report.Failed ? 1 : 0);
        }

        /// <summary>
        /// Reports whether every manifest is already what assignment would produce, without writing.
        /// </summary>
        /// <remarks>
        /// The drift gate. A manifest that is out of date is a build that cannot compile on the next
        /// machine to check it out, so CI wants that as a failure rather than as a silent rewrite.
        /// </remarks>
        public static void VerifyFromCommandLine()
        {
            Report report = Run(false);
            Debug.Log(report.Describe("Would rewrite"));
            EditorApplication.Exit(report.Failed || 0 < report.Changed.Count ? 1 : 0);
        }

        /// <summary>
        /// Computes every assembly's manifest and optionally writes the ones that differ.
        /// </summary>
        /// <param name="write">Whether to write the files, or only to compare.</param>
        /// <returns>What changed, what was left alone, and anything that went wrong.</returns>
        /// <remarks>
        /// The explicit run: somebody asked for it, so the survey is taken at its word and a
        /// manifest entry with no declaration is a deletion.
        /// </remarks>
        public static Report Run(bool write)
        {
            return Run(write, WProtoSubtypeTagDiscovery.Complete);
        }

        /// <summary>
        /// Computes every assembly's manifest and optionally writes the ones that differ.
        /// </summary>
        /// <param name="write">Whether to write the files, or only to compare.</param>
        /// <param name="discovery">
        /// How much of each assembly this run can promise it saw. The automatic pass passes
        /// <see cref="WProtoSubtypeTagDiscovery.Partial"/>.
        /// </param>
        /// <returns>What changed, what was left alone, and anything that went wrong.</returns>
        /// <remarks>
        /// <para>
        /// One value decides both halves of the unattended policy, which is the whole point of it
        /// being one value. A <see cref="WProtoSubtypeTagDiscovery.Partial"/> run writes only where
        /// a number is actually missing -- drift, such as a subtype whose number moved into its own
        /// attribute, is a wire-neutral rewrite that belongs in a diff somebody asked for -- AND
        /// plans conservatively, keeping an entry whose declaration it cannot see. Gating only the
        /// write left the plan itself full of retirements for
        /// <c>#if !UNITY_EDITOR</c> subtypes, which the first unrelated assignment then committed.
        /// </para>
        /// </remarks>
        public static Report Run(bool write, WProtoSubtypeTagDiscovery discovery)
        {
            bool unattended = discovery == WProtoSubtypeTagDiscovery.Partial;
            Report report = new Report();
            Dictionary<Assembly, Inventory> byAssembly = Collect(report);
            List<string> ordered = new List<string>();
            Dictionary<string, Assembly> assemblies = new Dictionary<string, Assembly>(
                StringComparer.Ordinal
            );

            foreach (KeyValuePair<Assembly, Inventory> pair in byAssembly)
            {
                string name = pair.Key.GetName().Name;
                if (assemblies.ContainsKey(name))
                {
                    continue;
                }

                assemblies[name] = pair.Key;
                ordered.Add(name);
            }

            // Assemblies are processed in a fixed order so the console transcript of a run is the
            // same on two machines, which is what makes a CI diff readable.
            ordered.Sort(StringComparer.Ordinal);

            foreach (string name in ordered)
            {
                Assembly assembly = assemblies[name];
                Inventory inventory = byAssembly[assembly];
                WProtoSubtypeTagPlan plan = WProtoSubtypeTagPlan.Create(
                    inventory.Declarations,
                    inventory.Reserved,
                    WProtoSubtypeTagManifestFile.ReadAssigned(assembly),
                    WProtoSubtypeTagManifestFile.ReadRetired(assembly),
                    discovery
                );

                if (0 < plan.FreshlyAssigned.Count)
                {
                    List<string> unnumbered = new List<string>();
                    foreach (WProtoSubtypeTagPlan.Entry entry in plan.FreshlyAssigned)
                    {
                        unnumbered.Add(entry.SubTypeName + " on " + entry.BaseTypeName);
                    }

                    report.Unnumbered[name] = unnumbered;
                }

                string directory = DirectoryFor(name, !plan.IsEmpty, report);
                if (directory == null)
                {
                    continue;
                }

                string path = directory + "/" + ManifestFileName;
                string rendered = plan.Render(name);
                string existing = ReadIfPresent(path);
                if (!WProtoSubtypeTagManifestFile.NeedsWrite(existing, rendered, plan.IsEmpty))
                {
                    report.Unchanged.Add(name);
                    continue;
                }

                report.Changed.Add(name);
                if (!write || (unattended && plan.FreshlyAssigned.Count == 0))
                {
                    continue;
                }

                string failure = Write(path, rendered);
                if (failure == null)
                {
                    report.Written.Add(path);
                }
                else
                {
                    report.Failures.Add(failure);
                }
            }

            if (write && 0 < report.Written.Count)
            {
                AssetDatabase.Refresh();
            }

            return report;
        }

        private static Dictionary<Assembly, Inventory> Collect(Report report)
        {
            Dictionary<Assembly, Inventory> byAssembly = new Dictionary<Assembly, Inventory>();

            foreach (Type subType in TypeCache.GetTypesWithAttribute<WProtoSubtypeAttribute>())
            {
                if (subType == null || subType.IsGenericTypeDefinition)
                {
                    continue;
                }

                foreach (
                    WProtoSubtypeAttribute declaration in subType.GetCustomAttributes<WProtoSubtypeAttribute>(
                        false
                    )
                )
                {
                    Type baseType = declaration.BaseType;
                    if (baseType == null || baseType.Assembly != subType.Assembly)
                    {
                        // A cross-assembly declaration is refused by the generator (WPROTO040), and
                        // numbering it here would put a number in a manifest no compilation reads.
                        continue;
                    }

                    Inventory inventory = InventoryFor(byAssembly, subType.Assembly);
                    inventory.Declarations.Add(
                        new WProtoSubtypeTagPlan.Declaration(
                            WProtoSubtypeTagManifestFile.NameOf(subType),
                            WProtoSubtypeTagManifestFile.NameOf(baseType),
                            declaration.HasTag,
                            declaration.Tag
                        )
                    );

                    if (inventory.Bases.Add(baseType))
                    {
                        AddReserved(inventory, baseType, report);
                    }
                }
            }

            // An assembly whose only remaining manifest entries are orphans declares no subtype at
            // all, so the sweep above never reaches it -- and its stale numbers would be neither
            // honoured nor retired. Every assembly that carries a manifest is therefore visited,
            // whether or not anything in it still declares a subtype.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic || byAssembly.ContainsKey(assembly))
                {
                    continue;
                }

                if (0 < WProtoSubtypeTagManifestFile.ReadAssigned(assembly).Count)
                {
                    InventoryFor(byAssembly, assembly);
                }
            }

            return byAssembly;
        }

        /// <summary>
        /// Records every field number the base itself already spends.
        /// </summary>
        /// <remarks>
        /// A subtype's include shares the base's field-number space with the base's own members and
        /// with any <c>[WProtoInclude]</c> the base declares, so a number picked without consulting
        /// both is a <c>WPROTO039</c> or <c>WPROTO040</c> the developer would have to resolve by
        /// hand -- which is the thing this tool exists to remove.
        /// </remarks>
        private static void AddReserved(Inventory inventory, Type baseType, Report report)
        {
            string baseName = WProtoSubtypeTagManifestFile.NameOf(baseType);

            try
            {
                foreach (
                    WProtoIncludeAttribute include in baseType.GetCustomAttributes<WProtoIncludeAttribute>(
                        false
                    )
                )
                {
                    inventory.Reserved.Add(
                        new WProtoSubtypeTagPlan.Entry(
                            include.KnownType == null
                                ? "?"
                                : WProtoSubtypeTagManifestFile.NameOf(include.KnownType),
                            baseName,
                            include.Tag
                        )
                    );
                }

                const BindingFlags Declared =
                    BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly;

                foreach (MemberInfo member in baseType.GetMembers(Declared))
                {
                    WProtoMemberAttribute annotated =
                        member.GetCustomAttribute<WProtoMemberAttribute>(false);
                    if (annotated != null)
                    {
                        inventory.Reserved.Add(
                            new WProtoSubtypeTagPlan.Entry(member.Name, baseName, annotated.Tag)
                        );
                    }
                }
            }
            catch (Exception error)
            {
                // A type whose members cannot be loaded would otherwise abort the whole run. Its
                // numbers are simply unknown, so say so instead of assigning around a blank.
                report.Failures.Add(
                    "Could not read the field numbers '"
                        + baseName
                        + "' already uses ("
                        + error.GetType().Name
                        + "), so a number assigned against it may collide."
                );
            }
        }

        private static Inventory InventoryFor(
            Dictionary<Assembly, Inventory> byAssembly,
            Assembly assembly
        )
        {
            if (!byAssembly.TryGetValue(assembly, out Inventory inventory))
            {
                inventory = new Inventory();
                byAssembly[assembly] = inventory;
            }

            return inventory;
        }

        /// <summary>
        /// The directory an assembly's manifest belongs in, creating it when that is safe.
        /// </summary>
        /// <param name="assemblyName">The compiled assembly's name.</param>
        /// <param name="hasEntriesToPlace">
        /// Whether this assembly's plan holds any entry at all. An assembly whose subtypes all
        /// write their own numbers needs no manifest, so a directory nothing can be written to is
        /// not yet anybody's problem and saying so would be noise.
        /// </param>
        /// <param name="report">Receives an actionable failure when there is no safe directory.</param>
        /// <returns>The directory, or <c>null</c> when the manifest cannot be placed.</returns>
        /// <remarks>
        /// A manifest is only read by the assembly it is compiled into, so a path chosen "close
        /// enough" is a file that compiles somewhere else and does nothing. Failing loudly is
        /// therefore the correct outcome for an assembly whose home cannot be established, and
        /// silently falling back to <c>Assets/</c> is not.
        /// </remarks>
        private static string DirectoryFor(
            string assemblyName,
            bool hasEntriesToPlace,
            Report report
        )
        {
            // Fully qualified rather than imported: UnityEditor.Compilation declares its own
            // `Assembly`, and a using directive for that namespace makes every
            // System.Reflection.Assembly in this file ambiguous.
            string definition =
                UnityEditor.Compilation.CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(
                    assemblyName
                );

            if (!string.IsNullOrEmpty(definition))
            {
                string beside = Path.GetDirectoryName(definition);
                if (string.IsNullOrEmpty(beside))
                {
                    report.Failures.Add(
                        WProtoSubtypeTagManifestFile.DescribeUnplaceableAssembly(assemblyName)
                    );
                    return null;
                }

                return beside.Replace('\\', '/');
            }

            string predefined = WProtoSubtypeTagManifestFile.DirectoryForPredefinedAssembly(
                assemblyName
            );
            if (predefined == null)
            {
                report.Failures.Add(
                    WProtoSubtypeTagManifestFile.DescribeUnplaceableAssembly(assemblyName)
                );
                return null;
            }

            string claimant = WProtoSubtypeTagManifestFile.AssemblyDefinitionClaiming(
                predefined,
                AssemblyDefinitionsAtOrAbove(
                    predefined,
                    WProtoSubtypeTagManifestFile.ClaimFloorForPredefinedAssembly(assemblyName)
                )
            );
            if (claimant != null)
            {
                if (hasEntriesToPlace)
                {
                    report.Failures.Add(
                        WProtoSubtypeTagManifestFile.DescribeClaimedPredefinedDirectory(
                            assemblyName,
                            predefined,
                            claimant
                        )
                    );
                }

                return null;
            }

            if (Directory.Exists(predefined))
            {
                return predefined;
            }

            string parent = Path.GetDirectoryName(predefined);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                report.Failures.Add(
                    WProtoSubtypeTagManifestFile.DescribeMissingDirectory(
                        assemblyName,
                        predefined,
                        string.IsNullOrEmpty(parent) ? predefined : parent
                    )
                );
                return null;
            }

            try
            {
                Directory.CreateDirectory(predefined);
                return predefined;
            }
            catch (Exception error)
            {
                report.Failures.Add(
                    "Could not create '" + predefined + "': " + error.Message + "."
                );
                return null;
            }
        }

        /// <summary>
        /// Every <c>.asmdef</c> sitting at or above a directory, down to <c>Assets</c>.
        /// </summary>
        /// <param name="directory">The project-relative directory a manifest would go in.</param>
        /// <returns>The paths, in a fixed order; empty when nothing claims the directory.</returns>
        /// <remarks>
        /// Only the ancestors are read, because only an ancestor can take the file: Unity binds a
        /// script to its nearest ancestor <c>.asmdef</c>. Walking them is also what keeps this off
        /// the asset database, which the automatic pass runs too early to consult.
        /// </remarks>
        private static List<string> AssemblyDefinitionsAtOrAbove(string directory, string floor)
        {
            List<string> paths = new List<string>();
            string stopAt = string.IsNullOrEmpty(floor) ? AssetsRoot : floor;
            string current = directory;
            while (!string.IsNullOrEmpty(current))
            {
                try
                {
                    if (Directory.Exists(current))
                    {
                        foreach (
                            string path in Directory.GetFiles(
                                current,
                                "*.asmdef",
                                SearchOption.TopDirectoryOnly
                            )
                        )
                        {
                            paths.Add(path.Replace('\\', '/'));
                        }
                    }
                }
                catch (Exception)
                {
                    // An unreadable directory cannot be shown to be free of an .asmdef, but it also
                    // cannot be shown to hold one; the write below reports its own failure.
                }

                // Stops at the floor, not always at Assets: Unity compiles the firstpass roots in
                // an earlier phase, so an .asmdef above one of them does not take it.
                if (string.Equals(current, stopAt, StringComparison.Ordinal))
                {
                    break;
                }

                string parent = Path.GetDirectoryName(current);
                current = string.IsNullOrEmpty(parent) ? null : parent.Replace('\\', '/');
            }

            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        private static string ReadIfPresent(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return File.ReadAllText(path);
            }
            catch (IOException)
            {
                // Unreadable and rewritable are the same outcome here: the file cannot be shown to
                // match, so it is rewritten and the write reports its own failure if it fails too.
                return string.Empty;
            }
        }

        /// <summary>
        /// Writes one manifest.
        /// </summary>
        /// <param name="path">Where it goes.</param>
        /// <param name="rendered">What it says.</param>
        /// <returns><c>null</c> on success, or the failure to report.</returns>
        private static string Write(string path, string rendered)
        {
            try
            {
                File.WriteAllText(path, rendered, new UTF8Encoding(false));
                return null;
            }
            catch (Exception error)
            {
                return "Could not write '" + path + "': " + error.Message;
            }
        }

        /// <summary>
        /// What one assembly contributes to its own manifest.
        /// </summary>
        private sealed class Inventory
        {
            /// <summary>Every subtype declaration the assembly makes.</summary>
            internal List<WProtoSubtypeTagPlan.Declaration> Declarations { get; } =
                new List<WProtoSubtypeTagPlan.Declaration>();

            /// <summary>Field numbers the bases already spend.</summary>
            internal List<WProtoSubtypeTagPlan.Entry> Reserved { get; } =
                new List<WProtoSubtypeTagPlan.Entry>();

            /// <summary>The bases already surveyed, so each is read once.</summary>
            internal HashSet<Type> Bases { get; } = new HashSet<Type>();
        }

        /// <summary>
        /// The outcome of one assignment run.
        /// </summary>
        public sealed class Report
        {
            /// <summary>Assemblies whose manifest already matched.</summary>
            public List<string> Unchanged { get; } = new List<string>();

            /// <summary>Assemblies whose manifest differs from what assignment produces.</summary>
            public List<string> Changed { get; } = new List<string>();

            /// <summary>Manifest paths actually rewritten.</summary>
            public List<string> Written { get; } = new List<string>();

            /// <summary>Anything that stopped a manifest being produced.</summary>
            public List<string> Failures { get; } = new List<string>();

            /// <summary>
            /// Per assembly, the declarations that had no number before this run.
            /// </summary>
            /// <remarks>
            /// Exactly what <c>WPROTO041</c> reports, in the same order, so a build gate can name
            /// the types rather than telling the developer to go and look.
            /// </remarks>
            public Dictionary<string, List<string>> Unnumbered { get; } =
                new Dictionary<string, List<string>>(StringComparer.Ordinal);

            /// <summary>Whether anything went wrong.</summary>
            public bool Failed => 0 < Failures.Count;

            /// <summary>
            /// Renders the run as one console line plus its details.
            /// </summary>
            /// <param name="verb">How to describe the changed assemblies.</param>
            /// <returns>The message.</returns>
            public string Describe(string verb)
            {
                StringBuilder builder = new StringBuilder();
                builder.Append("WallstopProto subtype tags: ");
                builder.Append(verb);
                builder.Append(' ');
                builder.Append(Changed.Count);
                builder.Append(" manifest(s), ");
                builder.Append(Unchanged.Count);
                builder.Append(" already current.");

                foreach (string path in Written)
                {
                    builder.Append("\n  wrote ");
                    builder.Append(path);
                }

                if (Written.Count == 0)
                {
                    foreach (string name in Changed)
                    {
                        builder.Append("\n  stale: ");
                        builder.Append(name);
                    }
                }

                foreach (string failure in Failures)
                {
                    builder.Append("\n  FAILED: ");
                    builder.Append(failure);
                }

                return builder.ToString();
            }
        }
    }
}
