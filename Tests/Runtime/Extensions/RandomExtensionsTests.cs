// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.InteropServices;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools.Constraints;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.TestDoubles;
    using WallstopStudios.UnityHelpers.Utils;
    using Is = NUnit.Framework.Is;
    using UnityIs = UnityEngine.TestTools.Constraints.Is;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RandomExtensionsTests : CommonTestBase
    {
        private static readonly SystemRandom DeterministicRandom = new(1234);

        private static int FirstPartySkewedReference(
            IReadOnlyList<float> samples,
            int min,
            int max,
            float target
        )
        {
            float sum = 0f;
            for (int index = 0; index < samples.Count; index++)
            {
                sum += samples[index];
            }

            sum += target * 2f;
            return (int)Math.Clamp(sum / (samples.Count + 2f), min, max);
        }

        /// <summary>
        /// Every ranged draw on <see cref="IRandom"/> has a sibling that answers the low bound
        /// where the strict one raises.
        /// </summary>
        /// <remarks>
        /// Collapsing a serialized min/max pair is how a designer asks for "no spread", and these
        /// draws live in coroutines and periodic ticks, where the exception the strict overload
        /// raises ends the loop for the rest of the level (#546). Driven off the shipped surface
        /// rather than a list, so a ranged draw added to <see cref="IRandom"/> later without its
        /// sibling fails here.
        /// <para>
        /// A range is identified by its parameter <b>names</b>, not by its shape:
        /// <c>NextGaussian(double mean, double stdDev)</c> and
        /// <c>NextEnumExcept(T exception1, T exception2)</c> both take two same-typed arguments and
        /// neither is a range. The first version of this test matched on shape and reported both.
        /// </para>
        /// <para>
        /// Scope is <see cref="IRandom"/> itself. The composed ranged draws in
        /// <see cref="RandomExtensions"/> throw on a collapsed range too; the decision on #552 was to
        /// document that rather than name siblings that would collide with the existing
        /// <c>NextVector2InRange(range, origin)</c>, and
        /// <see cref="ComposedRangedDrawsRaiseOnACollapsedRange"/> holds them to it.
        /// </para>
        /// </remarks>
        [Test]
        public void EveryRangedDrawHasANonThrowingSibling()
        {
            List<string> missing = new();
            int ranged = 0;
            foreach (MethodInfo strict in typeof(IRandom).GetMethods())
            {
                ParameterInfo[] parameters = strict.GetParameters();
                if (
                    parameters.Length != 2
                    || !string.Equals(parameters[0].Name, "min", StringComparison.Ordinal)
                    || !string.Equals(parameters[1].Name, "max", StringComparison.Ordinal)
                    || parameters[0].ParameterType != parameters[1].ParameterType
                    || parameters[0].ParameterType != strict.ReturnType
                )
                {
                    continue;
                }

                ranged++;
                string expected = string.Equals(
                    strict.Name,
                    nameof(IRandom.Next),
                    System.StringComparison.Ordinal
                )
                    ? "NextIntInRange"
                    : strict.Name + "InRange";
                MethodInfo sibling = typeof(RandomExtensions).GetMethod(
                    expected,
                    new[] { typeof(IRandom), strict.ReturnType, strict.ReturnType }
                );
                if (sibling == null)
                {
                    missing.Add($"{strict.ReturnType.Name} {strict.Name} -> {expected}");
                }
            }

            Assert.AreEqual(
                8,
                ranged,
                "the sweep must find every ranged draw; a different count means the predicate stopped matching"
            );
            CollectionAssert.IsEmpty(
                missing,
                "a ranged draw without a non-throwing sibling leaves an authored min/max pair able to kill a coroutine"
            );
        }

        /// <summary>
        /// The empty range answers the low bound, for every type, however it is empty.
        /// </summary>
        [Test]
        public void AnEmptyRangeAnswersTheLowBound()
        {
            SystemRandom rng = new(7);

            Assert.AreEqual(3, rng.NextIntInRange(3, 3));
            Assert.AreEqual(5, rng.NextIntInRange(5, 2));
            Assert.AreEqual(-4, rng.NextIntInRange(-4, -4));
            Assert.AreEqual(int.MaxValue, rng.NextIntInRange(int.MaxValue, int.MinValue));

            Assert.AreEqual(3u, rng.NextUintInRange(3u, 3u));
            Assert.AreEqual(5u, rng.NextUintInRange(5u, 2u));
            Assert.AreEqual(uint.MaxValue, rng.NextUintInRange(uint.MaxValue, 0u));

            Assert.AreEqual((short)3, rng.NextShortInRange(3, 3));
            Assert.AreEqual((short)-4, rng.NextShortInRange(-4, -9));
            Assert.AreEqual(short.MaxValue, rng.NextShortInRange(short.MaxValue, short.MinValue));

            Assert.AreEqual((byte)3, rng.NextByteInRange(3, 3));
            Assert.AreEqual((byte)9, rng.NextByteInRange(9, 4));
            Assert.AreEqual(byte.MaxValue, rng.NextByteInRange(byte.MaxValue, 0));

            Assert.AreEqual(3L, rng.NextLongInRange(3L, 3L));
            Assert.AreEqual(5L, rng.NextLongInRange(5L, 2L));
            Assert.AreEqual(long.MaxValue, rng.NextLongInRange(long.MaxValue, long.MinValue));

            Assert.AreEqual(3ul, rng.NextUlongInRange(3ul, 3ul));
            Assert.AreEqual(5ul, rng.NextUlongInRange(5ul, 2ul));
            Assert.AreEqual(ulong.MaxValue, rng.NextUlongInRange(ulong.MaxValue, 0ul));

            Assert.AreEqual(3f, rng.NextFloatInRange(3f, 3f));
            Assert.AreEqual(5f, rng.NextFloatInRange(5f, 2f));
            Assert.AreEqual(0f, rng.NextFloatInRange(0f, 0f));
            Assert.AreEqual(-4f, rng.NextFloatInRange(-4f, -4f));

            Assert.AreEqual(3d, rng.NextDoubleInRange(3d, 3d));
            Assert.AreEqual(5d, rng.NextDoubleInRange(5d, 2d));
            Assert.AreEqual(-4d, rng.NextDoubleInRange(-4d, -4d));
        }

        /// <summary>
        /// Floating-point range helpers preserve the low bound when either bound is NaN.
        /// </summary>
        [Test]
        public void ANotANumberBoundAnswersTheLowBound()
        {
            SystemRandom rng = new(7);

            Assert.AreEqual(1f, rng.NextFloatInRange(1f, float.NaN));
            Assert.IsNaN(rng.NextFloatInRange(float.NaN, 5f));
            Assert.AreEqual(1d, rng.NextDoubleInRange(1d, double.NaN));
            Assert.IsNaN(rng.NextDoubleInRange(double.NaN, 5d));
        }

        /// <summary>
        /// A generator that has not been wired up yet degrades to the authored minimum.
        /// </summary>
        [Test]
        public void ANullGeneratorAnswersTheLowBound()
        {
            IRandom absent = null;

            Assert.AreEqual(2, absent.NextIntInRange(2, 9));
            Assert.AreEqual(2u, absent.NextUintInRange(2u, 9u));
            Assert.AreEqual((short)2, absent.NextShortInRange(2, 9));
            Assert.AreEqual((byte)2, absent.NextByteInRange(2, 9));
            Assert.AreEqual(2L, absent.NextLongInRange(2L, 9L));
            Assert.AreEqual(2ul, absent.NextUlongInRange(2ul, 9ul));
            Assert.AreEqual(2f, absent.NextFloatInRange(2f, 9f));
            Assert.AreEqual(2d, absent.NextDoubleInRange(2d, 9d));
        }

        /// <summary>
        /// A range that is not empty still draws from it, so the softened contract costs nothing
        /// where the strict one already worked.
        /// </summary>
        [Test]
        public void ANonEmptyRangeStillDrawsFromIt()
        {
            SystemRandom rng = new(11);

            for (int i = 0; i < 512; ++i)
            {
                Assert.That(rng.NextIntInRange(2, 5), Is.InRange(2, 4));
                Assert.That(rng.NextUintInRange(2u, 5u), Is.InRange(2u, 4u));
                Assert.That(rng.NextShortInRange(2, 5), Is.InRange((short)2, (short)4));
                Assert.That(rng.NextByteInRange(2, 5), Is.InRange((byte)2, (byte)4));
                Assert.That(rng.NextLongInRange(2L, 5L), Is.InRange(2L, 4L));
                Assert.That(rng.NextUlongInRange(2ul, 5ul), Is.InRange(2ul, 4ul));

                float sampled = rng.NextFloatInRange(2f, 5f);
                Assert.GreaterOrEqual(sampled, 2f);
                Assert.Less(sampled, 5f);

                double precise = rng.NextDoubleInRange(2d, 5d);
                Assert.GreaterOrEqual(precise, 2d);
                Assert.Less(precise, 5d);
            }
        }

        /// <remarks>
        /// The reference calculation is frozen from DoxReloaded's
        /// <c>Assets/Scripts/Generators/TargetMoveSampler.cs</c> at commit
        /// <c>91f0a620b50131f83ef1f635268dc3b044a9ec58</c>. Keeping the sampled values separate
        /// from the normalized generator sequence makes changes to either the draw transformation
        /// or the weighted-mean arithmetic visible here.
        /// </remarks>
        [Test]
        public void NextIntSkewedMatchesTheFirstPartySampler()
        {
            float[] positiveSamples = { 10f, 15f, 19f };
            float[] negativeSamples = { -17.5f, -15f, -12.5f };
            float[] oneSample = { 25f };
            EdgeCaseRandom positive = new(
                floatSequence: new[] { 0f, 0.5f, 0.9f },
                maxFloatCalls: 3
            );
            EdgeCaseRandom negative = new(
                floatSequence: new[] { 0.25f, 0.5f, 0.75f },
                maxFloatCalls: 3
            );
            EdgeCaseRandom oneDraw = new(floatFallback: 0.25f, maxFloatCalls: 1);

            Assert.AreEqual(
                FirstPartySkewedReference(positiveSamples, 10, 20, 18f),
                positive.NextIntSkewed(10, 20, 18f)
            );
            Assert.AreEqual(
                FirstPartySkewedReference(negativeSamples, -20, -10, -12f),
                negative.NextIntSkewed(-20, -10, -12f)
            );
            Assert.AreEqual(
                FirstPartySkewedReference(oneSample, 0, 100, 50f),
                oneDraw.NextIntSkewed(0, 100, 50f, 1)
            );
        }

        [TestCase(-100f, 10)]
        [TestCase(100f, 20)]
        public void NextIntSkewedClampsAfterApplyingTargetWeight(float target, int expected)
        {
            EdgeCaseRandom random = new(floatFallback: 0.5f, maxFloatCalls: 3);

            Assert.AreEqual(expected, random.NextIntSkewed(10, 20, target));
        }

        [Test]
        public void NextIntSkewedHandlesDegenerateInputsWithoutDrawing()
        {
            EdgeCaseRandom random = new(maxFloatCalls: 0);
            IRandom absent = null;

            Assert.AreEqual(10, absent.NextIntSkewed(10, 20, 15f));
            Assert.AreEqual(10, random.NextIntSkewed(10, 10, 15f));
            Assert.AreEqual(20, random.NextIntSkewed(20, 10, 15f));
            Assert.AreEqual(10, random.NextIntSkewed(10, 20, float.NaN));
            Assert.AreEqual(15, random.NextIntSkewed(10, 20, 15f, 0));
            Assert.AreEqual(10, random.NextIntSkewed(10, 20, 15f, -1));
            Assert.AreEqual(
                10,
                random.NextIntSkewed(10, 20, 15f, RandomExtensions.MaxSkewedIterations + 1)
            );
        }

        [Test]
        public void NextIntSkewedHandlesIntegerAndFloatBoundariesWithoutDrawingOutOfRange()
        {
            EdgeCaseRandom collapsed = new(maxFloatCalls: 0);
            EdgeCaseRandom upper = new(floatFallback: 0.5f, maxFloatCalls: 1);
            EdgeCaseRandom lower = new(floatFallback: 0.5f, maxFloatCalls: 1);
            EdgeCaseRandom invalidSample = new(floatFallback: float.NaN, maxFloatCalls: 1);

            Assert.AreEqual(
                16_777_216,
                collapsed.NextIntSkewed(16_777_216, 16_777_217, 16_777_217f, 1)
            );
            Assert.AreEqual(
                int.MaxValue,
                upper.NextIntSkewed(int.MinValue, int.MaxValue, float.PositiveInfinity, 1)
            );
            Assert.AreEqual(
                int.MinValue,
                lower.NextIntSkewed(int.MinValue, int.MaxValue, float.NegativeInfinity, 1)
            );
            Assert.AreEqual(
                int.MinValue,
                invalidSample.NextIntSkewed(int.MinValue, int.MaxValue, 0f, 1)
            );
        }

        [Test]
        public void NextIntSkewedAcceptsTheMaximumSupportedIterationCount()
        {
            EdgeCaseRandom random = new(
                floatFallback: 0.5f,
                maxFloatCalls: RandomExtensions.MaxSkewedIterations
            );

            Assert.AreEqual(
                15,
                random.NextIntSkewed(10, 20, 15f, RandomExtensions.MaxSkewedIterations)
            );
        }

        /// <summary>
        /// The strict overloads keep raising, because a computed range that inverts is a bug.
        /// </summary>
        [Test]
        public void TheStrictOverloadsStillRefuseAnEmptyRange()
        {
            SystemRandom rng = new(13);

            Assert.Throws<ArgumentException>(() => rng.Next(3, 3));
            Assert.Throws<ArgumentException>(() => rng.NextUint(3u, 3u));
            Assert.Throws<ArgumentException>(() => rng.NextShort(3, 3));
            Assert.Throws<ArgumentException>(() => rng.NextByte(3, 3));
            Assert.Throws<ArgumentException>(() => rng.NextLong(3L, 3L));
            Assert.Throws<ArgumentException>(() => rng.NextUlong(3ul, 3ul));
            Assert.Throws<ArgumentException>(() => rng.NextFloat(3f, 3f));
            Assert.Throws<ArgumentException>(() => rng.NextDouble(3d, 3d));
        }

        /// <summary>
        /// The message names the two values, so a designer's inspector entry reaches the console.
        /// </summary>
        [Test]
        public void TheStrictOverloadNamesBothBoundsInItsMessage()
        {
            SystemRandom rng = new(17);

            ArgumentException raised = Assert.Throws<ArgumentException>(() =>
                rng.NextFloat(7f, 4f)
            );

            StringAssert.Contains("7", raised.Message);
            StringAssert.Contains("4", raised.Message);
        }

        [Test]
        public void NextOfExceptThrowsWhenCollectionEmpty()
        {
            SystemRandom rng = new(42);
            Assert.Throws<ArgumentException>(() => rng.NextOfExcept(Array.Empty<int>(), 1));
        }

        [Test]
        public void NextOfExceptThrowsWhenAllValuesExcluded()
        {
            SystemRandom rng = new(42);
            int[] source = { 1, 2 };
            Assert.Throws<ArgumentException>(() => rng.NextOfExcept(source, 1, 2));
        }

        [Test]
        public void NextOfExceptReturnsValueNotInExceptions()
        {
            SystemRandom rng = new(42);
            int[] source = { 1, 2, 3, 4 };
            int selected = rng.NextOfExcept(source, 2, 2, 3, 3);
            CollectionAssert.DoesNotContain(new[] { 2, 3 }, selected);
        }

        [Test]
        public void NextWeightedIndexThrowsWhenWeightsDoNotSumPositive()
        {
            SystemRandom rng = new(1);
            Assert.Throws<ArgumentException>(() => rng.NextWeightedIndex(new[] { 0f, -1f }));
        }

        [Test]
        public void NextSubsetReturnsDeterministicReservoirSample()
        {
            SystemRandom rng = new(99);
            int[] source = { 10, 11, 12, 13, 14 };

            IEnumerable<int> subset = rng.NextSubset(source, 3);
            int[] result = subset.ToArray();

            Assert.AreEqual(3, result.Length);
            CollectionAssert.AllItemsAreUnique(result);
            CollectionAssert.IsSubsetOf(result, source);

            CollectionAssert.AreEqual(new[] { 10, 14, 13 }, result);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void WeightedSelectionRejectsNonfiniteWeightsBeforeDrawing(float invalidWeight)
        {
            SystemRandom rng = new(17);
            SystemRandom control = new(17);
            float[] weights = { 1f, invalidWeight, 2f };
            (string, float)[] items = { ("first", 1f), ("invalid", invalidWeight), ("last", 2f) };

            Assert.Throws<ArgumentException>(() => rng.NextWeightedIndex(weights));
            Assert.Throws<ArgumentException>(() =>
                rng.NextWeightedIndex((IReadOnlyList<float>)weights)
            );
            Assert.Throws<ArgumentException>(() => rng.NextWeighted(items));
            Assert.Throws<ArgumentException>(() => rng.NextWeighted(items.Select(item => item)));
            Assert.Throws<ArgumentException>(() =>
                rng.NextWeightedElement(new[] { 1, 2, 3 }, weights)
            );
            Assert.AreEqual(control.Next(), rng.Next());
        }

        [Test]
        public void StrictWeightedSelectionRejectsOverflowingTotalsBeforeDrawing()
        {
            SystemRandom rng = new(17);
            SystemRandom control = new(17);
            float[] weights = { float.MaxValue, float.MaxValue };
            Assert.Throws<ArgumentException>(() => rng.NextWeightedIndex(weights));
            Assert.Throws<ArgumentException>(() =>
                rng.NextWeighted(new[] { (1, weights[0]), (2, weights[1]) })
            );
            Assert.AreEqual(control.Next(), rng.Next());
            Assert.That(rng.NextWeightedIndex((IReadOnlyList<float>)weights), Is.InRange(0, 1));
        }

        [Test]
        public void WeightedListStillTreatsFiniteNegativeWeightsAsZero()
        {
            SystemRandom rng = new(17);
            IReadOnlyList<float> weights = new[] { -float.MaxValue, 0f, float.Epsilon, -1f };
            Assert.AreEqual(2, rng.NextWeightedIndex(weights));
        }

        [Test]
        public void TryNextWeightedIndexSupportsExtremeDoubleWeights()
        {
            EdgeCaseRandom random = new(doubleSequence: new[] { 0.75d }, maxDoubleCalls: 1);
            double[] weights = { double.MaxValue, double.MaxValue };

            bool selected = random.TryNextWeightedIndex(weights, out int index);

            Assert.IsTrue(selected);
            Assert.AreEqual(1, index);
            Assert.Throws<InvalidOperationException>(() => random.NextDouble());
        }

        [Test]
        public void TryNextWeightedIndexByRaceConsumesOneDrawPerSlot()
        {
            EdgeCaseRandom random = new(
                doubleSequence: new[] { 0.5d, 0.5d, 0.5d, 0.99d },
                maxDoubleCalls: 4
            );
            double[] weights = { 1d, 2d, 4d, 0d };

            bool selected = random.TryNextWeightedIndexByRace(weights, out int index);

            Assert.IsTrue(selected);
            Assert.AreEqual(2, index);
            Assert.Throws<InvalidOperationException>(() => random.NextDouble());
        }

        [Test]
        public void TryNextWeightedIndexByRaceSupportsFloatWeightsAndLowerIndexTies()
        {
            EdgeCaseRandom random = new(doubleSequence: new[] { 0d, 0d, 0.75d }, maxDoubleCalls: 3);
            float[] weights = { 1f, 4f, 0f };

            bool selected = random.TryNextWeightedIndexByRace(
                (ReadOnlySpan<float>)weights,
                out int index
            );

            Assert.IsTrue(selected);
            Assert.AreEqual(0, index);
            Assert.Throws<InvalidOperationException>(() => random.NextDouble());
        }

        [Test]
        public void TryNextWeightedRaceHandlesExtremeAndInactiveWeights()
        {
            EdgeCaseRandom random = new(
                doubleSequence: new[] { 0.5d, 0.5d, 0.5d },
                maxDoubleCalls: 3
            );
            double[] weights = { 0d, double.Epsilon, double.MaxValue };

            Assert.IsTrue(random.TryNextWeightedIndexByRace(weights, out int index));
            Assert.AreEqual(2, index);
            Assert.Throws<InvalidOperationException>(() => random.NextDouble());

            EdgeCaseRandom single = new(doubleFallback: 0.5d, maxDoubleCalls: 3);
            Assert.IsTrue(single.TryNextWeightedIndexByRace(new[] { 0d, 7d, -1d }, out index));
            Assert.AreEqual(1, index);
        }

        [Test]
        public void WeightedSpanMethodsHaveSeededGoldenSequences()
        {
            int[] expectedFloatRace = { 2, 0, 2, 2, 1, 2, 0, 1 };
            int[] expectedDoubleRace = { 2, 0, 2, 2, 1, 2, 0, 1 };
            int[] expectedCumulative = { 2, 2, 1, 1, 2, 2, 1, 2 };
            int[] expectedSubset = { 2, 1 };
            float[] floatWeights = { 1f, 3f, 7f };
            double[] doubleWeights = { 1d, 3d, 7d };
            PcgRandom floatRace = new(792);
            PcgRandom doubleRace = new(792);
            PcgRandom cumulative = new(792);
            PcgRandom subset = new(792);

            for (int draw = 0; draw < expectedFloatRace.Length; draw++)
            {
                Assert.IsTrue(
                    floatRace.TryNextWeightedIndexByRace(
                        (ReadOnlySpan<float>)floatWeights,
                        out int index
                    )
                );
                Assert.AreEqual(expectedFloatRace[draw], index);
                Assert.IsTrue(doubleRace.TryNextWeightedIndexByRace(doubleWeights, out index));
                Assert.AreEqual(expectedDoubleRace[draw], index);
                Assert.IsTrue(cumulative.TryNextWeightedIndex(doubleWeights, out index));
                Assert.AreEqual(expectedCumulative[draw], index);
            }

            int[] destination = new int[2];
            Assert.IsTrue(subset.TryNextWeightedSubsetByRace(doubleWeights, destination.AsSpan()));
            CollectionAssert.AreEqual(expectedSubset, destination);
        }

        [Test]
        public void TryNextWeightedMethodsRejectInvalidWeightsBeforeDrawing()
        {
            EdgeCaseRandom random = new(maxDoubleCalls: 0);
            int index = 99;

            Assert.IsFalse(random.TryNextWeightedIndex(ReadOnlySpan<double>.Empty, out index));
            Assert.AreEqual(0, index);
            Assert.IsFalse(random.TryNextWeightedIndex(new[] { 0d, -1d }, out index));
            Assert.AreEqual(0, index);
            Assert.IsFalse(random.TryNextWeightedIndex(new[] { 1d, double.NaN }, out index));
            Assert.AreEqual(0, index);
            Assert.IsFalse(
                random.TryNextWeightedIndexByRace(new[] { 1d, double.PositiveInfinity }, out index)
            );
            Assert.AreEqual(0, index);
            Assert.IsFalse(
                random.TryNextWeightedIndexByRace(
                    (ReadOnlySpan<float>)new[] { 1f, float.NegativeInfinity },
                    out index
                )
            );
            Assert.AreEqual(0, index);
        }

        [Test]
        public void TryNextWeightedIndexByRaceMatchesWeightedFrequencies()
        {
            const int DrawCount = 4000;
            SystemRandom random = new(792);
            double[] weights = { 1d, 3d };
            int firstCount = 0;
            bool everySelectionSucceeded = true;
            bool everyIndexWasInRange = true;
            for (int draw = 0; draw < DrawCount; draw++)
            {
                if (!random.TryNextWeightedIndexByRace(weights, out int index))
                {
                    everySelectionSucceeded = false;
                    continue;
                }

                everyIndexWasInRange &= 0 <= index && index <= 1;
                if (index == 0)
                {
                    firstCount++;
                }
            }

            double expectedFirst = DrawCount * 0.25d;
            double expectedSecond = DrawCount - expectedFirst;
            double firstDifference = firstCount - expectedFirst;
            double secondDifference = DrawCount - firstCount - expectedSecond;
            double chiSquared =
                firstDifference * firstDifference / expectedFirst
                + secondDifference * secondDifference / expectedSecond;
            Assert.IsTrue(everySelectionSucceeded);
            Assert.IsTrue(everyIndexWasInRange);
            Assert.Less(chiSquared, 10d);
        }

        [Test]
        public void TryNextWeightedIndexByRaceMovesFewerPicksWhenOneWeightChanges()
        {
            const int DrawCount = 1000;
            double[] original = { 1d, 1d, 1d, 1d, 1d, 1d, 1d, 1d };
            double[] changed = { 1d, 1d, 1d, 4d, 1d, 1d, 1d, 1d };
            SystemRandom cumulativeOriginal = new(792);
            SystemRandom cumulativeChanged = new(792);
            SystemRandom raceOriginal = new(792);
            SystemRandom raceChanged = new(792);
            int cumulativeChanges = 0;
            int raceChanges = 0;
            bool everySelectionSucceeded = true;

            for (int draw = 0; draw < DrawCount; draw++)
            {
                everySelectionSucceeded &= cumulativeOriginal.TryNextWeightedIndex(
                    original,
                    out int oldIndex
                );
                everySelectionSucceeded &= cumulativeChanged.TryNextWeightedIndex(
                    changed,
                    out int newIndex
                );
                if (oldIndex != newIndex)
                {
                    cumulativeChanges++;
                }

                everySelectionSucceeded &= raceOriginal.TryNextWeightedIndexByRace(
                    original,
                    out oldIndex
                );
                everySelectionSucceeded &= raceChanged.TryNextWeightedIndexByRace(
                    changed,
                    out newIndex
                );
                if (oldIndex != newIndex)
                {
                    raceChanges++;
                }
            }

            Assert.IsTrue(everySelectionSucceeded);
            Assert.Less(raceChanges, cumulativeChanges);
        }

        [Test]
        public void TryNextWeightedSubsetByRaceReturnsSmallestUniqueClocks()
        {
            double[] uniforms = { 0.5d, 0.5d, 0.5d, 0.99d };
            double[] weights = { 1d, 2d, 4d, 0d };
            int[] expected = { 2, 1 };
            int[] convenience = new int[2];
            int[] callerOwned = new int[2];
            double[] scratch = new double[2];
            EdgeCaseRandom convenienceRandom = new(
                doubleSequence: uniforms,
                maxDoubleCalls: uniforms.Length
            );
            EdgeCaseRandom callerOwnedRandom = new(
                doubleSequence: uniforms,
                maxDoubleCalls: uniforms.Length
            );

            Assert.IsTrue(
                convenienceRandom.TryNextWeightedSubsetByRace(weights, convenience.AsSpan())
            );
            Assert.IsTrue(
                callerOwnedRandom.TryNextWeightedSubsetByRace(
                    weights,
                    callerOwned.AsSpan(),
                    scratch.AsSpan()
                )
            );

            CollectionAssert.AreEqual(expected, convenience);
            CollectionAssert.AreEqual(expected, callerOwned);
            Assert.Throws<InvalidOperationException>(() => convenienceRandom.NextDouble());
            Assert.Throws<InvalidOperationException>(() => callerOwnedRandom.NextDouble());
        }

        [Test]
        public void TryNextWeightedSubsetByRaceSortsFullHeapByClockThenIndex()
        {
            EdgeCaseRandom ties = new(doubleFallback: 0.5d, maxDoubleCalls: 3);
            int[] all = new int[3];
            Assert.IsTrue(
                ties.TryNextWeightedSubsetByRace(
                    new[] { 1d, 1d, 1d },
                    all.AsSpan(),
                    new double[3].AsSpan()
                )
            );
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, all);

            EdgeCaseRandom rejected = new(doubleFallback: 0.5d, maxDoubleCalls: 3);
            int[] best = new int[2];
            Assert.IsTrue(
                rejected.TryNextWeightedSubsetByRace(
                    new[] { 100d, 100d, double.Epsilon },
                    best.AsSpan(),
                    new double[2].AsSpan()
                )
            );
            CollectionAssert.AreEqual(new[] { 0, 1 }, best);

            double[] uniforms = { 0.9d, 0.1d, 0.8d, 0.2d, 0.7d, 0.3d };
            double[] weights = { 1d, 8d, 2d, 16d, 3d, 7d };
            List<(double score, int index)> oracle = new(weights.Length);
            for (int index = 0; index < weights.Length; index++)
            {
                double exponential = -Math.Log(1d - uniforms[index]);
                oracle.Add((Math.Log(exponential) - Math.Log(weights[index]), index));
            }

            int[] expected = oracle
                .OrderBy(entry => entry.score)
                .ThenBy(entry => entry.index)
                .Take(3)
                .Select(entry => entry.index)
                .ToArray();
            int[] selected = new int[3];
            EdgeCaseRandom multiLevel = new(
                doubleSequence: uniforms,
                maxDoubleCalls: uniforms.Length
            );
            Assert.IsTrue(
                multiLevel.TryNextWeightedSubsetByRace(
                    weights,
                    selected.AsSpan(),
                    new double[3].AsSpan()
                )
            );
            CollectionAssert.AreEqual(expected, selected);

            EdgeCaseRandom cutoffTie = new(doubleFallback: 0.5d, maxDoubleCalls: 5);
            int[] tied = new int[3];
            Assert.IsTrue(
                cutoffTie.TryNextWeightedSubsetByRace(
                    new[] { 1d, 1d, 1d, 1d, 1d },
                    tied.AsSpan(),
                    new double[3].AsSpan()
                )
            );
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, tied);
        }

        [Test]
        public void TryNextWeightedSubsetByRaceSupportsPooledWinnerScratch()
        {
            const int WinnerCount = 1025;
            double[] weights = new double[WinnerCount];
            int[] winners = new int[WinnerCount];
            Array.Fill(weights, 1d);
            EdgeCaseRandom random = new(doubleFallback: 0.5d, maxDoubleCalls: WinnerCount);

            Assert.IsTrue(random.TryNextWeightedSubsetByRace(weights, winners.AsSpan()));
            bool ordered = true;
            for (int index = 0; index < winners.Length; index++)
            {
                ordered &= winners[index] == index;
            }
            Assert.IsTrue(ordered);
        }

        [Test]
        public void TryNextWeightedSubsetByRaceAllocatesNothingWithReusableScratch()
        {
            double[] weights = { 1d, 2d, 3d, 4d };
            int[] winners = new int[2];
            double[] scratch = new double[2];
            SystemRandom random = new(792);

            Assert.IsTrue(
                random.TryNextWeightedSubsetByRace(weights, winners.AsSpan(), scratch.AsSpan())
            );
            Assert.IsTrue(random.TryNextWeightedSubsetByRace(weights, winners.AsSpan()));
            AllocationProbe.IgnoreWhenUnmeasurable();
            Assert.That(
                () =>
                {
                    for (int iteration = 0; iteration < AllocationProbe.Iterations; iteration++)
                    {
                        if (
                            !random.TryNextWeightedSubsetByRace(
                                weights,
                                winners.AsSpan(),
                                scratch.AsSpan()
                            ) || !random.TryNextWeightedSubsetByRace(weights, winners.AsSpan())
                        )
                        {
                            throw new InvalidOperationException("weighted selection failed");
                        }
                    }
                },
                UnityIs.Not.AllocatingGCMemory()
            );
        }

        [Test]
        public void TryNextWeightedSubsetByRacePreservesDestinationOnValidationFailure()
        {
            EdgeCaseRandom random = new(maxDoubleCalls: 0);
            int[] destination = { 7, 8 };

            Assert.IsFalse(
                random.TryNextWeightedSubsetByRace(
                    new[] { 1d, 2d },
                    destination.AsSpan(),
                    new double[1].AsSpan()
                )
            );
            CollectionAssert.AreEqual(new[] { 7, 8 }, destination);

            Assert.IsFalse(
                random.TryNextWeightedSubsetByRace(
                    new[] { 1d, 0d },
                    destination.AsSpan(),
                    new double[2].AsSpan()
                )
            );
            CollectionAssert.AreEqual(new[] { 7, 8 }, destination);

            double[] overlappingWeights = { 1d, 2d, 3d };
            int[] oneWinner = { 9 };
            Assert.IsFalse(
                random.TryNextWeightedSubsetByRace(
                    overlappingWeights,
                    oneWinner.AsSpan(),
                    overlappingWeights.AsSpan()
                )
            );
            CollectionAssert.AreEqual(new[] { 9 }, oneWinner);

            byte[] sharedWeightDestination = new byte[24];
            Span<double> sharedWeights = MemoryMarshal.Cast<byte, double>(sharedWeightDestination);
            sharedWeights.Fill(1d);
            Span<int> sharedDestination = MemoryMarshal.Cast<byte, int>(
                sharedWeightDestination.AsSpan(8, sizeof(int))
            );
            sharedDestination[0] = 9;
            Assert.IsFalse(
                random.TryNextWeightedSubsetByRace(
                    sharedWeights,
                    sharedDestination,
                    new double[1].AsSpan()
                )
            );
            Assert.AreEqual(9, sharedDestination[0]);
            Assert.IsFalse(random.TryNextWeightedSubsetByRace(sharedWeights, sharedDestination));
            Assert.AreEqual(9, sharedDestination[0]);

            byte[] sharedScoreDestination = new byte[8];
            Span<double> sharedScores = MemoryMarshal.Cast<byte, double>(sharedScoreDestination);
            Span<int> scoreBackedDestination = MemoryMarshal.Cast<byte, int>(
                sharedScoreDestination.AsSpan(0, sizeof(int))
            );
            scoreBackedDestination[0] = 9;
            Assert.IsFalse(
                random.TryNextWeightedSubsetByRace(
                    new[] { 1d, 2d },
                    scoreBackedDestination,
                    sharedScores
                )
            );
            Assert.AreEqual(9, scoreBackedDestination[0]);
            Assert.IsFalse(
                random.TryNextWeightedSubsetByRace(
                    overlappingWeights,
                    oneWinner.AsSpan(),
                    overlappingWeights.AsSpan(1, 1)
                )
            );
            CollectionAssert.AreEqual(new[] { 9 }, oneWinner);
        }

        [Test]
        public void TryNextWeightedSubsetByRaceDoesNoWorkForEmptyDestination()
        {
            EdgeCaseRandom random = new(maxDoubleCalls: 0);

            Assert.IsTrue(
                random.TryNextWeightedSubsetByRace(
                    ReadOnlySpan<double>.Empty,
                    Span<int>.Empty,
                    Span<double>.Empty
                )
            );
            Assert.IsFalse(
                ((IRandom)null).TryNextWeightedSubsetByRace(
                    ReadOnlySpan<double>.Empty,
                    Span<int>.Empty,
                    Span<double>.Empty
                )
            );
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-0.01f)]
        [TestCase(1.01f)]
        public void NextBoolRejectsInvalidProbabilityBeforeDrawing(float probability)
        {
            SystemRandom rng = new(17);
            SystemRandom control = new(17);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextBool(probability));
            Assert.AreEqual(control.Next(), rng.Next());
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void SphereSurfaceSamplingReturnsCenterForNonfiniteRadius(float radius)
        {
            SystemRandom rng = new(17);
            SystemRandom control = new(17);
            Vector3 center = new(1f, 2f, 3f);
            Assert.AreEqual(center, rng.NextVector3OnSphere(radius, center));
            Assert.AreEqual(control.Next(), rng.Next());
        }

        [Test]
        public void NextWeightedIndexHandlesExtremeValues()
        {
            SystemRandom rng = new(1);
            float[] weights = { float.MaxValue / 4f, float.MaxValue / 4f, float.MaxValue / 2f };

            Assert.DoesNotThrow(() => rng.NextWeightedIndex(weights));
        }

        [Test]
        public void NextWeightedPrefersHigherWeights()
        {
            SystemRandom rng = new(2);
            (string label, float weight)[] weighted = { ("low", 1f), ("high", 4f) };

            int lowCount = 0;
            int highCount = 0;
            for (int i = 0; i < 1000; ++i)
            {
                string choice = rng.NextWeighted(weighted);
                if (string.Equals(choice, "low", System.StringComparison.Ordinal))
                {
                    lowCount++;
                }
                else
                {
                    highCount++;
                }
            }

            Assert.Greater(highCount, lowCount, "Higher weights should be selected more often.");
        }

        [Test]
        public void NextSubsetCountZeroReturnsEmpty()
        {
            SystemRandom rng = new(5);
            int[] source = { 1, 2, 3 };
            IEnumerable<int> subset = rng.NextSubset(source, 0);
            CollectionAssert.IsEmpty(subset);
        }

        [Test]
        public void NextSubsetThrowsWhenCountExceedsSource()
        {
            SystemRandom rng = new(5);
            Assert.Throws<ArgumentException>(() => rng.NextSubset(new[] { 1, 2 }, 3));
        }

        [Test]
        public void NextWeightedElementThrowsWhenLengthsMismatch()
        {
            SystemRandom rng = new(1);
            Assert.Throws<ArgumentException>(() =>
                rng.NextWeightedElement(new[] { "a", "b" }, new[] { 0.5f })
            );
        }

        [Test]
        public void NextWeightedIndexHandlesTinyWeights()
        {
            SystemRandom rng = new(1);
            float tiny = float.Epsilon;
            float[] weights = { tiny, tiny, tiny };
            Assert.DoesNotThrow(() => rng.NextWeightedIndex(weights));
        }

        [Test]
        public void NextFloatAroundRespectsVariance()
        {
            SystemRandom rng = new(3);
            float center = 5f;
            float variance = 0f;
            float sample = rng.NextFloatAround(center, variance);
            Assert.AreEqual(center, sample);

            float rangedSample = rng.NextFloatAround(2f, 0.5f);
            Assert.That(rangedSample, Is.InRange(1.5f, 2.5f));
        }

        [Test]
        public void NextIntAroundRespectsVariance()
        {
            SystemRandom rng = new(3);
            int center = 10;
            int variance = 0;
            int sample = rng.NextIntAround(center, variance);
            Assert.AreEqual(center, sample);
        }

        [Test]
        public void NextOfExceptHandlesAllButOneExcluded()
        {
            SystemRandom rng = new(10);
            int[] values = { 1, 2, 3 };
            int result = rng.NextOfExcept(values, 1, 2);
            Assert.AreEqual(3, result);
        }

        [Test]
        public void NextSubsetEqualCountReturnsCopy()
        {
            SystemRandom rng = new(77);
            int[] source = { 1, 2, 3 };
            int[] subset = rng.NextSubset(source, 3).ToArray();
            CollectionAssert.AreEqual(source, subset);
        }

        [Test]
        public void NextWeightedThrowsWhenAllWeightsZero()
        {
            SystemRandom rng = new(1);
            Assert.Throws<ArgumentException>(() => rng.NextWeightedIndex(new[] { 0f, 0f }));
        }

        [Test]
        public void NextSubsetDeferredEnumerationKeepsResults()
        {
            SystemRandom rng = new(5);
            int[] source = { 1, 2, 3, 4, 5 };
            IEnumerable<int> subset = rng.NextSubset(source, 2);

            using IEnumerator<int> enumerator = subset.GetEnumerator();
            Assert.IsTrue(enumerator.MoveNext());
            int first = enumerator.Current;
            Assert.IsTrue(enumerator.MoveNext());
            int second = enumerator.Current;
            CollectionAssert.Contains(source, first);
            CollectionAssert.Contains(source, second);
        }

        /// <summary>
        /// The source that is not an <see cref="IReadOnlyList{T}"/> takes the branch that stages
        /// into a pooled list, and the sampling iterator is deferred: it reads that staging buffer
        /// after the method returned it to the pool. Renting the same pool between the call and the
        /// enumeration is what turns the empty read into another owner's data.
        /// </summary>
        [Test]
        public void NextSubsetFromANonListSourceSurvivesTheStagingBufferGoingBackToThePool()
        {
            SystemRandom rng = new(5);
            HashSet<int> source = new() { 11, 22, 33, 44, 55 };
            IEnumerable<int> subset = rng.NextSubset(source, 3);

            using PooledResource<List<int>> intruder = Buffers<int>.List.Get(
                out List<int> stolenBuffer
            );
            stolenBuffer.AddRange(new[] { -1, -2, -3, -4, -5 });

            List<int> drawn = subset.ToList();
            Assert.AreEqual(3, drawn.Count, "the subset must have the requested size");
            foreach (int value in drawn)
            {
                Assert.IsTrue(
                    source.Contains(value),
                    $"{value} came from somewhere other than the source collection"
                );
            }
        }

        [Test]
        public void NextSubsetNegativeCountThrows()
        {
            SystemRandom rng = new(5);
            Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextSubset(new[] { 1 }, -1));
        }

        [Test]
        public void NextSubsetCountOneReturnsSingleElement()
        {
            SystemRandom rng = new(42);
            int[] subset = rng.NextSubset(new[] { 1, 2, 3 }, 1).ToArray();
            Assert.AreEqual(1, subset.Length);
            CollectionAssert.Contains(new[] { 1, 2, 3 }, subset[0]);
        }

        [Test]
        public void NextVector2NegativeAmplitudeUsesAbsoluteRange()
        {
            SystemRandom rng = new(7);
            Vector2 result = rng.NextVector2(-2f);

            Assert.That(result.x, Is.InRange(-2f, 2f));
            Assert.That(result.y, Is.InRange(-2f, 2f));

            Vector3 vector3 = rng.NextVector3(-3f);
            Assert.That(vector3.x, Is.InRange(-3f, 3f));
            Assert.That(vector3.y, Is.InRange(-3f, 3f));
            Assert.That(vector3.z, Is.InRange(-3f, 3f));
        }

        [Test]
        public void NextVector2IntNegativeAmplitudeUsesAbsoluteRange()
        {
            SystemRandom rng = new(9);
            Vector2Int result = rng.NextVector2Int(-3);

            Assert.GreaterOrEqual(result.x, -3);
            Assert.Less(result.x, 3);
            Assert.GreaterOrEqual(result.y, -3);
            Assert.Less(result.y, 3);
        }

        [Test]
        public void NextVector3IntZeroAmplitudeReturnsZeroVector()
        {
            SystemRandom rng = new(11);
            Assert.AreEqual(Vector3Int.zero, rng.NextVector3Int(0));
        }

        [Test]
        public void NextVector2InRectZeroWidthLocksXAxis()
        {
            SystemRandom rng = new(13);
            Rect rect = new(5f, 2f, 0f, 4f);

            Vector2 result = rng.NextVector2InRect(rect);

            Assert.AreEqual(rect.xMin, result.x);
            Assert.That(result.y, Is.InRange(rect.yMin, rect.yMax));
        }

        [Test]
        public void NextVector2InRectZeroAreaReturnsMinCorner()
        {
            SystemRandom rng = new(15);
            Rect rect = new(-3f, 8f, 0f, 0f);

            Vector2 result = rng.NextVector2InRect(rect);

            Assert.AreEqual(new Vector2(rect.xMin, rect.yMin), result);
        }

        [Test]
        public void NextVector3InBoundsZeroVolumeReturnsCenter()
        {
            SystemRandom rng = new(21);
            Bounds bounds = new(new Vector3(2f, 3f, 4f), Vector3.zero);

            Assert.AreEqual(bounds.center, rng.NextVector3InBounds(bounds));
        }

        [Test]
        public void NextVector3OnSphereHandlesNegativeRadius()
        {
            SystemRandom rng = new(17);
            Vector3 center = new(1.5f, -2f, 0.25f);
            float radius = -5f;

            Vector3 result = rng.NextVector3OnSphere(radius, center);

            Assert.AreEqual(Mathf.Abs(radius), Vector3.Distance(center, result), 1e-3f);
        }

        [Test]
        public void NextVector3OnSphereZeroRadiusReturnsCenter()
        {
            SystemRandom rng = new(19);
            Vector3 center = new(-1f, 0.5f, 3f);

            Assert.AreEqual(center, rng.NextVector3OnSphere(0f, center));
        }

        [Test]
        public void NextVector3InSphereZeroRadiusReturnsCenter()
        {
            SystemRandom rng = new(23);
            Vector3 center = new(2f, -3f, 4f);

            Assert.AreEqual(center, rng.NextVector3InSphere(0f, center));
        }

        [Test]
        public void NextVector2InRangeZeroRangeReturnsOrigin()
        {
            SystemRandom rng = new(25);
            Vector2 origin = new(-1.25f, 3.4f);

            Assert.AreEqual(origin, rng.NextVector2InRange(0f, origin));
        }

        [TestCase(-5f)]
        [TestCase(-0.25f)]
        public void NextVector2InRangeNegativeRangeUsesAbsolute(float inputRange)
        {
            SystemRandom rng = new(27);
            Vector2 origin = new(0.5f, -2.5f);

            Vector2 result = rng.NextVector2InRange(inputRange, origin);

            Assert.LessOrEqual(Vector2.Distance(origin, result), Mathf.Abs(inputRange));
        }

        [Test]
        public void NextVector2InRangeDefaultsToZeroOrigin()
        {
            SystemRandom rng = new(29);
            float range = 3.5f;

            Vector2 result = rng.NextVector2InRange(range);

            Assert.LessOrEqual(result.magnitude, range);
        }

        [Test]
        public void NextVector3InRangeZeroRangeReturnsOrigin()
        {
            SystemRandom rng = new(31);
            Vector3 origin = new(4f, -2f, 7f);

            Assert.AreEqual(origin, rng.NextVector3InRange(0f, origin));
        }

        [TestCase(-10f)]
        [TestCase(-0.5f)]
        public void NextVector3InRangeNegativeRangeUsesAbsolute(float inputRange)
        {
            SystemRandom rng = new(33);
            Vector3 origin = new(-1f, 2f, -3f);

            Vector3 result = rng.NextVector3InRange(inputRange, origin);

            Assert.LessOrEqual(Vector3.Distance(origin, result), Mathf.Abs(inputRange));
        }

        [Test]
        public void NextVector3InRangeDefaultsToZeroOrigin()
        {
            SystemRandom rng = new(35);
            float range = 4f;

            Vector3 result = rng.NextVector3InRange(range);

            Assert.LessOrEqual(result.magnitude, range);
        }

        /// <summary>
        /// The composed ranged draws raise on a collapsed range, and the two rect/bounds draws
        /// deliberately do not.
        /// </summary>
        /// <remarks>
        /// #552 decided to document this rather than ship a second suffix for one concept, so this
        /// is what makes the decision visible: the throw is the contract, not an oversight, and the
        /// XML docs name the scalar sibling to compose from instead.
        /// <para>
        /// The split is the point. #552 asserted that <c>NextVector2InRect</c> and
        /// <c>NextVector3InBounds</c> "are the same shape once removed" and reach
        /// <c>NextFloat(x, x)</c>; they do not -- both guard every axis first, and a zero-area rect
        /// has always answered its min corner. Pinning both halves is what stops the next reader
        /// "fixing" a guard that already exists.
        /// </para>
        /// </remarks>
        [Test]
        public void ComposedRangedDrawsRaiseOnACollapsedRange()
        {
            SystemRandom rng = new(552);

            Assert.Throws<ArgumentException>(() => rng.NextVector2Int(3, 3));
            Assert.Throws<ArgumentException>(() => rng.NextVector3Int(3, 3));
            Assert.Throws<ArgumentException>(() =>
                rng.NextVector2Int(new Vector2Int(1, 2), new Vector2Int(1, 5))
            );
            Assert.Throws<ArgumentException>(() =>
                rng.NextVector3Int(new Vector3Int(1, 2, 3), new Vector3Int(4, 2, 6))
            );
            Assert.Throws<ArgumentException>(() => rng.NextAngle(90f, 90f));

            // The documented escape hatch: the scalar siblings answer the low bound.
            Assert.AreEqual(3, rng.NextIntInRange(3, 3));
            Assert.AreEqual(90f, rng.NextFloatInRange(90f, 90f));
        }

        /// <summary>
        /// A flattened rect or a zero-volume bounds answers a point on it instead of raising.
        /// </summary>
        [Test]
        public void RectAndBoundsDrawsAnswerACollapsedRange()
        {
            SystemRandom rng = new(553);

            Vector2 onALine = rng.NextVector2InRect(new Rect(2f, 5f, 0f, 4f));
            Assert.AreEqual(2f, onALine.x);
            Assert.IsTrue(5f <= onALine.y && onALine.y < 9f);

            Vector2 onAPoint = rng.NextVector2InRect(new Rect(2f, 5f, 0f, 0f));
            Assert.AreEqual(new Vector2(2f, 5f), onAPoint);

            Bounds flat = new(new Vector3(1f, 2f, 3f), new Vector3(4f, 0f, 4f));
            Assert.AreEqual(flat.center, rng.NextVector3InBounds(flat));
        }
    }
}
