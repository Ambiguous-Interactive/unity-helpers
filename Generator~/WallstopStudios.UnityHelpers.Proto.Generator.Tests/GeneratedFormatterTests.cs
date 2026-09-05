// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Exercises the generator's output the way Unity will: compiled into this assembly by the
    /// generator itself, referenced as an analyzer rather than as a library.
    /// </summary>
    /// <remarks>
    /// These are not snapshot tests. A snapshot proves the emitter still emits the same characters;
    /// what has to hold is that the bytes match protobuf-net's rules -- ascending field number,
    /// defaults omitted, empty-but-not-null written -- and that a value survives a round trip. Every
    /// expected payload below is spelled out in hex for that reason.
    /// </remarks>
    [TestFixture]
    public sealed class GeneratedFormatterTests
    {
        [Test]
        public void TheGeneratorRegistersEveryContractItEmitted()
        {
            // Registration must come from the generated initializer, without a test calling Register.
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<ScalarContract>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<OutOfOrderContract>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<HookedContract>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<Outer.Point>());
        }

        [Test]
        public void AllDefaultMembersEncodeToNoBytesAtAll()
        {
            Assert.AreEqual(string.Empty, Encode(new ScalarContract()));
        }

        [Test]
        public void EveryScalarShapeRoundTrips()
        {
            ScalarContract original = new ScalarContract
            {
                Int32 = -1,
                Int64 = long.MinValue,
                UInt32 = uint.MaxValue,
                UInt64 = ulong.MaxValue,
                Flag = true,
                Single = 1.5f,
                Double = -2.25d,
                Text = "wallstop",
                Bytes = new byte[] { 1, 2, 3 },
                Enum = Mode.Careful,
                MaybeDouble = 0d,
                Int16 = -2,
                Hidden = 7,
                Counted = 9,
            };

            ScalarContract restored = RoundTrip(original);

            Assert.AreEqual(original.Int32, restored.Int32);
            Assert.AreEqual(original.Int64, restored.Int64);
            Assert.AreEqual(original.UInt32, restored.UInt32);
            Assert.AreEqual(original.UInt64, restored.UInt64);
            Assert.AreEqual(original.Flag, restored.Flag);
            Assert.AreEqual(original.Single, restored.Single);
            Assert.AreEqual(original.Double, restored.Double);
            Assert.AreEqual(original.Text, restored.Text);
            CollectionAssert.AreEqual(original.Bytes, restored.Bytes);
            Assert.AreEqual(original.Enum, restored.Enum);
            Assert.AreEqual(original.MaybeDouble, restored.MaybeDouble);
            Assert.AreEqual(original.Int16, restored.Int16);
            Assert.AreEqual(original.Hidden, restored.Hidden);
            Assert.AreEqual(original.Counted, restored.Counted);
        }

        [Test]
        public void ANullableHoldingZeroIsStillPresent()
        {
            // Nullable zero and null need distinct encodings even though zero equals the scalar default.
            Assert.AreEqual(
                "59" + "0000000000000000",
                Encode(new ScalarContract { MaybeDouble = 0d })
            );
            Assert.AreEqual(string.Empty, Encode(new ScalarContract { MaybeDouble = null }));
            Assert.IsNull(RoundTrip(new ScalarContract { MaybeDouble = null }).MaybeDouble);
            Assert.AreEqual(0d, RoundTrip(new ScalarContract { MaybeDouble = 0d }).MaybeDouble);
        }

        [Test]
        public void EmptyIsWrittenAndNullIsOmitted()
        {
            // protobuf-net writes empty strings and byte arrays as zero-length values; only null is absent.
            Assert.AreEqual("4200", Encode(new ScalarContract { Text = string.Empty }));
            Assert.AreEqual("4A00", Encode(new ScalarContract { Bytes = Array.Empty<byte>() }));
            Assert.AreEqual(string.Empty, Encode(new ScalarContract { Text = null, Bytes = null }));
            Assert.AreEqual(
                string.Empty,
                Encode(RoundTrip(new ScalarContract { Text = null })),
                "An omitted string must read back as null rather than as empty"
            );
        }

        [Test]
        public void NegativeZeroDoesNotSurvive()
        {
            // protobuf-net omits negative zero because its default comparison treats it as positive zero.
            Assert.AreEqual(string.Empty, Encode(new ScalarContract { Double = -0d }));
            Assert.IsFalse(
                double.IsNegative(RoundTrip(new ScalarContract { Double = -0d }).Double)
            );
        }

        [Test]
        public void MembersAreWrittenInAscendingFieldNumberNotDeclarationOrder()
        {
            /*
             * Declaration order differs from tag order here, exposing writers that serialize in declaration
             * order.
             */
            Assert.AreEqual(
                "0801" + "1803" + "2004",
                Encode(
                    new OutOfOrderContract
                    {
                        First = 1,
                        Third = 3,
                        Fourth = 4,
                    }
                )
            );
        }

        [Test]
        public void HooksRunInTheDocumentedOrderAndExactlyOnce()
        {
            HookedContract original = new HookedContract { Value = 5 };
            IWProtoFormatter<HookedContract> formatter =
                WProtoFormatterProvider.Get<HookedContract>();

            int size = formatter.Measure(original);
            byte[] buffer = new byte[size];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, original));

            CollectionAssert.AreEqual(
                new[] { "OnBeforeSerialization", "OnAfterSerialization" },
                original.Trace,
                "Before-serialization belongs to Measure alone; repeating it in Write would leak "
                    + "anything the hook rented."
            );

            WProtoReader reader = new WProtoReader(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out HookedContract restored));
            CollectionAssert.AreEqual(
                new[] { "OnBeforeDeserialization", "OnAfterDeserialization" },
                restored.Trace
            );
            Assert.AreEqual(5, restored.Value);
        }

        [TestCase(
            new byte[] { 0x08 },
            TestName = "AFailedReadDoesNotRunTheHookWhenAMemberIsTruncated"
        )]
        [TestCase(
            new byte[] { 0xFF },
            TestName = "AFailedReadDoesNotRunTheHookWhenTheKeyIsMalformed"
        )]
        public void AFailedReadDoesNotRunTheAfterDeserializationHook(byte[] payload)
        {
            /*
             * Truncated values and incomplete keys fail on different paths; neither may invoke the success
             * hook.
             */
            int before = HookedContract.AfterDeserializationRuns;
            WProtoReader reader = new WProtoReader(payload);

            Assert.IsFalse(
                WProtoFormatterProvider
                    .Get<HookedContract>()
                    .TryRead(ref reader, out HookedContract value)
            );
            Assert.IsNull(value);
            Assert.AreEqual(before, HookedContract.AfterDeserializationRuns);
        }

        [Test]
        public void AStructContractNestedInAnotherTypeRoundTrips()
        {
            Outer.Point original = new Outer.Point { X = 3, Y = -4 };
            IWProtoFormatter<Outer.Point> formatter = WProtoFormatterProvider.Get<Outer.Point>();

            int size = formatter.Measure(original);
            byte[] buffer = new byte[size];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, original));

            WProtoReader reader = new WProtoReader(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out Outer.Point restored));
            Assert.AreEqual(original.X, restored.X);
            Assert.AreEqual(original.Y, restored.Y);
        }

        [Test]
        public void AnUnknownFieldIsSkippedRatherThanRejected()
        {
            byte[] buffer = new byte[64];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(writer.TryWriteTag(1, WProtoWireType.Varint));
            Assert.IsTrue(writer.TryWriteInt32(11));
            Assert.IsTrue(writer.TryWriteTag(999, WProtoWireType.LengthDelimited));
            Assert.IsTrue(writer.TryWriteString("from the future"));

            WProtoReader reader = new WProtoReader(writer.Written);
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<ScalarContract>()
                    .TryRead(ref reader, out ScalarContract restored)
            );
            Assert.AreEqual(11, restored.Int32);
        }

        [Test]
        public void ANestedContractRoundTripsThroughEveryMemberShape()
        {
            NestingContract original = new NestingContract
            {
                Id = 9,
                Child = new HookedContract { Value = 5 },
                Where = new Outer.Point { X = 3, Y = -4 },
                MaybeWhere = new Outer.Point { X = 1, Y = 2 },
            };

            NestingContract restored = RoundTrip(original);

            Assert.AreEqual(9, restored.Id);
            Assert.AreEqual(5, restored.Child.Value);
            Assert.AreEqual(3, restored.Where.X);
            Assert.AreEqual(-4, restored.Where.Y);
            Assert.IsTrue(restored.MaybeWhere.HasValue);
            Assert.AreEqual(1, restored.MaybeWhere.Value.X);
            Assert.AreEqual(2, restored.MaybeWhere.Value.Y);
        }

        [Test]
        public void AStructSubMessageIsWrittenEvenWhenEveryMemberIsDefault()
        {
            // protobuf-net emits default struct sub-messages as zero-length values, unlike null references.
            Assert.AreEqual("1A00", Encode(new NestingContract()));
            Assert.AreEqual(
                "1A00",
                Encode(new NestingContract { Child = null, MaybeWhere = null }),
                "A null reference sub-message and an absent nullable are both omitted."
            );
            Assert.AreEqual(
                "1A00" + "2200",
                Encode(new NestingContract { MaybeWhere = default(Outer.Point) }),
                "A nullable sub-message holding a default value is present, because HasValue is "
                    + "what decides -- the same distinction 0d and null draw for a scalar."
            );
        }

        [Test]
        public void ANestedHookRunsExactlyOncePerSerializationAtEveryDepth()
        {
            // Re-measuring nested payloads repeats serialization hooks and can leak their pooled state.
            HookedContract hooked = new HookedContract { Value = 5 };
            DeepContract graph = new DeepContract
            {
                Id = 1,
                Child = new NestingContract { Id = 2, Child = hooked },
            };

            IWProtoFormatter<DeepContract> formatter = WProtoFormatterProvider.Get<DeepContract>();
            byte[] buffer = new byte[formatter.Measure(graph)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, graph));

            CollectionAssert.AreEqual(
                new[] { "OnBeforeSerialization", "OnAfterSerialization" },
                hooked.Trace,
                "Each hook runs exactly once per serialization no matter how deep the value sits."
            );
        }

        [Test]
        public void ASubMessageCrossingTheLengthPrefixWidthStaysExact()
        {
            // Crossing 127 bytes widens every enclosing prefix, exposing incorrect back-patch shifts.
            for (int length = 0; length <= 300; length++)
            {
                BulkHolder holder = new BulkHolder
                {
                    Child = new BulkContract { Payload = new byte[length] },
                    Trailer = 7,
                };
                for (int index = 0; index < length; index++)
                {
                    holder.Child.Payload[index] = (byte)(index * 31);
                }

                IWProtoFormatter<BulkHolder> formatter = WProtoFormatterProvider.Get<BulkHolder>();
                int predicted = formatter.Measure(holder);
                byte[] buffer = new byte[predicted];
                WProtoWriter writer = new WProtoWriter(buffer);
                Assert.IsTrue(formatter.Write(ref writer, holder), $"length {length}");
                Assert.AreEqual(predicted, writer.Position, $"length {length}");

                WProtoReader reader = new WProtoReader(buffer);
                Assert.IsTrue(formatter.TryRead(ref reader, out BulkHolder restored));
                Assert.AreEqual(7, restored.Trailer, $"length {length}");
                CollectionAssert.AreEqual(holder.Child.Payload, restored.Child.Payload);
            }
        }

        [Test]
        public void ABufferOneByteShortRefusesRatherThanTruncating()
        {
            BulkHolder holder = new BulkHolder
            {
                Child = new BulkContract { Payload = new byte[200] },
                Trailer = 7,
            };

            IWProtoFormatter<BulkHolder> formatter = WProtoFormatterProvider.Get<BulkHolder>();
            byte[] cramped = new byte[formatter.Measure(holder) - 1];
            WProtoWriter writer = new WProtoWriter(cramped);

            Assert.IsFalse(formatter.Write(ref writer, holder));
            Assert.IsTrue(writer.Faulted);
        }

        [Test]
        public void IsRequiredForcesADefaultValueOntoTheWireButNeverMaterializesANull()
        {
            /*
             * IsRequired forces existing default values onto the wire but does not invent values for null
             * references.
             */
            Assert.AreEqual("0800" + "2A00", Encode(new RequiredContract()));

            Assert.AreEqual(
                "0800" + "1200" + "1A00" + "2200" + "2A00",
                Encode(
                    new RequiredContract
                    {
                        Message = new EmptyContract(),
                        Text = string.Empty,
                        Bytes = Array.Empty<byte>(),
                    }
                ),
                "An empty-but-non-null reference is present; only null is absent."
            );

            RequiredContract restored = RoundTrip(new RequiredContract());
            Assert.IsNull(restored.Message);
            Assert.IsNull(restored.Text);
            Assert.IsNull(restored.Bytes);
            Assert.IsNull(restored.Ratio);
        }

        [Test]
        public void ARecursiveContractRoundTripsWithinTheNestingBound()
        {
            ChainContract head = BuildChain(60);
            ChainContract restored = RoundTrip(head);

            int links = 0;
            for (ChainContract current = restored; current != null; current = current.Next)
            {
                Assert.AreEqual(head.Id - links, current.Id);
                links++;
            }

            Assert.AreEqual(60, links);
        }

        [Test]
        public void MeasuringPastTheNestingBoundIsRefusedByNameRatherThanOverflowingTheStack()
        {
            // A cyclic message has no finite encoded size; unbounded measurement would overflow the stack.
            IWProtoFormatter<ChainContract> formatter =
                WProtoFormatterProvider.Get<ChainContract>();

            InvalidOperationException tooDeep = Assert.Throws<InvalidOperationException>(() =>
                formatter.Measure(BuildChain(80))
            );
            StringAssert.Contains("nesting", tooDeep.Message);

            ChainContract cycle = new ChainContract { Id = 1 };
            cycle.Next = cycle;
            Assert.Throws<InvalidOperationException>(() => formatter.Measure(cycle));

            // A rejected operation must unwind the depth counter before the next serialization.
            Assert.AreEqual(
                RoundTrip(BuildChain(60)).Id,
                60,
                "A legal graph must still serialize after a refusal."
            );
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryShape()
        {
            ScalarContract[] cases =
            {
                new ScalarContract(),
                new ScalarContract { Int32 = int.MinValue, Int64 = long.MaxValue },
                new ScalarContract { Text = string.Empty, Bytes = Array.Empty<byte>() },
                new ScalarContract { Text = "é中", Bytes = new byte[] { 0xFF } },
                new ScalarContract { Enum = Mode.Careful, MaybeDouble = double.NaN },
                new ScalarContract { UInt64 = ulong.MaxValue, Int16 = short.MinValue },
            };

            foreach (ScalarContract value in cases)
            {
                IWProtoFormatter<ScalarContract> formatter =
                    WProtoFormatterProvider.Get<ScalarContract>();
                int predicted = formatter.Measure(value);
                byte[] buffer = new byte[predicted];
                WProtoWriter writer = new WProtoWriter(buffer);
                Assert.IsTrue(formatter.Write(ref writer, value));
                Assert.AreEqual(predicted, writer.Position);
            }
        }

        private static ChainContract BuildChain(int links)
        {
            ChainContract head = null;
            for (int link = 1; link <= links; link++)
            {
                head = new ChainContract { Id = link, Next = head };
            }

            return head;
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            StringBuilder builder = new StringBuilder(writer.Position * 2);
            foreach (byte current in writer.Written)
            {
                builder.Append(current.ToString("X2"));
            }

            return builder.ToString();
        }

        private static T RoundTrip<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            WProtoReader reader = new WProtoReader(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out T restored));
            return restored;
        }

        [Test]
        public void WritingASubMessageLeavesTheNestingDepthWhereItStarted()
        {
            // Correct bytes cannot expose a drifting nesting counter; assert writer state directly.
            NestingContract value = new NestingContract
            {
                Id = 1,
                Child = new HookedContract { Value = 2 },
                Where = new Outer.Point { X = 3 },
            };

            IWProtoFormatter<NestingContract> formatter =
                WProtoFormatterProvider.Get<NestingContract>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);

            Assert.AreEqual(0, writer.Depth);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(0, writer.Depth, "depth did not return to zero after a complete write");
        }
    }
}
