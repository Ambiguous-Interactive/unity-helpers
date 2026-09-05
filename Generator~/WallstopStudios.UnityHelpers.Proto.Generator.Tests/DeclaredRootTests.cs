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
            // Leaked process-global claims leave later tests' declared types unserved.
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
            /*
             * Use a separate pair so this registration cannot satisfy the generated-registrar test. Registrar
             * ordering must not change consumer overrides.
             */
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
            /*
             * An interface reaches the self-reference guard; a concrete type is refused earlier. Query
             * CanServe directly to avoid recursive facade resolution.
             */
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
            /*
             * A real root formatter ensures this reaches the declared-type guard rather than the no-root
             * branch.
             */
            WProtoFormatterProvider.Register<ConcreteDerived>(new ConcreteDerivedFormatter());
            WProtoDeclaredRootProvider.Register<ConcreteDeclared, ConcreteDerived>();
            try
            {
                Assert.IsFalse(
                    WProtoFacade.TrySerialize(new ConcreteDeclared(), out byte[] bytes),
                    "a value that IS the declared type narrows to nothing and would write zero bytes"
                );
                Assert.IsNull(bytes);

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
