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
        private static readonly int Iterations = ResolveIterations(
            Environment.GetEnvironmentVariable("WPROTO_FUZZ_ITERATIONS")
        );

        private const int DefaultIterations = 1500;

        /// <summary>
        /// The smallest corpus the coverage gates below can be asked about. Under it they report a
        /// defect that does not exist -- a member unreached because nothing has generated a payload
        /// for it yet is a statement about the sample size, not about the fuzzer.
        /// </summary>
        /// <remarks>
        /// The environment variable exists to <b>raise</b> the count, so flooring it costs nothing a
        /// caller asked for. Measured on the mutation corpus, which is the gated one: at three
        /// iterations not one mutated payload survived and the suite reported that as a defect.
        /// </remarks>
        private const int MinimumIterations = 200;

        /// <summary>
        /// A decode may allocate the graph it returns and nothing else of consequence.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Both numbers are measured</b>, and the teardown below prints the measurement every run
        /// so the next person to tighten them does not have to trust this sentence. Across every
        /// strategy here, with the readers warmed: the worst allocation is <b>160 KB</b>, by the
        /// 20,000-level nesting bomb on its own 74 KB payload, which is 2.2x its length; and the
        /// worst <b>per-byte</b> cost is <b>152x</b> -- 304 bytes from two, which is one contract
        /// object and nothing else. The fixed term covers the second and the multiple covers the
        /// first, with roughly an order of magnitude over each.
        /// </para>
        /// <para>
        /// The previous 128 KB + 256x was inert: it accepted a 1,300x amplification on a 96-byte
        /// payload, so the only defect it could catch is one that asks for gigabytes. Both that have
        /// actually happened here did -- but a bug that tops out at a megabyte is a denial of
        /// service on a console all the same.
        /// <see cref="TheAllocationCeilingCatchesABoundedAmplification"/> is the proof that the
        /// replacement is not inert either, and it is an assertion rather than an argument.
        /// </para>
        /// </remarks>
        private const long AllocationSlackBytes = 4 * 1024;

        private const long AllocationMultiple = 256;

        /// <summary>
        /// Resolves the iteration count from the environment, never below
        /// <see cref="MinimumIterations"/>.
        /// </summary>
        /// <param name="configured">The raw environment value, which may be absent or nonsense.</param>
        /// <returns>The iteration count every strategy runs.</returns>
        internal static int ResolveIterations(string configured)
        {
            if (
                !string.IsNullOrEmpty(configured)
                && int.TryParse(configured, out int parsed)
                && parsed > 0
            )
            {
                return parsed < MinimumIterations ? MinimumIterations : parsed;
            }

            return DefaultIterations;
        }

        [TestCase(null, ExpectedResult = DefaultIterations)]
        [TestCase("", ExpectedResult = DefaultIterations)]
        [TestCase("0", ExpectedResult = DefaultIterations)]
        [TestCase("-1", ExpectedResult = DefaultIterations)]
        [TestCase("2.5", ExpectedResult = DefaultIterations)]
        [TestCase("many", ExpectedResult = DefaultIterations)]
        [TestCase("3", ExpectedResult = MinimumIterations)]
        [TestCase("200", ExpectedResult = MinimumIterations)]
        [TestCase("5000", ExpectedResult = 5000)]
        public int TheIterationCountNeverFallsBelowWhatTheGatesCanBeAskedAbout(string configured)
        {
            // A count of 3 turned this suite red on a coverage gate, naming a defect that does not
            // exist -- the corpus had not had a chance to reach the member, and the assertion said
            // the fuzzer had stopped covering it.
            return ResolveIterations(configured);
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
        /// Decodes through <see cref="WProtoFacade"/> -- the entry point a shipped game reaches
        /// through <c>Serializer.ProtoDeserialize</c>. Unlike <see cref="ReadPayload"/> this one is
        /// expected to <b>throw</b> on a payload it refuses, deliberately, so what it throws is the
        /// contract under test.
        /// </summary>
        private delegate bool FacadeReadPayload(byte[] payload);

        /// <summary>
        /// Builds a random value of the contract's type. The counterpart to a mutator: the read
        /// strategies cover payloads an attacker constructs, this covers values a <b>game</b>
        /// constructs, which is the only way a <c>Measure</c>/<c>Write</c> disagreement on a shape
        /// no decode produces can be seen.
        /// </summary>
        private delegate T ValueFactory<out T>(ref FuzzRandom random);

        /// <summary>
        /// Builds a random value, measures it, and writes it into a buffer of exactly that many
        /// bytes. Reports what <c>Measure</c> promised, what <c>Write</c> consumed, and the bytes.
        /// </summary>
        private delegate bool WriteGeneratedValue(
            ref FuzzRandom random,
            out int measured,
            out int written,
            out byte[] encoded
        );

        /// <summary>
        /// A decode target: the seeds a mutator starts from, and the reads under test.
        /// </summary>
        private sealed class Target
        {
            internal Target(
                string name,
                IReadOnlyList<byte[]> seeds,
                IReadOnlyList<int> members,
                ReadPayload read,
                ReEncodePayload reEncode,
                FacadeReadPayload facadeRead,
                WriteGeneratedValue writeGenerated
            )
            {
                Name = name;
                Seeds = seeds;
                Members = members;
                Read = read;
                ReEncode = reEncode;
                FacadeRead = facadeRead;
                WriteGenerated = writeGenerated;
            }

            internal string Name { get; }

            internal IReadOnlyList<byte[]> Seeds { get; }

            /// <summary>
            /// The field numbers this contract's members occupy, read out of the seeds rather than
            /// declared here. A member added to a contract and set in its sample is covered by the
            /// gate automatically; a list written down beside it would have to be remembered.
            /// </summary>
            internal IReadOnlyList<int> Members { get; }

            internal ReadPayload Read { get; }

            internal ReEncodePayload ReEncode { get; }

            internal FacadeReadPayload FacadeRead { get; }

            internal WriteGeneratedValue WriteGenerated { get; }
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

        private static Target For<T>(string name, ValueFactory<T> factory, params T[] samples)
        {
            // An all-default contract encodes to ZERO bytes, because every member equals its
            // default and protobuf omits those. Such a seed is not a mutation seed at all -- there
            // is nothing to corrupt, so every "mutation" of it is a handful of random bytes, which
            // is the strategy above wearing this one's name. Measured: keeping them held the scalar
            // corpus at 49.7% reaching a member reader -- below the gate below, which is how it was
            // found -- and dropping them clears it comfortably.
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

            SortedSet<int> members = new SortedSet<int>();
            foreach (byte[] seed in seeds)
            {
                ScanTopLevelFields(seed, seed.Length, members);
            }

            Assert.IsNotEmpty(
                members,
                $"{name}: no member field was found in any seed, so the per-member coverage gate "
                    + "would be vacuous."
            );

            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();

            // The facade answers only for a type it has a formatter for, and that is a property of
            // the target rather than of a payload -- so it is asked once, here, where "the facade
            // does not serve this contract at all" reads as itself instead of as every hostile
            // payload mysteriously declining.
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(new ReadOnlySpan<byte>(seeds[0]), out T _),
                $"{name}: the facade does not serve this contract, so driving hostile bytes through "
                    + "it would prove nothing."
            );

            return new Target(
                name,
                seeds,
                new List<int>(members),
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
                },
                payload => WProtoFacade.TryDeserialize(new ReadOnlySpan<byte>(payload), out T _),
                (ref FuzzRandom random, out int measured, out int written, out byte[] encoded) =>
                {
                    T value = factory(ref random);
                    measured = formatter.Measure(value);
                    encoded = new byte[measured];
                    WProtoWriter writer = new WProtoWriter(encoded);
                    bool wrote = formatter.Write(ref writer, value);
                    written = writer.Position;
                    return wrote && !writer.Faulted;
                }
            );
        }

        /// <summary>
        /// Records the field numbers a payload presents at its top level, up to
        /// <paramref name="limit"/> bytes in.
        /// </summary>
        /// <param name="payload">The bytes to walk.</param>
        /// <param name="limit">
        /// How far the reader got. A key that ends at or before this offset is one the reader
        /// dispatched on, which is the question a per-member coverage claim is really asking.
        /// </param>
        /// <param name="fields">Receives the field numbers found.</param>
        /// <remarks>
        /// A deliberately independent walk rather than a hook inside the reader: instrumenting the
        /// thing under test to report on itself would let a reader that silently skipped a member
        /// look covered.
        /// </remarks>
        private static void ScanTopLevelFields(byte[] payload, int limit, ISet<int> fields)
        {
            int offset = 0;
            while (offset < payload.Length)
            {
                if (!TryScanVarint(payload, ref offset, out ulong key))
                {
                    return;
                }

                int field = (int)(key >> 3);
                int wireType = (int)(key & 7);
                if (field <= 0)
                {
                    return;
                }

                if (offset <= limit)
                {
                    fields.Add(field);
                }

                switch (wireType)
                {
                    case 0:
                        if (!TryScanVarint(payload, ref offset, out ulong _))
                        {
                            return;
                        }

                        break;
                    case 1:
                        offset += 8;
                        break;
                    case 5:
                        offset += 4;
                        break;
                    case 2:
                        if (!TryScanVarint(payload, ref offset, out ulong length))
                        {
                            return;
                        }

                        if (length > (ulong)(payload.Length - offset))
                        {
                            return;
                        }

                        offset += (int)length;
                        break;
                    default:
                        return;
                }

                if (offset > payload.Length)
                {
                    return;
                }
            }
        }

        private static bool TryScanVarint(byte[] payload, ref int offset, out ulong value)
        {
            value = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                if (offset >= payload.Length)
                {
                    return false;
                }

                byte current = payload[offset++];
                value |= (ulong)(current & 0x7F) << shift;
                if ((current & 0x80) == 0)
                {
                    return true;
                }
            }

            return false;
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
                    RandomScalar,
                    new ScalarContract(),
                    // Every member is set, because the seeds are what the per-member coverage gate
                    // reads its expectations out of: a member left at its default is omitted from
                    // the wire, so leaving one unset here quietly excuses the corpus from covering
                    // it.
                    new ScalarContract
                    {
                        Int32 = -1,
                        Int64 = long.MinValue,
                        UInt32 = 11,
                        UInt64 = ulong.MaxValue,
                        Flag = true,
                        Single = 1.5f,
                        Double = -2.5,
                        Text = "seed",
                        Bytes = new byte[] { 1, 2, 3 },
                        Enum = Mode.Careful,
                        MaybeDouble = 0.25,
                        Int16 = -300,
                        Hidden = 5,
                        Counted = 7,
                    }
                ),
                For(
                    "nested",
                    RandomNesting,
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
                    RandomRepeated,
                    new RepeatedContract(),
                    new RepeatedContract
                    {
                        Ints = new[] { 1, 2, 3, -4 },
                        IntList = new List<int> { 5, 6 },
                        Texts = new[] { "a", string.Empty },
                        Doubles = new[] { 0.5, -1.5 },
                        Longs = new ulong[] { 1, ulong.MaxValue },
                        Flags = new[] { true, false },
                        Modes = new[] { Mode.Fast, Mode.Careful },
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
                    RandomInclude,
                    new IncludeHolder(),
                    new IncludeHolder
                    {
                        Value = new IncludeGamma { GammaOnly = true, Label = "g" },
                        Trailer = 4,
                    }
                ),
                For(
                    "rectangular",
                    RandomRectangular,
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
                        Layers = new[]
                        {
                            new[,]
                            {
                                { 1, 2 },
                            },
                        },
                        Frames = new List<int[,]>
                        {
                            new[,]
                            {
                                { 5 },
                            },
                        },
                        Named = new Dictionary<string, int[,]>
                        {
                            {
                                "k",
                                new[,]
                                {
                                    { 7 },
                                }
                            },
                        },
                        Rows = new int[,][]
                        {
                            { new[] { 1, 2 } },
                        },
                        Blobs = new byte[,]
                        {
                            { 1, 2 },
                        },
                    }
                ),
            };
        }

        /// <summary>
        /// A string with every UTF-8 width in it, plus the one shape an encoder can disagree with
        /// itself about: a <b>lone surrogate</b>, which has no encoding and is replaced. That is the
        /// case where <c>Measure</c> (<c>GetByteCount</c>) and <c>Write</c> (<c>GetBytes</c>) could
        /// promise different lengths, which is half the reason to fuzz the writer at all.
        /// </summary>
        private static string RandomText(ref FuzzRandom random)
        {
            switch (random.Next(6))
            {
                case 0:
                    return null;
                case 1:
                    return string.Empty;
                default:
                    int length = random.Next(12);
                    char[] text = new char[length];
                    for (int index = 0; index < length; ++index)
                    {
                        switch (random.Next(4))
                        {
                            case 0:
                                text[index] = (char)(0x20 + random.Next(0x5F));
                                break;
                            case 1:
                                text[index] = (char)(0x80 + random.Next(0x700));
                                break;
                            case 2:
                                text[index] = (char)(0x0800 + random.Next(0x800));
                                break;
                            default:
                                text[index] = (char)(0xD800 + random.Next(0x800));
                                break;
                        }
                    }

                    return new string(text);
            }
        }

        private static byte[] RandomBytes(ref FuzzRandom random)
        {
            switch (random.Next(6))
            {
                case 0:
                    return null;
                case 1:
                    return Array.Empty<byte>();
                default:
                    byte[] value = new byte[random.Next(16)];
                    for (int index = 0; index < value.Length; ++index)
                    {
                        value[index] = random.NextByte();
                    }

                    return value;
            }
        }

        /// <summary>
        /// Null, empty, or one to four elements -- but never a <b>null element</b>, which has no
        /// encoding at all and is a documented throw rather than a decode failure.
        /// </summary>
        private static T[] RandomArray<T>(ref FuzzRandom random, ValueFactory<T> element)
        {
            switch (random.Next(6))
            {
                case 0:
                    return null;
                case 1:
                    return Array.Empty<T>();
                default:
                    T[] values = new T[1 + random.Next(4)];
                    for (int index = 0; index < values.Length; ++index)
                    {
                        values[index] = element(ref random);
                    }

                    return values;
            }
        }

        private static List<T> RandomList<T>(ref FuzzRandom random, ValueFactory<T> element)
        {
            T[] values = RandomArray(ref random, element);
            return values == null ? null : new List<T>(values);
        }

        private static int RandomInt32(ref FuzzRandom random)
        {
            return (int)random.Next();
        }

        private static double RandomDouble(ref FuzzRandom random)
        {
            return BitConverter.Int64BitsToDouble((long)random.Next());
        }

        private static Mode RandomMode(ref FuzzRandom random)
        {
            switch (random.Next(3))
            {
                case 0:
                    return Mode.None;
                case 1:
                    return Mode.Fast;
                default:
                    return Mode.Careful;
            }
        }

        private static Outer.Point RandomPoint(ref FuzzRandom random)
        {
            return new Outer.Point { X = (int)random.Next(), Y = (int)random.Next() };
        }

        private static string RandomNonNullText(ref FuzzRandom random)
        {
            return RandomText(ref random) ?? string.Empty;
        }

        private static byte[] RandomNonNullBytes(ref FuzzRandom random)
        {
            return RandomBytes(ref random) ?? Array.Empty<byte>();
        }

        private static EmptyContract RandomEmpty(ref FuzzRandom random)
        {
            _ = random.Next();
            return new EmptyContract();
        }

        private static ulong RandomUInt64(ref FuzzRandom random)
        {
            return random.Next();
        }

        private static bool RandomBool(ref FuzzRandom random)
        {
            return random.Next(2) == 0;
        }

        private static short RandomInt16(ref FuzzRandom random)
        {
            return (short)random.Next();
        }

        private static ScalarContract RandomScalar(ref FuzzRandom random)
        {
            return new ScalarContract
            {
                Int32 = (int)random.Next(),
                Int64 = (long)random.Next(),
                UInt32 = (uint)random.Next(),
                UInt64 = random.Next(),
                Flag = random.Next(2) == 0,
                // Reconstituted from bits rather than sampled from a range, so NaN, the infinities
                // and every subnormal are in the corpus.
                Single = BitConverter.Int32BitsToSingle((int)random.Next()),
                Double = RandomDouble(ref random),
                Text = RandomText(ref random),
                Bytes = RandomBytes(ref random),
                Enum = RandomMode(ref random),
                MaybeDouble = random.Next(4) == 0 ? (double?)null : RandomDouble(ref random),
                Int16 = (short)random.Next(),
                Hidden = (int)random.Next(),
                Counted = (int)random.Next(),
            };
        }

        private static NestingContract RandomNesting(ref FuzzRandom random)
        {
            return new NestingContract
            {
                Id = (int)random.Next(),
                Child =
                    random.Next(4) == 0 ? null : new HookedContract { Value = (int)random.Next() },
                Where = RandomPoint(ref random),
                MaybeWhere = random.Next(4) == 0 ? (Outer.Point?)null : RandomPoint(ref random),
            };
        }

        private static RepeatedContract RandomRepeated(ref FuzzRandom random)
        {
            return new RepeatedContract
            {
                Ints = RandomArray<int>(ref random, RandomInt32),
                IntList = RandomList<int>(ref random, RandomInt32),
                Texts = RandomArray<string>(ref random, RandomNonNullText),
                Doubles = RandomArray<double>(ref random, RandomDouble),
                Longs = RandomArray<ulong>(ref random, RandomUInt64),
                Flags = RandomArray<bool>(ref random, RandomBool),
                Modes = RandomArray<Mode>(ref random, RandomMode),
                Points = RandomArray<Outer.Point>(ref random, RandomPoint),
                Messages = RandomArray<EmptyContract>(ref random, RandomEmpty),
                Blobs = RandomArray<byte[]>(ref random, RandomNonNullBytes),
                PointList = RandomList<Outer.Point>(ref random, RandomPoint),
                Shorts = RandomArray<short>(ref random, RandomInt16),
            };
        }

        private static IncludeHolder RandomInclude(ref FuzzRandom random)
        {
            IncludeBase value;
            switch (random.Next(5))
            {
                case 0:
                    value = null;
                    break;
                case 1:
                    value = new IncludeBase();
                    break;
                case 2:
                    value = new IncludeAlpha { AlphaOnly = (int)random.Next() };
                    break;
                case 3:
                    value = new IncludeBeta { BetaOnly = RandomDouble(ref random) };
                    break;
                default:
                    value = new IncludeGamma { GammaOnly = random.Next(2) == 0 };
                    break;
            }

            if (value != null)
            {
                value.Id = (int)random.Next();
                value.Label = RandomText(ref random);
            }

            return new IncludeHolder { Value = value, Trailer = (int)random.Next() };
        }

        /// <summary>
        /// Axes are kept to 0-2 deliberately: a zero axis is the shape whose product annihilates
        /// every other axis, and it is legal to <b>write</b>, so it belongs in the generated corpus
        /// that has to survive its own encoding.
        /// </summary>
        private static int[,] RandomGrid(ref FuzzRandom random)
        {
            if (random.Next(5) == 0)
            {
                return null;
            }

            int[,] grid = new int[random.Next(3), random.Next(3)];
            for (int row = 0; row < grid.GetLength(0); ++row)
            {
                for (int column = 0; column < grid.GetLength(1); ++column)
                {
                    grid[row, column] = (int)random.Next();
                }
            }

            return grid;
        }

        private static int[,] RandomNonNullGrid(ref FuzzRandom random)
        {
            return RandomGrid(ref random) ?? new int[0, 0];
        }

        private static RectangularArrayContract RandomRectangular(ref FuzzRandom random)
        {
            RectangularArrayContract value = new RectangularArrayContract
            {
                Grid = RandomGrid(ref random),
                Labels = random.Next(4) == 0 ? null : new string[random.Next(3), random.Next(3)],
                Layers = RandomArray<int[,]>(ref random, RandomNonNullGrid),
                Frames = RandomList<int[,]>(ref random, RandomNonNullGrid),
            };

            if (random.Next(4) != 0)
            {
                value.Volume = new int[random.Next(3), random.Next(3), random.Next(3)];
            }

            if (random.Next(4) != 0)
            {
                value.Points = new Outer.Point[random.Next(3), random.Next(3)];
            }

            if (random.Next(4) != 0)
            {
                value.Blobs = new byte[random.Next(3), random.Next(3)];
            }

            if (random.Next(4) != 0)
            {
                value.Rows = new int[random.Next(2), random.Next(2)][];
                for (int row = 0; row < value.Rows.GetLength(0); ++row)
                {
                    for (int column = 0; column < value.Rows.GetLength(1); ++column)
                    {
                        value.Rows[row, column] = new int[random.Next(3)];
                    }
                }
            }

            if (random.Next(4) != 0)
            {
                value.Named = new Dictionary<string, int[,]>
                {
                    { "k", RandomNonNullGrid(ref random) },
                };
            }

            if (value.Labels != null)
            {
                for (int row = 0; row < value.Labels.GetLength(0); ++row)
                {
                    for (int column = 0; column < value.Labels.GetLength(1); ++column)
                    {
                        value.Labels[row, column] = RandomNonNullText(ref random);
                    }
                }
            }

            return value;
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
            /// reader ran. Read from the reader's Position AFTER a failure, which is where it
            /// stopped rather than what it successfully decoded -- that is the question here, and
            /// an unknown field skipped along the way counts as reached because the skip is code
            /// under test too.
            /// </summary>
            private const int PastTheFirstKey = 4;

            internal int Payloads;

            internal int Accepted;

            internal int Reached;

            /// <summary>
            /// How many payloads got as far as dispatching on each field number. The corpus-wide
            /// reach rate cannot answer this: a strategy that reaches the tag reader on every
            /// payload and then always dies on field 1 scores 100% while nine members are untouched.
            /// </summary>
            private readonly Dictionary<int, int> _byMember = new Dictionary<int, int>();

            internal void Record(bool accepted, int consumed, byte[] payload)
            {
                Payloads += 1;
                Accepted += accepted ? 1 : 0;
                Reached += accepted || consumed >= PastTheFirstKey ? 1 : 0;

                SortedSet<int> fields = new SortedSet<int>();
                ScanTopLevelFields(payload, accepted ? payload.Length : consumed, fields);
                foreach (int field in fields)
                {
                    _byMember.TryGetValue(field, out int hits);
                    _byMember[field] = hits + 1;
                }
            }

            internal int HitsFor(int member)
            {
                _byMember.TryGetValue(member, out int hits);
                return hits;
            }

            internal double AcceptanceRate => Payloads == 0 ? 0 : (double)Accepted / Payloads;

            internal double ReachRate => Payloads == 0 ? 0 : (double)Reached / Payloads;

            internal string Describe(string target)
            {
                return $"{target}: {Payloads} payloads, {Accepted} accepted, {Reached} reaching a "
                    + "member reader";
            }

            internal string DescribeMembers(string target, IReadOnlyList<int> members)
            {
                StringBuilder text = new StringBuilder(Describe(target));
                text.Append("; per member:");
                foreach (int member in members)
                {
                    text.Append(' ').Append(member).Append('=').Append(HitsFor(member));
                }

                return text.ToString();
            }
        }

        /// <summary>
        /// The largest allocation any decode in this run charged, and the payload that charged it.
        /// Printed once at the end so the next person to tighten <see cref="AllocationSlackBytes"/>
        /// starts from a measurement rather than from a sentence.
        /// </summary>
        private static long _worstAllocation;

        private static int _worstAllocationPayloadLength;

        private static string _worstAllocationTarget = "none";

        private static double _worstAllocationRatio;

        private static long _worstRatioAllocation;

        private static int _worstRatioPayloadLength;

        private static string _worstRatioTarget = "none";

        [OneTimeSetUp]
        public void WarmEveryReaderBeforeAnythingIsMeasured()
        {
            // A first decode pays for type initialization, cached lookups and whatever else the
            // runtime builds on the way -- none of which is the amplification this suite exists to
            // catch, and all of which lands on iteration zero. Warming here is what lets the ceiling
            // above be tight enough to mean something instead of loose enough to swallow the first
            // call.
            foreach (Target target in Targets())
            {
                foreach (byte[] seed in target.Seeds)
                {
                    target.Read(seed, out int _);
                    target.ReEncode(seed, out byte[] _, out byte[] _);
                    target.FacadeRead(seed);
                }

                target.Read(Array.Empty<byte>(), out int _);
                FuzzRandom random = new FuzzRandom(1);
                target.WriteGenerated(ref random, out int _, out int _, out byte[] _);
            }
        }

        [OneTimeTearDown]
        public void ReportTheWorstAllocationObserved()
        {
            TestContext.Progress.WriteLine(
                $"WallstopProto fuzz: worst decode allocation {_worstAllocation} B on a "
                    + $"{_worstAllocationPayloadLength} byte {_worstAllocationTarget} payload; "
                    + $"worst ratio {_worstAllocationRatio:F1}x ({_worstRatioAllocation} B on "
                    + $"{_worstRatioPayloadLength} bytes, {_worstRatioTarget}). Ceiling is "
                    + $"{AllocationSlackBytes} + {AllocationMultiple} x length."
            );
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

            // Recorded AFTER the ceiling, so the figure the teardown prints is the worst cost the
            // ceiling ACCEPTED. Recording first would let the deliberate amplifier in
            // TheAllocationCeilingCatchesABoundedAmplification set the high-water mark, and the next
            // person to tune the constants would be reading this suite's own counter-example.
            if (allocated > _worstAllocation)
            {
                _worstAllocation = allocated;
                _worstAllocationPayloadLength = payload.Length;
                _worstAllocationTarget = target.Name;
            }

            if (payload.Length > 0 && (double)allocated / payload.Length > _worstAllocationRatio)
            {
                _worstAllocationRatio = (double)allocated / payload.Length;
                _worstRatioAllocation = allocated;
                _worstRatioPayloadLength = payload.Length;
                _worstRatioTarget = target.Name;
            }

            coverage?.Record(accepted, consumed, payload);

            AssertTheFacadeAnswersTheSameWay(target, payload, accepted, origin);

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

        /// <summary>
        /// Drives the same payload through <see cref="WProtoFacade"/> -- the entry point
        /// <c>Serializer.ProtoDeserialize</c> reaches, and the one a corrupt save file actually
        /// meets -- and pins what a refusal looks like from there.
        /// </summary>
        /// <param name="target">The contract under test.</param>
        /// <param name="payload">The hostile bytes.</param>
        /// <param name="accepted">What the formatter itself answered.</param>
        /// <param name="origin">The strategy, seed and iteration, for a reproducible failure.</param>
        /// <remarks>
        /// <para>
        /// The facade deliberately <b>throws</b> where the formatter returns <c>false</c>: "no
        /// formatter for this type" and "this type's formatter refused the payload" are different
        /// answers, and reporting both as <c>false</c> sent a rejected payload on to protobuf-net,
        /// which is the path IL2CPP cannot run. That decision is in tension with the
        /// <c>TryXxx</c>-never-throws rule, so what may escape is asserted here rather than left to
        /// the reader of the source: exactly <see cref="InvalidOperationException"/>, and only where
        /// the formatter also refused.
        /// </para>
        /// <para>
        /// Allocation is measured on the formatter's own read rather than here, because a throw
        /// allocates an exception and a stack trace -- charging that to the payload would say a
        /// refusal is more expensive than an acceptance, which is true and irrelevant.
        /// </para>
        /// </remarks>
        private static void AssertTheFacadeAnswersTheSameWay(
            Target target,
            byte[] payload,
            bool accepted,
            string origin
        )
        {
            bool served;
            try
            {
                served = target.FacadeRead(payload);
            }
            catch (InvalidOperationException)
            {
                Assert.IsFalse(
                    accepted,
                    $"{target.Name}: the facade threw for a payload its own formatter ACCEPTED, so "
                        + $"a legitimate save would fail to load.{Environment.NewLine}{origin}"
                        + $"{Environment.NewLine}payload: {Hex(payload)}"
                );
                return;
            }
            catch (Exception failure)
            {
                Assert.Fail(
                    $"{target.Name}: the facade raised {failure.GetType().Name}. The only exception "
                        + "it may raise on a refusal is InvalidOperationException -- anything else "
                        + "reaches a game as an unhandled crash rather than as a corrupt save."
                        + $"{Environment.NewLine}{origin}{Environment.NewLine}"
                        + $"payload: {Hex(payload)}{Environment.NewLine}{failure}"
                );
                return;
            }

            Assert.IsTrue(
                served,
                $"{target.Name}: the facade declined a contract it is registered for, which sends "
                    + $"the payload on to protobuf-net.{Environment.NewLine}{origin}"
                    + $"{Environment.NewLine}payload: {Hex(payload)}"
            );
            Assert.IsTrue(
                accepted,
                $"{target.Name}: the facade accepted a payload its own formatter refused."
                    + $"{Environment.NewLine}{origin}{Environment.NewLine}payload: {Hex(payload)}"
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
                // REFUSED, deep, by the dimension header's equality check, so its acceptance rate
                // sits near the floor while nearly all of its payloads reach a member reader. A
                // threshold on acceptance would have demanded the corpus stop exercising the very
                // check #434 added. Both live figures are in the failure message below.
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

                // The claim "every member shape is fuzzed", made checkable. The corpus-wide reach
                // rate above cannot support it: a strategy that dies on field 1 every time scores
                // 100% reach while every other member is untouched, which is exactly how the
                // capacity-claim strategy shipped covering one field of seven. The expected members
                // are read out of the seeds, so adding a member to a contract and setting it in the
                // sample extends this gate with no second list to remember.
                int minimumHits = Math.Max(1, Iterations / 100);
                foreach (int member in target.Members)
                {
                    Assert.GreaterOrEqual(
                        coverage.HitsFor(member),
                        minimumHits,
                        coverage.DescribeMembers(target.Name, target.Members)
                            + $" -- field {member} was dispatched on fewer than {minimumHits} "
                            + "times, so this suite does not fuzz that member's reader."
                    );
                }
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

                // A CORPUS-QUALITY gate, not a coverage one, and the distinction is worth stating
                // because the numbers barely move between targets: these payloads are mostly
                // unknown fields, which every contract skips the same way, so a contract with no
                // members at all scores about the same. What it proves is that the generator emits
                // well-formed keys rather than garbage that dies at the first byte -- which is a
                // precondition for the claims inside them ever being evaluated, and nothing more.
                // Per-member coverage is the named regression cases' job.
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
            // Every field a rectangular member occupies, not just field 1. Emitting one key put
            // 6,000 of 7,500 decodes into targets with no header at all and left the rank-three
            // member -- the shape #434's wrap-to-zero defect actually had -- unreachable, so the
            // axis fix this strategy exists for was pinned by a single iteration of a single seed.
            int[] rectangularFields = { 1, 2, 3, 4, 5, 6, 7 };
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

                int field = rectangularFields[random.Next(rectangularFields.Length)];
                List<byte> payload = new List<byte>();
                WriteVarint(payload, (ulong)((field << 3) | 2));
                WriteVarint(payload, (ulong)wrapper.Count);
                payload.AddRange(wrapper);

                byte[] bytes = payload.ToArray();
                foreach (Target target in targets)
                {
                    AssertDecodeIsSafe(
                        target,
                        bytes,
                        $"strategy=capacity-claim seed=0xD13E5 iteration={iteration} rank={rank} "
                            + $"field={field}"
                    );
                }
            }
        }

        [Test]
        public void AGeneratedValueWritesExactlyWhatItMeasures()
        {
            // The write path's whole failure class, and one the read strategies cannot see. The
            // fixed-point check above only compares encodings of values a DECODE produced, so a
            // shape no payload decodes to -- a lone surrogate in a string, a rectangular array with
            // a zero axis, a NaN -- could have Measure and Write disagreeing and nothing would say
            // so until a game saved one. Measure sizes the buffer and Write gets exactly that many
            // bytes, so a disagreement is a failed write or a short one rather than a silent
            // overrun: the buffer is the assertion.
            foreach (Target target in Targets())
            {
                FuzzRandom random = new FuzzRandom(0x217E5);
                for (int iteration = 0; iteration < Iterations; ++iteration)
                {
                    string origin = $"strategy=generated-value seed=0x217E5 iteration={iteration}";
                    int measured;
                    int written;
                    byte[] encoded;
                    bool wrote;
                    try
                    {
                        wrote = target.WriteGenerated(
                            ref random,
                            out measured,
                            out written,
                            out encoded
                        );
                    }
                    catch (Exception failure)
                    {
                        Assert.Fail(
                            $"{target.Name}: measuring or writing a generated value threw "
                                + $"{failure.GetType().Name}.{Environment.NewLine}{origin}"
                                + $"{Environment.NewLine}{failure}"
                        );
                        return;
                    }

                    Assert.IsTrue(
                        wrote,
                        $"{target.Name}: Write failed into a buffer of exactly the {measured} bytes "
                            + $"Measure asked for.{Environment.NewLine}{origin}"
                    );
                    Assert.AreEqual(
                        measured,
                        written,
                        $"{target.Name}: Measure promised {measured} bytes and Write produced "
                            + $"{written}. A value serialized into a pooled buffer would carry the "
                            + $"previous message's tail.{Environment.NewLine}{origin}"
                    );

                    // Its own encoding is a payload like any other, so it has to survive every
                    // property the hostile corpus is held to -- including reading back into
                    // something that re-encodes to the same bytes.
                    AssertDecodeIsSafe(target, encoded, origin);
                }
            }
        }

        /// <summary>
        /// The per-member coverage gate reads both what it expects and what it observed through
        /// <see cref="ScanTopLevelFields"/>, so a scanner that quietly stopped early would shrink
        /// both sides and stay green. That makes the scanner the one thing in this file that has to
        /// be pinned on its own rather than through the property it serves.
        /// </summary>
        /// <param name="hex">The payload.</param>
        /// <param name="limit">How far the reader is claimed to have got.</param>
        /// <returns>The field numbers found, comma separated.</returns>
        [TestCase("0801", 2, ExpectedResult = "1")]
        [TestCase("08011002", 4, ExpectedResult = "1,2")]
        [TestCase("08011002", 1, ExpectedResult = "1")]
        [TestCase("08011002", 0, ExpectedResult = "")]
        [TestCase("0A026162 1003", 6, ExpectedResult = "1,2")]
        [TestCase("0A05", 2, ExpectedResult = "1")]
        [TestCase("0D0000803F 1103000000000000 00", 14, ExpectedResult = "1,2")]
        [TestCase("1B 0801", 3, ExpectedResult = "3")]
        [TestCase("00", 1, ExpectedResult = "")]
        [TestCase("FFFFFFFFFFFFFFFFFFFF7F", 11, ExpectedResult = "")]
        [TestCase("", 0, ExpectedResult = "")]
        public string TheFieldScannerReportsWhatTheReaderWouldHaveDispatchedOn(
            string hex,
            int limit
        )
        {
            SortedSet<int> fields = new SortedSet<int>();
            ScanTopLevelFields(FromHex(hex.Replace(" ", string.Empty)), limit, fields);
            return string.Join(",", fields);
        }

        [Test]
        public void TheAllocationCeilingCatchesABoundedAmplification()
        {
            // The proof that tightening the ceiling bought something. The old 128 KB + 256x accepted
            // a decode that allocated 32 KB from ten bytes -- 3,300x -- which is every amplification
            // bug that happens to top out below a megabyte. This is that bug, synthesized: a reader
            // that allocates a fixed 32 KB whatever it was handed.
            Target amplifier = new Target(
                "synthetic-amplifier",
                new List<byte[]> { new byte[] { 0x08, 0x01 } },
                new List<int> { 1 },
                (byte[] payload, out int consumed) =>
                {
                    consumed = payload.Length;
                    GC.KeepAlive(new byte[32 * 1024]);
                    return false;
                },
                (byte[] payload, out byte[] first, out byte[] second) =>
                {
                    first = null;
                    second = null;
                    return false;
                },
                payload => throw new InvalidOperationException("refused"),
                (ref FuzzRandom random, out int measured, out int written, out byte[] encoded) =>
                {
                    measured = 0;
                    written = 0;
                    encoded = Array.Empty<byte>();
                    return true;
                }
            );

            Assert.Throws<AssertionException>(
                () =>
                    AssertDecodeIsSafe(
                        amplifier,
                        new byte[] { 0x08, 0x01, 0x10, 0x02, 0x18, 0x03, 0x20, 0x04, 0x28, 0x05 },
                        "strategy=ceiling-discrimination"
                    ),
                "the allocation ceiling accepted 32 KB from a ten-byte payload, so it is inert "
                    + "against every amplification that stops short of it."
            );
        }

        /// <summary>
        /// Findings live here, as the hex the fuzzer printed, so each one is pinned by a named case
        /// rather than by one iteration of one seed -- a corpus that drifts must not be able to stop
        /// covering a defect that has actually happened.
        /// </summary>
        [TestCase(
            "0A080A06FFFFFFFF0700",
            TestName = "AKnownHostilePayloadIsStillRefusedCleanlyForAnAxisNothingPaysFor"
        )]
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
