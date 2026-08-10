// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Text;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Runs generated surrogate code inside Unity, against a real engine type.
    /// </summary>
    /// <remarks>
    /// This is the shape that matters for the facade swap: a game's save data is full of
    /// <see cref="Vector3"/>, and no attribute of ours can be put on it. Expected payloads were
    /// copied out of protobuf-net 3.2.56 for an identical surrogate.
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoSurrogateContractTests
    {
        [Test]
        public void ASurrogatedMemberIsExactlyTheSurrogatesShape()
        {
            Assert.AreEqual(
                "0A0F0D0000803F15000000401D00004040",
                Encode(new WProtoSurrogateHolder { Position = new Vector3(1, 2, 3) })
            );
        }

        [Test]
        public void ADefaultSurrogatedStructIsStillWritten()
        {
            // The struct sub-message rule reached through a surrogate: a tag and a zero length,
            // rather than nothing.
            Assert.AreEqual("0A00", Encode(new WProtoSurrogateHolder()));
            Assert.AreEqual("0A001805", Encode(new WProtoSurrogateHolder { Trailer = 5 }));
        }

        [Test]
        public void ASurrogatedValueRoundTripsBackToTheEngineTypeUnderIl2cpp()
        {
            WProtoSurrogateHolder restored = RoundTrip(
                new WProtoSurrogateHolder
                {
                    Position = new Vector3(1, 2, 3),
                    Path = new[] { new Vector3(4, 0, 0), Vector3.zero },
                    Trailer = 7,
                }
            );

            Assert.AreEqual(new Vector3(1, 2, 3), restored.Position);
            Assert.AreEqual(2, restored.Path.Length);
            Assert.AreEqual(new Vector3(4, 0, 0), restored.Path[0]);
            Assert.AreEqual(Vector3.zero, restored.Path[1]);
            Assert.AreEqual(7, restored.Trailer);
        }

        [Test]
        public void MeasurePredictsWriteExactlyForEverySurrogatedShape()
        {
            WProtoSurrogateHolder[] cases =
            {
                new(),
                new() { Position = new Vector3(float.MaxValue, float.NaN, -1f) },
                new() { Path = new Vector3[64] },
            };

            IWProtoFormatter<WProtoSurrogateHolder> formatter =
                WProtoFormatterProvider.Get<WProtoSurrogateHolder>();

            foreach (WProtoSurrogateHolder value in cases)
            {
                int predicted = formatter.Measure(value);
                byte[] buffer = new byte[predicted];
                WProtoWriter writer = new(buffer);
                Assert.IsTrue(formatter.Write(ref writer, value));
                Assert.AreEqual(predicted, writer.Position);
            }
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
