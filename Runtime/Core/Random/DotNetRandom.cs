// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Core.Random
{
    using System;
    using System.Buffers.Binary;
    using System.Collections.Generic;
    using System.Reflection;
    using System.Runtime.Serialization;
    using System.Text.Json.Serialization;
    using Helper;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A thin wrapper around <c>System.Random</c> that exposes the <see cref="IRandom"/> API and supports state capture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Uses a real <c>System.Random</c> internally and advances it to reflect the captured <see cref="RandomState"/>.
    /// This makes it easy to interop with code that expects <c>System.Random</c> semantics while using the unified
    /// <see cref="IRandom"/> interface.
    /// </para>
    /// <para>Pros:</para>
    /// <list type="bullet">
    /// <item><description>Compatibility with <c>System.Random</c> behavior.</description></item>
    /// <item><description>Unified API; determinism via state capture.</description></item>
    /// </list>
    /// <para>Cons:</para>
    /// <list type="bullet">
    /// <item><description>Slower than modern PRNGs; not cryptographically secure.</description></item>
    /// <item><description>Internal advance required after deserialization to sync to generation count.</description></item>
    /// </list>
    /// <para>When to use:</para>
    /// <list type="bullet">
    /// <item><description>Bridging code that uses <c>System.Random</c> to the <see cref="IRandom"/> ecosystem.</description></item>
    /// </list>
    /// <para>When not to use:</para>
    /// <list type="bullet">
    /// <item><description>Performance-critical or quality-sensitive randomness—prefer PCG or IllusionFlow.</description></item>
    /// </list>
    /// </remarks>
    /// <example>
    /// <code>
    /// using WallstopStudios.UnityHelpers.Core.Random;
    ///
    /// var compat = new DotNetRandom(Guid.NewGuid());
    /// // Use IRandom methods while maintaining System.Random semantics
    /// byte b = compat.NextByte();
    /// float f = compat.NextFloat();
    /// </code>
    /// </example>
    [RandomGeneratorMetadata(
        RandomQuality.Poor,
        "Linear congruential generator (mod 2^31) with known correlation failures; unsuitable for high-quality simulations.",
        "System.Random considered harmful",
        "https://nullprogram.com/blog/2017/09/21/"
    )]
    [Serializable]
    [DataContract]
    [ProtoContract(SkipConstructor = true)]
    [WProtoContract(SkipConstructor = true)]
    public sealed partial class DotNetRandom : AbstractRandom
    {
        private const BindingFlags RandomFieldFlags =
            BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly FieldInfo SeedArrayField =
            typeof(Random).GetField("SeedArray", RandomFieldFlags)
            ?? typeof(Random).GetField("_seedArray", RandomFieldFlags);

        private static readonly FieldInfo InextField =
            typeof(Random).GetField("inext", RandomFieldFlags)
            ?? typeof(Random).GetField("_inext", RandomFieldFlags);

        private static readonly FieldInfo InextpField =
            typeof(Random).GetField("inextp", RandomFieldFlags)
            ?? typeof(Random).GetField("_inextp", RandomFieldFlags);

        private static readonly Func<Random, int[]> SeedArrayGetter = TryCreateGetter<int[]>(
            SeedArrayField
        );

        private static readonly FieldSetter<Random, int[]> SeedArraySetter = TryCreateSetter<int[]>(
            SeedArrayField
        );

        private static readonly Func<Random, int> InextGetter = TryCreateGetter<int>(InextField);

        private static readonly FieldSetter<Random, int> InextSetter = TryCreateSetter<int>(
            InextField
        );

        private static readonly Func<Random, int> InextpGetter = TryCreateGetter<int>(InextpField);

        private static readonly FieldSetter<Random, int> InextpSetter = TryCreateSetter<int>(
            InextpField
        );

        private static readonly bool SnapshotSupported =
            SeedArrayField != null && InextField != null && InextpField != null;

        public static DotNetRandom Instance => ThreadLocalRandom<DotNetRandom>.Instance;

        public override RandomState InternalState =>
            BuildState(
                unchecked((ulong)_seed),
                state2: _numberGenerated,
                payload: CaptureSerializedState()
            );

        [ProtoMember(6)]
        [WProtoMember(6)]
        private ulong _numberGenerated;

        [ProtoMember(7)]
        [WProtoMember(7)]
        private int _seed;

        [ProtoMember(8)]
        [WProtoMember(8)]
        [JsonInclude]
        private byte[] SerializedState
        {
            get => CaptureSerializedState();
            set => _pendingStatePayload = value;
        }

        [ProtoIgnore]
        [WProtoIgnore]
        [JsonIgnore]
        private byte[] _pendingStatePayload;

        private Random _random;

        public DotNetRandom()
            : this(Guid.NewGuid()) { }

        public DotNetRandom(Guid guid)
        {
            // Derive a deterministic 32-bit seed from GUID bytes without allocations
            _seed = RandomUtilities.GuidToInt32(guid);
            _random = new Random(_seed);
            _pendingStatePayload = null;
        }

        [JsonConstructor]
        public DotNetRandom(RandomState internalState)
        {
            _seed = unchecked((int)internalState.State1);
            _numberGenerated = internalState.State2;
            _pendingStatePayload = CopyPayload(internalState.PayloadBytes);
            RestoreCommonState(internalState);
            EnsureRandomInitialized();
        }

        [ProtoAfterDeserialization]
        [WProtoAfterDeserialization]
        private void OnProtoDeserialize()
        {
            EnsureRandomInitialized();
        }

        private void EnsureRandomInitialized()
        {
            if (_random != null)
            {
                return;
            }

            _random = new Random(_seed);

            if (_pendingStatePayload != null)
            {
                if (
                    TryDeserializeSnapshot(_pendingStatePayload, out RandomSnapshot snapshot)
                    && TryApplySnapshot(_random, snapshot)
                )
                {
                    _pendingStatePayload = null;
                    return;
                }

                // Snapshot could not be applied (e.g., runtime no longer exposes the fields);
                // fall back to deterministic replay and drop the stale payload.
                _pendingStatePayload = null;
            }

            for (ulong i = 0; i < _numberGenerated; ++i)
            {
                _ = _random.Next(int.MinValue, int.MaxValue);
            }
        }

        public override uint NextUint()
        {
            EnsureRandomInitialized();
            ++_numberGenerated;
            return unchecked((uint)_random.Next(int.MinValue, int.MaxValue));
        }

        public override IRandom Copy()
        {
            return new DotNetRandom(InternalState);
        }

        private byte[] CaptureSerializedState()
        {
            EnsureRandomInitialized();
            if (!TryCaptureSnapshot(_random, out RandomSnapshot snapshot))
            {
                return null;
            }

            return SerializeSnapshot(snapshot);
        }

        private static byte[] CopyPayload(IReadOnlyList<byte> payload)
        {
            if (payload == null || payload.Count == 0)
            {
                return null;
            }

            byte[] buffer = new byte[payload.Count];
            for (int i = 0; i < payload.Count; ++i)
            {
                buffer[i] = payload[i];
            }
            return buffer;
        }

        private static bool TryCaptureSnapshot(Random random, out RandomSnapshot snapshot)
        {
            if (!SnapshotSupported || random == null)
            {
                snapshot = default;
                return false;
            }

            try
            {
                int[] seedArray =
                    SeedArrayGetter != null
                        ? SeedArrayGetter(random)
                        : (int[])SeedArrayField.GetValue(random);
                if (seedArray == null)
                {
                    snapshot = default;
                    return false;
                }

                int[] seedClone = new int[seedArray.Length];
                Array.Copy(seedArray, seedClone, seedClone.Length);

                int inext =
                    InextGetter != null ? InextGetter(random) : (int)InextField.GetValue(random);
                int inextp =
                    InextpGetter != null ? InextpGetter(random) : (int)InextpField.GetValue(random);
                snapshot = new RandomSnapshot(inext, inextp, seedClone);
                return true;
            }
            catch
            {
                snapshot = default;
                return false;
            }
        }

        private static Func<Random, T> TryCreateGetter<T>(FieldInfo field)
        {
            if (field == null)
            {
                return null;
            }

            try
            {
                return ReflectionHelpers.GetFieldGetter<Random, T>(field);
            }
            catch
            {
                return null;
            }
        }

        private static FieldSetter<Random, T> TryCreateSetter<T>(FieldInfo field)
        {
            if (field == null)
            {
                return null;
            }

            try
            {
                return ReflectionHelpers.GetFieldSetter<Random, T>(field);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryApplySnapshot(Random random, RandomSnapshot snapshot)
        {
            if (!SnapshotSupported || random == null || snapshot.SeedArray == null)
            {
                return false;
            }

            try
            {
                int[] seedClone = new int[snapshot.SeedArray.Length];
                Array.Copy(snapshot.SeedArray, seedClone, seedClone.Length);

                bool seedApplied = false;
                if (SeedArraySetter != null)
                {
                    Random instance = random;
                    SeedArraySetter(ref instance, seedClone);
                    seedApplied = true;
                }
                else
                {
                    SeedArrayField.SetValue(random, seedClone);
                    seedApplied = true;
                }

                if (!seedApplied)
                {
                    return false;
                }

                if (InextSetter != null)
                {
                    Random instance = random;
                    InextSetter(ref instance, snapshot.Inext);
                }
                else
                {
                    InextField.SetValue(random, snapshot.Inext);
                }

                if (InextpSetter != null)
                {
                    Random instance = random;
                    InextpSetter(ref instance, snapshot.Inextp);
                }
                else
                {
                    InextpField.SetValue(random, snapshot.Inextp);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] SerializeSnapshot(RandomSnapshot snapshot)
        {
            if (snapshot.SeedArray == null)
            {
                return null;
            }

            int length = snapshot.SeedArray.Length;
            byte[] buffer = new byte[12 + length * sizeof(int)];
            Span<byte> span = buffer.AsSpan();
            BinaryPrimitives.WriteInt32LittleEndian(span, length);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), snapshot.Inext);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(8), snapshot.Inextp);

            Span<byte> seedSpan = span.Slice(12);
            for (int i = 0; i < length; ++i)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    seedSpan.Slice(i * sizeof(int)),
                    snapshot.SeedArray[i]
                );
            }

            return buffer;
        }

        private static bool TryDeserializeSnapshot(
            IReadOnlyList<byte> payload,
            out RandomSnapshot snapshot
        )
        {
            if (payload == null || payload.Count < 12)
            {
                snapshot = default;
                return false;
            }

            Span<byte> header = stackalloc byte[12];
            for (int i = 0; i < 12; ++i)
            {
                header[i] = payload[i];
            }

            int length = BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length <= 0)
            {
                snapshot = default;
                return false;
            }

            int expectedBytes = 12 + length * sizeof(int);
            if (payload.Count < expectedBytes)
            {
                snapshot = default;
                return false;
            }

            int inext = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4));
            int inextp = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(8));

            int[] seedArray = new int[length];
            for (int i = 0; i < length; ++i)
            {
                Span<byte> temp = stackalloc byte[sizeof(int)];
                int offset = 12 + i * sizeof(int);
                for (int j = 0; j < sizeof(int); ++j)
                {
                    temp[j] = payload[offset + j];
                }
                seedArray[i] = BinaryPrimitives.ReadInt32LittleEndian(temp);
            }

            snapshot = new RandomSnapshot(inext, inextp, seedArray);
            return true;
        }

        private readonly struct RandomSnapshot
        {
            public RandomSnapshot(int inext, int inextp, int[] seedArray)
            {
                Inext = inext;
                Inextp = inextp;
                SeedArray = seedArray;
            }

            public int Inext { get; }

            public int Inextp { get; }

            public int[] SeedArray { get; }
        }
    }
}
