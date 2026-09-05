// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Proto.Generator.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// Pins <c>[WProtoSubtype]</c> as a second spelling of <c>[WProtoInclude]</c>, not a second
    /// encoding.
    /// </summary>
    /// <remarks>
    /// The property the whole feature rests on is byte identity: a hierarchy whose subtypes declare
    /// themselves has to produce exactly the bytes the same hierarchy produces when its base
    /// declares them, and both have to match protobuf-net. Anything less would mean saved data
    /// depends on which end of an inheritance edge the annotation was written on.
    /// </remarks>
    [TestFixture]
    public sealed class SubtypeDeclarationTests
    {
        [TestCaseSource(nameof(EquivalentHierarchyValues))]
        public void TheTwoDeclarationFormsProduceIdenticalBytes(
            string label,
            BaseFormRoot declaredByBase,
            SubtypeFormRoot declaredBySubtype
        )
        {
            string fromBase = Encode<BaseFormRoot>(declaredByBase);

            Assert.AreEqual(fromBase, Encode<SubtypeFormRoot>(declaredBySubtype), label);

            Assert.AreEqual(fromBase, OracleHex(declaredByBase), label + " against the oracle");
            Assert.AreEqual(fromBase, OracleHex(declaredBySubtype), label + " against the oracle");
        }

        [TestCaseSource(nameof(EquivalentHierarchyValues))]
        public void TheTwoDeclarationFormsAreIdenticalUnderALengthPrefixToo(
            string label,
            BaseFormRoot declaredByBase,
            SubtypeFormRoot declaredBySubtype
        )
        {
            string fromBase = Encode(new BaseFormHolder { Value = declaredByBase, Trailer = 2 });

            Assert.AreEqual(
                fromBase,
                Encode(new SubtypeFormHolder { Value = declaredBySubtype, Trailer = 2 }),
                label
            );
            Assert.AreEqual(
                fromBase,
                OracleHex(new BaseFormHolder { Value = declaredByBase, Trailer = 2 }),
                label + " against the oracle"
            );
        }

        [TestCaseSource(nameof(EquivalentHierarchyValues))]
        public void ASelfDeclaredHierarchyRoundTripsAsItsConcreteType(
            string label,
            BaseFormRoot declaredByBase,
            SubtypeFormRoot declaredBySubtype
        )
        {
            Assert.AreEqual(
                declaredByBase.GetType().Name.Substring("BaseForm".Length),
                RoundTrip<SubtypeFormRoot>(declaredBySubtype)
                    .GetType()
                    .Name.Substring("SubtypeForm".Length),
                label
            );
        }

        [Test]
        public void ASelfDeclaredIncludeIsWrittenBeforeTheBaseMembersWhateverItsTagNumber()
        {
            // A small include tag distinguishes include-first ordering from ordinary tag sorting.
            SubtypeLowTagSub value = new SubtypeLowTagSub
            {
                First = 1,
                Fifth = 5,
                SubOnly = 9,
            };

            Assert.AreEqual(
                "1A0208090801 2805".Replace(" ", string.Empty),
                Encode<SubtypeLowTagBase>(value)
            );
            Assert.AreEqual(OracleHex(value), Encode<SubtypeLowTagBase>(value));
        }

        [Test]
        public void EveryLevelOfASelfDeclaredChainWritesItsOwnIncludeThenItsOwnMembers()
        {
            SubtypeFormGamma deep = new SubtypeFormGamma
            {
                Id = 1,
                Label = "a",
                BetaOnly = 1.5,
                GammaOnly = true,
            };

            BaseFormGamma twin = new BaseFormGamma
            {
                Id = 1,
                Label = "a",
                BetaOnly = 1.5,
                GammaOnly = true,
            };

            Assert.AreEqual(
                "AA060EC20C02080109000000000000F83F08011201 61".Replace(" ", string.Empty),
                Encode<SubtypeFormRoot>(deep)
            );
            Assert.AreEqual(Encode<BaseFormRoot>(twin), Encode<SubtypeFormRoot>(deep));
            Assert.AreEqual(OracleHex(deep), Encode<SubtypeFormRoot>(deep));
        }

        [Test]
        public void ADeeperSelfDeclaredSubtypeIsNotWrittenUnderItsBasesTag()
        {
            // Assignability matches ancestors too; dispatch must retain the most derived subtype.
            SubtypeFormGamma gamma = new SubtypeFormGamma { GammaOnly = true };

            Assert.IsInstanceOf<SubtypeFormGamma>(RoundTrip<SubtypeFormRoot>(gamma));
            Assert.IsTrue(((SubtypeFormGamma)RoundTrip<SubtypeFormRoot>(gamma)).GammaOnly);
        }

        [Test]
        public void OneBaseCarriesBothDeclarationFormsAtOnce()
        {
            MixedFormAlpha alpha = new MixedFormAlpha
            {
                Id = 1,
                Label = "a",
                AlphaOnly = 7,
                AlphaText = "x",
            };
            MixedFormBeta beta = new MixedFormBeta
            {
                Id = 2,
                Label = "b",
                BetaOnly = 1.5,
            };

            Assert.AreEqual(OracleHex(alpha), Encode<MixedFormRoot>(alpha));
            Assert.AreEqual(OracleHex(beta), Encode<MixedFormRoot>(beta));
            Assert.IsInstanceOf<MixedFormAlpha>(RoundTrip<MixedFormRoot>(alpha));
            Assert.IsInstanceOf<MixedFormBeta>(RoundTrip<MixedFormRoot>(beta));

            IWProtoPolymorphicFormatter root = MixedFormRoot.WProtoFormatter.Instance;
            Assert.IsTrue(root.CanWrite(typeof(MixedFormAlpha)));
            Assert.IsTrue(root.CanWrite(typeof(MixedFormBeta)));
        }

        [Test]
        public void CanWriteCoversASelfDeclaredChainAndStopsAtItsEdges()
        {
            IWProtoPolymorphicFormatter root = SubtypeFormRoot.WProtoFormatter.Instance;

            Assert.IsTrue(root.CanWrite(typeof(SubtypeFormRoot)));
            Assert.IsTrue(root.CanWrite(typeof(SubtypeFormAlpha)));
            Assert.IsTrue(root.CanWrite(typeof(SubtypeFormBeta)));
            Assert.IsTrue(root.CanWrite(typeof(SubtypeFormGamma)));
            Assert.IsFalse(
                root.CanWrite(typeof(UndeclaredSubtypeFormAlpha)),
                "a subtype nothing declares has no field number to be written under"
            );
            Assert.IsFalse(root.CanWrite(typeof(BaseFormAlpha)), "an unrelated chain");

            // Root delegation includes sibling subtypes, so a subtype entry point must narrow its claim.
            IWProtoPolymorphicFormatter alpha = SubtypeFormAlpha.WProtoRootFormatter.Instance;

            Assert.IsTrue(alpha.CanWrite(typeof(SubtypeFormAlpha)));
            Assert.IsFalse(alpha.CanWrite(typeof(SubtypeFormGamma)), "a sibling");
        }

        [Test]
        public void AnUndeclaredSubtypeIsStillRefusedUnderTheNewForm()
        {
            IWProtoFormatter<SubtypeFormRoot> formatter =
                WProtoFormatterProvider.Get<SubtypeFormRoot>();

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                formatter.Measure(new UndeclaredSubtypeFormAlpha { AlphaOnly = 7 })
            );

            StringAssert.Contains(nameof(UndeclaredSubtypeFormAlpha), refused.Message);
            StringAssert.Contains("WProtoSubtype", refused.Message);
        }

        [Test]
        public void AnAbstractBaseIsSatisfiedByASubtypeThatDeclaresItself()
        {
            // Successful compilation verifies merged subtypes reach the abstract-contract validation.
            SubtypeAbstractConcrete value = new SubtypeAbstractConcrete { Sides = 3, Edge = 5 };
            SubtypeAbstractBase decoded = RoundTrip<SubtypeAbstractBase>(value);

            Assert.IsInstanceOf<SubtypeAbstractConcrete>(decoded);
            Assert.AreEqual(3, decoded.Sides);
            Assert.AreEqual(5, ((SubtypeAbstractConcrete)decoded).Edge);

            WProtoReader reader = new WProtoReader(new byte[] { 0x08, 0x03 });
            Assert.IsFalse(
                WProtoFormatterProvider
                    .Get<SubtypeAbstractBase>()
                    .TryRead(ref reader, out SubtypeAbstractBase none)
            );
            Assert.IsNull(none);
        }

        [TestCaseSource(nameof(EquivalentHierarchyValues))]
        public void MeasurePredictsWriteExactlyForASelfDeclaredHierarchy(
            string label,
            BaseFormRoot declaredByBase,
            SubtypeFormRoot declaredBySubtype
        )
        {
            IWProtoFormatter<SubtypeFormRoot> formatter =
                WProtoFormatterProvider.Get<SubtypeFormRoot>();
            int predicted = formatter.Measure(declaredBySubtype);
            byte[] buffer = new byte[predicted];
            WProtoWriter writer = new WProtoWriter(buffer);

            Assert.IsTrue(formatter.Write(ref writer, declaredBySubtype), label);
            Assert.AreEqual(predicted, writer.Position, label);

            Assert.AreEqual(
                WProtoFormatterProvider.Get<BaseFormRoot>().Measure(declaredByBase),
                predicted,
                label
            );
        }

        /// <summary>
        /// Every shape of the twin hierarchies, paired value for value.
        /// </summary>
        /// <returns>A label and the two equivalent values.</returns>
        /// <remarks>
        /// One source feeds four assertions -- bytes, bytes under a prefix, the round-tripped type,
        /// and the measured length -- because they are the same cases asked four questions, and a
        /// shape added here is covered by all of them at once.
        /// </remarks>
        private static IEnumerable<TestCaseData> EquivalentHierarchyValues()
        {
            yield return Pair("the root itself", new BaseFormRoot(), new SubtypeFormRoot());
            yield return Pair(
                "the root with members",
                new BaseFormRoot { Id = 1, Label = "a" },
                new SubtypeFormRoot { Id = 1, Label = "a" }
            );
            yield return Pair(
                "an all-default leaf subtype",
                new BaseFormAlpha(),
                new SubtypeFormAlpha()
            );
            yield return Pair(
                "a leaf subtype with members",
                new BaseFormAlpha
                {
                    Id = -1,
                    Label = string.Empty,
                    AlphaOnly = int.MinValue,
                    AlphaText = "é中",
                },
                new SubtypeFormAlpha
                {
                    Id = -1,
                    Label = string.Empty,
                    AlphaOnly = int.MinValue,
                    AlphaText = "é中",
                }
            );
            yield return Pair(
                "a middle subtype",
                new BaseFormBeta { Id = 2, BetaOnly = -0.5 },
                new SubtypeFormBeta { Id = 2, BetaOnly = -0.5 }
            );
            yield return Pair(
                "an all-default deep subtype",
                DeepBase(0, null, 0, false),
                DeepSubtype(0, null, 0, false)
            );
            yield return Pair(
                "a deep subtype with members",
                DeepBase(3, "g", double.MaxValue, true),
                DeepSubtype(3, "g", double.MaxValue, true)
            );
        }

        private static TestCaseData Pair(
            string label,
            BaseFormRoot declaredByBase,
            SubtypeFormRoot declaredBySubtype
        )
        {
            return new TestCaseData(label, declaredByBase, declaredBySubtype).SetName(
                "{m} - " + label
            );
        }

        private static BaseFormGamma DeepBase(int id, string label, double middle, bool deepest)
        {
            return new BaseFormGamma
            {
                Id = id,
                Label = label,
                BetaOnly = middle,
                GammaOnly = deepest,
            };
        }

        private static SubtypeFormGamma DeepSubtype(
            int id,
            string label,
            double middle,
            bool deepest
        )
        {
            return new SubtypeFormGamma
            {
                Id = id,
                Label = label,
                BetaOnly = middle,
                GammaOnly = deepest,
            };
        }

        [Test]
        public void ASubtypeClaimedUnderTwoFieldNumbersWritesTheLowerOne()
        {
            TwiceClaimingBase value = new TwiceClaimedSubtype { Id = 7, Extra = 9 };

            string mine = Encode(value);

            // Only the lower duplicate tag is writable; deleting it would change future wire identity.
            StringAssert.StartsWith("2A", mine);
            Assert.AreEqual(OracleHex(value), mine, "the wire has to agree with protobuf-net");
            Assert.IsInstanceOf<TwiceClaimedSubtype>(RoundTrip(value));
        }

        private static string OracleHex<T>(T value)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                ProtoBuf.Serializer.Serialize(stream, value);
                return ToHex(stream.ToArray());
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
    }
}
