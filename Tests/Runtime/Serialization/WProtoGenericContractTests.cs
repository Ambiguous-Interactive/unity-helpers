// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Runs generated generic contracts inside Unity, on the editors and players CI builds.
    /// </summary>
    /// <remarks>
    /// The standalone legs are IL2CPP, and a closed generic is exactly what that compiler has to
    /// have seen ahead of time. Expected payloads were copied out of protobuf-net 3.2.56 for the
    /// same closures.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoGenericContractTests
    {
        [Test]
        public void TheFieldKeyChangesWithTheClosure()
        {
            /*
                Three closures of one contract, three wire types on the same field number. This is why a generic
                contract cannot be emitted with a wire-type constant.
            */
            Assert.AreEqual("0801", Encode(new WProtoBox<int> { Value = 1 }));
            Assert.AreEqual("09000000000000F03F", Encode(new WProtoBox<double> { Value = 1 }));
            Assert.AreEqual("0A0161", Encode(new WProtoBox<string> { Value = "a" }));
            Assert.AreEqual(
                "0A020801",
                Encode(
                    new WProtoBox<WProtoRepeatedPoint> { Value = new WProtoRepeatedPoint { X = 1 } }
                )
            );
        }

        [Test]
        public void AGenericMemberObeysTheOmissionRuleOfItsClosure()
        {
            Assert.AreEqual(string.Empty, Encode(new WProtoBox<int> { Value = 0 }));
            Assert.AreEqual(string.Empty, Encode(new WProtoBox<double> { Value = 0 }));
            Assert.AreEqual(string.Empty, Encode(new WProtoBox<string> { Value = null }));
            Assert.AreEqual("0A00", Encode(new WProtoBox<string> { Value = string.Empty }));
        }

        [Test]
        public void EveryClosureRoundTripsUnderIl2cpp()
        {
            WProtoBox<int> ints = RoundTrip(
                new WProtoBox<int>
                {
                    Value = 7,
                    Many = new[] { 1, 2 },
                    Trailer = 3,
                }
            );
            Assert.AreEqual(7, ints.Value);
            CollectionAssert.AreEqual(new[] { 1, 2 }, ints.Many);
            Assert.AreEqual(3, ints.Trailer);

            WProtoBox<string> texts = RoundTrip(
                new WProtoBox<string> { Value = "a", Many = new[] { "b", string.Empty } }
            );
            Assert.AreEqual("a", texts.Value);
            CollectionAssert.AreEqual(new[] { "b", string.Empty }, texts.Many);

            WProtoBox<WProtoRepeatedPoint> points = RoundTrip(
                new WProtoBox<WProtoRepeatedPoint>
                {
                    Value = new WProtoRepeatedPoint { X = 4, Y = 5 },
                    Many = new[] { new WProtoRepeatedPoint { X = 6 } },
                }
            );
            Assert.AreEqual(4, points.Value.X);
            Assert.AreEqual(5, points.Value.Y);
            Assert.AreEqual(6, points.Many[0].X);

            Assert.AreEqual(1.5, RoundTrip(new WProtoBox<double> { Value = 1.5 }).Value);
        }

        [Test]
        public void EveryClosureNamedInSourceIsRegisteredWithoutAnythingBeingCalled()
        {
            /*
                Registrars need discovered closed generic constructions; open-generic registration cannot serve
                consumer collections.
            */
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<WProtoBox<int>>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<WProtoBox<double>>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<WProtoBox<string>>());
            Assert.IsTrue(WProtoFormatterProvider.IsRegistered<WProtoBox<WProtoRepeatedPoint>>());
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEveryClosure()
        {
            AssertExact(new WProtoBox<int> { Value = int.MinValue, Many = new[] { 0, -1 } });
            AssertExact(new WProtoBox<double> { Value = double.NaN });
            AssertExact(new WProtoBox<string> { Value = new string('x', 200) });
            AssertExact(
                new WProtoBox<WProtoRepeatedPoint>
                {
                    Many = new[]
                    {
                        default,
                        new WProtoRepeatedPoint { X = 1 },
                    },
                }
            );
        }

        private static void AssertExact<T>(WProtoBox<T> value)
        {
            IWProtoFormatter<WProtoBox<T>> formatter = WProtoFormatterProvider.Get<WProtoBox<T>>();
            int predicted = formatter.Measure(value);
            byte[] buffer = new byte[predicted];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value), typeof(T).Name);
            Assert.AreEqual(predicted, writer.Position, typeof(T).Name);
        }

        private static string Encode<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            StringBuilder builder = new(writer.Position * 2);
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
            WProtoWriter writer = new(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));

            WProtoReader reader = new(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out T restored));
            return restored;
        }
    }
}
