// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System.Text.Json;

    internal readonly struct BenchmarkCpuSetRecord
    {
        internal uint RecordSize { get; }
        internal uint Id { get; }
        internal ushort Group { get; }
        internal byte LogicalProcessorIndex { get; }
        internal byte CoreIndex { get; }
        internal byte EfficiencyClass { get; }
        internal byte AllFlags { get; }

        internal BenchmarkCpuSetRecord(
            uint recordSize,
            uint id,
            ushort group,
            byte logicalProcessorIndex,
            byte coreIndex,
            byte efficiencyClass,
            byte allFlags
        )
        {
            RecordSize = recordSize;
            Id = id;
            Group = group;
            LogicalProcessorIndex = logicalProcessorIndex;
            CoreIndex = coreIndex;
            EfficiencyClass = efficiencyClass;
            AllFlags = allFlags;
        }

        internal void WriteTo(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteNumber(nameof(RecordSize), RecordSize);
            writer.WriteNumber(nameof(Id), Id);
            writer.WriteNumber(nameof(Group), Group);
            writer.WriteNumber(nameof(LogicalProcessorIndex), LogicalProcessorIndex);
            writer.WriteNumber(nameof(CoreIndex), CoreIndex);
            writer.WriteNumber(nameof(EfficiencyClass), EfficiencyClass);
            writer.WriteNumber(nameof(AllFlags), AllFlags);
            writer.WriteEndObject();
        }
    }
}
