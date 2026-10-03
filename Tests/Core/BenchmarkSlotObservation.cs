// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Core
{
    using System.Text.Json;

    internal readonly struct BenchmarkSlotObservation
    {
        internal int ChronologicalIndex { get; }
        internal bool IsSubject { get; }
        internal long WorkStartTimestamp { get; }
        internal long WorkEndTimestamp { get; }
        internal BenchmarkThreadSnapshot Before { get; }
        internal BenchmarkThreadSnapshot After { get; }

        internal BenchmarkSlotObservation(
            int chronologicalIndex,
            bool isSubject,
            long workStartTimestamp,
            long workEndTimestamp,
            BenchmarkThreadSnapshot before,
            BenchmarkThreadSnapshot after
        )
        {
            ChronologicalIndex = chronologicalIndex;
            IsSubject = isSubject;
            WorkStartTimestamp = workStartTimestamp;
            WorkEndTimestamp = workEndTimestamp;
            Before = before;
            After = after;
        }

        internal void WriteTo(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteNumber(nameof(ChronologicalIndex), ChronologicalIndex);
            writer.WriteBoolean(nameof(IsSubject), IsSubject);
            writer.WriteNumber(nameof(WorkStartTimestamp), WorkStartTimestamp);
            writer.WriteNumber(nameof(WorkEndTimestamp), WorkEndTimestamp);
            writer.WritePropertyName(nameof(Before));
            Before.WriteTo(writer);
            writer.WritePropertyName(nameof(After));
            After.WriteTo(writer);
            writer.WriteEndObject();
        }
    }
}
