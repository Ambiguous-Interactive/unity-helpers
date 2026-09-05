// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
    using UnityEditor;
    using UnityEngine;

    /// <summary>
    /// Assigns a field number to every <c>[WProtoSubtype(typeof(Base))]</c> that does not have one
    /// yet, after each assembly reload, without anybody asking.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what makes the numberless form zero-touch rather than nearly zero-touch. A step the
    /// developer has to remember is a step somebody forgets, and the thing they would find out at
    /// the end is a player build that will not compile. Adding a subtype is now one attribute: the
    /// declaration compiles with a warning, this runs, the manifest gains an entry, and the reload
    /// that follows compiles clean.
    /// </para>
    /// <para>
    /// It runs only when a number is actually MISSING. A manifest that has merely drifted -- a
    /// subtype whose number moved into its own attribute, an entry whose type has been deleted --
    /// is left to the menu item, because both of those rewrites change what the file says about
    /// types the developer just edited, and a diff that appears on its own after an unrelated
    /// recompile is worse than one somebody asked for.
    /// </para>
    /// <para>
    /// Retirement in particular is never automatic, and that is a property of the PLAN rather than
    /// of the write decision. This pass asks for <see cref="WProtoSubtypeTagDiscovery.Partial"/>,
    /// under which an entry whose declaration is not in <c>TypeCache</c> keeps its number instead of
    /// being retired. <c>TypeCache</c> answers for the editor's own compilation, so a subtype behind
    /// <c>#if !UNITY_EDITOR</c> or a platform define, or one in an assembly that failed to compile,
    /// is absent from it and alive in the player. Deciding only whether to write left every one of
    /// those retirements sitting in the plan, ready to be committed by the first unrelated
    /// declaration that needed a number.
    /// </para>
    /// <para>
    /// Idempotency is a hard requirement rather than a nicety, because a write triggers a reimport
    /// and a reimport triggers another reload. Nothing is written when the manifest already says
    /// what assignment produces, so the second reload does no work, writes no file, and does not
    /// call <c>AssetDatabase.Refresh</c>. Both entry points below rely on that: whichever fires
    /// first does the work, and the other finds nothing to do.
    /// </para>
    /// <para>
    /// <c>AssemblyReloadEvents.afterAssemblyReload</c> runs the pass DIRECTLY rather than through
    /// <c>EditorApplication.delayCall</c>, which was measured to matter. An editor that nothing is
    /// interacting with -- a background window, a CI editor driven over a socket -- does not
    /// necessarily pump <c>delayCall</c> at all: in editor 6000.4.6f1 a queued call was still
    /// pending minutes after the reload that queued it, so the manifest was never written even
    /// though the assignment itself was correct. The reload callback is a callback Unity invokes,
    /// not a tick it might not reach. <c>delayCall</c> is kept only for the retry path, where the
    /// alternative is surveying a project that is still compiling.
    /// </para>
    /// <para>
    /// <c>EditorApplication.isUpdating</c> is deliberately NOT a reason to defer. The reload this
    /// runs from is routinely raised while the import that caused it is still finishing, so
    /// treating that as "come back later" hands the work to the tick that may never arrive. Nothing
    /// here reads the asset database: the survey is <c>TypeCache</c> and reflection over a domain
    /// that has just finished loading, and the only asset-database call is the refresh after a
    /// write actually happened.
    /// </para>
    /// <para>
    /// Undo policy: Tier C, inherited from <see cref="WProtoSubtypeTagAssigner"/>. It writes source
    /// files and triggers a reimport, neither of which Unity's undo system reverses.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    public static class WProtoSubtypeTagAutoAssign
    {
        private const string EnabledKey =
            "WallstopStudios.UnityHelpers.WProtoSubtypeTagAutoAssign.Enabled";

        private const string MenuPath =
            "Tools/Wallstop Studios/Unity Helpers/Assign WallstopProto Subtype Tags Automatically";

        private static bool _scheduled;
        private static bool _running;

        static WProtoSubtypeTagAutoAssign()
        {
            // Both initialization paths are idempotent; neither event ordering needs to be assumed.
            AssemblyReloadEvents.afterAssemblyReload += RunWhenSettled;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Schedule();
        }

        /// <summary>
        /// Whether the automatic pass runs at all.
        /// </summary>
        /// <remarks>
        /// A tool that writes source files unprompted needs a switch, even though leaving it on is
        /// the point. Turning it off leaves the warning, the menu item and the build gate, so a
        /// project that prefers the explicit act still cannot ship an unnumbered subtype.
        /// </remarks>
        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, true);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        [MenuItem(MenuPath, priority = 1)]
        private static void ToggleEnabled()
        {
            Enabled = !Enabled;
            if (Enabled)
            {
                Schedule();
            }
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateToggleEnabled()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
            {
                Schedule();
            }
        }

        private static void Schedule()
        {
            if (_scheduled)
            {
                return;
            }

            _scheduled = true;
            EditorApplication.delayCall += RunWhenSettled;
        }

        private static void RunWhenSettled()
        {
            _scheduled = false;
            if (!Enabled || _running)
            {
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            {
                // Retry on leaving play mode rather than polling through an arbitrarily long session.
                return;
            }

            if (EditorApplication.isCompiling)
            {
                // TypeCache is mid-rebuild, so a run now would be a survey of a moving project.
                Schedule();
                return;
            }

            _running = true;
            try
            {
                WProtoSubtypeTagAssigner.Report report = WProtoSubtypeTagAssigner.Run(
                    true,
                    WProtoSubtypeTagDiscovery.Partial
                );
                if (0 < report.Written.Count)
                {
                    Debug.Log(report.Describe("Assigned"));
                }

                foreach (string failure in report.Failures)
                {
                    Debug.LogError("WallstopProto subtype tags: " + failure);
                }
            }
            finally
            {
                // Always clear the running flag so one failure cannot disable future assignment.
                _running = false;
            }
        }
    }
}
