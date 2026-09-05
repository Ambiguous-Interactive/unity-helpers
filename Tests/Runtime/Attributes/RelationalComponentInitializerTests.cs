// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Reflection;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class RelationalComponentInitializerTests
    {
        // The runner reuses static caches between runs; reset them so prewarm assertions start cold.
        [SetUp]
        public void ClearWarmedCaches()
        {
            ReflectionHelpers.ClearFieldGetterCache();
            ReflectionHelpers.ClearFieldSetterCache();
            ReflectionHelpers.ClearHashSetClearerCache();
            SiblingComponentExtensions.ClearCachedFieldMetadata();
            ChildComponentExtensions.ClearCachedFieldMetadata();
            ParentComponentExtensions.ClearCachedFieldMetadata();
        }

        private static bool CacheContainsField(string cache, FieldInfo field)
        {
            return cache == "FieldGetterCache"
                ? ReflectionHelpers.IsFieldGetterCached(field)
                : ReflectionHelpers.IsFieldSetterCached(field);
        }

        [Test]
        public void InitializeWarmsWhatTheAssignmentPathActuallyReads()
        {
            Type testerType = typeof(PrewarmHashSetTesterComponent);

            Assert.IsFalse(
                SiblingComponentExtensions.HasCachedFieldMetadata(testerType),
                "Sibling metadata unexpectedly cached before prewarm."
            );
            Assert.IsFalse(
                ChildComponentExtensions.HasCachedFieldMetadata(testerType),
                "Child metadata unexpectedly cached before prewarm."
            );
            Assert.IsFalse(
                ParentComponentExtensions.HasCachedFieldMetadata(testerType),
                "Parent metadata unexpectedly cached before prewarm."
            );
            Assert.IsFalse(
                ReflectionHelpers.IsHashSetClearerCached(typeof(BoxCollider)),
                "HashSet clearer unexpectedly cached before prewarm."
            );

            RelationalComponentInitializer.Report report =
                RelationalComponentInitializer.Initialize(new[] { testerType }, logSummary: false);

            Assert.AreEqual(0, report.Errors, "Prewarm reported errors.");

            // Set-valued fields also require the clearer cache; leaving it cold moves first-use cost into Awake.
            Assert.IsTrue(
                SiblingComponentExtensions.HasCachedFieldMetadata(testerType),
                "Sibling metadata not cached after prewarm."
            );
            Assert.IsTrue(
                ChildComponentExtensions.HasCachedFieldMetadata(testerType),
                "Child metadata not cached after prewarm."
            );
            Assert.IsTrue(
                ParentComponentExtensions.HasCachedFieldMetadata(testerType),
                "Parent metadata not cached after prewarm."
            );
            Assert.IsTrue(
                ReflectionHelpers.IsHashSetClearerCached(typeof(BoxCollider)),
                "HashSet clearer not cached after prewarm."
            );
        }

        [Test]
        public void InitializeWarmsReflectionCachesForProvidedType()
        {
            Type testerType = typeof(PrewarmTesterComponent);
            FieldInfo parentField = testerType.GetField(nameof(PrewarmTesterComponent.parentBody));
            FieldInfo siblingField = testerType.GetField(
                nameof(PrewarmTesterComponent.siblingCollider)
            );
            FieldInfo childField = testerType.GetField(
                nameof(PrewarmTesterComponent.childColliders)
            );
            Assert.NotNull(parentField);
            Assert.NotNull(siblingField);
            Assert.NotNull(childField);

            Assert.IsFalse(
                CacheContainsField("FieldGetterCache", parentField),
                "Parent field unexpectedly present in getter cache before prewarm."
            );
            Assert.IsFalse(
                CacheContainsField("FieldSetterCache", parentField),
                "Parent field unexpectedly present in setter cache before prewarm."
            );

            RelationalComponentInitializer.Report report =
                RelationalComponentInitializer.Initialize(new[] { testerType }, logSummary: false);

            Assert.IsTrue(
                CacheContainsField("FieldGetterCache", parentField),
                "Parent field not present in getter cache after prewarm."
            );
            Assert.IsTrue(
                CacheContainsField("FieldSetterCache", parentField),
                "Parent field not present in setter cache after prewarm."
            );
            Assert.IsTrue(
                CacheContainsField("FieldGetterCache", siblingField),
                "Sibling field not present in getter cache after prewarm."
            );
            Assert.IsTrue(
                CacheContainsField("FieldSetterCache", siblingField),
                "Sibling field not present in setter cache after prewarm."
            );
            Assert.IsTrue(
                CacheContainsField("FieldGetterCache", childField),
                "Child field not present in getter cache after prewarm."
            );
            Assert.IsTrue(
                CacheContainsField("FieldSetterCache", childField),
                "Child field not present in setter cache after prewarm."
            );

            Assert.GreaterOrEqual(report.FieldsWarmed, 3, "Expected at least three warmed fields.");
            Assert.That(
                report.WarmedFieldsPerType.ContainsKey(testerType),
                "Missing per-type warm results."
            );
            Assert.GreaterOrEqual(
                report.WarmedFieldsPerType.ValueFor(testerType),
                3,
                "Per-type warmed count too low."
            );
        }
    }
}
