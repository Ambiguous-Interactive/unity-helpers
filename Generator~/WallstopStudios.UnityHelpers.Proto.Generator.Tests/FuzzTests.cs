// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Reads hostile bytes into every member shape and asserts what a shipped player needs (#437).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A save file is attacker-controlled input, and so is anything that arrives over a socket. The
    /// property under test is therefore not "the right value comes back" -- for these payloads there
    /// is no right value -- but that a decode <b>terminates, allocates in proportion to what it was
    /// given, and reports failure instead of throwing</b>. Every defect this file exists to catch has
    /// already happened once here: a rank-three dimension header wrapped to zero and asked for 8 GB
    /// from sixteen bytes (#434), a map with an omitted string key threw
    /// <c>ArgumentNullException</c> from inside <c>Dictionary</c> (#387), and protobuf-net itself
    /// takes the process down with a stack overflow that cannot be caught, on a payload naming two
    /// sibling subtypes (#390).
    /// </para>
    /// <para>
    /// Every run is <b>reproducible</b>: each case is a fixed seed, and a failure prints the seed,
    /// the strategy, the iteration and the payload hex, which is a <c>[TestCase]</c> for
    /// <see cref="AKnownHostilePayloadIsStillRefusedCleanly"/>. Randomness with no seed would make a
    /// finding a story rather than a test.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class FuzzTests
    {
        /// <summary>
        /// Iterations per target per strategy. Raise it locally with <c>WPROTO_FUZZ_ITERATIONS</c>;
        /// CI pays a fixed, small cost every run rather than an occasional large one.
        /// </summary>
        private static readonly int Iterations = ResolveIterations();

        /// <summary>
        /// A decode may allocate the graph it returns and nothing else of consequence. The multiple
        /// is deliberately loose -- a dictionary slot costs many times the bytes that describe it --
        /// because the failure this catches is not overhead, it is a payload that names a size
        /// nothing in it pays for. The header that wrapped to zero asked for 8 GB from sixteen
        /// bytes, which is five hundred million times its own length.
        /// </summary>
        private const long AllocationSlackBytes = 128 * 1024;

        private const long AllocationMultiple = 256;

        private static int ResolveIterations()
        {
            string configured = Environment.GetEnvironmentVariable("WPROTO_FUZZ_ITERATIONS");
            if (
                !string.IsNullOrEmpty(configured)
                && int.TryParse(configured, out int parsed)
                && parsed > 0
            )
            {
                return parsed;
            }

            return 1500;
        }

        /// <summary>
        /// Decodes a payload, reporting acceptance and how many bytes the reader consumed before it
        /// stopped. Must never throw.
        /// </summary>
        private delegate bool ReadPayload(byte[] payload, out int consumed);

        /// <summary>
        /// Decodes, encodes, decodes and encodes again, reporting the last two encodings. Returns
        /// <c>false</c> when any step declines.
        /// </summary>
        private delegate bool ReEncodePayload(byte[] payload, out byte[] first, out byte[] second);

        /// <summary>
        /// A decode target: the seeds a mutator starts from, and the reads under test.
        /// </summary>
        private sealed class Target
        {
            internal Target(
                string name,
                IReadOnlyList<byte[]> seeds,
                ReadPayload read,
                ReEncodePayload reEncode
            )
            {
                Name = name;
                Seeds = seeds;
                Read = read;
                ReEncode = reEncode;
            }

            internal string Name { get; }

            internal IReadOnlyList<byte[]> Seeds { get; }

            internal ReadPayload Read { get; }

            internal ReEncodePayload ReEncode { get; }
        }

        private static byte[] Encode<T>(IWProtoFormatter<T> formatter, T value)
        {
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            return formatter.Write(ref writer, value) ? buffer : null;
        }

        private static byte[] Encode<T>(T value)
        {
            byte[] encoded = Encode(WProtoFormatterProvider.Get<T>(), value);
            Assert.IsNotNull(encoded, "a seed value failed to encode");
            return encoded;
        }

        private static Target For<T>(string name, params T[] samples)
        {
            // An all-default contract encodes to ZERO bytes, because every member equals its
            // default and protobuf omits those. Such a seed is not a mutation seed at all -- there
            // is nothing to corrupt, so every "mutation" of it is a handful of random bytes, which
            // is the strategy above wearing this one's name. Measured: keeping them held the
            // scalar corpus at 49.7% reaching a member reader, and dropping them takes it past 90%.
            List<byte[]> seeds = new List<byte[]>(samples.Length);
            foreach (T sample in samples)
            {
                byte[] encoded = Encode(sample);
                if (encoded.Length > 0)
                {
                    seeds.Add(encoded);
                }
            }

            Assert.IsNotEmpty(
                seeds,
                $"{name}: every sample encoded to nothing, so this target has no mutation corpus."
            );

            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            return new Target(
                name,
                seeds,
                (byte[] payload, out int consumed) =>
                {
                    WProtoReader reader = new WProtoReader(payload);
                    bool accepted = formatter.TryRead(ref reader, out T _);
                    consumed = reader.Position;
                    return accepted;
                },
                (byte[] payload, out byte[] first, out byte[] second) =>
                {
                    first = null;
                    second = null;
                    WProtoReader reader = new WProtoReader(payload);
                    if (!formatter.TryRead(ref reader, out T decoded))
                    {
                        return false;
                    }

                    first = Encode(formatter, decoded);
                    if (first == null)
                    {
                        return false;
                    }

                    WProtoReader again = new WProtoReader(first);
                    if (!formatter.TryRead(ref again, out T reDecoded))
                    {
                        return false;
                    }

                    second = Encode(formatter, reDecoded);
                    return second != null;
                }
            );
        }

        /// <summary>
        /// One target per member shape the generator emits, because each has its own reader: a
        /// packed run, a length-delimited run, a map entry, an include chain and a dimension header
        /// share almost no code, and a corpus that exercises one proves nothing about the others.
        /// </summary>
        private static IReadOnlyList<Target> Targets()
        {
            return new List<Target>
            {
                For(
                    "scalars",
                    new ScalarContract(),
                    new ScalarContract
                    {
                        Int32 = -1,
                        Int64 = long.MinValue,
                        UInt64 = ulong.MaxValue,
                        Flag = true,
                        Single = 1.5f,
                        Double = -2.5,
                        Text = "seed",
                        Bytes = new byte[] { 1, 2, 3 },
                        MaybeDouble = 0.25,
                        Int16 = -300,
                        Counted = 7,
                    }
                ),
                For(
                    "nested",
                    new NestingContract(),
                    new NestingContract
                    {
                        Id = 3,
                        Child = new HookedContract { Value = 2 },
                        Where = new Outer.Point { X = 1, Y = 2 },
                        MaybeWhere = new Outer.Point { X = 3, Y = 4 },
                    }
                ),
                For(
                    "repeated",
                    new RepeatedContract(),
                    new RepeatedContract
                    {
                        Ints = new[] { 1, 2, 3, -4 },
                        IntList = new List<int> { 5, 6 },
                        Texts = new[] { "a", string.Empty },
                        Doubles = new[] { 0.5, -1.5 },
                        Longs = new ulong[] { 1, ulong.MaxValue },
                        Flags = new[] { true, false },
                        Points = new[]
                        {
                            new Outer.Point { X = 1, Y = 2 },
                        },
                        Messages = new[] { new EmptyContract() },
                        Blobs = new[] { new byte[] { 9 }, Array.Empty<byte>() },
                        PointList = new List<Outer.Point>
                        {
                            new Outer.Point { X = 7, Y = 8 },
                        },
                        Shorts = new short[] { -1, 300 },
                    }
                ),
                For(
                    "polymorphic",
                    new IncludeHolder(),
                    new IncludeHolder
                    {
                        Value = new IncludeGamma { GammaOnly = true, Label = "g" },
                        Trailer = 4,
                    }
                ),
                For(
                    "rectangular",
                    new RectangularArrayContract(),
                    new RectangularArrayContract
                    {
                        Grid = new[,]
                        {
                            { 1, 2 },
                            { 3, 4 },
                        },
                        Volume = new int[1, 1, 2],
                        Labels = new[,]
                        {
                            { "a", "b" },
                        },
                        Points = new[,]
                        {
                            {
                                new Outer.Point { X = 1, Y = 1 },
                            },
                        },
                        Frames = new List<int[,]>
                        {
                            new[,]
                            {
                                { 5 },
                            },
                        },
                    }
                ),
            };
        }

        private static string Hex(byte[] payload)
        {
            StringBuilder text = new StringBuilder(payload.Length * 2);
            foreach (byte value in payload)
            {
                text.Append(value.ToString("X2"));
            }

            return text.ToString();
        }

        private static byte[] FromHex(string hex)
        {
            byte[] payload = new byte[hex.Length / 2];
            for (int index = 0; index < payload.Length; ++index)
            {
                payload[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return payload;
        }

        /// <summary>
        /// A seeded generator, so a finding is a test case rather than an anecdote. Xorshift64* is
        /// used rather than <see cref="Random"/> because its sequence is fixed by this file, and a
        /// runtime that re-tunes its own generator would otherwise silently change the corpus.
        /// </summary>
        private struct FuzzRandom
        {
            private ulong _state;

            internal FuzzRandom(ulong seed)
            {
                _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
            }

            internal ulong Next()
            {
                _state ^= _state >> 12;
                _state ^= _state << 25;
                _state ^= _state >> 27;
                return _state * 0x2545F4914F6CDD1DUL;
            }

            internal int Next(int exclusiveUpperBound)
            {
                return exclusiveUpperBound <= 0 ? 0 : (int)(Next() % (ulong)exclusiveUpperBound);
            }

            internal byte NextByte()
            {
                return (byte)Next();
            }
        }

        /// <summary>
        /// How many payloads a strategy produced, and how many the reader accepted. A fuzzer whose
        /// payloads are all refused by the first tag exercises the tag reader and nothing else, and
        /// it passes exactly as loudly as one that reaches every member. The counts are asserted,
        /// not merely printed.
        /// </summary>
        private sealed class Coverage
        {
            /// <summary>
            /// A payload dying at its first key consumes one or two bytes. Past that, a member
            /// reader ran.
            /// </summary>
            private const int PastTheFirstKey = 4;

            internal int Payloads;

            internal int Accepted;

            internal int Reached;

            internal void Record(bool accepted, int consumed)
            {
                Payloads += 1;
                Accepted += accepted ? 1 : 0;
                Reached += accepted || consumed >= PastTheFirstKey ? 1 : 0;
            }

            internal double AcceptanceRate => Payloads == 0 ? 0 : (double)Accepted / Payloads;

            internal double ReachRate => Payloads == 0 ? 0 : (double)Reached / Payloads;

            internal string Describe(string target)
            {
                return $"{target}: {Payloads} payloads, {Accepted} accepted, {Reached} reaching a "
                    + "member reader";
            }
        }

        /// <summary>
        /// Runs one payload through one target and asserts the three properties a shipped player
        /// depends on.
        /// </summary>
        private static void AssertDecodeIsSafe(
            Target target,
            byte[] payload,
            string origin,
            Coverage coverage = null
        )
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool accepted;
            int consumed;
            try
            {
                accepted = target.Read(payload, out consumed);
            }
            catch (Exception failure)
            {
                Assert.Fail(
                    $"{target.Name}: decoding threw {failure.GetType().Name} instead of reporting "
                        + $"failure.{Environment.NewLine}{origin}{Environment.NewLine}"
                        + $"payload: {Hex(payload)}{Environment.NewLine}{failure}"
                );
                return;
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            long ceiling = AllocationSlackBytes + (AllocationMultiple * payload.Length);
            Assert.LessOrEqual(
                allocated,
                ceiling,
                $"{target.Name}: decoding {payload.Length} bytes allocated {allocated}, above the "
                    + $"{ceiling} a payload of that length can account for. A size a payload STATES "
                    + $"was believed rather than one it DELIVERED.{Environment.NewLine}{origin}"
                    + $"{Environment.NewLine}payload: {Hex(payload)}"
            );

            coverage?.Record(accepted, consumed);

            if (!accepted)
            {
                return;
            }

            // An accepted payload has to be one this serializer can PRODUCE. Encode what was
            // decoded, decode that, and encode again: the last two encodings must be identical.
            // The first is deliberately not compared -- dropping an unknown field is correct, and a
            // packed run this writer normalizes is correct -- but past that first normalization
            // nothing is left to explain a difference, so anything else means the reader accepts a
            // shape the writer cannot express, and a save written from it would not load.
            byte[] first;
            byte[] second;
            try
            {
                if (!target.ReEncode(payload, out first, out second))
                {
                    Assert.Fail(
                        $"{target.Name}: a payload was accepted and then could not be re-encoded "
                            + $"or re-read.{Environment.NewLine}{origin}{Environment.NewLine}"
                            + $"payload: {Hex(payload)}"
                    );
                    return;
                }
            }
            catch (Exception failure)
            {
                Assert.Fail(
                    $"{target.Name}: re-encoding an ACCEPTED payload threw "
                        + $"{failure.GetType().Name}.{Environment.NewLine}{origin}"
                        + $"{Environment.NewLine}payload: {Hex(payload)}{Environment.NewLine}{failure}"
                );
                return;
            }

            Assert.AreEqual(
                Hex(first),
                Hex(second),
                $"{target.Name}: encoding is not a fixed point -- the value decoded from an "
                    + $"accepted payload does not survive its own encoding.{Environment.NewLine}"
                    + $"{origin}{Environment.NewLine}payload: {Hex(payload)}"
            );
        }

        [Test]
        public void RandomBytesAreRefusedRatherThanThrown()
        {
            // The least structured strategy, and the one that reaches the tag reader's own edges:
            // an overlong varint, a reserved wire type, a key naming field zero.
            foreach (Target target in Targets())
            {
                FuzzRandom random = new FuzzRandom(0xC0FFEE);
                for (int iteration = 0; iteration < Iterations; ++iteration)
                {
                    byte[] payload = new byte[random.Next(96)];
                    for (int index = 0; index < payload.Length; ++index)
                    {
                        payload[index] = random.NextByte();
                    }

                    AssertDecodeIsSafe(
                        target,
                        payload,
                        $"strategy=random seed=0xC0FFEE iteration={iteration}"
                    );
                }
            }
        }

        [Test]
        public void AMutatedValidPayloadIsRefusedRatherThanThrown()
        {
            // Random bytes are refused by the first tag more often than not. Mutating a VALID
            // encoding is what reaches the readers underneath -- a length prefix that now overruns
            // its message, a packed run whose element count no longer divides, a map entry missing
            // its value.
            foreach (Target target in Targets())
            {
                Coverage coverage = new Coverage();
                FuzzRandom random = new FuzzRandom(0x5EED);
                for (int iteration = 0; iteration < Iterations; ++iteration)
                {
                    byte[] seed = target.Seeds[random.Next(target.Seeds.Count)];
                    byte[] payload = Mutate(ref random, seed);
                    AssertDecodeIsSafe(
                        target,
                        payload,
                        $"strategy=mutation seed=0x5EED iteration={iteration}",
                        coverage
                    );
                }

                // The evidence that the corpus reaches past the tag reader, measured directly from
                // how far the reader got rather than inferred from acceptance. Acceptance is the
                // wrong proxy here and measuring proved it: a mutated rectangular payload is usually
                // REFUSED, deep, by the dimension header's equality check -- 8% accepted against 96%
                // reaching a member reader. A threshold on acceptance would have demanded the
                // corpus stop exercising the very check #434 added.
                Assert.Greater(
                    coverage.ReachRate,
                    0.5,
                    coverage.Describe(target.Name)
                        + " -- the mutation corpus is dying at the first key, so it has become a "
                        + "slower way of testing TryReadTag."
                );
                Assert.Greater(
                    coverage.Accepted,
                    0,
                    coverage.Describe(target.Name)
                        + " -- no mutated payload survived at all, which no valid seed should allow."
                );
                Assert.Less(
                    coverage.AcceptanceRate,
                    0.99,
                    coverage.Describe(target.Name)
                        + " -- the mutations are barely changing anything."
                );
            }
        }

        [Test]
        public void AHostileFieldSequenceIsRefusedRatherThanThrown()
        {
            // The strategy that matters most, because it is the one that produces CLAIMS: a length
            // prefix of 2^31, a packed run announcing a million elements, a chain of sub-messages
            // deeper than the reader's bound. None of those survive random mutation often enough to
            // be reached by it.
            foreach (Target target in Targets())
            {
                Coverage coverage = new Coverage();
                FuzzRandom random = new FuzzRandom(0xBADF00D);
                for (int iteration = 0; iteration < Iterations; ++iteration)
                {
                    byte[] payload = HostileMessage(ref random, 0);
                    AssertDecodeIsSafe(
                        target,
                        payload,
                        $"strategy=hostile seed=0xBADF00D iteration={iteration}",
                        coverage
                    );
                }

                // These are well-formed KEYS carrying hostile CLAIMS, so a real share has to be
                // accepted -- a message of unknown fields is a valid message -- and most must reach
                // past the first key. Zero of either would mean the generator emits garbage that
                // dies at the first byte and the claims inside it are never evaluated.
                Assert.Greater(
                    coverage.ReachRate,
                    0.5,
                    coverage.Describe(target.Name)
                        + " -- the hostile field sequences are not well-formed enough to reach the "
                        + "claims they carry."
                );
                Assert.Greater(
                    coverage.Accepted,
                    0,
                    coverage.Describe(target.Name)
                        + " -- not one hostile message was accepted, so unknown-field skipping is "
                        + "never exercised."
                );
            }
        }

        private static byte[] Mutate(ref FuzzRandom random, byte[] seed)
        {
            List<byte> payload = new List<byte>(seed);
            int mutations = 1 + random.Next(4);
            for (int mutation = 0; mutation < mutations; ++mutation)
            {
                if (payload.Count == 0)
                {
                    payload.Add(random.NextByte());
                    continue;
                }

                int index = random.Next(payload.Count);
                switch (random.Next(7))
                {
                    case 0:
                        payload[index] = (byte)(payload[index] ^ (1 << random.Next(8)));
                        break;
                    case 1:
                        payload[index] = random.NextByte();
                        break;
                    case 2:
                        payload.RemoveRange(index, payload.Count - index);
                        break;
                    case 3:
                        payload.RemoveAt(index);
                        break;
                    case 4:
                        payload.Insert(index, random.NextByte());
                        break;
                    case 5:
                        // A duplicated run is how a repeated field, a map key or an include tag
                        // arrives twice, which is a different path from a corrupted one.
                        int length = Math.Min(payload.Count - index, 1 + random.Next(8));
                        payload.InsertRange(index, payload.GetRange(index, length));
                        break;
                    default:
                        // Widening a varint in place: the same value, more bytes, which is what a
                        // hand-written encoder produces and a strict reader must decide about.
                        payload[index] = (byte)(payload[index] | 0x80);
                        payload.Insert(index + 1, (byte)random.Next(0x80));
                        break;
                }
            }

            return payload.ToArray();
        }

        private static byte[] HostileMessage(ref FuzzRandom random, int depth)
        {
            List<byte> payload = new List<byte>();
            int fields = 1 + random.Next(4);
            for (int field = 0; field < fields; ++field)
            {
                int fieldNumber = 1 + random.Next(12);
                int wireType = random.Next(8);
                WriteVarint(payload, (ulong)((fieldNumber << 3) | wireType));
                switch (wireType)
                {
                    case 0:
                        WriteVarint(
                            payload,
                            random.Next() % 2 == 0 ? random.Next() : ulong.MaxValue
                        );
                        break;
                    case 1:
                        for (int index = 0; index < 8; ++index)
                        {
                            payload.Add(random.NextByte());
                        }

                        break;
                    case 5:
                        for (int index = 0; index < 4; ++index)
                        {
                            payload.Add(random.NextByte());
                        }

                        break;
                    case 2:
                        WriteLengthDelimited(payload, ref random, depth);
                        break;
                    default:
                        // 3, 4, 6 and 7: group markers and the two reserved codes. A reader that
                        // does not refuse them walks off the end of whatever follows.
                        break;
                }
            }

            return payload.ToArray();
        }

        private static void WriteLengthDelimited(
            List<byte> payload,
            ref FuzzRandom random,
            int depth
        )
        {
            switch (random.Next(4))
            {
                case 0:
                    // The claim with nothing behind it: a length prefix far larger than the bytes
                    // that follow. This is the shape a reader must refuse rather than reserve for.
                    WriteVarint(payload, (ulong)(int.MaxValue - random.Next(1024)));
                    for (int index = 0; index < random.Next(8); ++index)
                    {
                        payload.Add(random.NextByte());
                    }

                    break;
                case 1:
                    // A nested message, one level deeper. Recursion is bounded here at 96 against
                    // the reader's 64 so the bound itself is crossed rather than approached.
                    byte[] nested =
                        depth >= 96 ? Array.Empty<byte>() : HostileMessage(ref random, depth + 1);
                    WriteVarint(payload, (ulong)nested.Length);
                    payload.AddRange(nested);
                    break;
                case 2:
                    // A well-formed prefix around random bytes: what a packed run, a string and a
                    // map entry all look like before they are interpreted.
                    byte[] body = new byte[random.Next(24)];
                    for (int index = 0; index < body.Length; ++index)
                    {
                        body[index] = random.NextByte();
                    }

                    WriteVarint(payload, (ulong)body.Length);
                    payload.AddRange(body);
                    break;
                default:
                    WriteVarint(payload, 0);
                    break;
            }
        }

        private static void WriteVarint(List<byte> payload, ulong value)
        {
            while (value >= 0x80)
            {
                payload.Add((byte)(value | 0x80));
                value >>= 7;
            }

            payload.Add((byte)value);
        }

        [Test]
        public void ADeeplyNestedChainIsRefusedWithoutOverflowingTheStack()
        {
            // The reader's bound is 64 and this is 20,000. Stated separately from the fuzz
            // strategies because a stack overflow cannot be caught: if this regresses, the process
            // dies and no assertion runs, so it is worth being the one test whose failure mode is
            // visible as a missing result rather than a red one.
            byte[] payload = Array.Empty<byte>();
            for (int level = 0; level < 20000; ++level)
            {
                List<byte> wrapped = new List<byte> { 0x0A };
                WriteVarint(wrapped, (ulong)payload.Length);
                wrapped.AddRange(payload);
                payload = wrapped.ToArray();
            }

            foreach (Target target in Targets())
            {
                AssertDecodeIsSafe(target, payload, "strategy=nesting-bomb depth=20000");
            }
        }

        [Test]
        public void APackedRunCannotReserveMoreThanItDelivers()
        {
            // The #434 defect in its general form: a run whose ANNOUNCED length is enormous and
            // whose delivered bytes are not. Reserving from the announcement is how sixteen bytes
            // become an OutOfMemoryException.
            foreach (Target target in Targets())
            {
                foreach (int announced in new[] { 0x7FFFFFFF, 0x40000000, 0x00FFFFFF, 0x0000FFFF })
                {
                    List<byte> payload = new List<byte> { 0x0A };
                    WriteVarint(payload, (ulong)announced);
                    payload.AddRange(new byte[] { 0x01, 0x02, 0x03, 0x04 });
                    AssertDecodeIsSafe(
                        target,
                        payload.ToArray(),
                        $"strategy=oversized-length announced={announced}"
                    );
                }
            }
        }

        /// <summary>
        /// The dimensions a header can claim. Chosen so their products straddle every boundary that
        /// matters: <c>int.MaxValue</c>, 2^32, and 2^64 exactly -- the last because a wrapped
        /// product of exactly zero is the one an attacker steers toward, since it matches an empty
        /// element run and asks for the whole address space.
        /// </summary>
        private static readonly int[] HostileDimensions =
        {
            0,
            1,
            2,
            3,
            46341,
            65536,
            1 << 21,
            1 << 22,
            1 << 30,
            int.MaxValue,
        };

        [Test]
        public void ADimensionHeaderCannotReserveMoreThanItsElementsPayFor()
        {
            // The general form of the #434 defect, which arrived as a rank-three header whose
            // product wrapped to zero. Random mutation will not find this shape -- a wrapper holding
            // a dimension run and an element run is too specific to stumble into -- so it is
            // generated exactly, across ranks one to four and every dimension that straddles a
            // boundary. A header is a CAPACITY CLAIM: the reader may believe it only to the extent
            // the sender paid for it in bytes.
            IReadOnlyList<Target> targets = Targets();
            FuzzRandom random = new FuzzRandom(0xD13E5);
            for (int iteration = 0; iteration < Iterations; ++iteration)
            {
                int rank = 1 + random.Next(4);
                List<byte> dimensions = new List<byte>();
                for (int axis = 0; axis < rank; ++axis)
                {
                    WriteVarint(
                        dimensions,
                        (ulong)HostileDimensions[random.Next(HostileDimensions.Length)]
                    );
                }

                List<byte> elements = new List<byte>();
                int elementCount = random.Next(7);
                for (int element = 0; element < elementCount; ++element)
                {
                    WriteVarint(elements, (ulong)random.Next(128));
                }

                List<byte> wrapper = new List<byte> { 0x0A };
                WriteVarint(wrapper, (ulong)dimensions.Count);
                wrapper.AddRange(dimensions);
                if (elements.Count > 0)
                {
                    wrapper.Add(0x12);
                    WriteVarint(wrapper, (ulong)elements.Count);
                    wrapper.AddRange(elements);
                }

                // Field 1 of the contract, holding that wrapper.
                List<byte> payload = new List<byte> { 0x0A };
                WriteVarint(payload, (ulong)wrapper.Count);
                payload.AddRange(wrapper);

                byte[] bytes = payload.ToArray();
                foreach (Target target in targets)
                {
                    AssertDecodeIsSafe(
                        target,
                        bytes,
                        $"strategy=capacity-claim seed=0xD13E5 iteration={iteration} rank={rank}"
                    );
                }
            }
        }

        /// <summary>
        /// Findings live here, as the hex the fuzzer printed. An empty list is not a placeholder --
        /// it is the record that every strategy above has run clean.
        /// </summary>
        [TestCase("0A", TestName = "AKnownHostilePayloadIsStillRefusedCleanlyForATruncatedPrefix")]
        [TestCase(
            "0AFFFFFFFF0F",
            TestName = "AKnownHostilePayloadIsStillRefusedCleanlyForAnOversizedPrefix"
        )]
        [TestCase(
            "FFFFFFFFFFFFFFFFFFFF7F",
            TestName = "AKnownHostilePayloadIsStillRefusedCleanlyForAnOverlongKey"
        )]
        public void AKnownHostilePayloadIsStillRefusedCleanly(string hex)
        {
            byte[] payload = FromHex(hex);
            foreach (Target target in Targets())
            {
                AssertDecodeIsSafe(target, payload, $"strategy=regression hex={hex}");
            }
        }
    }
}
