// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Core.Helper;

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
    ///     In normal LIFO use, disposal restores the level observed when the scope was taken rather
    ///     than decrementing. Copies share one disposal claim. If nested package scopes are
    ///     disposed out of order, the newest active scope stays applied and the final disposal
    ///     restores the value from before that package scope chain began.
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
    ///     This one is a <c>readonly struct</c> and creates no per-scope garbage after its shared
    ///     owner has warmed to the maximum concurrent nesting depth.
    ///     </para>
    /// </remarks>
    public readonly struct IndentLevelScope : IDisposable
    {
        private readonly RestorableGlobal<int>.Scope _scope;

        private IndentLevelScope(int level)
        {
            _scope = EditorGlobalScopes.IndentLevel.Borrow(level < 0 ? 0 : level);
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
            _scope.Dispose();
        }
    }
#endif
}
