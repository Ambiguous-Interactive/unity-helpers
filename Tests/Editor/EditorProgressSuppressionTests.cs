// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
namespace WallstopStudios.UnityHelpers.Tests
{
#if UNITY_EDITOR
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    public sealed class EditorProgressSuppressionTests : CommonTestBase
    {
        [TestCase(0f)]
        [TestCase(0.5f)]
        [TestCase(1f)]
        public void SuppressedProgressDoesNotRequestCancellation(float progress)
        {
            bool previousSuppress = EditorUi.Suppress;
            try
            {
                EditorUi.Suppress = true;
                Assert.DoesNotThrow(() => EditorUi.ShowProgress("Test", "Suppressed", progress));
                Assert.IsFalse(EditorUi.CancelableProgress("Test", "Suppressed", progress));
                Assert.DoesNotThrow(EditorUi.ClearProgress);
            }
            finally
            {
                EditorUi.Suppress = previousSuppress;
            }
        }
    }
#endif
}
