// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using NUnit.Framework;

    [Category("Performance")]
    public sealed partial class BorrowedBufferPreflightTests
    {
        private static NativeAllocationObservation[] CaptureNativeAllocations(
            int seed,
            out int backend,
            out string sourceSha256,
            out string error
        )
        {
            if (!NativeAllocationProbe.TryInitialize(out backend, out sourceSha256, out error))
            {
                return Array.Empty<NativeAllocationObservation>();
            }
            _retainedBoxes = new object[LargerBoxCount];
            _preboxedValue = seed;
            NativeAllocationObservation[] observations = new NativeAllocationObservation[
                WarmupWindows + AllocationRepetitions * 4
            ];
            observations[0] = MeasureNativeBoxes(LargerBoxCount, seed, false);
            observations[1] = MeasureNativeBoxes(LargerBoxCount, seed, true);
            observations[2] = MeasureNativeBoxes(0, seed, false);
            for (int repetition = 0; repetition < AllocationRepetitions; ++repetition)
            {
                int offset = WarmupWindows + repetition * 4;
                observations[offset] = MeasureNativeBoxes(0, seed, false);
                observations[offset + 1] = MeasureNativeBoxes(LargerBoxCount, seed, true);
                observations[offset + 2] = MeasureNativeBoxes(SmallerBoxCount, seed, false);
                observations[offset + 3] = MeasureNativeBoxes(LargerBoxCount, seed, false);
            }
            return observations;
        }

        private static bool NativeAllocationControlsPass(NativeAllocationObservation[] observations)
        {
            if (observations.Length != WarmupWindows + AllocationRepetitions * 4)
            {
                return false;
            }
            for (int index = 0; index < observations.Length; ++index)
            {
                NativeAllocationObservation observation = observations[index];
                int kind = index < WarmupWindows ? index : (index - WarmupWindows) % 4;
                int requested =
                    index < WarmupWindows
                        ? kind == 2
                            ? 0
                            : LargerBoxCount
                        : kind == 0
                            ? 0
                            : kind == 2
                                ? SmallerBoxCount
                                : LargerBoxCount;
                bool preboxed = kind == 1;
                ulong expected = observation.Preboxed ? 0 : (ulong)observation.Requested;
                if (
                    observation.Requested != requested
                    || observation.Preboxed != preboxed
                    || !observation.Completed
                    || !observation.ValuesVerified
                    || observation.Count != expected
                )
                {
                    return false;
                }
                if (expected == 0 && observation.LogicalObjectBytes != 0)
                {
                    return false;
                }
                if (0 < expected && observation.LogicalObjectBytes < expected * sizeof(int))
                {
                    return false;
                }
            }
            for (int repetition = 0; repetition < AllocationRepetitions; ++repetition)
            {
                int offset = WarmupWindows + repetition * 4;
                NativeAllocationObservation smaller = observations[offset + 2];
                NativeAllocationObservation larger = observations[offset + 3];
                if (
                    smaller.Requested != SmallerBoxCount
                    || larger.Requested != LargerBoxCount
                    || ulong.MaxValue / 2 < smaller.LogicalObjectBytes
                    || larger.LogicalObjectBytes != smaller.LogicalObjectBytes * 2
                )
                {
                    return false;
                }
            }
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static NativeAllocationObservation MeasureNativeBoxes(
            int count,
            int seed,
            bool preboxed
        )
        {
            if (!NativeAllocationProbe.Begin())
            {
                return new NativeAllocationObservation(count, preboxed, 0, 0, false, false);
            }
            try
            {
                for (int index = 0; index < count; ++index)
                {
                    if (preboxed)
                    {
                        _retainedBoxes[index] = _preboxedValue;
                    }
                    else
                    {
                        _retainedBoxes[index] = unchecked(seed + index);
                    }
                }
            }
            finally
            {
                NativeAllocationProbe.End();
            }
            ulong observedCount = NativeAllocationProbe.Count();
            ulong logicalBytes = NativeAllocationProbe.LogicalObjectBytes();
            bool verified = true;
            for (int index = 0; index < count; ++index)
            {
                bool valueVerified = preboxed
                    ? ReferenceEquals(_retainedBoxes[index], _preboxedValue)
                    : _retainedBoxes[index] is int value && value == unchecked(seed + index);
                verified = verified && valueVerified;
            }
            GC.KeepAlive(_retainedBoxes);
            return new NativeAllocationObservation(
                count,
                preboxed,
                observedCount,
                logicalBytes,
                true,
                verified
            );
        }

        private static void WriteNativeAllocationRecord(
            Utf8JsonWriter writer,
            NativeAllocationObservation[] observations,
            bool passed,
            int backend,
            string sourceSha256,
            string error
        )
        {
            writer.WriteString("NativeAllocationCountState", passed ? PassedState : RejectedState);
            writer.WriteNumber("NativeAllocationBackend", backend);
            writer.WriteString("NativeAllocationSourceSha256", sourceSha256);
            writer.WriteString("NativeAllocationError", error);
            writer.WriteString(
                "NativeAllocationScope",
                "Same-thread managed-object allocation events. Logical object sizes exclude allocator rounding and native allocations. Worker allocation and retained memory are unverified."
            );
            writer.WriteStartArray("NativeAllocationObservations");
            for (int index = 0; index < observations.Length; ++index)
            {
                NativeAllocationObservation observation = observations[index];
                writer.WriteStartObject();
                writer.WriteNumber("ChronologicalIndex", index);
                writer.WriteBoolean("IsWarmup", index < WarmupWindows);
                writer.WriteNumber(
                    nameof(NativeAllocationObservation.Requested),
                    observation.Requested
                );
                writer.WriteBoolean(
                    nameof(NativeAllocationObservation.Preboxed),
                    observation.Preboxed
                );
                writer.WriteNumber(nameof(NativeAllocationObservation.Count), observation.Count);
                writer.WriteNumber(
                    nameof(NativeAllocationObservation.LogicalObjectBytes),
                    observation.LogicalObjectBytes
                );
                writer.WriteBoolean(
                    nameof(NativeAllocationObservation.Completed),
                    observation.Completed
                );
                writer.WriteBoolean(
                    nameof(NativeAllocationObservation.ValuesVerified),
                    observation.ValuesVerified
                );
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        [TestCase(0, true)]
        [TestCase(1, false)]
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(5, false)]
        [TestCase(6, false)]
        [TestCase(7, false)]
        [TestCase(8, false)]
        [TestCase(9, false)]
        [TestCase(10, false)]
        [TestCase(11, false)]
        [TestCase(12, false)]
        public void NativeAllocationControlsRejectIncompleteOrInertWindows(
            int variant,
            bool expected
        )
        {
            NativeAllocationObservation empty = new NativeAllocationObservation(
                0,
                false,
                0,
                0,
                true,
                true
            );
            NativeAllocationObservation preboxed = new NativeAllocationObservation(
                8192,
                true,
                0,
                0,
                true,
                true
            );
            NativeAllocationObservation smaller = new NativeAllocationObservation(
                4096,
                false,
                4096,
                81920,
                true,
                true
            );
            NativeAllocationObservation larger = new NativeAllocationObservation(
                8192,
                false,
                8192,
                163840,
                true,
                true
            );
            NativeAllocationObservation[] observations =
            {
                larger,
                preboxed,
                empty,
                empty,
                preboxed,
                smaller,
                larger,
                empty,
                preboxed,
                smaller,
                larger,
                empty,
                preboxed,
                smaller,
                larger,
            };
            int target =
                variant == 2 ? 4
                : variant == 8 ? 3
                : variant == 9 ? 0
                : variant == 12 ? 1
                : 5;
            NativeAllocationObservation original = observations[target];
            if (variant == 1)
            {
                Array.Resize(ref observations, observations.Length - 1);
            }
            else if (variant != 0)
            {
                observations[target] = new NativeAllocationObservation(
                    variant == 10 || variant == 12 ? 0 : original.Requested,
                    variant == 7 ? !original.Preboxed : original.Preboxed,
                    variant == 2 ? 1
                        : variant == 3 || variant == 9 ? 0
                        : original.Count,
                    variant == 4 ? 0
                        : variant == 6 || variant == 8 ? original.LogicalObjectBytes + 1
                        : original.LogicalObjectBytes,
                    variant != 5,
                    variant != 11
                );
            }
            Assert.That(NativeAllocationControlsPass(observations), Is.EqualTo(expected));
        }

        private readonly struct NativeAllocationObservation
        {
            public readonly int Requested;
            public readonly bool Preboxed;
            public readonly ulong Count;
            public readonly ulong LogicalObjectBytes;
            public readonly bool Completed;
            public readonly bool ValuesVerified;

            public NativeAllocationObservation(
                int requested,
                bool preboxed,
                ulong count,
                ulong logicalObjectBytes,
                bool completed,
                bool valuesVerified
            )
            {
                Requested = requested;
                Preboxed = preboxed;
                Count = count;
                LogicalObjectBytes = logicalObjectBytes;
                Completed = completed;
                ValuesVerified = valuesVerified;
            }
        }
    }
}
