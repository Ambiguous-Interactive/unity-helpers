// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Extensions
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Pins the span overloads to the <c>IList</c> ones they share a body with.
    /// </summary>
    /// <remarks>
    /// The property that decides whether a caller with seeded, reproducible generation can adopt
    /// these is not "it shuffles" -- it is that moving a shuffle onto a stack buffer changes nothing
    /// the generator emits. So the assertions below check both halves of that: the permutation, and
    /// the generator's position afterwards. A reimplementation that produced the same permutation
    /// while consuming a different number of draws would pass the first and fail the second, and it
    /// is the second that silently rewrites every artifact generated after the shuffle.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpanExtensionTests : CommonTestBase
    {
        private const int Seed = 9_001;

        private static IEnumerable<int> LengthCases()
        {
            yield return 0;
            yield return 1;
            yield return 2;
            yield return 3;
            yield return 8;
            yield return 64;
        }

        [TestCaseSource(nameof(LengthCases))]
        public void ShuffleOverASpanMatchesTheListPathElementForElement(int length)
        {
            int[] source = Enumerable.Range(0, length).ToArray();

            int[] throughList = source.ToArray();
            IRandom listRandom = new SystemRandom(Seed);
            ((IList<int>)throughList).Shuffle(listRandom);

            int[] throughSpan = source.ToArray();
            IRandom spanRandom = new SystemRandom(Seed);
            throughSpan.AsSpan().Shuffle(spanRandom);

            Assert.That(
                throughSpan,
                Is.EqualTo(throughList),
                $"A span of {length} shuffled to a different permutation than the list path"
            );
            Assert.That(
                spanRandom.Next(),
                Is.EqualTo(listRandom.Next()),
                $"A span of {length} left the generator at a different position, so it drew a "
                    + "different number of times"
            );
        }

        /// <remarks>
        /// This vector is a compatibility contract, not a snapshot of an implementation detail.
        /// Every consumer with seeded, reproducible generation draws its content through this one
        /// body, so changing the permutation changes what all of them generate, for content already
        /// shipped. A failure here is therefore a decision to take deliberately and announce -- it is
        /// not a bug to be "fixed" by pasting the new numbers into the array below.
        /// </remarks>
        [Test]
        public void ShuffleOfSixteenElementsMatchesItsPinnedVector()
        {
            int[] values = Enumerable.Range(0, 16).ToArray();

            values.AsSpan().Shuffle(new SystemRandom(Seed));

            Assert.That(
                values,
                Is.EqualTo(new[] { 8, 14, 12, 3, 6, 0, 2, 13, 9, 1, 10, 5, 4, 11, 15, 7 }),
                $"SystemRandom({Seed}) over Range(0, 16) no longer produces the pinned permutation"
            );
        }

        /// <remarks>
        /// The array fast path and the pooled write-back path are different code in
        /// <c>IListExtensions</c> and reach the same span body, so a <see cref="List{T}"/> has to
        /// agree with a raw span as well as an array does.
        /// </remarks>
        [Test]
        public void ShuffleOverASpanMatchesAListThatTakesThePooledPath()
        {
            int[] source = Enumerable.Range(0, 64).ToArray();

            List<int> throughList = new List<int>(source);
            IRandom listRandom = new SystemRandom(Seed);
            throughList.Shuffle(listRandom);

            int[] throughSpan = source.ToArray();
            IRandom spanRandom = new SystemRandom(Seed);
            throughSpan.AsSpan().Shuffle(spanRandom);

            Assert.That(throughSpan, Is.EqualTo(throughList.ToArray()));
            Assert.That(spanRandom.Next(), Is.EqualTo(listRandom.Next()));
        }

        [Test]
        public void ShuffleOverASliceLeavesTheRestOfTheBufferAlone()
        {
            int[] buffer = Enumerable.Range(0, 16).ToArray();

            buffer.AsSpan(4, 8).Shuffle(new SystemRandom(Seed));

            Assert.That(buffer.Take(4).ToArray(), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(buffer.Skip(12).ToArray(), Is.EqualTo(new[] { 12, 13, 14, 15 }));
            Assert.That(
                buffer.Skip(4).Take(8).OrderBy(value => value).ToArray(),
                Is.EqualTo(new[] { 4, 5, 6, 7, 8, 9, 10, 11 }),
                "The slice must be a permutation of what it held"
            );
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ShuffleDrawsNothingForASpanThatCannotBeReordered(int length)
        {
            IRandom shuffled = new SystemRandom(Seed);
            IRandom untouched = new SystemRandom(Seed);

            new int[length]
                .AsSpan()
                .Shuffle(shuffled);

            Assert.That(
                shuffled.Next(),
                Is.EqualTo(untouched.Next()),
                $"A span of {length} drew from the generator"
            );
        }

        /// <remarks>
        /// This is the shape the issue is about: a shared <c>static readonly T[]</c> that must not be
        /// mutated, copied into caller storage instead of into a fresh array.
        /// </remarks>
        [Test]
        public void TryCopyShuffledMatchesCopyingThenShuffling()
        {
            int[] table = Enumerable.Range(100, 12).ToArray();

            int[] copyThenShuffle = table.ToArray();
            IRandom separateRandom = new SystemRandom(Seed);
            copyThenShuffle.AsSpan().Shuffle(separateRandom);

            Span<int> destination = stackalloc int[12];
            IRandom fusedRandom = new SystemRandom(Seed);
            bool copied = ((ReadOnlySpan<int>)table).TryCopyShuffled(destination, fusedRandom);

            Assert.IsTrue(copied);
            Assert.That(destination.ToArray(), Is.EqualTo(copyThenShuffle));
            Assert.That(fusedRandom.Next(), Is.EqualTo(separateRandom.Next()));
            Assert.That(
                table,
                Is.EqualTo(Enumerable.Range(100, 12).ToArray()),
                "The source table was mutated, which is the allocation this overload exists to avoid"
            );
        }

        [Test]
        public void TryCopyShuffledWritesOnlyTheSourceLengthOfALongerDestination()
        {
            int[] source = { 1, 2, 3 };
            int[] destination = { -1, -1, -1, -7, -8 };

            bool copied = ((ReadOnlySpan<int>)source).TryCopyShuffled(
                destination.AsSpan(),
                new SystemRandom(Seed)
            );

            Assert.IsTrue(copied);
            Assert.That(
                destination.Take(3).OrderBy(value => value).ToArray(),
                Is.EqualTo(new[] { 1, 2, 3 })
            );
            Assert.That(
                destination.Skip(3).ToArray(),
                Is.EqualTo(new[] { -7, -8 }),
                "The tail past the source length must be untouched"
            );
        }

        [Test]
        public void TryCopyShuffledRefusesAShortDestinationWithoutWritingOrDrawing()
        {
            int[] source = { 1, 2, 3, 4 };
            int[] destination = { -1, -1, -1 };
            IRandom random = new SystemRandom(Seed);
            IRandom untouched = new SystemRandom(Seed);

            bool copied = ((ReadOnlySpan<int>)source).TryCopyShuffled(destination.AsSpan(), random);

            Assert.IsFalse(copied);
            Assert.That(destination, Is.EqualTo(new[] { -1, -1, -1 }));
            Assert.That(
                random.Next(),
                Is.EqualTo(untouched.Next()),
                "A refused copy must not consume the generator"
            );
        }

        [Test]
        public void TryGetRandomElementReportsFalseAndDefaultForAnEmptySpan()
        {
            IRandom random = new SystemRandom(Seed);
            IRandom untouched = new SystemRandom(Seed);

            bool selected = ReadOnlySpan<string>.Empty.TryGetRandomElement(
                out string element,
                random
            );

            Assert.IsFalse(selected);
            Assert.IsTrue(element == null);
            Assert.That(
                random.Next(),
                Is.EqualTo(untouched.Next()),
                "An empty span must not consume the generator"
            );
        }

        [Test]
        public void TryGetRandomElementReachesEveryElement()
        {
            int[] source = { 10, 20, 30, 40 };
            HashSet<int> seen = new HashSet<int>();
            IRandom random = new SystemRandom(Seed);

            for (int draw = 0; draw < 1_000; ++draw)
            {
                bool selected = ((ReadOnlySpan<int>)source).TryGetRandomElement(
                    out int element,
                    random
                );

                Assert.IsTrue(selected);
                Assert.Contains(element, source);
                seen.Add(element);
            }

            Assert.That(seen.OrderBy(value => value).ToArray(), Is.EqualTo(source));
        }

        [Test]
        public void TryGetRandomElementMatchesTheListSiblingDrawForDraw()
        {
            int[] source = { 10, 20, 30, 40 };
            IRandom listRandom = new SystemRandom(Seed);
            IRandom spanRandom = new SystemRandom(Seed);

            for (int draw = 0; draw < 32; ++draw)
            {
                int fromList = ((IList<int>)source).GetRandomElement(listRandom);
                bool selected = ((ReadOnlySpan<int>)source).TryGetRandomElement(
                    out int fromSpan,
                    spanRandom
                );

                Assert.IsTrue(selected);
                Assert.That(fromSpan, Is.EqualTo(fromList), $"Draw {draw} disagreed");
            }
        }

        [Test]
        public void ShiftMatchesTheListSiblingForEveryAmountAndLength()
        {
            foreach (int length in LengthCases())
            {
                int[] baseline = Enumerable.Range(0, length).ToArray();
                for (int amount = -length - 2; amount <= length + 2; ++amount)
                {
                    int[] viaList = (int[])baseline.Clone();
                    ((IList<int>)viaList).Shift(amount);

                    int[] viaSpan = (int[])baseline.Clone();
                    viaSpan.AsSpan().Shift(amount);

                    Assert.That(
                        viaSpan,
                        Is.EqualTo(viaList),
                        $"length {length}, amount {amount} disagreed"
                    );
                }
            }
        }

        [Test]
        public void RotateLeftAndRotateRightAreOppositeShifts()
        {
            int[] source = { 0, 1, 2, 3, 4, 5, 6 };

            int[] left = (int[])source.Clone();
            left.AsSpan().RotateLeft(2);
            Assert.That(left, Is.EqualTo(new[] { 2, 3, 4, 5, 6, 0, 1 }));

            int[] right = (int[])source.Clone();
            right.AsSpan().RotateRight(2);
            Assert.That(right, Is.EqualTo(new[] { 5, 6, 0, 1, 2, 3, 4 }));

            right.AsSpan().RotateLeft(2);
            Assert.That(right, Is.EqualTo(source));
        }

        [Test]
        public void ShiftLeavesACovariantArrayUsableThroughTheListPath()
        {
            /*
                A string[] handed over as IList<object> is exactly the shape Span<T>'s array
                constructor refuses, so the list path must not route it through a span.
            */
            string[] values = { "a", "b", "c", "d" };
            ((IList<object>)values).Shift(1);

            Assert.That(values, Is.EqualTo(new[] { "d", "a", "b", "c" }));
        }

        [Test]
        public void TrySwapExchangesInRangeIndicesAndRefusesTheRest()
        {
            int[] values = { 1, 2, 3 };
            Span<int> span = values.AsSpan();

            Assert.IsTrue(span.TrySwap(0, 2));
            Assert.That(values, Is.EqualTo(new[] { 3, 2, 1 }));

            Assert.IsTrue(span.TrySwap(1, 1));
            Assert.That(values, Is.EqualTo(new[] { 3, 2, 1 }));

            Assert.IsFalse(span.TrySwap(-1, 0));
            Assert.IsFalse(span.TrySwap(0, 3));
            Assert.That(values, Is.EqualTo(new[] { 3, 2, 1 }), "A refused swap writes nothing.");
        }

        [Test]
        public void FillFromAFactoryMatchesTheListSibling()
        {
            int[] viaList = new int[6];
            ((IList<int>)viaList).Fill(index => index * index);

            int[] viaSpan = new int[6];
            viaSpan.AsSpan().Fill(index => index * index);

            Assert.That(viaSpan, Is.EqualTo(viaList));
        }

        [Test]
        public void FillFromANullFactoryWritesNothingRatherThanThrowing()
        {
            int[] values = { 1, 2, 3 };

            values.AsSpan().Fill(null);

            Assert.That(values, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void PredicateSearchMatchesTheListSibling()
        {
            int[] source = { 4, 7, 9, 7, 2 };
            Func<int, bool> isSeven = value => value == 7;

            Assert.That(
                ((ReadOnlySpan<int>)source).IndexOf(isSeven),
                Is.EqualTo(((IList<int>)source).IndexOf(isSeven))
            );
            Assert.That(
                ((ReadOnlySpan<int>)source).LastIndexOf(isSeven),
                Is.EqualTo(((IList<int>)source).LastIndexOf(isSeven))
            );

            Func<int, bool> never = value => value < 0;
            Assert.That(((ReadOnlySpan<int>)source).IndexOf(never), Is.EqualTo(-1));
            Assert.That(((ReadOnlySpan<int>)source).LastIndexOf(never), Is.EqualTo(-1));
        }

        [Test]
        public void PredicateSearchWithStateMatchesTheClosingOverload()
        {
            int[] source = { 4, 7, 9, 7, 2 };

            Assert.That(
                ((ReadOnlySpan<int>)source).IndexOf(7, static (value, target) => value == target),
                Is.EqualTo(1)
            );
            Assert.That(
                ((ReadOnlySpan<int>)source).LastIndexOf(
                    7,
                    static (value, target) => value == target
                ),
                Is.EqualTo(3)
            );
        }

        [Test]
        public void PredicateSearchReportsMinusOneForANullPredicate()
        {
            int[] source = { 1, 2, 3 };

            Assert.That(((ReadOnlySpan<int>)source).IndexOf((Func<int, bool>)null), Is.EqualTo(-1));
            Assert.That(
                ((ReadOnlySpan<int>)source).LastIndexOf((Func<int, bool>)null),
                Is.EqualTo(-1)
            );
            Assert.That(
                ((ReadOnlySpan<int>)source).IndexOf(0, (Func<int, int, bool>)null),
                Is.EqualTo(-1)
            );
            Assert.That(
                ((ReadOnlySpan<int>)source).LastIndexOf(0, (Func<int, int, bool>)null),
                Is.EqualTo(-1)
            );
        }

        [Test]
        public void TryFindAllMatchesTheListSiblingWhenEverythingFits()
        {
            int[] source = { 1, 2, 3, 4, 5, 6 };
            List<int> viaList = ((IList<int>)source).FindAll(value => value % 2 == 0);

            Span<int> destination = new int[source.Length];
            bool complete = ((ReadOnlySpan<int>)source).TryFindAll(
                destination,
                2,
                static (value, divisor) => value % divisor == 0,
                out int written
            );

            Assert.IsTrue(complete);
            Assert.That(written, Is.EqualTo(viaList.Count));
            Assert.That(destination.Slice(0, written).ToArray(), Is.EqualTo(viaList.ToArray()));
        }

        [Test]
        public void TryFindAllReportsTruncationAndKeepsWhatFit()
        {
            int[] source = { 2, 4, 6, 8 };
            Span<int> destination = new int[2];

            bool complete = ((ReadOnlySpan<int>)source).TryFindAll(
                destination,
                2,
                static (value, divisor) => value % divisor == 0,
                out int written
            );

            Assert.IsFalse(complete);
            Assert.That(written, Is.EqualTo(2));
            Assert.That(destination.ToArray(), Is.EqualTo(new[] { 2, 4 }));
        }

        [Test]
        public void TryPartitionMatchesTheListSibling()
        {
            int[] source = { 1, 2, 3, 4, 5 };
            (List<int> matching, List<int> notMatching) = ((IList<int>)source).Partition(value =>
                value % 2 == 0
            );

            Span<int> even = new int[source.Length];
            Span<int> odd = new int[source.Length];
            bool split = ((ReadOnlySpan<int>)source).TryPartition(
                even,
                odd,
                2,
                static (value, divisor) => value % divisor == 0,
                out int evenCount,
                out int oddCount
            );

            Assert.IsTrue(split);
            Assert.That(even.Slice(0, evenCount).ToArray(), Is.EqualTo(matching.ToArray()));
            Assert.That(odd.Slice(0, oddCount).ToArray(), Is.EqualTo(notMatching.ToArray()));
        }

        [Test]
        public void TryPartitionRefusesADestinationShorterThanTheSourceWithoutWriting()
        {
            int[] source = { 1, 2, 3, 4 };
            int[] evenStorage = new int[source.Length - 1];
            int[] oddStorage = new int[source.Length];

            bool split = ((ReadOnlySpan<int>)source).TryPartition(
                evenStorage.AsSpan(),
                oddStorage.AsSpan(),
                2,
                static (value, divisor) => value % divisor == 0,
                out int evenCount,
                out int oddCount
            );

            Assert.IsFalse(split);
            Assert.That(evenCount, Is.EqualTo(0));
            Assert.That(oddCount, Is.EqualTo(0));
            Assert.That(evenStorage, Is.EqualTo(new[] { 0, 0, 0 }));
            Assert.That(oddStorage, Is.EqualTo(new[] { 0, 0, 0, 0 }));
        }
    }
}
