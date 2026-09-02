// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;

    /// <summary>
    /// Runs one action after the editor has finished loading, exactly once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>EditorApplication.delayCall</c> alone is not enough. An editor nothing is interacting with
    /// -- a background window, a CI editor driven over a socket -- does not necessarily pump that
    /// tick at all: measured on 6000.4.6f1, a queued call was still pending minutes after the reload
    /// that queued it. <c>AssemblyReloadEvents.afterAssemblyReload</c> is a callback Unity invokes
    /// rather than a tick it might not reach.
    /// </para>
    /// <para>
    /// Both are armed and the first to fire wins, because the work here is deferred for a real
    /// reason -- asset and <c>EditorPrefs</c> access during static initialization can hang the
    /// editor -- and both hooks are after that point
    /// (<see href="https://github.com/Ambiguous-Interactive/unity-helpers/issues/684">#684</see>).
    /// </para>
    /// </remarks>
    internal static class EditorStartupCallback
    {
        /// <summary>Arms <paramref name="work"/> to run once, on whichever hook fires first.</summary>
        /// <param name="work">The work to run; a <c>null</c> is ignored.</param>
        internal static void RunOnce(Action work)
        {
            if (work == null)
            {
                return;
            }

            OneShot oneShot = new(work);
            AssemblyReloadEvents.afterAssemblyReload += oneShot.Run;
            EditorApplication.delayCall += oneShot.Run;
        }

        /// <summary>Holds the work until one of the two hooks reaches it.</summary>
        internal sealed class OneShot
        {
            private Action _work;

            /// <summary>Creates a one-shot around <paramref name="work"/>.</summary>
            /// <param name="work">The work to run at most once.</param>
            internal OneShot(Action work)
            {
                _work = work;
            }

            /// <summary>Runs the work if it has not run, and disarms both hooks.</summary>
            internal void Run()
            {
                AssemblyReloadEvents.afterAssemblyReload -= Run;
                EditorApplication.delayCall -= Run;

                Action work = _work;
                if (work == null)
                {
                    return;
                }

                _work = null;
                work();
            }
        }
    }
#endif
}
