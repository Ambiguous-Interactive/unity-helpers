// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Runtime.Performance
{
    using System;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Text.Json;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Tests.Extensions;

    [TestFixture]
    [Category("Performance")]
    public sealed partial class BorrowedBufferPreflightTests
    {
        private const int ClockBrackets = 4096;
        private const int AllocationRepetitions = 3;
        private const int WarmupWindows = 3;
        private const int SmallerBoxCount = 4096;
        private const int LargerBoxCount = 8192;
        private const double MaximumTickMicroseconds = 1;
        private const double MaximumEmptyBracketMicroseconds = 100;
        private const string RecordMarker = "BORROWED_PREFLIGHT";
        private const string PassedState = "passed";
        private const string RejectedState = "rejected";
        private const string UnsupportedState = "unsupported";
        private const string UnverifiedState = "unverified";

        private static object[] _retainedBoxes;
        private static object _preboxedValue;
        private static long? _unfinishedBeforeBytes;

        private static ClockObservation[] CaptureClock()
        {
            ClockObservation[] observations = new ClockObservation[ClockBrackets];
            for (int index = 0; index < observations.Length; ++index)
            {
                long start = Stopwatch.GetTimestamp();
                long end = Stopwatch.GetTimestamp();
                observations[index] = new ClockObservation(start, end);
            }
            return observations;
        }

        private static bool ClockControlsPass(
            ClockObservation[] observations,
            long frequency,
            bool isHighResolution
        )
        {
            if (!isHighResolution || frequency <= 0)
            {
                return false;
            }
            double microsecondsPerTick = 1_000_000.0 / frequency;
            long minimumPositiveTicks = long.MaxValue;
            long maximumTicks = 0;
            long previousEnd = long.MinValue;
            foreach (ClockObservation observation in observations)
            {
                if (
                    observation.StartTimestamp < 0
                    || observation.StartTimestamp < previousEnd
                    || observation.EndTimestamp < observation.StartTimestamp
                )
                {
                    return false;
                }
                long elapsed = observation.EndTimestamp - observation.StartTimestamp;
                if (0 < elapsed)
                {
                    minimumPositiveTicks = Math.Min(minimumPositiveTicks, elapsed);
                }
                maximumTicks = Math.Max(maximumTicks, elapsed);
                previousEnd = observation.EndTimestamp;
            }
            return observations.Length == ClockBrackets
                && microsecondsPerTick <= MaximumTickMicroseconds
                && minimumPositiveTicks != long.MaxValue
                && minimumPositiveTicks * microsecondsPerTick <= MaximumTickMicroseconds
                && maximumTicks * microsecondsPerTick <= MaximumEmptyBracketMicroseconds;
        }

        private static bool IsSupportedIl2CppCounterVersion(string version)
        {
            if (string.IsNullOrEmpty(version) || 0 <= version.IndexOf('\0'))
            {
                return false;
            }
            string[] components = version.Split('.');
            if (
                components.Length != 3
                || !string.Equals(components[0], "6000", StringComparison.Ordinal)
                || !int.TryParse(
                    components[1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int minor
                )
                || minor < 2
            )
            {
                return false;
            }
            string patchAndBuild = components[2];
            int suffixIndex = patchAndBuild.IndexOf('f');
            if (suffixIndex < 0)
            {
                suffixIndex = patchAndBuild.IndexOf('p');
            }
            return 0 < suffixIndex
                && int.TryParse(
                    patchAndBuild.Substring(0, suffixIndex),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int patch
                )
                && 0 <= patch
                && int.TryParse(
                    patchAndBuild.Substring(suffixIndex + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int build
                )
                && 0 < build;
        }

        private static bool CanCaptureThreadAllocations()
        {
#if ENABLE_IL2CPP && !UNITY_6000_2_OR_NEWER
            return false;
#elif ENABLE_IL2CPP
            return IsSupportedIl2CppCounterVersion(Application.unityVersion);
#else
            return true;
#endif
        }

        private static AllocationObservation[] CaptureAllocations(int seed, out string counterError)
        {
#if ENABLE_IL2CPP && !UNITY_6000_2_OR_NEWER
            counterError = null;
            return Array.Empty<AllocationObservation>();
#else
            if (!CanCaptureThreadAllocations())
            {
                counterError = null;
                return Array.Empty<AllocationObservation>();
            }
            AllocationObservation[] observations = new AllocationObservation[
                WarmupWindows + AllocationRepetitions * 4
            ];
            _retainedBoxes = new object[LargerBoxCount];
            _preboxedValue = seed;
            int completed = 0;
            string capturedError = null;
            try
            {
                observations[completed++] = MeasureBoxes(LargerBoxCount, seed);
                observations[completed++] = MeasurePreboxedStores(seed);
                observations[completed++] = MeasureBoxes(0, seed);
                for (int repetition = 0; repetition < AllocationRepetitions; ++repetition)
                {
                    observations[completed++] = MeasureBoxes(0, seed);
                    observations[completed++] = MeasurePreboxedStores(seed);
                    observations[completed++] = MeasureBoxes(SmallerBoxCount, seed);
                    observations[completed++] = MeasureBoxes(LargerBoxCount, seed);
                }
            }
            catch (NotSupportedException exception)
            {
                capturedError = exception.ToString();
            }
            catch (NotImplementedException exception)
            {
                capturedError = exception.ToString();
            }
            catch (EntryPointNotFoundException exception)
            {
                capturedError = exception.ToString();
            }
            if (capturedError != null)
            {
                // The increment occurs before invocation; exclude the unfinished window.
                Array.Resize(ref observations, completed - 1);
            }
            counterError = capturedError;
            return observations;
#endif
        }

        private static bool AllocationControlsPass(AllocationObservation[] observations)
        {
            if (observations.Length != WarmupWindows + AllocationRepetitions * 4)
            {
                return false;
            }
            long previousAfter = 0;
            foreach (AllocationObservation observation in observations)
            {
                if (
                    observation.BeforeBytes < previousAfter
                    || observation.AfterBytes < observation.BeforeBytes
                )
                {
                    return false;
                }
                previousAfter = observation.AfterBytes;
            }
            for (int index = 0; index < WarmupWindows; ++index)
            {
                AllocationObservation warmup = observations[index];
                int expectedCount = index == WarmupWindows - 1 ? 0 : LargerBoxCount;
                if (
                    !warmup.ValuesVerified
                    || warmup.Count != expectedCount
                    || warmup.BeforeBytes < 0
                    || warmup.AfterBytes < warmup.BeforeBytes
                )
                {
                    return false;
                }
            }
            for (int repetition = 0; repetition < AllocationRepetitions; ++repetition)
            {
                int offset = WarmupWindows + repetition * 4;
                AllocationObservation empty = observations[offset];
                AllocationObservation preboxed = observations[offset + 1];
                AllocationObservation smaller = observations[offset + 2];
                AllocationObservation larger = observations[offset + 3];
                if (
                    empty.BeforeBytes < 0
                    || preboxed.BeforeBytes < 0
                    || smaller.BeforeBytes < 0
                    || larger.BeforeBytes < 0
                    || empty.Count != 0
                    || preboxed.Count != LargerBoxCount
                    || smaller.Count != SmallerBoxCount
                    || larger.Count != LargerBoxCount
                    || !empty.ValuesVerified
                    || !preboxed.ValuesVerified
                    || !smaller.ValuesVerified
                    || !larger.ValuesVerified
                    || empty.DeltaBytes != 0
                    || preboxed.DeltaBytes != 0
                    || smaller.AfterBytes < smaller.BeforeBytes
                    || larger.AfterBytes < larger.BeforeBytes
                    || smaller.DeltaBytes < SmallerBoxCount * sizeof(int)
                    || long.MaxValue / 2 < smaller.DeltaBytes
                    || larger.DeltaBytes != smaller.DeltaBytes * 2
                )
                {
                    return false;
                }
            }
            return true;
        }

#if !ENABLE_IL2CPP || UNITY_6000_2_OR_NEWER
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static AllocationObservation MeasureBoxes(int count, int seed)
        {
            _unfinishedBeforeBytes = null;
            long before = GC.GetAllocatedBytesForCurrentThread();
            _unfinishedBeforeBytes = before;
            for (int index = 0; index < count; ++index)
            {
                _retainedBoxes[index] = unchecked(seed + index);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            _unfinishedBeforeBytes = null;
            long checksum = 0;
            bool verified = true;
            for (int index = 0; index < count; ++index)
            {
                if (_retainedBoxes[index] is int value)
                {
                    checksum += value;
                    verified &= value == unchecked(seed + index);
                }
                else
                {
                    verified = false;
                }
            }
            GC.KeepAlive(_retainedBoxes);
            return new AllocationObservation(before, after, count, checksum, verified);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static AllocationObservation MeasurePreboxedStores(int seed)
        {
            _unfinishedBeforeBytes = null;
            long before = GC.GetAllocatedBytesForCurrentThread();
            _unfinishedBeforeBytes = before;
            for (int index = 0; index < LargerBoxCount; ++index)
            {
                _retainedBoxes[index] = _preboxedValue;
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            _unfinishedBeforeBytes = null;
            bool verified = true;
            long checksum = 0;
            foreach (object value in _retainedBoxes)
            {
                verified &= ReferenceEquals(value, _preboxedValue);
                if (value is int observed)
                {
                    checksum += observed;
                    verified &= observed == seed;
                }
                else
                {
                    verified = false;
                }
            }
            GC.KeepAlive(_retainedBoxes);
            return new AllocationObservation(before, after, LargerBoxCount, checksum, verified);
        }
#endif

        private static string CreateRecord(
            ClockObservation[] clock,
            AllocationObservation[] allocations,
            bool runtimeReleasePlayer,
            bool clockPassed,
            bool allocationsPassed,
            int seed,
            string counterError
        )
        {
            string frozenRunId = null;
            string frozenDeclarationSha256 = null;
            string frozenCorpusSha256 = null;
            ReadFrozenAnchor(ref frozenRunId, ref frozenDeclarationSha256, ref frozenCorpusSha256);
            string actualCorpusSha256 = BorrowedBase64Tests.GetCanonicalCorpusSha256();
            using MemoryStream stream = new MemoryStream();
            Utf8JsonWriter writer = new Utf8JsonWriter(stream);
            try
            {
                writer.WriteStartObject();
                writer.WriteNumber("SchemaVersion", 1);
                writer.WriteString("FrozenRunId", frozenRunId);
                writer.WriteString("FrozenDeclarationSha256", frozenDeclarationSha256);
                writer.WriteString("FrozenCorpusSha256", frozenCorpusSha256);
                writer.WriteString("ActualCorpusSha256", actualCorpusSha256);
                writer.WriteBoolean("IsDiagnosticOnly", true);
                writer.WriteBoolean("CampaignEligible", false);
                writer.WriteBoolean("AdoptionEligible", false);
                writer.WriteString("BinaryIdentityState", UnverifiedState);
                writer.WriteString("CorpusIdentityState", UnverifiedState);
                writer.WriteString("BuildSettingsState", UnverifiedState);
                writer.WriteString("RetainedMemoryState", UnsupportedState);
                writer.WriteString("CodeSizeState", UnverifiedState);
                writer.WriteString(
                    "RuntimeReleasePlayerState",
                    runtimeReleasePlayer ? PassedState : RejectedState
                );
                writer.WriteStartObject("RuntimeIdentity");
                writer.WriteString("UnityVersion", Application.unityVersion);
#if ENABLE_IL2CPP
                writer.WriteString("Backend", "IL2CPP");
#else
                writer.WriteString("Backend", "Mono");
#endif
                writer.WriteBoolean("IsEditor", Application.isEditor);
                writer.WriteBoolean("DevelopmentBuild", UnityEngine.Debug.isDebugBuild);
                writer.WriteString("Platform", Application.platform.ToString());
                writer.WriteString("MachineName", Environment.MachineName);
                writer.WriteString("OperatingSystem", SystemInfo.operatingSystem);
                writer.WriteString("Cpu", SystemInfo.processorType);
                writer.WriteNumber("ProcessBits", IntPtr.Size * 8);
                writer.WriteString(
                    "Commit",
                    Environment.GetEnvironmentVariable("UH_PERF_COMMIT") ?? UnverifiedState
                );
                writer.WriteEndObject();
                writer.WriteString("ClockState", clockPassed ? PassedState : RejectedState);
                writer.WriteNumber("CounterFrequency", Stopwatch.Frequency);
                writer.WriteBoolean("CounterIsHighResolution", Stopwatch.IsHighResolution);
                writer.WriteNumber(nameof(MaximumTickMicroseconds), MaximumTickMicroseconds);
                writer.WriteNumber(
                    nameof(MaximumEmptyBracketMicroseconds),
                    MaximumEmptyBracketMicroseconds
                );
                writer.WriteNumber("ExpectedClockBrackets", ClockBrackets);
                writer.WriteStartArray("ClockObservations");
                for (int index = 0; index < clock.Length; ++index)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("ChronologicalIndex", index);
                    writer.WriteNumber(
                        nameof(ClockObservation.StartTimestamp),
                        clock[index].StartTimestamp
                    );
                    writer.WriteNumber(
                        nameof(ClockObservation.EndTimestamp),
                        clock[index].EndTimestamp
                    );
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                if (!CanCaptureThreadAllocations())
                {
                    writer.WriteString("AllocatedBytesState", UnsupportedState);
                    writer.WriteString(
                        "AllocatedBytesReason",
                        "The IL2CPP thread allocation counter requires a supported stable Unity 6.2 or later Unity 6 version; older, prerelease, and unknown versions are skipped before every counter call."
                    );
                }
                else
                {
                    writer.WriteString(
                        "AllocatedBytesState",
                        counterError != null ? UnsupportedState
                            : allocationsPassed ? PassedState
                            : RejectedState
                    );
                    writer.WriteString(
                        "AllocatedBytesReason",
                        "Same-thread managed-byte controls only; worker and native allocation channels are unverified."
                    );
                }
                writer.WriteString("CounterError", counterError);
                writer.WriteBoolean("HasIncompleteWindow", counterError != null);
                if (counterError != null)
                {
                    writer.WriteNumber("IncompleteWindowChronologicalIndex", allocations.Length);
                    if (_unfinishedBeforeBytes.HasValue)
                    {
                        writer.WriteNumber(
                            "IncompleteWindowBeforeBytes",
                            _unfinishedBeforeBytes.Value
                        );
                    }
                    else
                    {
                        writer.WriteNull("IncompleteWindowBeforeBytes");
                    }
                }
                writer.WriteNumber("Seed", seed);
                writer.WriteNumber("ExpectedWarmupWindows", WarmupWindows);
                writer.WriteNumber("ExpectedAllocationWindows", AllocationRepetitions * 4);
                writer.WriteNumber("EmptyExpectedBytes", 0);
                writer.WriteNumber("PreboxedExpectedBytes", 0);
                writer.WriteNumber("FreshSmallerMinimumBytes", SmallerBoxCount * sizeof(int));
                writer.WriteNumber("ScaledMultiplier", 2);
                writer.WriteNumber("ToleranceBytes", 0);
                writer.WriteStartArray("AllocationObservations");
                for (int index = 0; index < allocations.Length; ++index)
                {
                    AllocationObservation observation = allocations[index];
                    writer.WriteStartObject();
                    writer.WriteNumber("ChronologicalIndex", index);
                    writer.WriteBoolean("IsWarmup", index < WarmupWindows);
                    writer.WriteNumber(
                        "Repetition",
                        index < WarmupWindows ? -1 : (index - WarmupWindows) / 4
                    );
                    writer.WriteString(
                        "WindowKind",
                        index < WarmupWindows
                            ? index == 0
                                ? "fresh-larger"
                                : index == 1
                                    ? "preboxed-stores"
                                    : "empty"
                            : (index - WarmupWindows) % 4 == 0
                                ? "empty"
                                : (index - WarmupWindows) % 4 == 1
                                    ? "preboxed-stores"
                                    : (index - WarmupWindows) % 4 == 2
                                        ? "fresh-smaller"
                                        : "fresh-larger"
                    );
                    writer.WriteNumber(
                        nameof(AllocationObservation.BeforeBytes),
                        observation.BeforeBytes
                    );
                    writer.WriteNumber(
                        nameof(AllocationObservation.AfterBytes),
                        observation.AfterBytes
                    );
                    writer.WriteNumber(
                        nameof(AllocationObservation.DeltaBytes),
                        observation.DeltaBytes
                    );
                    writer.WriteNumber(nameof(AllocationObservation.Count), observation.Count);
                    writer.WriteNumber(
                        nameof(AllocationObservation.Checksum),
                        observation.Checksum
                    );
                    writer.WriteBoolean(
                        nameof(AllocationObservation.ValuesVerified),
                        observation.ValuesVerified
                    );
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();
            }
            finally
            {
                writer.Dispose();
            }
            return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
        }

        static partial void ReadFrozenAnchor(
            ref string runId,
            ref string declarationSha256,
            ref string corpusSha256
        );

        [TestCase("6000.2.0f1", true)]
        [TestCase("6000.5.2f1", true)]
        [TestCase("6000.2.0p1", true)]
        [TestCase("6000.0.99f1", false)]
        [TestCase("6000.1.4f1", false)]
        [TestCase("6000.2.0a1", false)]
        [TestCase("6000.2.0b6", false)]
        [TestCase("6000.2.0rc1", false)]
        [TestCase("6000.2.0f0", false)]
        [TestCase("6000.2.0f1-extra", false)]
        [TestCase("6000.2.0f1\0", false)]
        [TestCase("6000.2.0f1.1", false)]
        [TestCase("6000.2.0", false)]
        [TestCase("7000.2.0f1", false)]
        [TestCase("6000.2147483648.0f1", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void Il2CppCounterVersionRequiresKnownStableRelease(string version, bool expected)
        {
            Assert.That(IsSupportedIl2CppCounterVersion(version), Is.EqualTo(expected));
        }

        [TestCase(0, true, TestName = "Clock.Qualified.Passed")]
        [TestCase(1, false, TestName = "Clock.ZeroFrequency.Rejected")]
        [TestCase(2, false, TestName = "Clock.LowResolution.Rejected")]
        [TestCase(3, false, TestName = "Clock.CoarseNominalTick.Rejected")]
        [TestCase(4, false, TestName = "Clock.Incomplete.Rejected")]
        [TestCase(5, false, TestName = "Clock.NoPositiveDelta.Rejected")]
        [TestCase(6, false, TestName = "Clock.Nonmonotone.Rejected")]
        [TestCase(7, false, TestName = "Clock.Reversed.Rejected")]
        [TestCase(8, false, TestName = "Clock.TimestampOverflow.Rejected")]
        [TestCase(9, false, TestName = "Clock.ExcessiveBracket.Rejected")]
        [TestCase(10, false, TestName = "Clock.CoarseMeasuredTick.Rejected")]
        public void SyntheticClockDecisionsRejectUnqualifiedObservations(int variant, bool expected)
        {
            ClockObservation[] observations = new ClockObservation[ClockBrackets];
            for (int index = 0; index < observations.Length; ++index)
            {
                observations[index] = new ClockObservation(index * 2, index * 2 + 1);
            }
            long frequency = 10_000_000;
            bool highResolution = true;
            switch (variant)
            {
                case 1:
                    frequency = 0;
                    break;
                case 2:
                    highResolution = false;
                    break;
                case 3:
                    frequency = 100_000;
                    break;
                case 4:
                    Array.Resize(ref observations, ClockBrackets - 1);
                    break;
                case 5:
                    Array.Fill(observations, new ClockObservation(0, 0));
                    break;
                case 6:
                    observations[1] = new ClockObservation(0, 1);
                    break;
                case 7:
                    observations[0] = new ClockObservation(1, 0);
                    break;
                case 8:
                    observations[0] = new ClockObservation(long.MinValue, long.MaxValue);
                    break;
                case 10:
                    for (int index = 0; index < observations.Length; ++index)
                    {
                        observations[index] = new ClockObservation(index * 22, index * 22 + 11);
                    }
                    break;
                case 9:
                    observations[ClockBrackets - 1] = new ClockObservation(
                        ClockBrackets * 2,
                        ClockBrackets * 2 + 1001
                    );
                    break;
            }
            Assert.That(
                ClockControlsPass(observations, frequency, highResolution),
                Is.EqualTo(expected)
            );
        }

        [TestCase(0, true, TestName = "Allocation.Qualified.Passed")]
        [TestCase(1, false, TestName = "Allocation.Incomplete.Rejected")]
        [TestCase(2, false, TestName = "Allocation.NoisyEmpty.Rejected")]
        [TestCase(3, false, TestName = "Allocation.NoisyPreboxed.Rejected")]
        [TestCase(4, false, TestName = "Allocation.Inert.Rejected")]
        [TestCase(5, false, TestName = "Allocation.ScaledMismatch.Rejected")]
        [TestCase(6, false, TestName = "Allocation.NegativeDelta.Rejected")]
        [TestCase(7, false, TestName = "Allocation.CounterExtreme.Rejected")]
        [TestCase(8, false, TestName = "Allocation.UnverifiedValues.Rejected")]
        [TestCase(9, false, TestName = "Allocation.MalformedCount.Rejected")]
        [TestCase(10, false, TestName = "Allocation.MalformedWarmup.Rejected")]
        [TestCase(11, false, TestName = "Allocation.BetweenWindowReset.Rejected")]
        public void SyntheticAllocationDecisionsRejectUnqualifiedObservations(
            int variant,
            bool expected
        )
        {
            AllocationObservation[] observations = new AllocationObservation[
                WarmupWindows + AllocationRepetitions * 4
            ];
            observations[0] = new AllocationObservation(0, 0, LargerBoxCount, 0, true);
            observations[1] = new AllocationObservation(0, 0, LargerBoxCount, 0, true);
            observations[2] = new AllocationObservation(0, 0, 0, 0, true);
            long before = 100;
            for (int repetition = 0; repetition < AllocationRepetitions; ++repetition)
            {
                int offset = WarmupWindows + repetition * 4;
                observations[offset] = new AllocationObservation(before, before, 0, 0, true);
                observations[offset + 1] = new AllocationObservation(
                    before,
                    before,
                    LargerBoxCount,
                    0,
                    true
                );
                observations[offset + 2] = new AllocationObservation(
                    before,
                    before + SmallerBoxCount * 24,
                    SmallerBoxCount,
                    0,
                    true
                );
                before = observations[offset + 2].AfterBytes;
                observations[offset + 3] = new AllocationObservation(
                    before,
                    before + LargerBoxCount * 24,
                    LargerBoxCount,
                    0,
                    true
                );
                before = observations[offset + 3].AfterBytes;
            }
            switch (variant)
            {
                case 11:
                    observations[WarmupWindows + 4] = new AllocationObservation(
                        100,
                        100,
                        0,
                        0,
                        true
                    );
                    break;
                case 10:
                    observations[0] = new AllocationObservation(0, 0, LargerBoxCount - 1, 0, true);
                    break;
                case 1:
                    Array.Resize(ref observations, observations.Length - 1);
                    break;
                case 2:
                    observations[WarmupWindows] = new AllocationObservation(100, 101, 0, 0, true);
                    break;
                case 3:
                    observations[WarmupWindows + 1] = new AllocationObservation(
                        100,
                        101,
                        LargerBoxCount,
                        0,
                        true
                    );
                    break;
                case 4:
                    for (int index = WarmupWindows; index < observations.Length; ++index)
                    {
                        AllocationObservation observation = observations[index];
                        observations[index] = new AllocationObservation(
                            100,
                            100,
                            observation.Count,
                            observation.Checksum,
                            observation.ValuesVerified
                        );
                    }
                    break;
                case 5:
                    observations[WarmupWindows + 3] = new AllocationObservation(
                        observations[WarmupWindows + 3].BeforeBytes,
                        observations[WarmupWindows + 3].AfterBytes + 1,
                        LargerBoxCount,
                        0,
                        true
                    );
                    break;
                case 6:
                    observations[WarmupWindows + 2] = new AllocationObservation(
                        100,
                        99,
                        SmallerBoxCount,
                        0,
                        true
                    );
                    break;
                case 7:
                    observations[WarmupWindows + 2] = new AllocationObservation(
                        0,
                        long.MaxValue,
                        SmallerBoxCount,
                        0,
                        true
                    );
                    break;
                case 8:
                    observations[WarmupWindows + 2] = new AllocationObservation(
                        100,
                        100 + SmallerBoxCount * 24,
                        SmallerBoxCount,
                        0,
                        false
                    );
                    break;
                case 9:
                    observations[WarmupWindows + 2] = new AllocationObservation(
                        100,
                        100 + SmallerBoxCount * 24,
                        SmallerBoxCount - 1,
                        0,
                        true
                    );
                    break;
            }
            int shiftStart =
                variant == 2 ? WarmupWindows + 1
                : variant == 3 ? WarmupWindows + 2
                : variant == 5 ? WarmupWindows + 4
                : observations.Length;
            for (int index = shiftStart; index < observations.Length; ++index)
            {
                AllocationObservation observation = observations[index];
                observations[index] = new AllocationObservation(
                    observation.BeforeBytes + 1,
                    observation.AfterBytes + 1,
                    observation.Count,
                    observation.Checksum,
                    observation.ValuesVerified
                );
            }
            Assert.That(AllocationControlsPass(observations), Is.EqualTo(expected));
        }

        [Test]
        [Timeout(30_000)]
        public void ReleasePlayerChannelsAreReported()
        {
            try
            {
                bool runtimeReleasePlayer =
                    !Application.isEditor && !UnityEngine.Debug.isDebugBuild;
                ClockObservation[] clock = CaptureClock();
                int seed = Environment.TickCount;
                AllocationObservation[] allocations = CaptureAllocations(
                    seed,
                    out string counterError
                );
                bool clockPassed = ClockControlsPass(
                    clock,
                    Stopwatch.Frequency,
                    Stopwatch.IsHighResolution
                );
                bool allocationsPassed = AllocationControlsPass(allocations);
                UnityEngine.Debug.Log(
                    $"{RecordMarker} {CreateRecord(clock, allocations, runtimeReleasePlayer, clockPassed, allocationsPassed, seed, counterError)}"
                );
                if (!runtimeReleasePlayer || !clockPassed || !allocationsPassed)
                {
                    Assert.Ignore(
                        "Diagnostic preflight controls are unqualified or unsupported. All channel records are retained; no campaign or adoption is eligible."
                    );
                }
            }
            finally
            {
                if (_retainedBoxes != null)
                {
                    Array.Clear(_retainedBoxes, 0, _retainedBoxes.Length);
                }
                _retainedBoxes = null;
                _preboxedValue = null;
                _unfinishedBeforeBytes = null;
            }
        }

        private readonly struct ClockObservation
        {
            public readonly long StartTimestamp;
            public readonly long EndTimestamp;

            public ClockObservation(long startTimestamp, long endTimestamp)
            {
                StartTimestamp = startTimestamp;
                EndTimestamp = endTimestamp;
            }
        }

        private readonly struct AllocationObservation
        {
            public long DeltaBytes => AfterBytes - BeforeBytes;

            public readonly long BeforeBytes;
            public readonly long AfterBytes;
            public readonly int Count;
            public readonly long Checksum;
            public readonly bool ValuesVerified;

            public AllocationObservation(
                long beforeBytes,
                long afterBytes,
                int count,
                long checksum,
                bool valuesVerified
            )
            {
                BeforeBytes = beforeBytes;
                AfterBytes = afterBytes;
                Count = count;
                Checksum = checksum;
                ValuesVerified = valuesVerified;
            }
        }
    }
}
