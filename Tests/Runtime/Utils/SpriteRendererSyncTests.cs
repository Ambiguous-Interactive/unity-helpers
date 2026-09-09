// MIT License - Copyright (c) 2025 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Utils
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Tests.Core;
    using WallstopStudios.UnityHelpers.Utils;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class SpriteRendererSyncTests : CommonTestBase
    {
        [UnityTest]
        public IEnumerator MirrorsPropertiesFromTarget()
        {
            GameObject targetGo = Track(new GameObject("Target", typeof(SpriteRenderer)));
            GameObject followerGo = Track(
                new GameObject("Follower", typeof(SpriteRenderer), typeof(SpriteRendererSync))
            );
            SpriteRenderer target = targetGo.GetComponent<SpriteRenderer>();
            SpriteRenderer follower = followerGo.GetComponent<SpriteRenderer>();
            SpriteRendererSync sync = followerGo.GetComponent<SpriteRendererSync>();

            target.sprite = CreateSprite();
            target.enabled = true;
            target.flipX = true;
            target.flipY = false;
            target.color = new Color(0.1f, 0.2f, 0.3f, 0.4f);
            target.sortingLayerName = "Default";
            target.sortingOrder = 5;

            sync.toMatch = target;
            sync.matchColor = true;
            sync.matchMaterial = false;
            sync.matchSortingLayer = true;
            sync.matchOrderInLayer = true;

            sync.SendMessage("Awake");
            sync.SendMessage("LateUpdate");
            yield return null;

            Assert.AreEqual(target.sprite, follower.sprite);
            Assert.AreEqual(target.enabled, follower.enabled);
            Assert.AreEqual(target.flipX, follower.flipX);
            Assert.AreEqual(target.flipY, follower.flipY);
            Assert.AreEqual(target.color, follower.color);
            Assert.AreEqual(target.sortingLayerName, follower.sortingLayerName);
            Assert.AreEqual(target.sortingOrder, follower.sortingOrder);
        }

        [UnityTest]
        public IEnumerator NullTargetClearsSprite()
        {
            GameObject followerGo = Track(
                new GameObject("Follower", typeof(SpriteRenderer), typeof(SpriteRendererSync))
            );
            SpriteRenderer follower = followerGo.GetComponent<SpriteRenderer>();
            SpriteRendererSync sync = followerGo.GetComponent<SpriteRendererSync>();

            follower.sprite = CreateSprite();
            sync.toMatch = null;

            sync.SendMessage("Awake");
            sync.SendMessage("LateUpdate");
            yield return null;

            Assert.IsTrue(follower.sprite == null);
        }

        [UnityTest]
        public IEnumerator DynamicToMatchOverridesAndCaches()
        {
            GameObject aGo = Track(new GameObject("A", typeof(SpriteRenderer)));
            GameObject bGo = Track(new GameObject("B", typeof(SpriteRenderer)));
            GameObject followerGo = Track(
                new GameObject("Follower", typeof(SpriteRenderer), typeof(SpriteRendererSync))
            );
            SpriteRenderer a = aGo.GetComponent<SpriteRenderer>();
            SpriteRenderer b = bGo.GetComponent<SpriteRenderer>();
            a.sprite = CreateSprite(2, 2);
            b.sprite = CreateSprite(3, 3);

            SpriteRendererSync sync = followerGo.GetComponent<SpriteRendererSync>();
            sync.dynamicToMatch = () => a;

            sync.SendMessage("Awake");
            sync.SendMessage("LateUpdate");
            yield return null;
            Assert.AreEqual(a.sprite, followerGo.GetComponent<SpriteRenderer>().sprite);

            aGo.SetActive(false);
            sync.dynamicToMatch = () => b;
            sync.SendMessage("LateUpdate");
            yield return null;
            Assert.AreEqual(b.sprite, followerGo.GetComponent<SpriteRenderer>().sprite);
        }

        [UnityTest]
        public IEnumerator SortingOrderCanBeOverridden()
        {
            GameObject targetGo = Track(new GameObject("Target", typeof(SpriteRenderer)));
            GameObject followerGo = Track(
                new GameObject("Follower", typeof(SpriteRenderer), typeof(SpriteRendererSync))
            );
            SpriteRenderer target = targetGo.GetComponent<SpriteRenderer>();
            SpriteRenderer follower = followerGo.GetComponent<SpriteRenderer>();
            SpriteRendererSync sync = followerGo.GetComponent<SpriteRendererSync>();

            target.sprite = CreateSprite();
            target.sortingOrder = 2;
            sync.toMatch = target;
            sync.DynamicSortingOrderOverride = 10;

            sync.SendMessage("Awake");
            sync.SendMessage("LateUpdate");
            yield return null;

            Assert.AreEqual(10, follower.sortingOrder);
        }

        private Texture2D CreateTexture(int w, int h)
        {
            Texture2D t = Track(new Texture2D(w, h, TextureFormat.RGBA32, false, false));
            t.SetPixels(new Color[w * h]);
            t.Apply(false, false);
            return t;
        }

        private Sprite CreateSprite(int w = 4, int h = 4)
        {
            Texture2D t = CreateTexture(w, h);
            return Track(Sprite.Create(t, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f));
        }
    }
}
