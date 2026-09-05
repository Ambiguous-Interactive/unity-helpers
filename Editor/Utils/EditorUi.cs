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
        private static bool _suppressManual;
        private static bool _suppressAuto;

        public static bool Suppress
        {
            get => _suppressManual || _suppressAuto;
            set => _suppressManual = value;
        }

        static EditorUi()
        {
            try
            {
                _suppressAuto =
                    Application.isBatchMode
                    || IsInvokedByTestRunner()
                    || Helpers.IsRunningInContinuousIntegration;
            }
            catch
            {
                _suppressAuto = false;
            }

            // Tests suppress UI explicitly so the Editor assembly needs no TestRunner dependency.
        }

        private static bool IsInvokedByTestRunner()
        {
            string[] args = Environment.GetCommandLineArgs();
            foreach (string a in args)
            {
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
