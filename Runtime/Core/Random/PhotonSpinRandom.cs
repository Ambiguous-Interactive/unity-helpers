// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE
//
// The algorithm in this file is PhotonSpin, by Will Stafford Parsons (wileylooper/photonspin, the
// upstream repository is offline). This is an adaptation of that work; the design is the original
// author's. The upstream license is not recorded in this repository.
// See docs/project/third-party-notices.md.

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using Extension;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Utils;

    /// <summary>
    /// PhotonSpin32: a 20-word ring-buffer generator inspired by SHISHUA, tuned for high throughput and large period.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reference: Will Stafford Parsons (wileylooper/photonspin, repository offline).
    /// Ported from <c>wileylooper/photonspin</c>, this generator produces batches of 20 new 32-bit values per round,
    /// offering a huge period (~2<sup>512</sup>) and robust statistical performance. It shines when large streams are
    /// required, while still supporting deterministic state capture and serialization.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Large state with excellent distribution; great for heavy simulation workloads.</description></item>
    /// <item><description>Thread-local friendly via <see cref="ThreadLocalRandom{T}.Instance"/>.</description></item>
    /// <item><description>Deterministic snapshots through <see cref="RandomState"/>.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Higher per-instance memory (~20×4 bytes).</description></item>
    /// <item><description>Not cryptographically secure.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>Procedural workloads that benefit from bulk generation and long non-overlapping streams.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Security or adversarial scenarios.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// PhotonSpinRandom rng = new PhotonSpinRandom(seed: 42u);
    /// float noise = rng.NextFloat();
    /// Guid guid = rng.NextGuid();
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Excellent,
        "SHISHUA-inspired generator. Verified here: PractRand 0.95 clean through 8GB, the depth at which SystemRandom fails. The author reports 128GB; that run cannot be checked -- the upstream repository is offline.",
        "Will Stafford Parsons",
        "" // Original repository wileylooper/photonspin is offline
    )]
    [Serializable]
    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class PhotonSpinRandom
        : AbstractRandom,
            IEquatable<PhotonSpinRandom>,
            IComparable,
            IComparable<PhotonSpinRandom>
    {
        private const uint Increment = 111_111U;
        private const int BlockSize = 20;
        private const int ElementByteSize = BlockSize * sizeof(uint);

        public static PhotonSpinRandom Instance => ThreadLocalRandom<PhotonSpinRandom>.Instance;

        public override RandomState InternalState
        {
            get
            {
                EnsureElements();
                using PooledArray<byte> payloadLease = WallstopArrayPool<byte>.Get(
                    ElementByteSize,
                    out byte[] buffer
                );
                Buffer.BlockCopy(_elements, 0, buffer, 0, ElementByteSize);

                uint packedIndex = (uint)(_index & 0x7FFFFFFF);
                if (_hasPrimed)
                {
                    packedIndex |= 0x80000000U;
                }

                ulong state1 = ((ulong)_a << 32) | _b;
                ulong state2 = ((ulong)_c << 32) | packedIndex;
                return BuildState(
                    state1,
                    state2,
                    new ArraySegment<byte>(buffer, 0, ElementByteSize)
                );
            }
        }

        [ProtoMember(6)]
        [WProtoMember(6)]
        private uint[] _elements = new uint[BlockSize];

        [ProtoMember(7)]
        [WProtoMember(7)]
        private uint _a;

        [ProtoMember(8)]
        [WProtoMember(8)]
        private uint _b;

        [ProtoMember(9)]
        [WProtoMember(9)]
        private uint _c;

        [ProtoMember(10)]
        [WProtoMember(10)]
        private int _index;

        [ProtoMember(11)]
        [WProtoMember(11)]
        private bool _hasPrimed;

        public PhotonSpinRandom()
            : this(Guid.NewGuid()) { }

        public PhotonSpinRandom(Guid guid)
        {
            InitializeFromGuid(guid);
        }

        public PhotonSpinRandom(uint seed)
        {
            uint seedA = seed;
            uint seedB = seed ^ 0x9E3779B9U;
            if (seedB == 0)
            {
                seedB = 1U;
            }

            uint seedC = seed + 0x6C8E9CF5U;
            InitializeFromScalars(seedA, seedB, seedC);
        }

        public PhotonSpinRandom(uint seedA, uint seedB, uint seedC)
        {
            InitializeFromScalars(seedA, seedB == 0 ? 1U : seedB, seedC);
        }

        [JsonConstructor]
        public PhotonSpinRandom(RandomState internalState)
        {
            ulong state1 = internalState.State1;
            ulong state2 = internalState.State2;

            _a = (uint)(state1 >> 32);
            _b = (uint)state1;
            _c = (uint)(state2 >> 32);

            uint packedIndex = (uint)state2;
            _hasPrimed = (packedIndex & 0x80000000U) != 0;
            _index = (int)(packedIndex & 0x7FFFFFFF);
            if (_index < 0 || _index > BlockSize)
            {
                _index = BlockSize;
            }

            LoadSerializedElements(internalState._payload);
            NormalizeIndex();
            EnsurePrimed();
            RestoreCommonState(internalState);
        }

        protected override void OnAfterDeserialization()
        {
            EnsureElements();
            NormalizeIndex();
            EnsurePrimed();
        }

        public override uint NextUint()
        {
            if (_index >= BlockSize)
            {
                GenerateBlock();
            }

            uint value = _elements[_index];
            _index += 1;
            return value;
        }

        public override IRandom Copy()
        {
            return new PhotonSpinRandom(InternalState);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PhotonSpinRandom);
        }

        public bool Equals(PhotonSpinRandom other)
        {
            if (other == null)
            {
                return false;
            }

            if (_a != other._a || _b != other._b || _c != other._c)
            {
                return false;
            }

            if (_index != other._index || _hasPrimed != other._hasPrimed)
            {
                return false;
            }

            EnsureElements();
            other.EnsureElements();
            if (!_elements.AsSpan().SequenceEqual(other._elements))
            {
                return false;
            }

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return _cachedGaussian == other._cachedGaussian;
        }

        public override int GetHashCode()
        {
            return Objects.HashCode(_a, _b, _c, _index, _hasPrimed);
        }

        public override string ToString()
        {
            return this.ToJson();
        }

        public int CompareTo(object obj)
        {
            return CompareTo(obj as PhotonSpinRandom);
        }

        public int CompareTo(PhotonSpinRandom other)
        {
            if (other == null)
            {
                return 1;
            }

            EnsureElements();
            other.EnsureElements();

            int comparison = _a.CompareTo(other._a);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = _b.CompareTo(other._b);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = _c.CompareTo(other._c);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = _index.CompareTo(other._index);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = _hasPrimed.CompareTo(other._hasPrimed);
            if (comparison != 0)
            {
                return comparison;
            }

            for (int i = 0; i < BlockSize; ++i)
            {
                comparison = _elements[i].CompareTo(other._elements[i]);
                if (comparison != 0)
                {
                    return comparison;
                }
            }

            return 0;
        }

        private void EnsureElements()
        {
            if (_elements == null || _elements.Length != BlockSize)
            {
                _elements = new uint[BlockSize];
            }
        }

        private void LoadSerializedElements(byte[] payload)
        {
            EnsureElements();

            if (payload != null && payload.Length >= ElementByteSize)
            {
                Buffer.BlockCopy(payload, 0, _elements, 0, ElementByteSize);
                return;
            }

            Array.Clear(_elements, 0, _elements.Length);
        }

        private void NormalizeIndex()
        {
            if (_index < 0 || _index > BlockSize)
            {
                _index = BlockSize;
            }
        }

        private void InitializeFromGuid(Guid guid)
        {
            (ulong seed0, ulong seed1) = RandomUtilities.GuidToUInt64Pair(guid);
            InitializeFromUlongs(seed0, seed1);
        }

        private void InitializeFromScalars(uint seedA, uint seedB, uint seedC)
        {
            ulong seed0 = ((ulong)seedA << 32) | seedB;
            ulong seed1 = ((ulong)seedC << 32) | (seedB ^ 0xA5A5A5A5U);
            InitializeFromUlongs(seed0, seed1);
        }

        private void InitializeFromUlongs(ulong seed0, ulong seed1)
        {
            EnsureElements();

            ulong mixer = seed0 ^ (seed1 << 1) ^ 0x9E3779B97F4A7C15UL;

            for (int i = 0; i < BlockSize; ++i)
            {
                _elements[i] = Mix32(ref mixer);
            }

            _a = Mix32(ref mixer);
            _b = Mix32(ref mixer);
            _c = Mix32(ref mixer);

            _index = BlockSize;
            _hasPrimed = false;
            NormalizeIndex();
            EnsurePrimed();
        }

        private void EnsurePrimed()
        {
            if (_hasPrimed)
            {
                return;
            }

            // Older payloads can represent the pre-first-draw state. Advance only the discarded
            // warmup block here so the next generated block and every published output stay exact.
            GenerateBlock();
            _hasPrimed = true;
            _index = BlockSize;
        }

        private void GenerateBlock()
        {
            unchecked
            {
                Span<uint> mix = stackalloc uint[4];
                int baseIndex = (int)(_a & 15U);
                mix[0] = _elements[baseIndex];
                mix[1] = _elements[(baseIndex + 3) & 15];
                mix[2] = _elements[(baseIndex + 6) & 15];
                mix[3] = _elements[(baseIndex + 9) & 15];

                _a += Increment;

                int k = 0;
                for (int i = 0; i < 4; ++i)
                {
                    _b += _a;
                    _c ^= _a;
                    mix[i] += RotateLeft(_b, 9);

                    for (int j = 0; j < 5; ++j)
                    {
                        _elements[k] += RotateLeft(mix[i], 25);
                        _elements[k] ^= _c;
                        mix[i] += _elements[k];
                        k++;
                    }

                    if (
                        _elements[k - 1] == _elements[k - 2]
                        && _elements[k - 3] == _elements[k - 4]
                    )
                    {
                        _elements[k - 1] += mix[i] ^ 81_016U;
                        _elements[k - 2] += mix[i] ^ 297_442_265U;
                        _elements[k - 3] += (mix[i] ^ 5_480U) | (mix[i] & 1U);
                        _elements[k - 4] += mix[i] ^ 19_006_808U;
                        _elements[k - 5] += mix[i];
                    }
                }

                _b += RotateLeft(mix[0], 23);
                _b ^= mix[1];
                _c += RotateLeft(mix[2], 13);
                _c ^= mix[3];
            }

            _index = 0;
        }
    }
}
