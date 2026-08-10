// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Math
{
    using System;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using Helper;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Inclusive Range [min,max] with configurable endpoint inclusivity.
    /// </summary>
    /// <example>
    /// <code><![CDATA[
    /// var r = Range<int>.Inclusive(0, 10);
    /// bool yes = r.Contains(10); // true (inclusive end)
    /// var re = Range<int>.Exclusive(0, 10);
    /// bool no = re.Contains(10); // false (exclusive end)
    /// ]]></code>
    /// </example>
    [Serializable]
    [DataContract]
    [ProtoContract]
    [WProtoContract]
    public partial struct Range<T> : IEquatable<Range<T>>, IComparable<Range<T>>
        where T : IEquatable<T>, IComparable<T>
    {
        [DataMember]
        [JsonInclude]
        [ProtoMember(1)]
        [WProtoMember(1)]
        public T min;

        [DataMember]
        [JsonInclude]
        [ProtoMember(2)]
        [WProtoMember(2)]
        public T max;

        [DataMember]
        [JsonInclude]
        [ProtoMember(3)]
        [WProtoMember(3)]
        public bool startInclusive;

        [DataMember]
        [JsonInclude]
        [ProtoMember(4)]
        [WProtoMember(4)]
        public bool endInclusive;

        [JsonIgnore]
        public readonly T Min => min;

        [JsonIgnore]
        public readonly T Max => max;

        [JsonIgnore]
        public readonly bool StartInclusive => startInclusive;

        [JsonIgnore]
        public readonly bool EndInclusive => endInclusive;

        [JsonConstructor]
        public Range(T min, T max, bool startInclusive = true, bool endInclusive = true)
        {
            if (min.CompareTo(max) > 0)
            {
                throw new ArgumentException($"min ({min}) must be <= max ({max})");
            }
            this.min = min;
            this.max = max;
            this.startInclusive = startInclusive;
            this.endInclusive = endInclusive;
        }

        public bool Equals(Range<T> other)
        {
            return min.Equals(other.min)
                && max.Equals(other.max)
                && startInclusive == other.startInclusive
                && endInclusive == other.endInclusive;
        }

        public override bool Equals(object obj)
        {
            return obj is Range<T> other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(min, max, startInclusive, endInclusive);
        }

        public int CompareTo(Range<T> other)
        {
            int minComparison = min.CompareTo(other.min);
            if (minComparison != 0)
            {
                return minComparison;
            }

            int maxComparison = max.CompareTo(other.max);
            if (maxComparison != 0)
            {
                return maxComparison;
            }

            if (startInclusive != other.startInclusive)
            {
                return startInclusive ? -1 : 1;
            }

            if (endInclusive != other.endInclusive)
            {
                return endInclusive ? -1 : 1;
            }

            return 0;
        }

        public override string ToString()
        {
            char start = startInclusive ? '[' : '(';
            char end = endInclusive ? ']' : ')';
            return $"{start}{min}, {max}{end}";
        }

        public bool WithinRange(T value)
        {
            int minComparison = value.CompareTo(min);
            if (minComparison < 0 || (minComparison == 0 && !startInclusive))
            {
                return false;
            }

            int maxComparison = value.CompareTo(max);
            return maxComparison < 0 || (maxComparison == 0 && endInclusive);
        }

        public bool Contains(T value) => WithinRange(value);

        public bool Overlaps(Range<T> other)
        {
            return WithinRange(other.min)
                || WithinRange(other.max)
                || other.WithinRange(min)
                || other.WithinRange(max);
        }

        public static Range<T> Inclusive(T min, T max) => new(min, max, true, true);

        public static Range<T> Exclusive(T min, T max) => new(min, max, false, false);

        public static Range<T> InclusiveExclusive(T min, T max) => new(min, max, true, false);

        public static Range<T> ExclusiveInclusive(T min, T max) => new(min, max, false, true);

        public static bool operator ==(Range<T> left, Range<T> right) => left.Equals(right);

        public static bool operator !=(Range<T> left, Range<T> right) => !left.Equals(right);
    }
}
