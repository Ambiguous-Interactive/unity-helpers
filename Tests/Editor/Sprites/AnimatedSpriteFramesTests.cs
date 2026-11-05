// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Sprites
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Editor.Sprites;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestTypes;

    [TestFixture]
    [Category("Fast")]
    public sealed class AnimatedSpriteFramesTests : CommonTestBase
    {
        private static void SetFrames(
            AnimationClip clip,
            string path,
            System.Type type,
            params Sprite[] frames
        )
        {
            ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[frames.Length];
            for (int index = 0; index < frames.Length; ++index)
            {
                keys[index] = new ObjectReferenceKeyframe { time = index, value = frames[index] };
            }
            AnimationUtility.SetObjectReferenceCurve(
                clip,
                EditorCurveBinding.PPtrCurve(path, type, UnityExtensions.SpriteBindingProperty),
                keys
            );
        }

        [Test]
        public void ReadOnlyDestinationsAreIgnoredWithoutLosingTheirExistingSprites()
        {
            AnimatedSpriteFrameReferences references = CreateReferences();
            references.sprite = CreateSprite();
            references.clip = Track(new AnimationClip());
            SetFrames(references.clip, string.Empty, typeof(SpriteRenderer), CreateSprite());
            SpriteRenderer renderer = references.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = references.sprite;
            Sprite sentinel = CreateSprite();
            ICollection<Sprite> destination = Array.AsReadOnly(new[] { sentinel });
            AnimatedSpriteFrames.AddFramesOf(references.clip, string.Empty, destination);
            AnimatedSpriteFrames.AddEveryFrameNamedBy(references, destination);
            AnimatedSpriteFrames.AddEverythingDrawnBy(renderer, destination);
            CollectionAssert.AreEqual(new[] { sentinel }, destination);
        }

        [TestCase("")]
        [TestCase("Child")]
        [TestCase("child")]
        [TestCase("Missing")]
        [TestCase(null)]
        public void FramesMatchExactRendererPathsAndIgnoreOtherComponentTypes(string path)
        {
            Sprite root = CreateSprite();
            Sprite child = CreateSprite();
            Sprite mask = CreateSprite();
            AnimationClip clip = Track(new AnimationClip());
            SetFrames(clip, string.Empty, typeof(SpriteRenderer), root);
            SetFrames(clip, "Child", typeof(SpriteRenderer), child);
            SetFrames(clip, string.Empty, typeof(SpriteMask), mask);
            List<Sprite> actual = new();
            AnimatedSpriteFrames.AddFramesOf(clip, path, actual);
            List<Sprite> expected = new();
            if (string.IsNullOrEmpty(path))
            {
                expected.Add(root);
            }
            if (path == null || string.Equals(path, "Child", StringComparison.Ordinal))
            {
                expected.Add(child);
            }
            CollectionAssert.AreEquivalent(expected, actual);
        }

        [Test]
        public void FramesKeepAllKeyframesAndDuplicatesButSkipNulls()
        {
            Sprite first = CreateSprite();
            Sprite second = CreateSprite();
            AnimationClip clip = Track(new AnimationClip());
            SetFrames(clip, string.Empty, typeof(SpriteRenderer), first, first, null, second);
            List<Sprite> actual = new() { second };
            AnimatedSpriteFrames.AddFramesOf(clip, string.Empty, actual);
            CollectionAssert.AreEqual(new[] { second, first, first, second }, actual);
        }

        [Test]
        public void SerializedReferencesCollectDirectSpritesAndUnfilteredClipFramesWithoutMutation()
        {
            AnimatedSpriteFrameReferences references = CreateReferences();
            Sprite assigned = CreateSprite();
            Sprite root = CreateSprite();
            Sprite child = CreateSprite();
            references.sprite = assigned;
            references.clip = Track(new AnimationClip());
            SetFrames(references.clip, string.Empty, typeof(SpriteRenderer), root);
            SetFrames(references.clip, "Child", typeof(SpriteRenderer), child);
            references.clips = new[] { references.clip, null };
            string before = EditorJsonUtility.ToJson(references);
            List<Sprite> actual = new();
            AnimatedSpriteFrames.AddEveryFrameNamedBy(references, actual);
            CollectionAssert.AreEquivalent(new[] { assigned, root, child, root, child }, actual);
            Assert.That(EditorJsonUtility.ToJson(references), Is.EqualTo(before));
        }

        [Test]
        public void RendererCollectsAssignedAndRootFramesFromItsOwnComponents()
        {
            AnimatedSpriteFrameReferences references = CreateReferences();
            SpriteRenderer renderer = references.gameObject.AddComponent<SpriteRenderer>();
            Sprite assigned = CreateSprite();
            Sprite root = CreateSprite();
            Sprite child = CreateSprite();
            Sprite unrelated = CreateSprite();
            renderer.sprite = assigned;
            references.sprite = unrelated;
            references.clip = Track(new AnimationClip());
            SetFrames(references.clip, string.Empty, typeof(SpriteRenderer), root);
            SetFrames(references.clip, "Child", typeof(SpriteRenderer), child);
            references.clips = new[] { references.clip, null };
            HashSet<Sprite> actual = new();
            AnimatedSpriteFrames.AddEverythingDrawnBy(renderer, actual);
            CollectionAssert.AreEquivalent(new[] { assigned, root }, actual);
        }

        [Test]
        public void RendererWalkDoesNotBorrowClipsFromParentsOrChildren()
        {
            AnimatedSpriteFrameReferences parent = CreateReferences();
            AnimatedSpriteFrameReferences child = CreateReferences();
            child.transform.SetParent(parent.transform);
            parent.clip = Track(new AnimationClip());
            SetFrames(parent.clip, string.Empty, typeof(SpriteRenderer), CreateSprite());
            child.clip = Track(new AnimationClip());
            SetFrames(child.clip, string.Empty, typeof(SpriteRenderer), CreateSprite());
            SpriteRenderer renderer = parent.gameObject.AddComponent<SpriteRenderer>();
            List<Sprite> actual = new();
            AnimatedSpriteFrames.AddEverythingDrawnBy(renderer, actual);
            Assert.That(actual.Count, Is.EqualTo(1));
            CollectionAssert.AreEqual(parent.clip.GetSpritesFromClip(), actual);
        }

        [Test]
        public void EmptyMissingAndDestroyedInputsLeaveExistingResultsAlone()
        {
            Sprite sentinel = CreateSprite();
            List<Sprite> actual = new() { sentinel };
            AnimationClip empty = Track(new AnimationClip());
            AnimatedSpriteFrames.AddFramesOf(null, null, actual);
            AnimatedSpriteFrames.AddFramesOf(empty, null, null);
            AnimatedSpriteFrames.AddFramesOf(empty, null, actual);
            AnimatedSpriteFrames.AddEveryFrameNamedBy(null, actual);
            AnimatedSpriteFrames.AddEveryFrameNamedBy(empty, null);
            AnimatedSpriteFrames.AddEverythingDrawnBy(null, actual);
            AnimatedSpriteFrameReferences references = CreateReferences();
            SpriteRenderer renderer = references.gameObject.AddComponent<SpriteRenderer>();
            AnimatedSpriteFrames.AddEverythingDrawnBy(renderer, null);
            UnityEngine.Object.DestroyImmediate(references.gameObject); // UNH-SUPPRESS: The result sprite must outlive this destroyed input.
            AnimatedSpriteFrames.AddEveryFrameNamedBy(references, actual);
            AnimatedSpriteFrames.AddEverythingDrawnBy(renderer, actual);
            UnityEngine.Object.DestroyImmediate(empty); // UNH-SUPPRESS: The result sprite must outlive this destroyed input.
            AnimatedSpriteFrames.AddFramesOf(empty, null, actual);
            CollectionAssert.AreEqual(new[] { sentinel }, actual);
        }

        private AnimatedSpriteFrameReferences CreateReferences()
        {
            return Track(
                    new GameObject(
                        nameof(AnimatedSpriteFramesTests),
                        typeof(AnimatedSpriteFrameReferences)
                    )
                )
                .GetComponent<AnimatedSpriteFrameReferences>();
        }

        private Sprite CreateSprite()
        {
            Texture2D texture = Track(new Texture2D(2, 2, TextureFormat.RGBA32, false));
            return Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
        }
    }
}
