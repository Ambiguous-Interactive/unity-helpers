// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tools
{
    using NUnit.Framework;
    using UnityEditor;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class SpritePreviewCacheTests : CommonTestBase
    {
        [TestCase(false)]
        [TestCase(true)]
        public void EvictionUsesRecencyAndReleasesOnlyOwnedTextures(bool owned)
        {
            SpritePreviewCache cache = new(2);
            Texture2D first = Track(new Texture2D(2, 2));
            Texture2D second = Track(new Texture2D(2, 2));
            Texture2D third = Track(new Texture2D(2, 2));
            Sprite firstSprite = CreateSprite(first);
            Sprite secondSprite = CreateSprite(second);
            Sprite thirdSprite = CreateSprite(third);
            Assert.IsTrue(cache.Add(firstSprite, first, owned));
            Assert.IsTrue(cache.Add(secondSprite, second, owned));
            Assert.IsTrue(cache.TryGetValue(firstSprite, out Texture2D hit));
            Assert.AreSame(first, hit);
            Assert.IsTrue(cache.Add(thirdSprite, third, owned));
            Assert.IsFalse(cache.TryGetValue(secondSprite, out _));
            Assert.AreEqual(owned, second == null);
            Assert.IsTrue(first != null);
            Assert.AreEqual(2, cache.Count);
            cache.Clear();
            cache.Clear();
            Assert.AreEqual(owned, first == null);
            Assert.AreEqual(owned, third == null);
            Assert.AreEqual(0, cache.Count);
            Assert.AreEqual(0, cache.EstimatedBytes);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ByteBudgetRejectsOversizeWithoutTakingOwnership(bool owned)
        {
            Texture2D small = Track(new Texture2D(2, 2));
            Texture2D large = Track(new Texture2D(64, 64));
            Sprite first = CreateSprite(small);
            Sprite second = CreateSprite(small);
            long budget = SpritePreviewCache.EstimateBytes(
                small.width,
                small.height,
                small.format,
                small.mipmapCount
            );
            Assert.Greater(
                SpritePreviewCache.EstimateBytes(
                    large.width,
                    large.height,
                    large.format,
                    large.mipmapCount
                ),
                budget
            );
            SpritePreviewCache cache = new(128, budget);
            Assert.IsTrue(cache.Add(first, small));
            Assert.IsTrue(cache.Add(second, small));
            Assert.IsFalse(cache.TryGetValue(first, out _));
            Assert.AreEqual(1, cache.Count);
            Assert.AreEqual(budget, cache.EstimatedBytes);
            Assert.IsFalse(cache.Add(second, large, owned));
            Assert.IsTrue(large != null);
            Assert.IsTrue(cache.TryGetValue(second, out Texture2D retained));
            Assert.AreSame(small, retained);
            cache.Clear();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReplacementAndDeadEntriesKeepAccountingConsistent(bool owned)
        {
            Texture2D first = Track(new Texture2D(2, 2));
            Texture2D second = Track(new Texture2D(4, 4));
            Sprite sprite = CreateSprite(second);
            SpritePreviewCache cache = new();
            Assert.IsTrue(cache.Add(sprite, first, owned));
            Assert.IsTrue(cache.Add(sprite, first, owned));
            Assert.IsTrue(first != null);
            Assert.IsTrue(cache.Add(sprite, second, owned));
            Assert.AreEqual(owned, first == null);
            Assert.AreEqual(
                SpritePreviewCache.EstimateBytes(
                    second.width,
                    second.height,
                    second.format,
                    second.mipmapCount
                ),
                cache.EstimatedBytes
            );
            Object.DestroyImmediate(second); // UNH-SUPPRESS: externally destroyed previews are the subject
            Assert.IsFalse(cache.TryGetValue(sprite, out _));
            Assert.AreEqual(0, cache.Count);
            Assert.AreEqual(0, cache.EstimatedBytes);
            Texture2D rebuilt = Track(new Texture2D(2, 2));
            Assert.IsTrue(cache.Add(sprite, rebuilt, owned));
            Assert.IsTrue(cache.TryGetValue(sprite, out Texture2D hit));
            Assert.AreSame(rebuilt, hit);
            cache.Clear();
        }

        [TestCase(0, 1024)]
        [TestCase(-1, 1024)]
        [TestCase(2, 0)]
        [TestCase(2, -1)]
        public void DisabledBudgetsRetainNothing(int entries, long bytes)
        {
            SpritePreviewCache cache = new(entries, bytes);
            Texture2D texture = Track(new Texture2D(2, 2));
            Sprite sprite = CreateSprite(texture);
            Assert.IsFalse(cache.Add(sprite, texture, owned: true));
            Assert.IsFalse(cache.Add(null, texture));
            Assert.IsFalse(cache.Add(sprite, null));
            Assert.IsFalse(cache.TryGetValue(null, out _));
            Assert.IsTrue(texture != null);
            Assert.AreEqual(0, cache.Count);
            Assert.AreEqual(0, cache.EstimatedBytes);
        }

        [Test]
        public void EventPreviewLookupRefreshesRecencyAndFollowsChangedEventTime()
        {
            Texture2D texture = Track(new Texture2D(2, 2));
            Texture2D firstPreview = Track(new Texture2D(2, 2));
            Texture2D secondPreview = Track(new Texture2D(2, 2));
            Sprite first = CreateSprite(texture);
            Sprite second = CreateSprite(texture);
            Sprite third = CreateSprite(texture);
            AnimationClip clip = Track(new AnimationClip());
            AnimationUtility.SetObjectReferenceCurve(
                clip,
                EditorCurveBinding.PPtrCurve(
                    string.Empty,
                    typeof(SpriteRenderer),
                    WallstopStudios
                        .UnityHelpers
                        .Core
                        .Extension
                        .UnityExtensions
                        .SpriteBindingProperty
                ),
                new[]
                {
                    new ObjectReferenceKeyframe { time = 0, value = first },
                    new ObjectReferenceKeyframe { time = 1, value = second },
                }
            );
            AnimationEventEditorViewModel model = new();
            model.LoadClip(clip);
            AnimationEventItem item = new(new AnimationEvent());
            SpritePreviewCache cache = new(2);
            cache.Add(first, firstPreview);
            cache.Add(second, secondPreview);
            Assert.AreSame(
                firstPreview,
                AnimationEventSpritePreviewRenderer.SetupPreviewData(
                    item,
                    model,
                    cache,
                    out Rect? crop
                )
            );
            Assert.IsFalse(crop.HasValue);
            cache.Add(third, texture);
            Assert.IsTrue(cache.TryGetValue(first, out _));
            Assert.IsFalse(cache.TryGetValue(second, out _));
            cache.Add(second, secondPreview);
            item.animationEvent.time = 1;
            Assert.AreSame(
                secondPreview,
                AnimationEventSpritePreviewRenderer.SetupPreviewData(item, model, cache, out _)
            );
            item.animationEvent.time = -1;
            Assert.IsTrue(
                AnimationEventSpritePreviewRenderer.SetupPreviewData(item, model, cache, out _)
                    == null
            );
            Assert.IsTrue(item.sprite == null);
            cache.Clear();
        }

        [TestCase(1, false)]
        [TestCase(SpritePreviewCache.DefaultMaximumEstimatedBytes, true)]
        public void ReadableEventPreviewRetainsOnlyCopiesWithinBudget(long budget, bool retained)
        {
            Texture2D source = Track(new Texture2D(8, 8));
            Sprite sprite = Track(Sprite.Create(source, new Rect(2, 3, 4, 2), Vector2.zero));
            AnimationClip clip = Track(new AnimationClip());
            AnimationUtility.SetObjectReferenceCurve(
                clip,
                EditorCurveBinding.PPtrCurve(
                    string.Empty,
                    typeof(SpriteRenderer),
                    WallstopStudios
                        .UnityHelpers
                        .Core
                        .Extension
                        .UnityExtensions
                        .SpriteBindingProperty
                ),
                new[]
                {
                    new ObjectReferenceKeyframe { time = 0, value = sprite },
                }
            );
            AnimationEventEditorViewModel model = new();
            model.LoadClip(clip);
            SpritePreviewCache cache = new(2, budget);
            AnimationEventItem item = new(new AnimationEvent());
            Texture2D preview = AnimationEventSpritePreviewRenderer.SetupReadablePreview(
                item,
                sprite,
                cache,
                out Rect? crop
            );
            Assert.IsTrue(preview != null);
            Assert.AreEqual(retained, !crop.HasValue);
            if (retained)
            {
                Assert.AreNotSame(source, preview);
                Assert.AreEqual(4, preview.width);
                Assert.AreEqual(2, preview.height);
                Assert.AreEqual(1, cache.Count);
                Assert.IsTrue(cache.TryGetValue(sprite, out Texture2D cached));
                Assert.AreSame(preview, cached);
            }
            else
            {
                Assert.AreSame(source, preview);
                Assert.AreEqual(sprite.textureRect, crop);
                Assert.AreEqual(0, cache.Count);
                Assert.AreEqual(0, cache.EstimatedBytes);
            }
            cache.Clear();
            Assert.AreEqual(retained, preview == null);
            Assert.IsTrue(source != null);
        }

        [Test]
        public void DestroyedSpriteLookupReleasesOwnedPreview()
        {
            Texture2D texture = Track(new Texture2D(2, 2));
            Sprite sprite = CreateSprite(texture);
            SpritePreviewCache cache = new();
            Assert.IsTrue(cache.Add(sprite, texture, owned: true));
            Object.DestroyImmediate(sprite); // UNH-SUPPRESS: destroyed sprite lookup is the subject
            Assert.IsFalse(cache.TryGetValue(sprite, out _));
            Assert.IsTrue(texture == null);
            Assert.AreEqual(0, cache.Count);
            Assert.AreEqual(0, cache.EstimatedBytes);
        }

        private Sprite CreateSprite(Texture2D texture)
        {
            return Track(Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero));
        }
    }
}
