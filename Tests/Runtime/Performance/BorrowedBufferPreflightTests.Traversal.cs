// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Extension;

    public sealed partial class BorrowedBufferPreflightTests
    {
        private const int TraversalAllocationCalls = 128;
        private static TraversalTiming[] _traversalTimings;
        private static TraversalAllocation[] _traversalAllocations;
        private static long _traversalSink;

        private static TraversalCase[] CreateTraversalCases()
        {
            TraversalCase[] cases = new TraversalCase[12];
            int target = 0;
            foreach (int size in new[] { 1, 16, 4096 })
            {
                int[] values = new int[size];
                Dictionary<int, int> dictionary = new Dictionary<int, int>(size);
                for (int index = 0; index < size; ++index)
                {
                    values[index] = index;
                    dictionary.Add(index, index);
                }
                long expected = (long)size * (size - 1) / 2;
                cases[target++] = new TraversalCase("array", size, values, null, expected);
                cases[target++] = new TraversalCase(
                    "list",
                    size,
                    new List<int>(values),
                    null,
                    expected
                );
                cases[target++] = new TraversalCase(
                    "hash-set",
                    size,
                    new HashSet<int>(values),
                    null,
                    expected
                );
                cases[target++] = new TraversalCase(
                    "dictionary",
                    size,
                    null,
                    dictionary,
                    expected * 2
                );
            }
            return cases;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long Traverse(in TraversalCase subject, bool valueView)
        {
            long sum = 0;
            if (subject.Pairs != null)
            {
                if (valueView)
                {
                    foreach (
                        KeyValuePair<int, int> pair in subject.Pairs.AsValueDictionaryEnumerable()
                    )
                    {
                        sum += (long)pair.Key + pair.Value;
                    }
                }
                else
                {
                    foreach (KeyValuePair<int, int> pair in subject.Pairs)
                    {
                        sum += (long)pair.Key + pair.Value;
                    }
                }
            }
            else if (valueView)
            {
                foreach (int value in subject.Values.AsValueEnumerable())
                {
                    sum += value;
                }
            }
            else
            {
                foreach (int value in subject.Values)
                {
                    sum += value;
                }
            }
            return sum;
        }

        private static long TimeTraversal(in TraversalCase subject, bool valueView, int calls)
        {
            long checksum = 0;
            long start = Stopwatch.GetTimestamp();
            for (int index = 0; index < calls; ++index)
            {
                checksum += Traverse(in subject, valueView);
            }
            long elapsed = Stopwatch.GetTimestamp() - start;
            _traversalSink = checksum;
            Assert.That(checksum, Is.EqualTo(subject.Expected * calls));
            Assert.That(elapsed, Is.GreaterThan(0));
            return elapsed;
        }

        private static void CaptureTraversalTimings()
        {
            TraversalCase[] cases = CreateTraversalCases();
            _traversalTimings = new TraversalTiming[cases.Length * AllocationRepetitions];
            int target = 0;
            foreach (TraversalCase subject in cases)
            {
                int calls = Math.Max(128, 131072 / subject.Size);
                for (int warmup = 0; warmup < WarmupWindows; ++warmup)
                {
                    TimeTraversal(in subject, false, calls);
                    TimeTraversal(in subject, true, calls);
                }
                for (int repetition = 0; repetition < AllocationRepetitions; ++repetition)
                {
                    long reference;
                    long value;
                    if ((repetition & 1) == 0)
                    {
                        reference = TimeTraversal(in subject, false, calls);
                        value = TimeTraversal(in subject, true, calls);
                    }
                    else
                    {
                        value = TimeTraversal(in subject, true, calls);
                        reference = TimeTraversal(in subject, false, calls);
                    }
                    _traversalTimings[target++] = new TraversalTiming(
                        subject.Shape,
                        subject.Size,
                        repetition,
                        calls,
                        reference,
                        value
                    );
                }
            }
        }

        private static ulong CountTraversal(in TraversalCase subject, bool valueView)
        {
            if (!NativeAllocationProbe.Begin())
            {
                Assert.Fail("The native allocation window is already active.");
            }
            long checksum = 0;
            try
            {
                for (int index = 0; index < TraversalAllocationCalls; ++index)
                {
                    checksum += Traverse(in subject, valueView);
                }
            }
            finally
            {
                NativeAllocationProbe.End();
            }
            ulong count = NativeAllocationProbe.Count();
            _traversalSink = checksum;
            Assert.That(checksum, Is.EqualTo(subject.Expected * TraversalAllocationCalls));
            return count;
        }

        private static void CaptureTraversalAllocations()
        {
            TraversalCase[] cases = CreateTraversalCases();
            _traversalAllocations = new TraversalAllocation[cases.Length * AllocationRepetitions];
            int target = 0;
            foreach (TraversalCase subject in cases)
            {
                for (int warmup = 0; warmup < WarmupWindows; ++warmup)
                {
                    _traversalSink = Traverse(in subject, false);
                    _traversalSink = Traverse(in subject, true);
                }
                for (int repetition = 0; repetition < AllocationRepetitions; ++repetition)
                {
                    ulong reference = CountTraversal(in subject, false);
                    ulong value = CountTraversal(in subject, true);
                    _traversalAllocations[target++] = new TraversalAllocation(
                        subject.Shape,
                        subject.Size,
                        repetition,
                        reference,
                        value
                    );
                    Assert.That(
                        reference,
                        Is.EqualTo((ulong)TraversalAllocationCalls),
                        "The allocating traversal control must remain observable."
                    );
                    Assert.That(value, Is.EqualTo(0UL));
                }
            }
        }

        private static void WriteTraversalRecord(Utf8JsonWriter writer)
        {
            writer.WriteString(
                "TraversalTimingScope",
                "Whole checksum traversals before native probe initialization; shared call overhead remains included."
            );
            writer.WriteStartArray("TraversalTimingObservations");
            if (_traversalTimings != null)
            {
                foreach (TraversalTiming observation in _traversalTimings)
                {
                    writer.WriteStartObject();
                    writer.WriteString(nameof(TraversalTiming.Shape), observation.Shape);
                    writer.WriteNumber(nameof(TraversalTiming.Size), observation.Size);
                    writer.WriteNumber(nameof(TraversalTiming.Repetition), observation.Repetition);
                    writer.WriteNumber(nameof(TraversalTiming.Calls), observation.Calls);
                    writer.WriteNumber(
                        nameof(TraversalTiming.ReferenceTicks),
                        observation.ReferenceTicks
                    );
                    writer.WriteNumber(nameof(TraversalTiming.ValueTicks), observation.ValueTicks);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
            writer.WriteStartArray("TraversalAllocationObservations");
            if (_traversalAllocations != null)
            {
                foreach (TraversalAllocation observation in _traversalAllocations)
                {
                    writer.WriteStartObject();
                    writer.WriteString(nameof(TraversalAllocation.Shape), observation.Shape);
                    writer.WriteNumber(nameof(TraversalAllocation.Size), observation.Size);
                    writer.WriteNumber(
                        nameof(TraversalAllocation.Repetition),
                        observation.Repetition
                    );
                    writer.WriteNumber("Calls", TraversalAllocationCalls);
                    writer.WriteNumber(
                        nameof(TraversalAllocation.ReferenceCount),
                        observation.ReferenceCount
                    );
                    writer.WriteNumber(
                        nameof(TraversalAllocation.ValueCount),
                        observation.ValueCount
                    );
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }

        private readonly struct TraversalCase
        {
            public readonly string Shape;
            public readonly int Size;
            public readonly IEnumerable<int> Values;
            public readonly IEnumerable<KeyValuePair<int, int>> Pairs;
            public readonly long Expected;

            public TraversalCase(
                string shape,
                int size,
                IEnumerable<int> values,
                IEnumerable<KeyValuePair<int, int>> pairs,
                long expected
            )
            {
                Shape = shape;
                Size = size;
                Values = values;
                Pairs = pairs;
                Expected = expected;
            }
        }

        private readonly struct TraversalTiming
        {
            public readonly string Shape;
            public readonly int Size;
            public readonly int Repetition;
            public readonly int Calls;
            public readonly long ReferenceTicks;
            public readonly long ValueTicks;

            public TraversalTiming(
                string shape,
                int size,
                int repetition,
                int calls,
                long referenceTicks,
                long valueTicks
            )
            {
                Shape = shape;
                Size = size;
                Repetition = repetition;
                Calls = calls;
                ReferenceTicks = referenceTicks;
                ValueTicks = valueTicks;
            }
        }

        private readonly struct TraversalAllocation
        {
            public readonly string Shape;
            public readonly int Size;
            public readonly int Repetition;
            public readonly ulong ReferenceCount;
            public readonly ulong ValueCount;

            public TraversalAllocation(
                string shape,
                int size,
                int repetition,
                ulong referenceCount,
                ulong valueCount
            )
            {
                Shape = shape;
                Size = size;
                Repetition = repetition;
                ReferenceCount = referenceCount;
                ValueCount = valueCount;
            }
        }
    }
}
