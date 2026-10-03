// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Runtime.InteropServices;
    using System.Text.Json;

    internal sealed class BenchmarkCpuSetTopology
    {
        internal const int ScratchBufferBytes = 64 * 1024;
        private const string NativeLibrary = "kernel32.dll";
        private const int InsufficientBufferError = 122;
        private const int HeaderBytes = 8;
        private const int CpuSetRecordBytes = 32;
        private const uint CpuSetInformationType = 0;

        internal int SchemaVersion => 1;
        internal bool IsDiagnosticOnly => true;
        internal long CaptureStartTimestamp { get; }
        internal long CaptureEndTimestamp { get; }
        internal string MachineName { get; }
        internal string Availability { get; }
        internal int? NativeError { get; }
        internal uint? ReportedBytes { get; }
        internal ushort? ActiveProcessorGroupCount { get; }
        internal int UnknownRecordTypeCount { get; }
        internal bool CanMapProcessorNumberEndpoints { get; }
        internal ushort? ProcessorNumberEndpointGroup =>
            CanMapProcessorNumberEndpoints ? CpuSets[0].Group : (ushort?)null;
        internal string MappingLimits =>
            "Processor endpoints omit group and intermediate migrations. Match only reported CPU sets; mapping requires one active group, unique logical indices, and no unknown record types. EfficiencyClass is OS metadata, not an inferred P/E label.";
        internal ReadOnlyCollection<BenchmarkCpuSetRecord> CpuSets { get; }

        private BenchmarkCpuSetTopology(
            string availability,
            int? nativeError,
            uint? reportedBytes,
            ushort? activeProcessorGroupCount,
            long start,
            long end,
            string machineName,
            BenchmarkCpuSetRecord[] cpuSets = null,
            int unknownRecordTypeCount = 0,
            bool canMap = false
        )
        {
            Availability = availability;
            NativeError = nativeError;
            ReportedBytes = reportedBytes;
            ActiveProcessorGroupCount = activeProcessorGroupCount;
            CaptureStartTimestamp = start;
            CaptureEndTimestamp = end;
            MachineName = machineName;
            CpuSets = Array.AsReadOnly(cpuSets ?? Array.Empty<BenchmarkCpuSetRecord>());
            UnknownRecordTypeCount = unknownRecordTypeCount;
            CanMapProcessorNumberEndpoints = canMap;
        }

        internal static BenchmarkCpuSetTopology Capture()
        {
            long start = Stopwatch.GetTimestamp();
            string machineName = null;
            try
            {
                machineName = Environment.MachineName;
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                ushort groups = GetActiveProcessorGroupCount();
                if (groups == 0)
                {
                    return new BenchmarkCpuSetTopology(
                        "ActiveGroupCountUnavailable",
                        null,
                        null,
                        groups,
                        start,
                        Stopwatch.GetTimestamp(),
                        machineName
                    );
                }
                byte[] buffer = new byte[ScratchBufferBytes];
                bool succeeded = GetSystemCpuSetInformation(
                    buffer,
                    ScratchBufferBytes,
                    out uint returned,
                    IntPtr.Zero,
                    0
                );
                int error = succeeded ? 0 : Marshal.GetLastWin32Error();
                return FromNativeResult(
                    buffer,
                    succeeded,
                    error,
                    returned,
                    groups,
                    start,
                    Stopwatch.GetTimestamp(),
                    machineName
                );
#else
                return new BenchmarkCpuSetTopology(
                    "UnsupportedPlatform",
                    null,
                    null,
                    null,
                    start,
                    Stopwatch.GetTimestamp(),
                    machineName
                );
#endif
            }
            catch (DllNotFoundException)
            {
                return new BenchmarkCpuSetTopology(
                    "NativeLibraryUnavailable",
                    null,
                    null,
                    null,
                    start,
                    Stopwatch.GetTimestamp(),
                    machineName
                );
            }
            catch (EntryPointNotFoundException)
            {
                return new BenchmarkCpuSetTopology(
                    "NativeEntryPointUnavailable",
                    null,
                    null,
                    null,
                    start,
                    Stopwatch.GetTimestamp(),
                    machineName
                );
            }
            catch (BadImageFormatException)
            {
                return new BenchmarkCpuSetTopology(
                    "NativeBinaryUnavailable",
                    null,
                    null,
                    null,
                    start,
                    Stopwatch.GetTimestamp(),
                    machineName
                );
            }
            catch (InvalidOperationException)
            {
                return new BenchmarkCpuSetTopology(
                    "HostMetadataUnavailable",
                    null,
                    null,
                    null,
                    start,
                    Stopwatch.GetTimestamp(),
                    machineName
                );
            }
        }

        internal static BenchmarkCpuSetTopology FromNativeResult(
            byte[] buffer,
            bool succeeded,
            int error,
            uint reportedBytes,
            ushort groups,
            long start,
            long end,
            string machineName
        )
        {
            if (!succeeded)
            {
                return new BenchmarkCpuSetTopology(
                    error == InsufficientBufferError ? "BufferTooSmall" : "NativeApiFailed",
                    error,
                    error == InsufficientBufferError ? reportedBytes : (uint?)null,
                    groups,
                    start,
                    end,
                    machineName
                );
            }
            if (
                buffer == null
                || ScratchBufferBytes < buffer.Length
                || buffer.Length < reportedBytes
                || ScratchBufferBytes < reportedBytes
            )
            {
                return new BenchmarkCpuSetTopology(
                    "InvalidReturnedLength",
                    null,
                    reportedBytes,
                    groups,
                    start,
                    end,
                    machineName
                );
            }
            if (groups == 0)
            {
                return new BenchmarkCpuSetTopology(
                    "ActiveGroupCountUnavailable",
                    null,
                    reportedBytes,
                    groups,
                    start,
                    end,
                    machineName
                );
            }
            if (reportedBytes == 0)
            {
                return new BenchmarkCpuSetTopology(
                    "NoCpuSets",
                    null,
                    reportedBytes,
                    groups,
                    start,
                    end,
                    machineName
                );
            }
            List<BenchmarkCpuSetRecord> records = new();
            int unknown = 0;
            int offset = 0;
            ulong logicalIndices = 0;
            bool uniqueLogicalIndices = true;
            while (offset < reportedBytes)
            {
                int remaining = (int)reportedBytes - offset;
                if (remaining < HeaderBytes)
                {
                    return new BenchmarkCpuSetTopology(
                        "TruncatedRecordHeader",
                        null,
                        reportedBytes,
                        groups,
                        start,
                        end,
                        machineName
                    );
                }
                uint size = ReadUInt32(buffer, offset);
                uint type = ReadUInt32(buffer, offset + 4);
                if (size < HeaderBytes || remaining < size)
                {
                    return new BenchmarkCpuSetTopology(
                        "InvalidRecordSize",
                        null,
                        reportedBytes,
                        groups,
                        start,
                        end,
                        machineName
                    );
                }
                if (type == CpuSetInformationType)
                {
                    if (size < CpuSetRecordBytes)
                    {
                        return new BenchmarkCpuSetTopology(
                            "TruncatedCpuSetRecord",
                            null,
                            reportedBytes,
                            groups,
                            start,
                            end,
                            machineName
                        );
                    }
                    ushort group = ReadUInt16(buffer, offset + 12);
                    byte logicalIndex = buffer[offset + 14];
                    if (groups <= group || 64 <= logicalIndex)
                    {
                        return new BenchmarkCpuSetTopology(
                            "InvalidProcessorIndex",
                            null,
                            reportedBytes,
                            groups,
                            start,
                            end,
                            machineName
                        );
                    }
                    ulong bit = 1UL << logicalIndex;
                    uniqueLogicalIndices &= (logicalIndices & bit) == 0;
                    logicalIndices |= bit;
                    records.Add(
                        new BenchmarkCpuSetRecord(
                            size,
                            ReadUInt32(buffer, offset + 8),
                            group,
                            logicalIndex,
                            buffer[offset + 15],
                            buffer[offset + 18],
                            buffer[offset + 19]
                        )
                    );
                }
                else
                {
                    ++unknown;
                }
                offset += (int)size;
            }
            return new BenchmarkCpuSetTopology(
                records.Count == 0 ? "NoKnownCpuSets" : "Available",
                null,
                reportedBytes,
                groups,
                start,
                end,
                machineName,
                records.ToArray(),
                unknown,
                groups == 1 && records.Count != 0 && unknown == 0 && uniqueLogicalIndices
            );
        }

        private static ushort ReadUInt16(byte[] buffer, int offset)
        {
            return (ushort)(buffer[offset] | buffer[offset + 1] << 8);
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)buffer[offset]
                | (uint)buffer[offset + 1] << 8
                | (uint)buffer[offset + 2] << 16
                | (uint)buffer[offset + 3] << 24;
        }

        [DllImport(NativeLibrary, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemCpuSetInformation(
            [Out] byte[] information,
            uint bufferLength,
            out uint returnedLength,
            IntPtr process,
            uint flags
        );

        [DllImport(NativeLibrary, ExactSpelling = true)]
        private static extern ushort GetActiveProcessorGroupCount();

        internal void WriteTo(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteNumber(nameof(SchemaVersion), SchemaVersion);
            writer.WriteBoolean(nameof(IsDiagnosticOnly), IsDiagnosticOnly);
            writer.WriteNumber(nameof(CaptureStartTimestamp), CaptureStartTimestamp);
            writer.WriteNumber(nameof(CaptureEndTimestamp), CaptureEndTimestamp);
            writer.WriteString(nameof(MachineName), MachineName);
            writer.WriteString(nameof(Availability), Availability);
            if (NativeError.HasValue)
            {
                writer.WriteNumber(nameof(NativeError), NativeError.Value);
            }
            else
            {
                writer.WriteNull(nameof(NativeError));
            }
            if (ReportedBytes.HasValue)
            {
                writer.WriteNumber(nameof(ReportedBytes), ReportedBytes.Value);
            }
            else
            {
                writer.WriteNull(nameof(ReportedBytes));
            }
            if (ActiveProcessorGroupCount.HasValue)
            {
                writer.WriteNumber(
                    nameof(ActiveProcessorGroupCount),
                    ActiveProcessorGroupCount.Value
                );
            }
            else
            {
                writer.WriteNull(nameof(ActiveProcessorGroupCount));
            }
            writer.WriteNumber(nameof(UnknownRecordTypeCount), UnknownRecordTypeCount);
            writer.WriteBoolean(
                nameof(CanMapProcessorNumberEndpoints),
                CanMapProcessorNumberEndpoints
            );
            if (ProcessorNumberEndpointGroup.HasValue)
            {
                writer.WriteNumber(
                    nameof(ProcessorNumberEndpointGroup),
                    ProcessorNumberEndpointGroup.Value
                );
            }
            else
            {
                writer.WriteNull(nameof(ProcessorNumberEndpointGroup));
            }
            writer.WriteString(nameof(MappingLimits), MappingLimits);
            writer.WriteStartArray(nameof(CpuSets));
            foreach (BenchmarkCpuSetRecord record in CpuSets)
            {
                record.WriteTo(writer);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
