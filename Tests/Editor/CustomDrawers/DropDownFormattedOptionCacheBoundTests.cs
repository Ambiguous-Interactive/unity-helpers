// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.CustomDrawers
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers;
    using WallstopStudios.UnityHelpers.Editor.CustomDrawers.Utils;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.CustomDrawers.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    public sealed class DropDownFormattedOptionCacheBoundTests : CommonTestBase
    {
        private const int MaxFormattedOptionCacheEntries = 2048;
        private const int ChurnedOptionCount = 2100;
        private const int ControlOptionCount = 128;
        private const int DestroyedOptionCount = 64;
        private const string SubjectOptionName = "FormattedOptionSubject";
        private const string StaleProbeNameBeforeRename = "FormattedOptionStaleProbe";
        private const string StaleProbeNameAfterRename = "FormattedOptionStaleProbeRenamed";
        private const string EvictionProbeNameBeforeChurn = "FormattedOptionEvictionProbe";
        private const string EvictionProbeNameAfterChurn = "FormattedOptionEvictionProbeRenamed";
        private const string DestroyedOptionNamePrefix = "FormattedOptionDestroyed";
        private const string DestroyedOptionLabel = "(None)";

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            DropDownShared.ClearFormattedOptionCache();
        }

        [TearDown]
        public void ClearFormattedOptionCacheAfterTest()
        {
            DropDownShared.ClearFormattedOptionCache();
        }

        /// <summary>
        /// Pins that the shared dropdown label cache stops growing at its bound, and that it is still
        /// caching when it stops: a cache that silently ceased to store anything would satisfy the
        /// upper bound on its own.
        /// </summary>
        [Test]
        public void FormatOptionEvictsLeastRecentlyUsedOption()
        {
            for (int optionIndex = 0; optionIndex < ControlOptionCount; optionIndex++)
            {
                object option = optionIndex;
                _ = DropDownShared.FormatOption(option);
            }

            int controlCount = DropDownShared.TestHooks.FormattedOptionCacheCount;
            Assert.AreEqual(
                ControlOptionCount,
                controlCount,
                $"{ControlOptionCount} distinct options below the bound should all be retained; a "
                    + "cache that stopped being exercised would report a smaller count."
            );

            for (
                int optionIndex = ControlOptionCount;
                optionIndex < ChurnedOptionCount;
                optionIndex++
            )
            {
                object option = optionIndex;
                _ = DropDownShared.FormatOption(option);
            }

            int churnedCount = DropDownShared.TestHooks.FormattedOptionCacheCount;
            Assert.That(
                churnedCount,
                Is.GreaterThan(0),
                $"{ChurnedOptionCount} formatted options left an empty cache, so the cache stopped "
                    + "caching rather than evicting."
            );
            Assert.That(
                churnedCount,
                Is.GreaterThan(controlCount),
                "The cache never grew past its control size, so nothing after the control was cached."
            );
            Assert.That(
                churnedCount,
                Is.LessThanOrEqualTo(MaxFormattedOptionCacheEntries),
                $"{ChurnedOptionCount} distinct options must not leave more than "
                    + $"{MaxFormattedOptionCacheEntries} entries retained."
            );
        }

        /// <summary>
        /// Pins that an evicted option is formatted to exactly the text it had before, so eviction
        /// costs a recompute and never a different label, for both dropdown label caches.
        /// </summary>
        /// <remarks>
        /// A renamed option is the observable test for cache membership: a retained entry answers with
        /// the name it was cached under, an evicted one recomputes from the live object. The stale
        /// probe proves the formatter caches at all and the eviction probe proves the churn crossed
        /// the bound, so neither the retained nor the evicted claim can pass vacuously.
        /// </remarks>
        [Test]
        [TestCaseSource(nameof(FormattedOptionCaches))]
        public void FormatOptionReturnsSameTextAfterEviction(Func<object, string> formatOption)
        {
            BoundedDrawerCacheChurnHost subject =
                CreateScriptableObject<BoundedDrawerCacheChurnHost>();
            subject.name = SubjectOptionName;
            BoundedDrawerCacheChurnHost staleProbe =
                CreateScriptableObject<BoundedDrawerCacheChurnHost>();
            staleProbe.name = StaleProbeNameBeforeRename;
            BoundedDrawerCacheChurnHost evictionProbe =
                CreateScriptableObject<BoundedDrawerCacheChurnHost>();
            evictionProbe.name = EvictionProbeNameBeforeChurn;

            string subjectTextBeforeChurn = formatOption(subject);
            Assert.AreEqual(SubjectOptionName, subjectTextBeforeChurn);
            Assert.AreEqual(StaleProbeNameBeforeRename, formatOption(staleProbe));
            Assert.AreEqual(EvictionProbeNameBeforeChurn, formatOption(evictionProbe));

            staleProbe.name = StaleProbeNameAfterRename;
            Assert.AreEqual(
                StaleProbeNameBeforeRename,
                formatOption(staleProbe),
                "A renamed option was recomputed while still cached, so a rename cannot tell a "
                    + "retained entry from an evicted one and the eviction below is unobservable."
            );

            for (int optionIndex = 0; optionIndex < ChurnedOptionCount; optionIndex++)
            {
                object option = optionIndex;
                _ = formatOption(option);
            }

            evictionProbe.name = EvictionProbeNameAfterChurn;
            Assert.AreEqual(
                EvictionProbeNameAfterChurn,
                formatOption(evictionProbe),
                $"The probe still answered with its cached label after {ChurnedOptionCount} other "
                    + "options, so the churn never reached the bound."
            );

            Assert.AreEqual(
                subjectTextBeforeChurn,
                formatOption(subject),
                "An evicted option must be recomputed to the same text it was first cached with."
            );
        }

        /// <summary>
        /// Pins the regression the bound exists for: options are keyed on the option value itself, so
        /// an unbounded cache kept every destroyed <c>ScriptableObject</c> a dropdown ever rendered
        /// alive for the editor process.
        /// </summary>
        [Test]
        public void FormatOptionDoesNotRootDestroyedObjects()
        {
            List<BoundedDrawerCacheChurnHost> destroyedOptions = new(DestroyedOptionCount);
            for (int optionIndex = 0; optionIndex < DestroyedOptionCount; optionIndex++)
            {
                BoundedDrawerCacheChurnHost option =
                    CreateScriptableObject<BoundedDrawerCacheChurnHost>();
                option.name = DestroyedOptionNamePrefix + optionIndex;
                destroyedOptions.Add(option);
                Assert.AreEqual(option.name, DropDownShared.FormatOption(option));
            }

            Assert.AreEqual(
                DestroyedOptionCount,
                DropDownShared.TestHooks.FormattedOptionCacheCount,
                "Every option must be cached before it is destroyed, or this fixture measures an "
                    + "empty cache."
            );

            foreach (BoundedDrawerCacheChurnHost option in destroyedOptions)
            {
                UnityEngine.Object.DestroyImmediate(option); // UNH-SUPPRESS: the destroyed option is the subject
            }

            for (int optionIndex = 0; optionIndex < ChurnedOptionCount; optionIndex++)
            {
                object option = optionIndex;
                _ = DropDownShared.FormatOption(option);
            }

            int churnedCount = DropDownShared.TestHooks.FormattedOptionCacheCount;
            Assert.That(
                churnedCount,
                Is.GreaterThan(0),
                "The cache emptied itself rather than evicting, so this fixture proves nothing about "
                    + "destroyed options."
            );
            Assert.That(
                churnedCount,
                Is.LessThanOrEqualTo(MaxFormattedOptionCacheEntries),
                $"Destroyed options plus {ChurnedOptionCount} live ones must not leave more than "
                    + $"{MaxFormattedOptionCacheEntries} entries retained."
            );

            foreach (BoundedDrawerCacheChurnHost option in destroyedOptions)
            {
                Assert.AreEqual(
                    DestroyedOptionLabel,
                    DropDownShared.FormatOption(option),
                    "A destroyed option still answered with the name it had while alive, so its "
                        + "entry outlived the object it keys on."
                );
            }
        }

        private static IEnumerable<TestCaseData> FormattedOptionCaches()
        {
            yield return new TestCaseData(
                (Func<object, string>)DropDownShared.FormatOption
            ).SetName("Cache.DropDownShared");

            yield return new TestCaseData(
                (Func<object, string>)WValueDropDownDrawer.TestHooks.FormatOptionCached
            ).SetName("Cache.WValueDropDown");
        }
    }
}
