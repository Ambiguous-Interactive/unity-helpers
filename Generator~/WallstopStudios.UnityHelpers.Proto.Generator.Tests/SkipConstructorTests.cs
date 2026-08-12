// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins <c>SkipConstructor</c>, which decides whether reading a contract runs code its author
    /// wrote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Five of this package's pseudo-random generators carry
    /// <c>[ProtoContract(SkipConstructor = true)]</c> and the flag is load-bearing on every one of
    /// them: the parameterless constructor seeds a live generator from a fresh <c>Guid</c>, and the
    /// after-deserialization hook that rebuilds the generator from the saved seed returns early when
    /// one already exists. A formatter that ran the constructor would therefore hand back a generator
    /// on a <b>random</b> stream rather than the saved one -- identical bytes on the wire, a
    /// different game on reload, and nothing to report it.
    /// </para>
    /// <para>
    /// The flag was declared on <c>WProtoContractAttribute</c>, documented, and ignored by the
    /// generator, which is worse than absent: it reads as handled.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class SkipConstructorTests
    {
        [SetUp]
        public void Reset()
        {
            ConstructorWitness.Constructions = 0;
        }

        [Test]
        public void AContractDeclaringSkipConstructorDoesNotRunIt()
        {
            Read<SkippingContract>("0807");

            Assert.AreEqual(
                0,
                ConstructorWitness.Constructions,
                "The author's constructor ran, so anything it initialized shadows the payload."
            );
        }

        [Test]
        public void AContractWithoutTheFlagStillRunsIt()
        {
            Read<ConstructingContract>("0807");

            Assert.AreEqual(1, ConstructorWitness.Constructions);
        }

        /// <summary>
        /// The payload wins over whatever the type would otherwise have set up for itself.
        /// </summary>
        /// <remarks>
        /// This is the pseudo-random generators' exact shape: a hook that rebuilds derived state
        /// only when it is missing, which a constructor would have filled in first.
        /// </remarks>
        [Test]
        public void TheHookRebuildsFromThePayloadRatherThanFromTheConstructor()
        {
            SkippingContract read = Read<SkippingContract>("0807");

            Assert.AreEqual(7, read.Seed);
            Assert.AreEqual("rebuilt-from-7", read.Derived);
        }

        [Test]
        public void APresentRepeatedMemberReplacesItsInitializer()
        {
            SkippingContract read = Read<SkippingContract>("080710011002");

            CollectionAssert.AreEqual(
                new[] { 1, 2 },
                read.Values,
                "SkipConstructor must match protobuf-net's uninitialized collection seed."
            );
        }

        [Test]
        public void WithoutTheFlagTheConstructorsDerivedStateSurvivesTheHook()
        {
            ConstructingContract read = Read<ConstructingContract>("0807");

            Assert.AreEqual(7, read.Seed);
            Assert.AreEqual(
                "constructed",
                read.Derived,
                "The hook returned early because the constructor had already produced derived state, "
                    + "which is the failure SkipConstructor exists to avoid."
            );
        }

        /// <summary>
        /// A skipped constructor is still a constructor, so field initializers and base constructors
        /// run.
        /// </summary>
        /// <remarks>
        /// protobuf-net allocates the object uninitialized instead, so neither runs there. The
        /// difference only ever makes the object more initialized, and it is asserted rather than
        /// assumed because it is the one place this implementation departs from the oracle.
        /// </remarks>
        [Test]
        public void FieldInitializersAndBaseConstructorsStillRun()
        {
            SkippingContract read = Read<SkippingContract>("0807");

            Assert.IsNotNull(read.Scratch, "the base class field initializer did not run");
            Assert.AreEqual(16, read.Scratch.Length);
            Assert.AreEqual("initialized", read.FromInitializer);
        }

        /// <summary>
        /// The flag leaves a type that writes no constructor of its own constructible.
        /// </summary>
        /// <remarks>
        /// The real assertion is that this file compiles: <c>new NoConstructorContract()</c> resolves
        /// to the implicit parameterless constructor, which a generated one would have removed. The
        /// runtime half checks the type still reads, so the case is not merely compiled and ignored.
        /// </remarks>
        [Test]
        public void TheFlagDoesNotRemoveAnImplicitConstructor()
        {
            NoConstructorContract fresh = new NoConstructorContract();

            Assert.AreEqual(0, fresh.Seed);
            Assert.AreEqual(7, Read<NoConstructorContract>("0807").Seed);
        }

        [Test]
        public void SkipConstructorDoesNotChangeTheBytes()
        {
            SkippingContract value = Read<SkippingContract>("0807");

            using MemoryStream stream = new MemoryStream();
            ProtoBuf.Serializer.Serialize(stream, value);

            IWProtoFormatter<SkippingContract> formatter =
                WProtoFormatterProvider.Get<SkippingContract>();
            byte[] mine = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(mine);
            Assert.IsTrue(formatter.Write(ref writer, value));

            Assert.AreEqual(BitConverter.ToString(stream.ToArray()), BitConverter.ToString(mine));
        }

        private static T Read<T>(string hex)
        {
            byte[] bytes = new byte[hex.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
            {
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            WProtoReader reader = new WProtoReader(bytes);
            Assert.IsTrue(
                WProtoFormatterProvider.Get<T>().TryRead(ref reader, out T value),
                "read refused " + hex
            );
            return value;
        }
    }
}
