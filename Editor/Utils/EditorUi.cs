// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Editor.Utils
{
    using System;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;

    public static class EditorUi
    {
        // Manual suppression set by code/tests and automatic suppression inferred from environment
        private static bool _suppressManual;
        private static bool _suppressAuto;

        public static bool Suppress
        {
            // Only suppress when explicitly requested (_suppressManual, set by tests)
            // or in non-interactive environments (_suppressAuto, set for batch mode/CI).
            // Tests set EditorUi.Suppress = true in their SetUp via CommonTestBase.
            get => _suppressManual || _suppressAuto;
            set => _suppressManual = value;
        }

        static EditorUi()
        {
            try
            {
                // Suppress only when actually running in non-interactive contexts
                // such as batch mode, the Unity Test Runner CLI, or CI environments.
                _suppressAuto =
                    Application.isBatchMode
                    || IsInvokedByTestRunner()
                    || Helpers.IsRunningInContinuousIntegration;
            }
            catch
            {
                _suppressAuto = false;
            }

            // Note: We avoid taking a hard compile-time dependency on the TestRunner API here.
            // Tests should set EditorUi.Suppress = true in their SetUp (CommonTestBase does this).
        }

        private static bool IsInvokedByTestRunner()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; ++i)
            {
                string a = args[i];
                if (
                    0 <= a.IndexOf("runTests", StringComparison.OrdinalIgnoreCase)
                    || 0 <= a.IndexOf("testResults", StringComparison.OrdinalIgnoreCase)
                    || 0 <= a.IndexOf("testPlatform", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }
            return false;
        }

        public static bool Confirm(
            string title,
            string message,
            string ok,
            string cancel,
            bool defaultWhenSuppressed = true
        )
        {
            if (Suppress)
            {
                return defaultWhenSuppressed;
            }
            return EditorUtility.DisplayDialog(title, message, ok, cancel);
        }

        public static void Info(string title, string message)
        {
            if (Suppress)
            {
                return;
            }
            EditorUtility.DisplayDialog(title, message, "OK");
        }

        public static void ShowProgress(string title, string info, float progress)
        {
            if (Suppress)
            {
                return;
            }
            EditorUtility.DisplayProgressBar(title, info, progress);
        }

        public static bool CancelableProgress(string title, string info, float progress)
        {
            if (Suppress)
            {
                return false;
            }
            return EditorUtility.DisplayCancelableProgressBar(title, info, progress);
        }

        public static void ClearProgress()
        {
            EditorUtility.ClearProgressBar();
        }

        public static string OpenFilePanel(string title, string directory, string extension)
        {
            if (Suppress)
            {
                return string.Empty;
            }
            return EditorUtility.OpenFilePanel(title, directory, extension);
        }

        public static string OpenFolderPanel(string title, string directory, string defaultName)
        {
            if (Suppress)
            {
                return string.Empty;
            }
            return EditorUtility.OpenFolderPanel(title, directory, defaultName);
        }

        // Intentionally no hard dependency on TestRunner API to keep Editor asmdef clean.
    }
}
#endif
