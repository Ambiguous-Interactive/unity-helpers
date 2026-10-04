// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using System.IO;
    using UnityEditor;
    using UnityEditor.Compilation;
    using UnityEditor.TestTools.TestRunner.Api;
    using UnityEngine;
    using static WallstopStudios.UnityHelpers.Editor.Tools.TestRunReporter;

    /// <summary>Provides test-side setup and inspection for TestRunReporter.</summary>
    internal static class TestRunReporterTestAccess
    {
        internal static void ClearRunSession()
        {
            ClearActiveOwner();
        }
    }
#endif
}
