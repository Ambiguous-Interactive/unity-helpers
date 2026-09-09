// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if UNITY_EDITOR
namespace WallstopStudios.UnityHelpers.Tests.Core.Attributes
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class RelationalComponentAssignerTests : CommonTestBase
    {
        [Test]
        public void HasRelationalAssignmentsRespectsMetadata()
        {
            AttributeMetadataCache cache = CreateCacheWithSiblingSelfInclusionMetadata();
            RelationalComponentAssigner assigner = new(cache);

            Assert.IsTrue(
                assigner.HasRelationalAssignments(typeof(SiblingSelfInclusionTester)),
                $"Expected HasRelationalAssignments to be true for {nameof(SiblingSelfInclusionTester)} with relational metadata"
            );
            Assert.IsFalse(
                assigner.HasRelationalAssignments(typeof(EnabledProbe)),
                $"Expected HasRelationalAssignments to be false for {nameof(EnabledProbe)} without relational metadata"
            );
        }

        [Test]
        public void HasRelationalAssignmentsReturnsFalseForNullType()
        {
            AttributeMetadataCache cache = CreateCacheWithSiblingSelfInclusionMetadata();
            RelationalComponentAssigner assigner = new(cache);

            Assert.IsFalse(
                assigner.HasRelationalAssignments(null),
                "Expected HasRelationalAssignments to be false for null type"
            );
        }

        [Test]
        public void AssignIEnumerableAssignsOnlyRelationalTypesAndSkipsNulls()
        {
            AttributeMetadataCache cache = CreateCacheWithSiblingSelfInclusionMetadata();
            RelationalComponentAssigner assigner = new(cache);

            GameObject go1 = NewGameObject("Relational");
            Assert.IsTrue(go1 != null, "Failed to create Relational GameObject");

            SpriteRenderer sr1 = go1.AddComponent<SpriteRenderer>();
            Assert.IsTrue(sr1 != null, "Failed to add SpriteRenderer to Relational GameObject");

            SiblingSelfInclusionTester consumer = go1.AddComponent<SiblingSelfInclusionTester>();
            Assert.IsTrue(
                consumer != null,
                "Failed to add SiblingSelfInclusionTester to Relational GameObject"
            );

            GameObject go2 = NewGameObject("NonRelational");
            Assert.IsTrue(go2 != null, "Failed to create NonRelational GameObject");

            EnabledProbe non = go2.AddComponent<EnabledProbe>();
            Assert.IsTrue(non != null, "Failed to add EnabledProbe to NonRelational GameObject");

            Assert.IsTrue(
                consumer.siblingRenderer == null,
                $"Precondition: {nameof(SiblingSelfInclusionTester)}.siblingRenderer should start null, was: {consumer.siblingRenderer}"
            );

            List<Component> items = new() { consumer, null, non.transform };
            assigner.Assign(items);

            Assert.IsTrue(
                consumer.siblingRenderer != null,
                $"Expected {nameof(SiblingSelfInclusionTester)}.siblingRenderer to be assigned by Assign(IEnumerable<Component>), but it was null"
            );

            Assert.IsTrue(
                non != null,
                $"{nameof(EnabledProbe)} component should still be valid after Assign"
            );
        }

        [Test]
        public void AssignSingleComponentAssignsRelationalFields()
        {
            AttributeMetadataCache cache = CreateCacheWithSiblingSelfInclusionMetadata();
            RelationalComponentAssigner assigner = new(cache);

            GameObject go = NewGameObject("SingleComponent");
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            SiblingSelfInclusionTester consumer = go.AddComponent<SiblingSelfInclusionTester>();

            Assert.IsTrue(
                consumer.siblingRenderer == null,
                $"Precondition: {nameof(SiblingSelfInclusionTester)}.siblingRenderer should start null"
            );

            assigner.Assign(consumer);

            Assert.IsTrue(
                consumer.siblingRenderer != null,
                $"Expected {nameof(SiblingSelfInclusionTester)}.siblingRenderer to be assigned by Assign(Component)"
            );
        }

        [Test]
        public void AssignSingleNullComponentDoesNotThrow()
        {
            AttributeMetadataCache cache = CreateCacheWithSiblingSelfInclusionMetadata();
            RelationalComponentAssigner assigner = new(cache);

            Assert.DoesNotThrow(
                () => assigner.Assign((Component)null),
                "Assign(null) should not throw"
            );
        }

        [Test]
        public void AssignNullEnumerableDoesNotThrow()
        {
            AttributeMetadataCache cache = CreateCacheWithSiblingSelfInclusionMetadata();
            RelationalComponentAssigner assigner = new(cache);

            Assert.DoesNotThrow(
                () => assigner.Assign((IEnumerable<Component>)null),
                "Assign(null IEnumerable) should not throw"
            );
        }

        [Test]
        public void AssignEmptyEnumerableDoesNotThrow()
        {
            AttributeMetadataCache cache = CreateCacheWithSiblingSelfInclusionMetadata();
            RelationalComponentAssigner assigner = new(cache);

            Assert.DoesNotThrow(
                () => assigner.Assign(new List<Component>()),
                "Assign(empty IEnumerable) should not throw"
            );
        }

        [Test]
        public void AssignEnumerableWithOnlyNullsDoesNotThrow()
        {
            AttributeMetadataCache cache = CreateCacheWithSiblingSelfInclusionMetadata();
            RelationalComponentAssigner assigner = new(cache);

            List<Component> items = new() { null, null, null };

            Assert.DoesNotThrow(
                () => assigner.Assign(items),
                "Assign(IEnumerable with only nulls) should not throw"
            );
        }

        [Test]
        public void AssignNonRelationalComponentDoesNotModifyComponent()
        {
            AttributeMetadataCache cache = CreateCacheWithSiblingSelfInclusionMetadata();
            RelationalComponentAssigner assigner = new(cache);

            GameObject go = NewGameObject("NonRelational");
            EnabledProbe probe = go.AddComponent<EnabledProbe>();
            Transform originalTransform = probe.transform;

            assigner.Assign(probe);

            Assert.IsTrue(
                probe != null,
                $"{nameof(EnabledProbe)} should still be valid after Assign"
            );
            Assert.AreEqual(
                originalTransform,
                probe.transform,
                "Transform should be unchanged after Assign on non-relational component"
            );
        }

        [Test]
        public void AssignHierarchyAssignsAllRelationalComponentsInHierarchy()
        {
            AttributeMetadataCache cache = CreateCacheWithSiblingSelfInclusionMetadata();
            RelationalComponentAssigner assigner = new(cache);

            GameObject root = NewGameObject("Root");
            GameObject child = NewGameObject("Child");
            child.transform.SetParent(root.transform);

            SpriteRenderer rootRenderer = root.AddComponent<SpriteRenderer>();
            SiblingSelfInclusionTester rootConsumer =
                root.AddComponent<SiblingSelfInclusionTester>();

            SpriteRenderer childRenderer = child.AddComponent<SpriteRenderer>();
            SiblingSelfInclusionTester childConsumer =
                child.AddComponent<SiblingSelfInclusionTester>();

            Assert.IsTrue(
                rootConsumer.siblingRenderer == null,
                "Precondition: root siblingRenderer should start null"
            );
            Assert.IsTrue(
                childConsumer.siblingRenderer == null,
                "Precondition: child siblingRenderer should start null"
            );

            assigner.AssignHierarchy(root);

            Assert.IsTrue(
                rootConsumer.siblingRenderer != null,
                "Root siblingRenderer should be assigned after AssignHierarchy"
            );
            Assert.IsTrue(
                childConsumer.siblingRenderer != null,
                "Child siblingRenderer should be assigned after AssignHierarchy"
            );
        }

        [Test]
        public void AssignHierarchyWithNullRootDoesNotThrow()
        {
            AttributeMetadataCache cache = CreateCacheWithSiblingSelfInclusionMetadata();
            RelationalComponentAssigner assigner = new(cache);

            Assert.DoesNotThrow(
                () => assigner.AssignHierarchy(null),
                "AssignHierarchy(null) should not throw"
            );
        }

        private AttributeMetadataCache CreateCacheWithSiblingSelfInclusionMetadata()
        {
            AttributeMetadataCache cache = CreateScriptableObject<AttributeMetadataCache>();
            AttributeMetadataCache.RelationalTypeMetadata relationalMetadata = new(
                typeof(SiblingSelfInclusionTester).AssemblyQualifiedName,
                new[]
                {
                    new AttributeMetadataCache.RelationalFieldMetadata(
                        "siblingRenderer",
                        AttributeMetadataCache.RelationalAttributeKind.Sibling,
                        AttributeMetadataCache.FieldKind.Single,
                        typeof(SpriteRenderer).AssemblyQualifiedName,
                        false
                    ),
                }
            );

            cache.SetMetadata(
                Array.Empty<string>(),
                Array.Empty<AttributeMetadataCache.TypeFieldMetadata>(),
                new[] { relationalMetadata },
                Array.Empty<AttributeMetadataCache.AutoLoadSingletonEntry>()
            );
            cache.ForceRebuildForTests();
            return cache;
        }
    }
}
#endif
