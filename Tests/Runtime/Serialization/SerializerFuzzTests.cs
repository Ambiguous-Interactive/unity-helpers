// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization;
    using Serializer = WallstopStudios.UnityHelpers.Core.Serialization.Serializer;

    /// <summary>
    /// Fuzz tests random byte buffers through every public deserializer. Throwing variants must
    /// only ever throw <see cref="SerializationFailureException"/> (or a subclass) — never a raw
    /// framework exception. <c>Try*</c> variants must never throw at all on input/corrupt-data
    /// failures.
    /// </summary>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SerializerFuzzTests
    {
        private const int Iterations = 1024;

        [ProtoContract]
        private sealed class Sample
        {
            [ProtoMember(1)]
            public int Id { get; set; }

            [ProtoMember(2)]
            public string Name { get; set; }
        }

        [Test]
        public void ProtoDeserializeRandomBytesOnlyLeaksSerializationFailureException()
        {
            FuzzThrowing(bytes => Serializer.ProtoDeserialize<Sample>(bytes));
        }

        [Test]
        public void JsonDeserializeBytesRandomBytesOnlyLeaksSerializationFailureException()
        {
            FuzzThrowing(bytes => Serializer.JsonDeserialize<Sample>(bytes));
        }

        [Test]
        public void JsonDeserializeFastRandomBytesOnlyLeaksSerializationFailureException()
        {
            FuzzThrowing(bytes => Serializer.JsonDeserializeFast<Sample>(bytes));
        }

        [Test]
        public void BinaryDeserializeRandomBytesOnlyLeaksSerializationFailureException()
        {
            FuzzThrowing(bytes => Serializer.BinaryDeserialize<Sample>(bytes));
        }

        [Test]
        public void TryProtoDeserializeRandomBytesNeverThrows()
        {
            FuzzTry(bytes => Serializer.TryProtoDeserialize(bytes, out Sample _));
        }

        [Test]
        public void TryJsonDeserializeRandomBytesNeverThrows()
        {
            FuzzTry(bytes => Serializer.TryJsonDeserialize(bytes, out Sample _));
        }

        [Test]
        public void TryJsonDeserializeFastRandomBytesNeverThrows()
        {
            FuzzTry(bytes => Serializer.TryJsonDeserializeFast(bytes, out Sample _));
        }

        [Test]
        public void TryBinaryDeserializeRandomBytesNeverThrows()
        {
            FuzzTry(bytes => Serializer.TryBinaryDeserialize(bytes, out Sample _));
        }

        // ---------------------------------------------------------------------------
        // Allocation guard: the null-input fast path must not allocate. The exception
        // itself necessarily allocates, but the lazy Message means a caller who never
        // touches ex.Message pays no string-formatting cost.
        // ---------------------------------------------------------------------------

        [Test]
        public void NullInputFastPathDoesNotAllocateMessageString()
        {
            // Warm up the JIT, type initializers, and any test-runner internal caches.
            for (int i = 0; i < 16; i++)
            {
                try
                {
                    Serializer.ProtoDeserialize<Sample>(null);
                }
                catch (SerializationInputException) { }
            }

            // Take the MINIMUM over multiple runs to filter out background-GC / test-runner noise.
            long minThrowAlloc = long.MaxValue;
            long minMessageDelta = long.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                SerializationInputException captured = null;
                try
                {
                    Serializer.ProtoDeserialize<Sample>(null);
                }
                catch (SerializationInputException ex)
                {
                    captured = ex;
                }
                long afterThrow = GC.GetAllocatedBytesForCurrentThread();
                Assert.IsTrue(captured != null);
                _ = captured.Message;
                long afterMessage = GC.GetAllocatedBytesForCurrentThread();

                minThrowAlloc = Math.Min(minThrowAlloc, afterThrow - before);
                minMessageDelta = Math.Min(minMessageDelta, afterMessage - afterThrow);
            }

            // Composing Message must allocate at least one string — verifies lazy composition.
            Assert.Greater(
                minMessageDelta,
                0,
                "Composing ex.Message after the throw must allocate. If zero, the lazy path is "
                    + "broken (message was composed eagerly in the constructor)."
            );
            // Throwing without touching Message stays in a tight allocation envelope.
            Assert.LessOrEqual(
                minThrowAlloc,
                2048,
                "Throwing SerializationInputException allocated more than 2KB without Message access; "
                    + "check that the constructor does not eagerly format the message string."
            );
        }

        // ---------------------------------------------------------------------------
        // Helpers.
        // ---------------------------------------------------------------------------

        private static void FuzzThrowing(Action<byte[]> action)
        {
            Random rng = new(unchecked((int)0xCafeBabe));
            for (int i = 0; i < Iterations; i++)
            {
                byte[] payload = RandomPayload(rng, i);
                try
                {
                    action(payload);
                }
                catch (SerializationFailureException)
                {
                    // OK — documented exception type.
                }
                catch (Exception other)
                {
                    Assert.Fail(
                        "Iteration "
                            + i
                            + " (len="
                            + (payload?.Length.ToString() ?? "null")
                            + "): leaked non-SerializationFailureException: "
                            + other.GetType().FullName
                            + ": "
                            + other.Message
                    );
                }
            }
        }

        private static void FuzzTry(Func<byte[], bool> action)
        {
            Random rng = new(unchecked((int)0xDeadBeef));
            for (int i = 0; i < Iterations; i++)
            {
                byte[] payload = RandomPayload(rng, i);
                try
                {
                    _ = action(payload);
                }
                catch (SerializationTypeException)
                {
                    // Programmer-error path is allowed to propagate even from Try* — not relevant
                    // here because Sample is concrete.
                }
                catch (SerializationConfigurationException)
                {
                    // Same as above.
                }
                catch (Exception other)
                {
                    Assert.Fail(
                        "Iteration "
                            + i
                            + " (len="
                            + (payload?.Length.ToString() ?? "null")
                            + "): Try* must not throw, but it did: "
                            + other.GetType().FullName
                            + ": "
                            + other.Message
                    );
                }
            }
        }

        private static byte[] RandomPayload(Random rng, int i)
        {
            int kind = i % 8;
            return kind switch
            {
                0 => null,
                1 => Array.Empty<byte>(),
                2 => new byte[] { 0x00 },
                3 => Repeat((byte)0xFF, rng.Next(1, 32)),
                4 => Repeat((byte)0x00, rng.Next(1, 32)),
                5 => RandomBytes(rng, rng.Next(1, 256)),
                6 => RandomBytes(rng, rng.Next(256, 4096)),
                _ => RandomBytes(rng, rng.Next(4096, 16384)),
            };
        }

        private static byte[] Repeat(byte value, int count)
        {
            byte[] buf = new byte[count];
            for (int i = 0; i < count; i++)
            {
                buf[i] = value;
            }
            return buf;
        }

        private static byte[] RandomBytes(Random rng, int count)
        {
            byte[] buf = new byte[count];
            rng.NextBytes(buf);
            return buf;
        }
    }
}
