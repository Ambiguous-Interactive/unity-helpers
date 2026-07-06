// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils.WButton
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using UnityEditor;
    using WallstopStudios.UnityHelpers.Editor.Settings;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// Helper class for integrating WButton functionality into custom editors.
    /// Use this when creating custom editors (including with Odin Inspector) to ensure
    /// WButton methods are drawn correctly in the inspector.
    /// </summary>
    /// <example>
    /// <code>
    /// // Example with Odin Inspector's SerializedMonoBehaviour
    /// using Sirenix.OdinInspector.Editor;
    ///
    /// public class MyOdinEditor : OdinEditor
    /// {
    ///     private WButtonEditorHelper _wButtonHelper;
    ///
    ///     protected override void OnEnable()
    ///     {
    ///         base.OnEnable();
    ///         _wButtonHelper = new WButtonEditorHelper();
    ///     }
    ///
    ///     public override void OnInspectorGUI()
    ///     {
    ///         // Draw buttons at top
    ///         _wButtonHelper.DrawButtonsAtTop(this);
    ///
    ///         // Draw your custom inspector content
    ///         base.OnInspectorGUI();
    ///
    ///         // Draw buttons at bottom and process invocations
    ///         _wButtonHelper.DrawButtonsAtBottomAndProcessInvocations(this);
    ///     }
    /// }
    /// </code>
    /// </example>
    public sealed class WButtonEditorHelper
    {
        private readonly Dictionary<WButtonGroupKey, WButtonPaginationState> _paginationStates =
            new();
        private readonly Dictionary<WButtonGroupKey, bool> _foldoutStates = new();
        private readonly List<WButtonMethodContext> _triggeredContexts = new();

        /// <summary>
        /// Draws WButton methods configured for top placement.
        /// Call this at the beginning of your OnInspectorGUI before drawing other content.
        /// </summary>
        /// <param name="editor">The editor instance (typically 'this' from your custom editor)</param>
        /// <returns>True if any buttons were drawn</returns>
        public bool DrawButtonsAtTop(Editor editor)
        {
            UnityHelpersSettings.WButtonActionsPlacement placement =
                UnityHelpersSettings.GetWButtonActionsPlacement();
            UnityHelpersSettings.WButtonFoldoutBehavior foldoutBehavior =
                UnityHelpersSettings.GetWButtonFoldoutBehavior();
            bool globalPlacementIsTop =
                placement == UnityHelpersSettings.WButtonActionsPlacement.Top;

            return WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Top,
                _paginationStates,
                _foldoutStates,
                foldoutBehavior,
                _triggeredContexts,
                globalPlacementIsTop
            );
        }

        /// <summary>
        /// Draws WButton methods configured for bottom placement.
        /// Call this at the end of your OnInspectorGUI after drawing other content.
        /// </summary>
        /// <param name="editor">The editor instance (typically 'this' from your custom editor)</param>
        /// <returns>True if any buttons were drawn</returns>
        public bool DrawButtonsAtBottom(Editor editor)
        {
            UnityHelpersSettings.WButtonActionsPlacement placement =
                UnityHelpersSettings.GetWButtonActionsPlacement();
            UnityHelpersSettings.WButtonFoldoutBehavior foldoutBehavior =
                UnityHelpersSettings.GetWButtonFoldoutBehavior();
            bool globalPlacementIsTop =
                placement == UnityHelpersSettings.WButtonActionsPlacement.Top;

            return WButtonGUI.DrawButtons(
                editor,
                WButtonPlacement.Bottom,
                _paginationStates,
                _foldoutStates,
                foldoutBehavior,
                _triggeredContexts,
                globalPlacementIsTop
            );
        }

        /// <summary>
        /// Processes any button invocations that were triggered during the current GUI frame.
        /// Call this after all DrawButtons calls, typically at the very end of OnInspectorGUI.
        /// </summary>
        public void ProcessInvocations()
        {
            if (_triggeredContexts.Count > 0)
            {
                WButtonInvocationController.ProcessTriggeredMethods(_triggeredContexts);
                _triggeredContexts.Clear();
            }
        }

        /// <summary>
        /// Convenience method that draws buttons at bottom and processes invocations.
        /// This is the most common pattern - call this at the end of your OnInspectorGUI.
        /// </summary>
        /// <param name="editor">The editor instance (typically 'this' from your custom editor)</param>
        /// <returns>True if any buttons were drawn</returns>
        public bool DrawButtonsAtBottomAndProcessInvocations(Editor editor)
        {
            bool anyDrawn = DrawButtonsAtBottom(editor);
            ProcessInvocations();
            return anyDrawn;
        }

        /// <summary>
        /// Draws all buttons (both top and bottom placement) and processes invocations.
        /// Use this when you want to draw all buttons in a single location regardless of placement settings.
        /// </summary>
        /// <param name="editor">The editor instance (typically 'this' from your custom editor)</param>
        /// <returns>True if any buttons were drawn</returns>
        public bool DrawAllButtonsAndProcessInvocations(Editor editor)
        {
            bool anyDrawnTop = DrawButtonsAtTop(editor);
            bool anyDrawnBottom = DrawButtonsAtBottom(editor);
            ProcessInvocations();
            return anyDrawnTop || anyDrawnBottom;
        }
    }
#endif
}
