// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Text.Json;

    internal readonly struct BenchmarkThreadSnapshot
    {
        private const string NativeLibrary = "kernel32.dll";

        internal long CaptureStartTimestamp { get; }
        internal long CaptureEndTimestamp { get; }
        internal uint? NativeProcessId { get; }
        internal uint? NativeThreadId { get; }
        internal uint? ProcessorNumber { get; }
        internal ulong? KernelTime100Nanoseconds { get; }
        internal ulong? UserTime100Nanoseconds { get; }
        internal ulong? ThreadCycleCount { get; }
        internal int? ThreadTimesError { get; }
        internal int? ThreadCyclesError { get; }
        internal string Availability { get; }

        internal BenchmarkThreadSnapshot(
            long captureStartTimestamp,
            long captureEndTimestamp,
            uint? nativeProcessId,
            uint? nativeThreadId,
            uint? processorNumber,
            ulong? kernelTime100Nanoseconds,
            ulong? userTime100Nanoseconds,
            ulong? threadCycleCount,
            int? threadTimesError,
            int? threadCyclesError,
            string availability
        )
        {
            CaptureStartTimestamp = captureStartTimestamp;
            CaptureEndTimestamp = captureEndTimestamp;
            NativeProcessId = nativeProcessId;
            NativeThreadId = nativeThreadId;
            ProcessorNumber = processorNumber;
            KernelTime100Nanoseconds = kernelTime100Nanoseconds;
            UserTime100Nanoseconds = userTime100Nanoseconds;
            ThreadCycleCount = threadCycleCount;
            ThreadTimesError = threadTimesError;
            ThreadCyclesError = threadCyclesError;
            Availability = availability;
        }

        internal static BenchmarkThreadSnapshot Capture()
        {
            long start = Stopwatch.GetTimestamp();
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                IntPtr thread = GetCurrentThread();
                uint processId = GetCurrentProcessId();
                uint threadId = GetCurrentThreadId();
                uint processor = GetCurrentProcessorNumber();
                bool timesSucceeded = GetThreadTimes(
                    thread,
                    out _,
                    out _,
                    out ulong kernel,
                    out ulong user
                );
                int timesError = timesSucceeded ? 0 : Marshal.GetLastWin32Error();
                bool cyclesSucceeded = QueryThreadCycleTime(thread, out ulong nativeCycles);
                int cyclesError = cyclesSucceeded ? 0 : Marshal.GetLastWin32Error();
                return FromNativeResults(
                    start,
                    Stopwatch.GetTimestamp(),
                    processId,
                    threadId,
                    processor,
                    timesSucceeded,
                    kernel,
                    user,
                    timesError,
                    cyclesSucceeded,
                    nativeCycles,
                    cyclesError
                );
            }
            catch (DllNotFoundException)
            {
                return Unavailable(start, "NativeLibraryUnavailable");
            }
            catch (EntryPointNotFoundException)
            {
                return Unavailable(start, "NativeEntryPointUnavailable");
            }
            catch (BadImageFormatException)
            {
                return Unavailable(start, "NativeBinaryUnavailable");
            }
#else
            return Unavailable(start, "UnsupportedPlatform");
#endif
        }

        internal static BenchmarkThreadSnapshot FromNativeResults(
            long captureStartTimestamp,
            long captureEndTimestamp,
            uint nativeProcessId,
            uint nativeThreadId,
            uint processorNumber,
            bool threadTimesSucceeded,
            ulong kernelTime100Nanoseconds,
            ulong userTime100Nanoseconds,
            int threadTimesError,
            bool threadCyclesSucceeded,
            ulong threadCycleCount,
            int threadCyclesError
        )
        {
            return new BenchmarkThreadSnapshot(
                captureStartTimestamp,
                captureEndTimestamp,
                nativeProcessId,
                nativeThreadId,
                processorNumber,
                threadTimesSucceeded ? kernelTime100Nanoseconds : (ulong?)null,
                threadTimesSucceeded ? userTime100Nanoseconds : (ulong?)null,
                threadCyclesSucceeded ? threadCycleCount : (ulong?)null,
                threadTimesSucceeded ? (int?)null : threadTimesError,
                threadCyclesSucceeded ? (int?)null : threadCyclesError,
                threadTimesSucceeded && threadCyclesSucceeded ? "Available" : "NativeApiFailed"
            );
        }

        private static BenchmarkThreadSnapshot Unavailable(long start, string availability)
        {
            return new BenchmarkThreadSnapshot(
                start,
                Stopwatch.GetTimestamp(),
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                availability
            );
        }

        private static void WriteUnsigned(Utf8JsonWriter writer, string name, ulong? value)
        {
            if (value.HasValue)
            {
                writer.WriteNumber(name, value.Value);
            }
            else
            {
                writer.WriteNull(name);
            }
        }

        private static void WriteError(Utf8JsonWriter writer, string name, int? error)
        {
            if (error.HasValue)
            {
                writer.WriteNumber(name, error.Value);
            }
            else
            {
                writer.WriteNull(name);
            }
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport(NativeLibrary, ExactSpelling = true)]
        private static extern IntPtr GetCurrentThread();

        [DllImport(NativeLibrary, ExactSpelling = true)]
        private static extern uint GetCurrentProcessId();

        [DllImport(NativeLibrary, ExactSpelling = true)]
        private static extern uint GetCurrentThreadId();

        [DllImport(NativeLibrary, ExactSpelling = true)]
        private static extern uint GetCurrentProcessorNumber();

        [DllImport(NativeLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetThreadTimes(
            IntPtr thread,
            out ulong creationTime,
            out ulong exitTime,
            out ulong kernelTime,
            out ulong userTime
        );

        [DllImport(NativeLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycleTime);
#endif

        internal void WriteTo(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteNumber(nameof(CaptureStartTimestamp), CaptureStartTimestamp);
            writer.WriteNumber(nameof(CaptureEndTimestamp), CaptureEndTimestamp);
            WriteUnsigned(writer, nameof(NativeProcessId), NativeProcessId);
            WriteUnsigned(writer, nameof(NativeThreadId), NativeThreadId);
            WriteUnsigned(writer, nameof(ProcessorNumber), ProcessorNumber);
            WriteUnsigned(writer, nameof(KernelTime100Nanoseconds), KernelTime100Nanoseconds);
            WriteUnsigned(writer, nameof(UserTime100Nanoseconds), UserTime100Nanoseconds);
            WriteUnsigned(writer, nameof(ThreadCycleCount), ThreadCycleCount);
            WriteError(writer, nameof(ThreadTimesError), ThreadTimesError);
            WriteError(writer, nameof(ThreadCyclesError), ThreadCyclesError);
            writer.WriteString(nameof(Availability), Availability);
            writer.WriteEndObject();
        }
    }
}
