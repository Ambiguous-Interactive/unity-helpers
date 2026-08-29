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
    /// This is the step a Roslyn generator deliberately does not perform. A generator runs in
    /// memory, on every keystroke, over whichever compilation the IDE happens to be holding, so a
    /// number it chose would depend on what it could see at that moment -- and a field number that
    /// moves is saved data that reads back as the wrong type. Assignment is therefore an explicit
    /// act with a reviewable diff, exactly as this package already treats its committed analyzer
    /// DLLs and its out-parameter baseline.
    /// </para>
    /// <para>
    /// Discovery is <see cref="TypeCache"/>, Unity's own index, rather than a scan of every loaded
    /// assembly: it answers the question directly and is what the editor already maintains.
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
        public const string ManifestFileName = "WProtoSubtypeTags.cs";

        private const string MenuPath =
            "Tools/Wallstop Studios/Unity Helpers/Assign WallstopProto Subtype Tags";

        private const string FallbackDirectory = "Assets";

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
        public static Report Run(bool write)
        {
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
                    ReadAssigned(assembly),
                    ReadRetired(assembly)
                );

                string rendered = plan.Render(name);
                if (!TryResolvePath(name, out string path))
                {
                    report.Failures.Add(
                        "Could not decide where to write the manifest for '"
                            + name
                            + "'. Move its types into an assembly that has an .asmdef, or add the "
                            + "entries to that assembly by hand."
                    );
                    continue;
                }

                bool empty = plan.Assigned.Count == 0 && plan.Retired.Count == 0;
                if ((empty && !File.Exists(path)) || Matches(path, rendered))
                {
                    // An assembly whose subtypes all write their own numbers needs no manifest, and
                    // creating an empty one for it would put a file in every project that ever used
                    // the explicit form.
                    report.Unchanged.Add(name);
                    continue;
                }

                report.Changed.Add(name);
                if (!write)
                {
                    continue;
                }

                if (TryWrite(path, rendered, out string failure))
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
                            NameOf(subType),
                            NameOf(baseType),
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
            string baseName = NameOf(baseType);

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
                            include.KnownType == null ? "?" : NameOf(include.KnownType),
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

        private static List<WProtoSubtypeTagPlan.Entry> ReadAssigned(Assembly assembly)
        {
            List<WProtoSubtypeTagPlan.Entry> entries = new List<WProtoSubtypeTagPlan.Entry>();
            foreach (
                WProtoSubtypeTagAttribute entry in assembly.GetCustomAttributes<WProtoSubtypeTagAttribute>()
            )
            {
                if (entry.SubType == null || entry.BaseType == null)
                {
                    continue;
                }

                entries.Add(
                    new WProtoSubtypeTagPlan.Entry(
                        NameOf(entry.SubType),
                        NameOf(entry.BaseType),
                        entry.Tag
                    )
                );
            }

            return entries;
        }

        private static List<WProtoSubtypeTagPlan.Entry> ReadRetired(Assembly assembly)
        {
            List<WProtoSubtypeTagPlan.Entry> entries = new List<WProtoSubtypeTagPlan.Entry>();
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
        /// The name the manifest writes, which has to be the one <c>typeof</c> accepts.
        /// </summary>
        /// <remarks>
        /// A nested type's reflection name spells the separator as <c>+</c>, which is not C#, so the
        /// rendered <c>typeof</c> would not compile. Generic and array shapes cannot reach here --
        /// the generator refuses a generic subtype -- so the dot is the whole conversion.
        /// </remarks>
        private static string NameOf(Type type)
        {
            string full = type.FullName;
            return string.IsNullOrEmpty(full) ? type.Name : full.Replace('+', '.');
        }

        private static bool TryResolvePath(string assemblyName, out string path)
        {
            path = null;
            // Fully qualified rather than imported: UnityEditor.Compilation declares its own
            // `Assembly`, and a using directive for that namespace makes every
            // System.Reflection.Assembly in this file ambiguous.
            string definition =
                UnityEditor.Compilation.CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(
                    assemblyName
                );

            if (string.IsNullOrEmpty(definition))
            {
                // Assembly-CSharp and friends have no .asmdef to sit beside, and Assets is the only
                // directory every project has.
                path = FallbackDirectory + "/" + ManifestFileName;
                return Directory.Exists(FallbackDirectory);
            }

            string directory = Path.GetDirectoryName(definition);
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            path = directory.Replace('\\', '/') + "/" + ManifestFileName;
            return true;
        }

        private static bool Matches(string path, string rendered)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                return string.Equals(
                    File.ReadAllText(path).Replace("\r\n", "\n"),
                    rendered.Replace("\r\n", "\n"),
                    StringComparison.Ordinal
                );
            }
            catch (IOException)
            {
                return false;
            }
        }

        private static bool TryWrite(string path, string rendered, out string failure)
        {
            failure = null;
            try
            {
                File.WriteAllText(path, rendered, new UTF8Encoding(false));
                return true;
            }
            catch (Exception error)
            {
                failure = "Could not write '" + path + "': " + error.Message;
                return false;
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
