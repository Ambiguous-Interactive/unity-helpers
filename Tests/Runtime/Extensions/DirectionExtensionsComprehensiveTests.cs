// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Model;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class DirectionExtensionsComprehensiveTests : CommonTestBase
    {
        private const int KnownDirectionMask = (1 << 8) - 1;

        private static readonly Direction[] OrderedDirections =
        {
            Direction.North,
            Direction.NorthEast,
            Direction.East,
            Direction.SouthEast,
            Direction.South,
            Direction.SouthWest,
            Direction.West,
            Direction.NorthWest,
        };

        private static IEnumerable<TestCaseData> WithoutAnglesCaseData()
        {
            yield return new TestCaseData(0f, Direction.North);
            yield return new TestCaseData(22.4f, Direction.North);
            yield return new TestCaseData(22.6f, Direction.NorthEast);
            yield return new TestCaseData(67.4f, Direction.NorthEast);
            yield return new TestCaseData(67.6f, Direction.East);
            yield return new TestCaseData(112.4f, Direction.East);
            yield return new TestCaseData(112.6f, Direction.SouthEast);
            yield return new TestCaseData(157.4f, Direction.SouthEast);
            yield return new TestCaseData(157.6f, Direction.South);
            yield return new TestCaseData(202.4f, Direction.South);
            yield return new TestCaseData(202.6f, Direction.SouthWest);
            yield return new TestCaseData(247.4f, Direction.SouthWest);
            yield return new TestCaseData(247.6f, Direction.West);
            yield return new TestCaseData(292.4f, Direction.West);
            yield return new TestCaseData(292.6f, Direction.NorthWest);
            yield return new TestCaseData(337.4f, Direction.NorthWest);
            yield return new TestCaseData(337.6f, Direction.North);
        }

        private static IEnumerable<TestCaseData> PreferAnglesCaseData()
        {
            yield return new TestCaseData(0f, Direction.North);
            yield return new TestCaseData(14.9f, Direction.North);
            yield return new TestCaseData(15.1f, Direction.NorthEast);
            yield return new TestCaseData(74.9f, Direction.NorthEast);
            yield return new TestCaseData(75.1f, Direction.East);
            yield return new TestCaseData(104.9f, Direction.East);
            yield return new TestCaseData(105.1f, Direction.SouthEast);
            yield return new TestCaseData(164.9f, Direction.SouthEast);
            yield return new TestCaseData(165.1f, Direction.South);
            yield return new TestCaseData(194.9f, Direction.South);
            yield return new TestCaseData(195.1f, Direction.SouthWest);
            yield return new TestCaseData(254.9f, Direction.SouthWest);
            yield return new TestCaseData(255.1f, Direction.West);
            yield return new TestCaseData(284.9f, Direction.West);
            yield return new TestCaseData(285.1f, Direction.NorthWest);
            yield return new TestCaseData(344.9f, Direction.NorthWest);
            yield return new TestCaseData(345.1f, Direction.North);
        }

        private static Vector2 CreateVector(float angleDegrees)
        {
            float radians = angleDegrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        private static IEnumerable<Direction> CreateEnumerable(params Direction[] directions)
        {
            foreach (Direction direction in directions)
            {
                yield return direction;
            }
        }

        [Test]
        public void DirectionConstantsMatchDefinedCounts()
        {
            Direction[] allDirections = Enum.GetValues(typeof(Direction))
                .Cast<Direction>()
                .Where(direction => direction != Direction.None)
                .ToArray();
            Assert.AreEqual(DirectionConstants.AllDirections, allDirections.Length);

            Direction[] cardinalDirections =
            {
                Direction.North,
                Direction.East,
                Direction.South,
                Direction.West,
            };
            Assert.AreEqual(DirectionConstants.NumDirections, cardinalDirections.Length);
        }

        [TestCase(Direction.North, Direction.South)]
        [TestCase(Direction.NorthEast, Direction.SouthWest)]
        [TestCase(Direction.East, Direction.West)]
        [TestCase(Direction.SouthEast, Direction.NorthWest)]
        [TestCase(Direction.South, Direction.North)]
        [TestCase(Direction.SouthWest, Direction.NorthEast)]
        [TestCase(Direction.West, Direction.East)]
        [TestCase(Direction.NorthWest, Direction.SouthEast)]
        [TestCase(Direction.None, Direction.None)]
        public void OppositeReturnsExpectedForEachDirection(Direction direction, Direction expected)
        {
            Assert.AreEqual(expected, direction.Opposite());
        }

        [Test]
        public void OppositeRoundTripsAllDirections()
        {
            foreach (Direction direction in Enum.GetValues(typeof(Direction)))
            {
                Assert.AreEqual(direction, direction.Opposite().Opposite());
            }
        }

        [Test]
        public void OppositeThrowsForCompositeValues()
        {
            Direction composite = Direction.North | Direction.East;
            Assert.Throws<ArgumentException>(() => composite.Opposite());
        }

        [Test]
        public void OppositeThrowsForUnknownValues()
        {
            Direction unknown = (Direction)(1 << 8);
            Assert.Throws<ArgumentException>(() => unknown.Opposite());
        }

        [TestCase(Direction.North, 0, 1)]
        [TestCase(Direction.NorthEast, 1, 1)]
        [TestCase(Direction.East, 1, 0)]
        [TestCase(Direction.SouthEast, 1, -1)]
        [TestCase(Direction.South, 0, -1)]
        [TestCase(Direction.SouthWest, -1, -1)]
        [TestCase(Direction.West, -1, 0)]
        [TestCase(Direction.NorthWest, -1, 1)]
        [TestCase(Direction.None, 0, 0)]
        public void AsVector2IntReturnsExpectedVectors(Direction direction, int x, int y)
        {
            Assert.AreEqual(new Vector2Int(x, y), direction.AsVector2Int());
        }

        [Test]
        public void AsVector2IntThrowsForCompositeValues()
        {
            Direction composite = Direction.North | Direction.East;
            Assert.Throws<ArgumentException>(() => composite.AsVector2Int());
        }

        [Test]
        public void AsVector2IntThrowsForUnknownValues()
        {
            Direction unknown = (Direction)(1 << 8);
            Assert.Throws<ArgumentException>(() => unknown.AsVector2Int());
        }

        [Test]
        public void AsVector2MatchesVector2Int()
        {
            foreach (Direction direction in Enum.GetValues(typeof(Direction)))
            {
                Vector2Int vector2Int = direction.AsVector2Int();
                Assert.AreEqual((Vector2)vector2Int, direction.AsVector2());
            }
        }

        [Test]
        public void AsDirectionReturnsNoneForZeroVector()
        {
            Assert.AreEqual(Direction.None, Vector2.zero.AsDirection());
        }

        [Test]
        public void AsDirectionVector3IgnoresZComponent()
        {
            Vector3 vector = new(1f, 0f, 5f);
            Assert.AreEqual(Direction.East, vector.AsDirection());
        }

        [TestCaseSource(nameof(WithoutAnglesCaseData))]
        public void AsDirectionWithoutAnglesUsesNearestOctant(float angle, Direction expected)
        {
            Vector2 vector = CreateVector(angle);
            Assert.AreEqual(expected, vector.AsDirection());
        }

        [TestCaseSource(nameof(PreferAnglesCaseData))]
        public void AsDirectionWithAnglePreferenceUsesCustomThresholds(
            float angle,
            Direction expected
        )
        {
            Vector2 vector = CreateVector(angle);
            Assert.AreEqual(expected, vector.AsDirection(preferAngles: true));
        }

        [Test]
        public void AsDirectionAnglePreferenceChangesClassificationNearBoundaries()
        {
            Vector2 nearEastSouth = CreateVector(105.1f);
            Assert.AreEqual(Direction.East, nearEastSouth.AsDirection());
            Assert.AreEqual(Direction.SouthEast, nearEastSouth.AsDirection(preferAngles: true));

            Vector2 nearWestNorth = CreateVector(285.1f);
            Assert.AreEqual(Direction.West, nearWestNorth.AsDirection());
            Assert.AreEqual(Direction.NorthWest, nearWestNorth.AsDirection(preferAngles: true));
        }

        [Test]
        public void SplitReturnsEachFlagInDeclaredOrder()
        {
            Direction combination = OrderedDirections.Aggregate(
                Direction.None,
                (current, direction) => current | direction
            );
            Direction[] split = combination.Split().ToArray();
            CollectionAssert.AreEqual(OrderedDirections, split);
        }

        [Test]
        public void SplitReturnsSingleDirectionWhenOnlyOneFlagIsSet()
        {
            foreach (Direction direction in OrderedDirections)
            {
                Direction[] split = direction.Split().ToArray();
                Assert.AreEqual(1, split.Length);
                Assert.AreEqual(direction, split[0]);
            }
        }

        [Test]
        public void SplitReturnsNoneWhenNoFlagsAreSet()
        {
            Direction[] split = Direction.None.Split().ToArray();
            Assert.AreEqual(1, split.Length);
            Assert.AreEqual(Direction.None, split[0]);
        }

        [Test]
        public void SplitIncludesKnownFlagsWhenUnknownBitsArePresent()
        {
            Direction unknownAugmented = Direction.North | (Direction)(1 << 8);
            Direction[] split = unknownAugmented.Split().ToArray();
            CollectionAssert.Contains(split, Direction.North);
            CollectionAssert.DoesNotContain(split, Direction.None);
        }

        [Test]
        public void SplitReturnsNoneForPureUnknownValues()
        {
            Direction unknown = (Direction)(1 << 8);
            Direction[] split = unknown.Split().ToArray();
            Assert.AreEqual(1, split.Length);
            Assert.AreEqual(Direction.None, split[0]);
        }

        [Test]
        public void SplitWithBufferReturnsEachFlagInDeclaredOrder()
        {
            Direction combination = OrderedDirections.Aggregate(
                Direction.None,
                (current, direction) => current | direction
            );
            List<Direction> buffer = new() { Direction.South };
            List<Direction> result = combination.Split(buffer);
            Assert.AreSame(buffer, result);
            CollectionAssert.AreEqual(OrderedDirections, result);
        }

        [Test]
        public void SplitWithBufferClearsExistingEntries()
        {
            List<Direction> buffer = new() { Direction.West, Direction.South };
            List<Direction> result = Direction.East.Split(buffer);
            Assert.AreSame(buffer, result);
            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(Direction.East, buffer[0]);
        }

        [Test]
        public void SplitWithBufferAddsNoneWhenNoFlagsSet()
        {
            List<Direction> buffer = new() { Direction.North };
            List<Direction> result = Direction.None.Split(buffer);
            Assert.AreSame(buffer, result);
            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(Direction.None, buffer[0]);
        }

        [Test]
        public void SplitWithBufferIncludesKnownFlagsWhenUnknownBitsArePresent()
        {
            List<Direction> buffer = new();
            Direction unknownAugmented = Direction.South | (Direction)(1 << 8);
            unknownAugmented.Split(buffer);
            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(Direction.South, buffer[0]);
            CollectionAssert.DoesNotContain(buffer, Direction.None);
        }

        [Test]
        public void SplitWithBufferReturnsNoneForPureUnknownValues()
        {
            List<Direction> buffer = new() { Direction.North };
            ((Direction)(1 << 8)).Split(buffer);
            Assert.AreEqual(1, buffer.Count);
            Assert.AreEqual(Direction.None, buffer[0]);
        }

        [Test]
        public void CombineThrowsForNullEnumerables()
        {
            IEnumerable<Direction> directions = null;
            Assert.Throws<ArgumentNullException>(() => directions.Combine());
        }

        [Test]
        public void CombineAggregatesIReadOnlyList()
        {
            IReadOnlyList<Direction> list = new List<Direction>
            {
                Direction.North,
                Direction.East,
                Direction.South,
            };
            Direction combined = list.Combine();
            Assert.IsTrue(combined.HasFlag(Direction.North));
            Assert.IsTrue(combined.HasFlag(Direction.East));
            Assert.IsTrue(combined.HasFlag(Direction.South));
        }

        [Test]
        public void CombineAggregatesHashSet()
        {
            HashSet<Direction> set = new()
            {
                Direction.SouthWest,
                Direction.West,
                Direction.NorthWest,
            };
            Direction combined = set.Combine();
            Assert.IsTrue(combined.HasFlag(Direction.SouthWest));
            Assert.IsTrue(combined.HasFlag(Direction.West));
            Assert.IsTrue(combined.HasFlag(Direction.NorthWest));
        }

        [Test]
        public void CombineAggregatesGeneralEnumerables()
        {
            IEnumerable<Direction> enumerable = CreateEnumerable(
                Direction.North,
                Direction.North,
                Direction.SouthEast
            );
            Direction combined = enumerable.Combine();
            Assert.IsTrue(combined.HasFlag(Direction.North));
            Assert.IsTrue(combined.HasFlag(Direction.SouthEast));
            Assert.IsFalse(combined.HasFlag(Direction.West));
        }

        [Test]
        public void CombineReturnsNoneForEmptySequence()
        {
            Assert.AreEqual(Direction.None, Array.Empty<Direction>().Combine());
        }

        [Test]
        public void CombinePreservesUnknownBits()
        {
            Direction combined = new[] { Direction.North, (Direction)(1 << 8) }.Combine();
            Assert.AreEqual(Direction.North | (Direction)(1 << 8), combined);
        }

        [Test]
        public void SplitAndCombineRoundTripAllKnownCombinations()
        {
            for (int value = 0; value <= KnownDirectionMask; value++)
            {
                Direction direction = (Direction)value;
                Direction recombined = direction.Split().Combine();
                Assert.AreEqual(direction, recombined);
            }
        }

        [Test]
        public void CombineOfSplitRemovesUnknownBits()
        {
            Direction original = (Direction)((1 << 8) | (int)Direction.South);
            Direction recombined = original.Split().Combine();
            Assert.AreEqual(Direction.South, recombined);
        }
    }
}
