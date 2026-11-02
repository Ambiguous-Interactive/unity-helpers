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
    public sealed class RelationalComponentInitializerTests : CommonTestBase
    {
        private static bool CacheContainsField(string cache, FieldInfo field)
        {
            return string.Equals(cache, "FieldGetterCache", System.StringComparison.Ordinal)
                ? ReflectionHelpers.IsFieldGetterCached(field)
                : ReflectionHelpers.IsFieldSetterCached(field);
        }

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

        [TearDown]
        public void ClearCachesAfterCapabilityScope()
        {
            ClearWarmedCaches();
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

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void InitializeSkipsOpenGenericsAndWarmsClosedInheritedFields(
            bool expressions,
            bool dynamicIl
        )
        {
            using ReflectionCapabilityScope capabilities = new(expressions, dynamicIl);
            Type openType = typeof(RelationalGenericBase<>);
            Type openDerivedType = typeof(RelationalGenericOpenDerived<>);
            Type closedType = typeof(RelationalGenericBase<BoxCollider>);
            Type derivedType = typeof(RelationalGenericDerived);
            RelationalComponentInitializer.Report report =
                RelationalComponentInitializer.Initialize(
                    new[]
                    {
                        openType,
                        openDerivedType,
                        closedType,
                        derivedType,
                        typeof(PrewarmTesterComponent),
                    },
                    logSummary: false
                );

            Assert.AreEqual(5, report.TypesVisited);
            Assert.AreEqual(0, report.Errors);
            Assert.AreEqual(0, report.Warnings);
            Assert.AreEqual(3, report.TypesWarmed);
            Assert.AreEqual(9, report.FieldsWarmed);
            Assert.IsFalse(report.WarmedFieldsPerType.ContainsKey(openType));
            Assert.IsFalse(report.WarmedFieldsPerType.ContainsKey(openDerivedType));
            Assert.AreEqual(3, report.WarmedFieldsPerType.ValueFor(closedType));
            Assert.AreEqual(3, report.WarmedFieldsPerType.ValueFor(derivedType));
            foreach (FieldInfo field in closedType.GetFields())
            {
                Assert.IsTrue(ReflectionHelpers.IsFieldGetterCached(field), field.Name);
                Assert.IsTrue(ReflectionHelpers.IsFieldSetterCached(field), field.Name);
            }
            foreach (Type skippedType in new[] { openType, openDerivedType })
            {
                Assert.IsFalse(SiblingComponentExtensions.HasCachedFieldMetadata(skippedType));
                Assert.IsFalse(ChildComponentExtensions.HasCachedFieldMetadata(skippedType));
                Assert.IsFalse(ParentComponentExtensions.HasCachedFieldMetadata(skippedType));
            }
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

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void ClosedGenericFieldsBindAfterOpenGenericPrewarm(bool expressions, bool dynamicIl)
        {
            using ReflectionCapabilityScope capabilities = new(expressions, dynamicIl);
            GameObject parent = Track(new GameObject(nameof(parent)));
            BoxCollider expectedParent = parent.AddComponent<BoxCollider>();
            GameObject owner = Track(new GameObject(nameof(owner)));
            owner.transform.SetParent(parent.transform);
            BoxCollider expectedSibling = owner.AddComponent<BoxCollider>();
            RelationalGenericDerived target = owner.AddComponent<RelationalGenericDerived>();
            GameObject child = Track(new GameObject(nameof(child)));
            child.transform.SetParent(owner.transform);
            BoxCollider expectedChild = child.AddComponent<BoxCollider>();

            RelationalComponentInitializer.Report report =
                RelationalComponentInitializer.Initialize(
                    new[] { typeof(RelationalGenericBase<>), typeof(RelationalGenericDerived) },
                    logSummary: false
                );
            Assert.AreEqual(0, report.Errors);
            Assert.AreEqual(3, report.FieldsWarmed);
            target.AssignRelationalComponents();

            Assert.AreEqual(expectedParent, target.parent);
            CollectionAssert.AreEqual(new[] { expectedChild }, target.children);
            CollectionAssert.AreEqual(new[] { expectedSibling }, target.siblings);
        }
    }
}
