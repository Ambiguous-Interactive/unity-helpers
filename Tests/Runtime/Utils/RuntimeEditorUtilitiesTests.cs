// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
#if UNITY_EDITOR
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Editor.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class RuntimeEditorUtilitiesTests : CommonTestBase
    {
        [Test]
        public void EditorUtilitiesBelongToRuntimeAssembly()
        {
            Assert.That(
                typeof(AnimatedSpriteFrames).Assembly,
                Is.EqualTo(typeof(Helpers).Assembly)
            );
            Assert.That(typeof(SceneCanonicalizer).Assembly, Is.EqualTo(typeof(Helpers).Assembly));
        }

        [Test]
        public void RuntimeAssemblyCanReadAnimationFrames()
        {
            AnimationClip clip = Track(new AnimationClip());
            List<Sprite> frames = new();
            AnimatedSpriteFrames.AddFramesOf(clip, string.Empty, frames);
            Assert.That(frames, Is.Empty);
        }

        [Test]
        public void RuntimeAssemblyCanCanonicalizeSceneText()
        {
            const string yaml =
                "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n"
                + "--- !u!104 &2\nRenderSettings:\n  m_ObjectHideFlags: 0\n";
            Assert.That(
                SceneCanonicalizer.TryCanonicalizeYaml(yaml, out string result, out _),
                Is.True
            );
            Assert.That(result, Does.Contain("RenderSettings:\n  m_ObjectHideFlags: 0\n"));
            Assert.That(SceneCanonicalizer.CanonicalizeYaml(result), Is.EqualTo(result));
        }
    }
#endif
}
