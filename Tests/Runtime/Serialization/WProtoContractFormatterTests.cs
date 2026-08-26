// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.DataStructure.Adapters;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins the hand-written formatters for the package's own contracts to protobuf-net's bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two layers, deliberately. The golden vectors are literal protobuf-net 3.2.56 output captured
    /// by a differential harness, and they run on <b>every</b> backend -- including the standalone
    /// IL2CPP leg, where protobuf-net itself cannot run at all. That is the point of this migration:
    /// the leg that cannot use the oracle is the leg that has to be covered. The live differential
    /// against the vendored oracle then runs on Mono only, and is what keeps the golden vectors
    /// honest as the corpus grows.
    /// </para>
    /// <para>
    /// The harness that produced these vectors compiled the real sources against protobuf-net and
    /// compared <b>644</b> values across the four contracts, byte-equal plus cross-deserialization
    /// in both directions. Seven mutations were run against it and all seven failed it.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoContractFormatterTests
    {
        private const int ScratchSize = 512;

        // Nothing here registers the built-in formatters. That is deliberate: every test below
        // resolves through WProtoFormatterProvider, so the whole fixture is the assertion that
        // WProtoBootstrap ran. A RegisterAll() call in a setup would hide a stripped or unreached
        // bootstrap on the one leg -- standalone IL2CPP -- where it can actually happen.

        // Components are sint32 on fields 5 and 6. The bytes come from protobuf-net itself, through
        // the GridCellShape stand-in in the generator suite -- this assembly cannot ask the oracle,
        // because protobuf-net is exactly what does not run on the IL2CPP legs these have to hold on.
        [TestCase(0, 0, "")]
        [TestCase(1, 2, "28023004")]
        [TestCase(-1, -2, "28013003")]
        [TestCase(int.MaxValue, int.MinValue, "28FEFFFFFF0F30FFFFFFFF0F")]
        public void FastVector2IntMatchesProtobufNetBytes(int x, int y, string expected)
        {
            AssertRoundTrip(new FastVector2Int(x, y), expected);
        }

        [TestCase(0, 0, 0, "")]
        [TestCase(1, 2, 3, "280230043806")]
        [TestCase(-1, -2, -3, "280130033805")]
        [TestCase(int.MaxValue, 0, int.MinValue, "28FEFFFFFF0F38FFFFFFFF0F")]
        public void FastVector3IntMatchesProtobufNetBytes(int x, int y, int z, string expected)
        {
            AssertRoundTrip(new FastVector3Int(x, y, z), expected);
        }

        [Test]
        public void NeitherFastVectorPutsItsDerivedHashOnTheWire()
        {
            // The hash is a pure function of the components and is recomputed on read, so carrying
            // it bought nothing. Being well distributed it was almost always a five-byte varint plus
            // a tag: six of the ten bytes an ordinary cell cost.
            Assert.AreEqual(-1, IndexOfTag(Encode(new FastVector2Int(5, 3)), 3));
            Assert.AreEqual(-1, IndexOfTag(Encode(new FastVector3Int(1, 2, 3)), 3));
        }

        [Test]
        public void NeitherFastVectorWritesTheInt32FieldsItStillReads()
        {
            // The old field numbers are a read path and nothing else. A write that still touched
            // them would be a payload carrying each component twice, in two encodings, and the
            // second reader to see it would have to pick.
            byte[] twoDimensional = Encode(new FastVector2Int(5, 3));
            Assert.AreEqual(-1, IndexOfTag(twoDimensional, 1), "x must not be written as int32");
            Assert.AreEqual(-1, IndexOfTag(twoDimensional, 2), "y must not be written as int32");
            Assert.Greater(IndexOfTag(twoDimensional, 5), -1, "x belongs on 5");
            Assert.Greater(IndexOfTag(twoDimensional, 6), -1, "y belongs on 6");

            byte[] threeDimensional = Encode(new FastVector3Int(1, 2, 3));
            Assert.AreEqual(-1, IndexOfTag(threeDimensional, 4), "z must not be written as int32");
            Assert.Greater(IndexOfTag(threeDimensional, 7), -1, "z belongs on 7");
        }

        [Test]
        public void APayloadWrittenWhenTheHashWasOnTheWireStillReadsBack()
        {
            // Field 3 is skipped as unknown and the hash recomputed, so data a shipped build already
            // persisted is unaffected. z keeps tag 4 for exactly this reason: were it renumbered onto
            // the vacated 3, every legacy payload would read its hash as z.
            AssertLegacyReadsBack("08011002188AC1D0EBFEFFFFFFFF01", new FastVector2Int(1, 2));
            AssertLegacyReadsBack("0801100218ABEFBCB6052003", new FastVector3Int(1, 2, 3));
            AssertLegacyReadsBack("18CDAFDA8B01", default(FastVector2Int));
            AssertLegacyReadsBack("18B7EFC3D504", default(FastVector3Int));
        }

        /// <summary>
        /// A payload carrying both encodings of a component decodes the same under either reader.
        /// </summary>
        /// <remarks>
        /// No encoder here writes one: the components are written as <c>sint32</c> and the
        /// <c>int32</c> fields are a read path only. A hand-written payload can carry both, though,
        /// and the two readers reach the components by different routes -- a switch in the formatter
        /// and a surrogate's conversion operator -- so "whichever the reader happens to see last"
        /// would have been two different answers to one question. Both apply the same rule: the
        /// ZigZag field unless it is absent.
        /// </remarks>
        [Test]
        public void APayloadCarryingBothEncodingsDecodesToTheZigZagOne()
        {
            // 08 05 = field 1, int32 5 (the retired x); 28 0E = field 5, varint 14 -> ZigZag 7.
            byte[] both = FromHex("0805280E");
            WProtoReader reader = new(both);
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<FastVector2Int>()
                    .TryRead(ref reader, out FastVector2Int restored)
            );
            Assert.AreEqual(new FastVector2Int(7, 0), restored);
        }

        // A component's cost is the number this holds, and under sint32 it follows the component's
        // DISTANCE from the origin rather than its sign: (-1, -2) is four bytes where it was
        // twenty-two. The 8,192 row is the other half of that trade -- zigzag spends the low bit on
        // the sign, so a positive value just over a varint boundary grows by one byte.
        [TestCase(0, 0, 0)]
        [TestCase(5, 3, 4)]
        [TestCase(-1, -2, 4)]
        [TestCase(-5, -3, 4)]
        [TestCase(8191, 8191, 6)]
        [TestCase(8192, 8192, 8)]
        [TestCase(16384, 16384, 8)]
        public void AFastVector2IntCellCostsExactlyItsComponents(int x, int y, int expectedBytes)
        {
            Assert.AreEqual(expectedBytes, Encode(new FastVector2Int(x, y)).Length);
        }

        /// <summary>
        /// The reason the encoding changed, as the two grids that motivated it.
        /// </summary>
        /// <remarks>
        /// 1,000 cells laid out twice over the same magnitudes -- once anchored at the origin, once
        /// centered on it. Under int32 the centered grid cost 14,690 bytes against the anchored one's
        /// 5,870, purely because half its coordinates were negative. They are now equal, and the
        /// number is the one the generator suite measures against protobuf-net itself.
        /// </remarks>
        [Test]
        public void ACentredGridOfCellsCostsWhatAnAnchoredOneDoes()
        {
            Assert.AreEqual(3870, GridBytes(0, 0));
            Assert.AreEqual(3870, GridBytes(-20, -12));
        }

        private static int GridBytes(int originX, int originY)
        {
            int total = 0;
            for (int x = originX; x < originX + 40; x++)
            {
                for (int y = originY; y < originY + 25; y++)
                {
                    total += Encode(new FastVector2Int(x, y)).Length;
                }
            }

            return total;
        }

        [Test]
        public void WGuidRoundTripsThroughItsFormatter()
        {
            WGuid guid = WGuid.NewGuid();
            byte[] encoded = Encode(guid);

            WProtoReader reader = new(encoded);
            Assert.IsTrue(WGuid.WProtoFormatter.Instance.TryRead(ref reader, out WGuid restored));
            Assert.AreEqual(guid, restored);
        }

        [Test]
        public void AnEmptyWGuidEncodesToNoBytesAtAll()
        {
            Assert.AreEqual(0, Encode(default(WGuid)).Length);
        }

        [Test]
        public void ADefaultRandomStateEncodesToNoBytesAtAll()
        {
            Assert.AreEqual(0, Encode(default(RandomState)).Length);
        }

        [Test]
        public void RandomStateStatesMatchProtobufNetBytes()
        {
            AssertRoundTrip(new RandomState(1UL, 2UL), "0801100250CEFEFD9206");
        }

        [Test]
        public void RandomStateGaussianMatchesProtobufNetBytes()
        {
            AssertRoundTrip(
                new RandomState(1UL, 2UL, 0.5d),
                "08011002180121000000000000E03F50C9B8D39003"
            );
        }

        [Test]
        public void RandomStateNullPayloadIsOmittedAndEmptyPayloadIsWritten()
        {
            // Measured, not assumed. A null byte array is absent; an empty one is present as a tag
            // plus a zero length (2A 00), the same asymmetry protobuf-net applies to strings.
            AssertRoundTrip(
                new RandomState(3UL, 4UL, null, null, 5u, 6, 7u, 8),
                "08031004300538064007480850B2F4BEF406"
            );
            AssertRoundTrip(
                new RandomState(3UL, 4UL, null, Array.Empty<byte>()),
                "080310042A0050F6FECEFD05"
            );
            AssertRoundTrip(
                new RandomState(3UL, 4UL, null, new byte[] { 9, 8, 7 }),
                "080310042A0309080750B3EFB1C801"
            );
        }

        [Test]
        public void RandomStateNegativeCountersMatchProtobufNetBytes()
        {
            AssertRoundTrip(
                new RandomState(
                    ulong.MaxValue,
                    ulong.MaxValue,
                    -1.5d,
                    new byte[] { 1 },
                    uint.MaxValue,
                    -5,
                    uint.MaxValue,
                    -9
                ),
                "08FFFFFFFFFFFFFFFFFF0110FFFFFFFFFFFFFFFFFF01180121000000000000F8BF2A010130FFFFFFFF0F38FBFFFFFFFFFFFFFFFF0140FFFFFFFF0F48F7FFFFFFFFFFFFFFFF01509FAFF2ADF9FFFFFFFF01"
            );
        }

        [Test]
        public void ANegativeZeroGaussianIsDroppedAndReadsBackPositive()
        {
            // protobuf-net's omission test is `value == 0`, and -0.0 == 0.0, so the field never
            // reaches the wire even though the "has gaussian" flag does. This is a silent data
            // change, reproduced because wire compatibility outranks fidelity, and pinned here so
            // it stays a decision rather than becoming a surprise.
            RandomState state = new(1UL, 2UL, -0d);
            byte[] encoded = Encode(state);

            Assert.AreEqual("08011002180150C9B8D3B5F9FFFFFFFF01", ToHex(encoded));
            Assert.AreEqual(-1, IndexOfTag(encoded, 4), "The gaussian field must be absent");

            WProtoReader reader = new(encoded);
            Assert.IsTrue(
                RandomState.WProtoFormatter.Instance.TryRead(ref reader, out RandomState restored)
            );
            Assert.IsFalse(double.IsNegative(restored.Gaussian ?? -1d));
        }

        [Test]
        public void EveryBuiltInFormatterIsRegisteredWithoutAnyoneAskingForIt()
        {
            // No RegisterAll() anywhere in this fixture: reaching this assertion at all means the
            // startup hook ran. On the standalone IL2CPP leg this is the only check that the
            // registrar survived managed stripping, which is why the hook is a
            // [RuntimeInitializeOnLoadMethod] -- a linker root -- rather than a [ModuleInitializer].
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<FastVector2Int>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<FastVector3Int>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<WGuid>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<RandomState>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<DateTime>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<TimeSpan>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<Guid>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<decimal>());
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<DateTime>());
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<TimeSpan>());
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<Guid>());
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<decimal>());

            Assert.IsTrue(
                ReferenceEquals(
                    WProtoFormatterProvider.Get<RandomState>(),
                    RandomState.WProtoFormatter.Instance
                )
            );
            Assert.IsTrue(
                ReferenceEquals(
                    WProtoFormatterProvider.Get<DateTime>(),
                    WProtoDateTimeFormatter.Instance
                )
            );
        }

        [Test]
        public void ARegistrationMadeAfterStartupReplacesTheBuiltInOne()
        {
            // The ordering guarantee auto-registration has to keep: built-ins go in at
            // SubsystemRegistration, the earliest phase, so a consumer registering from any later
            // one wins. Without that ordering this passes anyway -- what it pins is that
            // registration stays last-wins rather than becoming first-wins or throwing on a
            // duplicate, which is the shape auto-registration would tempt someone into.
            IWProtoFormatter<RandomState> builtIn = WProtoFormatterProvider.Get<RandomState>();
            StubFormatter replacement = new();
            try
            {
                WProtoFormatterProvider.Register<RandomState>(replacement);
                Assert.IsTrue(
                    ReferenceEquals(replacement, WProtoFormatterProvider.Get<RandomState>())
                );
            }
            finally
            {
                WProtoFormatterProvider.Register(builtIn);
            }

            Assert.IsTrue(
                ReferenceEquals(builtIn, WProtoFormatterProvider.Get<RandomState>()),
                "A consumer override must be reversible, or this fixture poisons every later test."
            );
        }

        [Test]
        public void AnUnregisteredTypeReportsWhichTypeAndHowToFixIt()
        {
            Assert.IsFalse(WProtoFormatterProvider.TryGet(out IWProtoFormatter<Uri> unregistered));
            Assert.IsTrue(unregistered == null);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
                WProtoFormatterProvider.Get<Uri>()
            );

            // The whole reason this throws instead of returning null: an opaque
            // ExecutionEngineException from IL2CPP names nothing.
            Assert.IsTrue(error.Message.Contains(typeof(Uri).FullName));
            Assert.IsTrue(error.Message.Contains(nameof(WProtoFormatterProvider)));
        }

        [TestCase(0, true)]
        [TestCase(1, true)]
        [TestCase(WProtoReader.MaxNestingDepth, true)]
        [TestCase(WProtoReader.MaxNestingDepth + 1, false)]
        [TestCase(WProtoReader.MaxNestingDepth + 8, false)]
        public void ANestedContractIsBoundedByTheDepthLimitWhereverItDescends(
            int depth,
            bool expected
        )
        {
            // The bound lives on the reader, so a formatter only gets it by descending through
            // TryReadMessage. One that reads the payload as bytes and builds its own reader restarts
            // the count at zero at every level and round-trips this same data happily -- which is
            // exactly why the deep cases are here rather than only the shallow ones. Past-the-bound
            // depths are kept small enough that the unbounded form would NOT overflow the stack:
            // a test that crashes the runner proves nothing, and a stack overflow cannot be caught.
            WProtoFormatterProvider.Register(NestingProbe.Formatter.Instance);
            WProtoReader reader = new(BuildNesting(depth));

            Assert.AreEqual(
                expected,
                WProtoFormatterProvider
                    .Get<NestingProbe>()
                    .TryRead(ref reader, out NestingProbe decoded)
            );

            if (expected)
            {
                Assert.AreEqual(depth, NestingProbe.DepthOf(decoded));
            }
        }

        [Test]
        public void AFormatterBuildingItsOwnReaderInheritsTheParentsDepth()
        {
            // The escape hatch for a formatter that has already read a payload as bytes. Taking the
            // parent rather than an int is what stops the depth being understated back to zero.
            byte[] payload = BuildNesting(1);
            WProtoReader root = new(payload);
            Assert.IsTrue(root.TryReadTag(out _, out _));
            Assert.IsTrue(root.TryReadBytes(out ReadOnlySpan<byte> inner));

            WProtoReader nested = new(inner, in root);
            Assert.AreEqual(root.Depth + 1, nested.Depth);
            Assert.IsFalse(nested.Malformed);
        }

        [Test]
        public void AReaderBuiltFromAnExhaustedParentRefusesEveryRead()
        {
            WProtoReader root = new(BuildNesting(WProtoReader.MaxNestingDepth));
            AssertDescendingPastTheBoundIsRefused(ref root);
        }

        [Test]
        public void SubMessageNestingAtTheBoundIsAccepted()
        {
            int deepest = 0;
            WProtoReader reader = new(BuildNesting(WProtoReader.MaxNestingDepth));

            Assert.IsTrue(TryWalkNesting(ref reader, ref deepest));
            Assert.AreEqual(WProtoReader.MaxNestingDepth, deepest);
        }

        [Test]
        public void SubMessageNestingOneLevelPastTheBoundIsRefused()
        {
            int deepest = 0;
            WProtoReader reader = new(BuildNesting(WProtoReader.MaxNestingDepth + 1));

            Assert.IsFalse(TryWalkNesting(ref reader, ref deepest));
            Assert.AreEqual(WProtoReader.MaxNestingDepth, deepest);
        }

        [Test]
        public void GroupNestingIsBudgetedTogetherWithSubMessageNesting()
        {
            // A reader that is already deep inside sub-messages must not get a fresh group budget:
            // MaxNestingDepth groups at each of MaxNestingDepth sub-message levels is a product,
            // and the product is what overflows the stack the two kinds of nesting share.
            int deepest = 0;
            byte[] payload = BuildNesting(WProtoReader.MaxNestingDepth - 1, BuildGroupNesting(4));
            WProtoReader reader = new(payload);

            Assert.IsFalse(TryWalkNesting(ref reader, ref deepest));
            Assert.AreEqual(WProtoReader.MaxNestingDepth - 1, deepest);
        }

        [Test]
        public void ASubMessageNestingBombIsRefusedRatherThanRecursed()
        {
            // A few kilobytes describe two thousand levels. A formatter reads a sub-message by
            // calling another formatter, so without the bound this is two thousand stack frames and
            // a stack overflow, which no caller can catch.
            byte[] bomb = BuildNesting(2000);
            Assert.Less(bomb.Length, 8192, "The bomb has to be small relative to its depth");

            int deepest = 0;
            WProtoReader reader = new(bomb);

            Assert.IsFalse(TryWalkNesting(ref reader, ref deepest));
            Assert.AreEqual(
                WProtoReader.MaxNestingDepth,
                deepest,
                "It must stop at the bound, not run out of some other budget first"
            );
        }

        [Test]
        [WallstopStudios.UnityHelpers.Tests.Core.SkipUnderIL2CPP]
        public void EveryFormatterAgreesWithTheVendoredOracle()
        {
            // Without its surrogate registered, protobuf-net does not refuse FastVector2Int -- it
            // encodes the type's own contract, on the int32 fields the surrogate retired. See
            // WProtoFacadeTests.APortedTypeIsServedAndMatchesProtobufNetByteForByte.
            ProtobufUnityModel.EnsureInitialized();
            int checks = 0;
            int[] components = { 0, 1, -1, 300, -300, int.MaxValue, int.MinValue };

            foreach (int x in components)
            {
                foreach (int y in components)
                {
                    AssertMatchesOracle(new FastVector2Int(x, y), ref checks);
                    AssertMatchesOracle(new FastVector3Int(x, y, x + y), ref checks);
                }
            }

            ulong[] states = { 0UL, 1UL, 127UL, 128UL, ulong.MaxValue, 0x8000000000000000UL };
            double?[] gaussians = { null, 0d, -0d, 0.5d, -1.5d, double.MaxValue, double.NaN };
            byte[][] payloads =
            {
                null,
                Array.Empty<byte>(),
                new byte[] { 0 },
                new byte[] { 9, 8, 7 },
            };
            int[] counts = { 0, 1, -1, int.MaxValue, int.MinValue };
            uint[] buffers = { 0u, 1u, uint.MaxValue };

            int index = 0;
            foreach (ulong state1 in states)
            {
                foreach (double? gaussian in gaussians)
                {
                    foreach (byte[] payload in payloads)
                    {
                        int count = counts[index % counts.Length];
                        uint buffer = buffers[index % buffers.Length];
                        ulong state2 = states[index % states.Length];
                        index++;
                        AssertMatchesOracle(
                            new RandomState(
                                state1,
                                state2,
                                gaussian,
                                payload,
                                buffer,
                                count,
                                buffer,
                                -count
                            ),
                            ref checks
                        );
                    }
                }
            }

            for (int i = 0; i < 16; i++)
            {
                AssertMatchesOracle(WGuid.NewGuid(), ref checks);
            }

            Assert.Greater(checks, 200, "The differential corpus shrank; that is a coverage loss");
        }

        private static void AssertMatchesOracle<T>(T value, ref int checks)
        {
            byte[] mine = Encode(value);
            byte[] oracle;
            using (MemoryStream stream = new())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                oracle = stream.ToArray();
            }

            checks++;
            Assert.AreEqual(ToHex(oracle), ToHex(mine), $"{typeof(T).Name} bytes diverged");

            // Bytes matching is not the same as agreeing on what they mean, so both directions are
            // decoded as well.
            WProtoReader reader = new(oracle);
            Assert.IsTrue(WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T decoded));
            Assert.AreEqual(value, decoded);

            using (MemoryStream stream = new(mine))
            {
                Assert.AreEqual(value, ProtoBuf.Serializer.Deserialize<T>(stream));
            }
        }

        private static void AssertRoundTrip<T>(T value, string expectedHex)
        {
            byte[] encoded = Encode(value);
            Assert.AreEqual(expectedHex, ToHex(encoded));

            WProtoReader reader = new(encoded);
            Assert.IsTrue(WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T restored));
            Assert.AreEqual(value, restored);
        }

        private static void AssertLegacyReadsBack<T>(string legacyHex, T expected)
        {
            byte[] legacy = FromHex(legacyHex);
            WProtoReader reader = new(legacy);
            Assert.IsTrue(
                WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T restored),
                $"A payload carrying the retired hash field no longer reads: {legacyHex}"
            );
            Assert.AreEqual(expected, restored);
            Assert.AreEqual(expected.GetHashCode(), restored.GetHashCode());
        }

        private static byte[] FromHex(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; ++i)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }

        private static byte[] Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            int measured = formatter.Measure(value);

            byte[] scratch = new byte[ScratchSize];
            WProtoWriter writer = new(scratch);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.IsFalse(writer.Faulted);
            Assert.AreEqual(
                measured,
                writer.Position,
                "Measure must predict Write exactly, or every enclosing length prefix lies"
            );

            byte[] result = new byte[writer.Position];
            Array.Copy(scratch, result, writer.Position);
            return result;
        }

        private static string ToHex(byte[] bytes)
        {
            char[] characters = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                characters[i * 2] = "0123456789ABCDEF"[bytes[i] >> 4];
                characters[(i * 2) + 1] = "0123456789ABCDEF"[bytes[i] & 0xF];
            }

            return new string(characters);
        }

        /// <summary>Returns the offset of a top-level field key, or -1 when absent.</summary>
        private static int IndexOfTag(byte[] encoded, int fieldNumber)
        {
            WProtoReader reader = new(encoded);
            while (true)
            {
                int position = reader.Position;
                if (!reader.TryReadTag(out int number, out int wireType))
                {
                    return -1;
                }

                if (number == fieldNumber)
                {
                    return position;
                }

                if (!reader.TrySkipField(number, wireType))
                {
                    return -1;
                }
            }
        }

        /// <summary>Builds <paramref name="depth"/> levels of "field 1 is a sub-message".</summary>
        /// <remarks>
        /// The length prefix is a real varint. Writing it as a single byte caps honest nesting at
        /// 127 levels and then silently wraps, which makes a deep payload malformed for a reason
        /// that has nothing to do with depth -- a bomb test built that way passes with the depth
        /// bound removed, which is how this was found.
        /// </remarks>
        private static byte[] BuildNesting(int depth)
        {
            return BuildNesting(depth, Array.Empty<byte>());
        }

        /// <summary>Wraps <paramref name="innermost"/> in <paramref name="depth"/> sub-messages.</summary>
        private static byte[] BuildNesting(int depth, byte[] innermost)
        {
            byte[] payload = innermost;
            for (int level = 0; level < depth; level++)
            {
                int lengthSize = WProtoSizes.Varint32Size((uint)payload.Length);
                byte[] next = new byte[1 + lengthSize + payload.Length];
                next[0] = (byte)((1 << 3) | WProtoWireType.LengthDelimited);

                uint remainingLength = (uint)payload.Length;
                int cursor = 1;
                while (0x80u <= remainingLength)
                {
                    next[cursor++] = (byte)(remainingLength | 0x80u);
                    remainingLength >>= 7;
                }

                next[cursor++] = (byte)remainingLength;
                Array.Copy(payload, 0, next, cursor, payload.Length);
                payload = next;
            }

            return payload;
        }

        /// <summary>Builds <paramref name="depth"/> nested groups on field 2, innermost empty.</summary>
        private static byte[] BuildGroupNesting(int depth)
        {
            byte[] payload = Array.Empty<byte>();
            for (int level = 0; level < depth; level++)
            {
                byte[] next = new byte[payload.Length + 2];
                next[0] = (byte)((2 << 3) | WProtoWireType.StartGroup);
                Array.Copy(payload, 0, next, 1, payload.Length);
                next[payload.Length + 1] = (byte)((2 << 3) | WProtoWireType.EndGroup);
                payload = next;
            }

            return payload;
        }

        /// <summary>Descends the nesting the way a generated formatter would: recursively.</summary>
        /// <remarks>
        /// Failure propagates through the return value rather than through the root reader's
        /// <c>Malformed</c> flag, because that is the formatter contract: a refused nested read is
        /// reported by the nested reader, and the caller's job is to stop. Asserting on the root's
        /// flag instead passes with the depth bound removed, since the root's own stream was never
        /// the thing that failed.
        /// </remarks>
        private static bool TryWalkNesting(ref WProtoReader reader, ref int deepest)
        {
            if (deepest < reader.Depth)
            {
                deepest = reader.Depth;
            }

            while (reader.TryReadTag(out int fieldNumber, out int wireType))
            {
                if (fieldNumber == 1 && wireType == WProtoWireType.LengthDelimited)
                {
                    if (!reader.TryReadMessage(out WProtoReader nested))
                    {
                        return false;
                    }

                    if (!TryWalkNesting(ref nested, ref deepest))
                    {
                        return false;
                    }

                    continue;
                }

                if (!reader.TrySkipField(fieldNumber, wireType))
                {
                    return false;
                }
            }

            return !reader.Malformed;
        }

        /// <summary>Descends to the bound, then asserts the level past it is refused.</summary>
        private static void AssertDescendingPastTheBoundIsRefused(ref WProtoReader reader)
        {
            if (WProtoReader.MaxNestingDepth <= reader.Depth)
            {
                WProtoReader past = new(new byte[] { 0x08, 0x01 }, in reader);
                Assert.IsTrue(past.Malformed);
                Assert.IsFalse(past.TryReadTag(out _, out _));
                return;
            }

            Assert.IsTrue(reader.TryReadTag(out _, out _));
            Assert.IsTrue(reader.TryReadMessage(out WProtoReader nested));
            AssertDescendingPastTheBoundIsRefused(ref nested);
        }

        /// <summary>A self-nesting contract: the shape whose formatter has to carry the depth.</summary>
        private sealed class NestingProbe
        {
            internal NestingProbe Child;

            internal static int DepthOf(NestingProbe probe)
            {
                int depth = 0;
                for (NestingProbe node = probe; node?.Child != null; node = node.Child)
                {
                    depth++;
                }

                return depth;
            }

            internal sealed class Formatter : IWProtoFormatter<NestingProbe>
            {
                internal static readonly Formatter Instance = new();

                public int Measure(in NestingProbe value)
                {
                    if (value?.Child == null)
                    {
                        return 0;
                    }

                    int childSize = Measure(value.Child);
                    return WProtoSizes.TagSize(1) + WProtoSizes.LengthDelimitedSize(childSize);
                }

                public bool Write(ref WProtoWriter writer, in NestingProbe value)
                {
                    if (value?.Child == null)
                    {
                        return true;
                    }

                    return writer.TryWriteTag(1, WProtoWireType.LengthDelimited)
                        && writer.TryWriteLengthPrefix(Measure(value.Child))
                        && Write(ref writer, value.Child);
                }

                public bool TryRead(ref WProtoReader reader, out NestingProbe value)
                {
                    NestingProbe read = new();
                    while (reader.TryReadTag(out int fieldNumber, out int wireType))
                    {
                        if (fieldNumber == 1 && wireType == WProtoWireType.LengthDelimited)
                        {
                            // The bounded descent. Reading the payload with TryReadBytes and
                            // constructing a reader over it with the single-argument constructor
                            // passes every other test in this file and removes the bound entirely.
                            if (!reader.TryReadMessage(Instance, out NestingProbe child))
                            {
                                value = null;
                                return false;
                            }

                            read.Child = child;
                            continue;
                        }

                        if (!reader.TrySkipField(fieldNumber, wireType))
                        {
                            value = null;
                            return false;
                        }
                    }

                    if (reader.Malformed)
                    {
                        value = null;
                        return false;
                    }

                    value = read;
                    return true;
                }
            }
        }

        /// <summary>Stands in for a consumer's replacement formatter; never actually invoked.</summary>
        private sealed class StubFormatter : IWProtoFormatter<RandomState>
        {
            public int Measure(in RandomState value)
            {
                return 0;
            }

            public bool Write(ref WProtoWriter writer, in RandomState value)
            {
                return true;
            }

            public bool TryRead(ref WProtoReader reader, out RandomState value)
            {
                value = default;
                return true;
            }
        }
    }
}
