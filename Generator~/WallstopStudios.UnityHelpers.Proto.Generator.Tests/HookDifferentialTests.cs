// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System.Collections.Generic;
    using System.IO;
    using NUnit.Framework;
    using ProtoBuf;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Records which lifecycle hooks each reader runs, and in which order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the only differential in the suite whose two oracles <b>disagree with each other</b>,
    /// which is why <c>WPROTO034</c> is a warning rather than a behaviour change. There is no single
    /// answer to conform to: protobuf-net 3.2.56 invokes the callbacks of the type that owns the wire
    /// shape and none of a subtype's, 2.4.9 invokes every level outermost-first, and this generator
    /// invokes every level innermost-first. Only a hook on the <b>root</b> of the chain runs exactly
    /// once under all three, which is what the diagnostic tells a developer to do and what
    /// <see cref="WallstopStudios.UnityHelpers.Core.Random.AbstractRandom"/> now does.
    /// </para>
    /// <para>
    /// Pinned rather than asserted-equal so the disagreement is a recorded fact. A protobuf-net
    /// upgrade that changes any of these rows should fail here and be read, not silently alter what
    /// a consumer's save does on load.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class HookDifferentialTests
    {
        private static readonly List<string> Trace = new List<string>();

        [Test]
        public void AHookOnTheRootRunsOnceUnderEveryReader()
        {
            // The placement WPROTO034 recommends. Both oracles and this generator agree on it, for
            // every one of the four hooks, whether the value is handed over as its own type or as
            // the base -- so a hook written here behaves the same in a WALLSTOP_PROTO build, a
            // WALLSTOP_PROTO-off build, and anything Serializer reaches reflectively.
            const string Expected = "Root.BeforeSer,Root.AfterSer,Root.BeforeDes,Root.AfterDes";

            HookLeaf value = new HookLeaf { Id = 1, LeafValue = 2 };

            Assert.AreEqual(Expected, Oracle(value));
            Assert.AreEqual(Expected, Generated(value));
            Assert.AreEqual(Expected, Oracle<HookRoot>(value));
            Assert.AreEqual(Expected, Generated<HookRoot>(value));
        }

        [Test]
        public void AHookOnASubtypeIsWhereTheReadersPartCompany()
        {
            SubHookLeaf value = new SubHookLeaf { Id = 1, LeafValue = 2 };

            // This generator runs it. So does 2.4.9. protobuf-net 3.2.56 runs NOTHING -- the hook a
            // developer wrote is dead in every build that reader serves, silently, which is the
            // whole content of WPROTO034.
#if PROTOBUF_NET_ORACLE_V2
            const string ExpectedOracle =
                "Leaf.BeforeSer,Leaf.AfterSer,Leaf.BeforeDes,Leaf.AfterDes";
#else
            const string ExpectedOracle = "";
#endif
            Assert.AreEqual(ExpectedOracle, Oracle(value));
            Assert.AreEqual(ExpectedOracle, Oracle<SubHookRoot>(value));

            Assert.AreEqual(
                "Leaf.BeforeSer,Leaf.AfterSer,Leaf.BeforeDes,Leaf.AfterDes",
                Generated(value)
            );
            Assert.AreEqual(
                "Leaf.BeforeSer,Leaf.AfterSer,Leaf.BeforeDes,Leaf.AfterDes",
                Generated<SubHookRoot>(value)
            );
        }

        [Test]
        public void EveryLevelDeclaringAHookDisagreesOnTheSetAndOnTheOrder()
        {
            // Three levels, one after-deserialization hook each. The disagreement is not only about
            // WHICH hooks run: where 2.4.9 and this generator run the same three, they run them in
            // opposite orders, so a hook whose work depends on a base's having finished is correct
            // under one reader and wrong under the other.
            EveryLevelLeaf value = new EveryLevelLeaf();

#if PROTOBUF_NET_ORACLE_V2
            const string ExpectedOracle = "Root.AfterDes,Middle.AfterDes,Leaf.AfterDes";
#else
            const string ExpectedOracle = "Root.AfterDes";
#endif
            Assert.AreEqual(ExpectedOracle, Oracle(value));
            Assert.AreEqual(ExpectedOracle, Oracle<EveryLevelRoot>(value));

            Assert.AreEqual("Leaf.AfterDes,Middle.AfterDes,Root.AfterDes", Generated(value));
        }

        [Test]
        public void SkipConstructorDoesNotSuppressAHookOnEitherOracle()
        {
            // WPROTO033 claimed the opposite until this was measured. What actually kept
            // DotNetRandom's hook from running under protobuf-net was its being a SUBTYPE; the flag
            // was never the reason, and a developer following the old message would have moved the
            // work somewhere it was not needed.
            Assert.AreEqual(
                "Skip.BeforeSer,Skip.AfterSer,Skip.BeforeDes,Skip.AfterDes",
                Oracle(new SkippingHookContract { Value = 3 })
            );
        }

        private static string Oracle<T>(T value)
        {
            Trace.Clear();
            using MemoryStream stream = new MemoryStream();
            Serializer.Serialize(stream, value);
            stream.Position = 0;
            Serializer.Deserialize<T>(stream);
            return string.Join(",", Trace);
        }

        private static string Generated<T>(T value)
        {
            Trace.Clear();
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            formatter.Write(ref writer, value);
            WProtoReader reader = new WProtoReader(buffer);
            Assert.IsTrue(formatter.TryRead(ref reader, out T _));
            return string.Join(",", Trace);
        }

        internal static void Record(string entry)
        {
            Trace.Add(entry);
        }
    }
}
