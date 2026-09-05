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

        internal static Func<bool> isCompilationPendingEvaluator = () =>
            EditorApplication.isCompiling;

        internal static Func<bool> IsCompilationPendingEvaluator
        {
            get => isCompilationPendingEvaluator;
            set
            {
                if (value != null)
                {
                    isCompilationPendingEvaluator = value;
                    return;
                }

                isCompilationPendingEvaluator = () => EditorApplication.isCompiling;
            }
        }

        internal static bool SkipCompilationRequestForTests { get; set; }

        internal static Action AssetsRefreshedForTests { get; set; }

        internal static Action CompilationRequestedForTests { get; set; }

        [MenuItem(MenuItemPath)]
        public static void RequestFromMenu()
        {
            Request();
        }

        [Shortcut(ShortcutId, KeyCode.R, ShortcutModifiers.Alt | ShortcutModifiers.Action)]
        public static void RequestFromShortcut()
        {
            Request();
        }

        private static void Request()
        {
            if (IsCompilationPending())
            {
                Debug.Log(
                    $"{LogPrefix} Script compilation already in progress; manual request skipped."
                );
                return;
            }

            bool skipCompilation = SkipCompilationRequestForTests;

            try
            {
                AssetDatabase.Refresh(RefreshOptions);
                AssetsRefreshedForTests?.Invoke();

                if (skipCompilation)
                {
                    Debug.Log(
                        $"{LogPrefix} Asset database refreshed; compilation request skipped (tests)."
                    );
                    return;
                }

                CompilationRequestedForTests?.Invoke();
                CompilationPipeline.RequestScriptCompilation();
                Debug.Log($"{LogPrefix} Refreshed assets and requested script compilation.");
            }
            finally
            {
                // Reset the skip flag even if callbacks throw so later compile requests retain normal behavior.
                if (skipCompilation)
                {
                    SkipCompilationRequestForTests = false;
                }
            }
        }

        private static bool IsCompilationPending()
        {
            if (isCompilationPendingEvaluator == null)
            {
                Debug.LogWarning(
                    $"{LogPrefix} Compilation pending evaluator is null; resetting to default."
                );
                isCompilationPendingEvaluator = () => EditorApplication.isCompiling;
            }

            return isCompilationPendingEvaluator();
        }
    }
}
