// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
    using System.Collections.Generic;
    using System.Text;
    using UnityEditor.Build;
    using UnityEditor.Build.Reporting;

    /// <summary>
    /// Refuses a player build that contains a <c>[WProtoSubtype]</c> with no field number.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Belt and braces with <c>WPROTO041</c>, which is an error for any compilation without
    /// <c>UNITY_EDITOR</c> and therefore already stops the player's own compile. This exists because
    /// that severity is suppressible -- a <c>NoWarn</c>, a ruleset, a
    /// <c>[SuppressMessage]</c> -- and because a diagnostic can only speak for the compilation it
    /// runs in. An unnumbered subtype has no wire representation at all, so the alternative to
    /// failing here is a build that throws the first time a player saves.
    /// </para>
    /// <para>
    /// Only assemblies the player actually contains are considered. An editor-only assembly's
    /// declarations are compiled with <c>UNITY_EDITOR</c> defined, get their numbers from the
    /// automatic pass, and never reach a player -- refusing a build for one of those would be a
    /// gate that fails on code the build does not include.
    /// </para>
    /// </remarks>
    public sealed class WProtoSubtypeTagBuildGate : IPreprocessBuildWithReport
    {
        /// <inheritdoc />
        public int callbackOrder => 0;

        /// <inheritdoc />
        /// <exception cref="BuildFailedException">
        /// When a subtype the player contains has no committed field number.
        /// </exception>
        public void OnPreprocessBuild(BuildReport report)
        {
            WProtoSubtypeTagAssigner.Report assignment = WProtoSubtypeTagAssigner.Run(false);
            if (assignment.Unnumbered.Count == 0)
            {
                return;
            }

            HashSet<string> shipped = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (
                UnityEditor.Compilation.Assembly assembly in UnityEditor.Compilation.CompilationPipeline.GetAssemblies(
                    UnityEditor.Compilation.AssembliesType.PlayerWithoutTestAssemblies
                )
            )
            {
                shipped.Add(assembly.name);
            }

            StringBuilder builder = new StringBuilder();
            int count = 0;
            foreach (
                KeyValuePair<
                    string,
                    List<string>
                > pair in WProtoSubtypeTagManifestFile.ShippedUnnumbered(
                    assignment.Unnumbered,
                    shipped
                )
            )
            {
                foreach (string declaration in pair.Value)
                {
                    count++;
                    builder.Append("\n  ");
                    builder.Append(pair.Key);
                    builder.Append(": ");
                    builder.Append(declaration);
                }
            }

            if (count == 0)
            {
                return;
            }

            throw new BuildFailedException(
                "WallstopProto: "
                    + count
                    + " subtype declaration(s) in this build have no field number, so there is "
                    + "nothing to write them under and the first save that reaches one would throw."
                    + builder
                    + "\nOpen the project in the editor, which assigns the numbers automatically on "
                    + "the next assembly reload, or run Tools > Wallstop Studios > Unity Helpers > "
                    + "Assign WallstopProto Subtype Tags (headless: -executeMethod "
                    + "WallstopStudios.UnityHelpers.Editor.Tools.WProtoSubtypeTagAssigner.AssignFromCommandLine), "
                    + "then commit the WProtoSubtypeTags.cs it writes."
            );
        }
    }
}
