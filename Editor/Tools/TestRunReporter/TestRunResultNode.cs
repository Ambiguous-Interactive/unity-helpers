// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Editor.Tools
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using UnityEditor.TestTools.TestRunner.Api;

    /// <summary>
    ///     One node of a test run result tree, in a shape a caller can build by hand.
    /// </summary>
    // Use a constructible result model so reporting can be verified without TestRunnerApi.
    internal sealed class TestRunResultNode
    {
        /// <summary>
        ///     The fully qualified name of the assembly, suite or test case this node represents.
        /// </summary>
        public string fullName = string.Empty;

        /// <summary>
        ///     The outcome the test runner reported for this node.
        /// </summary>
        public TestStatus status = TestStatus.Inconclusive;

        /// <summary>
        ///     The failure message the test runner reported, empty when there is none.
        /// </summary>
        public string message = string.Empty;

        /// <summary>
        ///     The stack trace the test runner reported, empty when there is none.
        /// </summary>
        public string stackTrace = string.Empty;

        /// <summary>
        ///     How long the test runner reported this node took, in seconds.
        /// </summary>
        public double durationSeconds;

        /// <summary>
        ///     When the compiled assembly behind an assembly-level node was last written.
        /// </summary>
        public DateTime? assemblyBuiltUtc;

        /// <summary>
        ///     The children of this node; a node with none is a single test case.
        /// </summary>
        public readonly List<TestRunResultNode> children = new();
    }
#endif
}
