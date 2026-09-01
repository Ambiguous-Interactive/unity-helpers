// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Tags
{
    using System;
    using NUnit.Framework;
    using UnityEngine;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Tests.Editor.TestTypes;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class AttributeMetadataCacheTests : CommonTestBase
    {
        [Test]
        public void SetMetadataSortsSerializedContent()
        {
            AttributeMetadataCache cache = Track(
                ScriptableObject.CreateInstance<AttributeMetadataCache>()
            );

            string[] attributeNames = new[] { "Gamma", "Alpha", "Beta" };

            string alphaAttributesTypeName =
                typeof(AlphaAttributesComponent).AssemblyQualifiedName ?? string.Empty;
            string bravoAttributesTypeName =
                typeof(BravoAttributesComponent).AssemblyQualifiedName ?? string.Empty;

            AttributeMetadataCache.TypeFieldMetadata alphaTypeMetadata = new(
                alphaAttributesTypeName,
                new[] { "gammaField", "betaField" }
            );

            AttributeMetadataCache.TypeFieldMetadata bravoTypeMetadata = new(
                bravoAttributesTypeName,
                new[] { "zetaField", "alphaField" }
            );

            string alphaRelationalTypeName =
                typeof(AlphaRelationalComponent).AssemblyQualifiedName ?? string.Empty;
            string bravoRelationalTypeName =
                typeof(BravoRelationalComponent).AssemblyQualifiedName ?? string.Empty;

            AttributeMetadataCache.RelationalFieldMetadata[] alphaRelationalFields =
                new AttributeMetadataCache.RelationalFieldMetadata[]
                {
                    new(
                        "betaRelation",
                        AttributeMetadataCache.RelationalAttributeKind.Parent,
                        AttributeMetadataCache.FieldKind.HashSet,
                        typeof(Light).AssemblyQualifiedName ?? string.Empty,
                        true
                    ),
                    new(
                        "alphaRelation",
                        AttributeMetadataCache.RelationalAttributeKind.Child,
                        AttributeMetadataCache.FieldKind.Single,
                        typeof(Transform).AssemblyQualifiedName ?? string.Empty,
                        false
                    ),
                };

            AttributeMetadataCache.RelationalFieldMetadata[] bravoRelationalFields =
                new AttributeMetadataCache.RelationalFieldMetadata[]
                {
                    new(
                        "zetaRelation",
                        AttributeMetadataCache.RelationalAttributeKind.Sibling,
                        AttributeMetadataCache.FieldKind.List,
                        typeof(Camera).AssemblyQualifiedName ?? string.Empty,
                        true
                    ),
                    new(
                        "alphaRelation",
                        AttributeMetadataCache.RelationalAttributeKind.Child,
                        AttributeMetadataCache.FieldKind.Single,
                        typeof(Transform).AssemblyQualifiedName ?? string.Empty,
                        false
                    ),
                };

            AttributeMetadataCache.RelationalTypeMetadata[] relationalMetadata =
                new AttributeMetadataCache.RelationalTypeMetadata[]
                {
                    new(
                        bravoRelationalTypeName,
                        new[] { bravoRelationalFields[0], null, bravoRelationalFields[1] }
                    ),
                    null,
                    new(alphaRelationalTypeName, alphaRelationalFields),
                };

            AttributeMetadataCache.TypeFieldMetadata[] typeMetadata = new[]
            {
                bravoTypeMetadata,
                null,
                alphaTypeMetadata,
            };

            bool changed = cache.SetMetadata(
                attributeNames,
                typeMetadata,
                relationalMetadata,
                Array.Empty<AttributeMetadataCache.AutoLoadSingletonEntry>()
            );

            Assert.IsTrue(changed);

            string[] storedAttributeNames = cache.SerializedAttributeNames;
            Assert.That(storedAttributeNames, Is.EqualTo(new[] { "Alpha", "Beta", "Gamma" }));

            AttributeMetadataCache.TypeFieldMetadata[] storedTypeMetadata =
                cache.SerializedTypeMetadata;
            Assert.That(storedTypeMetadata.Length, Is.EqualTo(2));
            Assert.That(storedTypeMetadata[0].typeName, Is.EqualTo(alphaAttributesTypeName));
            Assert.That(
                storedTypeMetadata[0].fieldNames,
                Is.EqualTo(new[] { "betaField", "gammaField" })
            );
            Assert.That(storedTypeMetadata[1].typeName, Is.EqualTo(bravoAttributesTypeName));
            Assert.That(
                storedTypeMetadata[1].fieldNames,
                Is.EqualTo(new[] { "alphaField", "zetaField" })
            );

            AttributeMetadataCache.RelationalTypeMetadata[] storedRelationalMetadata =
                cache.SerializedRelationalTypeMetadata;
            Assert.That(storedRelationalMetadata.Length, Is.EqualTo(2));
            Assert.That(storedRelationalMetadata[0].typeName, Is.EqualTo(alphaRelationalTypeName));
            Assert.That(storedRelationalMetadata[0].fields.Length, Is.EqualTo(2));
            Assert.That(
                storedRelationalMetadata[0].fields[0].fieldName,
                Is.EqualTo("alphaRelation")
            );
            Assert.That(
                storedRelationalMetadata[0].fields[1].fieldName,
                Is.EqualTo("betaRelation")
            );
            Assert.That(storedRelationalMetadata[1].typeName, Is.EqualTo(bravoRelationalTypeName));
            Assert.That(storedRelationalMetadata[1].fields.Length, Is.EqualTo(2));
            Assert.That(
                storedRelationalMetadata[1].fields[0].fieldName,
                Is.EqualTo("alphaRelation")
            );
            Assert.That(
                storedRelationalMetadata[1].fields[1].fieldName,
                Is.EqualTo("zetaRelation")
            );
        }

        [Test]
        public void SetMetadataReturnsFalseWhenNormalizedContentIsUnchanged()
        {
            AttributeMetadataCache cache = Track(
                ScriptableObject.CreateInstance<AttributeMetadataCache>()
            );

            string alphaAttributesTypeName =
                typeof(AlphaAttributesComponent).AssemblyQualifiedName ?? string.Empty;
            AttributeMetadataCache.TypeFieldMetadata alphaTypeMetadata = new(
                alphaAttributesTypeName,
                new[] { "zetaField", "alphaField" }
            );

            AttributeMetadataCache.RelationalFieldMetadata relationalField = new(
                "target",
                AttributeMetadataCache.RelationalAttributeKind.Child,
                AttributeMetadataCache.FieldKind.Single,
                typeof(Transform).AssemblyQualifiedName ?? string.Empty,
                false
            );

            AttributeMetadataCache.RelationalTypeMetadata relationalMetadata = new(
                typeof(AlphaRelationalComponent).AssemblyQualifiedName ?? string.Empty,
                new[] { relationalField }
            );

            AttributeMetadataCache.AutoLoadSingletonEntry autoLoad = new(
                typeof(AttributeMetadataCache).AssemblyQualifiedName ?? string.Empty,
                SingletonAutoLoadKind.ScriptableObject,
                RuntimeInitializeLoadType.AfterSceneLoad
            );

            bool firstChanged = cache.SetMetadata(
                new[] { "Bravo", "Alpha" },
                new[] { alphaTypeMetadata },
                new[] { relationalMetadata },
                new[] { autoLoad }
            );
            bool secondChanged = cache.SetMetadata(
                new[] { "Alpha", "Bravo" },
                new[]
                {
                    new AttributeMetadataCache.TypeFieldMetadata(
                        alphaAttributesTypeName,
                        new[] { "alphaField", "zetaField" }
                    ),
                },
                new[]
                {
                    new AttributeMetadataCache.RelationalTypeMetadata(
                        typeof(AlphaRelationalComponent).AssemblyQualifiedName ?? string.Empty,
                        new[]
                        {
                            new AttributeMetadataCache.RelationalFieldMetadata(
                                "target",
                                AttributeMetadataCache.RelationalAttributeKind.Child,
                                AttributeMetadataCache.FieldKind.Single,
                                typeof(Transform).AssemblyQualifiedName ?? string.Empty,
                                false
                            ),
                        }
                    ),
                },
                new[]
                {
                    new AttributeMetadataCache.AutoLoadSingletonEntry(
                        typeof(AttributeMetadataCache).AssemblyQualifiedName ?? string.Empty,
                        SingletonAutoLoadKind.ScriptableObject,
                        RuntimeInitializeLoadType.AfterSceneLoad
                    ),
                }
            );

            Assert.IsTrue(firstChanged);
            Assert.IsFalse(secondChanged);
        }
    }
}
