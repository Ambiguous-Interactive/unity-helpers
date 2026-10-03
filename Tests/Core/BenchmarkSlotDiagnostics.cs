// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System;
    using System.Collections.ObjectModel;
    using System.Text.Json;

    internal sealed class BenchmarkSlotDiagnostics
    {
        internal bool IsDiagnosticOnly => true;
        internal long CounterFrequency { get; }
        internal bool CounterIsHighResolution { get; }
        internal ReadOnlyCollection<BenchmarkSlotObservation> Slots { get; }
        internal BenchmarkCpuSetTopology CpuSetTopology { get; }

        internal BenchmarkSlotDiagnostics(
            BenchmarkSlotObservation[] slots,
            long counterFrequency,
            bool counterIsHighResolution
        )
            : this(slots, counterFrequency, counterIsHighResolution, null) { }

        internal BenchmarkSlotDiagnostics(
            BenchmarkSlotObservation[] slots,
            long counterFrequency,
            bool counterIsHighResolution,
            BenchmarkCpuSetTopology cpuSetTopology
        )
        {
            CpuSetTopology = cpuSetTopology;
            Slots = Array.AsReadOnly(
                slots == null
                    ? Array.Empty<BenchmarkSlotObservation>()
                    : (BenchmarkSlotObservation[])slots.Clone()
            );
            CounterFrequency = counterFrequency;
            CounterIsHighResolution = counterIsHighResolution;
        }

        internal void WriteTo(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteBoolean(nameof(IsDiagnosticOnly), IsDiagnosticOnly);
            writer.WriteNumber(nameof(CounterFrequency), CounterFrequency);
            writer.WriteBoolean(nameof(CounterIsHighResolution), CounterIsHighResolution);
            if (CpuSetTopology != null)
            {
                writer.WritePropertyName(nameof(CpuSetTopology));
                CpuSetTopology.WriteTo(writer);
            }
            writer.WriteStartArray(nameof(Slots));
            foreach (BenchmarkSlotObservation slot in Slots)
            {
                slot.WriteTo(writer);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }
}
