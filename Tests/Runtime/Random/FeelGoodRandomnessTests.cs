// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Random
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Random;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class FeelGoodRandomnessTests
    {
        [Test]
        public void ExactAveragePrdRejectsInvalidTargets()
        {
            Assert.False(ExactAveragePrd.TryCreate(-0.01f, out _));
            Assert.False(ExactAveragePrd.TryCreate(1.01f, out _));
            Assert.False(ExactAveragePrd.TryCreate(float.NaN, out _));
            Assert.False(ExactAveragePrd.TryCreate(float.PositiveInfinity, out _));
            Assert.False(
                ExactAveragePrd.TryCreate(ExactAveragePrd.MinimumPositiveTargetChance * 0.5f, out _)
            );
        }

        [TestCase(0f, 0f, int.MaxValue)]
        [TestCase(1f, 1f, 1)]
        public void ExactAveragePrdHandlesExtremeTargets(
            float target,
            float expectedChance,
            int expectedGuarantee
        )
        {
            Assert.True(ExactAveragePrd.TryCreate(target, out ExactAveragePrd prd));

            Assert.AreEqual(expectedChance, prd.CurrentChance);
            Assert.AreEqual(expectedGuarantee, prd.GuaranteedAttempt);
        }

        [TestCase(0.1f)]
        [TestCase(0.25f)]
        [TestCase(0.5f)]
        public void ExactAveragePrdSolvesCoefficientForLongRunTarget(float targetChance)
        {
            Assert.True(ExactAveragePrd.TryCreate(targetChance, out ExactAveragePrd prd));

            double expectedAttempts = EstimateExpectedAttempts(prd.Coefficient);
            double solvedChance = 1d / expectedAttempts;

            Assert.That(solvedChance, Is.EqualTo(targetChance).Within(0.0001d));
            Assert.That(prd.Coefficient, Is.Positive);
            Assert.That(prd.Coefficient, Is.LessThanOrEqualTo(targetChance));
            Assert.That(prd.GuaranteedAttempt, Is.Positive);
        }

        [Test]
        public void ExactAveragePrdRaisesChanceUntilSuccessThenResets()
        {
            Assert.True(ExactAveragePrd.TryCreate(0.25f, out ExactAveragePrd prd));
            float firstChance = prd.CurrentChance;
            SequenceRandom random = new(uint.MaxValue, 0);

            Assert.False(prd.Roll(random));
            Assert.AreEqual(1, prd.FailuresSinceSuccess);
            Assert.That(prd.CurrentChance, Is.GreaterThan(firstChance));

            Assert.True(prd.Roll(random));
            Assert.AreEqual(0, prd.FailuresSinceSuccess);
            Assert.AreEqual(firstChance, prd.CurrentChance);
        }

        [Test]
        public void ExactAveragePrdRestoresFailureState()
        {
            Assert.True(ExactAveragePrd.TryCreate(0.25f, 2, out ExactAveragePrd prd));

            Assert.AreEqual(2, prd.FailuresSinceSuccess);
            Assert.AreEqual(3, prd.NextAttempt);
            Assert.False(prd.TrySetFailuresSinceSuccess(-1));
            Assert.True(prd.TrySetFailuresSinceSuccess(1));
            Assert.AreEqual(1, prd.FailuresSinceSuccess);
        }

        [Test]
        public void BadLuckProtectionRejectsInvalidConfiguration()
        {
            Assert.False(BadLuckProtection.TryCreate(-0.01f, 0.1f, 0, out _));
            Assert.False(BadLuckProtection.TryCreate(1.01f, 0.1f, 0, out _));
            Assert.False(BadLuckProtection.TryCreate(0.25f, -0.1f, 0, out _));
            Assert.False(BadLuckProtection.TryCreate(0.25f, 0.1f, -1, out _));
            Assert.False(BadLuckProtection.TryCreate(float.NaN, 0.1f, 0, out _));
        }

        [Test]
        public void BadLuckProtectionRaisesChanceAndGuaranteesSuccess()
        {
            Assert.True(
                BadLuckProtection.TryCreate(
                    baseChance: 0.1f,
                    chanceIncreasePerFailure: 0.2f,
                    guaranteedAfterFailures: 2,
                    out BadLuckProtection protection
                )
            );
            SequenceRandom random = new(uint.MaxValue, uint.MaxValue, uint.MaxValue);

            Assert.AreEqual(0.1f, protection.CurrentChance);
            Assert.False(protection.Roll(random));
            Assert.AreEqual(1, protection.FailuresSinceSuccess);
            Assert.That(protection.CurrentChance, Is.EqualTo(0.3f).Within(0.0001f));

            Assert.False(protection.Roll(random));
            Assert.AreEqual(2, protection.FailuresSinceSuccess);
            Assert.AreEqual(1f, protection.CurrentChance);

            Assert.True(protection.Roll(random));
            Assert.AreEqual(0, protection.FailuresSinceSuccess);
        }

        [Test]
        public void BadLuckProtectionRestoresFailureState()
        {
            Assert.True(
                BadLuckProtection.TryCreate(
                    baseChance: 0.1f,
                    chanceIncreasePerFailure: 0.2f,
                    guaranteedAfterFailures: 4,
                    failuresSinceSuccess: 2,
                    out BadLuckProtection protection
                )
            );

            Assert.AreEqual(2, protection.FailuresSinceSuccess);
            Assert.That(protection.CurrentChance, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.False(protection.TrySetFailuresSinceSuccess(-1));
            Assert.True(protection.TrySetFailuresSinceSuccess(3));
            Assert.AreEqual(3, protection.FailuresSinceSuccess);
        }

        [Test]
        public void WeightedShuffleBagRejectsNegativeTicketsAndEmptyDraws()
        {
            WeightedShuffleBag<string> bag = new();

            Assert.False(bag.TryAdd("invalid", -1));
            Assert.True(bag.TryAdd("zero", 0));
            Assert.AreEqual(0, bag.Count);
            Assert.False(bag.TryNext(new SequenceRandom(0), out string item));
            Assert.AreEqual(default(string), item);
        }

        [Test]
        public void WeightedShuffleBagRejectsOversizedTicketCountsBeforeMutation()
        {
            WeightedShuffleBag<string> bag = new();

            Assert.False(bag.TryAdd("too-many", WeightedShuffleBag<string>.MaxTickets + 1));
            Assert.AreEqual(0, bag.Count);
        }

        [Test]
        public void WeightedShuffleBagEmitsExactTicketCountsBeforeRepeating()
        {
            WeightedShuffleBag<string> bag = new();
            Assert.True(bag.TryAdd("common", 2));
            Assert.True(bag.TryAdd("rare", 1));
            SystemRandom random = new(123);
            Dictionary<string, int> counts = new() { ["common"] = 0, ["rare"] = 0 };

            for (int i = 0; i < 3; ++i)
            {
                Assert.True(bag.TryNext(random, out string item));
                ++counts[item];
            }

            Assert.AreEqual(2, counts["common"]);
            Assert.AreEqual(1, counts["rare"]);
            Assert.AreEqual(0, bag.RemainingCount);

            Assert.True(bag.TryNext(random, out _));
            Assert.AreEqual(2, bag.RemainingCount);
        }

        [Test]
        public void WeightedShuffleBagResetRestoresCurrentCycle()
        {
            WeightedShuffleBag<int> bag = new();
            Assert.True(bag.TryAdd(10, 1));
            Assert.True(bag.TryAdd(20, 2));

            Assert.True(bag.TryNext(new SequenceRandom(0), out _));
            Assert.AreEqual(2, bag.RemainingCount);

            bag.Reset();

            Assert.AreEqual(3, bag.RemainingCount);
        }

        [Test]
        public void WeightedShuffleBagRejectsAddDuringPartialCycle()
        {
            WeightedShuffleBag<string> bag = new();
            Assert.True(bag.TryAdd("a", 2));

            Assert.True(bag.TryNext(new SequenceRandom(0), out _));
            Assert.False(bag.TryAdd("b", 1));

            Assert.AreEqual(2, bag.Count);
            Assert.AreEqual(1, bag.RemainingCount);
        }

        [Test]
        public void WeightedShuffleBagAddAfterExhaustionStartsFullNewCycle()
        {
            WeightedShuffleBag<string> bag = new();
            Assert.True(bag.TryAdd("a", 1));
            Assert.True(bag.TryNext(new SequenceRandom(0), out _));

            Assert.AreEqual(0, bag.RemainingCount);
            Assert.True(bag.TryAdd("b", 1));

            Assert.AreEqual(2, bag.Count);
            Assert.AreEqual(2, bag.RemainingCount);
        }

        [Test]
        public void WeightedShuffleBagRestoresRemainingTicketState()
        {
            WeightedShuffleBag<string> bag = new();
            Assert.True(bag.TryAdd("common", 2));
            Assert.True(bag.TryAdd("rare", 1));
            Assert.True(bag.TryNext(new SequenceRandom(0), out _));
            List<string> remaining = new();

            Assert.True(bag.TryCopyRemainingTicketsTo(remaining));
            bag.Reset();
            Assert.AreEqual(3, bag.RemainingCount);

            Assert.True(bag.TryRestoreRemaining(remaining));
            Assert.AreEqual(2, bag.RemainingCount);
            Assert.False(bag.TryRestoreRemaining(new[] { "common", "rare", "rare" }));
        }

        [Test]
        public void WeightedShuffleBagCopyMethodsRejectReadOnlyDestinations()
        {
            WeightedShuffleBag<string> bag = new();
            Assert.True(bag.TryAdd("common", 2));
            Assert.True(bag.TryAdd("rare", 1));
            ReadOnlyCollection<string> destination = new();
            bool copiedConfigured = true;
            bool copiedRemaining = true;

            Assert.DoesNotThrow(() =>
                copiedConfigured = bag.TryCopyConfiguredTicketsTo(destination)
            );
            Assert.DoesNotThrow(() => copiedRemaining = bag.TryCopyRemainingTicketsTo(destination));
            Assert.False(copiedConfigured);
            Assert.False(copiedRemaining);
            Assert.That(destination, Is.Empty);
        }

        private static double EstimateExpectedAttempts(double coefficient)
        {
            double expectedAttempts = 0d;
            double survival = 1d;
            for (int attempt = 1; attempt < 10_000; ++attempt)
            {
                expectedAttempts += survival;
                double chance = coefficient * attempt;
                if (1d <= chance)
                {
                    return expectedAttempts;
                }

                survival *= 1d - chance;
                if (survival <= 1e-14d)
                {
                    return expectedAttempts;
                }
            }

            return expectedAttempts;
        }

        private sealed class SequenceRandom : AbstractRandom
        {
            private readonly uint[] _values;
            private int _index;

            public SequenceRandom(params uint[] values)
            {
                _values = values ?? Array.Empty<uint>();
            }

            public override RandomState InternalState => BuildState((ulong)_index);

            public override uint NextUint()
            {
                if (_values.Length == 0)
                {
                    return 0;
                }

                int index = Math.Min(_index, _values.Length - 1);
                if (_index < int.MaxValue)
                {
                    ++_index;
                }

                return _values[index];
            }

            public override IRandom Copy()
            {
                SequenceRandom copy = new(_values);
                copy._index = _index;
                return copy;
            }
        }

        private sealed class ReadOnlyCollection<T> : ICollection<T>
        {
            public int Count => 0;

            public bool IsReadOnly => true;

            public void Add(T item)
            {
                throw new NotSupportedException();
            }

            public void Clear()
            {
                throw new NotSupportedException();
            }

            public bool Contains(T item)
            {
                return false;
            }

            public void CopyTo(T[] array, int arrayIndex) { }

            public IEnumerator<T> GetEnumerator()
            {
                yield break;
            }

            public bool Remove(T item)
            {
                throw new NotSupportedException();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
