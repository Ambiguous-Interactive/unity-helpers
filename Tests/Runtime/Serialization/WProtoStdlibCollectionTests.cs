// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;
    using WallstopStudios.UnityHelpers.Tests.Core;

    /// <summary>
    /// Runs the collection shapes added for issue 395 inside Unity, on the editors and players CI
    /// builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The oracle comparison is not here, for the reason it is not in
    /// <see cref="WProtoRepeatedContractTests"/>: protobuf-net is what fails under IL2CPP. What only
    /// Unity can prove is that a per-type fill method, a construct-at-end commit and a reverse push
    /// are AOT-compiled and behave. Every expected payload below was transcribed from the
    /// generator's own suite, which measured it against protobuf-net 3.2.56.
    /// </para>
    /// <para>
    /// Four of these shapes have no oracle at all. protobuf-net 2.4.9 has no serializer for
    /// <c>Queue&lt;T&gt;</c> or <c>Stack&lt;T&gt;</c>, and neither major can read back a
    /// <c>ReadOnlyCollection&lt;T&gt;</c> or <c>ReadOnlyDictionary&lt;K,V&gt;</c> it wrote itself,
    /// so for those the bytes are pinned and the round trip is the whole claim.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoStdlibCollectionTests
    {
        [Test]
        public void EveryStdlibShapeMatchesItsGoldenBytes()
        {
            Assert.AreEqual(
                "0A040102AC02",
                Encode(
                    new WProtoStdlibCollectionContract
                    {
                        Linked = new LinkedList<int>(new[] { 1, 2, 300 }),
                    }
                ),
                "LinkedList packs like any other varint run"
            );
            Assert.AreEqual(
                "12020405",
                Encode(
                    new WProtoStdlibCollectionContract { Queued = new Queue<int>(new[] { 4, 5 }) }
                ),
                "Queue writes front to back"
            );

            // The one that would have been guessed wrong. A stack pushed 1, 2, 3 enumerates 3, 2, 1,
            // and that enumeration order is what goes on the wire -- so the reader has to push the
            // run back in reverse or the stack comes out inverted.
            Assert.AreEqual(
                "1A03030201",
                Encode(
                    new WProtoStdlibCollectionContract
                    {
                        Stacked = new Stack<int>(new[] { 1, 2, 3 }),
                    }
                ),
                "Stack writes top first"
            );

            Assert.AreEqual(
                "2201612200",
                Encode(
                    new WProtoStdlibCollectionContract
                    {
                        Listed = new List<string> { "a", string.Empty },
                    }
                ),
                "an empty element is written, only null is absent"
            );
            Assert.AreEqual(
                "2A0B00FFFFFFFFFFFFFFFFFF01",
                Encode(
                    new WProtoStdlibCollectionContract
                    {
                        Collected = new List<int> { 0, -1 },
                    }
                ),
                "an element equal to its default is still written"
            );
            Assert.AreEqual(
                "320109",
                Encode(new WProtoStdlibCollectionContract { Enumerated = new List<int> { 9 } }),
                "the count-free path still opens exactly one run"
            );
            Assert.AreEqual(
                "3A020A0B",
                Encode(
                    new WProtoStdlibCollectionContract
                    {
                        ReadOnlyListed = new List<int> { 10, 11 },
                    }
                )
            );
            Assert.AreEqual(
                "42010C",
                Encode(new WProtoStdlibCollectionContract { SetOf = new HashSet<int> { 12 } })
            );
            Assert.AreEqual(
                "4A050A016B100D",
                Encode(
                    new WProtoStdlibCollectionContract
                    {
                        Mapped = new Dictionary<string, int> { { "k", 13 } },
                    }
                ),
                "a dictionary interface is a map, not a repeated value"
            );
            Assert.AreEqual(
                "52050A0172100E",
                Encode(
                    new WProtoStdlibCollectionContract
                    {
                        ReadOnlyMapped = new Dictionary<string, int> { { "r", 14 } },
                    }
                )
            );
            Assert.AreEqual(
                "5A0210025A020801",
                Encode(
                    new WProtoStdlibCollectionContract
                    {
                        StackedPoints = new Stack<WProtoRepeatedPoint>(
                            new[]
                            {
                                new WProtoRepeatedPoint { X = 1 },
                                new WProtoRepeatedPoint { Y = 2 },
                            }
                        ),
                    }
                ),
                "a message element cannot pack, so the reversal shows in the key order"
            );
        }

        [Test]
        public void EveryStdlibShapeRoundTrips()
        {
            WProtoStdlibCollectionContract value = new WProtoStdlibCollectionContract
            {
                Linked = new LinkedList<int>(new[] { 1, 2, 300 }),
                Queued = new Queue<int>(new[] { 4, 5 }),
                Stacked = new Stack<int>(new[] { 1, 2, 3 }),
                Listed = new List<string> { "a", string.Empty },
                Collected = new List<int> { 0, -1 },
                Enumerated = new List<int> { 9 },
                ReadOnlyListed = new List<int> { 10, 11 },
                SetOf = new HashSet<int> { 12 },
                Mapped = new Dictionary<string, int> { { "k", 13 } },
                ReadOnlyMapped = new Dictionary<string, int> { { "r", 14 } },
                StackedPoints = new Stack<WProtoRepeatedPoint>(
                    new[]
                    {
                        new WProtoRepeatedPoint { X = 1 },
                        new WProtoRepeatedPoint { Y = 2 },
                    }
                ),
            };

            WProtoStdlibCollectionContract restored = RoundTrip(value);

            CollectionAssert.AreEqual(value.Linked, restored.Linked);
            CollectionAssert.AreEqual(value.Queued, restored.Queued);
            CollectionAssert.AreEqual(value.Stacked, restored.Stacked);
            CollectionAssert.AreEqual(value.Listed, restored.Listed);
            CollectionAssert.AreEqual(value.Collected, restored.Collected);
            CollectionAssert.AreEqual(value.Enumerated, restored.Enumerated);
            CollectionAssert.AreEqual(value.ReadOnlyListed, restored.ReadOnlyListed);
            CollectionAssert.AreEquivalent(value.SetOf, restored.SetOf);
            CollectionAssert.AreEquivalent(value.Mapped, restored.Mapped);
            CollectionAssert.AreEquivalent(value.ReadOnlyMapped, restored.ReadOnlyMapped);
            CollectionAssert.AreEqual(value.StackedPoints, restored.StackedPoints);
        }

        [Test]
        public void AnInterfaceMemberIsLeftHoldingTheImplementationTheOraclePicks()
        {
            // Which concrete type a member holds after a round trip is a decision a consumer's code
            // runs against, so it matches protobuf-net's choice rather than being convenient.
            WProtoStdlibCollectionContract restored = RoundTrip(
                new WProtoStdlibCollectionContract
                {
                    Listed = new List<string> { "a" },
                    Collected = new List<int> { 1 },
                    Enumerated = new List<int> { 2 },
                    ReadOnlyListed = new List<int> { 3 },
                    SetOf = new HashSet<int> { 4 },
                    Mapped = new Dictionary<string, int> { { "k", 5 } },
                    ReadOnlyMapped = new Dictionary<string, int> { { "r", 6 } },
                }
            );

            Assert.AreEqual(typeof(List<string>), restored.Listed.GetType());
            Assert.AreEqual(typeof(List<int>), restored.Collected.GetType());
            Assert.AreEqual(typeof(List<int>), restored.Enumerated.GetType());
            Assert.AreEqual(typeof(List<int>), restored.ReadOnlyListed.GetType());
            Assert.AreEqual(typeof(HashSet<int>), restored.SetOf.GetType());
            Assert.AreEqual(typeof(Dictionary<string, int>), restored.Mapped.GetType());
            Assert.AreEqual(typeof(Dictionary<string, int>), restored.ReadOnlyMapped.GetType());
        }

        [Test]
        public void ACollectionThatCanOnlyBeConstructedRoundTrips()
        {
            WProtoConstructedCollectionContract value = new WProtoConstructedCollectionContract
            {
                Frozen = new ReadOnlyCollection<int>(new List<int> { 1, 2 }),
                FrozenMap = new ReadOnlyDictionary<string, int>(
                    new Dictionary<string, int> { { "k", 3 } }
                ),
            };

            Assert.AreEqual("0A02010212050A016B1003", Encode(value));

            WProtoConstructedCollectionContract restored = RoundTrip(value);
            CollectionAssert.AreEqual(new[] { 1, 2 }, restored.Frozen);
            Assert.AreEqual(3, restored.FrozenMap.ValueFor("k"));
        }

        [Test]
        public void EveryNewShapeAppendsAndOverwritesAsMeasured()
        {
            // Field 1 {1}, field 2 {1}, field 3 {1}, field 4 {1}, field 5 {1}, field 9 {"k": 1}.
            WProtoSeededStdlibContract restored = Decode(
                "0A0101" + "120101" + "1A0101" + "220101" + "2A0101" + "4A050A016B1001"
            );

            CollectionAssert.AreEqual(new[] { 7, 8, 1 }, restored.Linked);
            CollectionAssert.AreEqual(new[] { 7, 8, 1 }, restored.Queued);
            CollectionAssert.AreEqual(new[] { 1, 8, 7 }, restored.Stacked);
            CollectionAssert.AreEqual(new[] { 1 }, restored.OverwrittenStack);
            CollectionAssert.AreEqual(new[] { 7, 8, 1 }, restored.Listed);
            Assert.AreEqual(9, restored.Mapped.ValueFor("seed"));
            Assert.AreEqual(1, restored.Mapped.ValueFor("k"));
        }

        [Test]
        public void AnAbsentFieldLeavesEveryNewShapeAlone()
        {
            // "Absent" and "empty" are the same bytes, so the constructor's value has to survive an
            // empty payload -- including for a stack, whose commit runs after the read loop and
            // would otherwise replace it with a fresh one.
            WProtoSeededStdlibContract restored = Decode(string.Empty);

            CollectionAssert.AreEqual(new[] { 7, 8 }, restored.Linked);
            CollectionAssert.AreEqual(new[] { 7, 8 }, restored.Queued);
            CollectionAssert.AreEqual(new[] { 8, 7 }, restored.Stacked);
            CollectionAssert.AreEqual(new[] { 8, 7 }, restored.OverwrittenStack);
            CollectionAssert.AreEqual(new[] { 7, 8 }, restored.Listed);
            Assert.AreEqual(9, restored.Mapped.ValueFor("seed"));
        }

        [Test]
        public void AnEmptyOrNullCollectionWritesNothing()
        {
            Assert.AreEqual(string.Empty, Encode(new WProtoStdlibCollectionContract()));
            Assert.AreEqual(
                string.Empty,
                Encode(
                    new WProtoStdlibCollectionContract
                    {
                        Linked = new LinkedList<int>(),
                        Queued = new Queue<int>(),
                        Stacked = new Stack<int>(),
                        Listed = new List<string>(),
                        Collected = new List<int>(),
                        Enumerated = new List<int>(),
                        ReadOnlyListed = new List<int>(),
                        SetOf = new HashSet<int>(),
                        Mapped = new Dictionary<string, int>(),
                        ReadOnlyMapped = new Dictionary<string, int>(),
                        StackedPoints = new Stack<WProtoRepeatedPoint>(),
                    }
                )
            );
            Assert.AreEqual(string.Empty, Encode(new WProtoConstructedCollectionContract()));
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(buffer.Length, writer.Position, "Measure disagreed with Write");
            return ToHex(buffer);
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

        private static WProtoSeededStdlibContract Decode(string hex)
        {
            WProtoReader reader = new WProtoReader(Parse(hex));
            Assert.IsTrue(
                WProtoFormatterProvider
                    .Get<WProtoSeededStdlibContract>()
                    .TryRead(ref reader, out WProtoSeededStdlibContract value),
                hex
            );
            return value;
        }

        private static string ToHex(byte[] bytes)
        {
            StringBuilder builder = new StringBuilder(bytes.Length * 2);
            foreach (byte current in bytes)
            {
                builder.Append(current.ToString("X2"));
            }

            return builder.ToString();
        }

        private static byte[] Parse(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = System.Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }
    }
}
