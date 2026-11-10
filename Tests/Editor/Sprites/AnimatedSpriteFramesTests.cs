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

        private static void ReadEveryBinding(AnimationClip clip, List<Sprite> destination)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            foreach (EditorCurveBinding binding in bindings)
            {
                ObjectReferenceKeyframe[] frames = AnimationUtility.GetObjectReferenceCurve(
                    clip,
                    binding
                );
                foreach (ObjectReferenceKeyframe frame in frames)
                {
                    if (frame.value is Sprite sprite && sprite != null)
                    {
                        destination.Add(sprite);
                    }
                }
            }
        }

        [Test]
        public void FilteredEnumerationObservesClipChangesAtEnumerationTime()
        {
            AnimationClip clip = Track(new AnimationClip());
            Sprite original = CreateSprite();
            Sprite replacement = CreateSprite();
            SetFrames(clip, string.Empty, typeof(SpriteRenderer), original);
            IEnumerable<Sprite> deferred = clip.GetSpritesFromClip(
                string.Empty,
                UnityExtensions.SpriteBindingProperty,
                typeof(SpriteRenderer)
            );
            SetFrames(clip, string.Empty, typeof(SpriteRenderer), replacement);
            CollectionAssert.AreEqual(new[] { replacement }, deferred);
            SetFrames(clip, string.Empty, typeof(SpriteRenderer), original);
            CollectionAssert.AreEqual(new[] { original }, deferred);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(64)]
        public void EagerAndDeferredFiltersKeepOnlyTheMatchingBinding(int unrelatedCount)
        {
            AnimationClip clip = Track(new AnimationClip());
            Sprite wanted = CreateSprite();
            Sprite unrelated = CreateSprite();
            SetFrames(clip, string.Empty, typeof(SpriteRenderer), wanted, null, wanted);
            for (int index = 0; index < unrelatedCount; ++index)
            {
                string path = "Unrelated" + index;
                SetFrames(clip, path, typeof(SpriteRenderer), unrelated);
            }
            List<Sprite> eager = new() { unrelated };
            AnimatedSpriteFrames.AddFramesOf(clip, string.Empty, eager);
            CollectionAssert.AreEqual(new[] { unrelated, wanted, wanted }, eager);
            IEnumerable<Sprite> deferred = clip.GetSpritesFromClip(
                string.Empty,
                UnityExtensions.SpriteBindingProperty,
                typeof(SpriteRenderer)
            );
            CollectionAssert.AreEqual(new[] { wanted, wanted }, deferred);
        }

        [Test]
        [Category("Performance")]
        public void FilteredReadsAvoidAllocatingUnrelatedKeyframeArrays()
        {
            const int iterations = 8;
            const int capacity = 8192;
            object control = new();
            Action allocate = () => GC.KeepAlive(new byte[4096]);
            Action reuse = () => GC.KeepAlive(control);
            long positive = ProfilerAllocationCounter.Measure(allocate, iterations, capacity);
            if (positive < iterations)
            {
                Assert.Ignore("The allocation recorder did not observe the positive control.");
            }
            Assert.That(ProfilerAllocationCounter.Measure(reuse, iterations, capacity), Is.Zero);

            AnimationClip clip = Track(new AnimationClip());
            Sprite sprite = CreateSprite();
            Sprite[] frames = new Sprite[128];
            Array.Fill(frames, sprite);
            for (int index = 0; index < 16; ++index)
            {
                string path = "Unrelated" + index;
                SetFrames(clip, path, typeof(SpriteRenderer), frames);
            }
            SetFrames(clip, string.Empty, typeof(SpriteRenderer), sprite);
            List<Sprite> destination = new(4096);
            for (int warmup = 0; warmup < 8; ++warmup)
            {
                ReadEveryBinding(clip, destination);
                destination.Clear();
                AnimatedSpriteFrames.AddFramesOf(clip, string.Empty, destination);
                destination.Clear();
            }

            Action reference = () =>
            {
                destination.Clear();
                ReadEveryBinding(clip, destination);
            };
            Action filtered = () =>
            {
                destination.Clear();
                AnimatedSpriteFrames.AddFramesOf(clip, string.Empty, destination);
            };
            long referenceEvents = ProfilerAllocationCounter.Measure(
                reference,
                iterations,
                capacity
            );
            Assert.That(destination.Count, Is.EqualTo(2049));
            long filteredEvents = ProfilerAllocationCounter.Measure(filtered, iterations, capacity);
            CollectionAssert.AreEqual(new[] { sprite }, destination);
            Assert.That(referenceEvents, Is.GreaterThan(iterations));
            Assert.That(filteredEvents, Is.LessThan(referenceEvents));
            TestContext.WriteLine(
                $"Unfiltered events={referenceEvents}; filtered events={filteredEvents}"
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
