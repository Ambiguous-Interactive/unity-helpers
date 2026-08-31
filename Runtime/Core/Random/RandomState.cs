// MIT License - Copyright (c) 2023 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Collections.Generic;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using Extension;
    using Helper;
    using ProtoBuf;
    using Utils;

    [Serializable]
    [DataContract]
#pragma warning disable WPROTO030 // Served by RandomStateWProtoFormatter.
    [ProtoContract]
#pragma warning restore WPROTO030
    public readonly partial struct RandomState : IEquatable<RandomState>
    {
        [JsonInclude]
        public ulong State1 => _state1;

        [JsonInclude]
        public ulong State2 => _state2;

        [JsonInclude]
        public double? Gaussian
        {
            get
            {
                if (_hasGaussian)
                {
                    return _gaussian;
                }

                return null;
            }
        }

        [JsonIgnore]
        public IReadOnlyList<byte> PayloadBytes => _payload;

        [JsonInclude]
        [JsonPropertyName("Payload")]
        public IReadOnlyList<byte> Payload => _payload;

        // Reservoir state (for AbstractRandom bit/byte reservoirs)
        [JsonInclude]
        public uint BitBuffer => _bitBuffer;

        [JsonInclude]
        public int BitCount => _bitCount;

        [JsonInclude]
        public uint ByteBuffer => _byteBuffer;

        [JsonInclude]
        public int ByteCount => _byteCount;

        [ProtoMember(1)]
        [JsonIgnore]
        private readonly ulong _state1;

        [ProtoMember(2)]
        [JsonIgnore]
        private readonly ulong _state2;

        [ProtoMember(3)]
        [JsonIgnore]
        private readonly bool _hasGaussian;

        [ProtoMember(4)]
        [JsonIgnore]
        private readonly double _gaussian;

        [ProtoMember(5)]
        [JsonIgnore]
        internal readonly byte[] _payload;

        // Added fields for reservoir serialization
        [ProtoMember(6)]
        [JsonIgnore]
        private readonly uint _bitBuffer;

        [ProtoMember(7)]
        [JsonIgnore]
        private readonly int _bitCount;

        [ProtoMember(8)]
        [JsonIgnore]
        private readonly uint _byteBuffer;

        [ProtoMember(9)]
        [JsonIgnore]
        private readonly int _byteCount;

        [ProtoMember(10)]
        [JsonIgnore]
        private readonly int _hashCode;

        [JsonConstructor]
        public RandomState(
            ulong state1,
            ulong state2 = 0,
            double? gaussian = null,
            IReadOnlyList<byte> payload = null,
            uint bitBuffer = 0,
            int bitCount = 0,
            uint byteBuffer = 0,
            int byteCount = 0
        )
        {
            _state1 = state1;
            _state2 = state2;
            _hasGaussian = gaussian.HasValue;
            _gaussian = gaussian ?? 0;
            if (payload is byte[] byteArray)
            {
                _payload = byteArray;
            }
            else if (payload != null)
            {
                using (Buffers<byte>.List.Get(out List<byte> buffer))
                {
                    buffer.AddRange(payload);
                    _payload = buffer.ToArray();
                }
            }
            else
            {
                _payload = null;
            }
            _bitBuffer = bitBuffer;
            _bitCount = bitCount;
            _byteBuffer = byteBuffer;
            _byteCount = byteCount;
            _hashCode = ComputeHashCode(
                _state1,
                _state2,
                _hasGaussian,
                _gaussian,
                _payload,
                _bitBuffer,
                _bitCount,
                _byteBuffer,
                _byteCount
            );
        }

        public RandomState(Guid guid)
        {
            (ulong s1, ulong s2) = RandomUtilities.GuidToUInt64Pair(guid);
            _state1 = s1;
            _state2 = s2;
            _hasGaussian = false;
            _gaussian = 0;
            _payload = null;
            _bitBuffer = 0;
            _bitCount = 0;
            _byteBuffer = 0;
            _byteCount = 0;
            _hashCode = ComputeHashCode(
                _state1,
                _state2,
                _hasGaussian,
                _gaussian,
                _payload,
                _bitBuffer,
                _bitCount,
                _byteBuffer,
                _byteCount
            );
        }

        public override bool Equals(object other)
        {
            return other is RandomState randomState && Equals(randomState);
        }

        public bool Equals(RandomState other)
        {
            bool equivalent =
                _state1 == other._state1
                && _state2 == other._state2
                && _hasGaussian == other._hasGaussian
                && (!_hasGaussian || _gaussian.TotalEquals(other._gaussian))
                && _bitBuffer == other._bitBuffer
                && _bitCount == other._bitCount
                && _byteBuffer == other._byteBuffer
                && _byteCount == other._byteCount;
            if (!equivalent)
            {
                return false;
            }

            if (_payload == null && other._payload == null)
            {
                return true;
            }

            if (_payload == null || other._payload == null)
            {
                return false;
            }

            if (_payload.Length != other._payload.Length)
            {
                return false;
            }

            return _payload.AsSpan().SequenceEqual(other._payload);
        }

        /// <summary>
        /// Returns a hash derived from the members <see cref="Equals(RandomState)"/> compares.
        /// </summary>
        /// <remarks>
        /// Computed rather than read from the stored field: that field travels on the wire, so a
        /// payload may carry any value at all, and a zero-initialized state -- <c>default</c>, an
        /// array element, an instance a deserializer allocated without a constructor -- carries zero
        /// while <see cref="Equals(RandomState)"/> reports it equal to <c>new RandomState(0)</c>.
        /// </remarks>
        /// <returns>A hash code for this state.</returns>
        public override int GetHashCode()
        {
            return ComputeHashCode(
                _state1,
                _state2,
                _hasGaussian,
                _gaussian,
                _payload,
                _bitBuffer,
                _bitCount,
                _byteBuffer,
                _byteCount
            );
        }

        private static int ComputeHashCode(
            ulong state1,
            ulong state2,
            bool hasGaussian,
            double gaussian,
            byte[] payload,
            uint bitBuffer,
            int bitCount,
            uint byteBuffer,
            int byteCount
        )
        {
            return Objects.HashCode(
                state1,
                state2,
                hasGaussian,
                gaussian,
                payload?.Length,
                bitBuffer,
                bitCount,
                byteBuffer,
                byteCount
            );
        }

        public override string ToString()
        {
            return this.ToJson();
        }
    }
}
