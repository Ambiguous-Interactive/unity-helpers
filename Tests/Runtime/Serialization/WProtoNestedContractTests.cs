// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins how a generated formatter writes a member whose type is another contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sub-message is length-prefixed, so its size has to be on the wire before its bytes exist.
    /// The writer produces the prefix by back-patching a finished payload rather than by measuring
    /// the sub-message a second time, and that choice is what these tests pin. Re-measuring runs the
    /// child's before-serialization hook once per enclosing level while its after-serialization hook
    /// still runs once -- measured at 4 against 1 for a value three levels down -- so anything the
    /// before hook rents leaks one rental per level.
    /// </para>
    /// <para>
    /// The standalone legs are IL2CPP, which is the reason the whole serializer exists: these
    /// fixtures are the only place generated nested code is AOT-compiled and run.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoNestedContractTests
    {
        [Test]
        public void ANestedGraphRoundTripsAtEveryLevel()
        {
            WProtoNestedRootContract original = Build(3, 5);
            WProtoNestedRootContract restored = RoundTrip(original);

            Assert.AreEqual(1, restored.Id);
            Assert.AreEqual(2, restored.Child.Id);
            Assert.AreEqual(3, restored.Child.Child.Value);
            Assert.AreEqual(7, restored.Trailer);
            CollectionAssert.AreEqual(original.Child.Child.Bulk, restored.Child.Child.Bulk);
        }

        [Test]
        public void EachLifecycleHookRunsExactlyOncePerSerializationRegardlessOfDepth()
        {
            WProtoNestedRootContract graph = Build(3, 0);
            IWProtoFormatter<WProtoNestedRootContract> formatter =
                WProtoFormatterProvider.Get<WProtoNestedRootContract>();

            int before = WProtoNestedLeafContract.BeforeSerializationRuns;
            int after = WProtoNestedLeafContract.AfterSerializationRuns;

            byte[] buffer = new byte[formatter.Measure(graph)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, graph));

            Assert.AreEqual(
                1,
                WProtoNestedLeafContract.BeforeSerializationRuns - before,
                "Before-serialization belongs to Measure, and a nested value is measured once for "
                    + "the whole serialization no matter how deep it sits."
            );
            Assert.AreEqual(
                1,
                WProtoNestedLeafContract.AfterSerializationRuns - after,
                "After-serialization belongs to Write, and the two have to stay paired -- an "
                    + "unbalanced pair is what leaks whatever the before hook rented."
            );
        }

        /*
            The 125/126-byte boundary expands the leaf length prefix and can propagate expansion through its
            parents.
        */
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(120)]
        [TestCase(124)]
        [TestCase(125)]
        [TestCase(126)]
        [TestCase(127)]
        [TestCase(128)]
        [TestCase(1000)]
        [TestCase(20000)]
        public void ASubMessageCrossingTheLengthPrefixWidthStaysExact(int bulk)
        {
            WProtoNestedRootContract graph = Build(3, bulk);
            IWProtoFormatter<WProtoNestedRootContract> formatter =
                WProtoFormatterProvider.Get<WProtoNestedRootContract>();

            int predicted = formatter.Measure(graph);
            byte[] buffer = new byte[predicted];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, graph));
            Assert.AreEqual(
                predicted,
                writer.Position,
                "A prefix produced from the wrong length leaves Measure and Write disagreeing."
            );

            WProtoNestedRootContract restored = RoundTrip(graph);
            Assert.AreEqual(
                7,
                restored.Trailer,
                "The field after the sub-message was overwritten."
            );
            CollectionAssert.AreEqual(graph.Child.Child.Bulk, restored.Child.Child.Bulk);
        }

        /*
            Literal bytes pin length-prefix expansion where back-patching could overlap its own payload yet
            still decode.
        */
        [TestCase(125, "08011283010802127F127D")]
        [TestCase(126, "08011285010802128001127E")]
        public void ANestedPrefixWidensExactlyAtTheProtobufBoundary(int bulk, string expected)
        {
            StringAssert.StartsWith(expected, Encode(Build(0, bulk)));
        }

        [Test]
        public void ANullSubMessageIsOmittedAndAnEmptyOneIsWritten()
        {
            Assert.AreEqual(
                string.Empty,
                Encode(new WProtoNestedRootContract()),
                "A null sub-message and a default scalar are both absent."
            );
            Assert.AreEqual(
                "1200",
                Encode(new WProtoNestedRootContract { Child = new WProtoNestedMidContract() }),
                "A non-null sub-message with nothing in it is still written."
            );
        }

        [Test]
        public void IsRequiredForcesADefaultValueOntoTheWireButNeverMaterializesANull()
        {
            /*
                IsRequired writes default scalars but still omits null references; forcing null sub-messages
                would crash measurement.
            */
            IWProtoFormatter<WProtoRequiredContract> formatter =
                WProtoFormatterProvider.Get<WProtoRequiredContract>();
            WProtoRequiredContract empty = new();

            byte[] buffer = new byte[formatter.Measure(empty)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, empty));
            Assert.AreEqual("0800", ToHex(writer.Written));

            WProtoReader reader = new(writer.Written);
            Assert.IsTrue(formatter.TryRead(ref reader, out WProtoRequiredContract restored));
            Assert.IsTrue(restored.Message == null);
            Assert.IsTrue(restored.Text == null);
            Assert.IsTrue(restored.Bytes == null);
        }

        [Test]
        public void ARecursiveContractRoundTripsWithinTheNestingBound()
        {
            WProtoNestedChainContract head = BuildChain(60);
            IWProtoFormatter<WProtoNestedChainContract> formatter =
                WProtoFormatterProvider.Get<WProtoNestedChainContract>();

            byte[] buffer = new byte[formatter.Measure(head)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, head));

            WProtoReader reader = new(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out WProtoNestedChainContract restored));

            int links = 0;
            for (
                WProtoNestedChainContract current = restored;
                current != null;
                current = current.Next
            )
            {
                Assert.AreEqual(60 - links, current.Id);
                links++;
            }

            Assert.AreEqual(60, links);
        }

        [Test]
        public void MeasuringPastTheNestingBoundIsRefusedByNameRatherThanOverflowingTheStack()
        {
            // Cyclic messages have no finite encoded size; detect them before uncatchable stack overflow.
            IWProtoFormatter<WProtoNestedChainContract> formatter =
                WProtoFormatterProvider.Get<WProtoNestedChainContract>();

            WProtoNestedChainContract legal = BuildChain(60);
            int measuredBefore = formatter.Measure(legal);

            InvalidOperationException tooDeep = Assert.Throws<InvalidOperationException>(() =>
                formatter.Measure(BuildChain(80))
            );
            StringAssert.Contains("nesting", tooDeep.Message);

            WProtoNestedChainContract cycle = new() { Id = 1 };
            cycle.Next = cycle;
            Assert.Throws<InvalidOperationException>(() => formatter.Measure(cycle));

            // Unwind depth after refusal so later valid serializations on the thread remain usable.
            Assert.AreEqual(
                measuredBefore,
                formatter.Measure(legal),
                "A legal graph must measure the same after a refusal as it did before one."
            );
        }

        [Test]
        public void ABufferOneByteShortRefusesRatherThanTruncating()
        {
            WProtoNestedRootContract graph = Build(3, 200);
            IWProtoFormatter<WProtoNestedRootContract> formatter =
                WProtoFormatterProvider.Get<WProtoNestedRootContract>();

            byte[] cramped = new byte[formatter.Measure(graph) - 1];
            WProtoWriter writer = new(cramped);

            Assert.IsFalse(formatter.Write(ref writer, graph));
            Assert.IsTrue(writer.Faulted);
        }

        private static WProtoNestedRootContract Build(int value, int bulk)
        {
            byte[] payload = bulk == 0 ? null : new byte[bulk];
            for (int index = 0; index < bulk; index++)
            {
                payload[index] = (byte)(index * 31);
            }

            return new WProtoNestedRootContract
            {
                Id = 1,
                Trailer = 7,
                Child = new WProtoNestedMidContract
                {
                    Id = 2,
                    Child = new WProtoNestedLeafContract { Value = value, Bulk = payload },
                },
            };
        }

        private static WProtoNestedChainContract BuildChain(int links)
        {
            WProtoNestedChainContract head = null;
            for (int link = 1; link <= links; link++)
            {
                head = new WProtoNestedChainContract { Id = link, Next = head };
            }

            return head;
        }

        private static string ToHex(ReadOnlySpan<byte> bytes)
        {
            StringBuilder builder = new(bytes.Length * 2);
            for (int index = 0; index < bytes.Length; index++)
            {
                builder.Append(bytes[index].ToString("X2"));
            }

            return builder.ToString();
        }

        private static string Encode(WProtoNestedRootContract value)
        {
            IWProtoFormatter<WProtoNestedRootContract> formatter =
                WProtoFormatterProvider.Get<WProtoNestedRootContract>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(buffer.Length, writer.Position);

            StringBuilder builder = new(writer.Position * 2);
            foreach (byte current in writer.Written)
            {
                builder.Append(current.ToString("X2"));
            }

            return builder.ToString();
        }

        private static WProtoNestedRootContract RoundTrip(WProtoNestedRootContract value)
        {
            IWProtoFormatter<WProtoNestedRootContract> formatter =
                WProtoFormatterProvider.Get<WProtoNestedRootContract>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            WProtoReader reader = new(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out WProtoNestedRootContract restored));
            return restored;
        }
    }
}
