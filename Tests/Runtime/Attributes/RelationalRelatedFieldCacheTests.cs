// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;

    [TestFixture]
    [Category("Fast")]
    public sealed class RelationalRelatedFieldCacheTests : CommonTestBase
    {
        private static readonly AttributeMetadataCache.RelationalAttributeKind[] Relationships =
        {
            AttributeMetadataCache.RelationalAttributeKind.Sibling,
            AttributeMetadataCache.RelationalAttributeKind.Parent,
            AttributeMetadataCache.RelationalAttributeKind.Child,
        };

        private static readonly AttributeMetadataCache.FieldKind[] Shapes =
        {
            AttributeMetadataCache.FieldKind.Single,
            AttributeMetadataCache.FieldKind.Array,
            AttributeMetadataCache.FieldKind.List,
            AttributeMetadataCache.FieldKind.HashSet,
        };

        private static void ClearBindingCaches()
        {
            SiblingComponentExtensions.ClearCachedFieldMetadata();
            ParentComponentExtensions.ClearCachedFieldMetadata();
            ChildComponentExtensions.ClearCachedFieldMetadata();
            ReflectionHelpers.ClearFieldGetterCache();
            ReflectionHelpers.ClearFieldSetterCache();
        }

        private static void AssertShapes<T>(
            T[] expected,
            T single,
            T[] array,
            List<T> list,
            HashSet<T> set
        )
            where T : Component
        {
            Assert.AreSame(expected.Length == 0 ? null : expected[0], single);
            CollectionAssert.AreEqual(expected, array);
            CollectionAssert.AreEqual(expected, list);
            CollectionAssert.AreEquivalent(expected, set);
        }

        private static void AssertBindings(
            RelationalRelatedNameDerived target,
            GameObject source,
            int direction
        )
        {
            Collider[] broad =
                source == null ? Array.Empty<Collider>() : source.GetComponents<Collider>();
            BoxCollider[] narrow =
                source == null ? Array.Empty<BoxCollider>() : source.GetComponents<BoxCollider>();
            switch (direction)
            {
                case 0:
                    target.AssignSiblingComponents();
                    AssertShapes(
                        broad,
                        target.BaseSiblingSingle,
                        target.BaseSiblingArray,
                        target.BaseSiblingList,
                        target.BaseSiblingSet
                    );
                    AssertShapes(
                        narrow,
                        target.DerivedSiblingSingle,
                        target.DerivedSiblingArray,
                        target.DerivedSiblingList,
                        target.DerivedSiblingSet
                    );
                    break;
                case 1:
                    target.AssignParentComponents();
                    AssertShapes(
                        broad,
                        target.BaseParentSingle,
                        target.BaseParentArray,
                        target.BaseParentList,
                        target.BaseParentSet
                    );
                    AssertShapes(
                        narrow,
                        target.DerivedParentSingle,
                        target.DerivedParentArray,
                        target.DerivedParentList,
                        target.DerivedParentSet
                    );
                    break;
                case 2:
                    target.AssignChildComponents();
                    AssertShapes(
                        broad,
                        target.BaseChildSingle,
                        target.BaseChildArray,
                        target.BaseChildList,
                        target.BaseChildSet
                    );
                    AssertShapes(
                        narrow,
                        target.DerivedChildSingle,
                        target.DerivedChildArray,
                        target.DerivedChildList,
                        target.DerivedChildSet
                    );
                    break;
            }
        }

        [Test]
        public void SameNamedRelatedFieldsUseTheirOwnDeclaredElementTypes(
            [Values(0, 1, 2)] int direction,
            [Values(0, 1, 2, 3, 4, 5)] int cacheMode,
            [Values(0, 1, 2)] int capability
        )
        {
            using IDisposable scope = new ReflectionCapabilityScope(
                capability == 0,
                capability == 1
            );
            Lazy<AttributeMetadataCache> previous = AttributeMetadataCache._lazyInstance;
            AttributeMetadataCache cache = cacheMode == 0 ? null : CreateCache(cacheMode);
            AttributeMetadataCache._lazyInstance = new Lazy<AttributeMetadataCache>(() => cache);
            ClearBindingCaches();
            try
            {
                GameObject parent = Track(new GameObject("RelatedParent"));
                GameObject owner = Track(new GameObject("RelatedOwner"));
                owner.transform.SetParent(parent.transform);
                GameObject child = Track(new GameObject("RelatedChild"));
                child.transform.SetParent(owner.transform);
                RelationalRelatedNameDerived target =
                    owner.AddComponent<RelationalRelatedNameDerived>();
                GameObject source =
                    direction == 0 ? owner
                    : direction == 1 ? parent
                    : child;
                SphereCollider first = source.AddComponent<SphereCollider>();
                source.AddComponent<BoxCollider>();
                AssertBindings(target, source, direction);
                AssertBindings(target, source, direction);
                UnityEngine.Object.DestroyImmediate(first); // UNH-SUPPRESS: Rebinding must replace the former broad-type first match.
                source.AddComponent<SphereCollider>();
                source.AddComponent<BoxCollider>();
                AssertBindings(target, source, direction);
                if (direction == 0)
                {
                    foreach (Collider candidate in source.GetComponents<Collider>())
                    {
                        UnityEngine.Object.DestroyImmediate(candidate); // UNH-SUPPRESS: Rebinding must clear every shape after all candidates disappear.
                    }
                }
                else
                {
                    owner.transform.SetParent(null);
                    child.transform.SetParent(null);
                }
                AssertBindings(target, null, direction);
            }
            finally
            {
                AttributeMetadataCache._lazyInstance = previous;
                ClearBindingCaches();
            }
        }

        private AttributeMetadataCache CreateCache(int cacheMode)
        {
            AttributeMetadataCache cache = CreateScriptableObject<AttributeMetadataCache>();
            List<AttributeMetadataCache.RelationalFieldMetadata> fields = new();
            Type[] elementTypes = cacheMode switch
            {
                1 => new[] { typeof(Collider), typeof(BoxCollider) },
                2 => new[] { typeof(Collider) },
                3 => new[] { typeof(BoxCollider) },
                4 => new Type[] { null },
                5 => new[] { typeof(BoxCollider), typeof(Collider) },
                _ => Array.Empty<Type>(),
            };
            for (int direction = 0; direction < Relationships.Length; ++direction)
            {
                for (int shape = 0; shape < Shapes.Length; ++shape)
                {
                    string fieldName = RelationalRelatedNameBase.RelationalFieldNames[
                        direction * Shapes.Length + shape
                    ];
                    foreach (Type elementType in elementTypes)
                    {
                        fields.Add(
                            new AttributeMetadataCache.RelationalFieldMetadata(
                                fieldName,
                                Relationships[direction],
                                Shapes[shape],
                                elementType == null
                                    ? string.Empty
                                    : elementType.AssemblyQualifiedName,
                                true
                            )
                        );
                    }
                }
            }
            cache._relationalTypeMetadata = new[]
            {
                new AttributeMetadataCache.RelationalTypeMetadata(
                    typeof(RelationalRelatedNameDerived).AssemblyQualifiedName,
                    fields.ToArray()
                ),
            };
            AttributeMetadataCacheTestUtilities.Rebuild(cache);
            Assert.IsTrue(
                cache.TryGetRelationalFields(
                    typeof(RelationalRelatedNameDerived),
                    out AttributeMetadataCache.RelationalFieldMetadata[] cachedFields
                )
            );
            Assert.AreEqual(12 * elementTypes.Length, cachedFields.Length);
            return cache;
        }
    }
}
