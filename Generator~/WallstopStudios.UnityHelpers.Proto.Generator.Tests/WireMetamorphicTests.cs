// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Rewrites encoded payloads into other legal spellings of the same value and requires the
    /// decode to be unchanged.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The differential suites next door compare this package against protobuf-net on the bytes
    /// <b>either encoder chose to write</b>. That leaves a whole class of payload untested: the ones
    /// a third encoder would have written. Protobuf permits fields in any order, permits a field the
    /// reader has never heard of, and defines two occurrences of a sub-message as their merge -- so
    /// a decoder can pass every round trip and every differential in the repository and still
    /// mishandle a payload some other library, or a later version of this one, legitimately emits.
    /// </para>
    /// <para>
    /// Each transform is checked for having actually fired. A metamorphic battery whose transforms
    /// quietly become no-ops is the failure mode that matters here, because it reads exactly like a
    /// passing suite.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class WireMetamorphicTests
    {
        [Test]
        public void ReorderingFieldsDoesNotChangeWhatAPayloadMeans()
        {
            int transformed = 0;
            foreach (object value in Corpus())
            {
                byte[] original = Encode(value);
                byte[] reordered = WireMetamorphic.ReverseFieldOrder(original);
                if (reordered == null)
                {
                    continue;
                }

                transformed++;
                Assert.AreNotEqual(
                    ToHex(original),
                    ToHex(reordered),
                    "the transform reported a rewrite it did not make"
                );
                AssertDecodesTheSame(value, reordered, "reordered");
            }

            // The low floor catches a no-op transform without coupling the test to the corpus size.
            Assert.Greater(transformed, 5, "the reordering transform stopped firing");
        }

        [Test]
        public void AFieldTheContractDoesNotDeclareIsSkippedWhereverItSits()
        {
            int transformed = 0;
            foreach (object value in Corpus())
            {
                byte[] original = Encode(value);
                foreach (
                    byte[] injected in WireMetamorphic.InjectUnknownField(
                        original,
                        DeclaredTags(value.GetType())
                    )
                )
                {
                    transformed++;
                    Assert.Greater(
                        injected.Length,
                        original.Length,
                        "the injected field added no bytes"
                    );
                    AssertDecodesTheSame(value, injected, "unknown field injected");
                }
            }

            Assert.Greater(transformed, 20, "the injection transform stopped firing");
        }

        /// <summary>
        /// Two occurrences of a sub-message field decode as their merge, not as the second one.
        /// </summary>
        /// <remarks>
        /// The package already relies on this and has been wrong about it once: decoding each
        /// occurrence in turn made the second REPLACE the first, silently dropping the members only
        /// the first carried. That bug is invisible to a round trip, because this encoder never
        /// writes a field twice.
        /// </remarks>
        [Test]
        public void ASubMessageSplitInTwoDecodesAsTheMergeOfItsHalves()
        {
            int transformed = 0;
            foreach (object value in Corpus())
            {
                byte[] original = Encode(value);
                foreach (
                    byte[] split in WireMetamorphic.SplitSubMessages(
                        original,
                        MessageTags(value.GetType())
                    )
                )
                {
                    transformed++;
                    AssertDecodesTheSame(value, split, "sub-message split");
                }
            }

            Assert.Greater(transformed, 0, "the sub-message split transform stopped firing");
        }

        /// <summary>
        /// The transforms are legal, and protobuf-net agrees they are.
        /// </summary>
        /// <remarks>
        /// Without this the battery would only prove this package is self-consistent under its own
        /// idea of what a legal rewrite is. Running the same rewritten bytes through the oracle is
        /// what makes "legal" mean protobuf rather than "whatever WallstopProto accepts".
        /// </remarks>
        [Test]
        public void TheOracleReadsEveryRewrittenPayloadTheSameWay()
        {
            int checks = 0;
            foreach (RepeatedContract value in RepeatedCorpus())
            {
                byte[] original = Encode(value);
                List<byte[]> rewritten = new List<byte[]>();
                byte[] reordered = WireMetamorphic.ReverseFieldOrder(original);
                if (reordered != null)
                {
                    rewritten.Add(reordered);
                }

                rewritten.AddRange(
                    WireMetamorphic.InjectUnknownField(
                        original,
                        DeclaredTags(typeof(RepeatedContract))
                    )
                );

                foreach (byte[] payload in rewritten)
                {
                    checks++;
                    using System.IO.MemoryStream stream = new System.IO.MemoryStream(payload);
                    RepeatedContract theirs = ProtoBuf.Serializer.Deserialize<RepeatedContract>(
                        stream
                    );
                    Assert.AreEqual(
                        ToHex(Encode(value)),
                        ToHex(Encode(theirs)),
                        "protobuf-net read a rewritten payload differently"
                    );
                }
            }

            Assert.Greater(checks, 10, "the oracle cross-check stopped firing");
        }

        private static void AssertDecodesTheSame(object value, byte[] payload, string context)
        {
            /*
             * Re-encoding compares contracts without Equals and handles NaN values that are unequal to
             * themselves.
             */
            Assert.AreEqual(ToHex(Encode(value)), ToHex(Decode(value.GetType(), payload)), context);
        }

        private static IEnumerable<object> Corpus()
        {
            foreach (RepeatedContract repeated in RepeatedCorpus())
            {
                yield return repeated;
            }

            yield return new ScalarContract
            {
                Int32 = -7,
                Int64 = 1L << 40,
                UInt32 = 9u,
                Flag = true,
                Single = 1.5f,
                Double = -2.5d,
                Text = "metamorphic",
                Bytes = new byte[] { 1, 2, 3 },
                Int16 = -3,
                Hidden = 11,
                Counted = 13,
            };

            yield return new OutOfOrderContract
            {
                First = 1,
                Third = 3,
                Fourth = 4,
            };

            yield return new ZigZagContract
            {
                Int32 = -12345,
                Int64 = -1234567890123L,
                Int16 = -321,
                Int8 = -21,
                MaybeInt32 = -7,
                Plain = -7,
            };

            yield return new NestingContract
            {
                Id = 5,
                Where = new Outer.Point { X = 2, Y = 3 },
                MaybeWhere = new Outer.Point { X = -4, Y = 5 },
            };

            yield return new DeepContract
            {
                Id = 9,
                Child = new NestingContract
                {
                    Id = 8,
                    Where = new Outer.Point { X = 1, Y = 2 },
                },
            };
        }

        private static IEnumerable<RepeatedContract> RepeatedCorpus()
        {
            yield return new RepeatedContract { Ints = new[] { 1, 2, 3 } };
            yield return new RepeatedContract
            {
                Ints = new[] { 0, -1, int.MaxValue },
                IntList = new List<int> { 7, 8 },
                Texts = new[] { "a", "bb" },
            };
            yield return new RepeatedContract
            {
                Doubles = new[] { 0.5d, -1.5d },
                Longs = new[] { 1UL, ulong.MaxValue },
                Flags = new[] { true, false, true },
            };
            yield return new RepeatedContract
            {
                Ints = Array.Empty<int>(),
                Texts = new[] { string.Empty },
            };
            yield return new RepeatedContract
            {
                Ints = new[] { 5 },
                IntList = new List<int> { 6 },
                Texts = new[] { "c" },
                Doubles = new[] { 7.5d },
                Longs = new[] { 8UL },
                Flags = new[] { false },
            };
            yield return new RepeatedContract
            {
                IntList = new List<int> { -1, -2, -3 },
                Longs = new[] { 0UL, 1UL },
                Modes = new[] { Mode.Fast, Mode.Careful },
            };
            yield return new RepeatedContract
            {
                Texts = new[] { "one", "two", "three" },
                Flags = new[] { true },
                Doubles = new[] { double.MaxValue, double.Epsilon },
            };
        }

        /// <summary>Every field number the contract declares, read off its own annotations.</summary>
        /// <remarks>
        /// Reflection is fine here and nowhere near the serializer: these contracts live in the test
        /// assembly, which does load, and the alternative is a hand-kept list of tags that would
        /// drift silently -- turning an "unknown field" into a second value for a real member and
        /// asserting something false about it.
        /// </remarks>
        private static ISet<int> DeclaredTags(Type contract)
        {
            HashSet<int> tags = new HashSet<int>();
            foreach (MemberInfo member in Members(contract))
            {
                foreach (CustomAttributeData data in member.GetCustomAttributesData())
                {
                    if (
                        data.AttributeType == typeof(WProtoMemberAttribute)
                        && 0 < data.ConstructorArguments.Count
                    )
                    {
                        tags.Add((int)data.ConstructorArguments[0].Value);
                    }
                }
            }

            return tags;
        }

        /// <summary>The field numbers whose member is itself a non-repeated contract.</summary>
        private static ISet<int> MessageTags(Type contract)
        {
            HashSet<int> tags = new HashSet<int>();
            foreach (MemberInfo member in Members(contract))
            {
                Type declared =
                    member is FieldInfo field ? field.FieldType
                    : member is PropertyInfo property ? property.PropertyType
                    : null;
                if (declared == null)
                {
                    continue;
                }

                Type underlying = Nullable.GetUnderlyingType(declared) ?? declared;
                if (!underlying.GetCustomAttributes(false).Any(IsContract))
                {
                    continue;
                }

                foreach (CustomAttributeData data in member.GetCustomAttributesData())
                {
                    if (
                        data.AttributeType == typeof(WProtoMemberAttribute)
                        && 0 < data.ConstructorArguments.Count
                    )
                    {
                        tags.Add((int)data.ConstructorArguments[0].Value);
                    }
                }
            }

            return tags;
        }

        private static bool IsContract(object attribute)
        {
            return attribute is WProtoContractAttribute;
        }

        private static IEnumerable<MemberInfo> Members(Type contract)
        {
            return contract.GetMembers(
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly
            );
        }

        private static byte[] Encode(object value)
        {
            return (byte[])
                typeof(WireMetamorphicTests)
                    .GetMethod(nameof(EncodeTyped), BindingFlags.NonPublic | BindingFlags.Static)
                    .MakeGenericMethod(value.GetType())
                    .Invoke(null, new[] { value });
        }

        private static byte[] EncodeTyped<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            return buffer;
        }

        private static byte[] Decode(Type contract, byte[] payload)
        {
            return (byte[])
                typeof(WireMetamorphicTests)
                    .GetMethod(nameof(DecodeTyped), BindingFlags.NonPublic | BindingFlags.Static)
                    .MakeGenericMethod(contract)
                    .Invoke(null, new object[] { payload });
        }

        private static byte[] DecodeTyped<T>(byte[] payload)
        {
            WProtoReader reader = new WProtoReader(payload);
            Assert.IsTrue(
                WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T value),
                "a legal rewrite did not decode at all"
            );
            return EncodeTyped(value);
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
    }
}
