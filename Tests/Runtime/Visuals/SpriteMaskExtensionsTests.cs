// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Visuals
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [Category("Fast")]
    public sealed class SpriteMaskExtensionsTests : CommonTestBase
    {
        private static int UnusedLayerId()
        {
            int candidate = int.MinValue;
            while (SortingLayer.IsValid(candidate))
            {
                ++candidate;
            }
            return candidate;
        }

        [Test]
        public void MissingAndDestroyedMasksAreSafe()
        {
            SpriteMask mask = null;
            mask.RangeTo(0);
            mask.RangeToItsOwnLayer();
            Assert.That(mask.Reaches(0), Is.False);
            GameObject owner = Track(
                new GameObject(nameof(SpriteMaskExtensionsTests), typeof(SpriteMask))
            );
            mask = owner.GetComponent<SpriteMask>();
            DestroyTrackedObjects();
            mask.RangeTo(0);
            mask.RangeToItsOwnLayer();
            Assert.That(mask.Reaches(0), Is.False);
        }

        [Test]
        public void RangeToUsesEveryOrderWithoutChangingRendererLayerOrSprite()
        {
            SpriteMask mask = CreateMask();
            int ownLayer = mask.sortingLayerID;
            foreach (SortingLayer layer in SortingLayer.layers)
            {
                mask.RangeTo(layer.id);
                Assert.That(mask.isCustomRangeActive, Is.True);
                Assert.That(mask.frontSortingLayerID, Is.EqualTo(layer.id));
                Assert.That(mask.backSortingLayerID, Is.EqualTo(layer.id));
                Assert.That(mask.frontSortingOrder, Is.EqualTo(short.MaxValue));
                Assert.That(mask.backSortingOrder, Is.EqualTo(short.MinValue));
                Assert.That(mask.sortingLayerID, Is.EqualTo(ownLayer));
                Assert.That(mask.sprite == null, Is.True);
                Assert.That(mask.Reaches(layer.id), Is.True);
            }
        }

        [Test]
        public void OwnLayerIsReadAgainAfterRendererLayerChanges()
        {
            SpriteMask mask = CreateMask();
            foreach (SortingLayer layer in SortingLayer.layers)
            {
                mask.sortingLayerID = layer.id;
                mask.RangeToItsOwnLayer();
                Assert.That(mask.frontSortingLayerID, Is.EqualTo(layer.id));
                Assert.That(mask.backSortingLayerID, Is.EqualTo(layer.id));
            }
        }

        [Test]
        public void UnknownLayerDoesNotMutateAnExistingRange()
        {
            SpriteMask mask = CreateMask();
            int unknown = UnusedLayerId();
            mask.RangeToItsOwnLayer();
            int front = mask.frontSortingLayerID;
            int back = mask.backSortingLayerID;
            mask.RangeTo(unknown);
            Assert.That(mask.isCustomRangeActive, Is.True);
            Assert.That(mask.frontSortingLayerID, Is.EqualTo(front));
            Assert.That(mask.backSortingLayerID, Is.EqualTo(back));
            Assert.That(mask.Reaches(unknown), Is.False);
            Assert.That(SpriteMaskExtensions.Reaches(false, front, back, unknown), Is.False);
            Assert.That(SpriteMaskExtensions.Reaches(true, unknown, back, front), Is.False);
            Assert.That(SpriteMaskExtensions.Reaches(true, front, unknown, back), Is.False);
        }

        [Test]
        public void UnrestrictedMaskReachesEveryValidLayerRegardlessOfStoredBounds()
        {
            int unknown = UnusedLayerId();
            foreach (SortingLayer layer in SortingLayer.layers)
            {
                Assert.That(
                    SpriteMaskExtensions.Reaches(false, unknown, unknown, layer.id),
                    Is.True
                );
            }
        }

        [Test]
        public void LayerCoverageUsesOrderedValuesAndIncludesBothEndpoints()
        {
            SortingLayer[] layers = SortingLayer.layers;
            Assert.That(layers, Is.Not.Empty);
            Array.Sort(layers, (left, right) => left.value.CompareTo(right.value));
            SortingLayer[] probes =
            {
                layers[0],
                layers[layers.Length / 2],
                layers[layers.Length - 1],
            };
            foreach (SortingLayer front in probes)
            {
                foreach (SortingLayer back in probes)
                {
                    foreach (SortingLayer target in probes)
                    {
                        bool expected = back.value <= target.value && target.value <= front.value;
                        Assert.That(
                            SpriteMaskExtensions.Reaches(true, front.id, back.id, target.id),
                            Is.EqualTo(expected)
                        );
                    }
                }
            }
        }

        private SpriteMask CreateMask()
        {
            return Track(new GameObject(nameof(SpriteMaskExtensionsTests), typeof(SpriteMask)))
                .GetComponent<SpriteMask>();
        }
    }
}
