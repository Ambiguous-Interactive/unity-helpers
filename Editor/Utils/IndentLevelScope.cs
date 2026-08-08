// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    ///     A disposable scope that changes <see cref="EditorGUI.indentLevel"/> and restores it when
    ///     disposed, including when the body throws.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Hand-written <c>indentLevel++</c> / <c>indentLevel--</c> pairs are not exception-safe, and
    ///     an IMGUI body throwing is ordinary rather than exceptional: Unity unwinds the drawer with
    ///     <see cref="UnityEngine.GUIUtility.ExitGUI"/> whenever a control opens an object picker. A
    ///     leaked increment indents every property drawn after it for the rest of the pass.
    ///     </para>
    ///     <para>
    ///     Disposal restores the level that was current when the scope was taken rather than
    ///     decrementing, so it also heals a nested drawer that leaked one of its own.
    ///     </para>
    ///     <para>
    ///     Prefer a <c>using</c> declaration, which needs no re-indentation of the body:
    ///     </para>
    ///     <code>
    ///     using IndentLevelScope indentScope = IndentLevelScope.Indent();
    ///     EditorGUILayout.PropertyField(property, true);
    ///     </code>
    ///     <para>
    ///     Unity's own <c>EditorGUI.IndentLevelScope</c> is a class, so it allocates on every repaint.
    ///     This one is a <c>readonly struct</c> and allocates nothing.
    ///     </para>
    /// </remarks>
    public readonly struct IndentLevelScope : IDisposable
    {
        private readonly int _previousIndentLevel;

        // One scope is one indent change, so it must produce exactly one restore. A copy of this
        // struct carries a copy of any plain bool, and each copy would then restore on its own --
        // an early restore from a copy would un-indent the rest of a still-open scope's body.
        // See DisposalLease for why a copy is the ordinary way a scope gets closed twice.
        private readonly DisposalLease _lease;

        private IndentLevelScope(int level)
        {
            _lease = DisposalLeases.Acquire();
            _previousIndentLevel = EditorGUI.indentLevel;
            EditorGUI.indentLevel = level < 0 ? 0 : level;
        }

        /// <summary>
        ///     Indents by <paramref name="levels"/> relative to the current level for the life of the scope.
        /// </summary>
        /// <param name="levels">How many levels to indent by. Negative values outdent; the level never goes below zero.</param>
        /// <returns>A scope that restores the previous indent level when disposed.</returns>
        public static IndentLevelScope Indent(int levels = 1)
        {
            return new IndentLevelScope(EditorGUI.indentLevel + levels);
        }

        /// <summary>
        ///     Sets an absolute indent level for the life of the scope, most often zero to draw a
        ///     control against the left edge regardless of nesting.
        /// </summary>
        /// <param name="level">The level to draw at. Negative values are treated as zero.</param>
        /// <returns>A scope that restores the previous indent level when disposed.</returns>
        public static IndentLevelScope AtLevel(int level)
        {
            return new IndentLevelScope(level);
        }

        /// <summary>
        ///     Restores the indent level that was current when this scope was taken.
        /// </summary>
        /// <remarks>
        ///     The level is restored at most once per scope, however many copies of this struct exist
        ///     and whenever each is disposed.
        /// </remarks>
        public void Dispose()
        {
            if (!_lease.TryClaim())
            {
                return;
            }

            EditorGUI.indentLevel = _previousIndentLevel;
        }
    }
#endif
}
