// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections;
    using System.Linq;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Random;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpriteRendererMetadataTests : CommonTestBase
    {
        private SpriteRendererMetadata CreateMetadata()
        {
            GameObject go = Track(
                new GameObject(
                    "TestSpriteRendererMetadata",
                    typeof(SpriteRenderer),
                    typeof(SpriteRendererMetadata)
                )
            );
            return go.GetComponent<SpriteRendererMetadata>();
        }

        private Material CreateMaterial()
        {
            return Track(new Material(Shader.Find("Sprites/Default")));
        }

        private Color CreateColor()
        {
            IRandom random = PRNG.Instance;
            Color color = new(
                random.NextFloat(),
                random.NextFloat(),
                random.NextFloat(),
                random.NextFloat()
            );
            return color;
        }

        [UnityTest]
        public IEnumerator Initialization()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(spriteRenderer.material, metadata.CurrentMaterial);
            Assert.AreEqual(metadata.OriginalColor, metadata.CurrentColor);
            Assert.AreEqual(metadata.OriginalMaterial, metadata.CurrentMaterial);
            Assert.AreEqual(1, metadata.Colors.Count());
            Assert.IsTrue(metadata.Colors.Contains(metadata.OriginalColor));
            Assert.AreEqual(1, metadata.Materials.Count());
            Assert.IsTrue(metadata.Materials.Contains(metadata.OriginalMaterial));
            yield break;
        }

        [UnityTest]
        public IEnumerator PushColor()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            Color originalColor = metadata.OriginalColor;
            Color newColor = originalColor;
            Material originalMaterial = metadata.OriginalMaterial;
            do
            {
                newColor.r = PRNG.Instance.NextFloat();
            } while (newColor == originalColor);

            SpriteRendererMetadata second = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            metadata.PushColor(second, newColor);
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(newColor, metadata.CurrentColor);
            Assert.IsTrue(metadata.Colors.Contains(newColor));
            Assert.AreEqual(originalColor, metadata.OriginalColor);
            Assert.AreEqual(originalMaterial, metadata.CurrentMaterial);

            Color updatedColor = newColor;
            do
            {
                updatedColor.g = PRNG.Instance.NextFloat();
            } while (updatedColor == newColor);

            metadata.PushColor(spriteRenderer, updatedColor);
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(updatedColor, metadata.CurrentColor);
            Assert.AreEqual(3, metadata.Colors.Count());
            Assert.IsTrue(metadata.Colors.Contains(originalColor));
            Assert.IsTrue(metadata.Colors.Contains(newColor));
            Assert.IsTrue(metadata.Colors.Contains(updatedColor));
            Assert.AreEqual(originalColor, metadata.OriginalColor);
            Assert.AreEqual(originalMaterial, metadata.CurrentMaterial);

            Color latestColor = updatedColor;
            do
            {
                latestColor.b = PRNG.Instance.NextFloat();
            } while (latestColor == updatedColor);

            metadata.PushColor(second, latestColor);
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(latestColor, metadata.CurrentColor);
            Assert.AreEqual(3, metadata.Colors.Count());

            Assert.IsFalse(metadata.Colors.Contains(newColor));
            do
            {
                newColor.a = PRNG.Instance.NextFloat();
            } while (newColor == latestColor);

            metadata.PushColor(second, newColor);
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(newColor, metadata.CurrentColor);
            Assert.AreEqual(3, metadata.Colors.Count());
            Assert.IsTrue(metadata.Colors.Contains(originalColor));
            Assert.IsTrue(metadata.Colors.Contains(newColor));
            Assert.IsTrue(metadata.Colors.Contains(updatedColor));

            metadata.PushColor(second, newColor);
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(newColor, metadata.CurrentColor);
            Assert.AreEqual(3, metadata.Colors.Count());
            Assert.IsTrue(metadata.Colors.Contains(originalColor));
            Assert.IsTrue(metadata.Colors.Contains(newColor));
            Assert.IsTrue(metadata.Colors.Contains(updatedColor));
            yield break;
        }

        [UnityTest]
        public IEnumerator PushColorIdempotent()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Color color = CreateColor();

            for (int i = 0; i < 100; ++i)
            {
                Verify();
            }

            yield break;

            void Verify()
            {
                metadata.PushColor(spriteRenderer, color);
                Assert.AreEqual(spriteRenderer.color, color);
                Assert.AreEqual(color, metadata.CurrentColor);
                Assert.AreEqual(2, metadata.Colors.Count());
            }
        }

        [UnityTest]
        public IEnumerator PushMaterialIdempotent()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Material material = CreateMaterial();

            for (int i = 0; i < 100; ++i)
            {
                Verify();
            }

            yield break;

            void Verify()
            {
                metadata.PushMaterial(spriteRenderer, material);
                Assert.AreEqual(spriteRenderer.material, metadata.CurrentMaterial);
                Assert.AreEqual(2, metadata.Materials.Count());
                Assert.IsTrue(metadata.Materials.Contains(spriteRenderer.material));
                Assert.IsTrue(metadata.Materials.Contains(metadata.OriginalMaterial));
            }
        }

        [UnityTest]
        public IEnumerator PopColorIdempotent()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Color originalColor = spriteRenderer.color;
            Color color = CreateColor();
            metadata.PushColor(spriteRenderer, color);

            for (int i = 0; i < 100; ++i)
            {
                Verify();
            }

            yield break;

            void Verify()
            {
                metadata.PopColor(spriteRenderer);
                Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
                Assert.AreEqual(originalColor, metadata.CurrentColor);
                Assert.AreEqual(1, metadata.Colors.Count());
            }
        }

        [UnityTest]
        public IEnumerator PopMaterialIdempotent()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Material material = CreateMaterial();

            metadata.PushMaterial(spriteRenderer, material);

            for (int i = 0; i < 100; ++i)
            {
                Verify();
            }

            yield break;

            void Verify()
            {
                metadata.PopMaterial(spriteRenderer);
                Assert.AreEqual(spriteRenderer.material, metadata.CurrentMaterial);
                Assert.AreEqual(1, metadata.Materials.Count());
                Assert.IsTrue(metadata.Materials.Contains(spriteRenderer.material));
            }
        }

        [UnityTest]
        public IEnumerator CannotSelfPushColor()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Color color = CreateColor();
            Color originalColor = metadata.OriginalColor;
            metadata.PushColor(metadata, color);
            Assert.AreEqual(originalColor, metadata.CurrentColor);
            Assert.AreEqual(originalColor, spriteRenderer.color);
            Assert.AreEqual(1, metadata.Colors.Count());
            yield break;
        }

        [UnityTest]
        public IEnumerator CannotSelfPopColor()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Color originalColor = metadata.OriginalColor;
            metadata.PopColor(metadata);
            Assert.AreEqual(originalColor, metadata.CurrentColor);
            Assert.AreEqual(originalColor, spriteRenderer.color);
            Assert.AreEqual(1, metadata.Colors.Count());
            yield break;
        }

        [UnityTest]
        public IEnumerator CannotSelfPopMaterial()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Material originalMaterial = metadata.OriginalMaterial;
            metadata.PopMaterial(metadata);
            Assert.AreEqual(originalMaterial, metadata.CurrentMaterial);
            Assert.AreEqual(originalMaterial, spriteRenderer.material);
            Assert.AreEqual(1, metadata.Materials.Count());
            yield break;
        }

        [UnityTest]
        public IEnumerator CannotSelfPushMaterial()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Material material = CreateMaterial();
            Material originalMaterial = metadata.OriginalMaterial;
            metadata.PushMaterial(metadata, material);
            Assert.AreEqual(originalMaterial, metadata.CurrentMaterial);
            Assert.AreEqual(originalMaterial, spriteRenderer.material);
            Assert.AreNotEqual(material, spriteRenderer.material);
            Assert.AreEqual(1, metadata.Materials.Count());
            yield break;
        }

        [UnityTest]
        public IEnumerator EnableDisableColor()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Color color = CreateColor();
            metadata.PushColor(spriteRenderer, color);
            Assert.AreEqual(color, metadata.CurrentColor);
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(2, metadata.Colors.Count());

            metadata.enabled = false;
            Assert.AreEqual(metadata.OriginalColor, metadata.CurrentColor);
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(1, metadata.Colors.Count());

            metadata.enabled = true;
            Assert.AreEqual(color, metadata.CurrentColor);
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(2, metadata.Colors.Count());
            yield break;
        }

        [UnityTest]
        public IEnumerator EnableDisableMaterial()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Material material = CreateMaterial();
            metadata.PushMaterial(spriteRenderer, material);
            Assert.AreEqual(spriteRenderer.material, metadata.CurrentMaterial);
            Assert.AreEqual(2, metadata.Materials.Count());

            metadata.enabled = false;
            Assert.AreEqual(metadata.OriginalMaterial, metadata.CurrentMaterial);
            Assert.AreEqual(1, metadata.Materials.Count());

            metadata.enabled = true;
            Assert.AreEqual(spriteRenderer.material, metadata.CurrentMaterial);
            Assert.AreEqual(2, metadata.Materials.Count());
            yield break;
        }

        [UnityTest]
        public IEnumerator EnableDisableWithRemoveColor()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Color color = CreateColor();
            metadata.PushColor(spriteRenderer, color);
            Assert.AreEqual(color, metadata.CurrentColor);
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(2, metadata.Colors.Count());

            metadata.enabled = false;
            Assert.AreEqual(metadata.OriginalColor, metadata.CurrentColor);
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(1, metadata.Colors.Count());

            metadata.PopColor(spriteRenderer);

            Assert.AreEqual(metadata.OriginalColor, metadata.CurrentColor);
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(1, metadata.Colors.Count());

            metadata.enabled = true;
            Assert.AreEqual(metadata.OriginalColor, metadata.CurrentColor);
            Assert.AreEqual(spriteRenderer.color, metadata.CurrentColor);
            Assert.AreEqual(1, metadata.Colors.Count());
            yield break;
        }

        [UnityTest]
        public IEnumerator EnableDisableWithRemoveMaterial()
        {
            SpriteRendererMetadata metadata = CreateMetadata();
            SpriteRenderer spriteRenderer = metadata.GetComponent<SpriteRenderer>();
            Material material = CreateMaterial();
            metadata.PushMaterial(spriteRenderer, material);
            Assert.AreEqual(spriteRenderer.material, metadata.CurrentMaterial);
            Assert.AreEqual(2, metadata.Materials.Count());

            metadata.enabled = false;
            Assert.AreEqual(metadata.OriginalMaterial, metadata.CurrentMaterial);
            Assert.AreEqual(1, metadata.Materials.Count());

            metadata.PopMaterial(spriteRenderer);

            Assert.AreEqual(metadata.OriginalMaterial, metadata.CurrentMaterial);
            Assert.AreEqual(1, metadata.Materials.Count());

            metadata.enabled = true;
            Assert.AreEqual(metadata.OriginalMaterial, metadata.CurrentMaterial);
            Assert.AreEqual(1, metadata.Materials.Count());
            yield break;
        }
    }
}
