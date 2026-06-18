// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools
{
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class AnimationEventEditorSmokeTests : CommonTestBase
    {
        [Test]
        public void AnimationEventEditorCanBeInstantiatedRepeatedlyWithoutAnimator()
        {
            AnimationEventEditor first = Track(
                ScriptableObject.CreateInstance<AnimationEventEditor>()
            );
            AnimationEventEditor second = Track(
                ScriptableObject.CreateInstance<AnimationEventEditor>()
            );

            Assert.IsTrue(first != null);
            Assert.IsTrue(second != null);
            Assert.AreNotSame(first, second);
        }
    }
}
