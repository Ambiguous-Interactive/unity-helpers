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
        /// Reports whether this editor session was launched by the Unity Test Runner.
        /// </summary>
        /// <returns>
        /// True when any command-line argument mentions <c>runTests</c>, <c>testResults</c> or
        /// <c>testPlatform</c>, case-insensitively.
        /// </returns>
        /// <remarks>
        /// Test-runner launches open window <c>OnEnable</c> during the run itself, which can
        /// consume fixtures or produce logs the test session never asked for.
        /// </remarks>
        internal static bool IsInvokedByTestRunner()
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
    }
#endif
}
