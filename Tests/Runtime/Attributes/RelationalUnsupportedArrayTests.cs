// MIT License - Copyright (c) 2026 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Attributes
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Core.TestTypes;
    using static WallstopStudios.UnityHelpers.Core.Attributes.RelationalComponentProcessor;

    [TestFixture]
    [Category("Fast")]
    public sealed class RelationalUnsupportedArrayTests : CommonTestBase
    {
        [TearDown]
        public void ClearCaches()
        {
            ParentComponentExtensions.ClearCachedFieldMetadata();
            SiblingComponentExtensions.ClearCachedFieldMetadata();
            ChildComponentExtensions.ClearCachedFieldMetadata();
            ReflectionHelpers.ClearFieldGetterCache();
            ReflectionHelpers.ClearFieldSetterCache();
        }

        [TestCase(typeof(SpriteRenderer), (int)FieldKind.Single, typeof(SpriteRenderer))]
        [TestCase(typeof(Component), (int)FieldKind.Single, typeof(Component))]
        [TestCase(typeof(UnityEngine.Object), (int)FieldKind.Single, typeof(UnityEngine.Object))]
        [TestCase(typeof(object), (int)FieldKind.Single, typeof(object))]
        [TestCase(typeof(ITestInterface), (int)FieldKind.Single, typeof(ITestInterface))]
        [TestCase(typeof(SpriteRenderer[]), (int)FieldKind.Array, typeof(SpriteRenderer))]
        [TestCase(typeof(ITestInterface[]), (int)FieldKind.Array, typeof(ITestInterface))]
        [TestCase(typeof(List<SpriteRenderer>), (int)FieldKind.List, typeof(SpriteRenderer))]
        [TestCase(typeof(List<ITestInterface>), (int)FieldKind.List, typeof(ITestInterface))]
        [TestCase(typeof(HashSet<SpriteRenderer>), (int)FieldKind.HashSet, typeof(SpriteRenderer))]
        [TestCase(typeof(HashSet<ITestInterface>), (int)FieldKind.HashSet, typeof(ITestInterface))]
        public void SupportedFieldsKeepTheirDeclaredElementType(
            Type fieldType,
            int expectedKind,
            Type expectedElement
        )
        {
            Assert.That(
                TryGetFieldKind(fieldType, out FieldKind kind, out Type elementType),
                Is.True
            );
            Assert.That(kind, Is.EqualTo((FieldKind)expectedKind));
            Assert.That(elementType, Is.EqualTo(expectedElement));
        }

        [TestCase(null)]
        [TestCase(typeof(int))]
        [TestCase(typeof(string))]
        [TestCase(typeof(GameObject))]
        [TestCase(typeof(SpriteRenderer[,]))]
        [TestCase(typeof(SpriteRenderer[,,]))]
        [TestCase(typeof(SpriteRenderer[][]))]
        [TestCase(typeof(ITestInterface[][]))]
        [TestCase(typeof(List<int>))]
        [TestCase(typeof(HashSet<int>))]
        [TestCase(typeof(List<SpriteRenderer[]>))]
        [TestCase(typeof(HashSet<List<SpriteRenderer>>))]
        public void UnsupportedFieldsHaveNoBindingShape(Type fieldType)
        {
            Assert.That(
                TryGetFieldKind(fieldType, out FieldKind kind, out Type elementType),
                Is.False
            );
            Assert.That(kind, Is.EqualTo(default(FieldKind)));
            Assert.That(elementType, Is.Null);
        }

        [Test]
        public void UnsupportedArraysDoNotInterruptValidFields(
            [Values(0, 1, 2)] int relation,
            [Values(0, 1, 2)] int capability
        )
        {
            VerifyUnsupportedFields(relation, capability, false);
        }

        [Test]
        public void CachedMetadataSkipsUnsupportedFields(
            [Values(0, 1, 2)] int relation,
            [Values(0, 1, 2)] int capability
        )
        {
            VerifyUnsupportedFields(relation, capability, true);
        }

        [Test]
        public void NonVectorRankOneArraysHaveNoBindingShape()
        {
            Type fieldType = typeof(SpriteRenderer).MakeArrayType(1);
            Assert.That(
                TryGetFieldKind(fieldType, out FieldKind kind, out Type elementType),
                Is.False
            );
            Assert.That(kind, Is.EqualTo(default(FieldKind)));
            Assert.That(elementType, Is.Null);
        }

        [Test]
        public void PrewarmReportsUnsupportedFieldsWithoutBindingThem(
            [Values(false, true)] bool precomputed,
            [Values(0, 1, 2)] int capability
        )
        {
            using IDisposable scope = new ReflectionCapabilityScope(
                capability == 0,
                capability == 1
            );
            ClearCaches();
            AttributeMetadataCache cache = CreateCache(precomputed);
            Lazy<AttributeMetadataCache> previous = AttributeMetadataCache._lazyInstance;
            AttributeMetadataCache._lazyInstance = new Lazy<AttributeMetadataCache>(() => cache);
            try
            {
                for (int warning = 0; warning < 21; ++warning)
                {
                    LogAssert.Expect(
                        LogType.Warning,
                        new Regex("Skipping unsupported relational field")
                    );
                }
                RelationalComponentInitializer.Report report =
                    RelationalComponentInitializer.Initialize(
                        new[] { typeof(RelationalUnsupportedArrayTarget) },
                        logSummary: false
                    );
                Assert.That(report.Errors, Is.Zero);
                Assert.That(report.Warnings, Is.EqualTo(precomputed ? 21 : 7));
                Assert.That(report.FieldsWarmed, Is.EqualTo(precomputed ? 3 : 1));
                Assert.That(report.TypesVisited, Is.EqualTo(1));
                Assert.That(report.TypesWarmed, Is.EqualTo(1));
            }
            finally
            {
                AttributeMetadataCache._lazyInstance = previous;
            }
        }

        private AttributeMetadataCache CreateCache(bool precomputed)
        {
            AttributeMetadataCache cache = CreateScriptableObject<AttributeMetadataCache>();
            if (precomputed)
            {
                string[] names =
                {
                    nameof(RelationalUnsupportedArrayTarget.rectangle),
                    nameof(RelationalUnsupportedArrayTarget.cube),
                    nameof(RelationalUnsupportedArrayTarget.jagged),
                    nameof(RelationalUnsupportedArrayTarget.number),
                    nameof(RelationalUnsupportedArrayTarget.text),
                    nameof(RelationalUnsupportedArrayTarget.numbers),
                    nameof(RelationalUnsupportedArrayTarget.numberSet),
                    nameof(RelationalUnsupportedArrayTarget.valid),
                };
                AttributeMetadataCache.RelationalAttributeKind[] relations =
                {
                    AttributeMetadataCache.RelationalAttributeKind.Parent,
                    AttributeMetadataCache.RelationalAttributeKind.Sibling,
                    AttributeMetadataCache.RelationalAttributeKind.Child,
                };
                List<AttributeMetadataCache.RelationalFieldMetadata> fields = new();
                foreach (string name in names)
                {
                    foreach (AttributeMetadataCache.RelationalAttributeKind relation in relations)
                    {
                        fields.Add(
                            new AttributeMetadataCache.RelationalFieldMetadata(
                                name,
                                relation,
                                AttributeMetadataCache.FieldKind.Array,
                                typeof(SpriteRenderer).AssemblyQualifiedName,
                                false
                            )
                        );
                    }
                }
                cache._relationalTypeMetadata = new[]
                {
                    new AttributeMetadataCache.RelationalTypeMetadata(
                        typeof(RelationalUnsupportedArrayTarget).AssemblyQualifiedName,
                        fields.ToArray()
                    ),
                };
            }
            AttributeMetadataCacheTestUtilities.Rebuild(cache);
            return cache;
        }

        private void VerifyUnsupportedFields(int relation, int capability, bool precomputed)
        {
            using IDisposable scope = new ReflectionCapabilityScope(
                capability == 0,
                capability == 1
            );
            ClearCaches();
            GameObject owner = Track(
                new GameObject(
                    "ArrayOwner",
                    typeof(SpriteRenderer),
                    typeof(RelationalUnsupportedArrayTarget)
                )
            );
            RelationalUnsupportedArrayTarget target =
                owner.GetComponent<RelationalUnsupportedArrayTarget>();
            SpriteRenderer[,] rectangle = target.rectangle;
            SpriteRenderer[,,] cube = target.cube;
            SpriteRenderer[][] jagged = target.jagged;
            AttributeMetadataCache cache = CreateCache(precomputed);
            Lazy<AttributeMetadataCache> previous = AttributeMetadataCache._lazyInstance;
            AttributeMetadataCache._lazyInstance = new Lazy<AttributeMetadataCache>(() => cache);
            try
            {
                for (int warning = 0; warning < 7; ++warning)
                {
                    LogAssert.Expect(
                        LogType.Warning,
                        new Regex("Skipping unsupported relational field")
                    );
                }
                for (int pass = 0; pass < 2; ++pass)
                {
                    if (relation == 0)
                    {
                        Assert.DoesNotThrow(() => target.AssignParentComponents());
                    }
                    else if (relation == 1)
                    {
                        Assert.DoesNotThrow(() => target.AssignSiblingComponents());
                    }
                    else
                    {
                        Assert.DoesNotThrow(() => target.AssignChildComponents());
                    }
                    Assert.AreSame(rectangle, target.rectangle);
                    Assert.AreSame(cube, target.cube);
                    Assert.AreSame(jagged, target.jagged);
                    Assert.That(target.number, Is.EqualTo(37));
                    Assert.That(target.text, Is.EqualTo("Authored"));
                    CollectionAssert.AreEqual(new[] { 37 }, target.numbers);
                    CollectionAssert.AreEquivalent(new[] { 37 }, target.numberSet);
                    Assert.AreSame(owner.GetComponent<SpriteRenderer>(), target.valid);
                }
            }
            finally
            {
                AttributeMetadataCache._lazyInstance = previous;
            }
        }
    }
}
