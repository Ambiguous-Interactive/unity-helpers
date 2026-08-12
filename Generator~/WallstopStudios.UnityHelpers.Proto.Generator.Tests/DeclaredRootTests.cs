// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.IO;
    using NUnit.Framework;
    using UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// A value held as an interface, which is how a generator is held in practice and the half of
    /// #403 the write-side fix could not reach.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An interface has no members, so nothing about <c>IRandom</c> says which contract should read
    /// a payload written for it. <c>[assembly: WProtoDeclaredRoot]</c> is that missing sentence, and
    /// the cases here are mostly about the times it must NOT be believed -- a consumer's own
    /// implementation, and a consumer who named a different root -- because the read side has no
    /// second chance: a refusal after the formatter is chosen is an exception, not a fallback.
    /// </para>
    /// <para>
    /// The pair under test is declared in <c>AssemblyInfo.cs</c> and registered by the generated
    /// registrar, so these run against the same path a consumer's build produces rather than a
    /// hand-made registration.
    /// </para>
    /// </remarks>
    [TestFixture]
    public sealed class DeclaredRootTests
    {
        [TearDown]
        public void ReleaseClaims()
        {
            // Claims are process-global and a leaked one silently leaves the declared type unserved
            // for every test that runs after it.
            WProtoDeclaredRootProvider.ReleaseAllClaims();
        }

        [Test]
        public void TheGeneratedRegistrarRegistersTheDeclaredPair()
        {
            Assert.IsTrue(
                WProtoDeclaredRootProvider.TryGetFormatter(out IWProtoFormatter<IIncludeThing> _),
                "the attribute is the whole registration; nothing else names this interface"
            );
            Assert.IsFalse(
                WProtoFormatterProvider.IsRegistered<IIncludeThing>(),
                "a declared root is root-only: WProtoGeneric reads that provider for every member "
                    + "a closure decides, and asks it no CanServe or CanWrite question"
            );
            Assert.IsFalse(
                WProtoGeneric<IIncludeThing>.CanEncode,
                "so a Box<IIncludeThing> or Deque<IIncludeThing> must still decline, as it did "
                    + "before this pair existed"
            );
            Assert.IsTrue(
                WProtoDeclaredRootProvider.TryGetRoot(typeof(IIncludeThing), out Type root)
            );
            Assert.AreEqual(typeof(IncludeBase), root);
        }

        [Test]
        public void AValueHeldAsTheInterfaceIsServedAndMatchesTheOracle()
        {
            // protobuf-net writes from the outermost contract in the chain, so its answer for a
            // value reached through IRandom is AbstractRandom's bytes -- which is exactly what the
            // adapter delegates to. Three depths, because "the direct subtype happens to work" is
            // not the claim.
            AssertServedAndIdentical(
                new IncludeAlpha
                {
                    Id = 1,
                    AlphaOnly = 7,
                    AlphaText = "x",
                }
            );
            AssertServedAndIdentical(new IncludeBeta { Id = 2, BetaOnly = 1.5 });
            AssertServedAndIdentical(
                new IncludeGamma
                {
                    Id = 3,
                    Label = "z",
                    BetaOnly = 2.5,
                    GammaOnly = true,
                }
            );
        }

        [Test]
        public void AValueHeldAsTheInterfaceComesBackAsItsSubtype()
        {
            IIncludeThing original = new IncludeGamma
            {
                Id = 4,
                Label = "z",
                BetaOnly = 1.5,
                GammaOnly = true,
            };

            Assert.IsTrue(WProtoFacade.TrySerialize(original, out byte[] bytes));
            Assert.IsTrue(WProtoFacade.TryDeserialize(bytes, out IIncludeThing restored));

            IncludeGamma gamma = restored as IncludeGamma;
            Assert.IsNotNull(gamma, "the subtype must survive a round trip through the interface");
            Assert.AreEqual(4, gamma.Id);
            Assert.AreEqual("z", gamma.Label);
            Assert.AreEqual(1.5, gamma.BetaOnly);
            Assert.IsTrue(gamma.GammaOnly);
        }

        [Test]
        public void TheDeclaredRootReadsWhatProtobufNetWroteThroughIt()
        {
            // The direction that matters for existing save data: every payload a consumer holds was
            // written by protobuf-net through the root its own resolution picked.
            IncludeGamma original = new IncludeGamma { Id = 9, BetaOnly = 4.5 };

            using MemoryStream stream = new();
            ProtoBuf.Serializer.Serialize<IncludeBase>(stream, original);

            Assert.IsTrue(
                WProtoFacade.TryDeserialize(stream.ToArray(), out IIncludeThing restored)
            );
            IncludeGamma gamma = restored as IncludeGamma;
            Assert.IsNotNull(gamma);
            Assert.AreEqual(9, gamma.Id);
            Assert.AreEqual(4.5, gamma.BetaOnly);
        }

        [Test]
        public void AnImplementationOutsideTheRootsChainIsNotServed()
        {
            // A consumer's own type implementing an interface this package declares a root for. It
            // has its own formatter and its own bytes; decoding one through IncludeBase's chain
            // would hand back the wrong type from a payload something else wrote.
            IIncludeThing foreign = new ForeignThing { Value = 3 };

            Assert.IsFalse(WProtoFacade.TrySerialize(foreign, out byte[] bytes));
            Assert.IsNull(bytes);

            byte[] buffer = new byte[8];
            byte[] original = buffer;
            WProtoWriteResult result = WProtoFacade.Serialize(foreign, ref buffer);

            Assert.IsFalse(result.Served);
            Assert.AreSame(original, buffer, "an unserved value must leave the buffer alone");
        }

        [Test]
        public void ReadingIntoATypeOutsideTheRootsChainIsNotServed()
        {
            Assert.IsTrue(
                WProtoFacade.TrySerialize<IIncludeThing>(
                    new IncludeAlpha { Id = 1, AlphaOnly = 7 },
                    out byte[] bytes
                )
            );

            Assert.IsTrue(
                WProtoFacade.TryDeserializeAs(bytes, typeof(IncludeGamma), out IIncludeThing _),
                "a subtype the chain declares is this root's to produce"
            );
            Assert.IsFalse(
                WProtoFacade.TryDeserializeAs(bytes, typeof(ForeignThing), out IIncludeThing _),
                "a sibling implementation is not"
            );
        }

        [Test]
        public void AClaimForADifferentRootStopsTheAdapterServing()
        {
            // Serializer.RegisterProtobufRoot<IIncludeThing, ForeignThing>() says a consumer's own
            // implementation owns this interface. protobuf-net then serves it, and WallstopProto
            // must get out of the way on BOTH sides -- the read above all, because a formatter that
            // answers and then rejects the payload throws out of a call that works today.
            WProtoDeclaredRootProvider.Claim(typeof(IIncludeThing), typeof(ForeignThing));

            Assert.IsFalse(
                WProtoFacade.TrySerialize<IIncludeThing>(new IncludeAlpha { Id = 1 }, out byte[] _)
            );

            using MemoryStream stream = new();
            ProtoBuf.Serializer.Serialize(stream, new ForeignThing { Value = 3 });

            Assert.IsFalse(
                WProtoFacade.TryDeserialize(stream.ToArray(), out IIncludeThing _),
                "a claimed declared type must fall back rather than decode through the wrong chain"
            );
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReleasingAClaimRestoresTheDeclaration(bool releaseEverything)
        {
            WProtoDeclaredRootProvider.Claim(typeof(IIncludeThing), typeof(ForeignThing));
            if (releaseEverything)
            {
                WProtoDeclaredRootProvider.ReleaseAllClaims();
            }
            else
            {
                WProtoDeclaredRootProvider.ReleaseClaim(typeof(IIncludeThing));
            }

            Assert.IsTrue(
                WProtoDeclaredRootProvider.IsRootFor(typeof(IIncludeThing), typeof(IncludeBase)),
                "the declaration is registered once, as the assembly loads; discarding it would "
                    + "leave the declared type unserved for the rest of the process"
            );
            Assert.IsTrue(
                WProtoFacade.TrySerialize<IIncludeThing>(new IncludeAlpha { Id = 1 }, out byte[] _)
            );
        }

        [Test]
        public void AClaimNamingTheDeclaredRootItselfChangesNothing()
        {
            WProtoDeclaredRootProvider.Claim(typeof(IIncludeThing), typeof(IncludeBase));

            Assert.IsTrue(
                WProtoFacade.TrySerialize<IIncludeThing>(new IncludeAlpha { Id = 1 }, out byte[] _)
            );
        }

        [Test]
        public void AClaimBeatsADeclarationWhicheverRunsFirst()
        {
            // Generated registrars all run in the same unordered Unity phase, so a rule of "last
            // registration wins" would make a consumer's override depend on assembly load order --
            // the one thing they cannot control.
            //
            // Deliberately NOT the shipped pair. Registering IIncludeThing here would satisfy
            // TheGeneratedRegistrarRegistersTheDeclaredPair from this test rather than from the
            // registrar, whichever ran first -- which is exactly what it did, and deleting the
            // generator's emission left the whole fixture green.
            WProtoDeclaredRootProvider.Claim(typeof(IUnformatted), typeof(ForeignThing));
            WProtoDeclaredRootProvider.Register<IUnformatted, UnformattedRoot>();
            try
            {
                Assert.IsTrue(
                    WProtoDeclaredRootProvider.IsRootFor(typeof(IUnformatted), typeof(ForeignThing))
                );
            }
            finally
            {
                WProtoDeclaredRootProvider.Unregister<IUnformatted>();
            }
        }

        [Test]
        public void ARootWithNoFormatterIsDeclinedRatherThanCalled()
        {
            // A pair can name a contract whose assembly was compiled without the generator. There is
            // nothing to delegate to, and the honest answer is "not mine" rather than a throw from
            // inside Measure.
            WProtoDeclaredRootProvider.Register<IUnformatted, UnformattedRoot>();
            try
            {
                Assert.IsFalse(
                    WProtoFacade.TrySerialize<IUnformatted>(new UnformattedRoot(), out byte[] _)
                );
                Assert.IsFalse(
                    WProtoFacade.TryDeserialize(new byte[] { 0x08, 0x01 }, out IUnformatted _)
                );
            }
            finally
            {
                WProtoDeclaredRootProvider.Unregister<IUnformatted>();
            }
        }

        [Test]
        public void ARootThatIsItsOwnDeclaredTypeDoesNotRecurse()
        {
            // The generator reports this as WPROTO024, but the provider is public API and a
            // hand-written call can still make it. The adapter would resolve itself and never stop.
            //
            // The declared type must be an INTERFACE here. A concrete one is refused earlier, by
            // the guard against a declared type a value can be, so the version of this test that
            // used one stayed green with the self-reference guard deleted. Going through the facade
            // would stack-overflow rather than fail, so CanServe is asked directly.
            WProtoDeclaredRootProvider.Register<IUnformatted, IUnformatted>();
            try
            {
                Assert.IsTrue(
                    WProtoDeclaredRootProvider.TryGetFormatter(
                        out IWProtoFormatter<IUnformatted> formatter
                    )
                );
                Assert.IsFalse(
                    ((IWProtoConditionalFormatter)formatter).CanServe(),
                    "an adapter that finds itself must decline instead of recursing"
                );
            }
            finally
            {
                WProtoDeclaredRootProvider.Unregister<IUnformatted>();
            }
        }

        [Test]
        public void TheAdapterAnswersOnlyForTypesItsRootChainWrites()
        {
            Assert.IsTrue(
                WProtoDeclaredRootProvider.TryGetFormatter(
                    out IWProtoFormatter<IIncludeThing> registered
                )
            );
            IWProtoPolymorphicFormatter adapter = registered as IWProtoPolymorphicFormatter;

            Assert.IsNotNull(adapter, "the facade asks this question of every non-exact match");
            Assert.IsTrue(adapter.CanWrite(typeof(IncludeBase)));
            Assert.IsTrue(adapter.CanWrite(typeof(IncludeGamma)));
            Assert.IsFalse(adapter.CanWrite(typeof(ForeignThing)));
            Assert.IsFalse(
                adapter.CanWrite(typeof(UndeclaredAlpha)),
                "a subtype no include names is refused through the interface too"
            );
        }

        private static void AssertServedAndIdentical(IncludeBase value)
        {
            Assert.IsTrue(WProtoFacade.TrySerialize<IIncludeThing>(value, out byte[] mine));

            using MemoryStream stream = new();
            ProtoBuf.Serializer.Serialize<IncludeBase>(stream, value);

            CollectionAssert.AreEqual(stream.ToArray(), mine, value.GetType().Name);
        }

        /// <summary>
        /// A declared type a value can actually BE is refused rather than encoded as nothing.
        /// </summary>
        /// <remarks>
        /// The generator reports this pair (<c>WPROTO029</c>), but the provider is public API. The
        /// facade short-circuits an exact runtime-type match before asking <c>CanWrite</c>, so
        /// without the refusal a populated instance is served, fails to narrow to the root, and
        /// writes zero bytes -- then reads back as the root, a different type.
        /// </remarks>
        [Test]
        public void ADeclaredTypeAValueCanBeIsNotServed()
        {
            // The root needs a real formatter, or "not served" would be the no-root branch instead
            // and this would pass with the guard deleted -- it did, on the first attempt.
            WProtoFormatterProvider.Register<ConcreteDerived>(new ConcreteDerivedFormatter());
            WProtoDeclaredRootProvider.Register<ConcreteDeclared, ConcreteDerived>();
            try
            {
                Assert.IsFalse(
                    WProtoFacade.TrySerialize(new ConcreteDeclared(), out byte[] bytes),
                    "a value that IS the declared type narrows to nothing and would write zero bytes"
                );
                Assert.IsNull(bytes);

                // The derived value still is served, so the refusal is about the declared type
                // rather than the pair being broken.
                Assert.IsTrue(
                    WProtoFacade.TrySerialize<ConcreteDerived>(
                        new ConcreteDerived(),
                        out byte[] derived
                    )
                );
                Assert.AreEqual(1, derived.Length);
            }
            finally
            {
                WProtoDeclaredRootProvider.Unregister<ConcreteDeclared>();
                WProtoFormatterProvider.Register<ConcreteDerived>(null);
            }
        }

        /// <summary>
        /// A root formatter that is not polymorphic answers for its own type and nothing else.
        /// </summary>
        /// <remarks>
        /// Every generated formatter implements <see cref="IWProtoPolymorphicFormatter"/>, so this
        /// branch is reachable only through a hand-written one -- which is a supported shape, and
        /// was the only part of <c>CanWrite</c> nothing exercised.
        /// </remarks>
        [Test]
        public void AHandWrittenRootAnswersForItsOwnTypeOnly()
        {
            WProtoFormatterProvider.Register<UnformattedRoot>(new PlainRootFormatter());
            WProtoDeclaredRootProvider.Register<IUnformatted, UnformattedRoot>();
            try
            {
                Assert.IsTrue(
                    WProtoDeclaredRootProvider.TryGetFormatter(
                        out IWProtoFormatter<IUnformatted> registered
                    )
                );
                IWProtoPolymorphicFormatter adapter = registered as IWProtoPolymorphicFormatter;

                Assert.IsNotNull(adapter);
                Assert.IsTrue(adapter.CanWrite(typeof(UnformattedRoot)));
                Assert.IsFalse(adapter.CanWrite(typeof(OtherUnformatted)));
            }
            finally
            {
                WProtoDeclaredRootProvider.Unregister<IUnformatted>();
                WProtoFormatterProvider.Register<UnformattedRoot>(null);
            }
        }

        private interface IUnformatted { }

        private sealed class UnformattedRoot : IUnformatted { }

        private sealed class OtherUnformatted : IUnformatted { }

        private class ConcreteDeclared { }

        private sealed class ConcreteDerived : ConcreteDeclared { }

        private sealed class ConcreteDerivedFormatter : IWProtoFormatter<ConcreteDerived>
        {
            public int Measure(in ConcreteDerived value) => 1;

            public bool Write(ref WProtoWriter writer, in ConcreteDerived value)
            {
                return writer.TryWriteVarint32(8);
            }

            public bool TryRead(ref WProtoReader reader, out ConcreteDerived value)
            {
                value = new ConcreteDerived();
                return true;
            }
        }

        private sealed class PlainRootFormatter : IWProtoFormatter<UnformattedRoot>
        {
            public int Measure(in UnformattedRoot value) => 0;

            public bool Write(ref WProtoWriter writer, in UnformattedRoot value) => true;

            public bool TryRead(ref WProtoReader reader, out UnformattedRoot value)
            {
                value = new UnformattedRoot();
                return true;
            }
        }
    }
}
