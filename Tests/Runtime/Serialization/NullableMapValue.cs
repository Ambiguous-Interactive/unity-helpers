// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using ProtoBuf;

    [ProtoContract]
    public struct NullableMapValue : IEquatable<NullableMapValue>, IComparable<NullableMapValue>
    {
        [ProtoMember(1)]
        public int Number { get; set; }

        public int CompareTo(NullableMapValue other)
        {
            return Number.CompareTo(other.Number);
        }

        public bool Equals(NullableMapValue other)
        {
            return Number == other.Number;
        }

        public override bool Equals(object other)
        {
            return other is NullableMapValue value && Equals(value);
        }

        public override int GetHashCode()
        {
            return Number;
        }
    }
}
