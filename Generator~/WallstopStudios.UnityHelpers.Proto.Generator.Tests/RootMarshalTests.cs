// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;
    using System.Text;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins the root marshal: a type whose bytes at the root are a wrapper's, and whose bytes
    /// anywhere else are untouched.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Seven of this package's collections have two encodings, chosen by position.
    /// <c>Serializer.ProtoSerialize(aDeque)</c> writes <c>DequeProtoWrapper</c> -- items plus
    /// capacity -- because protobuf-net cannot be told to stop treating the type as a list; the same
    /// deque held as a member of another contract is written as an ordinary repeated field. Both are
    /// in shipped save files, so the port has to reproduce both.
    /// </para>
    /// <para>
    /// The failure this fixture exists to catch is silent in every other one: a marshal that leaked
    /// into <see cref="WProtoFormatterProvider"/> would be found by
    /// <see cref="WProtoGeneric{T}"/> from a member position and rewrite that member as the wrapper,
    /// producing bytes no protobuf-net reader ever wrote and no test that only round-trips through
    /// this package would notice.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class RootMarshalTests
    {
        /// <summary>
        /// A marshalled root writes the wrapper's fields, packed as this package writes every
        /// packable run.
        /// </summary>
        /// <remarks>
        /// Byte-identity with the oracle is deliberately NOT the contract for a packable repeated
        /// member -- this package writes <c>0A 03 01 AC 02</c> where protobuf-net at
        /// CompatibilityLevel 200 writes <c>08 01 08 AC 02</c>, which is roughly half the size and
        /// which protobuf-net decodes exactly. So the bytes are pinned literally, and the agreement
        /// is asserted in both directions below.
        /// </remarks>
        [Test]
        public void AMarshalledRootWritesTheWrappersFields()
        {
            StandInRing<int> ring = new StandInRing<int> { Capacity = 7 };
            ring.Add(1);
            ring.Add(300);

            Assert.AreEqual("0A0301AC021007", MarshalHex(ring));
        }

        /// <summary>
        /// A payload the SHIPPED wrapper path wrote still loads through the marshal.
        /// </summary>
        /// <remarks>
        /// This is the compatibility claim the whole port rests on. Every save file a consumer has
        /// written so far came out of <c>Serializer</c>'s reflection wrapper path, which is
        /// protobuf-net writing the wrapper -- unpacked. A marshal that only read its own output
        /// would pass every round-trip in this fixture and fail on the first real save.
        /// </remarks>
        [Test]
        public void TheMarshalReadsWhatTheShippedWrapperPathWrote()
        {
            byte[] theirs = OracleBytes(
                new StandInRingWrapper<int> { Items = new[] { 1, 300 }, Capacity = 7 }
            );

            Assert.AreEqual("080108AC021007", ToHex(theirs), "the oracle stopped writing unpacked");
            Assert.IsTrue(WProtoFacade.TryDeserialize(theirs, out StandInRing<int> restored));
            Assert.AreEqual(7, restored.Capacity);
            CollectionAssert.AreEqual(new[] { 1, 300 }, restored.ToArray());
        }

        /// <summary>
        /// The staging hook runs once per serialization, from Measure and not from Write.
        /// </summary>
        /// <remarks>
        /// Each of the real collections stages its serialized array in a callback, and
        /// <c>IWProtoFormatter&lt;T&gt;</c> puts that in Measure precisely once: a length prefix is
        /// emitted from the measurement, and a hook that also ran from Write would leak one rental
        /// per write for any hook that pools. An idempotent body hides the second call, so this
        /// counts them.
        /// </remarks>
        [Test]
        public void TheStagingHookRunsOncePerSerialization()
        {
            StandInRing<int> ring = new StandInRing<int>();
            ring.Add(1);

            Assert.AreEqual("0A0101", MarshalHex(ring));
            Assert.AreEqual(1, ring.StageCount);
        }

        [Test]
        public void AnEmptyMarshalledRootIsTheWrappersEmptyBytes()
        {
            Assert.AreEqual(
                ToHex(OracleBytes(new StandInRingWrapper<int>())),
                MarshalHex(new StandInRing<int>())
            );
            Assert.AreEqual(string.Empty, MarshalHex(new StandInRing<int>()));
        }

        [Test]
        public void ANonGenericMarshalledRootWritesItsWrappersFields()
        {
            StandInBag bag = new StandInBag();
            bag.Elements.Add(4);
            bag.Elements.Add(5);

            Assert.AreEqual("0A020405", MarshalHex(bag));

            byte[] theirs = OracleBytes(new StandInBagWrapper { Elements = new[] { 4, 5 } });
            Assert.IsTrue(WProtoFacade.TryDeserialize(theirs, out StandInBag restored));
            CollectionAssert.AreEqual(new[] { 4, 5 }, restored.Elements);
        }

        [Test]
        public void TheOracleDecodesWhatTheMarshalWrote()
        {
            StandInRing<int> ring = new StandInRing<int> { Capacity = 3 };
            ring.Add(9);

            using (MemoryStream stream = new MemoryStream(Parse(MarshalHex(ring))))
            {
                StandInRingWrapper<int> theirs = ProtoBuf.Serializer.Deserialize<
                    StandInRingWrapper<int>
                >(stream);
                Assert.AreEqual(3, theirs.Capacity);
                CollectionAssert.AreEqual(new[] { 9 }, theirs.Items);
            }
        }

        [Test]
        public void AMarshalledRootRoundTrips()
        {
            StandInRing<int> ring = new StandInRing<int> { Capacity = 5 };
            ring.Add(-1);
            ring.Add(0);
            ring.Add(int.MaxValue);

            Assert.IsTrue(WProtoFacade.TrySerialize(ring, out byte[] bytes));
            Assert.IsTrue(WProtoFacade.TryDeserialize(bytes, out StandInRing<int> restored));
            Assert.AreEqual(5, restored.Capacity);
            CollectionAssert.AreEqual(new[] { -1, 0, int.MaxValue }, restored.ToArray());
        }

        [Test]
        public void AnEmptyMarshalledRootReadsBackAsAnEmptyCollection()
        {
            Assert.IsTrue(
                WProtoFacade.TryDeserialize(Array.Empty<byte>(), out StandInRing<int> restored)
            );
            Assert.IsTrue(restored != null);
            Assert.AreEqual(0, restored.Count);
        }

        /// <summary>
        /// The facade serves a marshalled root, which is what makes the seven types reachable at all.
        /// </summary>
        [Test]
        public void TheFacadeServesAMarshalledRoot()
        {
            StandInRing<int> ring = new StandInRing<int>();
            ring.Add(2);

            Assert.IsTrue(WProtoFacade.TrySerialize(ring, out byte[] bytes));
            Assert.AreEqual("0A0102", ToHex(bytes));

            using (MemoryStream stream = new MemoryStream(bytes))
            {
                StandInRingWrapper<int> theirs = ProtoBuf.Serializer.Deserialize<
                    StandInRingWrapper<int>
                >(stream);
                CollectionAssert.AreEqual(new[] { 2 }, theirs.Items);
            }
        }

        /// <summary>
        /// A marshal whose element type WallstopProto cannot encode declines instead of throwing.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A contract formatter is registered only for a type someone annotated. A marshal is
        /// registered for <b>every</b> closed construction of the collection found in source,
        /// whatever the element type is — so the facade would claim a request it cannot finish, and
        /// the throw would land inside <c>Measure</c> where the old code fell through to
        /// protobuf-net and round-tripped.
        /// </para>
        /// <para>
        /// Not hypothetical: this package's own tests name <c>Deque&lt;Vector2&gt;</c>, and
        /// <c>Vector2</c> has a protobuf-net <b>surrogate</b> rather than a WallstopProto formatter —
        /// a surrogate is substituted at generate time, and the element of a generic wrapper is not
        /// known then. Every enum is the same shape. An empty collection hides it, because the
        /// element loop never runs, so the first failure would be on real data.
        /// </para>
        /// </remarks>
        [Test]
        public void AMarshalWhoseElementCannotBeEncodedIsNotServed()
        {
            StandInRing<Unserviceable> ring = new StandInRing<Unserviceable>();
            ring.Add(new Unserviceable { Value = 1 });

            Assert.IsTrue(
                WProtoRootMarshalProvider.IsRegistered<StandInRing<Unserviceable>>(),
                "the registration is what makes the decline necessary"
            );
            Assert.IsFalse(
                WProtoFacade.TrySerialize(ring, out byte[] _),
                "the facade claimed a request whose elements it cannot encode"
            );
            Assert.IsFalse(
                WProtoFacade.TryDeserialize(
                    new byte[] { 0x0A, 0x02, 0x08, 0x01 },
                    out StandInRing<Unserviceable> _
                ),
                "the read half has the same hole as the write half"
            );
        }

        /// <summary>
        /// An empty collection whose element cannot be encoded is refused too.
        /// </summary>
        /// <remarks>
        /// The element loop never runs for an empty one, so a check that only fired on the first
        /// element would serve this and produce a payload the reader cannot decode back.
        /// </remarks>
        [Test]
        public void AnEmptyMarshalOfAnUnserviceableElementIsNotServedEither()
        {
            Assert.IsFalse(
                WProtoFacade.TrySerialize(new StandInRing<Unserviceable>(), out byte[] _)
            );
        }

        /// <summary>
        /// A generic CONTRACT closed over an element it cannot encode declines too.
        /// </summary>
        /// <remarks>
        /// The same hole as a marshal's, and the one that was there first: a generic contract's
        /// formatter is registered for every closure found in source, and a member typed as its
        /// parameter resolves through <see cref="WProtoGeneric{T}"/> — which finds nothing for an
        /// enum or a surrogate's real type. <c>SerializableList&lt;Vector2&gt;</c> is the shipped
        /// shape of this. The generator emits <see cref="IWProtoConditionalFormatter"/> on any
        /// contract whose members encode one of its own type parameters.
        /// </remarks>
        [Test]
        public void AGenericContractClosedOverAnElementItCannotEncodeIsNotServed()
        {
            Assert.IsTrue(
                WProtoFormatterProvider.IsRegistered<Box<Unserviceable>>(),
                "the registration is what makes the decline necessary"
            );
            Assert.IsFalse(
                WProtoFacade.TrySerialize(
                    new Box<Unserviceable> { Value = new Unserviceable { Value = 1 } },
                    out byte[] _
                ),
                "the facade claimed a closure whose element it cannot encode"
            );
            Assert.IsFalse(
                WProtoFacade.TryDeserialize(
                    new byte[] { 0x0A, 0x02, 0x08, 0x01 },
                    out Box<Unserviceable> _
                )
            );
        }

        /// <summary>
        /// A generic contract closed over another generic contract that declines is refused too.
        /// </summary>
        /// <remarks>
        /// The outer closure resolves a registered message formatter, but registration alone does
        /// not prove the nested closure can serve its own type argument. Treating every registered
        /// message as encodable makes the facade claim this value and throw only after measurement
        /// reaches the unserviceable leaf.
        /// </remarks>
        [Test]
        public void AGenericContractClosedOverADecliningGenericContractIsNotServed()
        {
            Box<Box<Unserviceable>> value = new Box<Box<Unserviceable>>
            {
                Value = new Box<Unserviceable> { Value = new Unserviceable { Value = 1 } },
            };

            Assert.IsTrue(
                WProtoFormatterProvider.IsRegistered<Box<Box<Unserviceable>>>(),
                "the outer registration is what makes the nested service check necessary"
            );
            Assert.IsTrue(
                WProtoFormatterProvider.TryGet(
                    out IWProtoFormatter<Box<Unserviceable>> innerFormatter
                ),
                "the inner formatter must exist, or absence alone makes the outer closure decline"
            );
            Assert.IsTrue(
                innerFormatter is IWProtoConditionalFormatter,
                "the regression must exercise a registered formatter's conditional decision"
            );
            Assert.IsFalse(
                WProtoFacade.TrySerialize(value, out byte[] _),
                "the facade claimed an outer closure whose nested formatter declines"
            );
            Assert.IsFalse(
                WProtoFacade.TryDeserialize(
                    new byte[] { 0x0A, 0x02, 0x08, 0x01 },
                    out Box<Box<Unserviceable>> _
                ),
                "the read half must consult the same nested service predicate"
            );
        }

        /// <summary>A later registration replaces the formatter a generic closure cached.</summary>
        [Test]
        public void RegisterAfterGenericResolutionReplacesTheCachedFormatter()
        {
            WProtoFormatterProvider.TryGet(out IWProtoFormatter<Unserviceable> originalFormatter);
            try
            {
                ConditionalUnserviceableFormatter declining = new ConditionalUnserviceableFormatter(
                    false
                );
                ConditionalUnserviceableFormatter serving = new ConditionalUnserviceableFormatter(
                    true
                );

                WProtoFormatterProvider.Register<Unserviceable>(declining);
                Assert.IsFalse(WProtoGeneric<Unserviceable>.CanEncode);

                WProtoFormatterProvider.Register<Unserviceable>(serving);
                Assert.IsTrue(WProtoGeneric<Unserviceable>.CanEncode);
                Assert.AreSame(serving, WProtoFormatterProvider.Get<Unserviceable>());
                Assert.AreEqual(
                    2,
                    WProtoGeneric<Unserviceable>.MeasureField(1, new Unserviceable { Value = 7 })
                );
                Assert.AreEqual(1, serving.MeasureCount);
                Assert.AreEqual(0, declining.MeasureCount);

                WProtoFormatterProvider.Register<Unserviceable>(declining);
                Assert.IsFalse(WProtoGeneric<Unserviceable>.CanEncode);
            }
            finally
            {
                WProtoFormatterProvider.Register(originalFormatter);
            }
        }

        /// <summary>Scalar registrations invalidate the same closed generic cache.</summary>
        [Test]
        public void ScalarRegisterAfterGenericResolutionReplacesTheCachedFormatter()
        {
            WProtoScalarFormatterProvider.TryGet(
                out IWProtoScalarFormatter<Unserviceable> originalFormatter
            );
            try
            {
                CountingUnserviceableScalarFormatter first =
                    new CountingUnserviceableScalarFormatter();
                CountingUnserviceableScalarFormatter second =
                    new CountingUnserviceableScalarFormatter();

                WProtoScalarFormatterProvider.Register<Unserviceable>(null);
                Assert.IsFalse(WProtoGeneric<Unserviceable>.CanEncode);

                WProtoScalarFormatterProvider.Register(first);
                Assert.IsTrue(WProtoGeneric<Unserviceable>.CanEncode);
                WProtoGeneric<Unserviceable>.MeasureField(1, new Unserviceable { Value = 7 });
                Assert.AreEqual(1, first.MeasureCount);

                WProtoScalarFormatterProvider.Register(second);
                WProtoGeneric<Unserviceable>.MeasureField(1, new Unserviceable { Value = 8 });
                Assert.AreEqual(1, first.MeasureCount);
                Assert.AreEqual(1, second.MeasureCount);
            }
            finally
            {
                WProtoScalarFormatterProvider.Register(originalFormatter);
            }
        }

        /// <summary>
        /// A generic SUBTYPE's own parameter is asked about, not just its base's.
        /// </summary>
        /// <remarks>
        /// The entry point registered for a subtype is its root formatter, which writes the whole
        /// chain by delegating to the chain root. Asking only the root -- the first version of this
        /// -- missed the subtype's own encoded parameters, so a subtype closed over an element
        /// nothing can encode still claimed the request and failed inside <c>Measure</c>. Every
        /// contract from the declared type up to the root is asked now.
        /// </remarks>
        [Test]
        public void AGenericSubtypeClosedOverAnElementItCannotEncodeIsNotServed()
        {
            Assert.IsTrue(
                WProtoFormatterProvider.IsRegistered<ConditionalSub<Unserviceable>>(),
                "the registration is what makes the decline necessary"
            );
            Assert.IsFalse(
                WProtoFacade.TrySerialize(
                    new ConditionalSub<Unserviceable> { Id = 1 },
                    out byte[] _
                ),
                "the entry point asked only the chain root, so the subtype's own parameter was missed"
            );
        }

        /// <summary>
        /// A closure it CAN encode is still served, so the decline is not a blanket refusal.
        /// </summary>
        [Test]
        public void AGenericContractClosedOverAnEncodableElementIsStillServed()
        {
            Assert.IsTrue(WProtoFacade.TrySerialize(new Box<int> { Value = 7 }, out byte[] bytes));
            Assert.IsTrue(WProtoFacade.TryDeserialize(bytes, out Box<int> restored));
            Assert.AreEqual(7, restored.Value);
        }

        /// <summary>
        /// A marshal lives in its own registry, and a member-position lookup cannot see it.
        /// </summary>
        /// <remarks>
        /// This is the design property stated as a test rather than as a comment. Registering these
        /// with the contract formatters would compile, pass every round-trip, and silently change the
        /// encoding of any member <see cref="WProtoGeneric{T}"/> resolves at one of these types.
        /// </remarks>
        [Test]
        public void AMarshalIsNotVisibleToTheContractFormatterProvider()
        {
            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<StandInRing<int>>());
            Assert.IsFalse(WProtoFormatterProvider.IsRegistered<StandInRing<int>>());

            Assert.IsTrue(WProtoRootMarshalProvider.IsRegistered<StandInBag>());
            Assert.IsFalse(WProtoFormatterProvider.IsRegistered<StandInBag>());
        }

        /// <summary>
        /// Held as a member, a marshalled collection is still an ordinary repeated field.
        /// </summary>
        [Test]
        public void AMarshalledCollectionHeldAsAMemberIsStillARepeatedField()
        {
            RingHolder holder = new RingHolder
            {
                Ring = new StandInRing<int> { Capacity = 9 },
                Trailer = 4,
            };
            holder.Ring.Add(1);
            holder.Ring.Add(2);

            // Applying the root marshal to members would make the oracle decode capacity as an element.
            Assert.AreEqual("0A0201021004", ContractHex(holder));

            using (MemoryStream stream = new MemoryStream(Parse(ContractHex(holder))))
            {
                RingHolder theirs = ProtoBuf.Serializer.Deserialize<RingHolder>(stream);
                CollectionAssert.AreEqual(new[] { 1, 2 }, theirs.Ring.ToArray());
                Assert.AreEqual(4, theirs.Trailer);
                Assert.AreEqual(0, theirs.Ring.Capacity, "the member encoding carries no capacity");
            }
        }

        [Test]
        public void AConsumerClosureOfAReferencedMarshalIsRegistered()
        {
            IReadOnlyList<string> registrations = Registrations(
                "public static class Use { public static object Make() { return new "
                    + "global::WallstopStudios.UnityHelpers.Proto.Generator.Tests.StandInRing<long>(); } }"
            );

            Assert.That(
                registrations,
                Has.Some.Contains("StandInRingMarshalFormatter<long>"),
                "a consumer's own closure of a marshalled collection was not registered: "
                    + string.Join(" | ", registrations)
            );
        }

        [Test]
        public void AMarshalIsRegisteredThroughTheRootRegistryAndNotTheFormatterProvider()
        {
            IReadOnlyList<string> registrations = Registrations(
                "public static class Use { public static object Make() { return new "
                    + "global::WallstopStudios.UnityHelpers.Proto.Generator.Tests.StandInRing<long>(); } }"
            );

            // Counting registrations prevents an empty discovery result from passing vacuously.
            List<string> mine = registrations
                .Where(registration =>
                    registration.Contains("StandInRingMarshalFormatter", StringComparison.Ordinal)
                )
                .ToList();

            Assert.AreEqual(1, mine.Count, string.Join(" | ", registrations));
            Assert.IsTrue(
                mine[0].Contains("WProtoRootMarshalProvider", StringComparison.Ordinal),
                mine[0]
            );
            Assert.IsFalse(
                mine[0].Contains("WProtoFormatterProvider", StringComparison.Ordinal),
                mine[0]
            );
        }

        /// <summary>
        /// A non-generic marshal is registered by the assembly that declares it and by nobody else.
        /// </summary>
        /// <remarks>
        /// The registry is a static field per closed type shared by the whole process, so a consumer
        /// re-registering the same formatter is a duplicate rather than a fix -- and every consumer
        /// of the package would emit one.
        /// </remarks>
        [Test]
        public void ANonGenericMarshalIsNotReRegisteredByAConsumer()
        {
            IReadOnlyList<string> registrations = Registrations(
                "public static class Use { public static object Make() { return new "
                    + "global::WallstopStudios.UnityHelpers.Proto.Generator.Tests.StandInBag(); } }"
            );

            Assert.That(
                registrations,
                Has.None.Contains("StandInBagMarshalFormatter"),
                string.Join(" | ", registrations)
            );
        }

        /// <summary>
        /// A closure over a type the registrar cannot name is skipped, not emitted.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The registrar is a type of its own, so a <c>private</c> nested type is out of reach from
        /// it however accessible it is elsewhere in the assembly. Naming one is <c>CS0122</c> in the
        /// build of the assembly that declared it -- their code stops compiling because of a type
        /// they made private and never asked to have serialized, which is a worse outcome than the
        /// registration this drops.
        /// </para>
        /// <para>
        /// Found by compiling this package's own PlayMode fixtures, two of which hold a
        /// <c>SerializableDictionary</c> of a private nested payload.
        /// </para>
        /// </remarks>
        [Test]
        public void AClosureOverAPrivateTypeIsNotRegistered()
        {
            const string source =
                "public static class Use { private sealed class Hidden { } "
                + "public static object Make() { return new "
                + "global::WallstopStudios.UnityHelpers.Proto.Generator.Tests.StandInRing<Hidden>(); } }";

            Assert.That(
                Registrations(source),
                Has.None.Contains("Hidden"),
                string.Join(" | ", Registrations(source))
            );
            Assert.IsEmpty(
                CompileGenerated(source)
                    .Where(diagnostic => diagnostic.Id == "CS0122")
                    .Select(diagnostic => diagnostic.GetMessage()),
                "the generated registrar names a type the consumer cannot name"
            );
        }

        /// <summary>
        /// A closure whose argument the formatter's constraints reject is skipped.
        /// </summary>
        /// <remarks>
        /// <c>Construct</c> does not enforce constraints, so a formatter declared with one its
        /// collection does not have would be closed over an argument it cannot accept and emitted --
        /// <c>CS0453</c> in the consumer's own build, which is what these diagnostics exist to
        /// prevent.
        /// </remarks>
        [Test]
        public void AClosureTheFormattersConstraintsRejectIsNotRegistered()
        {
            const string source =
                "[assembly: WProtoRootMarshal(typeof(Consumer.Ring<>), typeof(Consumer.RingFormatter<>))]\n"
                + "public sealed class Ring<T> { }\n"
                + "public sealed class RingFormatter<T> : IWProtoFormatter<Consumer.Ring<T>> where T : struct\n"
                + "{\n"
                + "    public int Measure(in Consumer.Ring<T> value) => 0;\n"
                + "    public bool Write(ref WProtoWriter writer, in Consumer.Ring<T> value) => true;\n"
                + "    public bool TryRead(ref WProtoReader reader, out Consumer.Ring<T> value) { value = null; return true; }\n"
                + "}\n"
                + "public static class Use { public static Ring<string> Reference; public static Ring<int> Value; }";

            IReadOnlyList<string> registrations = Registrations(source);

            Assert.That(
                registrations,
                Has.None.Contains("RingFormatter<string>"),
                string.Join(" | ", registrations)
            );
            Assert.That(
                registrations,
                Has.Some.Contains("RingFormatter<int>"),
                "the closure the constraint DOES accept still has to be registered: "
                    + string.Join(" | ", registrations)
            );
            Assert.IsEmpty(
                CompileGenerated(source)
                    .Where(diagnostic => diagnostic.Id == "CS0453")
                    .Select(diagnostic => diagnostic.GetMessage())
            );
        }

        [Test]
        public void AStillOpenClosureOfAMarshalIsNotRegistered()
        {
            IReadOnlyList<string> registrations = Registrations(
                "public static class Use<T> { public static "
                    + "global::WallstopStudios.UnityHelpers.Proto.Generator.Tests.StandInRing<T> Field; }"
            );

            Assert.That(
                registrations,
                Has.None.Contains("StandInRingMarshalFormatter"),
                string.Join(" | ", registrations)
            );
        }

        [Test]
        public void AMarshalWhoseFormatterHasADifferentArityIsAnError()
        {
            AssertDiagnostic(
                "WPROTO019",
                "Consumer.Ring<T>",
                @"[assembly: WProtoRootMarshal(typeof(Consumer.Ring<>), typeof(Consumer.RingFormatter))]
                  public sealed class Ring<T> { }
                  public sealed class RingFormatter { }"
            );
        }

        [Test]
        public void AMarshalWhoseFormatterDoesNotFormatItIsAnError()
        {
            AssertDiagnostic(
                "WPROTO020",
                "'Consumer.Ring'",
                @"[assembly: WProtoRootMarshal(typeof(Consumer.Ring), typeof(Consumer.RingFormatter))]
                  public sealed class Ring { }
                  public sealed class RingFormatter { }"
            );
        }

        [Test]
        public void AMarshalWhoseFormatterHasNoPublicConstructorIsAnError()
        {
            AssertDiagnostic(
                "WPROTO020",
                "'Consumer.Ring'",
                @"[assembly: WProtoRootMarshal(typeof(Consumer.Ring), typeof(Consumer.RingFormatter))]
                  public sealed class Ring { }
                  public sealed class RingFormatter : IWProtoFormatter<Consumer.Ring>
                  {
                      private RingFormatter() { }
                      public int Measure(in Consumer.Ring value) => 0;
                      public bool Write(ref WProtoWriter writer, in Consumer.Ring value) => true;
                      public bool TryRead(ref WProtoReader reader, out Consumer.Ring value) { value = null; return true; }
                  }"
            );
        }

        [Test]
        public void AMarshalOnAContractIsAnError()
        {
            AssertDiagnostic(
                "WPROTO021",
                "'Consumer.Ring'",
                @"[assembly: WProtoRootMarshal(typeof(Consumer.Ring), typeof(Consumer.RingFormatter))]
                  [WProtoContract] public sealed partial class Ring { [WProtoMember(1)] public int Value; }
                  public sealed class RingFormatter : IWProtoFormatter<Consumer.Ring>
                  {
                      public int Measure(in Consumer.Ring value) => 0;
                      public bool Write(ref WProtoWriter writer, in Consumer.Ring value) => true;
                      public bool TryRead(ref WProtoReader reader, out Consumer.Ring value) { value = null; return true; }
                  }"
            );
        }

        /// <summary>
        /// Two marshals for one type in one assembly is an error, not a first-wins coin toss.
        /// </summary>
        [Test]
        public void AMarshalDeclaredTwiceIsAnError()
        {
            AssertDiagnostic(
                "WPROTO022",
                "'Consumer.Ring'",
                @"[assembly: WProtoRootMarshal(typeof(Consumer.Ring), typeof(Consumer.RingFormatter))]
                  [assembly: WProtoRootMarshal(typeof(Consumer.Ring), typeof(Consumer.OtherFormatter))]
                  public sealed class Ring { }
                  public sealed class RingFormatter : IWProtoFormatter<Consumer.Ring>
                  {
                      public int Measure(in Consumer.Ring value) => 0;
                      public bool Write(ref WProtoWriter writer, in Consumer.Ring value) => true;
                      public bool TryRead(ref WProtoReader reader, out Consumer.Ring value) { value = null; return true; }
                  }
                  public sealed class OtherFormatter : IWProtoFormatter<Consumer.Ring>
                  {
                      public int Measure(in Consumer.Ring value) => 0;
                      public bool Write(ref WProtoWriter writer, in Consumer.Ring value) => true;
                      public bool TryRead(ref WProtoReader reader, out Consumer.Ring value) { value = null; return true; }
                  }"
            );
        }

        [Test]
        public void AWellFormedMarshalReportsNothing()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                @"[assembly: WProtoRootMarshal(typeof(Consumer.Ring<>), typeof(Consumer.RingFormatter<>))]
                  public sealed class Ring<T> { }
                  public sealed class RingFormatter<T> : IWProtoFormatter<Consumer.Ring<T>>
                  {
                      public int Measure(in Consumer.Ring<T> value) => 0;
                      public bool Write(ref WProtoWriter writer, in Consumer.Ring<T> value) => true;
                      public bool TryRead(ref WProtoReader reader, out Consumer.Ring<T> value) { value = null; return true; }
                  }"
            );

            Assert.IsEmpty(
                diagnostics.Where(diagnostic =>
                    diagnostic.Id.StartsWith("WPROTO0", StringComparison.Ordinal)
                ),
                string.Join("; ", diagnostics.Select(diagnostic => diagnostic.Id))
            );
        }

        /// <summary>
        /// A pair a REFERENCED assembly declares is never re-reported here.
        /// </summary>
        /// <remarks>
        /// Blaming a consumer for a declaration they cannot edit fails their build over someone
        /// else's mistake, which is why validation reads the compilation's own attributes only.
        /// </remarks>
        [Test]
        public void APairFromAReferencedAssemblyIsNotValidatedAgain()
        {
            ImmutableArray<Diagnostic> diagnostics = Run(
                "public static class Use { public static object Make() { return new "
                    + "global::WallstopStudios.UnityHelpers.Proto.Generator.Tests.StandInRing<long>(); } }"
            );

            Assert.IsEmpty(
                diagnostics.Where(diagnostic =>
                    diagnostic.Id == "WPROTO019"
                    || diagnostic.Id == "WPROTO020"
                    || diagnostic.Id == "WPROTO021"
                ),
                string.Join("; ", diagnostics.Select(diagnostic => diagnostic.Id))
            );
        }

        private static string MarshalHex<T>(T value)
        {
            Assert.IsTrue(
                WProtoRootMarshalProvider.TryGet(out IWProtoFormatter<T> formatter),
                "no root marshal is registered for " + typeof(T).FullName
            );
            return Encode(formatter, value);
        }

        private static string ContractHex<T>(T value)
        {
            return Encode(WProtoFormatterProvider.Get<T>(), value);
        }

        private static string Encode<T>(IWProtoFormatter<T> formatter, T value)
        {
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(buffer.Length, writer.Position, "Measure disagreed with Write");
            return ToHex(buffer);
        }

        private static byte[] OracleBytes<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                return stream.ToArray();
            }
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
                bytes[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
            }

            return bytes;
        }

        private static void AssertDiagnostic(string id, string mustName, string source)
        {
            ImmutableArray<Diagnostic> diagnostics = Run(source);
            Diagnostic match = diagnostics.FirstOrDefault(diagnostic => diagnostic.Id == id);

            Assert.IsTrue(
                match != null,
                "expected "
                    + id
                    + ", saw: "
                    + string.Join(
                        "; ",
                        diagnostics.Select(diagnostic =>
                            diagnostic.Id + " " + diagnostic.GetMessage()
                        )
                    )
            );
            Assert.AreEqual(DiagnosticSeverity.Error, match.Severity);
            Assert.IsTrue(
                match.GetMessage().Contains(mustName, StringComparison.Ordinal),
                "the message must name '" + mustName + "': " + match.GetMessage()
            );
        }

        private static IReadOnlyList<string> Registrations(string body)
        {
            Compile(body, out Compilation updated);

            SyntaxTree registrar = updated.SyntaxTrees.FirstOrDefault(tree =>
                tree.FilePath.Contains("WProtoGeneratedRegistrar", StringComparison.Ordinal)
            );

            if (registrar == null)
            {
                return Array.Empty<string>();
            }

            return registrar
                .GetText()
                .ToString()
                .Split('\n')
                .Where(line => line.Contains(".Register(", StringComparison.Ordinal))
                .Select(line => line.Trim())
                .ToList();
        }

        private static ImmutableArray<Diagnostic> Run(string body)
        {
            return Compile(body, out Compilation _);
        }

        /// <summary>
        /// Compiles the generator's own output and returns its errors.
        /// </summary>
        /// <remarks>
        /// The generator reporting nothing is not the same as the consumer's build succeeding.
        /// Emitted code that does not compile is the failure a developer actually hits.
        /// </remarks>
        private static ImmutableArray<Diagnostic> CompileGenerated(string body)
        {
            Compile(body, out Compilation generated);
            return generated
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToImmutableArray();
        }

        private static ImmutableArray<Diagnostic> Compile(string body, out Compilation updated)
        {
            List<string> assemblyAttributes = new List<string>();
            List<string> rest = new List<string>();
            foreach (string line in body.Split('\n'))
            {
                (
                    line.TrimStart().StartsWith("[assembly:", StringComparison.Ordinal)
                        ? assemblyAttributes
                        : rest
                ).Add(line);
            }

            string source =
                "using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;\n"
                + string.Join("\n", assemblyAttributes)
                + "\nnamespace Consumer { using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto; "
                + string.Join("\n", rest)
                + " }";

            List<MetadataReference> references = new List<MetadataReference>();
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                {
                    references.Add(MetadataReference.CreateFromFile(assembly.Location));
                }
            }

            CSharpCompilation compilation = CSharpCompilation.Create(
                "ConsumerAssembly",
                new[] { CSharpSyntaxTree.ParseText(source) },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            CSharpGeneratorDriver
                .Create(new WProtoGenerator())
                .RunGeneratorsAndUpdateCompilation(
                    compilation,
                    out updated,
                    out ImmutableArray<Diagnostic> diagnostics
                );

            return diagnostics;
        }

        private sealed class ConditionalUnserviceableFormatter
            : IWProtoFormatter<Unserviceable>,
                IWProtoConditionalFormatter
        {
            private readonly bool _canServe;

            internal int MeasureCount { get; private set; }

            internal ConditionalUnserviceableFormatter(bool canServe)
            {
                _canServe = canServe;
            }

            public bool CanServe()
            {
                return _canServe;
            }

            public int Measure(in Unserviceable value)
            {
                MeasureCount++;
                return 0;
            }

            public bool Write(ref WProtoWriter writer, in Unserviceable value)
            {
                return true;
            }

            public bool TryRead(ref WProtoReader reader, out Unserviceable value)
            {
                value = default;
                return true;
            }
        }

        private sealed class CountingUnserviceableScalarFormatter
            : IWProtoScalarFormatter<Unserviceable>
        {
            public int WireType => WProtoWireType.Varint;

            internal int MeasureCount { get; private set; }

            public bool IsDefault(in Unserviceable value)
            {
                return false;
            }

            public int MeasureValue(in Unserviceable value)
            {
                MeasureCount++;
                return WProtoSizes.Int32Size(value.Value);
            }

            public bool WriteValue(ref WProtoWriter writer, in Unserviceable value)
            {
                return writer.TryWriteInt32(value.Value);
            }

            public bool TryReadValue(ref WProtoReader reader, out Unserviceable value)
            {
                bool read = reader.TryReadInt32(out int raw);
                value = new Unserviceable { Value = raw };
                return read;
            }
        }
    }
}
