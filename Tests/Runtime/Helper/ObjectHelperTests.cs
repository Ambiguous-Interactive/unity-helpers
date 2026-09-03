// MIT License - Copyright (c) 2024 wallstop
// Full license text: https://github.com/wallstop/unity-helpers/blob/main/LICENSE

namespace WallstopStudios.UnityHelpers.Tests.Helper
{
    using System.Collections;
    using NUnit.Framework;
    using UnityEngine;
    using UnityEngine.TestTools;
    using WallstopStudios.UnityHelpers.Core.Helper;
    using WallstopStudios.UnityHelpers.Tests.Core;

    [TestFixture]
    [NUnit.Framework.Category("Fast")]
    public sealed class ObjectHelperTests : CommonTestBase
    {
        [UnityTest]
        public IEnumerator SceneUnloadDropsTagLookupsWhoseObjectIsGone()
        {
            Helpers.ClearTagCache();
            try
            {
                GameObject alive = Track(
                    new GameObject("alive tag holder", typeof(ObjectHelperComponent))
                );
                GameObject doomed = new("doomed tag holder", typeof(ObjectHelperComponent));

                Helpers.SetInstance("alive tag", alive.GetComponent<ObjectHelperComponent>());
                Helpers.SetInstance("doomed tag", doomed.GetComponent<ObjectHelperComponent>());
                Assert.AreEqual(2, Helpers.TagCacheCount);

                Object.DestroyImmediate(doomed); // UNH-SUPPRESS: the destroyed entry is the subject
                Helpers.DropDestroyedTagCacheEntries(default);

                Assert.AreEqual(
                    1,
                    Helpers.TagCacheCount,
                    "A tag nobody looks up again roots the object it cached, so the unload has to "
                        + "let go of it."
                );
            }
            finally
            {
                Helpers.ClearTagCache();
            }

            yield break;
        }

        [UnityTest]
        public IEnumerator HasComponent()
        {
            GameObject go = Track(new GameObject("Test SpriteRenderer", typeof(SpriteRenderer)));
            SpriteRenderer spriteRenderer = go.GetComponent<SpriteRenderer>();

            Assert.IsTrue(go.HasComponent(typeof(SpriteRenderer)));
            Assert.IsTrue(go.HasComponent<SpriteRenderer>());
            Assert.IsTrue(spriteRenderer.HasComponent<SpriteRenderer>());
            Assert.IsTrue(spriteRenderer.HasComponent(typeof(SpriteRenderer)));

            Assert.IsFalse(go.HasComponent<LineRenderer>());
            Assert.IsFalse(go.HasComponent(typeof(LineRenderer)));
            Assert.IsFalse(spriteRenderer.HasComponent<LineRenderer>());
            Assert.IsFalse(spriteRenderer.HasComponent(typeof(LineRenderer)));

            Object obj = go;
            Assert.IsTrue(obj.HasComponent<SpriteRenderer>());
            Assert.IsTrue(obj.HasComponent(typeof(SpriteRenderer)));
            Assert.IsFalse(obj.HasComponent<LineRenderer>());
            Assert.IsFalse(obj.HasComponent(typeof(LineRenderer)));
            yield break;
        }

