// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.TestSupport;

    /// <summary>Restricts synchronous object-tracking fixtures to EditMode.</summary>
    public abstract class SynchronousEditorTestBase : UnityObjectTrackingTestBase
    {
        /// <summary>Refuses PlayMode lifecycle tests in this synchronous Editor scope.</summary>
        [SetUp]
        public void RequireEditMode()
        {
            Assert.IsFalse(Application.isPlaying, "This fixture requires EditMode.");
        }
    }
#endif
}
