// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [Category("Fast")]
    public sealed class SpritePixelViewTests : CommonTestBase
    {
        private static IEnumerable<TestCaseData> ReadableViews()
        {
            foreach (
                TextureFormat format in new[]
                {
                    TextureFormat.RGBA32,
                    TextureFormat.RGB24,
                    TextureFormat.ARGB32,
                    TextureFormat.BGRA32,
                    TextureFormat.Alpha8,
                    TextureFormat.RGB565,
                    TextureFormat.RGBA4444,
                }
            )
            {
                foreach (bool mipmaps in new[] { false, true })
                {
                    foreach (bool full in new[] { false, true })
                    {
                        yield return new TestCaseData(format, mipmaps, full).SetName(
                            $"SpriteView.{format}.Mip{mipmaps}.Full{full}"
                        );
                    }
                }
            }
        }

        private static IEnumerable<TestCaseData> AllocationViews()
        {
            foreach (
                TextureFormat format in new[]
                {
                    TextureFormat.RGBA32,
                    TextureFormat.RGB24,
                    TextureFormat.ARGB32,
                    TextureFormat.BGRA32,
                    TextureFormat.Alpha8,
                    TextureFormat.RGB565,
                    TextureFormat.RGBA4444,
                }
            )
            {
                foreach (int size in new[] { 8, 32 })
                {
                    yield return new TestCaseData(format, size).SetName(
                        $"SpriteViewAllocation.{format}.Size{size}"
                    );
                }
            }
        }

        [TestCase(TextureFormat.RGBA32, 4)]
        [TestCase(TextureFormat.ARGB32, 4)]
        [TestCase(TextureFormat.BGRA32, 4)]
        [TestCase(TextureFormat.RGB24, 3)]
        [TestCase(TextureFormat.RGB565, 2)]
        [TestCase(TextureFormat.RGBA4444, 2)]
        [TestCase(TextureFormat.Alpha8, 1)]
        public void SupportedFormatReportsItsPixelSize(TextureFormat format, int expected)
        {
            Assert.IsTrue(SpritePixelRegion.TryGetBytesPerPixel(format, out int actual));
            Assert.AreEqual(expected, actual);
        }

        [TestCase(TextureFormat.DXT1)]
        [TestCase((TextureFormat)(-1))]
        public void UnsupportedFormatReturnsFalse(TextureFormat format)
        {
            Assert.IsFalse(SpritePixelRegion.TryGetBytesPerPixel(format, out _));
        }

        [TestCaseSource(nameof(ReadableViews))]
        public void ReadableViewsMatchIndependentNativePixelsAndOwnedPublicMasks(
            TextureFormat format,
            bool mipmaps,
            bool full
        )
        {
            const int size = 8;
            Texture2D texture = CreateTexture(size, format, mipmaps);
            Color32[] source = texture.GetPixels32();
            Rect rect = full ? new Rect(0, 0, size, size) : new Rect(2, 1, 4, 5);
            Sprite sprite = Track(Sprite.Create(texture, rect, new Vector2(0.25f, 0.75f), 2));

            Assert.IsTrue(
                SpriteMaskReader.TryReadPixels(
                    sprite,
                    out SpritePixelRegion view,
                    out string error,
                    borrowReadableTexture: true
                ),
                error
            );
            Assert.IsTrue(
                view.Pixels == null,
                "The synchronous texture view must not copy pixels into a managed array."
            );
            Assert.IsTrue(view.TryGetPixelCount(out int count));
            Assert.AreEqual((int)(rect.width * rect.height), count);
            Assert.AreEqual(new Vector2(rect.width * 0.25f, rect.height * 0.75f), view.Pivot);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), view.UnitsPerPixel);
            bool[] expectedMask = new bool[count];
            for (int y = 0; y < (int)rect.height; ++y)
            {
                for (int x = 0; x < (int)rect.width; ++x)
                {
                    Color32 expected = source[((int)rect.y + y) * size + (int)rect.x + x];
                    Assert.AreEqual(expected, view.GetPixel(x, y));
                    expectedMask[y * (int)rect.width + x] = 128 <= expected.a;
                }
            }
            Assert.IsTrue(
                SpriteMaskReader.TryRead(sprite, out SpriteAlphaMask mask, out error, 128),
                error
            );
            CollectionAssert.AreEqual(expectedMask, mask.Pixels);
            SpriteAlphaMask independent = new(
                expectedMask,
                (int)rect.width,
                (int)rect.height,
                view.Pivot,
                view.UnitsPerPixel
            );
            bool expectedBounds = SpriteArtBounds.TryGetLocalArtBounds(
                independent,
                out Bounds bounds,
                out _
            );
            Assert.AreEqual(
                expectedBounds,
                SpriteArtBounds.TryGetLocalArtBounds(sprite, out Bounds actualBounds, out _, 128)
            );
            Assert.AreEqual(bounds, actualBounds);
            texture.Apply(false, true);
            CollectionAssert.AreEqual(
                expectedMask,
                mask.Pixels,
                "Public masks must own their pixels after the texture view expires."
            );
        }

        [TestCaseSource(nameof(AllocationViews))]
        public void TextureViewsRemoveTheOwnedPixelArrayAllocation(TextureFormat format, int size)
        {
            const int calls = 16;
            Texture2D texture = CreateTexture(size, format, false);
            Sprite sprite = Track(
                Sprite.Create(texture, new Rect(1, 1, size - 2, size - 2), Vector2.zero)
            );
            for (int warm = 0; warm < 8; ++warm)
            {
                Assert.IsTrue(
                    SpriteMaskReader.TryReadPixels(
                        sprite,
                        out _,
                        out _,
                        borrowReadableTexture: false
                    )
                );
                Assert.IsTrue(
                    SpriteMaskReader.TryReadPixels(
                        sprite,
                        out _,
                        out _,
                        borrowReadableTexture: true
                    )
                );
            }
            int ownedSuccesses = 0;
            int viewSuccesses = 0;
            int alphaSum = 0;
            Action withCopy = () =>
            {
                if (
                    SpriteMaskReader.TryReadPixels(
                        sprite,
                        out SpritePixelRegion owned,
                        out _,
                        borrowReadableTexture: false
                    )
                )
                {
                    ++ownedSuccesses;
                    alphaSum += owned.GetPixel(0, 0).a;
                }
            };
            Action withoutCopy = () =>
            {
                if (
                    SpriteMaskReader.TryReadPixels(
                        sprite,
                        out SpritePixelRegion view,
                        out _,
                        borrowReadableTexture: true
                    )
                )
                {
                    ++viewSuccesses;
                    alphaSum += view.GetPixel(0, 0).a;
                }
            };
            const int warmup = 8;
            long ownedEvents = ProfilerAllocationCounter.Measure(withCopy, calls, 256, warmup);
            long viewEvents = ProfilerAllocationCounter.Measure(withoutCopy, calls, 256, warmup);
            Assert.AreEqual(calls + warmup, ownedSuccesses);
            Assert.AreEqual(calls + warmup, viewSuccesses);
            Assert.Less(0, alphaSum);
            if (ownedEvents == 0)
            {
                Assert.Ignore("The profiler did not observe the owned pixel-array control.");
            }
            Assert.LessOrEqual(
                calls,
                ownedEvents,
                "The owned-array positive control must observe its storage."
            );
            TestContext.WriteLine(
                $"SPRITE_VIEW_EVENTS,{format},{size},{calls},{ownedEvents},{viewEvents}"
            );
            Assert.LessOrEqual(calls, ownedEvents - viewEvents);
        }

        [Test]
        public void InkTraversalKeepsItsSnapshotWhenPoolCallbacksChangeTheTexture()
        {
            Texture2D texture = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false));
            Color32[] initial = new Color32[16];
            for (int index = 0; index < initial.Length; ++index)
            {
                initial[index] = new Color32(255, 255, 255, 255);
            }
            initial[0] = new Color32(0, 0, 0, 255);
            texture.SetPixels32(initial);
            texture.Apply();
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero, 1));
            WallstopGenericPool<Dictionary<int, int>> pool = DictionaryBuffer<int, int>.Dictionary;
            int originalMax = pool.MaxPoolSize;
            int originalMin = pool.MinRetainCount;
            int originalWarm = pool.WarmRetainCount;
            int originalLimit = pool.MaxPurgesPerOperation;
            float originalIdle = pool.IdleTimeoutSeconds;
            bool originalIntelligent = pool.UseIntelligentPurging;
            PurgeTrigger originalTriggers = pool.Triggers;
            Action<Dictionary<int, int>, PurgeReason> originalCallback = pool.OnPurge;
            int callbacks = 0;
            try
            {
                pool.Triggers = PurgeTrigger.Explicit;
                pool.MaxPoolSize = 0;
                pool.MinRetainCount = 0;
                pool.WarmRetainCount = 0;
                pool.MaxPurgesPerOperation = 0;
                pool.IdleTimeoutSeconds = 0;
                pool.UseIntelligentPurging = false;
                using (pool.Get())
                using (pool.Get())
                using (pool.Get()) { }
                Assert.LessOrEqual(3, pool.CurrentPooledCount);
                pool.MaxPoolSize = 1;
                pool.Triggers = PurgeTrigger.OnRent;
                pool.OnPurge = (_, _) =>
                {
                    ++callbacks;
                    texture.SetPixel(0, 0, Color.white);
                };

                bool found = SpriteArtBounds.TryGetCentreOfLargestDarkestInkRegion(
                    sprite,
                    0,
                    out Vector2 centre,
                    out string error
                );

                Assert.Less(
                    0,
                    callbacks,
                    "The real pool purge callback must change the texture during traversal."
                );
                Assert.AreEqual(Color.white, texture.GetPixel(0, 0));
                Assert.IsTrue(found, error);
                Assert.AreEqual(
                    new Vector2(0.5f, 0.5f),
                    centre,
                    "Traversal must use the colors read before the callback."
                );
            }
            finally
            {
                pool.OnPurge = originalCallback;
                pool.Triggers = originalTriggers;
                pool.MaxPoolSize = originalMax;
                pool.MinRetainCount = originalMin;
                pool.WarmRetainCount = originalWarm;
                pool.MaxPurgesPerOperation = originalLimit;
                pool.IdleTimeoutSeconds = originalIdle;
                pool.UseIntelligentPurging = originalIntelligent;
            }
        }

        private Texture2D CreateTexture(int size, TextureFormat format, bool mipmaps)
        {
            Texture2D texture = Track(new Texture2D(size, size, format, mipmaps));
            if (format == TextureFormat.RGB565 || format == TextureFormat.RGBA4444)
            {
                ushort[] encoded = new ushort[size * size];
                for (int index = 0; index < encoded.Length; ++index)
                {
                    encoded[index] = unchecked((ushort)(index * 997));
                }
                texture.SetPixelData(encoded, 0);
            }
            else
            {
                Color32[] pixels = new Color32[size * size];
                for (int index = 0; index < pixels.Length; ++index)
                {
                    pixels[index] = new Color32(
                        (byte)(index * 3),
                        (byte)(255 - index),
                        (byte)(index * 2),
                        (byte)(index * 4)
                    );
                }
                texture.SetPixels32(pixels);
            }
            texture.Apply();
            return texture;
        }
    }
}
