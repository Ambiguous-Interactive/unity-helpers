// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
    using System;
    using UnityEditor;
    using UnityEditor.Compilation;
    using UnityEditor.ShortcutManagement;
    using UnityEngine;

    /// <summary>
    /// Provides menu and shortcut access to trigger Unity's script recompilation pipeline.
    /// </summary>
    public static class ManualRecompile
    {
        private const string ShortcutId = "Wallstop Studios/Request Script Compilation";
        private const string MenuItemPath =
            "Tools/Wallstop Studios/Unity Helpers/Request Script Compilation";
        private const string LogPrefix = "[Unity Helpers]";

        private static readonly ImportAssetOptions RefreshOptions =
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport;

        [MenuItem(MenuItemPath)]
        public static void RequestFromMenu()
        {
            Request(EditorApplication.isCompiling);
        }

        [Shortcut(ShortcutId, KeyCode.R, ShortcutModifiers.Alt | ShortcutModifiers.Action)]
        public static void RequestFromShortcut()
        {
            Request(EditorApplication.isCompiling);
        }

        internal static void Request(bool compilationPending)
        {
            if (compilationPending)
            {
                Debug.Log(
                    $"{LogPrefix} Script compilation already in progress; manual request skipped."
                );
                return;
            }
            AssetDatabase.Refresh(RefreshOptions);
            CompilationPipeline.RequestScriptCompilation();
            Debug.Log($"{LogPrefix} Refreshed assets and requested script compilation.");
        }
    }
}
