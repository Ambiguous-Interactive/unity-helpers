// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Reflection;
    using NUnit.Framework;
    using WallstopStudios.UnityHelpers.Core.Serialization.WallstopProto;

    /// <summary>
    /// The zero-touch subtype form, compiled by Unity rather than by the harness under
    /// <c>Generator~</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The differential suite already proves that a manifest-numbered subtype and a hand-numbered
    /// one produce the same bytes, against protobuf-net 2.4.9 and 3.2.56. What that suite cannot
    /// prove is that the arrangement survives the compiler Unity actually uses, with the analyzer
    /// DLL this package ships and an assembly attribute in a committed file. That is what this
    /// fixture is for, and it is the same reason the include golden vectors live here as well.
    /// </para>
    /// <para>
    /// Nothing here hard-codes a field number. Each assertion reads the number out of the manifest
    /// attribute and then requires the wire to agree with it, so the fixture cannot pass by having
    /// the same wrong number written in two places.
    /// </para>
    /// </remarks>
    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    [NUnit.Framework.Category("Serialization")]
    public sealed class WProtoSubtypeTagManifestTests
    {
        [Test]
        public void EveryNumberlessSubtypeInThisAssemblyHasAManifestEntry()
        {
            // A tag-less declaration with no entry is WPROTO041 -- a warning in the editor now,
            // so it WOULD have compiled -- and the automatic pass is what is supposed to have
            // written the entry. This therefore fails when someone adds a numberless subtype and
            // the manifest was not regenerated alongside it, which is the state that used to reach
            // a player build.
            List<string> missing = new List<string>();
            int numberless = 0;

            foreach (Type candidate in typeof(WProtoManifestBase).Assembly.GetTypes())
            {
                foreach (
                    WProtoSubtypeAttribute declaration in candidate.GetCustomAttributes<WProtoSubtypeAttribute>(
                        false
                    )
                )
                {
                    if (declaration.HasTag)
                    {
                        continue;
                    }

                    numberless++;
                    if (!TryManifestTag(candidate, declaration.BaseType, out int _))
                    {
                        missing.Add(candidate.Name);
                    }
                }
            }

            Assert.IsEmpty(missing, "No manifest entry for: " + string.Join(", ", missing));
            Assert.Less(0, numberless, "The sweep found no numberless declaration to check.");
        }

        [TestCaseSource(nameof(ManifestNumberedSubtypes))]
        public void AManifestNumberedSubtypeIsWrittenUnderTheNumberTheManifestGivesIt(
            Type subType,
            Type baseType
        )
        {
            Assert.IsTrue(
                TryManifestTag(subType, baseType, out int _),
                subType.Name + " has no manifest entry"
            );

            WProtoManifestBase value = (WProtoManifestBase)Activator.CreateInstance(subType);
            byte[] buffer = EncodeBytes(value);

            // Each level writes its own include first, whatever its number, so the payload nests
            // one key per level from the root down. Walking it proves the manifest number reached
            // the wire at the level that owns it, rather than merely appearing somewhere in it.
            int offset = 0;
            foreach (int tag in ChainTags(subType))
            {
                Assert.AreEqual(
                    ((long)tag << 3) | 2,
                    ReadVarint(buffer, ref offset),
                    subType.Name + " at offset " + offset
                );
                int length = (int)ReadVarint(buffer, ref offset);
                Assert.LessOrEqual(offset + length, buffer.Length, subType.Name);
            }
        }

        [TestCaseSource(nameof(ManifestNumberedSubtypes))]
        public void AManifestNumberedSubtypeRoundTripsAsItself(Type subType, Type baseType)
        {
            WProtoManifestBase value = (WProtoManifestBase)Activator.CreateInstance(subType);
            value.Id = 3;
            value.Label = "a";

            Assert.IsInstanceOf(subType, RoundTrip(value));
            Assert.AreEqual(3, RoundTrip(value).Id);
            Assert.AreEqual("a", RoundTrip(value).Label);
            IWProtoPolymorphicFormatter polymorphic = WProtoManifestBase.WProtoFormatter.Instance;
            Assert.IsTrue(
                polymorphic.CanWrite(subType),
                baseType.Name + " cannot write " + subType.Name
            );
        }

        [Test]
        public void ADeeperManifestNumberedSubtypeIsNotWrittenUnderItsBasesNumber()
        {
            // `value is WProtoManifestBeta` is true for a Gamma, so a dispatch chain that tested the
            // shallower type first would write the Gamma under Beta's number and lose the level --
            // exactly as it would with hand-written numbers, and worth pinning here because the
            // manifest is what decided the ordering.
            WProtoManifestGamma gamma = new WProtoManifestGamma
            {
                Id = 1,
                BetaOnly = 1.5,
                GammaOnly = true,
            };
            WProtoManifestBase restored = RoundTrip<WProtoManifestBase>(gamma);

            Assert.IsInstanceOf<WProtoManifestGamma>(restored);
            Assert.IsTrue(((WProtoManifestGamma)restored).GammaOnly);
            Assert.AreEqual(1.5, ((WProtoManifestGamma)restored).BetaOnly);
        }

        [Test]
        public void AManifestNumberedChainSitsUnderALengthPrefixUnchanged()
        {
            WProtoManifestHolder holder = new WProtoManifestHolder
            {
                Value = new WProtoManifestAlpha { AlphaOnly = 7, AlphaText = "x" },
                Trailer = 2,
            };

            WProtoManifestHolder restored = RoundTrip(holder);

            Assert.IsInstanceOf<WProtoManifestAlpha>(restored.Value);
            Assert.AreEqual(7, ((WProtoManifestAlpha)restored.Value).AlphaOnly);
            Assert.AreEqual("x", ((WProtoManifestAlpha)restored.Value).AlphaText);
            Assert.AreEqual(2, restored.Trailer);
        }

        [Test]
        public void NoTwoManifestEntriesShareANumberOnOneBase()
        {
            Dictionary<string, string> owners = new Dictionary<string, string>(
                StringComparer.Ordinal
            );

            foreach (
                WProtoSubtypeTagAttribute entry in typeof(WProtoManifestBase).Assembly.GetCustomAttributes<WProtoSubtypeTagAttribute>()
            )
            {
                string key = entry.BaseType.FullName + "|" + entry.Tag;
                bool taken = owners.TryGetValue(key, out string already);
                Assert.IsFalse(
                    taken,
                    "Field number "
                        + entry.Tag
                        + " on "
                        + entry.BaseType.Name
                        + " is claimed by both "
                        + already
                        + " and "
                        + entry.SubTypeName
                );
                owners[key] = entry.SubTypeName;
            }
        }

        private static IEnumerable<TestCaseData> ManifestNumberedSubtypes()
        {
            yield return new TestCaseData(
                typeof(WProtoManifestAlpha),
                typeof(WProtoManifestBase)
            ).SetName("{m} - Alpha");
            yield return new TestCaseData(
                typeof(WProtoManifestBeta),
                typeof(WProtoManifestBase)
            ).SetName("{m} - Beta");
            yield return new TestCaseData(
                typeof(WProtoManifestGamma),
                typeof(WProtoManifestBeta)
            ).SetName("{m} - Gamma");
        }

        private static bool TryManifestTag(Type subType, Type baseType, out int tag)
        {
            foreach (
                WProtoSubtypeTagAttribute entry in subType.Assembly.GetCustomAttributes<WProtoSubtypeTagAttribute>()
            )
            {
                if (
                    string.Equals(entry.SubTypeName, subType.FullName, StringComparison.Ordinal)
                    && entry.BaseType == baseType
                )
                {
                    tag = entry.Tag;
                    return true;
                }
            }

            tag = 0;
            return false;
        }

        /// <summary>
        /// The manifest numbers a value of <paramref name="subType"/> is written under, outermost
        /// first.
        /// </summary>
        /// <param name="subType">The concrete type being written.</param>
        /// <returns>One field number per level between the root and that type.</returns>
        private static List<int> ChainTags(Type subType)
        {
            List<int> tags = new List<int>();
            for (
                Type current = subType;
                current != null && current != typeof(WProtoManifestBase);
                current = current.BaseType
            )
            {
                Assert.IsTrue(
                    TryManifestTag(current, current.BaseType, out int tag),
                    current.Name + " has no manifest entry"
                );
                tags.Insert(0, tag);
            }

            return tags;
        }

        private static long ReadVarint(byte[] buffer, ref int offset)
        {
            long value = 0;
            int shift = 0;
            while (offset < buffer.Length)
            {
                byte current = buffer[offset++];
                value |= (long)(current & 0x7F) << shift;
                if (current < 0x80)
                {
                    return value;
                }

                shift += 7;
            }

            Assert.Fail("The payload ended inside a varint.");
            return value;
        }

        private static byte[] EncodeBytes<T>(T value)
        {
            IWProtoFormatter<T> formatter = WProtoFormatterProvider.Get<T>();
            byte[] buffer = new byte[formatter.Measure(value)];
            WProtoWriter writer = new WProtoWriter(buffer);
            Assert.IsTrue(formatter.Write(ref writer, value));
            Assert.AreEqual(buffer.Length, writer.Position, "Measure disagreed with Write");
            return buffer;
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
