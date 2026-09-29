// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Utils
{
#if UNITY_EDITOR
    using System;
    using System.Reflection;
    using UnityEditor;

    public static class EditorUtilities
    {
        public static string GetCurrentPathOfProjectWindow()
        {
            Type projectWindowUtilType = typeof(ProjectWindowUtil);
            MethodInfo getActiveFolderPath = projectWindowUtilType.GetMethod(
                "GetActiveFolderPath",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            object obj = getActiveFolderPath?.Invoke(null, Array.Empty<object>());
            return obj?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Reports whether this editor session was launched with Unity Test Runner command-line
        /// arguments.
        /// </summary>
        /// <returns>
        /// True when a command-line argument is exactly <c>-runTests</c>, <c>-testResults</c> or
        /// <c>-testPlatform</c>, case-insensitively. False when none do, when the session was
        /// started from the in-editor Test Runner window, or when the command line cannot be read.
        /// </returns>
        /// <remarks>
        /// Gate fixture-consuming work in window <c>OnEnable</c> on this alongside
        /// <see cref="UnityEngine.Application.isBatchMode"/> and
        /// <see cref="WallstopStudios.UnityHelpers.Core.Helper.Helpers.IsRunningInContinuousIntegration"/>.
        /// Never throws.
        /// </remarks>
        public static bool IsInvokedByTestRunner()
        {
            try
            {
                return IsTestRunnerCommandLine(Environment.GetCommandLineArgs());
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsTestRunnerCommandLine(string[] args)
        {
            if (args == null)
            {
                return false;
            }

            foreach (string argument in args)
            {
                if (
                    string.Equals(argument, "-runTests", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(argument, "-testResults", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(argument, "-testPlatform", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }

            return false;
        }
    }
#endif
}
