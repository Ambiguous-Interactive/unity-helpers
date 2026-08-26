// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

#if VCONTAINER_PRESENT
namespace WallstopStudios.UnityHelpers.Tests.Integrations.VContainer
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Attributes;
    using WallstopStudios.UnityHelpers.Integrations.VContainer;
    using WallstopStudios.UnityHelpers.Tags;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Slow")]
    [NUnit.Framework.Category("Integration")]
    public sealed class VContainerRelationalEntryPointTests : CommonTestBase
    {
        [UnityTest]
        public IEnumerator EntryPointAssignsSiblingOnActiveScene()
        {
            CreateTempScene("VContainerTestScene");

            GameObject go = NewGameObject("Root");
            go.AddComponent<SpriteRenderer>();
            Consumer consumer = go.AddComponent<Consumer>();

            // Give Unity a frame to register objects
            yield return null;

            AttributeMetadataCache cache = CreateScriptableObject<AttributeMetadataCache>();
#if UNITY_EDITOR
            AttributeMetadataCache.RelationalTypeMetadata relationalMetadata = new(
                typeof(Consumer).AssemblyQualifiedName,
                new[]
                {
                    new AttributeMetadataCache.RelationalFieldMetadata(
                        nameof(Consumer._spriteRenderer),
                        AttributeMetadataCache.RelationalAttributeKind.Sibling,
                        AttributeMetadataCache.FieldKind.Single,
                        typeof(SpriteRenderer).AssemblyQualifiedName,
                        false
                    ),
                }
            );

            cache.SetMetadata(
                System.Array.Empty<string>(),
                System.Array.Empty<AttributeMetadataCache.TypeFieldMetadata>(),
                new[] { relationalMetadata },
                System.Array.Empty<AttributeMetadataCache.AutoLoadSingletonEntry>()
            );
            cache.ForceRebuildForTests();
            yield return null;
#endif

            RelationalComponentAssigner assigner = new(cache);
            RelationalComponentEntryPoint entry = new(
                assigner,
                cache,
                RelationalSceneAssignmentOptions.Default
            );

            entry.Initialize();

            // Allow assignment to complete
            yield return null;

            Assert.IsTrue(consumer != null);
            Assert.IsTrue(
                consumer.SR != null,
                "Relational field should be assigned by entry point"
            );
        }

        private sealed class Consumer : MonoBehaviour
        {
            [SiblingComponent]
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
            internal SpriteRenderer _spriteRenderer;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value

            public SpriteRenderer SR => _spriteRenderer;
        }
    }
}
#endif
