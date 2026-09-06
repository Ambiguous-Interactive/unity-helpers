// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using UnityEngine.UI;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class MatchColliderToSpriteAdditionalTests : CommonTestBase
    {
        private Sprite _spriteWithNoShapes;

        [TestCase("1001")]
        [TestCase("0110")]
        public void ExactArtDiagonalOpaquePixelsMatchNativeCollider(string input)
        {
            Texture2D texture = Track(new Texture2D(2, 2, TextureFormat.RGBA32, false));
            Color32[] pixels = new Color32[4];
            for (int index = 0; index < input.Length; ++index)
            {
                pixels[index] = new Color32(
                    255,
                    255,
                    255,
                    input[index] == '1' ? (byte)255 : (byte)0
                );
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero, 1));
            GameObject gameObject = Track(
                new GameObject("Diagonal sprite", typeof(PolygonCollider2D), typeof(SpriteRenderer))
            );
            gameObject.GetComponent<SpriteRenderer>().sprite = sprite;
            MatchColliderToSprite matcher = gameObject.AddComponent<MatchColliderToSprite>();
            matcher.traceExactly = true;
            matcher.RebuildCollider();
            Physics2D.SyncTransforms();
            Assert.AreEqual(1, matcher.polygonCollider.pathCount);
            for (int index = 0; index < input.Length; ++index)
            {
                Assert.AreEqual(
                    input[index] == '1',
                    matcher.polygonCollider.OverlapPoint(
                        new Vector2(index % 2 + 0.5f, index / 2 + 0.5f)
                    ),
                    $"Pixel {index}"
                );
            }
        }

        [UnityTest]
        public IEnumerator DisabledMatcherPreservesBakedColliderOnStartup()
        {
            GameObject gameObject = Track(
                new GameObject("Baked sprite", typeof(PolygonCollider2D), typeof(SpriteRenderer))
            );
            gameObject.SetActive(false);
            MatchColliderToSprite matcher = gameObject.AddComponent<MatchColliderToSprite>();
            matcher.enabled = false;
            matcher.traceExactly = true;
            PolygonCollider2D collider = gameObject.GetComponent<PolygonCollider2D>();
            Vector2[] baked = { Vector2.zero, Vector2.right, Vector2.up };
            collider.pathCount = 1;
            collider.SetPath(0, baked);
            gameObject.SetActive(true);
            yield return null;
            CollectionAssert.AreEqual(baked, collider.GetPath(0));
        }

        [Test]
        public void ExactArtIsOptInAndDoesNotExpandOneOpaquePixel()
        {
            Texture2D texture = Track(new Texture2D(4, 4, TextureFormat.RGBA32, false));
            Color32[] pixels = new Color32[16];
            pixels[5] = new Color32(255, 255, 255, 128);
            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero, 1));
            Vector2[] generated = { Vector2.zero, new(4, 0), new(4, 4), new(0, 4) };
            sprite.OverridePhysicsShape(new[] { generated });
            GameObject gameObject = Track(
                new GameObject("Exact sprite", typeof(PolygonCollider2D), typeof(SpriteRenderer))
            );
            gameObject.GetComponent<SpriteRenderer>().sprite = sprite;
            MatchColliderToSprite matcher = gameObject.AddComponent<MatchColliderToSprite>();
            Assert.IsFalse(matcher.traceExactly);
            matcher.RebuildCollider();
            CollectionAssert.AreEqual(generated, matcher.polygonCollider.GetPath(0));
            matcher.traceExactly = true;
            matcher.SendMessage("Update");
            CollectionAssert.AreEquivalent(
                new[]
                {
                    new Vector2(1, 1),
                    new Vector2(2, 1),
                    new Vector2(2, 2),
                    new Vector2(1, 2),
                },
                matcher.polygonCollider.GetPath(0)
            );
            matcher.alphaThreshold = 129;
            matcher.SendMessage("Update");
            Assert.AreEqual(0, matcher.polygonCollider.pathCount);
            matcher.alphaThreshold = 128;
            matcher.SendMessage("Update");
            Assert.AreEqual(1, matcher.polygonCollider.pathCount);
            matcher.minimumTracedArea = 2;
            matcher.SendMessage("Update");
            Assert.AreEqual(0, matcher.polygonCollider.pathCount);
            matcher.traceExactly = false;
            matcher.SendMessage("Update");
            CollectionAssert.AreEqual(generated, matcher.polygonCollider.GetPath(0));
        }

        [Test]
        public void ExactArtUnreadableFailurePreservesColliderAndLogsOnce()
        {
            GameObject gameObject = Track(
                new GameObject(
                    "Unreadable sprite",
                    typeof(PolygonCollider2D),
                    typeof(SpriteRenderer)
                )
            );
            Texture2D texture = Track(new Texture2D(2, 2));
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero));
            texture.Apply(false, true);
            gameObject.GetComponent<SpriteRenderer>().sprite = sprite;
            MatchColliderToSprite matcher = gameObject.AddComponent<MatchColliderToSprite>();
            Vector2[] previous = { Vector2.zero, Vector2.right, Vector2.up };
            matcher.polygonCollider.pathCount = 1;
            matcher.polygonCollider.SetPath(0, previous);
            matcher.traceExactly = true;
            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    "Exact sprite art needs.*existing collider was preserved"
                )
            );
            matcher.RebuildCollider();
            CollectionAssert.AreEqual(previous, matcher.polygonCollider.GetPath(0));
            matcher.SendMessage("Update");
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExactArtKeepsHoleCentersOutsideNativeCollider(bool diagonalOpening)
        {
            Texture2D texture = Track(new Texture2D(3, 3, TextureFormat.RGBA32, false));
            Color32[] pixels = new Color32[9];
            System.Array.Fill(pixels, new Color32(255, 255, 255, 255));
            pixels[4] = default;
            if (diagonalOpening)
            {
                pixels[8] = default;
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite sprite = Track(Sprite.Create(texture, new Rect(0, 0, 3, 3), Vector2.zero, 1));
            GameObject gameObject = Track(
                new GameObject("Sprite hole", typeof(PolygonCollider2D), typeof(SpriteRenderer))
            );
            gameObject.GetComponent<SpriteRenderer>().sprite = sprite;
            MatchColliderToSprite matcher = gameObject.AddComponent<MatchColliderToSprite>();
            matcher.traceExactly = true;
            matcher.RebuildCollider();
            Physics2D.SyncTransforms();
            Assert.AreEqual(2, matcher.polygonCollider.pathCount);
            Assert.IsTrue(matcher.polygonCollider.OverlapPoint(new Vector2(0.5f, 0.5f)));
            Assert.IsFalse(matcher.polygonCollider.OverlapPoint(new Vector2(1.5f, 1.5f)));
            Assert.AreEqual(
                !diagonalOpening,
                matcher.polygonCollider.OverlapPoint(new Vector2(2.5f, 2.5f))
            );
        }

        [SetUp]
        public override void BaseSetUp()
        {
            base.BaseSetUp();
            Texture2D tex = Track(new Texture2D(8, 8));
            _spriteWithNoShapes = Track(
                Sprite.Create(tex, new Rect(0, 0, 8, 8), new Vector2(0.5f, 0.5f), 100f)
            );
        }

        [UnityTest]
        public IEnumerator OverrideProducerNullWinsOverComponents()
        {
            GameObject go = Track(
                new GameObject(
                    "Test",
                    typeof(PolygonCollider2D),
                    typeof(SpriteRenderer),
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(MatchColliderToSprite)
                )
            );
            SpriteRenderer renderer = go.GetComponent<SpriteRenderer>();
            Image image = go.GetComponent<Image>();
            renderer.sprite = _spriteWithNoShapes;
            image.sprite = _spriteWithNoShapes;

            MatchColliderToSprite matcher = go.GetComponent<MatchColliderToSprite>();
            matcher.spriteOverrideProducer = () => null;

            matcher.RebuildCollider();
            yield return null;

            Assert.IsTrue(matcher._lastHandled == null);
            Assert.AreEqual(0, go.GetComponent<PolygonCollider2D>().pathCount);
        }

        [UnityTest]
        public IEnumerator SpriteWithNoPhysicsShapesClearsCollider()
        {
            GameObject go = Track(
                new GameObject(
                    "Test",
                    typeof(PolygonCollider2D),
                    typeof(SpriteRenderer),
                    typeof(MatchColliderToSprite)
                )
            );
            go.GetComponent<SpriteRenderer>().sprite = _spriteWithNoShapes;
            MatchColliderToSprite matcher = go.GetComponent<MatchColliderToSprite>();

            matcher.RebuildCollider();
            yield return null;

            Assert.AreEqual(0, go.GetComponent<PolygonCollider2D>().pathCount);
        }
    }
}
