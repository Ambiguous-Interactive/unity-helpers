// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools
{
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using UnityEngine.Rendering;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class AnimationEventEditorSmokeTests : BatchedEditorTestBase
    {
        [SetUp]
        public void SetUp()
        {
            base.BaseSetUp();
            // Showing an EditorWindow under -batchmode -nographics logs the benign
            // "No graphic device is available to initialize the view." error (Unity 6+),
            // which the Unity Test Framework treats as a failure. Tolerate it in headless
            // CI only; on a machine with a graphics device this is inert. The framework
            // resets ignoreFailingMessages after each test, so no teardown restore is
            // needed (and restoring it would re-catch the same error if the window closes
            // during teardown).
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                LogAssert.ignoreFailingMessages = true;
            }
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
        }

        [Test]
        public void AnimationEventEditorOpensAndClosesWithoutAnimator()
        {
            AnimationEventEditor first = Track(EditorWindow.CreateWindow<AnimationEventEditor>());
            first.Show();
            first.Repaint();
            first.Close();

            AnimationEventEditor second = Track(EditorWindow.CreateWindow<AnimationEventEditor>());
            second.Show();
            second.Repaint();
            second.Close();
        }
    }
}
