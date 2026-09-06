// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
#if UNITY_EDITOR
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using Object = UnityEngine.Object;

    [TestFixture]
    public sealed class SpriteAnimationHelpersTests : CommonTestBase
    {
        [Test]
        public void KeyframesPreserveTimesDuplicatesNullsAndBindingPaths()
        {
            AnimationClip clip = Track(new AnimationClip());
            Sprite sprite = MakeSprite(0f, 0f);
            SetFrames(clip, "Child", (0f, sprite), (0.25f, sprite), (1.5f, null));
            SetFrames(clip, string.Empty, (0f, sprite), (0.75f, sprite));

            IReadOnlyList<SpriteAnimationKeyframe> frames =
                SpriteAnimationHelpers.SpriteKeyframesOf(clip);

            Assert.That(frames.Count, Is.EqualTo(5));
            Assert.That(frames[0].BindingPath, Is.EqualTo(string.Empty));
            Assert.That(frames[1].BindingPath, Is.EqualTo("Child"));
            Assert.That(frames[2].Time, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(frames[3].Time, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(frames[4].Time, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.IsTrue(frames[2].Sprite == sprite);
            Assert.IsTrue(frames[4].Sprite == null);
            Assert.That(
                SpriteAnimationHelpers.SpriteKeyframesOf(clip, string.Empty).Count,
                Is.EqualTo(2)
            );
            Assert.That(
                SpriteAnimationHelpers.SpriteKeyframesOf(clip, "Child").Count,
                Is.EqualTo(3)
            );
            Assert.That(SpriteAnimationHelpers.SpriteKeyframesOf(clip, "child"), Is.Empty);
        }

        [Test]
        public void IgnoresOtherComponentsAndProperties()
        {
            AnimationClip clip = Track(new AnimationClip());
            Sprite sprite = MakeSprite(0f, 0f);
            ObjectReferenceKeyframe[] keys =
            {
                new ObjectReferenceKeyframe { time = 0f, value = sprite },
            };
            AnimationUtility.SetObjectReferenceCurve(
                clip,
                EditorCurveBinding.PPtrCurve(
                    string.Empty,
                    typeof(ParticleSystemRenderer),
                    UnityExtensions.SpriteBindingProperty
                ),
                keys
            );
            AnimationUtility.SetObjectReferenceCurve(
                clip,
                EditorCurveBinding.PPtrCurve(
                    string.Empty,
                    typeof(SpriteRenderer),
                    nameof(Renderer.sharedMaterial)
                ),
                keys
            );
            Assert.That(SpriteAnimationHelpers.SpriteKeyframesOf(clip), Is.Empty);
            Assert.That(SpriteAnimationHelpers.LastMovingFrame(clip, 0f), Is.EqualTo(-1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrDestroyedClipHasNoKeyframesOrMotion(bool destroyed)
        {
            AnimationClip clip = null;
            if (destroyed)
            {
                clip = Track(new AnimationClip());
                Object.DestroyImmediate(clip); // UNH-SUPPRESS: Verify destroyed clip handling.
            }
            Assert.That(SpriteAnimationHelpers.SpriteKeyframesOf(clip), Is.Empty);
            Assert.That(SpriteAnimationHelpers.LastMovingFrame(clip, 0f), Is.EqualTo(-1));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(8)]
        public void EmptySingleOrStationaryClipHasNoMotion(int count)
        {
            AnimationClip clip = Track(new AnimationClip());
            Sprite sprite = MakeSprite(0f, 0f);
            (float, Sprite)[] frames = new (float, Sprite)[count];
            for (int index = 0; index < count; index++)
            {
                frames[index] = (index * 0.125f, sprite);
            }
            SetFrames(clip, string.Empty, frames);
            Assert.That(SpriteAnimationHelpers.LastMovingFrame(clip, 0f), Is.EqualTo(-1));
        }

        [TestCase(0.1f)]
        [TestCase(0.5f)]
        [TestCase(3f)]
        public void LandingIsAtFrameFortySixBeforeSeventeenSettlingFrames(float threshold)
        {
            AnimationClip clip = Track(new AnimationClip { frameRate = 12f });
            Sprite raised = MakeSprite(0f, 0f);
            Sprite landed = MakeSprite(0f, -3.56f);
            Sprite settled = MakeSprite(0f, -3.5f);
            (float, Sprite)[] frames = new (float, Sprite)[63];
            for (int index = 0; index < frames.Length; index++)
            {
                frames[index] = (
                    index / 12f,
                    index < 45 ? raised
                    : index == 45 ? landed
                    : settled
                );
            }
            SetFrames(clip, string.Empty, frames);
            int moving = SpriteAnimationHelpers.LastMovingFrame(clip, threshold);
            Assert.That(moving, Is.EqualTo(45));
            Assert.That(
                SpriteAnimationHelpers.SpriteKeyframesOf(clip)[moving].Time,
                Is.EqualTo(3.75f).Within(0.0001f)
            );
        }

        [Test]
        public void LastImpactWinsEvenWhenEarlierImpactWasLarger()
        {
            AnimationClip clip = Track(new AnimationClip());
            SetFrames(
                clip,
                string.Empty,
                (0f, MakeSprite(0f, 0f)),
                (0.3f, MakeSprite(0f, -8f)),
                (0.7f, MakeSprite(0f, -4f)),
                (1.1f, MakeSprite(0f, -4.0625f))
            );
            Assert.That(SpriteAnimationHelpers.LastMovingFrame(clip, 0.125f), Is.EqualTo(2));
        }

        [TestCase(0f, 1)]
        [TestCase(0.499f, 1)]
        [TestCase(0.5f, -1)]
        [TestCase(0.501f, -1)]
        [TestCase(float.MaxValue, -1)]
        [TestCase(-1f, -1)]
        [TestCase(float.NaN, -1)]
        [TestCase(float.PositiveInfinity, -1)]
        [TestCase(float.NegativeInfinity, -1)]
        public void ThresholdIsStrictFiniteAndNonnegative(float threshold, int expected)
        {
            AnimationClip clip = Track(new AnimationClip());
            SetFrames(clip, string.Empty, (0f, MakeSprite(0f, 0f)), (1f, MakeSprite(0f, -0.5f)));
            Assert.That(
                SpriteAnimationHelpers.LastMovingFrame(clip, threshold),
                Is.EqualTo(expected)
            );
        }

        [TestCase(SpriteBoundsEdge.Left, 1)]
        [TestCase(SpriteBoundsEdge.Right, 1)]
        [TestCase(SpriteBoundsEdge.Bottom, -1)]
        [TestCase(SpriteBoundsEdge.Top, -1)]
        [TestCase((SpriteBoundsEdge)0, -1)]
        [TestCase((SpriteBoundsEdge)99, -1)]
        public void EdgeSelectionDistinguishesHorizontalFromVerticalMotion(
            SpriteBoundsEdge edge,
            int expected
        )
        {
            AnimationClip clip = Track(new AnimationClip());
            SetFrames(clip, string.Empty, (0f, MakeSprite(0f, 0f)), (1f, MakeSprite(-1f, 0f)));
            Assert.That(
                SpriteAnimationHelpers.LastMovingFrame(clip, 0.5f, edge),
                Is.EqualTo(expected)
            );
        }

        [TestCase(SpriteBoundsEdge.Left, -1)]
        [TestCase(SpriteBoundsEdge.Right, 1)]
        [TestCase(SpriteBoundsEdge.Bottom, -1)]
        [TestCase(SpriteBoundsEdge.Top, 1)]
        public void EdgeSelectionUsesBoundsRatherThanCenterOrSize(
            SpriteBoundsEdge edge,
            int expected
        )
        {
            AnimationClip clip = Track(new AnimationClip());
            SetFrames(clip, string.Empty, (0f, MakeSprite(0f, 0f)), (1f, MakeSprite(0f, 0f, 2f)));
            Assert.That(
                SpriteAnimationHelpers.LastMovingFrame(clip, 0.5f, edge),
                Is.EqualTo(expected)
            );
        }

        [Test]
        public void IndependentBindingsNeverCreateCrossRendererMotion()
        {
            AnimationClip clip = Track(new AnimationClip());
            Sprite first = MakeSprite(0f, 0f);
            Sprite second = MakeSprite(0f, -100f);
            SetFrames(clip, "First", (0f, first), (1f, first));
            SetFrames(clip, "Second", (0.5f, second), (1.5f, second));
            Assert.That(SpriteAnimationHelpers.LastMovingFrame(clip, 0f), Is.EqualTo(-1));
            SetFrames(clip, "First", (0f, first), (1f, MakeSprite(0f, -1f)));
            Assert.That(SpriteAnimationHelpers.LastMovingFrame(clip, 0.5f), Is.EqualTo(2));
            Assert.That(
                SpriteAnimationHelpers.LastMovingFrame(clip, 0.5f, bindingPath: "First"),
                Is.EqualTo(1)
            );
            Assert.That(
                SpriteAnimationHelpers.LastMovingFrame(clip, 0.5f, bindingPath: "Second"),
                Is.EqualTo(-1)
            );
        }

        [Test]
        public void NullFrameResetsComparisonWithoutHidingEarlierMotion()
        {
            AnimationClip clip = Track(new AnimationClip());
            Sprite first = MakeSprite(0f, 0f);
            Sprite second = MakeSprite(0f, -1f);
            SetFrames(
                clip,
                string.Empty,
                (0f, first),
                (1f, second),
                (2f, null),
                (3f, first),
                (4f, first)
            );
            Assert.That(SpriteAnimationHelpers.LastMovingFrame(clip, 0.5f), Is.EqualTo(1));
        }

        [Test]
        public void ResultsAreFreshAfterClipEdits()
        {
            AnimationClip clip = Track(new AnimationClip());
            Sprite first = MakeSprite(0f, 0f);
            Sprite second = MakeSprite(0f, -1f);
            SetFrames(clip, string.Empty, (0f, first), (1f, first));
            IReadOnlyList<SpriteAnimationKeyframe> before =
                SpriteAnimationHelpers.SpriteKeyframesOf(clip);
            SetFrames(clip, string.Empty, (0f, first), (2f, second));
            Assert.That(SpriteAnimationHelpers.LastMovingFrame(clip, 0.5f), Is.EqualTo(1));
            Assert.That(before[1].Time, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(
                SpriteAnimationHelpers.SpriteKeyframesOf(clip)[1].Time,
                Is.EqualTo(2f).Within(0.0001f)
            );
        }

        [Test]
        public void DestroyedSpriteIsAnEmptyAssignment()
        {
            AnimationClip clip = Track(new AnimationClip());
            Sprite first = MakeSprite(0f, 0f);
            Sprite destroyed = MakeSprite(0f, -1f);
            SetFrames(clip, string.Empty, (0f, first), (1f, destroyed), (2f, first));
            Object.DestroyImmediate(destroyed); // UNH-SUPPRESS: Verify destroyed sprite handling.
            IReadOnlyList<SpriteAnimationKeyframe> frames =
                SpriteAnimationHelpers.SpriteKeyframesOf(clip);
            Assert.That(frames.Count, Is.EqualTo(3));
            Assert.IsTrue(frames[1].Sprite == null);
            Assert.That(SpriteAnimationHelpers.LastMovingFrame(clip, 0f), Is.EqualTo(-1));
        }

        [Test]
        public void LargeClipKeepsTheFinalMovingKeyframeIndex()
        {
            const int count = 10001;
            AnimationClip clip = Track(new AnimationClip());
            Sprite first = MakeSprite(0f, 0f);
            Sprite second = MakeSprite(0f, -1f);
            (float, Sprite)[] frames = new (float, Sprite)[count];
            for (int index = 0; index < frames.Length; index++)
            {
                frames[index] = (index * 0.125f, index < count - 1 ? first : second);
            }
            SetFrames(clip, string.Empty, frames);
            Assert.That(SpriteAnimationHelpers.LastMovingFrame(clip, 0.5f), Is.EqualTo(count - 1));
        }

        private Sprite MakeSprite(float left, float bottom, float size = 1f)
        {
            Texture2D texture = Track(new Texture2D(4, 4));
            return Track(
                Sprite.Create(
                    texture,
                    new Rect(0f, 0f, 4f, 4f),
                    new Vector2(-left / size, -bottom / size),
                    4f / size,
                    0,
                    SpriteMeshType.FullRect
                )
            );
        }

        private static void SetFrames(
            AnimationClip clip,
            string path,
            params (float time, Sprite sprite)[] frames
        )
        {
            ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[frames.Length];
            for (int index = 0; index < frames.Length; index++)
            {
                keys[index] = new ObjectReferenceKeyframe
                {
                    time = frames[index].time,
                    value = frames[index].sprite,
                };
            }
            AnimationUtility.SetObjectReferenceCurve(
                clip,
                EditorCurveBinding.PPtrCurve(
                    path,
                    typeof(SpriteRenderer),
                    UnityExtensions.SpriteBindingProperty
                ),
                keys
            );
        }
    }
#endif
}