        [UnityTest]
        public IEnumerator EnableRendererRecursively()
        {
            GameObject one = New("1");
            GameObject two = New("2");
            two.transform.SetParent(one.transform);
            GameObject three = New("3");
            three.transform.SetParent(two.transform);
            GameObject four = New("4");
            four.transform.SetParent(three.transform);

            // Act
            two.transform.EnableRendererRecursively<SpriteRenderer>(false);
            Assert.IsTrue(one.GetComponent<SpriteRenderer>().enabled);
            Assert.IsFalse(two.GetComponent<SpriteRenderer>().enabled);
            Assert.IsFalse(three.GetComponent<SpriteRenderer>().enabled);
            Assert.IsFalse(four.GetComponent<SpriteRenderer>().enabled);

            Assert.IsTrue(one.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(two.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(three.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(four.GetComponent<CircleCollider2D>().enabled);

            // Act
            three.transform.EnableRendererRecursively<SpriteRenderer>(true);

            Assert.IsTrue(one.GetComponent<SpriteRenderer>().enabled);
            Assert.IsFalse(two.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(three.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(four.GetComponent<SpriteRenderer>().enabled);

            Assert.IsTrue(one.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(two.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(three.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(four.GetComponent<CircleCollider2D>().enabled);

            // Act
            one.transform.EnableRendererRecursively<SpriteRenderer>(true);

            Assert.IsTrue(one.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(two.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(three.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(four.GetComponent<SpriteRenderer>().enabled);

            Assert.IsTrue(one.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(two.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(three.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(four.GetComponent<CircleCollider2D>().enabled);

            // Act
            two.transform.EnableRendererRecursively<SpriteRenderer>(
                false,
                renderer => renderer.gameObject == three
            );

            Assert.IsTrue(one.GetComponent<SpriteRenderer>().enabled);
            Assert.IsFalse(two.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(three.GetComponent<SpriteRenderer>().enabled);
            Assert.IsFalse(four.GetComponent<SpriteRenderer>().enabled);

            Assert.IsTrue(one.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(two.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(three.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(four.GetComponent<CircleCollider2D>().enabled);

            // Act
            one.transform.EnableRendererRecursively<SpriteRenderer>(
                true,
                renderer => renderer.gameObject == four
            );

            Assert.IsTrue(one.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(two.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(three.GetComponent<SpriteRenderer>().enabled);
            Assert.IsFalse(four.GetComponent<SpriteRenderer>().enabled);

            Assert.IsTrue(one.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(two.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(three.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(four.GetComponent<CircleCollider2D>().enabled);

            yield break;

            GameObject New(string name)
            {
                return Track(
                    new GameObject(name, typeof(SpriteRenderer), typeof(CircleCollider2D))
                );
            }
        }

        [UnityTest]
        public IEnumerator EnableRecursively()
        {
            GameObject one = New("1");
            GameObject two = New("2");
            two.transform.SetParent(one.transform);
            GameObject three = New("3");
            three.transform.SetParent(two.transform);
            GameObject four = New("4");
            four.transform.SetParent(three.transform);

            // Act
            two.transform.EnableRecursively<CircleCollider2D>(false);
            Assert.IsTrue(one.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(two.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(three.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(four.GetComponent<SpriteRenderer>().enabled);

            Assert.IsTrue(one.GetComponent<CircleCollider2D>().enabled);
            Assert.IsFalse(two.GetComponent<CircleCollider2D>().enabled);
            Assert.IsFalse(three.GetComponent<CircleCollider2D>().enabled);
            Assert.IsFalse(four.GetComponent<CircleCollider2D>().enabled);

            // Act
            three.transform.EnableRecursively<CircleCollider2D>(true);

            Assert.IsTrue(one.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(two.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(three.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(four.GetComponent<SpriteRenderer>().enabled);

            Assert.IsTrue(one.GetComponent<CircleCollider2D>().enabled);
            Assert.IsFalse(two.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(three.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(four.GetComponent<CircleCollider2D>().enabled);

            // Act
            one.transform.EnableRecursively<CircleCollider2D>(true);

            Assert.IsTrue(one.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(two.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(three.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(four.GetComponent<SpriteRenderer>().enabled);

            Assert.IsTrue(one.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(two.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(three.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(four.GetComponent<CircleCollider2D>().enabled);

            // Act
            two.transform.EnableRecursively<CircleCollider2D>(
                false,
                collider => collider.gameObject == three
            );

            Assert.IsTrue(one.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(two.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(three.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(four.GetComponent<SpriteRenderer>().enabled);

            Assert.IsTrue(one.GetComponent<CircleCollider2D>().enabled);
            Assert.IsFalse(two.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(three.GetComponent<CircleCollider2D>().enabled);
            Assert.IsFalse(four.GetComponent<CircleCollider2D>().enabled);

            // Act
            one.transform.EnableRecursively<CircleCollider2D>(
                true,
                collider => collider.gameObject == four
            );

            Assert.IsTrue(one.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(two.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(three.GetComponent<SpriteRenderer>().enabled);
            Assert.IsTrue(four.GetComponent<SpriteRenderer>().enabled);

            Assert.IsTrue(one.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(two.GetComponent<CircleCollider2D>().enabled);
            Assert.IsTrue(three.GetComponent<CircleCollider2D>().enabled);
            Assert.IsFalse(four.GetComponent<CircleCollider2D>().enabled);

            yield break;

            GameObject New(string name)
            {
                return Track(
                    new GameObject(name, typeof(SpriteRenderer), typeof(CircleCollider2D))
                );
            }
        }

        [UnityTest]
        public IEnumerator DestroyAllChildGameObjects()
        {
            GameObject one = Track(new GameObject("1"));
            GameObject two = Track(new GameObject("2"));
            two.transform.SetParent(one.transform);
            GameObject three = Track(new GameObject("3"));
            three.transform.SetParent(two.transform);
            GameObject four = Track(new GameObject("4"));
            four.transform.SetParent(three.transform);

            // Act
            two.DestroyAllChildrenGameObjects();
            yield return null;

            Assert.IsTrue(one != null);
            Assert.IsTrue(two != null);
            Assert.IsTrue(three == null);
            Assert.IsTrue(four == null);

            three = Track(new GameObject("3"));
            three.transform.SetParent(two.transform);
            four = Track(new GameObject("4"));
            four.transform.SetParent(three.transform);

            // Act
            one.DestroyAllChildrenGameObjects();
            yield return null;

            Assert.IsTrue(one != null);
            Assert.IsTrue(two == null);
            Assert.IsTrue(three == null);
            Assert.IsTrue(four == null);
        }

        [UnityTest]
        public IEnumerator DestroyAllComponentsOfType()
        {
            GameObject one = New("1");
            Assert.AreEqual(4, one.GetComponents<ObjectHelperComponent>().Length);

            GameObject two = New("2");
            two.transform.SetParent(one.transform);

            one.DestroyAllComponentsOfType<ObjectHelperComponent>();
            yield return null;

            Assert.AreEqual(0, one.GetComponents<ObjectHelperComponent>().Length);
            Assert.IsTrue(one.GetComponent<SpriteRenderer>() != null);
            Assert.AreEqual(4, two.GetComponents<ObjectHelperComponent>().Length);

            two.DestroyAllComponentsOfType<ObjectHelperComponent>();
            yield return null;

            Assert.AreEqual(0, one.GetComponents<ObjectHelperComponent>().Length);
            Assert.IsTrue(one.GetComponent<SpriteRenderer>() != null);
            Assert.AreEqual(0, two.GetComponents<ObjectHelperComponent>().Length);
            Assert.IsTrue(two.GetComponent<SpriteRenderer>() != null);
            yield break;

            GameObject New(string name)
            {
                return Track(
                    new GameObject(
                        name,
                        typeof(SpriteRenderer),
                        typeof(ObjectHelperComponent),
                        typeof(ObjectHelperComponent),
                        typeof(ObjectHelperComponent),
                        typeof(ObjectHelperComponent)
                    )
                );
            }
        }

        [UnityTest]
        public IEnumerator SmartDestroy()
        {
            GameObject one = Track(new GameObject("1"));

            one.SmartDestroy();
            yield return null;
            Assert.IsTrue(one == null);

            GameObject two = Track(new GameObject("2"));
            two.SmartDestroy(1.5f);
            yield return null;

            Assert.IsTrue(two != null);
            yield return new WaitForSeconds(1.6f);
            Assert.IsTrue(two == null);
        }

        [UnityTest]
        public IEnumerator DestroyAllChildrenGameObjectsImmediatelyConditionally()
        {
            GameObject one = Track(new GameObject("1"));
            GameObject two = Track(new GameObject("2"));
            two.transform.SetParent(one.transform);
            GameObject three = Track(new GameObject("3"));
            three.transform.SetParent(two.transform);
            GameObject four = Track(new GameObject("4"));
            four.transform.SetParent(two.transform);

            two.DestroyAllChildrenGameObjectsImmediatelyConditionally(go => go == four);
            Assert.IsTrue(one != null);
            Assert.IsTrue(two != null);
            Assert.IsTrue(three != null);
            Assert.IsTrue(four == null);

            one.DestroyAllChildrenGameObjectsImmediatelyConditionally(go => go != two);
            Assert.IsTrue(one != null);
            Assert.IsTrue(two != null);
            Assert.IsTrue(three != null);
            Assert.IsTrue(four == null);

            one.DestroyAllChildrenGameObjectsImmediatelyConditionally(go => go == two);
            Assert.IsTrue(one != null);
            Assert.IsTrue(two == null);
            Assert.IsTrue(three == null);
            Assert.IsTrue(four == null);

            yield break;
        }

        [UnityTest]
        public IEnumerator DestroyAllChildGameObjectsConditionally()
        {
            GameObject one = Track(new GameObject("1"));
            GameObject two = Track(new GameObject("2"));
            two.transform.SetParent(one.transform);
            GameObject three = Track(new GameObject("3"));
            three.transform.SetParent(two.transform);
            GameObject four = Track(new GameObject("4"));
            four.transform.SetParent(two.transform);

            two.DestroyAllChildGameObjectsConditionally(go => go == four);
            yield return null;

            Assert.IsTrue(one != null);
            Assert.IsTrue(two != null);
            Assert.IsTrue(three != null);
            Assert.IsTrue(four == null);
            one.DestroyAllChildGameObjectsConditionally(go => go != two);
            yield return null;

            Assert.IsTrue(one != null);
            Assert.IsTrue(two != null);
            Assert.IsTrue(three != null);
            Assert.IsTrue(four == null);

            one.DestroyAllChildGameObjectsConditionally(go => go == two);
            yield return null;

            Assert.IsTrue(one != null);
            Assert.IsTrue(two == null);
            Assert.IsTrue(three == null);
            Assert.IsTrue(four == null);
        }

        [UnityTest]
        public IEnumerator DestroyAllChildrenGameObjectsImmediately()
        {
            GameObject one = Track(new GameObject("1"));
            GameObject two = Track(new GameObject("2"));
            two.transform.SetParent(one.transform);
            GameObject three = Track(new GameObject("3"));
            three.transform.SetParent(two.transform);
            GameObject four = Track(new GameObject("4"));
            four.transform.SetParent(two.transform);

            two.DestroyAllChildrenGameObjectsImmediately();
            Assert.IsTrue(one != null);
            Assert.IsTrue(two != null);
            Assert.IsTrue(three == null);
            Assert.IsTrue(four == null);

            three = Track(new GameObject("3"));
            three.transform.SetParent(two.transform);
            four = Track(new GameObject("4"));
            four.transform.SetParent(two.transform);

            one.DestroyAllChildrenGameObjectsImmediately();
            Assert.IsTrue(one != null);
            Assert.IsTrue(two == null);
            Assert.IsTrue(three == null);
            Assert.IsTrue(four == null);

            yield break;
        }

        [UnityTest]
        public IEnumerator GetGameObject()
        {
            GameObject go = Track(new GameObject("Test", typeof(SpriteRenderer)));
            SpriteRenderer spriteRenderer = go.GetComponent<SpriteRenderer>();

            GameObject result = go.GetGameObject();
            Assert.AreEqual(result, go);
            result = spriteRenderer.GetGameObject();
            Assert.AreEqual(result, go);

            Object.DestroyImmediate(spriteRenderer); // UNH-SUPPRESS: Test verifies behavior after component destruction
            result = spriteRenderer.GetGameObject();
            Assert.IsTrue(result == null);
            result = go.GetGameObject();
            Assert.AreEqual(result, go);

            Object.DestroyImmediate(go); // UNH-SUPPRESS: Test verifies behavior after GameObject destruction
            result = spriteRenderer.GetGameObject();
            Assert.IsTrue(result == null);
            result = go.GetGameObject();
            Assert.IsTrue(result == null);

            result = ((GameObject)null).GetGameObject();
            Assert.IsTrue(result == null);

            result = ((SpriteRenderer)null).GetGameObject();
            Assert.IsTrue(result == null);
            yield break;
        }
    }
}
