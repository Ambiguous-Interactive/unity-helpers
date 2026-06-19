// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Helper;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class UnityMainThreadDispatcherEditorTests
    {
        private const HideFlags ExpectedHideFlags =
            HideFlags.HideInHierarchy | HideFlags.HideInInspector | HideFlags.NotEditable;

        [Ignore("This expectation has changed, revisit later.")]
        [Test]
        public void DispatcherUsesSceneFriendlyHideFlagsInEditMode()
        {
            UnityMainThreadDispatcher existing =
                UnityObjectExtensions.FindObjectOfTypeShim<UnityMainThreadDispatcher>();
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject); // UNH-SUPPRESS: Cleanup pre-existing dispatcher before test
            }

            UnityMainThreadDispatcher dispatcher = UnityMainThreadDispatcher.Instance;
            try
            {
                Assert.IsFalse(Application.isPlaying, "Edit mode test must run outside play mode.");
                Assert.AreEqual(
                    ExpectedHideFlags,
                    dispatcher.hideFlags,
                    "Dispatcher hide flags should remain hidden and unsaved in edit mode."
                );
            }
            finally
            {
                if (dispatcher != null)
                {
                    Object.DestroyImmediate(dispatcher.gameObject); // UNH-SUPPRESS: Test cleanup in finally block
                }
            }
        }
    }
}
